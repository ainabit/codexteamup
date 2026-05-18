using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CodexTeamUp.AgentBus;
using CodexTeamUp.AppServer;
using CodexTeamUp.Controller;
using CodexTeamUp.Core;
using CodexTeamUp.Mcp;
using CodexTeamUp.Service;

var url = ReadOption(args, "--url")
    ?? Environment.GetEnvironmentVariable("CTU_SERVICE_URL")
    ?? "http://127.0.0.1:47319/";
if (!url.EndsWith("/", StringComparison.Ordinal))
{
    url += "/";
}

var serviceUri = new Uri(url);
var port = serviceUri.Port;
var defaultBusRoot = Environment.GetEnvironmentVariable("CTU_AGENTBUS_ROOT")
    ?? Path.Combine(Environment.CurrentDirectory, ".codexteamup", "agentbus");
var pipeName = Environment.GetEnvironmentVariable("CODEX_WRAPPER_PIPE_NAME");
var parentProcessId = ReadIntOption(args, "--parent-pid")
    ?? ReadIntEnvironment("CTU_PARENT_PROCESS_ID");
var logRoot = Environment.GetEnvironmentVariable("CTU_LOG_ROOT")
    ?? Path.Combine(ResolveProjectRootForBus(defaultBusRoot), ".codexteamup", "logs");
var apiLogger = new CtuJsonLogger(Path.Combine(logRoot, $"api-adapter-{DateTimeOffset.Now:yyyyMMdd}.jsonl"));
var controllerLogger = new CtuJsonLogger(Path.Combine(logRoot, $"controller-{DateTimeOffset.Now:yyyyMMdd}.jsonl"));
var appServer = ReloadableAppServerClient.CreateDefault(pipeName, apiLogger);
var controllerPolicy = new ReloadableCtuControllerPolicy(
    Environment.GetEnvironmentVariable("CTU_CONTROLLER_POLICY_PATH"),
    controllerLogger);
var controller = ReloadableCtuController.CreateDefault(defaultBusRoot, appServer, controllerLogger, controllerPolicy);
var registry = McpToolRegistry.CreateDefault(controller, controllerLogger);
var jsonOptions = new JsonSerializerOptions(JsonFile.Options) { WriteIndented = false };
var serviceProjectRoot = ResolveProjectRootForBus(defaultBusRoot);
new KnownGoodRuntimeCheckpointStore(defaultBusRoot).WriteHealthy(
    serviceProjectRoot,
    defaultBusRoot,
    controller.Status.PluginPath,
    controller.Status.ActiveType,
    appServer.Status.PluginPath,
    appServer.Status.ActiveType,
    isVerified: false,
    verificationSource: "service_boot");

using var shutdown = new CancellationTokenSource();
var parentMonitor = parentProcessId is int pid
    ? MonitorParentProcessAsync(pid, shutdown)
    : Task.CompletedTask;
var startupSweepTask = RunControllerStartupSweepLoopAsync(controller, controllerLogger, shutdown.Token);

var listener = new TcpListener(IPAddress.Loopback, port);
listener.Start();

Console.WriteLine($"CodexTeamUp.Service listening on http://127.0.0.1:{port}/");
Console.WriteLine($"Default AgentBus root: {defaultBusRoot}");
Console.WriteLine($"CTU logs: {logRoot}");
if (parentProcessId is int parentPid)
{
    Console.WriteLine($"Parent process monitor: {parentPid}");
}

try
{
    while (!shutdown.IsCancellationRequested)
    {
        try
        {
            var client = await listener.AcceptTcpClientAsync(shutdown.Token).ConfigureAwait(false);
            _ = Task.Run(() => HandleClientAsync(client), shutdown.Token);
        }
        catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
        {
            break;
        }
        catch (ObjectDisposedException) when (shutdown.IsCancellationRequested)
        {
            break;
        }
    }
}
finally
{
    shutdown.Cancel();
    await startupSweepTask.ConfigureAwait(false);
    listener.Stop();
    await parentMonitor.ConfigureAwait(false);
}

