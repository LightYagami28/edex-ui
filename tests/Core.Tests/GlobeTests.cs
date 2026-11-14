// SPDX-License-Identifier: GPL-3.0-only
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Edex.Core.Geo;

namespace Edex.Core.Tests;

[TestClass]
public class GlobeTests
{
    [TestMethod]
    [DataRow(41.9, 12.5, true)]
    [DataRow(40.7, -74.5, true)]
    [DataRow(-25, 134, true)]
    [DataRow(-82, 0, true)]
    [DataRow(0, -140, false)]
    [DataRow(30, -40, false)]
    [DataRow(-30, 80, false)]
    public void LandMaskMatchesGeography(double lat, double lon, bool land) =>
        Assert.AreEqual(land, LandMask.Default.IsLand(lat, lon));

    [TestMethod]
    public void LandPointsCoverAboutAThirdOfTheGlobe()
    {
        var points = LandMask.Default.LandPoints(3);
        Assert.IsTrue(points.Count >= 800 && points.Count <= 3000);
        Assert.AreEqual(LandMask.Default.IsLand(0, 190), LandMask.Default.IsLand(0, -170));
    }

    [TestMethod]
    public void OrthographicProjection()
    {
        var view = new Orthographic(0, 0);
        var (x, y, visible) = view.Project(0, 0);
        Assert.IsTrue(visible);
        Assert.AreEqual(0, x, 6);
        Assert.AreEqual(0, y, 6);
        (x, _, visible) = view.Project(0, 90);
        Assert.IsTrue(visible);
        Assert.AreEqual(1, x, 6);
        (_, y, _) = view.Project(90, 0);
        Assert.AreEqual(-1, y, 6);
        Assert.IsFalse(view.Project(0, 180).Visible);
        Assert.IsTrue(new Orthographic(45, 10).Project(45, 10) is { Visible: true, X: < 1e-9 and > -1e-9 });
    }

    [TestMethod]
    public void ParsesKeylessGeolocationResponses()
    {
        var who = GeoLocator.ParseIpWhoIs(JsonElement.Parse("""{"ip":"203.0.113.9","success":true,"latitude":45.07,"longitude":7.69,"city":"Turin","country":"Italy"}"""));
        Assert.AreEqual(new GeoLocation(45.07, 7.69, "Turin", "Italy", "203.0.113.9", "ipwho.is"), who);
        Assert.IsNull(GeoLocator.ParseIpWhoIs(JsonElement.Parse("""{"success":false,"message":"reserved range"}""")));
        var co = GeoLocator.ParseIpApiCo(JsonElement.Parse("""{"ip":"203.0.113.9","city":"Milan","country_name":"Italy","latitude":45.46,"longitude":9.19}"""));
        Assert.AreEqual("Milan", co?.City);
        Assert.IsNull(GeoLocator.ParseIpApiCo(JsonElement.Parse("""{"error":true,"reason":"RateLimited"}""")));
        Assert.IsNull(GeoLocator.ParseIpApiCo(JsonElement.Parse("""{"latitude":"x","longitude":1}""")));
    }
}
