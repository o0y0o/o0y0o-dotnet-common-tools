using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace O0y0O.Common.ConfigT4;

public static class ConfigT4Helper
{
    public static async Task<string> GenerateConfigCodeFromConsul(
        string consulEndpoint,
        string namespaceName,
        ConfigT4Option[] options
    )
    {
        var moduleConfigs = await Task.WhenAll(options.Select(async option =>
        {
            using var httpClient = new HttpClient();
            var responseStream = await httpClient.GetStreamAsync($"{consulEndpoint}/v1/kv/{option.ModuleName}");
            var response = await JsonNode.ParseAsync(responseStream);

            var base64ModuleConfigString = response!.AsArray()[0]!["Value"]!.ToString();
            using var decodedModuleConfigStream = new MemoryStream(Convert.FromBase64String(base64ModuleConfigString));
            var moduleConfig = await JsonNode.ParseAsync(decodedModuleConfigStream);

            return moduleConfig!.AsObject();
        }));

        return GenerateConfigCode(namespaceName, options, moduleConfigs);
    }

    public static async Task<string> GenerateConfigCodeFromJsonFile(
        string jsonFilePath,
        string namespaceName,
        ConfigT4Option[] options
    )
    {
        var jsonFileStream = File.OpenRead(jsonFilePath);
        var config = await JsonNode.ParseAsync(jsonFileStream);
        var moduleConfigs = options.Select(option =>
        {
            var moduleConfig = config?[option.ModuleName];

            if (moduleConfig == null)
                throw new ArgumentException($"Could not found {option.ModuleName} property in {jsonFilePath}");

            return moduleConfig.AsObject();
        }).ToArray();

        return GenerateConfigCode(namespaceName, options, moduleConfigs);
    }

    private static string GenerateConfigCode(string namespaceName, ConfigT4Option[] options, JsonObject[] configs)
    {
        var code = new StringBuilder();

        code.AppendLine($$"""
                          // ReSharper disable All
                          using System;
                          using Microsoft.Extensions.Configuration;

                          namespace {{namespaceName}};

                          public static class Config
                          {
                              private static IConfiguration? _instance;
                              private static IConfiguration Instance =>
                                  _instance ?? throw new InvalidOperationException("Configuration has not been initialized.");
                          
                              public static void Initialize(IConfiguration config)
                              {
                                  _instance = config;
                              }
                          
                              private static T GetValue<T>(string key, T defaultValue = default(T)) =>
                                  Instance.GetValue<T>(key) ?? defaultValue;
                          
                              private static T GetSection<T>(string key, T defaultValue = default(T)) =>
                                  Instance.GetSection(key).Get<T>() ?? defaultValue;

                          """);

        for (var i = 0; i < configs.Length; i++)
        {
            var option = options[i];
            var config = configs[i];
            if (i != 0) code.AppendLine();
            AppendSectionConfigCode(option, code, option.ModuleName, config);
        }

        code.Append("}");

        return code.ToString();
    }

    private static void AppendSectionConfigCode(
        ConfigT4Option option,
        StringBuilder code,
        string sectionPath,
        JsonObject? sectionValue,
        int indents = 1
    )
    {
        var className = NormalizeMemberName(sectionPath.Split(':').Last());
        AppendGenericConfigCode(code, sectionPath, className, indents);

        if (sectionValue == null) return;
        if (IsMatchAnyPattern(sectionPath, option.ExcludeChildrenPathPatterns) ?? false) return;

        code.AppendLine();
        AppendCode(code, indents, $"public static class {className}");
        AppendCode(code, indents, "{");
        indents++;

        var isFirstLine = true;
        foreach (var config in sectionValue)
        {
            var configPath = $"{sectionPath}:{config.Key}";

            if (!(IsMatchAnyPattern(configPath, option.IncludePathPatterns) ?? true)) continue;
            if (IsMatchAnyPattern(configPath, option.ExcludePathPatterns) ?? false) continue;

            if (!isFirstLine) code.AppendLine();
            AppendConfigCode(option, code, configPath, config.Value, indents);

            isFirstLine = false;
        }

        indents--;
        AppendCode(code, indents, "}");
    }