static async Task RunControllerStartupSweepAsync(ICtuController controller, CtuJsonLogger? logger, CancellationToken cancellationToken)
{
    var heartbeatDelay = TimeSpan.FromMilliseconds(500);
    try
    {
        await controller.RunStartupSweepAsync(cancellationToken).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
    {
        return;
    }
    catch (Exception ex)
    {
        logger?.Error("service.controller_startup_sweep.failed", ex);
    }

    try
    {
        await Task.Delay(heartbeatDelay, cancellationToken).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
    }
}

static async Task RunControllerStartupSweepLoopAsync(ICtuController controller, CtuJsonLogger? logger, CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        await RunControllerStartupSweepAsync(controller, logger, cancellationToken).ConfigureAwait(false);
    }
}

async Task HandleClientAsync(TcpClient client)
{
    using var _ = client;
    NetworkStream? stream = null;
    try
    {
        stream = client.GetStream();
        var requestLine = await HttpRequestReader.ReadAsciiLineAsync(stream).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(requestLine))
        {
            return;
        }

        var parts = requestLine.Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            await WriteJsonAsync(stream, 400, new { error = "bad request" }).ConfigureAwait(false);
            return;
        }

        var method = parts[0].ToUpperInvariant();
        var target = parts[1];
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? headerLine;
        while (!string.IsNullOrEmpty(headerLine = await HttpRequestReader.ReadAsciiLineAsync(stream).ConfigureAwait(false)))
        {
            var colon = headerLine.IndexOf(':');
            if (colon > 0)
            {
                headers[headerLine[..colon].Trim()] = headerLine[(colon + 1)..].Trim();
            }
        }

        var contentLength = headers.TryGetValue("Content-Length", out var rawLength) && int.TryParse(rawLength, out var parsedLength)
            ? parsedLength
            : 0;
        if (contentLength > 0 &&
            headers.TryGetValue("Expect", out var expect) &&
            expect.Equals("100-continue", StringComparison.OrdinalIgnoreCase))
        {
            await stream.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n")).ConfigureAwait(false);
        }

        var body = await HttpRequestReader.ReadUtf8BodyAsync(stream, contentLength).ConfigureAwait(false);

        await HandleRequestAsync(stream, method, target, body).ConfigureAwait(false);
    }
    catch (Exception ex)
    {
        if (stream is not null)
        {
            try
            {
                await WriteJsonAsync(stream, 500, new { error = SafeText.Redact(ex.Message) }).ConfigureAwait(false);
            }
            catch
            {
            }
        }
    }
}

