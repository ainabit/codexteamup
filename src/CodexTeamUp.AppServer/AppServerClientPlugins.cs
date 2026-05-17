using System.Reflection;
using System.Runtime.Loader;
using CodexTeamUp.Core;

namespace CodexTeamUp.AppServer;

/// <summary>
/// Factory contract for hot-swappable Codex Desktop adapter plugins.
/// </summary>
public interface IAppServerClientPlugin
{
    string Name { get; }

    string Version { get; }

    IAppServerClient Create(AppServerClientPluginContext context);
}

public sealed record AppServerClientPluginContext(
    string? PipeName,
    IReadOnlyDictionary<string, string> Options,
    CtuJsonLogger? Logger = null);

public sealed record AppServerClientPluginStatus(
    string ActiveSource,
    string ActiveType,
    string? PluginPath,
    string? PluginType,
    DateTimeOffset LoadedAt,
    int ReloadCount,
    string? LastError);

public interface IReloadableAppServerClient : IAppServerClient
{
    AppServerClientPluginStatus Status { get; }

    AppServerClientPluginStatus Reload(
        string? pluginPath,
        string? pluginType = null,
        IReadOnlyDictionary<string, string>? options = null);
}

/// <summary>
/// Stable facade used by MCP. The wrapped Desktop adapter can be replaced while the service keeps running.
/// </summary>
public sealed class ReloadableAppServerClient : IReloadableAppServerClient, IDisposable
{
    private readonly object _gate = new();
    private readonly string? _pipeName;
    private readonly CtuJsonLogger? _logger;
    private LoadedClient _current;
    private int _reloadCount;
    private string? _lastError;

    private ReloadableAppServerClient(string? pipeName, CtuJsonLogger? logger)
    {
        _pipeName = pipeName;
        _logger = logger;
        _current = CreateBuiltIn(pipeName, reloadCount: 0, lastError: null, logger: logger);
    }

    public AppServerClientPluginStatus Status => _current.Status with { ReloadCount = _reloadCount, LastError = _lastError };

    public static ReloadableAppServerClient CreateDefault(string? pipeName, CtuJsonLogger? logger = null)
    {
        var client = new ReloadableAppServerClient(pipeName, logger);
        var pluginPath = Environment.GetEnvironmentVariable("CTU_APP_SERVER_PLUGIN_PATH");
        if (!string.IsNullOrWhiteSpace(pluginPath))
        {
            client.Reload(pluginPath, Environment.GetEnvironmentVariable("CTU_APP_SERVER_PLUGIN_TYPE"));
        }

        return client;
    }

    public AppServerClientPluginStatus Reload(
        string? pluginPath,
        string? pluginType = null,
        IReadOnlyDictionary<string, string>? options = null)
    {
        lock (_gate)
        {
            _reloadCount++;
            LoadedClient next;
            try
            {
                next = string.IsNullOrWhiteSpace(pluginPath)
                    ? CreateBuiltIn(_pipeName, _reloadCount, lastError: null, options ?? new Dictionary<string, string>(), _logger)
                    : CreatePlugin(_pipeName, pluginPath!, pluginType, options ?? new Dictionary<string, string>(), _reloadCount, _logger);
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                _logger?.Error("api_adapter.reload.failed", ex, new { pluginPath, pluginType });
                return Status;
            }

            var previous = _current;
            _current = next;
            _lastError = null;
            DisposeLoaded(previous);
            return Status;
        }
    }

    public Task<AppServerCallResult> ProbeAsync(CancellationToken cancellationToken = default)
        => _current.Client.ProbeAsync(cancellationToken);

    public Task<AppServerCallResult> CallAsync(string method, object? parameters, CancellationToken cancellationToken = default)
        => _current.Client.CallAsync(method, parameters, cancellationToken);

    public Task<AppServerCallResult> ListThreadsAsync(string? cwd, int limit, CancellationToken cancellationToken = default)
        => _current.Client.ListThreadsAsync(cwd, limit, cancellationToken);

    public Task<AppServerCallResult> ReadThreadAsync(string threadId, bool includeTurns, CancellationToken cancellationToken = default)
        => _current.Client.ReadThreadAsync(threadId, includeTurns, cancellationToken);

    public Task<AppServerCallResult> StartThreadAsync(
        string cwd,
        string? name,
        string? role,
        AppServerAgentSettings? settings = null,
        CancellationToken cancellationToken = default)
        => _current.Client.StartThreadAsync(cwd, name, role, settings, cancellationToken);

    public Task<AppServerCallResult> ResumeThreadAsync(string threadId, CancellationToken cancellationToken = default)
        => _current.Client.ResumeThreadAsync(threadId, cancellationToken);

    public Task<AppServerCallResult> SendTurnAsync(
        string threadId,
        string message,
        string? cwd = null,
        AppServerAgentSettings? settings = null,
        CancellationToken cancellationToken = default)
        => _current.Client.SendTurnAsync(threadId, message, cwd, settings, cancellationToken);

    public Task<AppServerCallResult> ListTurnsAsync(string threadId, string sortDirection = "asc", int limit = 50, CancellationToken cancellationToken = default)
        => _current.Client.ListTurnsAsync(threadId, sortDirection, limit, cancellationToken);

    public Task<TurnWaitResult> WaitForTurnAsync(string threadId, string turnId, TimeSpan timeout, CancellationToken cancellationToken = default)
        => _current.Client.WaitForTurnAsync(threadId, turnId, timeout, cancellationToken);

    public void Dispose()
    {
        DisposeLoaded(_current);
    }

