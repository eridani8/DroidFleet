using System.Diagnostics.CodeAnalysis;

namespace DroidFleet.Service;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
public class AppConfiguration
{
    public string Code { get; set; } = string.Empty;
    public string AdbPath { get; set; } = string.Empty;
    public string DirectoriesPath { get; set; } = string.Empty;
    public string EmulatorPath { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public TimeSpan Timeout { get; set; }
    public List<string> Directories { get; set; } = [];
}