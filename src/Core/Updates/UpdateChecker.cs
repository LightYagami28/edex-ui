// SPDX-License-Identifier: GPL-3.0-only
using System.Net.Http.Headers;
using System.Text.Json;

namespace Edex.Core.Updates;

public sealed record UpdateInfo(string Version, Uri Url);

public static class UpdateChecker
{
    public static bool IsNewer(string current, string latest) =>
        TryParse(current, out var c) && TryParse(latest, out var l) && l > c;

    private static bool TryParse(string value, out Version version)
    {
        var text = value.Trim().TrimStart('v', 'V');
        int cut = text.IndexOfAny(['-', '+']);
        if (cut >= 0)
        {
            text = text[..cut];
        }

        return Version.TryParse(text, out version!);
    }

    public static Task<UpdateInfo?> CheckAsync(HttpClient client, string repository, string currentVersion) =>
        CheckAsync(client, repository, currentVersion, CancellationToken.None);

    public static async Task<UpdateInfo?> CheckAsync(HttpClient client, string repository, string currentVersion, CancellationToken cancel)
    {
        try
        {
            var json = await FetchLatestAsync(client, repository, currentVersion, cancel).ConfigureAwait(false);
            return json is null ? null : Parse(json, currentVersion);
        }
        catch (Exception e) when (e is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    private static async Task<string?> FetchLatestAsync(HttpClient client, string repository, string currentVersion, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{repository}/releases/latest");
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("edex-ui", currentVersion));
        using var response = await client.SendAsync(request, cancel).ConfigureAwait(false);
        return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(cancel).ConfigureAwait(false) : null;
    }

    private static UpdateInfo? Parse(string json, string currentVersion)
    {
        using var doc = JsonDocument.Parse(json);
        var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
        var url = doc.RootElement.GetProperty("html_url").GetString();
        return IsNewer(currentVersion, tag) && Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? new UpdateInfo(tag.TrimStart('v', 'V'), uri)
            : null;
    }
}
