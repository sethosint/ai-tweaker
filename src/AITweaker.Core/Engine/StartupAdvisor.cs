using System.Text.RegularExpressions;

namespace AITweaker.Core.Engine;

/// <summary>
/// Sorts startup entries (Run keys, Startup folder) into "off" — launchers, updaters and tray helpers nobody needs the
/// moment Windows starts — and "keep" — drivers' helpers, audio, mouse/keyboard software, anti-cheat, security.
/// Anything else is the user's call. "keep" always wins over "off".
/// </summary>
public static partial class StartupAdvisor
{
    [GeneratedRegex(@"(?ix)
        securityhealth | windowsdefender | msmpeng | ctfmon
      | realtek | rtkaud | nahimic | waves | maxxaudio | dolby | sonic\s?studio | voicemeeter
      | razer | logi | lghub | steelseries | corsair | icue | hyperx | wooting | glorious | roccat | asus\s?(armoury|aura)
      | synaptics | elan | etdctrl | touchpad | igfx | intel.*graphics | bluetooth
      | vanguard | vgtray | easyanticheat | battleye | faceit
      | afterburner | rtss | rivatuner | throttlestop | fancontrol")]
    private static partial Regex Keep();

    [GeneratedRegex(@"(?ix)
        steam | epicgameslauncher | \beadm\b | eadesktop | ea\s?desktop | origin | battle\.net | ubisoft | \bupc\.exe | goggalaxy | gog\s?galaxy
      | riotclient | rockstar | playnite | xboxpcapp
      | discord | spotify | telegram | skype | ms-?teams | \bteams\b | zoom | viber | whatsapp | yandex
      | onedrive | googledrivefs | dropbox | yandexdisk | megasync | icloud
      | update | updater | jusched | ccleaner | browser\s?assistant | edgeautolaunch | microsoftedgeautolaunch
      | adobe | acrobat | creative\s?cloud | ccxprocess | itunes | bonjour | cortana | com\.squirrel\.")]
    private static partial Regex Off();

    /// <returns>"off", "keep" or null.</returns>
    public static string? Suggest(string name, string command)
    {
        var text = name + " " + command;
        if (Keep().IsMatch(text)) return "keep";
        return Off().IsMatch(text) ? "off" : null;
    }
}
