using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace OpenCommonwealth.Launcher;

internal static class EngineReleases
{
    const string Api = "https://api.github.com";

    public const string Project = "G-A-R-D-E-N/godot";

    internal sealed record Release(string Tag, IReadOnlyDictionary<string, string> Assets);

    public static Release? Latest(string project, out string? error)
    {
        error = null;
        try
        {
            using var http = Client();
            using var response = Get(http, $"{Api}/repos/{project}/releases/latest", HttpCompletionOption.ResponseContentRead);
            if (response.StatusCode == HttpStatusCode.NotFound) { error = "no engine release published yet"; return null; }
            if (!response.IsSuccessStatusCode) { error = Describe(response.StatusCode); return null; }

            using var json = JsonDocument.Parse(response.Content.ReadAsStringAsync().GetAwaiter().GetResult());
            string tag = json.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            var assets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (json.RootElement.TryGetProperty("assets", out var a))
                foreach (var asset in a.EnumerateArray())
                {
                    string? name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                    string? href = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                    if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(href)) assets[name] = href;
                }
            if (tag.Length == 0 || assets.Count == 0) { error = "the latest release has no assets"; return null; }
            return new Release(tag, assets);
        }
        catch (Exception ex) { error = ex.Message; return null; }
    }

    // Returns null on success; the zip must match its .sha256 when the release carries one.
    public static string? Download(Release release, string asset, string destination)
    {
        if (!release.Assets.TryGetValue(asset, out string? url))
            return $"release {release.Tag} has no {asset} - the CI job for this platform may not have run.";

        try
        {
            using var http = Client();
            string tmp = destination + ".part";
            using (var response = Get(http, url, HttpCompletionOption.ResponseHeadersRead))
            {
                if (!response.IsSuccessStatusCode) return Describe(response.StatusCode);
                using var source = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult();
                using var file = File.Create(tmp);
                source.CopyTo(file);
            }

            if (!release.Assets.TryGetValue(asset + ".sha256", out string? sumUrl))
            {
                File.Delete(tmp);
                return $"release {release.Tag} has no {asset}.sha256, so the download can't be verified.";
            }
            {
                using var sumResponse = Get(http, sumUrl, HttpCompletionOption.ResponseContentRead);
                if (!sumResponse.IsSuccessStatusCode) { File.Delete(tmp); return "couldn't read " + asset + ".sha256: " + Describe(sumResponse.StatusCode); }
                string want = sumResponse.Content.ReadAsStringAsync().GetAwaiter().GetResult().TrimStart('\uFEFF').Split(' ', '\t', '\n', '\r')[0];
                string got;
                using (var stream = File.OpenRead(tmp)) got = Convert.ToHexString(SHA256.HashData(stream));
                if (!string.Equals(want, got, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(tmp);
                    return $"{asset} does not match its .sha256 - download it again.";
                }
            }
            File.Move(tmp, destination, overwrite: true);
            return null;
        }
        catch (Exception ex) { try { File.Delete(destination + ".part"); } catch { } return ex.Message; }
    }

    // Redirects are followed by hand: the sign-in must only reach github.com hosts, never the object storage they redirect to.
    static HttpClient Client()
    {
        var http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(30) };
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GardenDevLauncher", "1"));
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return http;
    }

    // The godot repo is public: a rejected stored sign-in must retry anonymously.
    static HttpResponseMessage Get(HttpClient http, string url, HttpCompletionOption completion)
    {
        string? token = GitService.Token();
        var response = Follow(http, url, token, completion);
        if (token is not null && response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            response.Dispose();
            response = Follow(http, url, null, completion);
        }
        return response;
    }

    static HttpResponseMessage Follow(HttpClient http, string url, string? token, HttpCompletionOption completion)
    {
        var target = new Uri(url);
        for (int hop = 0; ; hop++)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, target);
            if (token is not null && target.Scheme == Uri.UriSchemeHttps && (Eq(target.Host, "api.github.com") || Eq(target.Host, "github.com")))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = http.Send(request, completion);

            if (hop >= 5 || response.Headers.Location is null || !IsRedirect(response.StatusCode)) return response;
            target = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(target, response.Headers.Location);
            response.Dispose();
        }
    }

    static bool IsRedirect(HttpStatusCode code) =>
        code is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
             or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

    static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    static string Describe(HttpStatusCode code) => code switch
    {
        HttpStatusCode.Forbidden => "github refused the request (rate limit, or the stored sign-in is not allowed)",
        HttpStatusCode.NotFound => "not found on github",
        _ => $"github returned {(int)code} {code}",
    };
}
