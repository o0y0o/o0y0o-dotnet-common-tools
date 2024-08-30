using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace O0y0O.Common.Configuration;

[Generator]
public class ConfigGenerator : ISourceGenerator
{
    public void Initialize(GeneratorInitializationContext context)
    {
    }

    public void Execute(GeneratorExecutionContext context)
    {
        // 获取 AdditionalFiles 中的 config.json
        var settings = GetConfigInjectionSettings(context, "appsettings.json");

        if (settings == null) return;

        // 继续生成代码逻辑
        var options = new[]
        {
            new ConfigTemplateOption("Global")
                { IncludePathPatterns = ["AppSettings", $"DomainService(:{settings.ModuleName}|$)"] },
            new ConfigTemplateOption(settings.ModuleName)
        };

        // 模拟的 JSON 配置数据，实际上可能来自 JSON 文件

        var moduleConfigs = Task.WhenAll(options.Select(async option =>
        {
            using var httpClient = new HttpClient();
            var responseStream =
                await httpClient.GetStreamAsync($"{settings.ConsulUri}/v1/kv/{option.ModuleName}");
            var response = await JsonNode.ParseAsync(responseStream);

            var base64ModuleConfigString = response!.AsArray()[0]!["Value"]!.ToString();
            using var decodedModuleConfigStream =
                new MemoryStream(Convert.FromBase64String(base64ModuleConfigString));
            var moduleConfig = await JsonNode.ParseAsync(decodedModuleConfigStream);

            return moduleConfig!.AsObject();
        }));

        // 生成代码
        var generatedCode = GenerateConfigCode(settings.NamespaceName, options, moduleConfigs);

        // 添加生成的代码到编译上下文中
        context.AddSource("GeneratedConfig.g.cs", SourceText.From(generatedCode, Encoding.UTF8));
    }

    private static ConfigInjectionSettings? GetConfigInjectionSettings(GeneratorExecutionContext context,
        string fileName)
    {
        var configFile = context.AdditionalFiles.FirstOrDefault(file => file.Path.EndsWith(fileName));

        if (configFile == null) return null;

        // 读取文件内容
        var configText = configFile.GetText(context.CancellationToken);

        if (configText == null) return null;
        var json = JsonNode.Parse(configText.ToString());
        var configNode = json?["Configuration"];
        var consulNode = json?["Consul"];

        return new ConfigInjectionSettings
        {
            NamespaceName = configNode?["NamespaceName"]?.ToString() ?? string.Empty,
            ModuleName = configNode?["ModuleName"]?.ToString() ?? string.Empty,
            ConsulProtocol = consulNode?["Protocol"]?.ToString() ?? string.Empty,
            ConsulEndpoint = consulNode?["IP"]?.ToString() ?? string.Empty,
            ConsulPort = consulNode?["Port"]?.ToString() ?? string.Empty,
            ConsulUri =
                $"{consulNode?["Protocol"]}://{consulNode?["IP"]}:{consulNode?["Port"]}",
            Modules = consulNode?["Module"]?.ToString().Split(',') ?? Array.Empty<string>()
        };
    }

    private string GenerateConfigCode(string namespaceName, ConfigTemplateOption[] options, Task<JsonObject[]> configs)
    {
        var code = new StringBuilder();

        code.AppendLine($$"""
                              // Auto-generated code. Do not modify.
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

        for (var i = 0; i < configs.Result.Length; i++)
        {
            var option = options[i];
            var config = configs.Result[i];
            if (i != 0) code.AppendLine();
            AppendSectionConfigCode(option, code, option.ModuleName, config);
        }

        code.Append("}");

        return code.ToString();
    }

    private void AppendSectionConfigCode(
        ConfigTemplateOption option,
        StringBuilder code,
        string sectionPath,
        JsonObject? sectionValue,
        int indents = 1)
    {
        var className = NormalizeMemberName(sectionPath.Split(':').Last());
        AppendGenericConfigCode(code, sectionPath, className, indents);

        if (sectionValue == null) return;

        code.AppendLine();
        AppendCode(code, indents, $"public static class {className}");
        AppendCode(code, indents, "{");
        indents++;

        foreach (var config in sectionValue)
        {
            var configPath = $"{sectionPath}:{config.Key}";

            if (!(IsMatchAnyPattern(configPath, option.IncludePathPatterns) ?? true)) continue;
            if (IsMatchAnyPattern(configPath, option.ExcludePathPatterns) ?? false) continue;

            AppendConfigCode(option, code, configPath, config.Value, indents);
        }

        indents--;
        AppendCode(code, indents, "}");
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

    private static void AppendCode(StringBuilder code, int indents, string codeToAppend)
    {
        const int spacePerIndent = 4;
        var spaces = new string(' ', indents * spacePerIndent);
        code.AppendLine($"{spaces}{codeToAppend}");
    }

    private void AppendConfigCode(
        ConfigTemplateOption option,
        StringBuilder code,
        string configPath,
        JsonNode? configValue,
        int indents)
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

        if (arrayItemValueKind is JsonValueKind.Array or JsonValueKind.Object or JsonValueKind.Null)
        {
            AppendGenericConfigCode(code, configPath, propertyName, indents);
        }
        else
        {
            var arrayItemType = GetValueType(firstArrayItem!.AsValue());
            AppendCode(code, indents, $"public const string {propertyName}SectionKey = \"{configPath}\";");
            AppendCode(code, indents, $"private static {arrayItemType}[]? {fieldName};");
            AppendCode(code, indents,
                $"public static {arrayItemType}[] {propertyName} => {fieldName} ??= GetSection<{arrayItemType}[]>({propertyName}SectionKey);");
        }
    }

    private static void AppendValueTypeConfigCode(StringBuilder code, string configPath, JsonValue value, int indents)
    {
        var propertyType = GetValueType(value);
        var propertyName = NormalizeMemberName(value.GetPropertyName());
        var fieldName = GetFieldName(propertyName);

        AppendCode(code, indents, $"public const string {propertyName}SectionKey = \"{configPath}\";");
        AppendCode(code, indents, $"private static {propertyType}? {fieldName};");
        AppendCode(code, indents,
            $"public static {propertyType} {propertyName} => {fieldName} ??= GetValue<{propertyType}>({propertyName}SectionKey);");
    }

    private static string NormalizeMemberName(string memberName)
    {
        memberName = memberName.Replace("-", "_").Replace(".", "_");
        if (Regex.IsMatch(memberName, @"^\d")) memberName = "_" + memberName;

        return memberName;
    }

    private static string GetFieldName(string propertyName) =>
        $"_{char.ToLowerInvariant(propertyName[0])}{propertyName.Substring(1)}";

    private static string GetValueType(JsonValue value)
    {
        return value.GetValueKind() switch
        {
            JsonValueKind.True => "bool",
            JsonValueKind.False => "bool",
            JsonValueKind.Number =>
                value.TryGetValue(out int _) ? "int" :
                value.TryGetValue(out long _) ? "long" : "decimal",
            JsonValueKind.String => "string",
            _ => throw new ArgumentException("Unexpected value kind.")
        };
    }
}