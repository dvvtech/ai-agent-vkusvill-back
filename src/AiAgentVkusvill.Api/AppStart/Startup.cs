using AiAgentVkusvill.Api.Configuration;

namespace AiAgentVkusvill.Api.AppStart
{
    public class Startup
    {
        private WebApplicationBuilder _builder;

        public Startup(WebApplicationBuilder builder)
        {
            _builder = builder ?? throw new ArgumentNullException(nameof(builder));
        }

        public void Initialize()
        {
            if (_builder.Environment.IsDevelopment())
            {
                _builder.Services.AddSwaggerGen();
            }
            else
            {
                //_builder.Services.ConfigureCors();
            }

            // Регистрация HttpClientFactory
            _builder.Services.AddHttpClient();

            InitConfigs();
            /*ConfigureClientAPI();
            ConfigureServices();
            ConfigureRateLimiting();

            _builder.Services
                .AddHealthChecks()
                .AddCheck<ProxyHealthCheck>(nameof(ProxyHealthCheck));*/

            _builder.Services.AddControllers();
        }

        private void InitConfigs()
        {
            if (!_builder.Environment.IsDevelopment())
            {
                _builder.Configuration.AddKeyPerFile("/run/secrets", optional: true);
            }

            _builder.Services.Configure<AiConfig>(_builder.Configuration.GetSection(AiConfig.SectionName));
            _builder.Services.Configure<ProxyConfig>(_builder.Configuration.GetSection(ProxyConfig.SectionName));
        }
    }
}
