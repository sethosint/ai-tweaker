using System.Globalization;
using System.Text;
using System.Xml.Linq;

namespace AITweaker.Core.Nvidia;

/// <summary>
/// Reads and writes NVIDIA Profile Inspector <c>.nip</c> files: an XmlSerializer dump of
/// <c>List&lt;Profile&gt;</c> (root <c>ArrayOfProfile</c>), DWORD values as decimal strings, UTF-16.
/// </summary>
public static class NvidiaProfileFile
{
    /// <summary>The driver's global profile; settings here apply to every game without its own value.</summary>
    public const string BaseProfile = "Base Profile";

    public sealed record Setting(uint Id, string Name, uint Value);

    public static string Build(IEnumerable<Setting> settings, string profile = BaseProfile)
    {
        var doc = new XDocument(new XDeclaration("1.0", "utf-16", null),
            new XElement("ArrayOfProfile",
                new XElement("Profile",
                    new XElement("ProfileName", profile),
                    new XElement("Executeables"),
                    new XElement("Settings", settings.Select(s =>
                        new XElement("ProfileSetting",
                            new XElement("SettingNameInfo", s.Name),
                            new XElement("SettingID", s.Id.ToString(CultureInfo.InvariantCulture)),
                            new XElement("SettingValue", s.Value.ToString(CultureInfo.InvariantCulture)),
                            new XElement("ValueType", "Dword")))))));
        return Serialize(doc);
    }

    /// <summary>
    /// The profile as it is now (taken from a Profile Inspector export) with the given settings changed or added.
    /// Importing this gives the same result whether the import merges or rebuilds the profile from the file.
    /// </summary>
    public static string WithSettings(string? exportXml, IReadOnlyCollection<Setting> settings, string profile = BaseProfile)
    {
        var current = exportXml is null ? null : XDocument.Parse(exportXml).Root?.Elements("Profile")
            .FirstOrDefault(e => string.Equals((string?)e.Element("ProfileName"), profile, StringComparison.OrdinalIgnoreCase));
        if (current is null) return Build(settings, profile);
        var p = new XElement(current);
        var list = p.Element("Settings") ?? new XElement("Settings");
        if (list.Parent is null) p.Add(list);
        foreach (var s in settings)
        {
            var id = s.Id.ToString(CultureInfo.InvariantCulture);
            var row = list.Elements("ProfileSetting").FirstOrDefault(e => (string?)e.Element("SettingID") == id);
            if (row is null)
            {
                list.Add(new XElement("ProfileSetting",
                    new XElement("SettingNameInfo", s.Name),
                    new XElement("SettingID", id),
                    new XElement("SettingValue", s.Value.ToString(CultureInfo.InvariantCulture)),
                    new XElement("ValueType", "Dword")));
                continue;
            }
            row.SetElementValue("SettingValue", s.Value.ToString(CultureInfo.InvariantCulture));
            row.SetElementValue("ValueType", "Dword");
        }
        return Serialize(new XDocument(new XDeclaration("1.0", "utf-16", null), new XElement("ArrayOfProfile", p)));
    }

    /// <summary>Writes a file the way Profile Inspector expects it: UTF-16 with a byte order mark.</summary>
    public static void Save(string path, string xml) => File.WriteAllText(path, xml, Encoding.Unicode);

    private static string Serialize(XDocument doc)
    {
        var sb = new StringBuilder();
        using (var w = new Utf16Writer(sb)) doc.Save(w);
        return sb.ToString();
    }

    /// <summary>DWORD settings of one profile in an exported file (empty when the profile isn't in it, i.e. not customized).</summary>
    public static Dictionary<uint, uint> ReadDwords(string xml, string profile = BaseProfile)
    {
        var result = new Dictionary<uint, uint>();
        var doc = XDocument.Parse(xml);
        var p = doc.Root?.Elements("Profile").FirstOrDefault(e => string.Equals((string?)e.Element("ProfileName"), profile, StringComparison.OrdinalIgnoreCase));
        if (p is null) return result;
        foreach (var s in p.Element("Settings")?.Elements("ProfileSetting") ?? [])
        {
            var type = (string?)s.Element("ValueType") ?? "Dword";
            if (!type.Equals("Dword", StringComparison.OrdinalIgnoreCase)) continue;
            if (uint.TryParse((string?)s.Element("SettingID"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)
                && uint.TryParse((string?)s.Element("SettingValue"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                result[id] = v;
        }
        return result;
    }

    private sealed class Utf16Writer(StringBuilder sb) : StringWriter(sb, CultureInfo.InvariantCulture)
    {
        public override Encoding Encoding => Encoding.Unicode;
    }
}
