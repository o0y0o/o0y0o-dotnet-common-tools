namespace O0y0O.Common.ConfigT4
{
    public class ConfigT4Option
    {
        public readonly string _moduleName;

        public ConfigT4Option(string moduleName)
        {
            _moduleName = moduleName;
        }

        public string ModuleName
        {
            get => _moduleName;
        }

        public string[] IncludePathPatterns { get; set; }

        public string[] ExcludePathPatterns { get; set; }

        public string[] ExcludeChildrenPathPatterns { get; set; }
    }
}