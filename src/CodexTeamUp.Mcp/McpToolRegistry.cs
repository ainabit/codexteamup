using System.Text.Json;
using CodexTeamUp.AppServer;
using CodexTeamUp.Controller;
using CodexTeamUp.Core;

namespace CodexTeamUp.Mcp;

/// <summary>
/// Thin MCP registry. Workflow and coordination live in the controller runtime.
/// </summary>
public sealed class McpToolRegistry
{
    private readonly ICtuController _controller;
    private readonly CtuJsonLogger? _logger;

    private McpToolRegistry(ICtuController controller, CtuJsonLogger? logger = null)
    {
        _controller = controller;
        _logger = logger;
    }

    public static IReadOnlyList<string> KnownToolNames => CtuControllerTools.KnownToolNames;

    public IReadOnlyList<string> ToolNames => _controller.ToolNames;

    public async Task<object> InvokeAsync(string name, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        var startedAt = DateTimeOffset.Now;
        _logger?.Info("mcp.tool.start", new { name });
        try
        {
            var result = await _controller.InvokeToolAsync(name, arguments, cancellationToken).ConfigureAwait(false);
            _logger?.Info("mcp.tool.complete", new { name, elapsedMs = (DateTimeOffset.Now - startedAt).TotalMilliseconds });
            return result;
        }
        catch (Exception ex)
        {
            _logger?.Error("mcp.tool.exception", ex, new { name, elapsedMs = (DateTimeOffset.Now - startedAt).TotalMilliseconds });
            throw;
        }
    }

    public static McpToolRegistry CreateDefault(ICtuController controller, CtuJsonLogger? logger = null)
        => new(controller, logger);

    public static McpToolRegistry CreateDefault(
        string busRoot,
        IAppServerClient appServer,
        ReloadableCtuControllerPolicy? controllerPolicy = null,
        CtuJsonLogger? logger = null)
        => new(ReloadableCtuController.CreateDefault(busRoot, appServer, logger, controllerPolicy), logger);

    public static McpToolRegistry CreateServiceBacked(Uri serviceUri)
    {
        var backend = new ServiceMcpBackendClient(serviceUri);
        return new McpToolRegistry(new ServiceBackedController(backend));
    }

    private sealed class ServiceBackedController(ServiceMcpBackendClient backend) : ICtuController
    {
        public IReadOnlyList<string> ToolNames => KnownToolNames;

        public CtuControllerRuntimeStatus Status => new(
            "service",
            GetType().FullName ?? GetType().Name,
            null,
            null,
            DateTimeOffset.Now,
            0,
            null,
            new CtuControllerPolicyStatus(
                "service",
                nameof(CtuControllerPolicy),
                null,
                DateTimeOffset.Now,
                0,
                null,
                CtuControllerPolicy.Default));

        public Task<object> InvokeToolAsync(string name, JsonElement arguments, CancellationToken cancellationToken = default)
            => backend.CallToolAsync(name, arguments, cancellationToken);
    }
}
