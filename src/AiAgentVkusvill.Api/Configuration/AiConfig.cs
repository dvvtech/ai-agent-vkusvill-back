namespace AiAgentVkusvill.Api.Configuration
{
    public class AiConfig
    {
        public const string SectionName = "AiSettings";

        public string ApiKey { get; set; }

        public string McpUrl { get; set; }

        public string Model { get; set; }

        public int MaxToolRounds { get; set; }

        public int SessionTimeoutHours { get; set; }
    }
}
