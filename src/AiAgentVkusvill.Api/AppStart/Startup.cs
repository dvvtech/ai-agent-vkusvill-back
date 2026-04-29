using AiAgentVkusvill.Api.AppStart.Extensions;
using AiAgentVkusvill.Api.Configuration;
using AiAgentVkusvill.Api.Services;

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

            _builder.Services.ConfigureCors();

            InitConfigs();
            ConfigureServices();
            
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

        private void ConfigureServices()
        { 
            _builder.Services.AddSingleton<AiAgentService>();
            _builder.Services.AddSingleton<SessionManager>();
            _builder.Services.AddHostedService(sp => sp.GetRequiredService<SessionManager>());
        }
    }
}
