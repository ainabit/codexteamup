using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using CodexTeamUp.Core;

namespace CodexTeamUp.Mcp;

/// <summary>
/// Calls the local CodexTeamUp backend service for MCP tool execution.
/// </summary>
public sealed class ServiceMcpBackendClient
{
    private readonly Uri _serviceUri;
    private readonly HttpClient _httpClient = new();

    public ServiceMcpBackendClient(Uri serviceUri)
    {
        _serviceUri = serviceUri;
    }

    /// <summary>
    /// Calls a backend-hosted tool and returns its JSON result.
    /// </summary>
    public async Task<object> CallToolAsync(string name, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var uri = new Uri(_serviceUri, $"mcp/tools/{Uri.EscapeDataString(name)}");
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(arguments.GetRawText(), Encoding.UTF8, "application/json")
        };
        request.Headers.ConnectionClose = true;

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(body);
        }

        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }
}
