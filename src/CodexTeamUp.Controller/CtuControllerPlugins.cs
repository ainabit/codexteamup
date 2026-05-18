using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
using CodexTeamUp.AgentBus;
using CodexTeamUp.AppServer;
using CodexTeamUp.Core;

namespace CodexTeamUp.Controller;

public interface ICtuControllerPlugin
{
    string Name { get; }

    string Version { get; }

    ICtuController Create(CtuControllerPluginContext context);
}

public sealed record CtuControllerPluginContext(
    string DefaultBusRoot,
    IAppServerClient AppServer,
    ReloadableCtuControllerPolicy Policy,
    CtuJsonLogger? Logger,
    IReadOnlyDictionary<string, string> Options);

public interface IReloadableCtuController : ICtuController
{
    CtuControllerRuntimeStatus Reload(
        string? pluginPath,
        string? pluginType = null,
        IReadOnlyDictionary<string, string>? options = null);
}

public sealed class ReloadableCtuController : IReloadableCtuController, IDisposable
{
    private readonly object _gate = new();
    private readonly string _defaultBusRoot;
    private readonly IAppServerClient _appServer;
    private readonly CtuJsonLogger? _logger;
    private readonly ReloadableCtuControllerPolicy _policy;
    private LoadedController _current;
    private int _reloadCount;
    private string? _lastError;

    private ReloadableCtuController(
        string defaultBusRoot,
        IAppServerClient appServer,
        CtuJsonLogger? logger,
        ReloadableCtuControllerPolicy policy)
    {
        _defaultBusRoot = defaultBusRoot;
        _appServer = appServer;
        _logger = logger;
        _policy = policy;
        _current = CreateUnloaded(policy, 0, "Controller plugin has not been loaded.");
    }

    public IReadOnlyList<string> ToolNames => _current.Controller.ToolNames
        .Concat(["codex_controller_status", "codex_controller_reload"])
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public CtuControllerRuntimeStatus Status => _current.Status with
    {
        ReloadCount = _reloadCount,
        LastError = _lastError,
        Policy = _policy.Status
    };

    public static ReloadableCtuController CreateDefault(
        string defaultBusRoot,
        IAppServerClient appServer,
        CtuJsonLogger? logger = null,
        ReloadableCtuControllerPolicy? policy = null)
    {
        var controller = new ReloadableCtuController(defaultBusRoot, appServer, logger, policy ?? new ReloadableCtuControllerPolicy(logger: logger));
        controller.Reload(
            Environment.GetEnvironmentVariable("CTU_CONTROLLER_PLUGIN_PATH"),
            Environment.GetEnvironmentVariable("CTU_CONTROLLER_PLUGIN_TYPE"));

        return controller;
    }