    private static LoadedClient CreateBuiltIn(
        string? pipeName,
        int reloadCount,
        string? lastError,
        IReadOnlyDictionary<string, string>? options = null,
        CtuJsonLogger? logger = null)
    {
        var transport = new WrapperPipeAppServerClient(pipeName);
        IAppServerClient client = ShouldUsePolicy(options)
            ? new PolicyAppServerClient(transport, ReadOption(options, "policyPath") ?? ReadOption(options, "scriptPath"), options)
            : transport;
        if (logger is not null)
        {
            client = new LoggingAppServerClient(client, logger);
        }

        return new LoadedClient(
            client,
            null,
            new AppServerClientPluginStatus(
                "built-in",
                client.GetType().FullName ?? client.GetType().Name,
                null,
                null,
                DateTimeOffset.Now,
                reloadCount,
                lastError));
    }

    private static bool ShouldUsePolicy(IReadOnlyDictionary<string, string>? options)
    {
        if (options is null || options.Count == 0)
        {
            return false;
        }

        return string.Equals(ReadOption(options, "mode"), "policy", StringComparison.OrdinalIgnoreCase)
            || options.ContainsKey("policyPath")
            || options.ContainsKey("scriptPath");
    }

    private static string? ReadOption(IReadOnlyDictionary<string, string>? options, string name)
    {
        return options is not null && options.TryGetValue(name, out var value) ? value : null;
    }

    private static LoadedClient CreatePlugin(
        string? pipeName,
        string pluginPath,
        string? pluginType,
        IReadOnlyDictionary<string, string> options,
        int reloadCount,
        CtuJsonLogger? logger)
    {
        var fullPath = Path.GetFullPath(pluginPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("AppServer adapter plugin was not found.", fullPath);
        }

        var shadowPath = ShadowCopyPlugin(fullPath);
        var loadContext = new AppServerPluginLoadContext(shadowPath);
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(shadowPath);
            var type = ResolvePluginType(assembly, pluginType);
            var context = new AppServerClientPluginContext(pipeName, options, logger);
            var client = CreatePluginClient(type, context);
            if (logger is not null)
            {
                client = new LoggingAppServerClient(client, logger);
            }

            return new LoadedClient(
                client,
                loadContext,
                new AppServerClientPluginStatus(
                    "plugin",
                    client.GetType().FullName ?? client.GetType().Name,
                    fullPath,
                    type.FullName,
                    DateTimeOffset.Now,
                    reloadCount,
                    null));
        }
        catch
        {
            loadContext.Unload();
            throw;
        }
    }

    private static string ShadowCopyPlugin(string pluginPath)
    {
        var sourceDirectory = Path.GetDirectoryName(pluginPath) ?? Environment.CurrentDirectory;
        var shadowDirectory = Path.Combine(Path.GetTempPath(), "codexteamup-appserver-plugins", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(shadowDirectory);

        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            var extension = Path.GetExtension(file);
            if (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".deps.json", StringComparison.OrdinalIgnoreCase)
                || extension.Equals(".runtimeconfig.json", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(file, Path.Combine(shadowDirectory, Path.GetFileName(file)), overwrite: true);
            }
        }

        return Path.Combine(shadowDirectory, Path.GetFileName(pluginPath));
    }

    private static Type ResolvePluginType(Assembly assembly, string? pluginType)
    {
        if (!string.IsNullOrWhiteSpace(pluginType))
        {
            return assembly.GetType(pluginType, throwOnError: true)
                ?? throw new InvalidOperationException($"Plugin type {pluginType} was not found.");
        }

        return assembly.GetTypes()
            .FirstOrDefault(type => !type.IsAbstract && typeof(IAppServerClientPlugin).IsAssignableFrom(type))
            ?? assembly.GetTypes().FirstOrDefault(type => !type.IsAbstract && typeof(IAppServerClient).IsAssignableFrom(type))
            ?? throw new InvalidOperationException("Plugin assembly must contain an IAppServerClientPlugin or IAppServerClient implementation.");
    }

    private static IAppServerClient CreatePluginClient(Type type, AppServerClientPluginContext context)
    {
        if (typeof(IAppServerClientPlugin).IsAssignableFrom(type))
        {
            var plugin = (IAppServerClientPlugin?)Activator.CreateInstance(type)
                ?? throw new InvalidOperationException($"Could not create plugin factory {type.FullName}.");
            return plugin.Create(context);
        }

        if (typeof(IAppServerClient).IsAssignableFrom(type))
        {
            try
            {
                if (Activator.CreateInstance(type, context) is IAppServerClient withContext)
                {
                    return withContext;
                }
            }
            catch (MissingMethodException)
            {
            }

            if (Activator.CreateInstance(type) is IAppServerClient parameterless)
            {
                return parameterless;
            }
        }

        throw new InvalidOperationException($"Plugin type {type.FullName} cannot create an app-server client.");
    }

    private static void DisposeLoaded(LoadedClient loaded)
    {
        if (loaded.Client is IDisposable disposable)
        {
            disposable.Dispose();
        }

        loaded.LoadContext?.Unload();
    }

    private sealed record LoadedClient(
        IAppServerClient Client,
        AssemblyLoadContext? LoadContext,
        AppServerClientPluginStatus Status);

    private sealed class AppServerPluginLoadContext(string mainAssemblyPath) : AssemblyLoadContext(isCollectible: true)
    {
        private static readonly HashSet<string> SharedAssemblyNames = new(StringComparer.OrdinalIgnoreCase)
        {
            typeof(IAppServerClient).Assembly.GetName().Name ?? "CodexTeamUp.AppServer"
        };

        private readonly AssemblyDependencyResolver _resolver = new(mainAssemblyPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (!string.IsNullOrWhiteSpace(assemblyName.Name) && SharedAssemblyNames.Contains(assemblyName.Name))
            {
                return AssemblyLoadContext.Default.Assemblies
                    .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase));
            }

            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
        }
    }
}
