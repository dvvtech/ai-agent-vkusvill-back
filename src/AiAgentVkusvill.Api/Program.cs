using AiAgentVkusvill.Api.AppStart;
using AiAgentVkusvill.Api.AppStart.Extensions;

var builder = WebApplication.CreateBuilder(args);

var startup = new Startup(builder);
startup.Initialize();

var app = builder.Build();

app.ApplyCors();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{    
    app.UseHttpsRedirection();
}

app.UseAuthorization();
app.MapControllers();

app.Run();
