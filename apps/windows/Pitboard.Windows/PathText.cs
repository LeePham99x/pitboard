using System.Text;

namespace Pitboard.Windows;

// A path in a sentence wraps as Windows wraps one: after a backslash, which ends the line, and never inside a folder's name.
// U+200B offers each of those breaks, and U+2060 holds together the places DirectWrite would break otherwise: before a
// backslash and after a hyphen. Copying takes both out again.
public static class PathText
{
    private const char Break = '\u200B';
    private const char Join = '\u2060';

    public static string Shown(string? text)
    {
        text ??= "";
        var shown = new StringBuilder(text.Length);
        var start = 0;
        while (start < text.Length)
        {
            var end = start;
            var white = char.IsWhiteSpace(text[start]);
            while (end < text.Length && char.IsWhiteSpace(text[end]) == white)
            {
                end++;
            }

            var word = text[start..end];
            if (white || !word.Contains('\\'))
            {
                shown.Append(word);
            }
            else
            {
                Path(word, shown);
            }

            start = end;
        }

        return shown.ToString();
    }

    public static string Copied(string shown) => shown.Replace(Break.ToString(), "").Replace(Join.ToString(), "");

    private static void Path(string word, StringBuilder shown)
    {
        for (var at = 0; at < word.Length; at++)
        {
            var next = at + 1 < word.Length ? word[at + 1] : (char?)null;
            switch (word[at])
            {
                case '\\':
                    if (at > 0 && word[at - 1] != '\\')
                    {
                        shown.Append(Join);
                    }

                    shown.Append('\\');
                    if (next is { } following && following != '\\')
                    {
                        shown.Append(Break);
                    }

                    break;
                case '-' when next is not null:
                    shown.Append('-').Append(Join);
                    break;
                default:
                    shown.Append(word[at]);
                    break;
            }
        }
    }
}
