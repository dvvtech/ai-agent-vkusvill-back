namespace AiAgentVkusvill.Api.AppStart.Extensions
{
    public static class CorsExtensions
    {
        private const string AllowAllPolicy = "AllowAll";
        private const string AllowSpecificOriginPolicy = "AllowSpecificOrigin";

        public static void ConfigureCors(this IServiceCollection services)
        {
            services.AddCors(options =>
            {
                options.AddPolicy(AllowSpecificOriginPolicy,
                    policy =>
                    {
                        policy.AllowAnyOrigin()//WithOrigins("https://vkusvillpodbor.ru")
                                               //.AllowCredentials() // Разрешить куки
                              .AllowAnyHeader()
                              .AllowAnyMethod();
                    });

                options.AddPolicy(AllowAllPolicy, policy =>
                {
                    policy.AllowAnyOrigin()  // Разрешить любой источник
                                             //.AllowCredentials() // Разрешить куки
                          .AllowAnyMethod()  // Разрешить любые HTTP-методы (GET, POST, PUT и т. д.)
                          .AllowAnyHeader(); // Разрешить любые заголовки
                });
            });
        }

        public static void ApplyCors(this WebApplication app)
        {
            if (app.Environment.IsDevelopment())
            {
                app.UseCors(AllowAllPolicy);
            }
            else
            {
                //app.UseCors(AllowAllPolicy);
                app.UseCors(AllowSpecificOriginPolicy);
            }
        }
    }
}
