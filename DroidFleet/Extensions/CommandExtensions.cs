using AdvancedSharpAdbClient.DeviceCommands;

namespace DroidFleet.Extensions;

public static class CommandExtensions
{
    public static async Task UpdateMediaState(this DeviceClient client, string directory, CancellationToken ct = default)
    {
        await client.AdbClient.ExecuteRemoteCommandAsync(
            $"am broadcast -a android.intent.action.MEDIA_SCANNER_SCAN_FILE -d file:///{directory}",
            client.Device,
            ct);
    }
    
    public static async Task DeleteFile(this DeviceClient client, string path, CancellationToken ct = default)
    {
        await client.AdbClient.ExecuteRemoteCommandAsync(
            $"rm {path}",
            client.Device,
            ct);
    }
}