    public Task<object> InvokeToolAsync(string name, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (name.Equals("codex_controller_status", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<object>(Status);
        }

        if (name.Equals("codex_controller_reload", StringComparison.OrdinalIgnoreCase))
        {
            var policyPath = JsonString(arguments, "policyPath");
            if (arguments.TryGetProperty("reloadPolicy", out var reloadPolicyElement)
                && reloadPolicyElement.ValueKind == JsonValueKind.True)
            {
                _policy.Reload(policyPath);
            }

            return Task.FromResult<object>(Reload(
                JsonString(arguments, "pluginPath") ?? JsonString(arguments, "path"),
                JsonString(arguments, "pluginType") ?? JsonString(arguments, "type"),
                JsonStringDictionary(arguments, "options")));
        }

        return _current.Controller.InvokeToolAsync(name, arguments, cancellationToken);
    }

    public CtuControllerPolicyStatus ReloadPolicy(string? policyPath)
        => _policy.Reload(policyPath);

    public CtuControllerRuntimeStatus Reload(
        string? pluginPath,
        string? pluginType = null,
        IReadOnlyDictionary<string, string>? options = null)
    {
        lock (_gate)
        {
            _reloadCount++;
            LoadedController next;
            try
            {
                var resolvedPluginPath = ResolveControllerPluginPath(pluginPath);
                next = CreatePlugin(_defaultBusRoot, _appServer, _logger, _policy, resolvedPluginPath, pluginType, options ?? new Dictionary<string, string>(), _reloadCount);
            }
            catch (Exception ex)
            {
                _lastError = SafeText.Redact(ex.Message);
                _logger?.Error("controller.reload.failed", ex, new { pluginPath, pluginType });
                return Status;
            }

            var previous = _current;
            _current = next;
            _lastError = null;
            DisposeLoaded(previous);
            _logger?.Info("controller.reload.completed", new { pluginPath, pluginType, next.Status.ActiveSource, next.Status.ActiveType });
            return Status;
        }
    }

    public void Dispose()
    {
        DisposeLoaded(_current);
    }

    private static LoadedController CreateUnloaded(
        ReloadableCtuControllerPolicy policy,
        int reloadCount,
        string? lastError)
    {
        return new LoadedController(
            new NoControllerLoaded(),
            null,
            new CtuControllerRuntimeStatus(
                "unloaded",
                nameof(NoControllerLoaded),
                null,
                null,
                DateTimeOffset.Now,
                reloadCount,
                lastError,
                policy.Status));
    }

    private static string ResolveControllerPluginPath(string? pluginPath)
    {
        if (!string.IsNullOrWhiteSpace(pluginPath))
        {
            return pluginPath;
        }

        var configuredDefaultPath = Environment.GetEnvironmentVariable("CTU_CONTROLLER_PLUGIN_PATH");
        if (!string.IsNullOrWhiteSpace(configuredDefaultPath))
        {
            return configuredDefaultPath;
        }

        var defaultPath = Path.Combine(AppContext.BaseDirectory, "CodexTeamUp.Controller.Default.dll");
        if (File.Exists(defaultPath))
        {
            return defaultPath;
        }

        throw new FileNotFoundException("Default CTU controller plugin was not found. Set CTU_CONTROLLER_PLUGIN_PATH or deploy CodexTeamUp.Controller.Default.dll next to the service.", defaultPath);
    }

    private static LoadedController CreatePlugin(
        string defaultBusRoot,
        IAppServerClient appServer,
        CtuJsonLogger? logger,
        ReloadableCtuControllerPolicy policy,
        string pluginPath,
        string? pluginType,
        IReadOnlyDictionary<string, string> options,
        int reloadCount)
    {
        var fullPath = Path.GetFullPath(pluginPath);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException("CTU controller plugin was not found.", fullPath);
        }

        var shadowPath = ShadowCopyPlugin(fullPath);
        var loadContext = new ControllerPluginLoadContext(shadowPath);
        try
        {
            var assembly = loadContext.LoadFromAssemblyPath(shadowPath);
            var type = ResolvePluginType(assembly, pluginType);
            var context = new CtuControllerPluginContext(defaultBusRoot, appServer, policy, logger, options);
            var controller = CreatePluginController(type, context);
            return new LoadedController(
                controller,
                loadContext,
                new CtuControllerRuntimeStatus(
                    "plugin",
                    controller.GetType().FullName ?? controller.GetType().Name,
                    fullPath,
                    type.FullName,
                    DateTimeOffset.Now,
                    reloadCount,
                    null,
                    policy.Status));
        }
        catch
        {
            loadContext.Unload();
            throw;
        }
    }

    private static Type ResolvePluginType(Assembly assembly, string? pluginType)
    {
        if (!string.IsNullOrWhiteSpace(pluginType))
        {
            return assembly.GetType(pluginType, throwOnError: true)
                ?? throw new InvalidOperationException($"Plugin type {pluginType} was not found.");
        }

        return assembly.GetTypes()
            .FirstOrDefault(type => !type.IsAbstract && typeof(ICtuControllerPlugin).IsAssignableFrom(type))
            ?? assembly.GetTypes().FirstOrDefault(type => !type.IsAbstract && typeof(ICtuController).IsAssignableFrom(type))
            ?? throw new InvalidOperationException("Controller plugin assembly must contain ICtuControllerPlugin or ICtuController.");
    }

