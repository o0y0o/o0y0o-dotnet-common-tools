namespace O0y0O.Common.Configuration;

public class ConfigTemplateOption(string moduleName)
{
    public string ModuleName
    {
        get => moduleName;
    }

    public string[]? IncludePathPatterns { get; set; }

    public string[]? ExcludePathPatterns { get; set; }

    public string[]? ExcludeChildrenPathPatterns { get; set; }
}