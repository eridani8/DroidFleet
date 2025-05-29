using System.Diagnostics;
using System.Net.Http.Headers;
using DroidFleet.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using Spectre.Console;

namespace DroidFleet.Service;

public class ConsoleMenu(
    IHttpClientFactory clientFactory,
    IOptions<AppConfiguration> configuration,
    Style style,
    EmulatorHandler emulatorHandler,
    UploadToInst uploadToInst,
    IHostApplicationLifetime lifetime) : IHostedService
{
    public const string AdbLogsPath = "adb_logs";
    public const string MediaDirectory = "storage/emulated/0/Pictures";
    
    public static readonly TimeSpan SmallDelay = TimeSpan.FromSeconds(2);
    public static readonly TimeSpan MediumDelay = TimeSpan.FromSeconds(4);
    public static readonly TimeSpan LongDelay = TimeSpan.FromSeconds(8);
    
    private Task? _task;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _task = Worker();
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_task != null)
            {
                await Task.WhenAny(_task, Task.Delay(Timeout.Infinite, cancellationToken));
            }
        }
        finally
        {
            _task?.Dispose();
            lifetime.StopApplication();
        }
    }

    private async Task Worker()
    {
        await EnteringParameters();

        await emulatorHandler.Connect();
        
        const string scripts = "Скрипты";
        const string openDirectory = "Открыть папку с видео";
        const string exit = "Выйти";

        while (!lifetime.ApplicationStopping.IsCancellationRequested)
        {
            var choices = new SelectionPrompt<string>()
                .Title("")
                .HighlightStyle(style)
                .AddChoices(scripts, openDirectory, exit);
            var prompt = AnsiConsole.Prompt(choices);

            try
            {
                switch (prompt)
                {
                    case openDirectory:
                        Process.Start(new ProcessStartInfo(configuration.Value.DirectoriesPath) { UseShellExecute = true });
                        break;
                    case exit:
                        lifetime.StopApplication();
                        break;
                }
            }
            catch (Exception e)
            {
                Log.ForContext<ConsoleMenu>().Error(e, "Ошибка операции");
            }
        }

        // await appHandler.EnteringParameters();
        //
        // await appHandler.Connect();
        //
        // await appHandler.Process();
    }

    #region MyRegion

    private async Task<bool> CheckCode(string code)
    {
        var client = clientFactory.CreateClient("API");
        var request = new HttpRequestMessage
        {
            Method = HttpMethod.Post,
            RequestUri = new Uri($"{client.BaseAddress}Check"),
            Content = new StringContent($"{{\"key\":\"{code}\"}}")
            {
                Headers = { ContentType = new MediaTypeHeaderValue("application/json") },
            },
        };
        using var response = await client.SendAsync(request);
        return response.IsSuccessStatusCode;
    }

    private async Task EnteringParameters()
    {
        Directory.CreateDirectory(AdbLogsPath);

        while (!lifetime.ApplicationStopping.IsCancellationRequested)
        {
            string code;
            if (string.IsNullOrEmpty(configuration.Value.Code))
            {
                code = AnsiConsole.Prompt(
                    new TextPrompt<string>($"{"Введите код".MarkupSecondaryColor()}")
                        .PromptStyle(style)
                        .ValidationErrorMessage("Неверный формат".MarkupErrorColor())
                        .Validate(c => Guid.TryParse(c, out _))
                );
            }
            else
            {
                code = configuration.Value.Code;
            }

            var result = await CheckCode(code);
            if (!result)
            {
                AnsiConsole.MarkupLine("Неверный код".MarkupErrorColor());
                configuration.Value.Code = string.Empty;
            }
            else break;
        }

        if (!string.IsNullOrEmpty(configuration.Value.Code))
        {
            AnsiConsole.MarkupLine("Код загружен из конфигурации".MarkupSecondaryColor());
        }

        while (!lifetime.ApplicationStopping.IsCancellationRequested)
        {
            if (string.IsNullOrEmpty(configuration.Value.AdbPath))
            {
                configuration.Value.AdbPath = await AnsiConsole.PromptAsync(
                    new TextPrompt<string>(
                        "Введите путь к директории с adb.exe".MarkupSecondaryColor()
                    )
                        .PromptStyle(style)
                        .ValidationErrorMessage("Директория не найдена".MarkupErrorColor())
                        .Validate(Path.Exists)
                );
            }

            configuration.Value.AdbPath = Path.Combine(configuration.Value.AdbPath, "adb.exe");

            if (!File.Exists(configuration.Value.AdbPath))
            {
                AnsiConsole.MarkupLine("adb.exe не найден...".MarkupErrorColor());
                configuration.Value.AdbPath = string.Empty;
            }
            else break;
        }

        while (!lifetime.ApplicationStopping.IsCancellationRequested)
        {
            if (string.IsNullOrEmpty(configuration.Value.EmulatorPath))
            {
                configuration.Value.EmulatorPath = await AnsiConsole.PromptAsync(
                    new TextPrompt<string>(
                        "Введите путь к директории эмулятора".MarkupSecondaryColor()
                    )
                        .PromptStyle(style)
                        .ValidationErrorMessage("Директория не найдена".MarkupErrorColor())
                        .Validate(Path.Exists)
                );
            }

            configuration.Value.EmulatorPath = Path.Combine(configuration.Value.EmulatorPath, "emulator.exe");

            if (!File.Exists(configuration.Value.EmulatorPath))
            {
                AnsiConsole.MarkupLine("emulator.exe не найден...".MarkupErrorColor());
                configuration.Value.EmulatorPath = string.Empty;
            }
            else break;
        }

        if (!string.IsNullOrEmpty(configuration.Value.EmulatorPath))
        {
            AnsiConsole.MarkupLine(
                "Путь к эмулятору загружен из конфигурации".MarkupSecondaryColor()
            );
        }

        var directoriesPath = string.Empty;

        while (!lifetime.ApplicationStopping.IsCancellationRequested)
        {
            if (string.IsNullOrEmpty(configuration.Value.DirectoriesPath))
            {
                directoriesPath = await AnsiConsole.PromptAsync(
                    new TextPrompt<string>("Введите путь к списку папок".MarkupSecondaryColor())
                        .PromptStyle(style)
                        .ValidationErrorMessage("Директория не найдена".MarkupErrorColor())
                        .Validate(Directory.Exists)
                );
            }
            else
            {
                directoriesPath = configuration.Value.DirectoriesPath;
            }

            if (!Directory.Exists(directoriesPath))
            {
                configuration.Value.DirectoriesPath = string.Empty;
            }
            else break;
        }

        if (!string.IsNullOrEmpty(configuration.Value.DirectoriesPath))
        {
            AnsiConsole.MarkupLine(
                "Путь к директориям загружен из конфигурации".MarkupSecondaryColor()
            );
        }

        configuration.Value.Directories = Directory.EnumerateDirectories(directoriesPath).ToList();

        AnsiConsole.MarkupLine($"Найдено {configuration.Value.Directories.Count} директорий".MarkupSecondaryColor());

        const string tagsFile = "tags.txt";
        if (!File.Exists(tagsFile))
        {
            configuration.Value.Description = await AnsiConsole.PromptAsync(
                new TextPrompt<string>("Введите описание".MarkupSecondaryColor())
                    .PromptStyle(style)
                    .AllowEmpty()
            );
        }
        else if (
            await File.ReadAllLinesAsync(tagsFile, lifetime.ApplicationStopping) is
            { Length: > 0 } readAllLines
        )
        {
            var randomNumber = Random.Shared.Next(0, readAllLines.Length);
            var randomLine = readAllLines[randomNumber];
            configuration.Value.Description = randomLine;
            AnsiConsole.MarkupLine(
                $"Рандомное описание из tags.txt: {randomLine}".MarkupSecondaryColor()
            );
        }
        else
        {
            configuration.Value.Description = await AnsiConsole.PromptAsync(
                new TextPrompt<string>("Введите описание".MarkupSecondaryColor())
                    .PromptStyle(style)
                    .AllowEmpty()
            );
        }

        var timeoutInMinutes = await AnsiConsole.PromptAsync(
            new TextPrompt<int>("Введите таймаут (мин)".MarkupSecondaryColor())
                .PromptStyle(style)
                .ValidationErrorMessage("Неверный формат".MarkupErrorColor())
                .Validate(t => t >= 0)
        );

        configuration.Value.Timeout = TimeSpan.FromMinutes(timeoutInMinutes);
    }

    #endregion
    
}