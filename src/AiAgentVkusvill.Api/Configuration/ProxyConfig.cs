namespace AiAgentVkusvill.Api.Configuration
{
    public class ProxyConfig
    {
        public const string SectionName = "ProxySettings";
        public bool Enabled { get; init; }
        public string Ip { get; init; } = string.Empty;
        public string Port { get; init; } = string.Empty;
        public string Login { get; init; } = string.Empty;
        public string Password { get; init; } = string.Empty;
    }
}
