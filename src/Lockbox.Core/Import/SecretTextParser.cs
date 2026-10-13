using System.Text.RegularExpressions;

namespace Lockbox.Core.Import;

public sealed record ImportCandidate(string Name, string Value, int LineNumber, bool Suggested);

public sealed class ImportGroup(string? heading)
{
    public string? Heading { get; } = heading;
    public List<ImportCandidate> Items { get; } = [];
}

public sealed class ImportResult
{
    public List<ImportGroup> Groups { get; } = [];
    /// <summary>Lines that didn't look like a heading or a secret.</summary>
    public List<string> Leftovers { get; } = [];
}

/// <summary>
/// Best-effort reader for messy notes. Recognises:
/// <c>KEY=value</c>, <c>Key: value</c>, <c>export KEY=value</c>, <c>$env:KEY = "value"</c>, <c>"key": "value",</c>,
/// short lines as headings ("Netlify", "## acme-web", "[mobile-app]"),
/// a label line followed by a bare token, and bare tokens / connection URLs on their own.
/// </summary>
public static partial class SecretTextParser
{
    private const int MaxHeadingLength = 48;
    private const int MaxKeyLength = 64;
    private const int MaxKeyWords = 6;

    private static readonly string[] LinePrefixes = ["export ", "set ", "$env:", "- ", "* ", "• "];
    private static readonly string[] TokenPrefixes = ["sk_", "pk_", "rk_", "whsec_", "ghp_", "gho_", "ghs_", "github_pat_", "xox", "AKIA", "eyJ", "AIza", "npm_", "nfp_", "pat"];

    public static ImportResult Parse(string text)
    {
        var result = new ImportResult();
        ImportGroup? current = null;
        // A heading that hasn't received any items yet. If a bare token follows, the "heading" was a label.
        string? pendingLabel = null;
        ImportGroup? groupBeforeLabel = null;

        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var raw = lines[i];
            var line = StripPrefixes(raw.Trim());
            if (line.Length == 0 || IsPunctuationOnly(line))
                continue;

            if (TryParsePair(line, out var key, out var value))
            {
                if (value.Length == 0)
                {
                    StartGroup(CleanHeading(key));
                    continue;
                }

                Group().Items.Add(new ImportCandidate(key, value, i + 1, Suggested: !LooksLikeProse(value)));
                pendingLabel = null;
                continue;
            }

            if (LooksLikeToken(line))
            {
                string name;
                if (pendingLabel is not null)
                {
                    // "Stripe secret\nsk_live_..." – the line above was a label, not a project.
                    name = pendingLabel;
                    result.Groups.Remove(current!);
                    current = groupBeforeLabel;
                }
                else
                {
                    name = NameForToken(line);
                }

                Group().Items.Add(new ImportCandidate(name, line, i + 1, Suggested: true));
                pendingLabel = null;
                continue;
            }

            if (IsHeading(line))
            {
                StartGroup(CleanHeading(line));
                continue;
            }

            result.Leftovers.Add(raw.TrimEnd());
            pendingLabel = null;
        }

        result.Groups.RemoveAll(g => g.Items.Count == 0);
        return result;

        ImportGroup Group()
        {
            if (current is null)
            {
                current = new ImportGroup(null);
                result.Groups.Add(current);
            }

            return current;
        }

        void StartGroup(string heading)
        {
            groupBeforeLabel = current;
            current = new ImportGroup(heading);
            result.Groups.Add(current);
            pendingLabel = heading;
        }
    }

    private static string StripPrefixes(string line)
    {
        foreach (var prefix in LinePrefixes)
        {
            if (line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return line[prefix.Length..].TrimStart();
        }

        return line;
    }

    private static bool TryParsePair(string line, out string key, out string value)
    {
        key = value = "";
        var match = PairRegex().Match(line);
        if (!match.Success)
            return false;

        var rawKey = match.Groups["key"].Value.Trim().Trim('"', '\'', '`').Trim();
        var rest = match.Groups["value"].Value.Trim();

        // "https://host" splits at the scheme colon – that's a URL, not a pair.
        if (match.Groups["sep"].Value == ":" && rest.StartsWith("//", StringComparison.Ordinal))
            return false;
        if (rawKey.Length == 0 || rawKey.Length > MaxKeyLength || rawKey.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > MaxKeyWords)
            return false;

        key = rawKey;
        value = Unquote(rest.TrimEnd(',', ';').Trim());
        return true;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2 && (value[0] is '"' or '\'' or '`') && value[^1] == value[0])
            return value[1..^1];
        return value;
    }

    private static bool LooksLikeToken(string line)
    {
        if (line.Contains(' ') || line.Contains('\t'))
            return false;
        if (line.Contains("://", StringComparison.Ordinal))
            return true;
        if (TokenPrefixes.Any(p => line.StartsWith(p, StringComparison.Ordinal)) && line.Length >= 12)
            return true;
        return line.Length >= 16 && line.Any(char.IsLetter) && line.Any(char.IsDigit);
    }

    private static string NameForToken(string token)
    {
        if (Uri.TryCreate(token, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host))
            return $"{uri.Scheme} URL ({uri.Host})";
        return "Unnamed secret";
    }

    private static bool IsHeading(string line) =>
        line.Length <= MaxHeadingLength
        && line.Any(char.IsLetter)
        && CleanHeading(line).Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= MaxKeyWords
        && !line.EndsWith('.');

    private static string CleanHeading(string line) => line.Trim(' ', '\t', '#', '=', '-', '*', '[', ']', ':', '<', '>', '/');

    private static bool IsPunctuationOnly(string line) => !line.Any(char.IsLetterOrDigit);

    private static bool LooksLikeProse(string value) => value.Count(c => c == ' ') >= 3;

    [GeneratedRegex(@"^(?<key>[^=:]+?)\s*(?<sep>=>|=|:)\s*(?<value>.*)$")]
    private static partial Regex PairRegex();
}