async Task HandleRequestAsync(NetworkStream stream, string method, string target, string body)
{
    var targetUri = new Uri($"http://127.0.0.1{target}");
    var path = targetUri.AbsolutePath.Trim('/');
    var query = ParseQuery(targetUri.Query);

    if (method == "OPTIONS")
    {
        await WriteRawAsync(stream, 204, "text/plain; charset=utf-8", []).ConfigureAwait(false);
        return;
    }

    if (method == "GET" && path == "health")
    {
        await WriteJsonAsync(stream, 200, new
        {
            status = "ok",
            url = $"http://127.0.0.1:{port}/",
            mcpUrl = $"http://127.0.0.1:{port}/mcp",
            defaultBusRoot,
            logRoot,
            appServerAdapter = appServer.Status,
            controller = controller.Status,
            controllerPolicy = controller.Status.Policy,
            tools = McpToolRegistry.KnownToolNames
        }).ConfigureAwait(false);
        return;
    }

    if (method == "GET" && path == "mcp")
    {
        await WriteJsonAsync(stream, 200, new
        {
            status = "ok",
            protocol = "mcp-json-rpc",
            endpoint = $"http://127.0.0.1:{port}/mcp"
        }).ConfigureAwait(false);
        return;
    }

    if (method == "GET" && (path.Length == 0 || path == "dashboard"))
    {
        var bus = new AgentBusStore(query.TryGetValue("busRoot", out var busRoot) ? busRoot : defaultBusRoot);
        await WriteHtmlAsync(stream, AgentBusDashboard.Render(bus)).ConfigureAwait(false);
        return;
    }

    if (method == "GET" && path == "api/agents")
    {
        var bus = new AgentBusStore(query.TryGetValue("busRoot", out var busRoot) ? busRoot : defaultBusRoot);
        await WriteJsonAsync(stream, 200, new { agents = bus.ListAgents() }).ConfigureAwait(false);
        return;
    }

    if (method == "GET" && path == "api/snapshot")
    {
        var bus = new AgentBusStore(query.TryGetValue("busRoot", out var busRoot) ? busRoot : defaultBusRoot);
        await WriteJsonAsync(stream, 200, AgentBusDashboard.CreateSnapshot(bus)).ConfigureAwait(false);
        return;
    }

    if (method == "GET" && path == "api/tasks")
    {
        var bus = new AgentBusStore(query.TryGetValue("busRoot", out var busRoot) ? busRoot : defaultBusRoot);
        await WriteJsonAsync(stream, 200, new { tasks = bus.ListTasks() }).ConfigureAwait(false);
        return;
    }

    if (method == "GET" && path == "api/results")
    {
        var bus = new AgentBusStore(query.TryGetValue("busRoot", out var busRoot) ? busRoot : defaultBusRoot);
        await WriteJsonAsync(stream, 200, new { results = bus.ListResults() }).ConfigureAwait(false);
        return;
    }

    if (method == "GET" && path == "api/events")
    {
        var bus = new AgentBusStore(query.TryGetValue("busRoot", out var busRoot) ? busRoot : defaultBusRoot);
        await WriteJsonAsync(stream, 200, new { events = bus.ListEvents(500) }).ConfigureAwait(false);
        return;
    }

    if (method == "GET" && path == "api/appserver-adapter")
    {
        await WriteJsonAsync(stream, 200, appServer.Status).ConfigureAwait(false);
        return;
    }

    if (method == "GET" && path == "api/controller-policy")
    {
        await WriteJsonAsync(stream, 200, controller.Status.Policy).ConfigureAwait(false);
        return;
    }

    if (method == "POST" && path == "api/controller-policy/reload")
    {
        var request = string.IsNullOrWhiteSpace(body)
            ? JsonSerializer.Deserialize<JsonElement>("{}")
            : JsonSerializer.Deserialize<JsonElement>(body, JsonFile.Options);
        await WriteJsonAsync(stream, 200, controller.ReloadPolicy(JsonString(request, "policyPath") ?? JsonString(request, "path"))).ConfigureAwait(false);
        return;
    }

    if (method == "GET" && path == "api/controller")
    {
        await WriteJsonAsync(stream, 200, controller.Status).ConfigureAwait(false);
        return;
    }

    if (method == "POST" && path == "api/controller/reload")
    {
        var request = string.IsNullOrWhiteSpace(body)
            ? JsonSerializer.Deserialize<JsonElement>("{}")
            : JsonSerializer.Deserialize<JsonElement>(body, JsonFile.Options);
        var policyPath = JsonString(request, "policyPath");
        if (!string.IsNullOrWhiteSpace(policyPath))
        {
            controller.ReloadPolicy(policyPath);
        }

        await WriteJsonAsync(stream, 200, controller.Reload(
            JsonString(request, "pluginPath") ?? JsonString(request, "path"),
            JsonString(request, "pluginType") ?? JsonString(request, "type"),
            JsonStringDictionary(request, "options"))).ConfigureAwait(false);
        return;
    }

    if (method == "POST" && path == "api/appserver-adapter/reload")
    {
        var request = string.IsNullOrWhiteSpace(body)
            ? JsonSerializer.Deserialize<JsonElement>("{}")
            : JsonSerializer.Deserialize<JsonElement>(body, JsonFile.Options);
        var status = appServer.Reload(
            JsonString(request, "pluginPath") ?? JsonString(request, "path"),
            JsonString(request, "pluginType") ?? JsonString(request, "type"),
            JsonStringDictionary(request, "options"));
        await WriteJsonAsync(stream, 200, status).ConfigureAwait(false);
        return;
    }

    if (method == "POST" && path.StartsWith("mcp/tools/", StringComparison.OrdinalIgnoreCase))
    {
        var tool = Uri.UnescapeDataString(path["mcp/tools/".Length..]);
        var arguments = string.IsNullOrWhiteSpace(body)
            ? JsonSerializer.Deserialize<JsonElement>("{}")
            : JsonSerializer.Deserialize<JsonElement>(body, JsonFile.Options);
        var result = await registry.InvokeAsync(tool, arguments).ConfigureAwait(false);
        await WriteJsonAsync(stream, 200, result).ConfigureAwait(false);
        return;
    }

    if (method == "POST" && path == "mcp")
    {
        var response = await HandleMcpJsonRpcAsync(body).ConfigureAwait(false);
        if (response is null)
        {
            await WriteRawAsync(stream, 202, "application/json; charset=utf-8", []).ConfigureAwait(false);
        }
        else
        {
            await WriteJsonAsync(stream, 200, response).ConfigureAwait(false);
        }

        return;
    }

    await WriteJsonAsync(stream, 404, new { error = "not found", path }).ConfigureAwait(false);
}

