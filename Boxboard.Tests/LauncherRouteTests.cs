using System.Net;
using Azure.Core;
using Bevdox.Auth;
using Bevdox.Services;
using Boxboard.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Boxboard.Tests;

[TestClass]
public sealed class LauncherRouteTests
{
    private sealed class Credential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("test-only", DateTimeOffset.UtcNow.AddHours(1));
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }
    private sealed class Handler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json) });
    }

    [TestMethod]
    public async Task WindowsAppRoute_UsesExactCloudPcLink_NotTheAvdResourceLink()
    {
        const string cloudLink = "ms-cloudpc:connect?cpcid=cloud-pc&username=synthetic%40example.invalid&environment=test&version=2&source=DevBox";
        using var client = new HttpClient(new Handler($$"""
            {"rdpConnectionUrl":"ms-avd:connect?resourceid=different-avd-resource","cloudPcConnectionUrl":"{{cloudLink}}"}
            """));
        var launcher = new DevBoxLauncher(new AuthService(new Credential()), client);

        var cloud = await launcher.GetWindowsAppConnectionUriAsync(DemoData.Machines()[0]);
        var legacy = await launcher.GetConnectionUriAsync(DemoData.Machines()[0]);

        Assert.AreEqual(cloudLink, cloud.OriginalString);
        Assert.AreEqual("ms-cloudpc", cloud.Scheme);
        Assert.AreEqual("ms-avd:connect?resourceid=different-avd-resource", legacy.OriginalString);
    }

    [TestMethod]
    [DataRow("""{"rdpConnectionUrl":"ms-avd:connect?resourceid=legacy"}""")]
    [DataRow("""{"cloudPcConnectionUrl":null,"rdpConnectionUrl":"ms-avd:connect?resourceid=legacy"}""")]
    [DataRow("""{"cloudPcConnectionUrl":"ms-avd:connect?resourceid=legacy"}""")]
    [DataRow("""{"cloudPcConnectionUrl":"https://example.invalid/connection"}""")]
    public async Task WindowsAppRoute_MissingOrWrongRoute_RefusesSilentLegacyFallback(string json)
    {
        using var client = new HttpClient(new Handler(json));
        var launcher = new DevBoxLauncher(new AuthService(new Credential()), client);
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            launcher.GetWindowsAppConnectionUriAsync(DemoData.Machines()[0]));
    }
}