    private static ICtuController CreatePluginController(Type type, CtuControllerPluginContext context)
    {
        if (typeof(ICtuControllerPlugin).IsAssignableFrom(type))
        {
            var plugin = (ICtuControllerPlugin?)Activator.CreateInstance(type)
                ?? throw new InvalidOperationException($"Could not create controller plugin {type.FullName}.");
            return plugin.Create(context);
        }

        if (typeof(ICtuController).IsAssignableFrom(type))
        {
            if (Activator.CreateInstance(type, context) is ICtuController withContext)
            {
                return withContext;
            }

            if (Activator.CreateInstance(type) is ICtuController parameterless)
            {
                return parameterless;
            }
        }

        throw new InvalidOperationException($"Unsupported controller plugin type {type.FullName}.");
    }

    public Task RunStartupSweepAsync(CancellationToken cancellationToken = default)
    {
        return _current.Controller.RunStartupSweepAsync(cancellationToken);
    }

    private static string ShadowCopyPlugin(string pluginPath)
    {
        var sourceDirectory = Path.GetDirectoryName(pluginPath) ?? Environment.CurrentDirectory;
        var shadowDirectory = Path.Combine(Path.GetTempPath(), "codexteamup-controller-plugins", Guid.NewGuid().ToString("N"));
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

    private static void DisposeLoaded(LoadedController loaded)
    {
        if (loaded.Controller is IDisposable disposable)
        {
            disposable.Dispose();
        }

        loaded.LoadContext?.Unload();
    }

    private static string? JsonString(JsonElement item, string name)
    {
        return item.ValueKind == JsonValueKind.Object
            && item.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static IReadOnlyDictionary<string, string> JsonStringDictionary(JsonElement item, string name)
    {
        if (item.ValueKind != JsonValueKind.Object
            || !item.TryGetProperty(name, out var value)
            || value.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, string>();
        }

        return value.EnumerateObject()
            .Where(property => property.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(property => property.Name, property => property.Value.GetString() ?? string.Empty, StringComparer.OrdinalIgnoreCase);
    }

    private sealed record LoadedController(
        ICtuController Controller,
        AssemblyLoadContext? LoadContext,
        CtuControllerRuntimeStatus Status);

    private sealed class NoControllerLoaded : ICtuController
    {
        public IReadOnlyList<string> ToolNames => [];

        public CtuControllerRuntimeStatus Status => new(
            "unloaded",
            nameof(NoControllerLoaded),
            null,
            null,
            DateTimeOffset.Now,
            0,
            "Controller plugin has not been loaded.",
            new CtuControllerPolicyStatus(
                "unloaded",
                nameof(CtuControllerPolicy),
                null,
                DateTimeOffset.Now,
                0,
                null,
                CtuControllerPolicy.Default));

        public Task<object> InvokeToolAsync(string name, JsonElement arguments, CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("No CTU controller plugin is loaded. Use codex_controller_reload with a plugin DLL path.");

        public Task RunStartupSweepAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ControllerPluginLoadContext(string pluginPath) : AssemblyLoadContext(isCollectible: true)
    {
        private readonly AssemblyDependencyResolver _resolver = new(pluginPath);

        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name == typeof(ICtuController).Assembly.GetName().Name
                || assemblyName.Name == typeof(AgentBusStore).Assembly.GetName().Name
                || assemblyName.Name == typeof(IAppServerClient).Assembly.GetName().Name
                || assemblyName.Name == typeof(CtuJsonLogger).Assembly.GetName().Name)
            {
                return null;
            }

            var path = _resolver.ResolveAssemblyToPath(assemblyName);
            return path is null ? null : LoadFromAssemblyPath(path);
        }
    }
}
