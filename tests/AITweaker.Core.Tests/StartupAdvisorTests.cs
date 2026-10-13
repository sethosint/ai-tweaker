using AITweaker.Core.Engine;

namespace AITweaker.Core.Tests;

public class StartupAdvisorTests
{
    [Theory]
    [InlineData("Steam", "\"C:\\Program Files (x86)\\Steam\\steam.exe\" -silent", "off")]
    [InlineData("Discord", "C:\\Users\\u\\AppData\\Local\\Discord\\Update.exe --processStart Discord.exe", "off")]
    [InlineData("EpicGamesLauncher", "\"C:\\Program Files (x86)\\Epic Games\\Launcher\\Portal\\Binaries\\Win64\\EpicGamesLauncher.exe\" -silent", "off")]
    [InlineData("MicrosoftEdgeAutoLaunch_0123", "\"C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe\" --no-startup-window", "off")]
    [InlineData("GoogleChromeAutoLaunch_1", "\"C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe\" --no-startup-window", null)]
    [InlineData("SecurityHealth", "%windir%\\system32\\SecurityHealthSystray.exe", "keep")]
    [InlineData("RtkAudUService", "\"C:\\Windows\\System32\\DriverStore\\FileRepository\\realtekservice.inf\\RtkAudUService64.exe\" -background", "keep")]
    [InlineData("LGHUB", "\"C:\\Program Files\\LGHUB\\system_tray\\lghub_system_tray.exe\" --minimized", "keep")]
    [InlineData("Riot Vanguard", "\"C:\\Program Files\\Riot Vanguard\\vgtray.exe\"", "keep")]
    [InlineData("Razer Synapse Update", "C:\\Program Files (x86)\\Razer\\Synapse3\\Updater.exe", "keep")]
    [InlineData("MSI Afterburner", "\"C:\\Program Files (x86)\\MSI Afterburner\\MSIAfterburner.exe\" /s", "keep")]
    [InlineData("Lightshot", "C:\\Program Files (x86)\\Skillbrains\\lightshot\\Lightshot.exe", null)]
    public void SortsStartupEntries(string name, string command, string? expected) =>
        Assert.Equal(expected, StartupAdvisor.Suggest(name, command));
}
