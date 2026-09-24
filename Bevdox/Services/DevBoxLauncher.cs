using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Bevdox.Auth;
using Bevdox.Models;

namespace Bevdox.Services;

public sealed class DevBoxLauncher(AuthService auth, HttpClient? httpClient = null)
{
    private static readonly HttpClient SharedHttp = new();
    private readonly HttpClient _http = httpClient ?? SharedHttp;

    public async Task LaunchAsync(DevBoxInstance instance, CancellationToken ct = default)
    {
        var connection = await GetConnectionUriAsync(instance, ct);
        Console.WriteLine($"Launching {instance.EffectiveName}...");
        Process.Start(new ProcessStartInfo(connection.AbsoluteUri) { UseShellExecute = true });
    }

    public Task<Uri> GetConnectionUriAsync(DevBoxInstance instance, CancellationToken ct = default) =>
        GetConnectionUriAsync(instance, "rdpConnectionUrl", ct);

    public Task<Uri> GetWindowsAppConnectionUriAsync(DevBoxInstance instance, CancellationToken ct = default) =>
        GetConnectionUriAsync(instance, "cloudPcConnectionUrl", ct);

    private async Task<Uri> GetConnectionUriAsync(DevBoxInstance instance, string connectionField, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(instance.DevCenterUri))
            throw new InvalidOperationException($"No DevCenter URI available for '{instance.EffectiveName}'.");

        var token = await auth.GetDevCenterTokenAsync(ct);

        // Extract user ID from the resource ID: .../users/{userId}/devboxes/{name}
        var parts = instance.UniqueId.Split('/');
        var userIndex = Array.IndexOf(parts, "users");
        var userId = userIndex >= 0 && userIndex + 1 < parts.Length ? parts[userIndex + 1] : "me";

        var url = $"{instance.DevCenterUri}/projects/{instance.ProjectName}" +
                  $"/users/{userId}/devboxes/{instance.OriginalName}" +
                  "/remoteConnection?api-version=2025-02-01";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var connectionUrl = doc.RootElement.TryGetProperty(connectionField, out var link) ? link.GetString() : null;
        if (string.IsNullOrWhiteSpace(connectionUrl))
            throw new InvalidOperationException(connectionField == "cloudPcConnectionUrl"
                ? "Windows App Cloud PC connection URL is unavailable. The legacy AVD route was not used because it can ignore per-device display preferences."
                : "Remote connection URL not available.");

        var uri = new Uri(connectionUrl, UriKind.Absolute);
        if (connectionField == "cloudPcConnectionUrl" && uri.Scheme != "ms-cloudpc")
            throw new InvalidOperationException("The Cloud PC link does not use the supported Windows App ms-cloudpc route. No client was launched.");
        return uri;
    }
}