async Task<object?> HandleMcpJsonRpcAsync(string body)
{
    using var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
    var root = doc.RootElement;
    var hasId = root.TryGetProperty("id", out var idElement);
    var id = hasId ? idElement.Clone() : default;
    var method = root.TryGetProperty("method", out var methodElement)
        ? methodElement.GetString()
        : null;

    if (method is "notifications/initialized")
    {
        return null;
    }

    return method switch
    {
        "initialize" => JsonRpcResult(id, new
        {
            protocolVersion = "2025-06-18",
            capabilities = new
            {
                tools = new
                {
                    listChanged = false
                }
            },
            serverInfo = new
            {
                name = "CodexTeamUp",
                version = "0.1.0"
            }
        }),
        "tools/list" => JsonRpcResult(id, new
        {
            tools = registry.ToolNames.Select(name => new
            {
                name,
                description = McpToolMetadata.ToolDescription(name),
                inputSchema = McpToolMetadata.ToolInputSchema(name),
                annotations = McpToolMetadata.ToolAnnotations(name)
            }).ToArray()
        }),
        "tools/call" => await HandleMcpToolCallAsync(id, root).ConfigureAwait(false),
        _ => JsonRpcError(id, -32601, $"Unknown method: {method}")
    };
}

async Task<object> HandleMcpToolCallAsync(JsonElement id, JsonElement root)
{
    var parameters = root.TryGetProperty("params", out var paramsElement)
        ? paramsElement
        : throw new ArgumentException("Missing params.");
    var name = paramsElement.GetProperty("name").GetString()
        ?? throw new ArgumentException("Missing tool name.");
    var arguments = paramsElement.TryGetProperty("arguments", out var argsElement)
        ? argsElement
        : JsonSerializer.Deserialize<JsonElement>("{}");

    try
    {
        var result = await registry.InvokeAsync(name, arguments).ConfigureAwait(false);
        var text = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonFile.Options) { WriteIndented = true });
        return JsonRpcResult(id, new
        {
            content = new[]
            {
                new
                {
                    type = "text",
                    text
                }
            },
            isError = false
        });
    }
    catch (Exception ex)
    {
        return JsonRpcResult(id, new
        {
            content = new[]
            {
                new
                {
                    type = "text",
                    text = SafeText.Redact(ex.Message)
                }
            },
            isError = true
        });
    }
}

static object JsonRpcResult(JsonElement id, object result)
{
    return new
    {
        jsonrpc = "2.0",
        id,
        result
    };
}

static object JsonRpcError(JsonElement id, int code, string message)
{
    return new
    {
        jsonrpc = "2.0",
        id,
        error = new
        {
            code,
            message
        }
    };
}

async Task WriteJsonAsync(NetworkStream stream, int statusCode, object value)
{
    var bytes = JsonSerializer.SerializeToUtf8Bytes(value, jsonOptions);
    await WriteRawAsync(stream, statusCode, "application/json; charset=utf-8", bytes).ConfigureAwait(false);
}