    private static string NormalizeMemberName(string memberName)
    {
        memberName = memberName.Replace("-", "_").Replace(".", "_");
        if (Regex.IsMatch(memberName, @"^\d")) memberName = "_" + memberName;

        return memberName;
    }

    private static string GetFieldName(string propertyName)
    {
        return $"_{char.ToLowerInvariant(propertyName[0])}{propertyName.Substring(1)}";
    }

    private static void AppendCode(StringBuilder code, int indents, string codeToAppend)
    {
        const int spacePerIndent = 4;
        var spaces = new string(' ', indents * spacePerIndent);
        code.AppendLine($"{spaces}{codeToAppend}");
    }

    private static void AppendGenericConfigCode(StringBuilder code, string sectionPath, string className, int indents)
    {
        AppendCode(code, indents, $"public const string {className}SectionKey = \"{sectionPath}\";");
        AppendCode(code, indents, $"public static class {className}<T>");
        AppendCode(code, indents, "{");
        indents++;
        AppendCode(code, indents, "private static T? _value;");
        AppendCode(code, indents, $"public static T Get() => _value ??= GetSection<T>({className}SectionKey);");
        indents--;
        AppendCode(code, indents, "}");
    }

    private static bool? IsMatchAnyPattern(string value, IEnumerable<string>? patterns)
    {
        return patterns?.Any(pattern => new Regex(pattern).IsMatch(value));
    }

    private static void AppendConfigCode(
        ConfigT4Option option,
        StringBuilder code,
        string configPath,
        JsonNode? configValue,
        int indents
    )
    {
        var configValueKind = configValue?.GetValueKind() ?? JsonValueKind.Null;
        switch (configValueKind)
        {
            case JsonValueKind.Array:
                AppendArrayTypeConfigCode(code, configPath, configValue!.AsArray(), indents);

                break;
            case JsonValueKind.Number or
                JsonValueKind.String or
                JsonValueKind.True or
                JsonValueKind.False:
                AppendValueTypeConfigCode(code, configPath, configValue!.AsValue(), indents);

                break;
            default:
                AppendSectionConfigCode(option, code, configPath, configValue?.AsObject(), indents);

                break;
        }
    }

    private static void AppendArrayTypeConfigCode(StringBuilder code, string configPath, JsonArray array, int indents)
    {
        var propertyName = NormalizeMemberName(array.GetPropertyName());
        var fieldName = GetFieldName(propertyName);
        var firstArrayItem = array.FirstOrDefault();
        var arrayItemValueKind = firstArrayItem?.GetValueKind() ?? JsonValueKind.Object;
        switch (arrayItemValueKind)
        {
            case JsonValueKind.Array or JsonValueKind.Object or JsonValueKind.Null:
                AppendGenericConfigCode(code, configPath, propertyName, indents);

                break;
            default:
                var arrayItemType = GetValueType(firstArrayItem!.AsValue());
                AppendCode(code, indents, $"private static {arrayItemType}[]? {fieldName};");
                AppendCode(code, indents,
                    $"public static {arrayItemType}[] {propertyName} => {fieldName} ??= GetSection<{arrayItemType}[]>(\"{configPath}\");");

                break;
        }
    }

    private static void AppendValueTypeConfigCode(StringBuilder code, string configPath, JsonValue value, int indents)
    {
        var propertyType = GetValueType(value);
        var propertyName = NormalizeMemberName(value.GetPropertyName());
        var fieldName = GetFieldName(propertyName);
        AppendCode(code, indents,
            $"private static {propertyType}? {fieldName};");
        AppendCode(code, indents,
            $"public static {propertyType} {propertyName} => {fieldName} ??= GetValue<{propertyType}>(\"{configPath}\");");
    }

    private static string GetValueType(JsonValue value)
    {
        return value.GetValueKind() switch
        {
            JsonValueKind.True or JsonValueKind.False => "bool",
            JsonValueKind.Number =>
                value.TryGetValue(out int _) ? "int" :
                value.TryGetValue(out long _) ? "long" : "decimal",
            JsonValueKind.String => "string",
            _ => throw new ArgumentException("Unexpected value kind.")
        };
    }
}