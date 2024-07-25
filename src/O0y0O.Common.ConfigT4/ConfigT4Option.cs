namespace O0y0O.Common.ConfigT4;

public class ConfigT4Option(string moduleName)
{
    public string ModuleName
    {
        get => moduleName;
    }

    public string[]? IncludePathPatterns { get; set; }

    public string[]? ExcludePathPatterns { get; set; }

    public string[]? ExcludeChildrenPathPatterns { get; set; }
}