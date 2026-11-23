using LineLanguageLearn.Learning.Api;
using LineLanguageLearn.Learning.Infrastructure;
using LineLanguageLearn.Learning.Tutor.Mcp;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Azure.Functions.Worker.ApplicationInsights;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults(TutorMcpFunctions.Configure)
    .ConfigureServices((context, services) =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
        services.AddLearningServices(context.Configuration);
        services.AddSingleton<ApiPipeline>();
    })
    .Build();

await host.RunAsync();
