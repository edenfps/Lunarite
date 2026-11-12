using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace Lunarite.Network;

// Plain-HTTP copy of the website's launcher news page, for Windows XP and Vista. Their Internet Explorer can't open
// the site (its host needs TLS 1.2) and the launcher panel shows "Navigation to the webpage was canceled", so the
// launcher sends those systems to the patch server's /launcher.html instead (the Lunarite launcher). The page and
// its img/ files are fetched from HUXLEY_LAUNCHER_PAGE (default the live site) and kept for ten minutes; links to
// other site pages become absolute so they still open the site in the player's browser.
public sealed partial class LauncherPageMirror
{
    public const string DefaultPage = "https://huxleythedystopia.com/launcher.html";
    private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(10);

    private readonly Uri _page;
    private readonly HttpClient _http;
    private readonly object _lock = new();
    private readonly Dictionary<string, (byte[] Body, string Type, DateTime At)> _cache = new(StringComparer.OrdinalIgnoreCase);

    public LauncherPageMirror(string? pageUrl, HttpClient? http = null)
    {
        _page = new Uri(string.IsNullOrWhiteSpace(pageUrl) ? DefaultPage : pageUrl);
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    // The launcher page itself, rewritten; null when the site can't be reached and nothing is cached.
    public byte[]? Page() => Get("launcher.html", () =>
    {
        string html = _http.GetStringAsync(_page).GetAwaiter().GetResult();
        return (Encoding.UTF8.GetBytes(Rewrite(html, _page)), "text/html; charset=utf-8");
    })?.Body;

    // A file under the page's img/ folder (name only, no paths).
    public (byte[] Body, string Type)? Image(string name)
    {
        if (!SafeName().IsMatch(name)) return null;
        var hit = Get("img/" + name, () =>
        {
            var response = _http.GetAsync(new Uri(_page, "img/" + name)).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            string type = response.Content.Headers.ContentType?.MediaType ?? "application/octet-stream";
            return (response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult(), type);
        });
        return hit is { } h ? (h.Body, h.Type) : null;
    }

    private (byte[] Body, string Type, DateTime At)? Get(string key, Func<(byte[] Body, string Type)> fetch)
    {
        lock (_lock)
        {
            _cache.TryGetValue(key, out var cached);
            if (cached.Body != null && DateTime.UtcNow - cached.At < Fresh) return cached;
            try
            {
                var (body, type) = fetch();
                return _cache[key] = (body, type, DateTime.UtcNow);
            }
            catch (Exception)
            {
                return cached.Body != null ? cached : null; // stale copy beats an error page
            }
        }
    }

    // Relative links to other pages (index.html, ...) point at the site; img/ stays relative so it comes from here.
    public static string Rewrite(string html, Uri page) => RelativeHref().Replace(html, m =>
        m.Groups[1].Value + new Uri(page, m.Groups[2].Value) + m.Groups[3].Value);

    [GeneratedRegex("(href=\")(?![a-z]+:|#|/)([^\"]+)(\")", RegexOptions.IgnoreCase)]
    private static partial Regex RelativeHref();

    [GeneratedRegex(@"^[A-Za-z0-9_.-]+\.(png|jpg|jpeg|gif)$", RegexOptions.IgnoreCase)]
    private static partial Regex SafeName();
}
