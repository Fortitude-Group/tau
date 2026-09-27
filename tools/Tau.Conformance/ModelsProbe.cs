namespace Tau.Conformance;

/// <summary>Best-effort <c>GET /v1/models</c> against Tau, for the report header.</summary>
internal static class ModelsProbe
{
    public static async Task<string> TryFetch(HttpClient http, string baseUrl)
    {
        try
        {
            using var response = await http.GetAsync(new Uri(new Uri(baseUrl), "/v1/models"));
            var body = await response.Content.ReadAsStringAsync();
            return response.IsSuccessStatusCode ? body : $"not reachable (HTTP {(int)response.StatusCode})";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return $"not reachable ({ex.Message})";
        }
    }
}