async Task WriteHtmlAsync(NetworkStream stream, string html)
{
    await WriteRawAsync(stream, 200, "text/html; charset=utf-8", Encoding.UTF8.GetBytes(html)).ConfigureAwait(false);
}

async Task WriteRawAsync(NetworkStream stream, int statusCode, string contentType, byte[] bytes)
{
    var reason = statusCode switch
    {
        200 => "OK",
        204 => "No Content",
        202 => "Accepted",
        400 => "Bad Request",
        404 => "Not Found",
        500 => "Internal Server Error",
        _ => "OK"
    };
    var header = Encoding.ASCII.GetBytes(
        $"HTTP/1.1 {statusCode} {reason}\r\n" +
        $"Content-Type: {contentType}\r\n" +
        "Access-Control-Allow-Origin: *\r\n" +
        "Access-Control-Allow-Headers: content-type\r\n" +
        "Access-Control-Allow-Methods: GET,POST,OPTIONS\r\n" +
        $"Content-Length: {bytes.Length}\r\n" +
        "Connection: close\r\n\r\n");
    await stream.WriteAsync(header).ConfigureAwait(false);
    if (bytes.Length > 0)
    {
        await stream.WriteAsync(bytes).ConfigureAwait(false);
    }
}

static Dictionary<string, string> ParseQuery(string query)
{
    var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var raw = query.TrimStart('?');
    if (string.IsNullOrWhiteSpace(raw))
    {
        return result;
    }

    foreach (var part in raw.Split('&', StringSplitOptions.RemoveEmptyEntries))
    {
        var equals = part.IndexOf('=');
        var key = equals >= 0 ? part[..equals] : part;
        var value = equals >= 0 ? part[(equals + 1)..] : "";
        result[WebUtility.UrlDecode(key)] = WebUtility.UrlDecode(value);
    }

    return result;
}

static string? JsonString(JsonElement element, string name)
{
    return element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        ? value.GetString()
        : null;
}

static IReadOnlyDictionary<string, string> JsonStringDictionary(JsonElement element, string name)
{
    if (element.ValueKind != JsonValueKind.Object
        || !element.TryGetProperty(name, out var value)
        || value.ValueKind != JsonValueKind.Object)
    {
        return new Dictionary<string, string>();
    }

    return value.EnumerateObject()
        .Where(property => property.Value.ValueKind == JsonValueKind.String)
        .ToDictionary(property => property.Name, property => property.Value.GetString() ?? "", StringComparer.OrdinalIgnoreCase);
}

static string ResolveProjectRootForBus(string busRoot)
{
    var full = Path.GetFullPath(busRoot);
    var directory = new DirectoryInfo(full);
    if (string.Equals(directory.Name, "agentbus", StringComparison.OrdinalIgnoreCase)
        && string.Equals(directory.Parent?.Name, ".codexteamup", StringComparison.OrdinalIgnoreCase)
        && directory.Parent?.Parent is not null)
    {
        return directory.Parent.Parent.FullName;
    }

    return Environment.CurrentDirectory;
}

static string? ReadOption(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
    {
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
        {
            return args[i + 1];
        }
    }

    return null;
}

static int? ReadIntOption(string[] args, string name)
{
    var raw = ReadOption(args, name);
    return int.TryParse(raw, out var value) && value > 0 ? value : null;
}

static int? ReadIntEnvironment(string name)
{
    var raw = Environment.GetEnvironmentVariable(name);
    return int.TryParse(raw, out var value) && value > 0 ? value : null;
}

static async Task MonitorParentProcessAsync(int parentProcessId, CancellationTokenSource shutdown)
{
    try
    {
        using var parent = Process.GetProcessById(parentProcessId);
        await parent.WaitForExitAsync(shutdown.Token).ConfigureAwait(false);
    }
    catch (ArgumentException)
    {
    }
    catch (InvalidOperationException)
    {
    }
    catch (OperationCanceledException)
    {
        return;
    }

    if (!shutdown.IsCancellationRequested)
    {
        await shutdown.CancelAsync().ConfigureAwait(false);
    }
}
