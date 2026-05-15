using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using CodexTeamUp.CodexWrapper;

var invocationId = $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfffZ}-{Environment.ProcessId}";
var logger = WrapperLogger.Create(invocationId);

try
{
    var realCodex = ResolveRealCodexPath();
    var currentProcess = Environment.ProcessPath;

    logger.Write("start", new
    {
        invocationId,
        pid = Environment.ProcessId,
        cwd = Environment.CurrentDirectory,
        wrapperPath = currentProcess,
        realCodex,
        args,
        selectedEnvironment = SelectedEnvironment()
    });

    if (currentProcess is not null
        && string.Equals(Path.GetFullPath(currentProcess), Path.GetFullPath(realCodex), StringComparison.OrdinalIgnoreCase))
    {
        logger.Write("error", new { message = "CODEX_WRAPPER_REAL_CODEX resolves to the wrapper itself." });
        return 127;
    }

    if (!File.Exists(realCodex))
    {
        logger.Write("error", new { message = "Real Codex executable was not found.", realCodex });
        return 127;
    }

    using var cancellation = new CancellationTokenSource();
    Console.CancelKeyPress += (_, eventArgs) =>
    {
        eventArgs.Cancel = true;
        cancellation.Cancel();
    };

    var process = StartRealCodex(realCodex, args);
    logger.Write("child-started", new { childPid = process.Id });

    if (IsAppServerInvocation(args))
    {
        await RunAppServerProxyAsync(process, logger, cancellation.Token).ConfigureAwait(false);
    }
    else
    {
        await RunTransparentProxyAsync(process, cancellation.Token).ConfigureAwait(false);
    }

    logger.Write("exit", new { childPid = process.Id, exitCode = process.ExitCode });
    return process.ExitCode;
}
catch (OperationCanceledException)
{
    logger.Write("cancelled", new { pid = Environment.ProcessId });
    return 130;
}
catch (Exception ex)
{
    logger.Write("fatal", new { type = ex.GetType().FullName, ex.Message, ex.StackTrace });
    return 1;
}

static async Task RunTransparentProxyAsync(Process process, CancellationToken cancellationToken)
{
    var stdinTask = CopyStreamAsync(
        Console.OpenStandardInput(),
        process.StandardInput.BaseStream,
        closeDestination: true,
        cancellationToken);

    var stdoutTask = CopyStreamAsync(
        process.StandardOutput.BaseStream,
        Console.OpenStandardOutput(),
        closeDestination: false,
        cancellationToken);

    var stderrTask = CopyStreamAsync(
        process.StandardError.BaseStream,
        Console.OpenStandardError(),
        closeDestination: false,
        cancellationToken);

    await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
    await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
}

static async Task RunAppServerProxyAsync(Process process, WrapperLogger logger, CancellationToken cancellationToken)
{
    var pipeName = ResolvePipeName();
    var pending = new ConcurrentDictionary<string, PendingBridgeRequest>();
    using var proxyCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    using var childWriteLock = new SemaphoreSlim(1, 1);

    logger.Write("appserver-proxy-start", new { pipeName, childPid = process.Id });

    var desktopToChild = RelayJsonLinesAsync(
        source: Console.OpenStandardInput(),
        destination: process.StandardInput.BaseStream,
        direction: "desktop-to-appserver",
        logger,
        childWriteLock,
        closeDestination: true,
        pendingBridgeResponses: null,
        cancellationToken);

    var childToDesktop = RelayJsonLinesAsync(
        source: process.StandardOutput.BaseStream,
        destination: Console.OpenStandardOutput(),
        direction: "appserver-to-desktop",
        logger,
        writeLock: null,
        closeDestination: false,
        pendingBridgeResponses: pending,
        cancellationToken);

    var stderrTask = CopyStreamAsync(
        process.StandardError.BaseStream,
        Console.OpenStandardError(),
        closeDestination: false,
        cancellationToken);

    var pipeServer = RunBridgePipeServerAsync(
        pipeName,
        process.StandardInput.BaseStream,
        childWriteLock,
        pending,
        logger,
        proxyCancellation.Token);

    await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
    await proxyCancellation.CancelAsync().ConfigureAwait(false);

    foreach (var request in pending.Values)
    {
        request.TrySetResult(ErrorResponse(request.RequestId, -32000, "Codex app-server exited before responding."));
    }

    await Task.WhenAll(IgnoreCancellation(desktopToChild), IgnoreCancellation(childToDesktop), IgnoreCancellation(stderrTask), IgnoreCancellation(pipeServer)).ConfigureAwait(false);
}

