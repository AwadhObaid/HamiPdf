using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HamiPdf.Updates;

internal sealed record AvailableUpdate(Version Version, string Notes, Uri Download);
internal static class ReleaseFeed
{
    public const string Repository = "AwadhObaid/HamiPdf-Releases";
    public const string ReleasesUrl = "https://github.com/" + Repository + "/releases";
    private static readonly HttpClient Client = CreateClient();
    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 1_048_576 };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("HamiPdf-UpdateCheck/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }
    public static async Task<AvailableUpdate?> CheckAsync(Version current)
    {
        using var response = await Client.GetAsync("https://api.github.com/repos/" + Repository + "/releases/latest");
        if (response.StatusCode == HttpStatusCode.NotFound) throw new InvalidOperationException("لم يُنشر إصدار عام بعد، أو أن مستودع النشر غير متاح.");
        if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests)
            throw new InvalidOperationException("تعذر الفحص مؤقتًا بسبب حد طلبات GitHub. حاول لاحقًا.");
        response.EnsureSuccessStatusCode();
        return Parse(await response.Content.ReadAsStringAsync(), current);
    }
    public static AvailableUpdate? Parse(string json, Version current)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Regex.IsMatch(tag, @"^v?(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$")) throw new InvalidDataException("رقم الإصدار المنشور غير صالح.");
        if (!Version.TryParse(tag.TrimStart('v'), out var version)) throw new InvalidDataException("رقم الإصدار غير صالح.");
        var normalized = new Version(current.Major, current.Minor, Math.Max(0,current.Build));
        if (version <= normalized) return null;
        string assetName = $"HamiPdf-Setup-{version.ToString(3)}-win-x64.exe";
        string expected = $"https://github.com/{Repository}/releases/download/{tag}/{assetName}";
        var matches = root.GetProperty("assets").EnumerateArray().Where(x => x.GetProperty("name").GetString() == assetName).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("الإصدار الجديد لا يحتوي على مثبّت Windows مكتمل.");
        var asset = matches[0];
        if (asset.GetProperty("state").GetString() != "uploaded" || asset.GetProperty("size").GetInt64() <= 0 || asset.GetProperty("browser_download_url").GetString() != expected)
            throw new InvalidDataException("رابط المثبّت المنشور غير صالح.");
        string notes = root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "";
        if (notes.Length > 24000) notes = notes[..24000];
        return new AvailableUpdate(version, notes, new Uri(expected));
    }
}
