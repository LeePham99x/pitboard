using System.Globalization;
using System.Runtime.InteropServices;
using System.Xml.Linq;

namespace Pitboard.Windows;

// The feed as WinSparkle 0.9.4 reads it (appcast.cpp, updatechecker.cpp), for the version its
// did-find-update callback leaves out.
public static class UpdateFeed
{
    private static readonly XNamespace Sparkle = "http://www.andymatuschak.org/xml-namespaces/sparkle";

    private enum Kind
    {
        Number,
        Period,
        Text,
    }

    // What each architecture's WinSparkle.dll compiles in.
    public static string Marker(Architecture architecture) =>
        architecture is Architecture.Arm64 ? "windows-arm64" : "windows-x64";

    // The display version of the newest item WinSparkle would offer the machine with this marker.
    public static string? Newest(string feed, string marker)
    {
        (string Version, string Shown)? newest = null;
        foreach (var item in XDocument.Parse(feed).Root?.Element("channel")?.Elements("item") ?? [])
        {
            var enclosures = item.Elements("enclosure").ToList();
            var enclosure = enclosures.FirstOrDefault(found => Os(found) == marker)
                ?? enclosures.FirstOrDefault(found => Os(found) is "" or "windows");
            if (enclosures.Count > 0 && enclosure is null)
            {
                continue;
            }

            var version = Said(item, enclosure, "version");
            if (version is null)
            {
                continue;
            }

            if (newest is null || Compare(version, newest.Value.Version) > 0)
            {
                newest = (version, Said(item, enclosure, "shortVersionString") ?? version);
            }
        }

        return newest?.Shown;
    }

    // Sparkle's comparator as WinSparkle ports it: 1.0.0-beta.1 and 1.0.0+abc both sort below 1.0.0.
    public static int Compare(string a, string b)
    {
        var left = Parts(a);
        var right = Parts(b);
        var common = Math.Min(left.Count, right.Count);
        for (var i = 0; i < common; i++)
        {
            var (one, other) = (KindOf(left[i][0]), KindOf(right[i][0]));
            if (one == other)
            {
                var compared = one switch
                {
                    Kind.Text => Math.Sign(string.CompareOrdinal(left[i], right[i])),
                    Kind.Number => Number(left[i]).CompareTo(Number(right[i])),
                    _ => 0,
                };
                if (compared != 0)
                {
                    return compared;
                }
            }
            else if (one is Kind.Text || other is Kind.Text)
            {
                return one is Kind.Text ? -1 : 1;
            }
            else
            {
                return one is Kind.Number ? 1 : -1;
            }
        }

        if (left.Count == right.Count)
        {
            return 0;
        }

        var longer = left.Count > right.Count ? 1 : -1;
        var next = longer > 0 ? left[common] : right[common];
        return KindOf(next[0]) is Kind.Text ? -longer : longer;
    }

    private static string Os(XElement enclosure) => (string?)enclosure.Attribute(Sparkle + "os") ?? "";

    // An element of the item, or the enclosure's attribute that older feeds carry.
    private static string? Said(XElement item, XElement? enclosure, string name) =>
        ((string?)item.Element(Sparkle + name) ?? (string?)enclosure?.Attribute(Sparkle + name)) is { Length: > 0 } said
            ? said
            : null;

    private static Kind KindOf(char c) => c switch
    {
        '.' => Kind.Period,
        >= '0' and <= '9' => Kind.Number,
        _ => Kind.Text,
    };

    // "1.20rc3" is 1 . 20 rc 3: a period alone, else each run of digits or of anything else.
    private static List<string> Parts(string version)
    {
        var parts = new List<string>();
        for (var start = 0; start < version.Length;)
        {
            var kind = KindOf(version[start]);
            var end = start + 1;
            while (kind is not Kind.Period && end < version.Length && KindOf(version[end]) == kind)
            {
                end++;
            }

            parts.Add(version[start..end]);
            start = end;
        }

        return parts;
    }

    private static long Number(string digits) =>
        long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : long.MaxValue;
}