static async Task RunBridgePipeServerAsync(
    string pipeName,
    Stream childStdin,
    SemaphoreSlim childWriteLock,
    ConcurrentDictionary<string, PendingBridgeRequest> pending,
    WrapperLogger logger,
    CancellationToken cancellationToken)
{
    while (!cancellationToken.IsCancellationRequested)
    {
        await using var pipe = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            NamedPipeServerStream.MaxAllowedServerInstances,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        try
        {
            await pipe.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            await HandleBridgePipeClientAsync(pipe, childStdin, childWriteLock, pending, logger, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            break;
        }
        catch (Exception ex)
        {
            logger.Write("pipe-error", new { type = ex.GetType().FullName, ex.Message });
        }
    }
}

static async Task HandleBridgePipeClientAsync(
    Stream pipe,
    Stream childStdin,
    SemaphoreSlim childWriteLock,
    ConcurrentDictionary<string, PendingBridgeRequest> pending,
    WrapperLogger logger,
    CancellationToken cancellationToken)
{
    using var reader = new StreamReader(pipe, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);
    await using var writer = new StreamWriter(pipe, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), leaveOpen: true)
    {
        NewLine = "\n",
        AutoFlush = true
    };

    var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
    if (string.IsNullOrWhiteSpace(line))
    {
        await writer.WriteLineAsync(ErrorResponse(null, -32600, "Expected one JSON request line.")).ConfigureAwait(false);
        return;
    }

    using var document = JsonDocument.Parse(line);
    if (!document.RootElement.TryGetProperty("method", out var methodElement)
        || methodElement.ValueKind != JsonValueKind.String
        || string.IsNullOrWhiteSpace(methodElement.GetString()))
    {
        await writer.WriteLineAsync(ErrorResponse(null, -32600, "Expected string property 'method'.")).ConfigureAwait(false);
        return;
    }

    var method = methodElement.GetString()!;
    if (string.Equals(method, "ctu/status", StringComparison.Ordinal))
    {
        await writer.WriteLineAsync(JsonSerializer.Serialize(new
        {
            result = new
            {
                status = "ok",
                pipeName = ResolvePipeName(),
                pending = pending.Count
            }
        }, WrapperJson.Options)).ConfigureAwait(false);
        return;
    }

    var requestId = $"{WrapperProtocol.BridgeRequestIdPrefix}{Guid.NewGuid():N}";
    var appServerRequest = WrapperProtocol.BuildAppServerRequest(requestId, document.RootElement);
    var pendingRequest = new PendingBridgeRequest(requestId);

    if (!pending.TryAdd(requestId, pendingRequest))
    {
        await writer.WriteLineAsync(ErrorResponse(requestId, -32603, "Failed to register pending bridge request.")).ConfigureAwait(false);
        return;
    }

    logger.Write("bridge-request", new
    {
        id = requestId,
        method,
        hasParams = document.RootElement.TryGetProperty("params", out _),
        byteLength = appServerRequest.Length
    });

    try
    {
        await WriteLineLockedAsync(childStdin, appServerRequest, childWriteLock, cancellationToken).ConfigureAwait(false);

        var timeout = ResolveBridgeRequestTimeout();
        var responseTask = pendingRequest.Task.WaitAsync(timeout, cancellationToken);
        var response = await responseTask.ConfigureAwait(false);
        await writer.WriteLineAsync(response).ConfigureAwait(false);
    }
    catch (TimeoutException)
    {
        pending.TryRemove(requestId, out _);
        var response = ErrorResponse(requestId, -32001, $"Timed out waiting for app-server response after {ResolveBridgeRequestTimeout().TotalSeconds:n0}s.");
        logger.Write("bridge-timeout", new { id = requestId, method });
        await writer.WriteLineAsync(response).ConfigureAwait(false);
    }
    finally
    {
        pending.TryRemove(requestId, out _);
    }
}

