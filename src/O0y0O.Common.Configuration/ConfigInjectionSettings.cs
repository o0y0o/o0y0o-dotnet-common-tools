namespace O0y0O.Common.Configuration;

public class ConfigInjectionSettings
{
    public required string NamespaceName { get; init; }

    public required string ModuleName { get; init; }

    public required string ConsulProtocol { get; init; }

    public required string ConsulEndpoint { get; init; }

    public required string ConsulPort { get; init; }

    public required string ConsulUri { get; init; }

    public string[] Modules;
}