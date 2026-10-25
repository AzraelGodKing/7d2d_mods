using System.Text;

namespace SevenDaysModManager.Core;

/// <summary>
/// Reads Valve's text KeyValues format (what "app_info_print" prints): quoted keys, each followed by a quoted value or
/// a { } block. Keys are case-insensitive. A block is a <see cref="KeyValues"/>, a value a string.
/// </summary>
public sealed class KeyValues : Dictionary<string, object>
{
    public KeyValues() : base(StringComparer.OrdinalIgnoreCase) { }

    public KeyValues? Block(string key) => TryGetValue(key, out var v) ? v as KeyValues : null;
    public string? Value(string key) => TryGetValue(key, out var v) ? v as string : null;

    /// <summary>Parses text that starts with a key and its block (anything before the first quote is skipped).</summary>
    public static KeyValues Parse(string text)
    {
        var tokens = Tokens(text).GetEnumerator();
        var root = new KeyValues();
        ReadInto(root, tokens, topLevel: true);
        return root;
    }

    static void ReadInto(KeyValues into, IEnumerator<string?> tokens, bool topLevel)
    {
        while (tokens.MoveNext())
        {
            var key = tokens.Current;
            if (key == null) { if (!topLevel) return; continue; } // "}" ends this block
            if (key == "{") continue;
            if (!tokens.MoveNext()) { into[key] = ""; return; }
            var next = tokens.Current;
            if (next == "{")
            {
                var child = new KeyValues();
                ReadInto(child, tokens, topLevel: false);
                into[key] = child;
            }
            else if (next == null) { into[key] = ""; if (!topLevel) return; }
            else into[key] = next;
        }
    }

    /// <summary>The tokens: quoted strings (unescaped), "{" for a block start, and null for a block end.</summary>
    static IEnumerable<string?> Tokens(string s)
    {
        var i = 0;
        while (i < s.Length)
        {
            var c = s[i];
            if (c == '"')
            {
                var sb = new StringBuilder();
                i++;
                while (i < s.Length && s[i] != '"')
                {
                    if (s[i] == '\\' && i + 1 < s.Length)
                    {
                        i++;
                        sb.Append(s[i] switch { 'n' => '\n', 't' => '\t', _ => s[i] });
                    }
                    else sb.Append(s[i]);
                    i++;
                }
                i++;
                yield return sb.ToString();
            }
            else if (c == '{') { i++; yield return "{"; }
            else if (c == '}') { i++; yield return null; }
            else i++;
        }
    }
}
