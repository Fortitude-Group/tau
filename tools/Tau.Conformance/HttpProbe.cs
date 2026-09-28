using System.Diagnostics;
using System.Text;

namespace Tau.Conformance;

/// <summary>What actually came back (or didn't) from one target for one fixture.</summary>
internal sealed record TargetProbe(
    string TargetName,
    int? Status,
    double LatencyMs,
    string? RawBody,
    string? TransportError);

/// <summary>Sends a fixture's wire body to a target's <c>/v1/systemone</c> and times the round trip.</summary>
internal static class HttpProbe
{
    public static async Task<TargetProbe> Send(HttpClient http, string targetName, string baseUrl, string wireBody, string? apiKey = null)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(baseUrl), "/v1/systemone"))
            {
                Content = new StringContent(wireBody, Encoding.UTF8, "application/json"),
            };
            if (apiKey is not null)
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
            }

            using var response = await http.SendAsync(request);
            var body = await response.Content.ReadAsStringAsync();
            stopwatch.Stop();
            return new TargetProbe(targetName, (int)response.StatusCode, stopwatch.Elapsed.TotalMilliseconds, body, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or TimeoutException)
        {
            stopwatch.Stop();
            return new TargetProbe(targetName, null, stopwatch.Elapsed.TotalMilliseconds, null, ex.Message);
        }
    }
}
