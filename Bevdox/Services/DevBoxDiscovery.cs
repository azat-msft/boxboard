using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Bevdox.Auth;
using Bevdox.Models;

namespace Bevdox.Services;

public sealed class DevBoxDiscovery(AuthService auth, HttpClient? httpClient = null)
{
    private const string ArgEndpoint = "https://management.azure.com/providers/Microsoft.ResourceGraph/resources?api-version=2022-10-01";

    private static readonly HttpClient SharedHttp = new();
    private readonly HttpClient _http = httpClient ?? SharedHttp;

    public async Task<List<DevBoxInstance>> DiscoverAsync(IProgress<string>? progress = null, CancellationToken ct = default)
    {
        progress?.Report("Authenticating...");
        var armToken = await auth.GetArmTokenAsync(ct);

        // Step 1: Query ARG for all DevCenter projects to get their endpoints
        progress?.Report("Querying projects...");
        var projects = await QueryArgAsync(armToken, """
            resources
            | where type =~ 'microsoft.devcenter/projects'
            | project id, projectName=name, devCenterUri=properties.devCenterUri, location
            | order by id asc
            """, ct);

        var projectInfos = new List<(string Name, string DevCenterUri, string Location)>();
        foreach (var row in projects)
        {
            var name = row.GetProperty("projectName").GetString();
            var uri = row.GetProperty("devCenterUri").GetString();
            var location = row.GetProperty("location").GetString() ?? "";
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(uri))
                throw new InvalidDataException("Azure Resource Graph returned a project without a name or DevCenter URI.");
            projectInfos.Add((name, uri.TrimEnd('/'), location));
        }

        // Step 2: Query each project's DevCenter data plane for dev boxes (in parallel, max 4)
        progress?.Report($"Querying 0/{projectInfos.Count} projects...");
        var devCenterToken = await auth.GetDevCenterTokenAsync(ct);

        int completed = 0;
        var allInstances = new List<DevBoxInstance>();
        var lockObj = new object();

        await Parallel.ForEachAsync(projectInfos,
            new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct },
            async (p, token) =>
            {
                var result = await ListDevBoxesForProjectAsync(devCenterToken, p.Name, p.DevCenterUri, p.Location, token);
                lock (lockObj)
                {
                    allInstances.AddRange(result);
                    completed++;
                    progress?.Report($"Querying {completed}/{projectInfos.Count} projects...");
                }
            });

        progress?.Report($"Done — found {allInstances.Count} dev box(es).");
        return allInstances;
    }

    private async Task<List<DevBoxInstance>> ListDevBoxesForProjectAsync(
        string token, string projectName, string devCenterUri, string location, CancellationToken ct)
    {
        try
        {
            var endpoint = new Uri(devCenterUri);
            if (endpoint.Scheme != Uri.UriSchemeHttps)
                throw new InvalidDataException("DevCenter endpoint must use HTTPS.");
            string? url = $"{devCenterUri}/projects/{Uri.EscapeDataString(projectName)}/users/me/devboxes?api-version=2025-02-01";
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var instances = new List<DevBoxInstance>();
            while (url is not null)
            {
                var pageUri = new Uri(endpoint, url);
                if (pageUri.Scheme != Uri.UriSchemeHttps || pageUri.Authority != endpoint.Authority ||
                    !visited.Add(pageUri.AbsoluteUri))
                    throw new InvalidDataException("DevCenter returned an invalid or repeated pagination URL.");
                using var request = new HttpRequestMessage(HttpMethod.Get, pageUri);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                using var response = await _http.SendAsync(request, ct);
                response.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));

                foreach (var box in doc.RootElement.GetProperty("value").EnumerateArray())
                {
                    var name = box.GetProperty("name").GetString();
                    if (string.IsNullOrWhiteSpace(name))
                        throw new InvalidDataException("DevCenter returned a machine without a name.");
                    var uri = box.TryGetProperty("uri", out var u) ? u.GetString() : null;
                    var state = (box.TryGetProperty("powerState", out var ps) ? ps.GetString() : null)
                             ?? (box.TryGetProperty("provisioningState", out var prov) ? prov.GetString() : null)
                             ?? "Unknown";
                    var poolName = box.TryGetProperty("poolName", out var pool) ? pool.GetString() : null;
                    var boxLocation = box.TryGetProperty("location", out var loc) ? loc.GetString() ?? location : location;

                    var uniqueId = !string.IsNullOrEmpty(uri) ? uri
                        : $"{devCenterUri}/projects/{projectName}/users/me/devboxes/{name}";
                    instances.Add(new DevBoxInstance
                    {
                        UniqueId = uniqueId,
                        OriginalName = name,
                        ProjectName = projectName,
                        DevCenterUri = devCenterUri,
                        State = state,
                        Location = boxLocation,
                        PoolName = poolName
                    });
                }
                url = doc.RootElement.TryGetProperty("nextLink", out var next) ? next.GetString() : null;
                if (string.IsNullOrWhiteSpace(url))
                    url = null;
            }
            return instances;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or InvalidOperationException
            or KeyNotFoundException or InvalidDataException or UriFormatException)
        {
            throw new InvalidOperationException($"Discovery failed for project '{projectName}' at '{devCenterUri}'. " +
                $"Check the credential-chain account, tenant and project permissions. {ex.Message}", ex);
        }
    }

    private async Task<List<JsonElement>> QueryArgAsync(string token, string query, CancellationToken ct)
    {
        var results = new List<JsonElement>();
        string? skipToken = null;
        var visited = new HashSet<string>(StringComparer.Ordinal);
        do
        {
            var options = new Dictionary<string, object> { ["resultFormat"] = "objectArray" };
            if (skipToken is not null)
                options["$skipToken"] = skipToken;
            var body = JsonSerializer.Serialize(new { query, options });
            using var request = new HttpRequestMessage(HttpMethod.Post, ArgEndpoint)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var response = await _http.SendAsync(request, ct);
            response.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var data = doc.RootElement.GetProperty("data");
            if (data.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in data.EnumerateArray())
                    results.Add(item.Clone());
            }
            else if (data.TryGetProperty("rows", out var rows))
            {
                var columns = data.GetProperty("columns").EnumerateArray()
                    .Select(c => c.GetProperty("name").GetString()!)
                    .ToList();

                foreach (var row in rows.EnumerateArray())
                {
                    var dict = new Dictionary<string, JsonElement>();
                    for (int i = 0; i < columns.Count; i++)
                        dict[columns[i]] = row[i];

                    var rowJson = JsonSerializer.Serialize(dict);
                    using var rowDoc = JsonDocument.Parse(rowJson);
                    results.Add(rowDoc.RootElement.Clone());
                }
            }
            else
                throw new InvalidDataException("Azure Resource Graph returned an unrecognized results format.");
            skipToken = doc.RootElement.TryGetProperty("$skipToken", out var next) ? next.GetString() : null;
            if (string.IsNullOrWhiteSpace(skipToken))
            {
                skipToken = null;
                if (doc.RootElement.TryGetProperty("resultTruncated", out var truncated) &&
                    string.Equals(truncated.ToString(), "true", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Azure Resource Graph truncated the project list without a continuation token.");
            }
            else if (!visited.Add(skipToken))
                throw new InvalidDataException("Azure Resource Graph returned a repeated pagination token.");
        } while (skipToken is not null);
        return results;
    }
}