static async Task RelayJsonLinesAsync(
    Stream source,
    Stream destination,
    string direction,
    WrapperLogger logger,
    SemaphoreSlim? writeLock,
    bool closeDestination,
    ConcurrentDictionary<string, PendingBridgeRequest>? pendingBridgeResponses,
    CancellationToken cancellationToken)
{
    using var reader = new StreamReader(source, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

    try
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line is null)
            {
                break;
            }

            var summary = TrySummarizeJsonLine(line);
            logger.Write("rpc-line", new { direction, summary.Method, summary.Id, summary.HasParams, line.Length });

            if (pendingBridgeResponses is not null
                && summary.Id is not null
                && pendingBridgeResponses.TryRemove(summary.Id, out var bridgeRequest))
            {
                logger.Write("bridge-response", new { id = summary.Id, summary.Method, line.Length });
                bridgeRequest.TrySetResult(line);
                continue;
            }

            var outputLine = direction == "desktop-to-appserver"
                ? TryRewriteDesktopRequest(line, logger)
                : TryRewriteAppServerNotification(line, logger);

            if (writeLock is null)
            {
                await WriteLineAsync(destination, outputLine, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await WriteLineLockedAsync(destination, outputLine, writeLock, cancellationToken).ConfigureAwait(false);
            }
        }
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
    {
        logger.Write("relay-closed", new { direction, type = ex.GetType().FullName, ex.Message });
    }
    finally
    {
        if (closeDestination)
        {
            try
            {
                destination.Close();
            }
            catch
            {
                // Best-effort stream close only.
            }
        }
    }
}

static string TryRewriteDesktopRequest(string line, WrapperLogger logger)
{
    return WrapperProtocol.RewriteTurnsListAscending(line, ForceTurnsListAscending(), () =>
        logger.Write("rpc-rewrite", new
        {
            method = "thread/turns/list",
            rewrite = "sortDirection=asc"
        }));
}

static string TryRewriteAppServerNotification(string line, WrapperLogger logger)
{
    return WrapperProtocol.StampTurnStartedAt(line, StampTurnStartedAt(), DateTimeOffset.UtcNow.ToUnixTimeSeconds, () =>
        logger.Write("rpc-rewrite", new
        {
            method = "turn/started",
            rewrite = "startedAt=now"
        }));
}

static bool ForceTurnsListAscending()
{
    var raw = Environment.GetEnvironmentVariable("CODEX_WRAPPER_FORCE_TURNS_ASC");
    return string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase);
}

static bool StampTurnStartedAt()
{
    var raw = Environment.GetEnvironmentVariable("CODEX_WRAPPER_STAMP_TURN_STARTED_AT");
    return string.Equals(raw, "1", StringComparison.OrdinalIgnoreCase)
        || string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)
        || string.Equals(raw, "yes", StringComparison.OrdinalIgnoreCase);
}

static async Task WriteLineLockedAsync(Stream destination, string line, SemaphoreSlim writeLock, CancellationToken cancellationToken)
{
    await writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
        await WriteLineAsync(destination, line, cancellationToken).ConfigureAwait(false);
    }
    finally
    {
        writeLock.Release();
    }
}

static async Task WriteLineAsync(Stream destination, string line, CancellationToken cancellationToken)
{
    var bytes = Encoding.UTF8.GetBytes(line + "\n");
    await destination.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
}

static RpcSummary TrySummarizeJsonLine(string line)
{
    var summary = WrapperProtocol.SummarizeJsonLine(line);
    return new RpcSummary(summary.Id, summary.Method, summary.HasParams);
}

static string ErrorResponse(string? id, int code, string message)
{
    return JsonSerializer.Serialize(new
    {
        id,
        error = new
        {
            code,
            message
        }
    }, WrapperJson.Options);
}

static TimeSpan ResolveBridgeRequestTimeout()
{
    var raw = Environment.GetEnvironmentVariable("CODEX_WRAPPER_REQUEST_TIMEOUT_MS");
    return int.TryParse(raw, out var milliseconds) && milliseconds > 0
        ? TimeSpan.FromMilliseconds(milliseconds)
        : TimeSpan.FromMinutes(2);
}

static string ResolvePipeName()
{
    var configured = Environment.GetEnvironmentVariable("CODEX_WRAPPER_PIPE_NAME");
    if (!string.IsNullOrWhiteSpace(configured))
    {
        return configured.Trim();
    }

    return "codexteamup-appserver";
}

static bool IsAppServerInvocation(string[] args)
{
    return args.Length > 0 && string.Equals(args[0], "app-server", StringComparison.OrdinalIgnoreCase);
}

