namespace O0y0O.Common.ConfigT4;

public class ConfigT4Option(string sectionName, string? sectionKey = default)
{
    public string SectionName => sectionName;

    public string SectionKey => sectionKey ?? sectionName;

    public string[]? IncludePathPatterns { get; set; }

    public string[]? ExcludePathPatterns { get; set; }

    public string[]? ExcludeChildrenPathPatterns { get; set; }
}