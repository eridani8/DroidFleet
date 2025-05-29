using System.Text;
using AdvancedSharpAdbClient;
using CliWrap;
using DroidFleet.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace DroidFleet.Service;

public class EmulatorHandler(
    IOptions<AppConfiguration> configuration,
    IHostApplicationLifetime lifetime,
    ILogger<EmulatorHandler> logger)
{
    public List<string> Avds { get; set; } = [];
    public IAdbClient? AdbClient { get; set; }

    public async Task Connect()
    {
        var devicesStringBuilder = new StringBuilder();

        await Cli.Wrap(configuration.Value.EmulatorPath)
            .WithArguments("-list-avds")
            .WithStandardOutputPipe(PipeTarget.ToStringBuilder(devicesStringBuilder))
            .ExecuteAsync(lifetime.ApplicationStopping);

        Avds = devicesStringBuilder
            .ToString()
            .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)
            .ToList();
        
        AnsiConsole.MarkupLine($"Доступно {Avds.Count} эмулятор(ов)".MarkupPrimaryColor());

        await KillEmulatorProcesses();

        var server = new AdbServer();
        await server.StartServerAsync(configuration.Value.AdbPath, true, lifetime.ApplicationStopping);

        AdbClient = new AdbClient();

        if (Avds.Count != configuration.Value.Directories.Count)
        {
            AnsiConsole.MarkupLine("Количество эмуляторов и папок не совпадает".MarkupErrorColor());
            AnsiConsole.MarkupLine(
                $"{"Эмуляторов:".MarkupPrimaryColor()} {Avds.Count.ToString().MarkupSecondaryColor()}"
            );
            AnsiConsole.MarkupLine(
                $"{"Папок:".MarkupPrimaryColor()} {configuration.Value.Directories.Count.ToString().MarkupSecondaryColor()}"
            );
        }
    }

    public async Task KillEmulatorProcesses()
    {
        string[] processNames = ["emulator", "qemu-system-x86_64", "crashpad_handler"];
        foreach (var processName in processNames)
        {
            foreach (var emulator in System.Diagnostics.Process.GetProcessesByName(processName))
            {
                try
                {
                    emulator.Kill(true);
                    await emulator.WaitForExitAsync();
                }
                catch (Exception e)
                {
                    logger.LogError(e, "Ошибка снятия процесса");
                }
            }
        }

        await Task.Delay(TimeSpan.FromSeconds(3));
    }
}