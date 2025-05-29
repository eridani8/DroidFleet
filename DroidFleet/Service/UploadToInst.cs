﻿using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.DeviceCommands;
using AdvancedSharpAdbClient.Models;
using AdvancedSharpAdbClient.Receivers;
using CliWrap;
using DroidFleet.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace DroidFleet.Service;

public class UploadToInst(
    EmulatorHandler emulatorHandler,
    IHostApplicationLifetime lifetime,
    Style style,
    IOptions<AppConfiguration> configuration,
    ILogger<UploadToInst> logger
)
{
    private const string AppName = "com.instagram.android";

    public async Task Process()
    {
        while (!lifetime.ApplicationStopping.IsCancellationRequested)
        {
            foreach (var (i, avd) in emulatorHandler.Avds.Index())
            {
                try
                {
                    await HandleProcess(avd, configuration.Value.Directories[i]);
                    await Task.Delay(ConsoleMenu.LongDelay);
                }
                catch (OperationCanceledException) { }
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
                        var secondsRemaining = (int)configuration.Value.Timeout.TotalSeconds;
                        while (secondsRemaining > 0)
                        {
                            ctx.Status($"Возобновление через {secondsRemaining} секунд(ы)...");
                            await Task.Delay(1000, lifetime.ApplicationStopping);
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

            var emulatorProcess = Cli.Wrap(configuration.Value.EmulatorPath)
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
                lifetime.ApplicationStopping
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
                lifetime.ApplicationStopping
            );
            stream.Close();

            await device.UpdateMediaState(ConsoleMenu.MediaDirectory, lifetime.ApplicationStopping);

            File.Delete(file);
            
            AnsiConsole.MarkupLine("Загружен".MarkupPrimaryColor());

            if (await device.IsAppRunningAsync(AppName, lifetime.ApplicationStopping))
            {
                await device.StopAppAsync(AppName, cts.Token);
                await Task.Delay(ConsoleMenu.SmallDelay, cts.Token);
            }

            await device.StartAppAsync(AppName, cts.Token);
            await Task.Delay(ConsoleMenu.LongDelay, cts.Token);
            
            AnsiConsole.MarkupLine("Выполнение скрипта...".MarkupPrimaryColor());

            var creationTab = await device.FindElementAsync(
                "//node[@resource-id='com.instagram.android:id/creation_tab']",
                lifetime.ApplicationStopping
            );
            if (creationTab is not null)
            {
                await creationTab.ClickAsync(cts.Token);
                await Task.Delay(ConsoleMenu.SmallDelay, cts.Token);

                var continueVideoEditCloseButton = device.FindElement(
                    "//node[@resource-id='com.instagram.android:id/auxiliary_button']",
                    ConsoleMenu.SmallDelay
                );
                if (continueVideoEditCloseButton is not null)
                {
                    await continueVideoEditCloseButton.ClickAsync(cts.Token);
                    await Task.Delay(ConsoleMenu.SmallDelay, cts.Token);
                }
            }
            else
            {
                throw new Exception("Creation Button Not Found");
            }

            var selectReelsButton = device.FindElement(
                "//node[@resource-id='com.instagram.android:id/cam_dest_clips']",
                ConsoleMenu.SmallDelay
            );
            if (selectReelsButton is not null)
            {
                await selectReelsButton.ClickAsync(cts.Token);
                await Task.Delay(ConsoleMenu.SmallDelay, cts.Token);
            }

            var reelsPopupCloseButton = device.FindElement(
                "//node[@resource-id='com.instagram.android:id/dialog_container']//node[@resource-id='com.instagram.android:id/primary_button' and @text='ОК']",
                ConsoleMenu.SmallDelay
            );
            if (reelsPopupCloseButton is not null)
            {
                await reelsPopupCloseButton.ClickAsync(cts.Token);
                await Task.Delay(ConsoleMenu.SmallDelay, cts.Token);
            }

            var firstVideoInGallery = await device.FindElementAsync(
                "//node[@resource-id='com.instagram.android:id/gallery_recycler_view']/node[@class='android.view.ViewGroup']",
                lifetime.ApplicationStopping
            );
            if (firstVideoInGallery is not null)
            {
                await firstVideoInGallery.ClickAsync(cts.Token);
                await Task.Delay(ConsoleMenu.MediumDelay, cts.Token);

                var stickerDialogCloseButton = device.FindElement(
                    "//node[@resource-id='com.instagram.android:id/auxiliary_button']",
                    ConsoleMenu.SmallDelay
                );
                if (stickerDialogCloseButton is not null)
                {
                    await stickerDialogCloseButton.ClickAsync(cts.Token);
                    await Task.Delay(ConsoleMenu.SmallDelay, cts.Token);
                }
            }
            else
            {
                throw new Exception("FirstVideoInGallery Not Found");
            }

            var nextButton = await device.FindElementAsync(
                "//node[@resource-id='com.instagram.android:id/clips_right_action_button']",
                lifetime.ApplicationStopping
            );
            if (nextButton is not null)
            {
                await nextButton.ClickAsync(cts.Token);
                await Task.Delay(ConsoleMenu.MediumDelay, cts.Token);
            }
            else
            {
                throw new Exception("Next Button Not Found");
            }

            var descriptionInput = await device.FindElementAsync(
                "//node[@resource-id='com.instagram.android:id/caption_input_text_view']",
                lifetime.ApplicationStopping
            );
            if (descriptionInput is not null)
            {
                await descriptionInput.ClickAsync(cts.Token);
                await Task.Delay(ConsoleMenu.SmallDelay, cts.Token);
                await descriptionInput.SendTextAsync(configuration.Value.Description, cts.Token);
                await Task.Delay(ConsoleMenu.SmallDelay, cts.Token);
                await device.ClickBackButtonAsync(lifetime.ApplicationStopping);
                await Task.Delay(ConsoleMenu.SmallDelay, cts.Token);
            }
            else
            {
                throw new Exception("Description Input Not Found");
            }

            var trialPeriodCheckbox = device.FindElement(
                "//node[@resource-id='com.instagram.android:id/title' and @text='Пробный период']",
                ConsoleMenu.SmallDelay
            );
            if (trialPeriodCheckbox is not null)
            {
                if (
                    trialPeriodCheckbox.Attributes != null
                    && trialPeriodCheckbox.Attributes.TryGetValue("checked", out var checkedValue)
                )
                {
                    if (checkedValue == "false")
                    {
                        await trialPeriodCheckbox.ClickAsync(cts.Token);
                        await Task.Delay(ConsoleMenu.SmallDelay, cts.Token);

                        var closeButton = device.FindElement(
                            "//node[@resource-id='com.instagram.android:id/bb_primary_action_container']",
                            ConsoleMenu.SmallDelay
                        );
                        if (closeButton is not null)
                        {
                            await closeButton.ClickAsync(cts.Token);
                            await Task.Delay(ConsoleMenu.SmallDelay, cts.Token);
                        }
                    }
                }
                else
                {
                    throw new Exception("Checkbox Attribute Not Found");
                }
            }
            else
            {
                await device.SwipeAsync(
                    width / 2,
                    Convert.ToInt32(height / 1.3),
                    width / 2,
                    Convert.ToInt32(height / 3.3),
                    300,
                    lifetime.ApplicationStopping
                );

                await Task.Delay(ConsoleMenu.MediumDelay, cts.Token);

                var trialPeriodCheckbox1 = device.FindElement(
                    "//node[@resource-id='com.instagram.android:id/title' and @text='Пробный период']",
                    ConsoleMenu.SmallDelay
                );
                if (trialPeriodCheckbox1 is not null)
                {
                    if (
                        trialPeriodCheckbox1.Attributes != null
                        && trialPeriodCheckbox1.Attributes.TryGetValue(
                            "checked",
                            out var checkedValue
                        )
                    )
                    {
                        if (checkedValue == "false")
                        {
                            await trialPeriodCheckbox1.ClickAsync(cts.Token);
                            await Task.Delay(ConsoleMenu.SmallDelay, cts.Token);

                            var closeButton = device.FindElement(
                                "//node[@resource-id='com.instagram.android:id/bb_primary_action_container']",
                                ConsoleMenu.SmallDelay
                            );
                            if (closeButton is not null)
                            {
                                await closeButton.ClickAsync(cts.Token);
                                await Task.Delay(ConsoleMenu.SmallDelay, cts.Token);
                            }
                        }
                    }
                    else
                    {
                        throw new Exception("Checkbox Attribute Not Found");
                    }
                }
            }

            var shareButton = await device.FindElementAsync(
                "//node[@resource-id='com.instagram.android:id/share_button']",
                lifetime.ApplicationStopping
            );
            if (shareButton is not null)
            {
                await shareButton.ClickAsync(cts.Token);
                await Task.Delay(ConsoleMenu.LongDelay, cts.Token);

                var promoDialogCloseButton = device.FindElement(
                    "//node[@resource-id='com.instagram.android:id/igds_promo_dialog_action_button']",
                    ConsoleMenu.SmallDelay
                );
                if (promoDialogCloseButton is not null)
                {
                    await promoDialogCloseButton.ClickAsync(cts.Token);
                    await Task.Delay(ConsoleMenu.SmallDelay, cts.Token);
                }

                var sharePopupCloseButton = device.FindElement(
                    "//node[@resource-id='com.instagram.android:id/clips_nux_sheet_share_button']",
                    ConsoleMenu.SmallDelay
                );
                if (sharePopupCloseButton is not null)
                {
                    await sharePopupCloseButton.ClickAsync(cts.Token);
                    await Task.Delay(ConsoleMenu.SmallDelay, cts.Token);
                }
            }

            await Task.Delay(ConsoleMenu.MediumDelay, cts.Token);

            while (
                device.FindElement(
                    "//node[@resource-id='com.instagram.android:id/upload_progress_bar_container']",
                    ConsoleMenu.SmallDelay
                )
                    is not null
            )
            {
                await Task.Delay(ConsoleMenu.SmallDelay, cts.Token);
            }

            AnsiConsole.MarkupLine(
                $"Успешная публикация {deviceData.Model}".EscapeMarkup().MarkupPrimaryColor()
            );

            await Task.Delay(ConsoleMenu.SmallDelay, cts.Token);

            var files = await sync.GetDirectoryListingAsync(ConsoleMenu.MediaDirectory, cts.Token);
            foreach (var fileStatistic in files)
            {
                await device.DeleteFile(
                    $"{ConsoleMenu.MediaDirectory}/{fileStatistic.Path}",
                    lifetime.ApplicationStopping
                );
            }
            
            AnsiConsole.MarkupLine("Очистка файлов...".MarkupPrimaryColor());

            await device.UpdateMediaState(ConsoleMenu.MediaDirectory, lifetime.ApplicationStopping);

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
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            logger.LogError(e, "Ошибка в процессе");
        }
        finally
        {
            await cts.CancelAsync();
        }
    }
}