static async Task IgnoreCancellation(Task task)
{
    try
    {
        await task.ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
    }
}

static Process StartRealCodex(string realCodex, string[] args)
{
    var startInfo = new ProcessStartInfo
    {
        FileName = realCodex,
        WorkingDirectory = Environment.CurrentDirectory,
        UseShellExecute = false,
        RedirectStandardInput = true,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true
    };

    foreach (var arg in args)
    {
        startInfo.ArgumentList.Add(arg);
    }

    startInfo.Environment["CODEX_WRAPPER_DELEGATED"] = "1";

    return Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start real Codex process.");
}

static string ResolveRealCodexPath()
{
    var configured = Environment.GetEnvironmentVariable("CODEX_WRAPPER_REAL_CODEX");
    if (!string.IsNullOrWhiteSpace(configured))
    {
        return Environment.ExpandEnvironmentVariables(configured.Trim());
    }

    var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    return Path.Combine(localAppData, "OpenAI", "Codex", "bin", "codex.exe");
}

static async Task CopyStreamAsync(Stream source, Stream destination, bool closeDestination, CancellationToken cancellationToken)
{
    try
    {
        await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
    catch (OperationCanceledException)
    {
        throw;
    }
    catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
    {
        // The wrapped app-server frequently closes one side of stdio during shutdown.
    }
    finally
    {
        if (closeDestination)
        {
            try
            {
                destination.Close();
            }
            catch
            {
                // Best-effort stream close only.
            }
        }
    }
}

static Dictionary<string, string?> SelectedEnvironment()
{
    var names = new[]
    {
        "CODEX_CLI_PATH",
        "CODEX_WRAPPER_REAL_CODEX",
        "CODEX_WRAPPER_LOG_DIR",
        "CODEX_WRAPPER_PIPE_NAME",
        "CODEX_WRAPPER_REQUEST_TIMEOUT_MS",
        "CODEX_WRAPPER_FORCE_TURNS_ASC",
        "CODEX_WRAPPER_STAMP_TURN_STARTED_AT",
        "CODEX_HOME",
        "CODEX_APP_SERVER_WS_URL",
        "CODEX_APP_SERVER_FORCE_CLI",
        "CODEX_REMOTE_PAYLOAD",
        "CODEX_ELECTRON_USER_DATA_PATH"
    };

    return names.ToDictionary(name => name, name => Redact(Environment.GetEnvironmentVariable(name)));
}

static string? Redact(string? value)
{
    if (string.IsNullOrEmpty(value))
    {
        return value;
    }

    if (value.Contains("token", StringComparison.OrdinalIgnoreCase)
        || value.Contains("secret", StringComparison.OrdinalIgnoreCase)
        || value.Contains("key", StringComparison.OrdinalIgnoreCase))
    {
        return "[redacted]";
    }

    return value.Length > 600 ? string.Concat(value.AsSpan(0, 600), "...[truncated]") : value;
}

readonly record struct RpcSummary(string? Id, string? Method, bool HasParams);

sealed class PendingBridgeRequest
{
    private readonly TaskCompletionSource<string> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public PendingBridgeRequest(string requestId)
    {
        RequestId = requestId;
    }

    public string RequestId { get; }

    public Task<string> Task => completion.Task;

    public void TrySetResult(string response)
    {
        completion.TrySetResult(response);
    }
}

static class WrapperJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
}

sealed class WrapperLogger
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly string logFile;
    private readonly object gate = new();

    private WrapperLogger(string logFile)
    {
        this.logFile = logFile;
    }

    public static WrapperLogger Create(string invocationId)
    {
        var configuredDir = Environment.GetEnvironmentVariable("CODEX_WRAPPER_LOG_DIR");
        var logDir = string.IsNullOrWhiteSpace(configuredDir)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "codexteamup-wrapper")
            : Environment.ExpandEnvironmentVariables(configuredDir.Trim());

        Directory.CreateDirectory(logDir);
        return new WrapperLogger(Path.Combine(logDir, $"codex-wrapper-{invocationId}.jsonl"));
    }

    public void Write(string type, object payload)
    {
        var entry = new
        {
            timestamp = DateTimeOffset.UtcNow,
            type,
            payload
        };

        var line = JsonSerializer.Serialize(entry, JsonOptions);
        lock (gate)
        {
            File.AppendAllText(logFile, line + Environment.NewLine, Encoding.UTF8);
        }
    }
}
