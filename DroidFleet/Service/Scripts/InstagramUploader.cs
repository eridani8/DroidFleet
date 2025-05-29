using AdvancedSharpAdbClient.DeviceCommands;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Spectre.Console;

namespace DroidFleet.Service.Scripts;

public class InstagramUploader(
    EmulatorHandler emulatorHandler,
    IHostApplicationLifetime lifetime,
    Style style,
    IOptions<AppConfiguration> configuration,
    ILogger<InstagramUploader> logger) : ScryptBase(emulatorHandler, lifetime, style, configuration, logger)
{
    protected override string AppName => "com.instagram.android";
    protected override async Task Handle(DeviceClient device, int height, int width, CancellationTokenSource cts)
    {
        var creationTab = await device.FindElementAsync(
            "//node[@resource-id='com.instagram.android:id/creation_tab']",
            Lifetime.ApplicationStopping
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
            Lifetime.ApplicationStopping
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
            Lifetime.ApplicationStopping
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
            Lifetime.ApplicationStopping
        );
        if (descriptionInput is not null)
        {
            await descriptionInput.ClickAsync(cts.Token);
            await Task.Delay(ConsoleMenu.SmallDelay, cts.Token);
            await descriptionInput.SendTextAsync(Configuration.Value.Description, cts.Token);
            await Task.Delay(ConsoleMenu.SmallDelay, cts.Token);
            await device.ClickBackButtonAsync(Lifetime.ApplicationStopping);
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
                Lifetime.ApplicationStopping
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
            Lifetime.ApplicationStopping
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
    }
}