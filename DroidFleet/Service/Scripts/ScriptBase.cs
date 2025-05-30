using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.DeviceCommands;
using AdvancedSharpAdbClient.Models;
using AdvancedSharpAdbClient.Receivers;
using CliWrap;
using DroidFleet.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace DroidFleet.Service.Scripts;

public abstract class ScryptBase(
    EmulatorHandler emulatorHandler,
    IHostApplicationLifetime lifetime,
    Style style,
    IOptions<AppConfiguration> configuration,
    ILogger<ScryptBase> logger)
{
    protected readonly IHostApplicationLifetime Lifetime = lifetime;
    protected readonly IOptions<AppConfiguration> Configuration = configuration;
    protected abstract string AppName { get; }

    public async Task Start()
    {
        while (!Lifetime.ApplicationStopping.IsCancellationRequested)
        {
            foreach (var (i, avd) in emulatorHandler.Avds.Index())
            {
                try
                {
                    await HandleProcess(avd, Configuration.Value.Directories[i]);
                    await Task.Delay(ConsoleMenu.LongDelay);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception e)
                {
                    logger.LogError(e, e.Message);
                }
            }

            await AnsiConsole
                .Status()
                .Spinner(Spinner.Known.Balloon)
                .SpinnerStyle(style)
                .StartAsync(
                    "Таймаут...",
                    async ctx =>
                    {
                        var secondsRemaining = (int)Configuration.Value.Timeout.TotalSeconds;
                        while (secondsRemaining > 0)
                        {
                            ctx.Status($"Возобновление через {secondsRemaining} секунд(ы)...");
                            await Task.Delay(1000, Lifetime.ApplicationStopping);
                            secondsRemaining--;
                        }
                    }
                );

            AnsiConsole.WriteLine();
        }
    }

    private async Task HandleProcess(string avd, string directoryPath)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        try
        {
            if (emulatorHandler.AdbClient is null)
            {
                AnsiConsole.MarkupLine("Нет запущенных эмуляторов".MarkupErrorColor());
                AnsiConsole.MarkupLine(
                    "Нажмите любую клавишу для выхода...".MarkupSecondaryColor()
                );
                Console.ReadKey(true);
                return;
            }

            if (emulatorHandler.Avds.Count == 0)
            {
                AnsiConsole.MarkupLine("Нет загруженных эмуляторов".MarkupErrorColor());
                AnsiConsole.MarkupLine(
                    "Нажмите любую клавишу для выхода...".MarkupSecondaryColor()
                );
                Console.ReadKey(true);
                return;
            }

            const int port = 5554;

            var now = DateTime.Now;

            await emulatorHandler.KillEmulatorProcesses();

            await Task.Delay(ConsoleMenu.MediumDelay, cts.Token);

            AnsiConsole.MarkupLine($"Запуск {avd}");

            var emulatorProcess = Cli.Wrap(Configuration.Value.EmulatorPath)
                .WithArguments($"-avd {avd} -port {port} -no-snapshot-load -no-snapshot-save")
                .WithStandardOutputPipe(
                    PipeTarget.ToFile(Path.Combine(ConsoleMenu.AdbLogsPath, $"output_{now:HH_mm_ss}.log"))
                )
                .WithStandardErrorPipe(
                    PipeTarget.ToFile(Path.Combine(ConsoleMenu.AdbLogsPath, $"error_{now:HH_mm_ss}.log"))
                )
                .ExecuteAsync(cts.Token);

            await Task.Delay(ConsoleMenu.LongDelay * 2, cts.Token);

            await emulatorHandler.AdbClient.ConnectAsync($"127.0.0.1:{port}", cts.Token);

            var continueAt = DateTime.Now.AddMinutes(5);

            AnsiConsole.MarkupLine("Ожидание полной загрузки...".MarkupPrimaryColor());

            DeviceData deviceData = default;
            while (!cts.IsCancellationRequested)
            {
                var devices = await emulatorHandler.AdbClient.GetDevicesAsync(cts.Token);
                deviceData = devices.FirstOrDefault(d => d.State == DeviceState.Online);
                if (!deviceData.IsEmpty)
                {
                    var receiver = new ConsoleOutputReceiver();
                    await emulatorHandler.AdbClient.ExecuteRemoteCommandAsync(
                        "getprop sys.boot_completed",
                        deviceData,
                        receiver,
                        cts.Token
                    );
                    var output = receiver.ToString().Trim();
                    if (output == "1")
                    {
                        break;
                    }

                    AnsiConsole.MarkupLine("*".MarkupPrimaryColor());
                }
                else
                {
                    AnsiConsole.MarkupLine("~".MarkupPrimaryColor());
                }

                if (DateTime.Now >= continueAt)
                {
                    AnsiConsole.MarkupLine(
                        "Таймаут ожидания загрузки устройства".MarkupErrorColor()
                    );

                    return;
                }

                await Task.Delay(ConsoleMenu.LongDelay, cts.Token);
            }

            AnsiConsole.MarkupLine("+".MarkupPrimaryColor());

            await Task.Delay(ConsoleMenu.LongDelay, cts.Token);

            var device = new DeviceClient(emulatorHandler.AdbClient, deviceData);

            AnsiConsole.MarkupLine(
                $"Начало процесса отправки {deviceData.Model}".EscapeMarkup().MarkupPrimaryColor()
            );

            // var dump = await device.DumpScreenAsync();
            // dump?.Save("dump.xml");

            var screen = await emulatorHandler.AdbClient.GetFrameBufferAsync(
                deviceData,
                Lifetime.ApplicationStopping
            );
            var height = (int)screen.Header.Height;
            var width = (int)screen.Header.Width;

            var directory = Directory.GetFiles(directoryPath).ToList();
            if (directory.Count == 0)
            {
                throw new Exception($"Нет файлов в директории: {directoryPath}");
            }

            var file = directory.FirstOrDefault()!;

            using var sync = new SyncService(deviceData);

            AnsiConsole.MarkupLine("Загрузка файла...".MarkupPrimaryColor());

            await using var stream = File.OpenRead(file);
            await sync.PushAsync(
                stream,
                $"{ConsoleMenu.MediaDirectory}/{Guid.CreateVersion7()}.mp4",
                UnixFileStatus.DefaultFileMode,
                DateTimeOffset.Now,
                null,
                Lifetime.ApplicationStopping
            );
            stream.Close();

            await device.UpdateMediaState(ConsoleMenu.MediaDirectory, Lifetime.ApplicationStopping);

            File.Delete(file);

            AnsiConsole.MarkupLine("Успех".MarkupPrimaryColor());

            if (await device.IsAppRunningAsync(AppName, Lifetime.ApplicationStopping))
            {
                await device.StopAppAsync(AppName, cts.Token);
                await Task.Delay(ConsoleMenu.SmallDelay, cts.Token);
            }

            await device.StartAppAsync(AppName, cts.Token);
            await Task.Delay(ConsoleMenu.LongDelay, cts.Token);

            AnsiConsole.MarkupLine("Начало выполнения скрипта...".MarkupPrimaryColor());

            await Handle(device, height, width, cts);

            AnsiConsole.MarkupLine("Завершение выполнения скрипта".MarkupPrimaryColor());

            await Task.Delay(ConsoleMenu.SmallDelay, cts.Token);

            var files = await sync.GetDirectoryListingAsync(ConsoleMenu.MediaDirectory, cts.Token);
            foreach (var fileStatistic in files)
            {
                await device.DeleteFile(
                    $"{ConsoleMenu.MediaDirectory}/{fileStatistic.Path}",
                    Lifetime.ApplicationStopping
                );
            }

            AnsiConsole.MarkupLine("Очистка файлов...".MarkupPrimaryColor());

            await device.UpdateMediaState(ConsoleMenu.MediaDirectory, Lifetime.ApplicationStopping);

            await Task.Delay(ConsoleMenu.SmallDelay, cts.Token);

            AnsiConsole.MarkupLine("Остановка эмулятора...".MarkupPrimaryColor());

            await device.StopAppAsync(AppName, cts.Token);

            try
            {
                await emulatorHandler.AdbClient.DisconnectAsync($"127.0.0.1:{port}", cts.Token);

                var receiver = new ConsoleOutputReceiver();
                try
                {
                    await emulatorHandler.AdbClient.ExecuteRemoteCommandAsync(
                        "emu kill",
                        deviceData,
                        receiver,
                        cts.Token
                    );
                }
                catch (Exception ex)
                {
                    logger.LogWarning($"Expected emu kill error: {ex.Message}");
                }

                await Task.Delay(ConsoleMenu.LongDelay, cts.Token);

                await emulatorHandler.KillEmulatorProcesses();
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Error during emulator shutdown: {ex.Message}");
                await emulatorHandler.KillEmulatorProcesses();
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            logger.LogError(e, "Ошибка в процессе");
        }
        finally
        {
            await cts.CancelAsync();
        }
    }

    protected abstract Task Handle(DeviceClient device, int height, int width, CancellationTokenSource cts);
}