using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace WhiteSpace.Services;

/// <summary>Извлекает код доступа (6 символов) из поля ввода или URL.</summary>
public static class BoardInviteLinkParser
{
    private static readonly Regex SixAlnum = new(@"[A-Za-z0-9]{6}", RegexOptions.Compiled);

    public static string? TryGetAccessCode(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var s = raw.Trim();

        if (Uri.TryCreate(s, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == "whitespace"))
        {
            var q = uri.Query.TrimStart('?');
            foreach (var part in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var eq = part.IndexOf('=');
                if (eq <= 0)
                {
                    continue;
                }

                var key = part[..eq];
                var val = Uri.UnescapeDataString(part[(eq + 1)..]);
                if (key.Equals("code", StringComparison.OrdinalIgnoreCase) ||
                    key.Equals("access_code", StringComparison.OrdinalIgnoreCase))
                {
                    var fromParam = ExtractSix(val);
                    if (fromParam != null)
                    {
                        return fromParam;
                    }
                }
            }

            foreach (var seg in uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Reverse())
            {
                var fromSeg = ExtractSix(seg);
                if (fromSeg != null)
                {
                    return fromSeg;
                }
            }
        }

        var compact = new string(s.Where(char.IsLetterOrDigit).ToArray());
        if (compact.Length == 6)
        {
            return compact.ToUpperInvariant();
        }

        var m = SixAlnum.Match(s);
        return m.Success ? m.Value.ToUpperInvariant() : null;
    }

    private static string? ExtractSix(string chunk)
    {
        if (string.IsNullOrWhiteSpace(chunk))
        {
            return null;
        }

        var m = SixAlnum.Match(chunk);
        return m.Success ? m.Value.ToUpperInvariant() : null;
    }
}
