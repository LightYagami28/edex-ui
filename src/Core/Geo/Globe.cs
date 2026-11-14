// SPDX-License-Identifier: GPL-3.0-only
using System.Net.Http.Json;
using System.Text.Json;

namespace Edex.Core.Geo;

public sealed class LandMask
{
    public const int Width = 360, Height = 180;
    private readonly byte[] _bits;

    private LandMask(byte[] bits) => _bits = bits;

    private static readonly Lazy<LandMask> s_default = new(() =>
    {
        using var stream = typeof(LandMask).Assembly.GetManifestResourceStream("Edex.Core.Geo.land-1deg.bin")
            ?? throw new InvalidOperationException("land mask resource missing");
        var bits = new byte[Width * Height / 8];
        stream.ReadExactly(bits);
        return new LandMask(bits);
    });

    public static LandMask Default => s_default.Value;

    public bool IsLand(double latitude, double longitude)
    {
        int row = Math.Clamp((int)Math.Floor(90 - latitude), 0, Height - 1);
        double lon = ((longitude + 180) % 360 + 360) % 360;
        int col = Math.Clamp((int)Math.Floor(lon), 0, Width - 1);
        int i = row * Width + col;
        return (_bits[i >> 3] >> (i & 7) & 1) == 1;
    }

    public IReadOnlyList<(double Latitude, double Longitude)> LandPoints(double step)
    {
        var points = new List<(double, double)>();
        for (double lat = 90 - step / 2; lat > -90; lat -= step)
        {
            double lonStep = step / Math.Max(0.2, Math.Cos(lat * Math.PI / 180));
            for (double lon = -180 + lonStep / 2; lon < 180; lon += lonStep)
            {
                if (IsLand(lat, lon))
                {
                    points.Add((lat, lon));
                }
            }
        }
        return points;
    }
}

public readonly record struct Orthographic(double CenterLatitude, double CenterLongitude)
{
    public (double X, double Y, bool Visible) Project(double latitude, double longitude)
    {
        double phi = latitude * Math.PI / 180, lambda = (longitude - CenterLongitude) * Math.PI / 180;
        double phi0 = CenterLatitude * Math.PI / 180;
        double cosc = Math.Sin(phi0) * Math.Sin(phi) + Math.Cos(phi0) * Math.Cos(phi) * Math.Cos(lambda);
        double x = Math.Cos(phi) * Math.Sin(lambda);
        double y = Math.Cos(phi0) * Math.Sin(phi) - Math.Sin(phi0) * Math.Cos(phi) * Math.Cos(lambda);
        return (x, -y, cosc >= 0);
    }
}

public sealed record GeoLocation(double Latitude, double Longitude, string? City, string? Country, string? Ip, string Source);

public static class GeoLocator
{
    private const double MaxLatitude = 90;
    private const double MaxLongitude = 180;

    public static async Task<GeoLocation?> LocateAsync(HttpClient http, CancellationToken token)
    {
        foreach (var (url, parse) in new (string, Func<JsonElement, GeoLocation?>)[]
                 {
                     ("https://ipwho.is/", ParseIpWhoIs),
                     ("https://ipapi.co/json/", ParseIpApiCo),
                 })
        {
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(token);
                cts.CancelAfter(TimeSpan.FromSeconds(6));
                var json = await http.GetFromJsonAsync<JsonElement>(url, cts.Token).ConfigureAwait(false);
                if (parse(json) is { } location)
                {
                    return location;
                }
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException or NotSupportedException)
            {
                if (token.IsCancellationRequested)
                {
                    return null;
                }
            }
        }
        return null;
    }

    public static GeoLocation? ParseIpWhoIs(JsonElement json)
    {
        if (json.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.False)
        {
            return null;
        }

        return Build(json, "latitude", "longitude", "city", "country", "ip", "ipwho.is");
    }

    public static GeoLocation? ParseIpApiCo(JsonElement json)
    {
        if (json.TryGetProperty("error", out var err) && err.ValueKind == JsonValueKind.True)
        {
            return null;
        }

        return Build(json, "latitude", "longitude", "city", "country_name", "ip", "ipapi.co");
    }

    private static GeoLocation? Build(JsonElement json, string lat, string lon, string city, string country, string ip, string source)
    {
        if (!json.TryGetProperty(lat, out var la) || !json.TryGetProperty(lon, out var lo))
        {
            return null;
        }

        if (la.ValueKind != JsonValueKind.Number || lo.ValueKind != JsonValueKind.Number)
        {
            return null;
        }

        double latitude = la.GetDouble(), longitude = lo.GetDouble();
        if (latitude is < -MaxLatitude or > MaxLatitude || longitude is < -MaxLongitude or > MaxLongitude)
        {
            return null;
        }

        return new GeoLocation(latitude, longitude, Text(json, city), Text(json, country), Text(json, ip), source);
    }

    private static string? Text(JsonElement json, string name) =>
        json.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
