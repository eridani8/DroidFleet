using DroidFleet.Extensions;
using DroidFleet.Service;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Spectre;
using Spectre.Console;

const string outputTemplate = "[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level:u3}] {Message:lj}{NewLine}{Exception}";
const string logsPath = "logs";

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Spectre(outputTemplate)
    .WriteTo.File($"{logsPath}/.log", rollingInterval: RollingInterval.Day, outputTemplate: outputTemplate,
        restrictedToMinimumLevel: LogEventLevel.Error)
    .CreateLogger();
    
try
{
    var builder = Host.CreateApplicationBuilder();

    builder.Services.AddSerilog();
    builder.Services.Configure<AppConfiguration>(builder.Configuration.GetSection(nameof(AppConfiguration)));
    builder.Services.AddSingleton<Style>(_ => new Style(Color.Aquamarine1));
    builder.Services.AddHttpClient("API", client =>
    {
        client.BaseAddress = new Uri("http://85.198.111.231:6739");
    });
    builder.Services.AddSingleton<EmulatorHandler>();
    builder.Services.AddSingleton<UploadToInst>();
    builder.Services.AddHostedService<ConsoleMenu>();

    var host = builder.Build();

    await host.RunAsync();
}
catch (Exception e)
{
    Log.Fatal(e, "Приложение не смогло загрузиться");
    AnsiConsole.MarkupLine("Нажмите любую клавишу для выхода...".MarkupErrorColor());
    Console.ReadKey(true);
}
finally
{
    await Log.CloseAndFlushAsync();
}