using System.Net;
using System.Text.Json;
using Azure.Core;
using Bevdox.Auth;
using Bevdox.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Boxboard.Tests;

[TestClass]
public sealed class DiscoveryTests
{
    private sealed class TestCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            new("test-only", DateTimeOffset.UtcNow.AddHours(1));
        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handle) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) => handle(request);
    }

    private static HttpResponseMessage Json(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };
    private const string Projects = """{"data":[{"projectName":"p1","devCenterUri":"https://demo.invalid","location":"west"}]}""";

    [TestMethod]
    public async Task Discover_ProjectForbidden_ReportsProjectInsteadOfSuccessfulEmptyList()
    {
        using var client = new HttpClient(new Handler(request => Task.FromResult(
            request.Method == HttpMethod.Post ? Json(Projects) : new HttpResponseMessage(HttpStatusCode.Forbidden))));
        var discovery = new DevBoxDiscovery(new AuthService(new TestCredential()), client);
        var error = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => discovery.DiscoverAsync());
        Assert.Contains("'p1'", error.Message);
        Assert.Contains("403", error.Message);
        Assert.Contains("tenant", error.Message);
    }

    [TestMethod]
    public async Task Discover_PaginatedProjectsAndMachines_ReturnsEveryPage()
    {
        var requests = new List<string>();
        using var client = new HttpClient(new Handler(async request =>
        {
            lock (requests) { requests.Add(request.RequestUri!.AbsoluteUri); }
            if (request.Method == HttpMethod.Post)
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
                return body.RootElement.GetProperty("options").TryGetProperty("$skipToken", out var token)
                    ? token.GetString() == "page2"
                        ? Json("""{"data":[{"projectName":"p2","devCenterUri":"https://demo.invalid","location":"east"}]}""")
                        : throw new AssertFailedException("Unexpected ARG continuation token.")
                    : Json("""{"data":[{"projectName":"p1","devCenterUri":"https://demo.invalid","location":"west"}],"$skipToken":"page2"}""");
            }
            if (request.RequestUri!.AbsolutePath.Contains("/p2/"))
                return Json("""{"value":[{"name":"third","uri":"id3"}]}""");
            return request.RequestUri.Query.Contains("page=2")
                ? Json("""{"value":[{"name":"second","uri":"id2"}]}""")
                : Json("""{"value":[{"name":"first","uri":"id1"}],"nextLink":"https://demo.invalid/projects/p1/users/me/devboxes?page=2"}""");
        }));
        var result = await new DevBoxDiscovery(new AuthService(new TestCredential()), client).DiscoverAsync();
        CollectionAssert.AreEquivalent(new[] { "id1", "id2", "id3" }, result.Select(m => m.UniqueId).ToArray());
        Assert.HasCount(5, requests);
    }

    [TestMethod]
    [DataRow("""{"value":[],"nextLink":"https://other.invalid/page"}""")]
    [DataRow("""{"value":[],"nextLink":"http://demo.invalid/page"}""")]
    [DataRow("""{"wrong":[]}""")]
    [DataRow("""{"value":[{"name":null}]}""")]
    public async Task Discover_InvalidProjectResponse_IsNotSuccess(string response)
    {
        using var client = new HttpClient(new Handler(request => Task.FromResult(
            request.Method == HttpMethod.Post ? Json(Projects) : Json(response))));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            new DevBoxDiscovery(new AuthService(new TestCredential()), client).DiscoverAsync());
    }

    [TestMethod]
    public async Task Discover_RepeatedNextLink_FailsRatherThanLooping()
    {
        int calls = 0;
        using var client = new HttpClient(new Handler(request =>
        {
            calls++;
            return Task.FromResult(request.Method == HttpMethod.Post ? Json(Projects) :
                Json("""{"value":[],"nextLink":"https://demo.invalid/page2"}"""));
        }));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            new DevBoxDiscovery(new AuthService(new TestCredential()), client).DiscoverAsync());
        Assert.AreEqual(3, calls);
    }

    [TestMethod]
    public async Task Discover_TruncatedArgWithoutToken_FailsRatherThanReturningPartialList()
    {
        using var client = new HttpClient(new Handler(_ => Task.FromResult(
            Json("""{"data":[],"resultTruncated":"true"}"""))));
        await Assert.ThrowsExactlyAsync<InvalidDataException>(() =>
            new DevBoxDiscovery(new AuthService(new TestCredential()), client).DiscoverAsync());
    }
}
