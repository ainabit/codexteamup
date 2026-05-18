using System.Diagnostics;
using System.Text.Json;
using CodexTeamUp.AgentBus;
using CodexTeamUp.AppServer;
using CodexTeamUp.Core;

namespace CodexTeamUp.Controller;

/// <summary>
/// Default CTU workflow controller loaded through the controller plugin host.
/// </summary>
public sealed class DefaultCtuController : ICtuController
{
    private const string DefaultSpeed = "standard";
    private static readonly TimeSpan WakeupReadyTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan WakeupReadyPollInterval = TimeSpan.FromMilliseconds(200);
    private static readonly SemaphoreSlim DesktopWakeupGate = new(1, 1);
    private static readonly TimeSpan StartupSweepLeaseDuration = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan DeliverySweepWait = TimeSpan.FromMilliseconds(200);
    private static readonly TimeSpan ResultNotificationRetryDelay = TimeSpan.FromSeconds(3);
    private const int MaxResultNotifyRetryAttempts = 8;
    private readonly ReloadableCtuControllerPolicy _controllerPolicy;
    private readonly CtuJsonLogger? _logger;
    private readonly string _defaultBusRoot;
    private readonly IAppServerClient _appServer;
    private readonly DateTimeOffset _loadedAt = DateTimeOffset.Now;
    private readonly SemaphoreSlim _startupSweepGate = new(1, 1);

    public DefaultCtuController(
        string busRoot,
        IAppServerClient appServer,
        ReloadableCtuControllerPolicy? controllerPolicy = null,
        CtuJsonLogger? logger = null)
    {
        _controllerPolicy = controllerPolicy ?? new ReloadableCtuControllerPolicy(logger: logger);
        _appServer = appServer;
        _defaultBusRoot = Path.GetFullPath(busRoot);
        _logger = logger;
        RegisterDefaultTools(busRoot, appServer);
    }

    /// <summary>All tool names exposed by CodexTeamUp.</summary>
    public static readonly IReadOnlyList<string> KnownToolNames = CtuControllerTools.KnownToolNames;

    private readonly Dictionary<string, Func<JsonElement, CancellationToken, Task<object>>> _handlers = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Returns the names of registered tools.</summary>
    public IReadOnlyList<string> ToolNames => _handlers.Keys.OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToList();

    /// <summary>Registers a tool handler.</summary>
    public void Register(string name, Func<JsonElement, CancellationToken, Task<object>> handler)
    {
        _handlers[name] = handler;
    }

    /// <summary>Invokes a registered tool.</summary>
    public async Task<object> InvokeToolAsync(string name, JsonElement arguments, CancellationToken cancellationToken = default)
    {
        if (!_handlers.TryGetValue(name, out var handler))
        {
            throw new InvalidOperationException($"Unknown tool: {name}");
        }

        var startedAt = DateTimeOffset.Now;
        _logger?.Info("controller.tool.start", new { name });
        try
        {
            var result = await handler(arguments, cancellationToken).ConfigureAwait(false);
            _logger?.Info("controller.tool.complete", new { name, elapsedMs = (DateTimeOffset.Now - startedAt).TotalMilliseconds });
            return result;
        }
        catch (Exception ex)
        {
            _logger?.Error("controller.tool.exception", ex, new { name, elapsedMs = (DateTimeOffset.Now - startedAt).TotalMilliseconds });
            throw;
        }
    }

    public CtuControllerRuntimeStatus Status => new(
        "plugin-default",
        GetType().FullName ?? GetType().Name,
        null,
        null,
        _loadedAt,
        0,
        null,
        _controllerPolicy.Status);

    /// <summary>
    /// Executes one bounded startup/interop sweep over the exchange system channel.
    /// </summary>
    public async Task RunStartupSweepAsync(CancellationToken cancellationToken = default)
    {
        if (!await _startupSweepGate.WaitAsync(0, cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        try
        {
            await ProcessStartupSweepAsync(cancellationToken).ConfigureAwait(false);
            await ProcessResultNotificationRetriesAsync(cancellationToken).ConfigureAwait(false);
            await ProcessContinuityGuardianAsync(cancellationToken).ConfigureAwait(false);
            await ProcessAgentContinuationsAsync(cancellationToken).ConfigureAwait(false);
            await ProcessTaskDeliveryAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.Error("controller.startup_sweep.failed", ex);
            throw;
        }
        finally
        {
            _startupSweepGate.Release();
        }
    }

    private async Task ProcessTaskDeliveryAsync(CancellationToken cancellationToken)
    {
        var bus = new AgentBusStore(_defaultBusRoot);
        var continuity = new ExecutionContinuityStateStore(_defaultBusRoot, _controllerPolicy.Policy.ContinuityStateDirectory);
        continuity.Initialize();
        var now = DateTimeOffset.Now;
        var staleClaimRecoveryDelay = TimeSpan.FromSeconds(_controllerPolicy.Policy.StaleClaimedTaskRecoverySeconds);
        var directDeliveryRetryDelay = TimeSpan.FromSeconds(_controllerPolicy.Policy.StaleClaimedTaskRecoverySeconds);
        var recoverable = bus.ListTasks()
            .Where(task => string.Equals(task.Status, "open", StringComparison.OrdinalIgnoreCase)
                || string.Equals(task.Status, "claimed", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (recoverable.Count == 0)
        {
            return;
        }

        foreach (var stale in recoverable.Where(task =>
            string.Equals(task.Status, "claimed", StringComparison.OrdinalIgnoreCase)
            && IsStaleClaimForDelivery(task, now, staleClaimRecoveryDelay)))
        {
            var recovered = bus.RequeueClaimedTask(stale.Id);

            if (recovered is not null)
            {
                bus.RecordEvent(new AgentBusEvent
                {
                    Timestamp = DateTimeOffset.Now,
                    Type = "task.recovered_claimed",
                    TaskId = stale.Id,
                    From = stale.From,
                    To = stale.To,
                    Message = $"Recovered stale claimed task after {staleClaimRecoveryDelay}."
                });
            }
        }

        var openTasks = bus.ListTasks(status: "open")
            .Where(task => task.Status.Equals("open", StringComparison.OrdinalIgnoreCase))
            .Where(task => ShouldAttemptDirectDelivery(continuity.ReadLatest(task.Id), task, now, directDeliveryRetryDelay))
            .ToList();
        if (openTasks.Count == 0)
        {
            return;
        }

        var grouped = openTasks
            .Where(task => !string.IsNullOrWhiteSpace(task.To))
            .GroupBy(task => task.To!, StringComparer.OrdinalIgnoreCase);

        foreach (var group in grouped)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var ordered = group.OrderBy(task => task.CreatedAt).ToList();
            if (ordered.Count <= 1)
            {
                await TryDeliverQueuedTaskAsync(bus, ordered[0], cancellationToken).ConfigureAwait(false);
                continue;
            }

            var carryThrough = ordered[^1];
            foreach (var stale in ordered.SkipLast(1))
            {
                bus.MarkTaskSuperseded(stale);
                bus.RecordEvent(new AgentBusEvent
                {
                    Timestamp = DateTimeOffset.Now,
                    Type = "task.superseded",
                    TaskId = stale.Id,
                    From = stale.From,
                    To = stale.To,
                    Message = $"Superseded by later queued task {carryThrough.Id}."
                });
            }

            await TryDeliverQueuedTaskAsync(bus, carryThrough, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task TryDeliverQueuedTaskAsync(AgentBusStore bus, AgentBusTask task, CancellationToken cancellationToken)
    {
        task = bus.UpdateTask(task.Id, existing => existing with
        {
            DeliveryAttempts = existing.DeliveryAttempts + 1,
            LastDeliveryAttemptAt = DateTimeOffset.Now,
            LastDeliveryError = null
        }) ?? task;

        try
        {
            var dispatchResult = await DispatchTaskAsync(
                bus,
                _appServer,
                task.Id,
                ControllerWakeupTimeout(),
                cancellationToken,
                recordDeliveryAttempt: false).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                bus.UpdateTask(task.Id, existing => existing with { LastDeliveryError = "delivery canceled" });
                return;
            }

            if (!dispatchResult.Succeeded)
            {
                bus.UpdateTask(task.Id, existing => existing with
                {
                    LastDeliveryError = SafeText.Preview(dispatchResult.Error, 180) ?? "wake-up did not succeed",
                    LastDeliveryAttemptAt = DateTimeOffset.Now
                });
                bus.RecordEvent(new AgentBusEvent
                {
                    Timestamp = DateTimeOffset.Now,
                    Type = "task.delivery_failed",
                    TaskId = task.Id,
                    From = task.From,
                    To = task.To,
                    Message = SafeText.Preview(dispatchResult.Error, 180) ?? "wake-up did not succeed",
                    Payload = new { deferred = dispatchResult.Deferred }
                });
            }

            await Task.Delay(DeliverySweepWait, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            bus.UpdateTask(task.Id, existing => existing with { LastDeliveryError = "delivery canceled" });
            return;
        }
        catch (Exception ex)
        {
            bus.UpdateTask(task.Id, existing => existing with
            {
                LastDeliveryError = SafeText.Redact(ex.Message),
                LastDeliveryAttemptAt = DateTimeOffset.Now
            });
            bus.RecordEvent(new AgentBusEvent
            {
                Timestamp = DateTimeOffset.Now,
                Type = "task.delivery_failed",
                TaskId = task.Id,
                From = task.From,
                To = task.To,
                Message = SafeText.Redact(ex.Message),
                Payload = new
                {
                    exception = SafeText.Redact(ex.GetType().FullName)
                }
            });
        }
    }

    private async Task ProcessResultNotificationRetriesAsync(CancellationToken cancellationToken)
    {
        var bus = new AgentBusStore(_defaultBusRoot);
        var pendingResults = bus.ListResults()
            .Where(result =>
                result.NotifyAttempts > 0 &&
                result.LastNotifiedAt is null &&
                result.NotifyAttempts < MaxResultNotifyRetryAttempts)
            .ToList();

        foreach (var result in pendingResults)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (result.LastNotifyAttemptAt is not null
                && DateTimeOffset.Now - result.LastNotifyAttemptAt < ResultNotificationRetryDelay)
            {
                continue;
            }

            try
            {
                var notify = await AttemptResultNotifyAsync(
                    bus,
                    _appServer,
                    result,
                    optionalResultId: null,
                    requestedThreadId: null,
                    requestedAgentId: null,
                    cwd: null,
                    cancellationToken).ConfigureAwait(false);

                if (notify.Notified)
                {
                    continue;
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger?.Error("controller.result_notify.retry_failed", ex, new { result.Id });
            }
        }
    }

    private async Task ProcessContinuityGuardianAsync(CancellationToken cancellationToken)
    {
        var bus = new AgentBusStore(_defaultBusRoot);
        var continuity = new ExecutionContinuityStateStore(_defaultBusRoot, _controllerPolicy.Policy.ContinuityStateDirectory);
        continuity.Initialize();
        var guardian = ContinuityGuardian();

        await ProcessContinuityActionStatesAsync(bus, continuity, cancellationToken).ConfigureAwait(false);

        var queuedTasks = bus.ListTasks()
            .Where(task => string.Equals(task.Status, "open", StringComparison.OrdinalIgnoreCase)
                || string.Equals(task.Status, "claimed", StringComparison.OrdinalIgnoreCase))
            .ToList();

        foreach (var task in queuedTasks)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                var targetState = EvaluateGuardedStateForTask(task);
                var existing = continuity.ReadLatest(task.Id);
                if (existing is not null
                    && !ExecutionContinuityStateKind.TerminalStates.Contains(existing.State)
                    && !string.Equals(existing.NextActionKind, "task", StringComparison.OrdinalIgnoreCase)
                    && IsContinuityDecisionDispatchState(targetState))
                {
                    continue;
                }

                if (existing is not null
                    && ExecutionContinuityStateKind.TerminalStates.Contains(existing.State)
                    && !existing.ShouldContinue)
                {
                    continue;
                }

                if (IsTerminalRepeatForSameOutcome(existing, targetState, task.Id))
                {
                    continue;
                }

                var nextState = BuildContinuityState(
                    task.Id,
                    existing,
                    targetState,
                    shouldContinue: true,
                    lastOutcomeKind: "queued",
                    lastOutcomeRef: task.Id,
                    lastError: null,
                    guardianAgentId: guardian.id,
                    guardianDisplayName: guardian.displayName,
                    currentTargetAgentId: task.To,
                    currentTargetDisplayName: task.To,
                    blockingOwner: null,
                    blockingReason: null,
                    nextActionKind: "task",
                    nextActionRef: task.Id,
                    resumeCorrelationId: null);

                continuity.Upsert(nextState);
                RecordContinuityGuardianEvaluatedEvent(bus, task.From, task.To, task.Id, existing, nextState, isForResult: false);
                if (IsContinuityDecisionDispatchState(nextState.State))
                {
                    bus.RecordEvent(new AgentBusEvent
                    {
                        Timestamp = DateTimeOffset.Now,
                        Type = IsRepeatStateForDecision(existing, nextState.State)
                            ? "continuity.dispatch_retry_scheduled"
                            : "continuity.dispatch_requested",
                        TaskId = task.Id,
                        From = task.From,
                        To = task.To,
                        Message = $"Dispatch continuity decision for task {task.Id}.",
                        Payload = new { state = nextState.State, attempt = nextState.AttemptCount, continueFlow = nextState.ShouldContinue }
                    });
                }
                RecordContinuityTerminalStateEventIfNeeded(bus, task.From, task.To, task.Id, existing, nextState);
                bus.RecordEvent(new AgentBusEvent
                {
                    Timestamp = DateTimeOffset.Now,
                    Type = "continuity.state_updated",
                    TaskId = task.Id,
                    From = task.From,
                    To = task.To,
                    Message = $"Continuity state set to '{targetState}' for task {task.Id}.",
                    Payload = new { state = nextState.State, shouldContinue = nextState.ShouldContinue, stateId = nextState.StateId }
                });
            }
            catch (Exception ex)
            {
                _logger?.Error("controller.continuity.task_evaluation_failed", ex, new { taskId = task.Id });
            }
        }

        foreach (var result in bus.ListResults())
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            try
            {
                var notifyRetryExhausted = ShouldGuardianTransitionNotifyRetryToBlocked(result);
                var targetState = EvaluateGuardedStateForResult(result, notifyRetryExhausted);
                var existing = continuity.ReadLatest(result.TaskId);
                if (IsTerminalRepeatForSameOutcome(existing, targetState, result.Id))
                {
                    continue;
                }

                if (IsNonTerminalRepeatForSameOutcome(existing, targetState, result.Id))
                {
                    continue;
                }

                var nextState = BuildContinuityState(
                    result.TaskId,
                    existing,
                    targetState,
                    shouldContinue: ShouldContinueFromResultState(targetState),
                    lastOutcomeKind: string.IsNullOrWhiteSpace(result.Status) ? "unknown" : result.Status,
                    lastOutcomeRef: result.Id,
                    lastError: ShouldRecordNotifyRetryError(result, notifyRetryExhausted)
                        ? "result notify retries exhausted"
                        : result.Status.Equals("failed", StringComparison.OrdinalIgnoreCase) ? result.Summary : null,
                    guardianAgentId: guardian.id,
                    guardianDisplayName: guardian.displayName,
                currentTargetAgentId: result.To,
                currentTargetDisplayName: result.To,
                blockingOwner: GetResultBlockingOwner(result, notifyRetryExhausted),
                blockingReason: GetResultBlockingReason(result, notifyRetryExhausted),
                nextActionKind: string.IsNullOrWhiteSpace(result.NextSuggestedAction) ? "result" : "task",
                nextActionRef: result.NextSuggestedAction,
                resumeCorrelationId: null);

                continuity.Upsert(nextState);
                RecordContinuityGuardianEvaluatedEvent(bus, result.From, result.To, result.TaskId, existing, nextState, isForResult: true);
                if (string.Equals(targetState, ExecutionContinuityStateKind.NotifyRetryPending, StringComparison.OrdinalIgnoreCase))
                {
                    bus.RecordEvent(new AgentBusEvent
                    {
                        Timestamp = DateTimeOffset.Now,
                        Type = "continuity.notify_retry_scheduled",
                        TaskId = result.TaskId,
                        From = result.From,
                        To = result.To,
                        Message = $"Result notify continuity decision for result {result.Id}.",
                        Payload = new { state = nextState.State, attempt = nextState.AttemptCount, notifyAttempts = result.NotifyAttempts }
                    });
                }
                RecordContinuityTerminalStateEventIfNeeded(bus, result.From, result.To, result.TaskId, existing, nextState);
                bus.RecordEvent(new AgentBusEvent
                {
                    Timestamp = DateTimeOffset.Now,
                    Type = string.IsNullOrWhiteSpace(existing?.State) ? "continuity.state_created" : "continuity.state_transitioned",
                    TaskId = result.TaskId,
                    From = result.From,
                    To = result.To,
                    Message = $"Continuity transition '{existing?.State ?? "new"}' -> '{targetState}' via result {result.Id}.",
                    Payload = new { state = nextState.State, stateId = nextState.StateId, resultId = result.Id }
                });
            }
            catch (Exception ex)
            {
                _logger?.Error("controller.continuity.result_evaluation_failed", ex, new { resultId = result.Id });
            }
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }

    private static string EvaluateGuardedStateForTask(AgentBusTask task)
    {
        return string.Equals(task.Status, "claimed", StringComparison.OrdinalIgnoreCase)
            ? ExecutionContinuityStateKind.WaitingOnWorker
            : ExecutionContinuityStateKind.QueuedForDispatch;
    }

    private static string EvaluateGuardedStateForResult(AgentBusResult result, bool notifyRetryExhausted)
    {
        if (notifyRetryExhausted)
        {
            return ExecutionContinuityStateKind.BlockedNeedsHuman;
        }

        if (string.Equals(result.Status, "completed", StringComparison.OrdinalIgnoreCase))
        {
            if (result.NotifyAttempts > 0 && result.LastNotifiedAt is null)
            {
                return ExecutionContinuityStateKind.NotifyRetryPending;
            }

            return string.IsNullOrWhiteSpace(result.NextSuggestedAction)
                ? ExecutionContinuityStateKind.Completed
                : ExecutionContinuityStateKind.DelegatedNextTask;
        }

        if (string.Equals(result.Status, "failed", StringComparison.OrdinalIgnoreCase))
        {
            return ExecutionContinuityStateKind.BlockedNeedsHuman;
        }

        return ExecutionContinuityStateKind.PendingReview;
    }

    private static bool ShouldContinueFromResultState(string targetState)
    {
        return !ExecutionContinuityStateKind.TerminalStates.Contains(targetState);
    }

    private (string id, string displayName) ContinuityGuardian()
    {
        var policy = _controllerPolicy.Policy;
        var normalizedId = string.IsNullOrWhiteSpace(policy.ContinuityGuardianAgentId)
            ? "ctu/reviewer"
            : policy.ContinuityGuardianAgentId.Trim();
        return (normalizedId, string.IsNullOrWhiteSpace(policy.ContinuityGuardianDisplayName)
            ? normalizedId
            : policy.ContinuityGuardianDisplayName!.Trim());
    }

    private static bool IsTerminalRepeatForSameOutcome(ExecutionContinuityState? existing, string targetState, string outcomeRef)
    {
        return existing is not null
            && ExecutionContinuityStateKind.TerminalStates.Contains(existing.State)
            && string.Equals(existing.State, targetState, StringComparison.OrdinalIgnoreCase)
            && string.Equals(existing.LastOutcomeRef, outcomeRef, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRepeatStateForDecision(ExecutionContinuityState? existing, string targetState)
    {
        return existing is not null
            && string.Equals(existing.State, targetState, StringComparison.OrdinalIgnoreCase)
            && !ExecutionContinuityStateKind.TerminalStates.Contains(targetState);
    }

    private static bool IsNonTerminalRepeatForSameOutcome(ExecutionContinuityState? existing, string targetState, string outcomeRef)
    {
        return existing is not null
            && !ExecutionContinuityStateKind.TerminalStates.Contains(existing.State)
            && string.Equals(existing.State, targetState, StringComparison.OrdinalIgnoreCase)
            && string.Equals(existing.LastOutcomeRef, outcomeRef, StringComparison.OrdinalIgnoreCase);
    }

    private async Task ProcessContinuityActionStatesAsync(AgentBusStore bus, ExecutionContinuityStateStore continuity, CancellationToken cancellationToken)
    {
        var latestStates = continuity.ListStates()
            .GroupBy(state => state.CorrelationId, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(state => state.UpdatedAt).First())
            .ToList();

        foreach (var state in latestStates)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(state.State)
                && state.ShouldContinue
                && !ExecutionContinuityStateKind.TerminalStates.Contains(state.State))
            {
                try
                {
                    if (string.Equals(state.State, ExecutionContinuityStateKind.QueuedForDispatch, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(state.State, ExecutionContinuityStateKind.DispatchRetryPending, StringComparison.OrdinalIgnoreCase))
                    {
                        await ResumeDispatchFromContinuityStateAsync(bus, continuity, state, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (string.Equals(state.State, ExecutionContinuityStateKind.NotifyRetryPending, StringComparison.OrdinalIgnoreCase))
                    {
                        await ResumeNotifyFromContinuityStateAsync(bus, continuity, state, cancellationToken).ConfigureAwait(false);
                        continue;
                    }

                    if (string.Equals(state.State, ExecutionContinuityStateKind.ResumePendingExternal, StringComparison.OrdinalIgnoreCase))
                    {
                        await ResumeFromExternalCorrelationAsync(bus, continuity, state, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger?.Error("controller.continuity.action_failed", ex, new { correlationId = state.CorrelationId, state = state.State });
                }
            }
        }
    }

    private async Task ResumeDispatchFromContinuityStateAsync(
        AgentBusStore bus,
        ExecutionContinuityStateStore continuity,
        ExecutionContinuityState state,
        CancellationToken cancellationToken)
    {
        var taskId = state.NextActionRef ?? state.CorrelationId;
        if (string.IsNullOrWhiteSpace(taskId))
        {
            RecordContinuitySkipBlocking(
                bus,
                continuity,
                state,
                ExecutionContinuityStateKind.BlockedNeedsHuman,
                string.IsNullOrWhiteSpace(state.CurrentTargetAgentId) ? state.GuardianAgentId ?? "ctu/continuity-guardian" : state.CurrentTargetAgentId,
                $"Continuity dispatch continuation for {state.CorrelationId} could not be resumed because task reference is missing.",
                lastOutcomeKind: "dispatch_orphan",
                lastOutcomeRef: state.CorrelationId,
                lastError: "Missing task reference.");
            return;
        }

        var task = bus.FindTask(taskId);
        if (task is null)
        {
            RecordContinuitySkipBlocking(
                bus,
                continuity,
                state,
                ExecutionContinuityStateKind.BlockedNeedsHuman,
                state.CurrentTargetAgentId,
                $"Continuity dispatch continuation for {state.CorrelationId} could not be resumed because task {taskId} was not found.",
                lastOutcomeKind: "dispatch_orphan",
                lastOutcomeRef: taskId,
                lastError: $"Task {taskId} was not found.");
            return;
        }

        if (string.Equals(task.Status, "claimed", StringComparison.OrdinalIgnoreCase))
        {
            RecordContinuityDispatchSatisfied(
                bus,
                continuity,
                state,
                task,
                ExecutionContinuityStateKind.WaitingOnWorker,
                "task_claimed",
                "Task is already claimed by the target worker.");
            return;
        }

        if (string.Equals(task.Status, "done", StringComparison.OrdinalIgnoreCase))
        {
            RecordContinuityDispatchSatisfied(
                bus,
                continuity,
                state,
                task,
                ExecutionContinuityStateKind.Completed,
                "task_done",
                "Task is already done.");
            return;
        }

        if (task.LastDeliveryAttemptAt is not null
            && task.DeliveryAttempts > 0
            && string.IsNullOrWhiteSpace(task.LastDeliveryError))
        {
            RecordContinuityDispatchSatisfied(
                bus,
                continuity,
                state,
                task,
                ExecutionContinuityStateKind.WaitingOnWorker,
                "task_dispatched",
                "Task already has a successful delivery attempt.");
            return;
        }

        bus.RecordEvent(new AgentBusEvent
        {
            Timestamp = DateTimeOffset.Now,
            Type = "continuity.dispatch_from_state",
            TaskId = task.Id,
            From = task.From,
            To = task.To,
            Message = $"Dispatch continuation from continuity state '{state.State}' for task {task.Id}.",
            Payload = new { state = state.State, stateId = state.StateId, actionAttempt = state.AttemptCount }
        });

        var dispatch = await DispatchTaskAsync(bus, _appServer, task.Id, ControllerWakeupTimeout(), cancellationToken).ConfigureAwait(false);
        if (!dispatch.Succeeded)
        {
            bus.RecordEvent(new AgentBusEvent
            {
                Timestamp = DateTimeOffset.Now,
                Type = "continuity.dispatch_from_state_failed",
                TaskId = task.Id,
                From = task.From,
                To = task.To,
                Message = $"Dispatch continuation from continuity state '{state.State}' failed for task {task.Id}.",
                Payload = new { stateId = state.StateId, dispatch.Error }
            });
        }
    }

    private void RecordContinuityDispatchSatisfied(
        AgentBusStore bus,
        ExecutionContinuityStateStore continuity,
        ExecutionContinuityState state,
        AgentBusTask task,
        string nextState,
        string lastOutcomeKind,
        string message)
    {
        var terminalState = BuildContinuityState(
            task.Id,
            state,
            nextState,
            shouldContinue: false,
            lastOutcomeKind,
            task.Id,
            lastError: null,
            guardianAgentId: state.GuardianAgentId,
            guardianDisplayName: state.GuardianDisplayName,
            currentTargetAgentId: task.To,
            currentTargetDisplayName: task.To,
            blockingOwner: task.To,
            blockingReason: message,
            nextActionKind: null,
            nextActionRef: null,
            resumeCorrelationId: null);
        continuity.Upsert(terminalState);
        bus.RecordEvent(new AgentBusEvent
        {
            Timestamp = DateTimeOffset.Now,
            Type = "continuity.dispatch_satisfied",
            TaskId = task.Id,
            From = task.From,
            To = task.To,
            Message = $"Dispatch continuity satisfied for task {task.Id}: {message}",
            Payload = new { state = terminalState.State, stateId = terminalState.StateId, taskStatus = task.Status }
        });
    }

    private async Task ResumeNotifyFromContinuityStateAsync(
        AgentBusStore bus,
        ExecutionContinuityStateStore continuity,
        ExecutionContinuityState state,
        CancellationToken cancellationToken)
    {
        var resultId = state.LastOutcomeRef;
        if (string.IsNullOrWhiteSpace(resultId))
        {
            RecordContinuitySkipBlocking(
                bus,
                continuity,
                state,
                ExecutionContinuityStateKind.BlockedNeedsHuman,
                string.IsNullOrWhiteSpace(state.CurrentTargetAgentId) ? state.GuardianAgentId ?? "ctu/continuity-guardian" : state.CurrentTargetAgentId,
                $"Continuity notify continuation for {state.CorrelationId} could not be resumed because result reference is missing.",
                lastOutcomeKind: "notify_orphan",
                lastOutcomeRef: state.CorrelationId,
                lastError: "Missing result reference.");
            return;
        }

        var result = bus.FindResult(resultId);
        if (result is null)
        {
            RecordContinuitySkipBlocking(
                bus,
                continuity,
                state,
                ExecutionContinuityStateKind.BlockedNeedsHuman,
                state.CurrentTargetAgentId,
                $"Continuity notify continuation for {state.CorrelationId} could not be resumed because result {resultId} was not found.",
                lastOutcomeKind: "notify_orphan",
                lastOutcomeRef: resultId,
                lastError: $"Result {resultId} was not found.");
            return;
        }

        if (result.LastNotifiedAt is not null)
        {
            var terminalState = BuildContinuityState(
                state.CorrelationId,
                state,
                ExecutionContinuityStateKind.Completed,
                shouldContinue: false,
                lastOutcomeKind: "completed",
                lastOutcomeRef: result.Id,
                lastError: null,
                guardianAgentId: state.GuardianAgentId,
                guardianDisplayName: state.GuardianDisplayName,
                currentTargetAgentId: result.To,
                currentTargetDisplayName: result.To,
                blockingOwner: null,
                blockingReason: null,
                nextActionKind: state.NextActionKind,
                nextActionRef: state.NextActionRef,
                resumeCorrelationId: state.ResumeCorrelationId);

            continuity.Upsert(terminalState);
            RecordContinuityTerminalStateEventIfNeeded(bus, result.From, result.To, state.CorrelationId, state, terminalState);
            bus.RecordEvent(new AgentBusEvent
            {
                Timestamp = DateTimeOffset.Now,
                Type = "continuity.notify_resume_skipped",
                TaskId = state.CorrelationId,
                From = result.From,
                To = result.To,
                Message = $"Skipping continuity notify continuation for {state.CorrelationId}: result {resultId} already notified."
            });
            return;
        }

        var targetAgentId = string.IsNullOrWhiteSpace(state.CurrentTargetAgentId) ? result.To : state.CurrentTargetAgentId;

        bus.RecordEvent(new AgentBusEvent
        {
            Timestamp = DateTimeOffset.Now,
            Type = "continuity.notify_from_state",
            TaskId = state.CorrelationId,
            From = result.From,
            To = targetAgentId,
            Message = $"Notify continuation from continuity state '{state.State}' for result {result.Id}.",
            Payload = new { state = state.State, stateId = state.StateId, resultId, actionAttempt = state.AttemptCount }
        });

        var notify = await AttemptResultNotifyAsync(
            bus,
            _appServer,
            result,
            optionalResultId: resultId,
            requestedThreadId: null,
            requestedAgentId: targetAgentId,
            cwd: null,
            cancellationToken).ConfigureAwait(false);

        if (!notify.Notified)
        {
            bus.RecordEvent(new AgentBusEvent
            {
                Timestamp = DateTimeOffset.Now,
                Type = "continuity.notify_from_state_pending",
                TaskId = state.CorrelationId,
                From = result.From,
                To = targetAgentId,
                Message = $"Continuity notify continuation for result {result.Id} is pending.",
                Payload = new { state = state.State, stateId = state.StateId, error = notify.Error, deferred = notify.Deferred }
            });
        }
    }

    private async Task ResumeFromExternalCorrelationAsync(
        AgentBusStore bus,
        ExecutionContinuityStateStore continuity,
        ExecutionContinuityState state,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(state.ResumeCorrelationId))
        {
            RecordContinuitySkipBlocking(
                bus,
                continuity,
                state,
                ExecutionContinuityStateKind.BlockedNeedsHuman,
                state.CurrentTargetAgentId,
                $"Continuity resume pending external for {state.CorrelationId} could not be resolved because correlation is missing.",
                lastOutcomeKind: "resume_orphan",
                lastOutcomeRef: state.CorrelationId,
                lastError: "Missing resume correlation id.");
            return;
        }

        var task = bus.FindTask(state.ResumeCorrelationId);
        if (task is not null)
        {
            var resumeTaskState = state with
            {
                NextActionRef = task.Id,
                CurrentTargetAgentId = task.To,
                CurrentTargetDisplayName = task.To
            };
            await ResumeDispatchFromContinuityStateAsync(bus, continuity, resumeTaskState, cancellationToken).ConfigureAwait(false);
            return;
        }

        var result = bus.FindResult(state.ResumeCorrelationId);
        if (result is not null)
        {
            if (result.LastNotifiedAt is not null)
            {
                var terminalState = BuildContinuityState(
                    state.CorrelationId,
                    state,
                    ExecutionContinuityStateKind.Completed,
                    shouldContinue: false,
                    lastOutcomeKind: "completed",
                    lastOutcomeRef: result.Id,
                    lastError: null,
                    guardianAgentId: state.GuardianAgentId,
                    guardianDisplayName: state.GuardianDisplayName,
                    currentTargetAgentId: result.To,
                    currentTargetDisplayName: result.To,
                    blockingOwner: null,
                    blockingReason: null,
                    nextActionKind: state.NextActionKind,
                    nextActionRef: state.NextActionRef,
                    resumeCorrelationId: state.ResumeCorrelationId);

                continuity.Upsert(terminalState);
                RecordContinuityTerminalStateEventIfNeeded(bus, result.From, result.To, state.CorrelationId, state, terminalState);
                bus.RecordEvent(new AgentBusEvent
                {
                    Timestamp = DateTimeOffset.Now,
                    Type = "continuity.resume_pending_external_completed",
                    TaskId = state.CorrelationId,
                    From = result.From,
                    To = result.To,
                    Message = $"Continuity resume pending external for {state.CorrelationId} resolved as already completed via result {result.Id}."
                });
                return;
            }

            var resumeResultState = state with { LastOutcomeRef = result.Id };
            await ResumeNotifyFromContinuityStateAsync(bus, continuity, resumeResultState, cancellationToken).ConfigureAwait(false);
            return;
        }

        RecordContinuitySkipBlocking(
            bus,
            continuity,
            state,
            ExecutionContinuityStateKind.BlockedNeedsHuman,
            state.CurrentTargetAgentId,
            $"Continuity resume pending external for {state.CorrelationId} could not be resolved because correlation {state.ResumeCorrelationId} was not found.",
            lastOutcomeKind: "resume_orphan",
            lastOutcomeRef: state.CorrelationId,
            lastError: $"Resume correlation {state.ResumeCorrelationId} was not found.");
    }

    private static void RecordContinuitySkipBlocking(
        AgentBusStore bus,
        ExecutionContinuityStateStore continuity,
        ExecutionContinuityState state,
        string targetState,
        string? blockingOwner,
        string blockingReason,
        string? lastOutcomeKind,
        string? lastOutcomeRef,
        string? lastError)
    {
        var terminalState = BuildContinuityState(
            state.CorrelationId,
            state,
            targetState,
            shouldContinue: false,
            lastOutcomeKind: lastOutcomeKind,
            lastOutcomeRef: lastOutcomeRef,
            lastError: lastError,
            guardianAgentId: state.GuardianAgentId,
            guardianDisplayName: state.GuardianDisplayName,
            currentTargetAgentId: state.CurrentTargetAgentId,
            currentTargetDisplayName: state.CurrentTargetDisplayName,
            blockingOwner: blockingOwner,
            blockingReason: blockingReason,
            nextActionKind: state.NextActionKind,
            nextActionRef: state.NextActionRef,
            resumeCorrelationId: state.ResumeCorrelationId);

        continuity.Upsert(terminalState);
        RecordContinuityTerminalStateEventIfNeeded(bus, state.CurrentTargetAgentId, null, state.CorrelationId, state, terminalState);
        bus.RecordEvent(new AgentBusEvent
        {
            Timestamp = DateTimeOffset.Now,
            Type = "continuity.state_terminalized",
            TaskId = state.CorrelationId,
            Message = $"Continuity state '{state.State}' for {state.CorrelationId} was terminalized to '{targetState}'.",
            Payload = new
            {
                fromState = state.State,
                blockingOwner,
                blockingReason,
                lastError
            }
        });
    }

    private static bool IsContinuityDecisionDispatchState(string targetState)
    {
        return string.Equals(targetState, ExecutionContinuityStateKind.QueuedForDispatch, StringComparison.OrdinalIgnoreCase)
            || string.Equals(targetState, ExecutionContinuityStateKind.WaitingOnWorker, StringComparison.OrdinalIgnoreCase)
            || string.Equals(targetState, ExecutionContinuityStateKind.DispatchRetryPending, StringComparison.OrdinalIgnoreCase);
    }

    private static bool ShouldGuardianTransitionNotifyRetryToBlocked(AgentBusResult result)
    {
        return result.NotifyAttempts >= MaxResultNotifyRetryAttempts && result.LastNotifiedAt is null;
    }

    private static bool ShouldRecordNotifyRetryError(AgentBusResult result, bool notifyRetryExhausted)
    {
        return notifyRetryExhausted && result.NotifyAttempts > 0;
    }

    private static string? GetResultBlockingOwner(AgentBusResult result, bool notifyRetryExhausted)
    {
        return notifyRetryExhausted
            ? result.To
            : result.Status.Equals("failed", StringComparison.OrdinalIgnoreCase) ? result.To : null;
    }

    private static string? GetResultBlockingReason(AgentBusResult result, bool notifyRetryExhausted)
    {
        return notifyRetryExhausted
            ? "Result notify retries exhausted without successful delivery."
            : result.Status.Equals("failed", StringComparison.OrdinalIgnoreCase) ? result.Summary : null;
    }

    private static void RecordContinuityGuardianEvaluatedEvent(
        AgentBusStore bus,
        string? from,
        string? to,
        string taskId,
        ExecutionContinuityState? existing,
        ExecutionContinuityState nextState,
        bool isForResult)
    {
        bus.RecordEvent(new AgentBusEvent
        {
            Timestamp = DateTimeOffset.Now,
            Type = "continuity.guardian_evaluated",
            TaskId = taskId,
            From = from,
            To = to,
            Message = isForResult
                ? $"Result continuity evaluated to '{nextState.State}' for outcome {nextState.LastOutcomeRef}."
                : $"Task continuity evaluated to '{nextState.State}' for task {nextState.LastOutcomeRef}.",
            Payload = new
            {
                previousState = existing?.State,
                nextState = nextState.State,
                shouldContinue = nextState.ShouldContinue,
                attempt = nextState.AttemptCount,
                repeat = IsRepeatStateForDecision(existing, nextState.State)
            }
        });
    }

    private static void RecordContinuityTerminalStateEventIfNeeded(
        AgentBusStore bus,
        string? from,
        string? to,
        string taskId,
        ExecutionContinuityState? existing,
        ExecutionContinuityState nextState)
    {
        if (!ExecutionContinuityStateKind.TerminalStates.Contains(nextState.State)
            || string.Equals(existing?.State, nextState.State, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        bus.RecordEvent(new AgentBusEvent
        {
            Timestamp = DateTimeOffset.Now,
            Type = "continuity.terminal_recorded",
            TaskId = taskId,
            From = from,
            To = to,
            Message = $"Continuity reached terminal state '{nextState.State}'.",
            Payload = new { nextState.State, lastOutcomeRef = nextState.LastOutcomeRef }
        });
    }

    private static ExecutionContinuityState BuildContinuityState(
        string taskId,
        ExecutionContinuityState? existing,
        string targetState,
        bool shouldContinue,
        string lastOutcomeKind,
        string lastOutcomeRef,
        string? lastError,
        string? guardianAgentId,
        string? guardianDisplayName,
        string? currentTargetAgentId,
        string? currentTargetDisplayName,
        string? blockingOwner,
        string? blockingReason,
        string? nextActionKind,
        string? nextActionRef,
        string? resumeCorrelationId)
    {
        var now = DateTimeOffset.UtcNow;
        var sameState = existing is not null
            && string.IsNullOrWhiteSpace(existing.State) == false
            && string.Equals(existing.State, targetState, StringComparison.OrdinalIgnoreCase);
        var attemptCount = sameState && !ExecutionContinuityStateKind.TerminalStates.Contains(targetState)
            ? existing!.AttemptCount + 1
            : sameState
                ? existing!.AttemptCount
                : (existing?.AttemptCount ?? 0) + 1;

        return new ExecutionContinuityState
        {
            StateId = sameState ? existing!.StateId : Guid.NewGuid().ToString("N")[..30],
            CorrelationId = taskId,
            InitiativeId = null,
            TaskChainId = taskId,
            ShouldContinue = shouldContinue,
            State = targetState,
            EnteredAt = sameState ? existing!.EnteredAt : now,
            UpdatedAt = now,
            GuardianAgentId = guardianAgentId,
            GuardianDisplayName = guardianDisplayName,
            CurrentOwner = guardianAgentId,
            NextAction = nextActionKind,
            LastOutcomeKind = lastOutcomeKind,
            LastOutcomeRef = lastOutcomeRef,
            NextActionKind = nextActionKind,
            NextActionRef = nextActionRef,
            CurrentTargetAgentId = currentTargetAgentId,
            CurrentTargetDisplayName = currentTargetDisplayName,
            AttemptCount = attemptCount,
            MaxAttempts = 8,
            LastAttemptAt = now,
            AttemptMetadata = new ExecutionContinuityAttemptMetadata
            {
                Attempt = attemptCount,
                MaxAttempts = 8,
                LastAttemptAt = now,
                FailureReason = lastError
            },
            LastError = lastError,
            BlockingOwner = blockingOwner,
            BlockingReason = blockingReason,
            ResumeCorrelationId = resumeCorrelationId,
            SupersedesStateId = sameState ? existing?.SupersedesStateId : existing?.StateId
        };
    }

    private static bool IsStaleClaimForDelivery(AgentBusTask task, DateTimeOffset now, TimeSpan recoveryDelay)
    {
        return task.ClaimedAt.HasValue
            && now - task.ClaimedAt.Value >= recoveryDelay;
    }

    private static bool ShouldAttemptDirectDelivery(
        ExecutionContinuityState? state,
        AgentBusTask task,
        DateTimeOffset now,
        TimeSpan retryDelay)
    {
        if (state is null)
        {
            return ShouldRetryDirectDelivery(task, now, retryDelay);
        }

        if (ExecutionContinuityStateKind.TerminalStates.Contains(state.State))
        {
            return false;
        }

        if (state.ShouldContinue && !IsContinuityDecisionDispatchState(state.State))
        {
            return false;
        }

        return ShouldRetryDirectDelivery(task, now, retryDelay);
    }

    private static bool ShouldRetryDirectDelivery(AgentBusTask task, DateTimeOffset now, TimeSpan retryDelay)
    {
        if (task.LastDeliveryAttemptAt is null || task.DeliveryAttempts == 0)
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(task.LastDeliveryError))
        {
            return true;
        }

        return now - task.LastDeliveryAttemptAt.Value >= retryDelay;
    }

    private async Task ProcessAgentContinuationsAsync(CancellationToken cancellationToken)
    {
        var bus = new AgentBusStore(_defaultBusRoot);
        var due = bus.ListContinuations(status: "open")
            .Where(continuation => continuation.DueAt <= DateTimeOffset.Now)
            .OrderBy(continuation => continuation.DueAt)
            .ToList();

        foreach (var continuation in due)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            if (HasOpenOrClaimedTaskFor(bus, continuation.Owner))
            {
                continue;
            }

            if (continuation.Attempts >= continuation.MaxAttempts)
            {
                bus.CompleteContinuation(continuation.Id, "failed", "Continuation retry limit reached.");
                continue;
            }

            var agent = bus.FindAgent(continuation.Owner);
            if (agent is null || string.IsNullOrWhiteSpace(agent.ThreadId))
            {
                RecordContinuationWakeFailure(bus, continuation, $"Agent {continuation.Owner} is not registered with a visible thread.");
                continue;
            }

            var thread = await FindAgentThreadAsync(_appServer, agent, cancellationToken).ConfigureAwait(false);
            if (thread is null)
            {
                RecordContinuationWakeFailure(bus, continuation, $"Agent {continuation.Owner} thread is not visible.");
                continue;
            }

            if (IsBusyThreadStatus(thread.Status))
            {
                bus.RecordEvent(new AgentBusEvent
                {
                    Timestamp = DateTimeOffset.Now,
                    Type = "continuation.wakeup_skipped",
                    TaskId = continuation.TaskId,
                    ResultId = continuation.ResultId,
                    To = continuation.Owner,
                    Message = $"Continuation owner is busy ({thread.Status}).",
                    Payload = new { continuationId = continuation.Id }
                });
                continue;
            }

            var cwd = agent.Cwd ?? CtuProjectLayout.ProjectRootForBusRoot(_defaultBusRoot);
            var task = bus.CreateTask(
                "ctu/continuation",
                continuation.Owner,
                $"Self-continuation: {continuation.Owner}",
                BuildAgentContinuationPrompt(continuation),
                new DirectoryInfo(cwd).Name,
                cwd,
                [],
                continuation.ReturnTo ?? agent.ReturnTo ?? DefaultArchitectFor(continuation.Owner));
            bus.UpdateContinuation(continuation.Id, existing => existing with
            {
                Attempts = existing.Attempts + 1,
                LastWakeAttemptAt = DateTimeOffset.Now,
                LastWakeTaskId = task.Id,
                LastWakeError = null
            });
            bus.RecordEvent(new AgentBusEvent
            {
                Timestamp = DateTimeOffset.Now,
                Type = "continuation.wakeup_enqueued",
                TaskId = task.Id,
                ResultId = continuation.ResultId,
                From = "ctu/continuation",
                To = continuation.Owner,
                Message = $"Self-continuation wakeup task created for {continuation.Owner}.",
                Payload = new { continuationId = continuation.Id, continuation.DedupeKey }
            });

            await TryDeliverQueuedTaskAsync(bus, task, cancellationToken).ConfigureAwait(false);
            bus.CompleteContinuation(continuation.Id, "done", lastWakeTaskId: task.Id);
        }
    }

    private static void RecordContinuationWakeFailure(AgentBusStore bus, AgentBusContinuation continuation, string error)
    {
        var updated = bus.UpdateContinuation(continuation.Id, existing => existing with
        {
            Attempts = existing.Attempts + 1,
            LastWakeAttemptAt = DateTimeOffset.Now,
            LastWakeError = error
        }) ?? continuation;
        bus.RecordEvent(new AgentBusEvent
        {
            Timestamp = DateTimeOffset.Now,
            Type = "continuation.wakeup_failed",
            TaskId = continuation.TaskId,
            ResultId = continuation.ResultId,
            To = continuation.Owner,
            Message = error,
            Payload = new { continuationId = continuation.Id, attempt = updated.Attempts }
        });

        if (updated.Attempts >= updated.MaxAttempts)
        {
            bus.CompleteContinuation(updated.Id, "failed", error);
        }
    }

    private static string BuildAgentContinuationPrompt(AgentBusContinuation continuation)
    {
        return $"""
        Self-continuation for {continuation.Owner}.

        Previous task:
        {continuation.TaskId}

        Previous result:
        {continuation.ResultId}

        Reason:
        {continuation.Reason ?? "The previous result requested that this agent continue later."}

        Claim this task and continue the same work chain. When you stop, write exactly one result with an explicit outcome:
        - done: you are finished and no wakeup is needed.
        - handed_off: you delegated to another agent and no self-wakeup is needed.
        - self_continue: you still need to continue later; include continuation owner, reason, dedupe key, wake delay, and max attempts.
        - human: you need a human decision.
        - failed: a technical blocker prevents progress.
        """;
    }

    private static bool HasOpenOrClaimedTaskFor(AgentBusStore bus, string agentId)
    {
        return bus.ListTasks(agentId, "open").Count > 0
            || bus.ListTasks(agentId, "claimed").Count > 0;
    }

    private static async Task<CodexThreadRecord?> FindAgentThreadAsync(
        IAppServerClient appServer,
        AgentDefinition agent,
        CancellationToken cancellationToken)
    {
        var list = await appServer.ListThreadsAsync(agent.Cwd, 100, cancellationToken).ConfigureAwait(false);
        if (!list.Succeeded || string.IsNullOrWhiteSpace(list.ResultJson))
        {
            return null;
        }

        var threads = AppServerThreadMapper.ParseListResult(list.ResultJson);
        return threads.FirstOrDefault(thread =>
            string.Equals(thread.Id, agent.ThreadId, StringComparison.OrdinalIgnoreCase)
            || string.Equals(thread.Name, agent.DisplayName, StringComparison.OrdinalIgnoreCase)
            || string.Equals(thread.Name, agent.Id, StringComparison.OrdinalIgnoreCase));
    }

    private void RegisterDefaultTools(string busRoot, IAppServerClient appServer)
    {
        var registry = this;

        registry.Register("agentbus_init", (args, _) =>
        {
            var bus = Bus(args, busRoot);
            bus.Initialize();
            return Task.FromResult<object>(new { busRoot = bus.RootDirectory });
        });

        registry.Register("agentbus_list_agents", (args, _) =>
        {
            var bus = Bus(args, busRoot);
            return Task.FromResult<object>(new { agents = bus.ListAgents() });
        });

        registry.Register("agentbus_register_agent", (args, _) =>
        {
            var bus = Bus(args, busRoot);
            var agent = new AgentDefinition
            {
                Id = Required(args, "id"),
                Role = Optional(args, "role") ?? Required(args, "id"),
                DisplayName = Optional(args, "displayName") ?? Optional(args, "chatName") ?? Required(args, "id"),
                ThreadId = Optional(args, "threadId"),
                Cwd = Optional(args, "cwd"),
                AllowedPaths = Csv(args, "allowedPaths"),
                InstructionFiles = Csv(args, "instructionFiles"),
                ReturnTo = Optional(args, "returnTo"),
                Model = Optional(args, "model"),
                ReasoningEffort = NormalizeReasoningEffort(Optional(args, "reasoningEffort") ?? Optional(args, "effort")),
                Speed = NormalizeSpeed(Optional(args, "speed")),
                Status = Optional(args, "status") ?? "active"
            };
            return Task.FromResult<object>(new { agent = bus.RegisterAgent(ApplyAgentRuntimeDefaults(agent, bus.FindAgent(agent.Id))) });
        });

        registry.Register("agentbus_create_task", (args, _) =>
        {
            var bus = Bus(args, busRoot);
            var cwd = Optional(args, "cwd") ?? Environment.CurrentDirectory;
            var task = bus.CreateTask(
                Required(args, "from"),
                Required(args, "to"),
                Required(args, "title"),
                Required(args, "prompt"),
                Optional(args, "project") ?? new DirectoryInfo(cwd).Name,
                cwd,
                Csv(args, "allowedPaths"),
                Optional(args, "returnTo"));
            return Task.FromResult<object>(new { task });
        });

        registry.Register("agentbus_list_tasks", (args, _) =>
        {
            var bus = Bus(args, busRoot);
            return Task.FromResult<object>(new { tasks = bus.ListTasks(Optional(args, "to"), Optional(args, "status")) });
        });

        registry.Register("agentbus_clear_tasks", (args, _) =>
        {
            if (!string.Equals(Optional(args, "confirm"), "DELETE", StringComparison.Ordinal))
            {
                throw new ArgumentException("agentbus_clear_tasks requires confirm=DELETE.");
            }

            var bus = Bus(args, busRoot);
            return Task.FromResult<object>(new { reset = bus.ClearTasks(Bool(args, "includeResults")) });
        });

        registry.Register("agentbus_list_events", (args, _) =>
        {
            var bus = Bus(args, busRoot);
            return Task.FromResult<object>(new { events = bus.ListEvents(Int(args, "limit", 500)) });
        });

        registry.Register("agentbus_list_continuations", (args, _) =>
        {
            var bus = Bus(args, busRoot);
            return Task.FromResult<object>(new { continuations = bus.ListContinuations(Optional(args, "owner"), Optional(args, "status")) });
        });

        registry.Register("agentbus_claim_task", (args, _) =>
        {
            var bus = Bus(args, busRoot);
            return Task.FromResult<object>(new { task = bus.ClaimTask(Required(args, "taskId"), Optional(args, "owner")) });
        });

        registry.Register("agentbus_write_result", (args, _) =>
        {
            var bus = Bus(args, busRoot);
            var result = bus.WriteResult(
                Required(args, "taskId"),
                Required(args, "summary"),
                Optional(args, "status") ?? "completed",
                Optional(args, "from"),
                Optional(args, "to"),
                Optional(args, "commit"),
                Csv(args, "tests", "checks"),
                Csv(args, "openQuestions"),
                Csv(args, "changedFiles"),
                Csv(args, "artifacts"),
                Optional(args, "nextSuggestedAction"),
                Optional(args, "outcome"),
                ContinuationRequest(args));
            return Task.FromResult<object>(new { result });
        });

        registry.Register("agentbus_wait_result", (args, ct) =>
        {
            var bus = Bus(args, busRoot);
            var taskId = Required(args, "taskId");
            var policy = _controllerPolicy.Policy;
            var timeoutSeconds = WaitTimeoutSeconds(
                args,
                "timeoutSeconds",
                Math.Min(3, policy.WaitResultTimeoutCapSeconds),
                policy.WaitResultTimeoutCapSeconds);
            var stopwatch = Stopwatch.StartNew();
            var result = WaitForResultSafe(bus, taskId, TimeSpan.FromSeconds(timeoutSeconds), ct);
            stopwatch.Stop();
            return Task.FromResult<object>(new
            {
                taskId,
                completed = result is not null,
                result,
                timeoutSeconds,
                waitedMs = stopwatch.ElapsedMilliseconds
            });
        });

        registry.Register("codex_thread_list", async (args, ct) =>
        {
            var result = await appServer.ListThreadsAsync(Optional(args, "cwd"), Int(args, "limit", 30), ct).ConfigureAwait(false);
            return new { result.Succeeded, result.ResultJson, result.Error };
        });

        registry.Register("codex_thread_read", async (args, ct) =>
        {
            var result = await appServer.ReadThreadAsync(Required(args, "threadId"), Bool(args, "includeTurns"), ct).ConfigureAwait(false);
            return new { result.Succeeded, result.ResultJson, result.Error };
        });

        registry.Register("codex_thread_archive", async (args, ct) =>
        {
            var result = await appServer.CallAsync("thread/archive", new { threadId = Required(args, "threadId") }, ct).ConfigureAwait(false);
            return new { result.Succeeded, result.ResultJson, result.Error };
        });

        registry.Register("codex_turn_start", async (args, ct) =>
        {
            var settings = RuntimeSettings(Optional(args, "model"), Optional(args, "reasoningEffort") ?? Optional(args, "effort"), Optional(args, "speed"), null);
            var result = await appServer.SendTurnAsync(Required(args, "threadId"), Required(args, "message"), Optional(args, "cwd"), settings, ct).ConfigureAwait(false);
            return new { result.Succeeded, result.ResultJson, result.Error };
        });

        registry.Register("codex_appserver_adapter_status", (args, _) =>
        {
            return Task.FromResult<object>(AppServerAdapterStatus(appServer));
        });

        registry.Register("codex_appserver_adapter_reload", (args, _) =>
        {
            if (appServer is not IReloadableAppServerClient reloadable)
            {
                throw new InvalidOperationException($"Configured app-server client {appServer.GetType().FullName} is not reloadable.");
            }

            return Task.FromResult<object>(reloadable.Reload(
                Optional(args, "pluginPath") ?? Optional(args, "path"),
                Optional(args, "pluginType") ?? Optional(args, "type"),
                JsonStringDictionary(args, "options")));
        });

        registry.Register("codex_controller_status", (args, _) =>
        {
            return Task.FromResult<object>(Status);
        });

        registry.Register("codex_controller_reload", (args, _) =>
        {
            var policyPath = Optional(args, "policyPath");
            if (!string.IsNullOrWhiteSpace(policyPath))
            {
                _controllerPolicy.Reload(policyPath);
            }

            return Task.FromResult<object>(Status);
        });

        registry.Register("codex_controller_policy_status", (args, _) =>
        {
            return Task.FromResult<object>(registry._controllerPolicy.Status);
        });

        registry.Register("codex_controller_policy_reload", (args, _) =>
        {
            return Task.FromResult<object>(registry._controllerPolicy.Reload(Optional(args, "policyPath") ?? Optional(args, "path")));
        });

        registry.Register("bridge_dispatch_task", async (args, ct) =>
        {
            var bus = Bus(args, busRoot);
            var taskId = Required(args, "taskId");
            if (Defer(args))
            {
                var task = bus.FindTask(taskId) ?? throw new FileNotFoundException("Task not found.");
                var operationId = NewOperationId("bridge-dispatch-task");
                bus.RecordEvent(new AgentBusEvent
                {
                    Timestamp = DateTimeOffset.Now,
                    Type = "task.dispatch_accepted",
                    TaskId = task.Id,
                    From = task.From,
                    To = task.To,
                    Message = $"Accepted deferred dispatch for {task.Id}.",
                    Payload = new { operationId }
                });
                _ = RunDeferredAsync(operationId, "bridge.dispatch_task", async () =>
                {
                    await DispatchTaskAsync(bus, appServer, taskId, registry.ControllerWakeupTimeout(), CancellationToken.None).ConfigureAwait(false);
                });

                return new { accepted = true, deferred = true, operationId, taskId };
            }

            return await DispatchTaskAsync(bus, appServer, taskId, registry.ControllerWakeupTimeout(), ct).ConfigureAwait(false);
        });

        registry.Register("bridge_notify_result", async (args, ct) =>
        {
            var bus = Bus(args, busRoot);
            var busResult = bus.FindResult(Required(args, "resultId")) ?? throw new FileNotFoundException("Result not found.");
            var targetThreadArg = Optional(args, "toThread");
            var targetAgentArg = Optional(args, "toAgent") ?? busResult.To;
            var cwd = Optional(args, "cwd");

            var notify = await AttemptResultNotifyAsync(
                bus,
                appServer,
                busResult,
                optionalResultId: null,
                requestedThreadId: targetThreadArg,
                requestedAgentId: targetAgentArg,
                cwd,
                ct).ConfigureAwait(false);

            return new
            {
                result = notify.Result,
                target = new { agent = notify.TargetAgentId, threadId = notify.TargetThreadId, status = notify.FinalStatus, initialStatus = notify.InitialStatus },
                wakeup = new { notify.ResultJson, notify.Error, notify.TurnId, notify.Deferred, notifyLatencyMs = notify.NotifyLatencyMs, notify.WaitedMs }
            };
        });

        registry.Register("team_discover_agents", async (args, ct) =>
        {
            var bus = Bus(args, busRoot);
            bus.Initialize();
            var agents = Csv(args, "agents");
            if (agents.Count == 0)
            {
                throw new ArgumentException("team_discover_agents requires an explicit agents list. CodexTeamUp does not choose project roles.");
            }

            var cwd = Optional(args, "cwd") ?? Environment.CurrentDirectory;
            var project = Optional(args, "project") ?? new DirectoryInfo(cwd).Name;
            var list = await appServer.ListThreadsAsync(cwd, Int(args, "limit", 100), ct).ConfigureAwait(false);
            if (!list.Succeeded || string.IsNullOrWhiteSpace(list.ResultJson))
            {
                return new { list.Succeeded, list.Error, bindings = Array.Empty<object>() };
            }

            var threads = AppServerThreadMapper.ParseListResult(list.ResultJson);
            var bindings = AgentThreadMatcher.MatchAgents(agents, threads, cwd);
            var registered = bindings.Select(binding => bus.RegisterAgent(new AgentDefinition
            {
                Id = binding.AgentId,
                Role = RoleFromAgentId(binding.AgentId),
                DisplayName = binding.AgentId,
                ThreadId = binding.ThreadId,
                Cwd = cwd,
                ReturnTo = IsArchitect(binding.AgentId) ? null : DefaultArchitectFor(binding.AgentId),
                Model = null,
                ReasoningEffort = DefaultReasoningEffortForSpeed(null),
                Speed = DefaultSpeed,
                Status = binding.ThreadId is null ? "missing-thread" : "active"
            })).ToList();

            bus.RecordEvent(new AgentBusEvent
            {
                Timestamp = DateTimeOffset.Now,
                Type = "team.discovered",
                Message = $"Project {project}: {registered.Count} agents discovered."
            });

            return new { project, bindings, registered };
        });

        registry.Register("team_create_agent", async (args, ct) =>
        {
            var bus = Bus(args, busRoot);
            bus.Initialize();
            var cwd = Optional(args, "cwd") ?? Environment.CurrentDirectory;
            var spec = new TeamAgentSpec(
                Required(args, "id"),
                Optional(args, "role") ?? Required(args, "id"),
                Optional(args, "displayName") ?? Required(args, "id"),
                Csv(args, "allowedPaths"),
                Csv(args, "instructionFiles"),
                Optional(args, "returnTo"),
                Optional(args, "initialPrompt"),
                Optional(args, "model"),
                Optional(args, "reasoningEffort") ?? Optional(args, "effort"),
                Optional(args, "speed"));

            if (Defer(args))
            {
                var operationId = NewOperationId("team-create-agent");
                var prime = !IsExplicitFalse(args, "prime");
                var setName = !IsExplicitFalse(args, "setName");
                bus.RecordEvent(new AgentBusEvent
                {
                    Timestamp = DateTimeOffset.Now,
                    Type = "team.create_agent_accepted",
                    To = spec.Id,
                    Message = $"Accepted deferred create/bind for {spec.Id}.",
                    Payload = new { operationId, spec.Id }
                });
                _ = RunDeferredAsync(operationId, "team.create_agent", async () =>
                {
                    try
                    {
                        var agent = await EnsureOrCreateAgentAsync(bus, appServer, spec, cwd, createMissing: true, prime, setName, registry._controllerPolicy.Policy, CancellationToken.None).ConfigureAwait(false);
                        bus.RecordEvent(new AgentBusEvent
                        {
                            Timestamp = DateTimeOffset.Now,
                            Type = "team.agent_created_deferred",
                            To = spec.Id,
                            Message = $"Deferred create/bind completed for {spec.Id}.",
                            Payload = new { operationId, agent.Id, agent.ThreadId, agent.DisplayName }
                        });
                    }
                    catch (Exception ex)
                    {
                        bus.RecordEvent(new AgentBusEvent
                        {
                            Timestamp = DateTimeOffset.Now,
                            Type = "team.agent_create_failed_deferred",
                            To = spec.Id,
                            Message = SafeText.Redact(ex.Message),
                            Payload = new { operationId, spec.Id }
                        });
                        throw;
                    }
                });

                return new { accepted = true, deferred = true, operationId, agents = new[] { spec.Id } };
            }

            var agent = await EnsureOrCreateAgentAsync(bus, appServer, spec, cwd, createMissing: true, prime: true, setName: !IsExplicitFalse(args, "setName"), registry._controllerPolicy.Policy, ct).ConfigureAwait(false);
            return new { agent };
        });

        registry.Register("team_ensure_agents", async (args, ct) =>
        {
            var bus = Bus(args, busRoot);
            bus.Initialize();
            var cwd = Optional(args, "cwd") ?? Environment.CurrentDirectory;
            var createMissing = !string.Equals(Optional(args, "createMissing"), "false", StringComparison.OrdinalIgnoreCase);
            var prime = !string.Equals(Optional(args, "prime"), "false", StringComparison.OrdinalIgnoreCase);
            var setName = !IsExplicitFalse(args, "setName");
            var specs = ParseTeamAgentSpecs(args).ToList();
            if (specs.Count == 0)
            {
                throw new ArgumentException("team_ensure_agents requires agentsJson or agents.");
            }

            if (Defer(args))
            {
                var operationId = NewOperationId("team-ensure-agents");
                var requestedAgents = specs.Select(spec => spec.Id).ToArray();
                bus.RecordEvent(new AgentBusEvent
                {
                    Timestamp = DateTimeOffset.Now,
                    Type = "team.ensure_accepted",
                    Message = $"Accepted deferred ensure for {requestedAgents.Length} CodexTeamUp agents.",
                    Payload = new { operationId, agents = requestedAgents }
                });
                _ = RunDeferredAsync(operationId, "team.ensure_agents", async () =>
                {
                    try
                    {
                        var registered = new List<AgentDefinition>();
                        foreach (var spec in specs)
                        {
                            registered.Add(await EnsureOrCreateAgentAsync(bus, appServer, spec, cwd, createMissing, prime, setName, registry._controllerPolicy.Policy, CancellationToken.None).ConfigureAwait(false));
                        }

                        bus.RecordEvent(new AgentBusEvent
                        {
                            Timestamp = DateTimeOffset.Now,
                            Type = "team.ensured_deferred",
                            Message = $"Deferred ensure completed for {registered.Count} CodexTeamUp agents.",
                            Payload = new
                            {
                                operationId,
                                agents = registered.Select(agent => new { agent.Id, agent.ThreadId, agent.DisplayName, agent.Status }).ToArray()
                            }
                        });
                    }
                    catch (Exception ex)
                    {
                        bus.RecordEvent(new AgentBusEvent
                        {
                            Timestamp = DateTimeOffset.Now,
                            Type = "team.ensure_failed_deferred",
                            Message = SafeText.Redact(ex.Message),
                            Payload = new { operationId, agents = requestedAgents }
                        });
                        throw;
                    }
                });

                return new { accepted = true, deferred = true, operationId, agents = requestedAgents };
            }

            var registered = new List<AgentDefinition>();
            foreach (var spec in specs)
            {
                registered.Add(await EnsureOrCreateAgentAsync(bus, appServer, spec, cwd, createMissing, prime, setName, registry._controllerPolicy.Policy, ct).ConfigureAwait(false));
            }

            bus.RecordEvent(new AgentBusEvent
            {
                Timestamp = DateTimeOffset.Now,
                Type = "team.ensured",
                Message = $"Ensured {registered.Count} CodexTeamUp agents."
            });

            return new { agents = registered };
        });

        registry.Register("team_send_message", async (args, ct) =>
        {
            var bus = Bus(args, busRoot);
            var from = Required(args, "from");
            var to = Required(args, "to");
            var message = Required(args, "message");
            var title = Optional(args, "title") ?? $"Message from {from} to {to}";
            var cwd = Optional(args, "cwd") ?? Environment.CurrentDirectory;
            var task = bus.CreateTask(
                from,
                to,
                title,
                message,
                Optional(args, "project") ?? new DirectoryInfo(cwd).Name,
                cwd,
                Csv(args, "allowedPaths"),
                Optional(args, "returnTo") ?? from);

            var policy = registry._controllerPolicy.Policy;
            var dispatchMode = Optional(args, "dispatchMode") ?? Optional(args, "dispatch") ?? policy.TeamSendMessageDefaultDispatchMode;
            if (string.Equals(dispatchMode, "enqueue", StringComparison.OrdinalIgnoreCase)
                || string.Equals(dispatchMode, "defer", StringComparison.OrdinalIgnoreCase)
                || string.Equals(dispatchMode, "none", StringComparison.OrdinalIgnoreCase))
            {
                bus.RecordEvent(new AgentBusEvent
                {
                    Timestamp = DateTimeOffset.Now,
                    Type = "team.message.enqueued",
                    TaskId = task.Id,
                    From = from,
                    To = to,
                    Message = title,
                    Payload = new { dispatchMode = "enqueue" }
                });
                return new
                {
                    task,
                    accepted = true,
                    dispatchMode = "enqueue",
                    wakeup = (object?)null,
                    wait = (object?)null
                };
            }

            if (!string.Equals(dispatchMode, "inline", StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("dispatchMode must be enqueue or inline.");
            }

            var agent = await EnsureAgentBoundAsync(bus, appServer, to, cwd, ct).ConfigureAwait(false);

            var wake = BuildTaskWakeMessage(task.Id, to, Optional(args, "returnTo") ?? from);
            var wakeupTimeout = registry.WakeupTimeout(args);
            var wakeup = await SendTurnWhenReadyAsync(appServer, agent.ThreadId!, wake, cwd, RuntimeSettings(agent), wakeupTimeout, ct).ConfigureAwait(false);
            var result = wakeup.Result;
            bus.RecordEvent(new AgentBusEvent
            {
                Timestamp = DateTimeOffset.Now,
                Type = wakeup.Deferred ? "team.message.deferred" : result.Succeeded ? "team.message.sent" : "team.message.failed",
                TaskId = task.Id,
                From = from,
                To = to,
                Message = wakeup.Deferred ? $"{title}; {WakeupEventMessage(wakeup)}" : title,
                Payload = WakeupEventPayload(agent.ThreadId!, wakeup)
            });

            AgentBusResult? waitedResult = null;
            long? waitedMs = null;
            if (Bool(args, "waitResult") && !wakeup.Deferred)
            {
                var timeoutSeconds = WaitTimeoutSeconds(args, "timeoutSeconds", policy.WaitResultTimeoutCapSeconds, policy.WaitResultTimeoutCapSeconds);
                var waitStopwatch = Stopwatch.StartNew();
                waitedResult = WaitForResultSafe(bus, task.Id, TimeSpan.FromSeconds(timeoutSeconds), ct);
                waitStopwatch.Stop();
                waitedMs = waitStopwatch.ElapsedMilliseconds;
                bus.RecordEvent(new AgentBusEvent
                {
                    Timestamp = DateTimeOffset.Now,
                    Type = waitedResult is null ? "team.wait.timeout" : "team.wait.completed",
                    TaskId = task.Id,
                    ResultId = waitedResult?.Id,
                    From = to,
                    To = from,
                    Message = waitedResult is null
                        ? $"timeout after {waitedMs}ms"
                        : $"result observed after {waitedMs}ms"
                });
            }

            var wait = Bool(args, "waitResult") && !wakeup.Deferred
                ? new { completed = waitedResult is not null, waitedMs, result = waitedResult }
                : null;

            return new { task, accepted = true, dispatchMode = "inline", wakeup = new { result.Succeeded, result.ResultJson, result.Error, wakeup.Deferred, wakeup.InitialStatus, wakeup.FinalStatus }, wait };
        });

        registry.Register("team_dashboard_export", (args, _) =>
        {
            var bus = Bus(args, busRoot);
            var path = AgentBusDashboard.Export(bus, Optional(args, "outputPath"));
            return Task.FromResult<object>(new { path });
        });

        registry.Register("ctu_restart_request", (args, _) =>
        {
            var bus = Bus(args, busRoot);
            var sourceBusRoot = bus.RootDirectory;
            var sourceCwd = RestartOperationStore.NormalizeCheckoutPath(
                Optional(args, "sourceCwd")
                ?? Environment.CurrentDirectory);
            var targetCwd = RestartOperationStore.NormalizeCheckoutPath(Required(args, "targetCwd"));
            var targetBusRoot = ResolveCheckoutBusRoot(targetCwd, Optional(args, "targetBusRoot"));
            var fallbackCwd = Optional(args, "fallbackCwd") is { } fallback
                ? RestartOperationStore.NormalizeCheckoutPath(fallback)
                : null;
            var fallbackBusRoot = string.IsNullOrWhiteSpace(fallbackCwd)
                ? null
                : ResolveCheckoutBusRoot(
                    fallbackCwd,
                    Optional(args, "fallbackBusRoot"));
            var knownGood = new KnownGoodRuntimeCheckpointStore(sourceBusRoot).ReadVerified();

            var operationStore = new RestartOperationStore(sourceBusRoot);
            var operation = operationStore.Create(
                BlankToNull(Optional(args, "requestedByAgentId")) ?? "ctu/architect",
                sourceCwd,
                sourceBusRoot,
                targetCwd,
                targetBusRoot,
                Required(args, "targetAgentId"),
                fallbackCwd,
                fallbackBusRoot,
                Optional(args, "continueTitle") ?? "Continue after restart",
                Optional(args, "continuePrompt") ?? $"{Required(args, "targetAgentId")} requested a checkpointed restart into this checkout.\nPlease continue the active task from the source context.",
                Optional(args, "expectedTargetBranch"),
                knownGood?.Id);
            var operationPath = operationStore.OperationPath(operation.Id);
            try
            {
                var helperProcess = LaunchRestartHelper(sourceCwd, operationPath);
                operation = operationStore.UpdateStatus(operation, RestartOperationStatus.HelperStarted, helperPid: helperProcess.Id.ToString());
                operationStore.Write(operation);
                return Task.FromResult<object>(new
                {
                    accepted = true,
                    operationId = operation.Id,
                    operationPath,
                    status = operation.Status,
                    request = new
                    {
                        operation.Kind,
                        operation.SourceCwd,
                        operation.TargetCwd
                    }
                });
            }
            catch (Exception ex)
            {
                operation = operationStore.UpdateStatus(operation, RestartOperationStatus.Failed, lastError: ex.Message);
                operationStore.Write(operation);
                throw;
            }
        });

        registry.Register("ctu_restart_status", (args, _) =>
        {
            var operationPath = Optional(args, "operationPath");
            var operationId = Optional(args, "operationId");
            if (string.IsNullOrWhiteSpace(operationPath) && string.IsNullOrWhiteSpace(operationId))
            {
                throw new ArgumentException("Missing operationPath or operationId.");
            }

            RestartOperationRecord? operation = null;
            var opStore = new RestartOperationStore(Bus(args, busRoot).RootDirectory);
            if (!string.IsNullOrWhiteSpace(operationPath))
            {
                var resolvedPath = Path.GetFullPath(operationPath);
                operation = opStore.FindByPath(resolvedPath);
                if (operation is null)
                {
                    throw new FileNotFoundException($"Operation record was not found: {resolvedPath}");
                }
            }
            else
            {
                operation = opStore.Find(operationId!);
            }

            if (operation is null)
            {
                throw new FileNotFoundException($"Restart operation not found.");
            }

            return Task.FromResult<object>(new { operation });
        });

        return;
    }

    private async Task ProcessStartupSweepAsync(CancellationToken cancellationToken)
    {
        var exchange = new ExchangeStore(_defaultBusRoot);
        var messages = exchange.ListPendingSystemMessages(ExchangeEnvelopeKind.Restart, Math.Max(1, _controllerPolicy.Policy.WaitResultTimeoutCapSeconds));
        if (messages.Count == 0)
        {
            return;
        }

        foreach (var (path, envelope) in messages)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            using var lease = exchange.TryAcquireLease(path, "ctu-controller", StartupSweepLeaseDuration);
            if (lease is null)
            {
                continue;
            }

            try
            {
                if (!string.Equals(envelope.Kind, ExchangeEnvelopeKind.Restart, StringComparison.OrdinalIgnoreCase))
                {
                    exchange.Requeue(path, lease.Envelope, $"Unsupported envelope kind '{envelope.Kind}'.");
                    continue;
                }

                await ImportRestartStartupHandoffAsync(exchange, lease, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                exchange.DeadLetter(path, lease.Envelope, ex.Message);
                _logger?.Error("controller.startup_sweep.message_failed", ex, new { envelope.MessageId, operationPath = ExtractPayloadString(lease.Envelope, "operationPath") });
            }
        }
    }

    private async Task ImportRestartStartupHandoffAsync(ExchangeStore exchange, ExchangeStore.LeaseHandle lease, CancellationToken cancellationToken)
    {
        var operationPath = ExtractPayloadString(lease.Envelope, "operationPath");
        if (string.IsNullOrWhiteSpace(operationPath))
        {
            throw new InvalidOperationException("Restart envelope is missing operationPath.");
        }

        var sourceOperationPath = Path.GetFullPath(operationPath);
        var operation = JsonFile.Read<RestartOperationRecord>(sourceOperationPath);
        if (operation is null)
        {
            throw new FileNotFoundException($"Restart operation was not found at {sourceOperationPath}");
        }

        if (string.IsNullOrWhiteSpace(operation.SourceBusRoot))
        {
            throw new DirectoryNotFoundException($"Restart source bus root is not available: {operation.SourceBusRoot}");
        }
        Directory.CreateDirectory(operation.SourceBusRoot);

        if (operation.Status is RestartOperationStatus.Completed or RestartOperationStatus.RolledBack or RestartOperationStatus.Failed)
        {
            exchange.Complete(lease.EnvelopePath, lease.Envelope);
            return;
        }

        var targetBus = new AgentBusStore(operation.TargetBusRoot);
        targetBus.Initialize();
        var continuity = new ExecutionContinuityStateStore(operation.TargetBusRoot, _controllerPolicy.Policy.ContinuityStateDirectory);
        continuity.Initialize();
        var existingTask = operation.ContinuationTaskId is not null ? targetBus.FindTask(operation.ContinuationTaskId) : null;
        var task = existingTask ?? await CreateRestartContinuationTaskAsync(targetBus, operation).ConfigureAwait(false);
        var taskId = task.Id;

        AgentDefinition? targetAgent = null;
        try
        {
            targetAgent = await EnsureAgentBoundAsync(
                targetBus,
                _appServer,
                operation.TargetAgentId,
                operation.TargetCwd,
                cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            _logger?.Info("controller.startup_sweep.target_not_callable", new
            {
                operation.Id,
                operation.TargetAgentId,
                error = SafeText.Preview(ex.Message, 180)
            });
            targetAgent = targetBus.FindAgent(operation.TargetAgentId);
        }

        if (string.IsNullOrWhiteSpace(targetAgent?.ThreadId))
        {
            var store = new RestartOperationStore(operation.SourceBusRoot);
            operation = store.UpdateStatus(
                operation,
                RestartOperationStatus.ContinuationEnqueued,
                continuationTaskId: taskId,
                startupHandoffMessageId: lease.Envelope.MessageId);
            store.Write(operation);
            RecordRestartContinuationState(targetBus, continuity, operation, task);
            exchange.Complete(lease.EnvelopePath, lease.Envelope with
            {
                LastError = $"Continuation task {taskId} created; target agent not currently callable."
            });
            return;
        }

        var wakeMessage = BuildTaskWakeMessage(task.Id, operation.TargetAgentId, operation.TargetAgentId);
        var wakeup = await SendTurnWhenReadyAsync(
            _appServer,
            targetAgent.ThreadId!,
            wakeMessage,
            operation.TargetCwd,
            RuntimeSettings(targetBus.FindAgent(operation.TargetAgentId)),
            ControllerWakeupTimeout(),
            cancellationToken).ConfigureAwait(false);

        var finalStatus = wakeup.Result.Succeeded
            ? RestartOperationStatus.ContinuationDispatched
            : RestartOperationStatus.ContinuationEnqueued;

        var sourceStore = new RestartOperationStore(operation.SourceBusRoot);
        var knownGoodCheckpointId = operation.KnownGoodCheckpointId;
        operation = sourceStore.UpdateStatus(
            operation,
            finalStatus,
            continuationTaskId: taskId,
            startupHandoffMessageId: lease.Envelope.MessageId,
            lastError: wakeup.Result.Succeeded ? null : wakeup.Result.Error);
        sourceStore.Write(operation);
        if (finalStatus == RestartOperationStatus.ContinuationDispatched)
        {
            PromoteKnownGoodCheckpoint(operation.SourceBusRoot, knownGoodCheckpointId);
        }
        if (finalStatus == RestartOperationStatus.ContinuationEnqueued)
        {
            RecordRestartContinuationState(targetBus, continuity, operation, task);
        }

        exchange.Complete(lease.EnvelopePath, lease.Envelope);
    }

    private void RecordRestartContinuationState(
        AgentBusStore targetBus,
        ExecutionContinuityStateStore continuity,
        RestartOperationRecord operation,
        AgentBusTask task)
    {
        var guardian = ContinuityGuardian();
        var existing = continuity.ReadLatest(operation.Id);
        var continuityState = BuildContinuityState(
            operation.Id,
            existing,
            ExecutionContinuityStateKind.ResumePendingExternal,
            shouldContinue: true,
            lastOutcomeKind: "restart_handoff",
            lastOutcomeRef: task.Id,
            lastError: null,
            guardianAgentId: guardian.id,
            guardianDisplayName: guardian.displayName,
            currentTargetAgentId: task.To,
            currentTargetDisplayName: task.To,
            blockingOwner: null,
            blockingReason: null,
            nextActionKind: "task",
            nextActionRef: task.Id,
            resumeCorrelationId: task.Id);

        continuity.Upsert(continuityState);
        targetBus.RecordEvent(new AgentBusEvent
        {
            Timestamp = DateTimeOffset.Now,
            Type = string.IsNullOrWhiteSpace(existing?.State) ? "continuity.state_created" : "continuity.state_transitioned",
            TaskId = operation.Id,
            From = operation.RequestedByAgentId,
            To = operation.TargetAgentId,
            Message = $"Restart continuation continuity state '{continuityState.State}' recorded for operation {operation.Id}.",
            Payload = new { state = continuityState.State, resumeCorrelationId = continuityState.ResumeCorrelationId, stateId = continuityState.StateId, targetTaskId = task.Id }
        });
    }

    private void PromoteKnownGoodCheckpoint(string sourceBusRoot, string? checkpointId)
    {
        if (string.IsNullOrWhiteSpace(checkpointId))
        {
            return;
        }

        try
        {
            var store = new KnownGoodRuntimeCheckpointStore(sourceBusRoot);
            var promoted = store.MarkVerifiedIfMatch(
                checkpointId,
                $"startup_sweep:{RestartOperationStatus.ContinuationDispatched}");
            if (promoted is null)
            {
                _logger?.Info("controller.startup_sweep.known_good_checkpoint_missed", new { sourceBusRoot, checkpointId });
            }
        }
        catch (Exception ex)
        {
            _logger?.Error("controller.startup_sweep.known_good_checkpoint_verify_failed", ex, new { sourceBusRoot, checkpointId });
        }
    }

    private static async Task<AgentBusTask> CreateRestartContinuationTaskAsync(
        AgentBusStore targetBus,
        RestartOperationRecord operation)
    {
        return await Task.FromResult(targetBus.CreateTask(
            string.IsNullOrWhiteSpace(operation.RequestedByAgentId) ? operation.TargetAgentId : operation.RequestedByAgentId,
            operation.TargetAgentId,
            operation.ContinueTitle ?? "Continue after restart",
            operation.ContinuePrompt
                ?? $"{operation.TargetAgentId} requested a checkpointed restart into this checkout.\nPlease continue the source task from the operation record.",
            new DirectoryInfo(operation.TargetCwd).Name,
            operation.TargetCwd,
            [],
            operation.RequestedByAgentId)).ConfigureAwait(false);
    }

    private static string? ExtractPayloadString(ExchangeEnvelope envelope, string name)
    {
        if (!envelope.Payload.HasValue || envelope.Payload.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!envelope.Payload.Value.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static AgentBusStore Bus(JsonElement args, string defaultBusRoot)
    {
        var busRoot = Optional(args, "busRoot");
        if (!string.IsNullOrWhiteSpace(busRoot))
        {
            return new AgentBusStore(NormalizeBusRoot(busRoot));
        }

        var cwd = Optional(args, "cwd");
        if (!string.IsNullOrWhiteSpace(cwd))
        {
            return new AgentBusStore(DefaultBusRootForCwd(cwd));
        }

        return new AgentBusStore(defaultBusRoot);
    }

    private static string NormalizeBusRoot(string busRoot)
    {
        var fullPath = Path.GetFullPath(busRoot);
        var directoryName = Path.GetFileName(fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (directoryName.Equals("agentbus", StringComparison.OrdinalIgnoreCase))
        {
            return fullPath;
        }

        if (directoryName.Equals(".codexteamup", StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(fullPath, "agentbus");
        }

        if (Directory.Exists(Path.Combine(fullPath, ".codexteamup")))
        {
            return DefaultBusRootForCwd(fullPath);
        }

        return fullPath;
    }

    private static string ResolveCheckoutBusRoot(string cwd, string? requestedBusRoot)
    {
        if (!string.IsNullOrWhiteSpace(requestedBusRoot))
        {
            var normalized = NormalizeBusRoot(requestedBusRoot);
            if (Path.GetFileName(normalized).Equals("agentbus", StringComparison.OrdinalIgnoreCase))
            {
                return normalized;
            }

            if (Directory.Exists(Path.Combine(normalized, ".codexteamup", "agentbus")))
            {
                return Path.Combine(normalized, ".codexteamup", "agentbus");
            }

            if (Directory.Exists(Path.Combine(normalized, "agentbus")))
            {
                return Path.Combine(normalized, "agentbus");
            }
        }

        return DefaultBusRootForCwd(string.IsNullOrWhiteSpace(cwd) ? Environment.CurrentDirectory : cwd);
    }

    private static Process LaunchRestartHelper(string sourceCwd, string operationPath)
    {
        var supervisorPath = ResolveRestartSupervisorScript(sourceCwd);
        if (!File.Exists(supervisorPath))
        {
            throw new FileNotFoundException($"Restart supervisor script not found at {supervisorPath}.");
        }

        var command = $"-NoExit -ExecutionPolicy Bypass -File {QuoteProcessArgument(supervisorPath)} -OperationPath {QuoteProcessArgument(operationPath)}";
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "pwsh",
                Arguments = command,
                WorkingDirectory = sourceCwd,
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Normal,
                CreateNoWindow = false
            }
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("Restart helper process failed to start.");
        }

        return process;
    }

    private static string ResolveRestartSupervisorScript(string sourceCwd)
    {
        return Path.Combine(sourceCwd, "scripts", "restart-supervisor.ps1");
    }

    private static string QuoteProcessArgument(string value)
    {
        return $"\"{value.Replace("\"", "\\\"")}\"";
    }

    private static string NewOperationId(string prefix)
    {
        var value = $"{prefix}-{DateTimeOffset.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        return value[..Math.Min(value.Length, 80)];
    }

    private bool Defer(JsonElement args)
    {
        return Bool(args, "defer")
            || Bool(args, "ackOnly")
            || Bool(args, "background");
    }

        private async Task RunDeferredAsync(string operationId, string operationName, Func<Task> action)
    {
        var startedAt = DateTimeOffset.Now;
        _logger?.Info("controller.deferred.start", new { operationId, operationName });
        try
        {
            await action().ConfigureAwait(false);
            _logger?.Info("controller.deferred.complete", new
            {
                operationId,
                operationName,
                elapsedMs = (DateTimeOffset.Now - startedAt).TotalMilliseconds
            });
        }
        catch (Exception ex)
        {
            _logger?.Error("controller.deferred.exception", ex, new
            {
                operationId,
                operationName,
                elapsedMs = (DateTimeOffset.Now - startedAt).TotalMilliseconds
            });
        }
    }

    private static async Task<DispatchTaskResult> DispatchTaskAsync(
        AgentBusStore bus,
        IAppServerClient appServer,
        string taskId,
        TimeSpan wakeupTimeout,
        CancellationToken cancellationToken,
        bool recordDeliveryAttempt = true)
    {
        var task = bus.FindTask(taskId) ?? throw new FileNotFoundException("Task not found.");
        var agent = await EnsureAgentBoundAsync(bus, appServer, task.To, task.Cwd, cancellationToken).ConfigureAwait(false);

        var message = BuildTaskWakeMessage(task.Id, task.To, DefaultArchitectFor(task.To));
        var wakeup = await SendTurnWhenReadyAsync(appServer, agent.ThreadId!, message, task.Cwd, RuntimeSettings(agent), wakeupTimeout, cancellationToken).ConfigureAwait(false);
        if (recordDeliveryAttempt)
        {
            bus.UpdateTask(task.Id, existing => existing with
            {
                DeliveryAttempts = existing.DeliveryAttempts + 1,
                LastDeliveryAttemptAt = DateTimeOffset.Now,
                LastDeliveryError = wakeup.Result.Succeeded
                    ? null
                    : SafeText.Preview(wakeup.Result.Error, 180) ?? "wake-up did not succeed"
            });
        }

        bus.RecordEvent(new AgentBusEvent
        {
            Timestamp = DateTimeOffset.Now,
            Type = wakeup.Deferred ? "task.dispatch_deferred" : wakeup.Result.Succeeded ? "task.dispatched" : "task.dispatch_failed",
            TaskId = task.Id,
            From = task.From,
            To = task.To,
            Message = WakeupEventMessage(wakeup),
            Payload = WakeupEventPayload(agent.ThreadId!, wakeup)
        });

        return new DispatchTaskResult(
            wakeup.Result.Succeeded,
            wakeup.Result.ResultJson,
            wakeup.Result.Error,
            wakeup.Deferred,
            wakeup.InitialStatus,
            wakeup.FinalStatus);
    }

    private async Task<ResultNotifyAttempt> AttemptResultNotifyAsync(
        AgentBusStore bus,
        IAppServerClient appServer,
        AgentBusResult result,
        string? optionalResultId,
        string? requestedThreadId,
        string? requestedAgentId,
        string? cwd,
        CancellationToken cancellationToken)
    {
        var resultId = optionalResultId ?? result.Id;
        var notifyStartedAt = DateTimeOffset.Now;

        var currentResult = bus.UpdateResult(resultId, existing => existing with
        {
            NotifyAttempts = existing.NotifyAttempts + 1,
            LastNotifyAttemptAt = notifyStartedAt,
            LastNotifyError = null
        }) ?? throw new FileNotFoundException($"Result not found: {resultId}");

        try
        {
            var effectiveRequestedAgentId = string.IsNullOrWhiteSpace(requestedAgentId)
                ? currentResult.To
                : requestedAgentId;
            var target = await ResolveNotifyTargetAsync(
                bus,
                appServer,
                requestedThreadId,
                effectiveRequestedAgentId,
                currentResult,
                cwd,
                cancellationToken).ConfigureAwait(false);
            var targetThreadId = target.ThreadId;
            var targetAgent = target.AgentId;
            cwd ??= target.Cwd;

            bus.RecordEvent(new AgentBusEvent
            {
                Timestamp = DateTimeOffset.Now,
                Type = "result.notify_target_resolved",
                ResultId = currentResult.Id,
                From = currentResult.From,
                To = targetAgent,
                Message = $"Notify target resolved via {target.ResolutionPath}.",
                Payload = new
                {
                    requestedThreadId = requestedThreadId,
                    requestedAgentId = requestedAgentId,
                    resolvedThreadId = targetThreadId,
                    requestedFromResult = currentResult.To,
                    resolutionPath = target.ResolutionPath,
                    cwd
                }
            });

            var message =
                $"CodexTeamUp result arrived: {currentResult.Id}.\n" +
                $"Please read .codexteamup/agentbus/results/{currentResult.Id}.json and review the result, scope, and next steps.";
            var targetSettings = !string.IsNullOrWhiteSpace(targetAgent)
                ? RuntimeSettings(bus.FindAgent(targetAgent))
                : null;
            var wakeup = await SendTurnWhenReadyAsync(
                appServer,
                targetThreadId,
                message,
                cwd,
                targetSettings,
                ControllerWakeupTimeout(),
                cancellationToken).ConfigureAwait(false);
            var turnId = TryExtractTurnId(wakeup.Result.ResultJson);

            if (wakeup.Result.Succeeded)
            {
                var notifiedAt = DateTimeOffset.Now;
                currentResult = bus.UpdateResult(currentResult.Id, existing => existing with
                {
                    LastNotifiedAt = notifiedAt,
                    LastNotifyError = null
                }) ?? currentResult;

                bus.RecordEvent(new AgentBusEvent
                {
                    Timestamp = notifiedAt,
                    Type = "result.notified",
                    ResultId = currentResult.Id,
                    From = currentResult.From,
                    To = targetAgent,
                    Message = WakeupEventMessage(wakeup, turnId),
                    Payload = new
                    {
                        targetThreadId,
                        targetStatus = wakeup.FinalStatus,
                        initialStatus = wakeup.InitialStatus,
                        notifyStartedAt,
                        notifyCompletedAt = notifiedAt,
                        notifyLatencyMs = wakeup.ElapsedMs,
                        waitedMs = wakeup.WaitedMs,
                        notifyAttempts = currentResult.NotifyAttempts
                    }
                });

                return new ResultNotifyAttempt(
                    currentResult,
                    targetThreadId,
                    targetAgent,
                    Notified: true,
                    Deferred: false,
                    InitialStatus: wakeup.InitialStatus,
                    FinalStatus: wakeup.FinalStatus,
                    ResultJson: wakeup.Result.ResultJson,
                    Error: wakeup.Result.Error,
                    TurnId: turnId,
                    NotifyLatencyMs: wakeup.ElapsedMs,
                    WaitedMs: wakeup.WaitedMs);
            }

            currentResult = bus.UpdateResult(currentResult.Id, existing => existing with
            {
                LastNotifyError = SafeText.Redact(wakeup.Result.Error)
            }) ?? currentResult;

            bus.RecordEvent(new AgentBusEvent
            {
                Timestamp = DateTimeOffset.Now,
                Type = wakeup.Deferred ? "result.notify_deferred" : "result.notify_failed",
                ResultId = currentResult.Id,
                From = currentResult.From,
                To = targetAgent,
                Message = WakeupEventMessage(wakeup, turnId),
                Payload = new
                {
                    targetThreadId,
                    targetStatus = wakeup.FinalStatus,
                    initialStatus = wakeup.InitialStatus,
                    notifyStartedAt,
                    notifyCompletedAt = DateTimeOffset.Now,
                    notifyLatencyMs = wakeup.ElapsedMs,
                    waitedMs = wakeup.WaitedMs,
                    wakeup.Result.Error,
                    notifyAttempts = currentResult.NotifyAttempts,
                    notifyDeferred = wakeup.Deferred
                }
            });

            return new ResultNotifyAttempt(
                currentResult,
                targetThreadId,
                targetAgent,
                Notified: false,
                Deferred: wakeup.Deferred,
                InitialStatus: wakeup.InitialStatus,
                FinalStatus: wakeup.FinalStatus,
                ResultJson: wakeup.Result.ResultJson,
                Error: wakeup.Result.Error,
                TurnId: turnId,
                NotifyLatencyMs: wakeup.ElapsedMs,
                WaitedMs: wakeup.WaitedMs);
        }
        catch (Exception ex)
        {
            currentResult = bus.UpdateResult(currentResult.Id, existing => existing with
            {
                LastNotifyError = SafeText.Redact(ex.Message)
            }) ?? currentResult;

            bus.RecordEvent(new AgentBusEvent
            {
                Timestamp = DateTimeOffset.Now,
                Type = "result.notify_failed",
                ResultId = currentResult.Id,
                From = currentResult.From,
                To = currentResult.To,
                Message = $"Could not notify result: {SafeText.Preview(ex.Message, 180)}",
                Payload = new
                {
                    notifyStartedAt,
                    notifyCompletedAt = DateTimeOffset.Now,
                    notifyError = SafeText.Redact(ex.Message)
                }
            });

            throw;
        }
    }

    private static string RoleFromAgentId(string agentId)
    {
        var lower = agentId.ToLowerInvariant();
        if (lower.Contains("architect")) return "Architect / Coordinator";
        if (lower.Contains("web")) return "Web Implementation";
        if (lower.Contains("backend") || lower.Contains("database")) return "Backend / Data";
        if (lower.Contains("designer") || lower.Contains("design")) return "Designer / UX";
        return agentId;
    }

    private static string DefaultBusRootForCwd(string cwd)
    {
        return Path.Combine(cwd, ".codexteamup", "agentbus");
    }

    private static bool IsArchitect(string agentId)
    {
        var normalized = agentId.Replace("\\", "/", StringComparison.Ordinal).ToLowerInvariant();
        return normalized is "ctu/architect" or "ag/architect" or "architect" || normalized.Contains("architect", StringComparison.Ordinal);
    }

    private static string DefaultArchitectFor(string agentId)
    {
        var normalized = agentId.Replace("\\", "/", StringComparison.Ordinal).ToLowerInvariant();
        if (normalized.StartsWith("ag/", StringComparison.Ordinal))
        {
            return "ag/architect";
        }

        return "ctu/architect";
    }

    private sealed record TeamAgentSpec(
        string Id,
        string Role,
        string DisplayName,
        IReadOnlyList<string> AllowedPaths,
        IReadOnlyList<string> InstructionFiles,
        string? ReturnTo,
        string? InitialPrompt,
        string? Model,
        string? ReasoningEffort,
        string? Speed);

    private static IEnumerable<TeamAgentSpec> ParseTeamAgentSpecs(JsonElement args)
    {
        var agentsJson = Optional(args, "agentsJson");
        if (!string.IsNullOrWhiteSpace(agentsJson))
        {
            using var doc = JsonDocument.Parse(agentsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException("agentsJson must be a JSON array.");
            }

            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var id = JsonString(item, "id") ?? throw new ArgumentException("Agent spec is missing id.");
                yield return new TeamAgentSpec(
                    id,
                    JsonString(item, "role") ?? id,
                    JsonString(item, "displayName") ?? id,
                    JsonStringList(item, "allowedPaths"),
                    JsonStringList(item, "instructionFiles"),
                    JsonString(item, "returnTo"),
                    JsonString(item, "initialPrompt"),
                    JsonString(item, "model"),
                    JsonString(item, "reasoningEffort") ?? JsonString(item, "effort"),
                    JsonString(item, "speed"));
            }

            yield break;
        }

        foreach (var id in Csv(args, "agents"))
        {
            yield return new TeamAgentSpec(id, id, id, [], [], IsArchitect(id) ? null : DefaultArchitectFor(id), null, null, null, null);
        }
    }

    private static async Task<AgentDefinition> EnsureOrCreateAgentAsync(
        AgentBusStore bus,
        IAppServerClient appServer,
        TeamAgentSpec spec,
        string cwd,
        bool createMissing,
        bool prime,
        bool setName,
        CtuControllerPolicy policy,
        CancellationToken cancellationToken)
    {
        var existing = bus.FindAgent(spec.Id);
        var threadId = existing?.ThreadId;

        var list = await appServer.ListThreadsAsync(cwd, 100, cancellationToken).ConfigureAwait(false);
        var threads = list.Succeeded && !string.IsNullOrWhiteSpace(list.ResultJson)
            ? AppServerThreadMapper.ParseListResult(list.ResultJson)
            : [];

        if (!string.IsNullOrWhiteSpace(threadId) && !ThreadExists(threads, threadId, cwd))
        {
            threadId = null;
        }

        if (string.IsNullOrWhiteSpace(threadId) && threads.Count > 0)
        {
            var matchName = string.Equals(spec.DisplayName, spec.Id, StringComparison.OrdinalIgnoreCase)
                ? spec.Id
                : spec.DisplayName;
            threadId = AgentThreadMatcher.MatchAgents([matchName], threads, cwd).FirstOrDefault()?.ThreadId;
        }

        AppServerCallResult? created = null;
        if (string.IsNullOrWhiteSpace(threadId) && createMissing)
        {
            var createSettings = RuntimeSettings(spec.Model, spec.ReasoningEffort, spec.Speed, existing);
            created = await appServer.StartThreadAsync(cwd, spec.DisplayName, spec.Role, createSettings, cancellationToken).ConfigureAwait(false);
            if (!created.Succeeded || string.IsNullOrWhiteSpace(created.ResultJson))
            {
                throw new InvalidOperationException($"Could not create visible Codex thread for {spec.Id}: {created.Error}");
            }

            threadId = TryExtractThreadId(created.ResultJson)
                ?? throw new InvalidOperationException($"Could not read new thread id for {spec.Id}: {created.ResultJson}");
        }

        if (string.IsNullOrWhiteSpace(threadId))
        {
            throw new InvalidOperationException($"Agent {spec.Id} has no visible thread and createMissing=false.");
        }

        var agent = bus.RegisterAgent(new AgentDefinition
        {
            Id = spec.Id,
            Role = spec.Role,
            DisplayName = spec.DisplayName,
            ThreadId = threadId,
            Cwd = cwd,
            AllowedPaths = spec.AllowedPaths,
            InstructionFiles = spec.InstructionFiles,
            ReturnTo = spec.ReturnTo ?? (IsArchitect(spec.Id) ? null : DefaultArchitectFor(spec.Id)),
            Model = RuntimeSettings(spec.Model, spec.ReasoningEffort, spec.Speed, existing)?.Model,
            ReasoningEffort = RuntimeSettings(spec.Model, spec.ReasoningEffort, spec.Speed, existing)?.ReasoningEffort,
            Speed = NormalizeSpeed(spec.Speed) ?? NormalizeSpeed(existing?.Speed) ?? DefaultSpeed,
            Status = "active"
        });

        bus.RecordEvent(new AgentBusEvent
        {
            Timestamp = DateTimeOffset.Now,
            Type = created is null ? "agent.bound" : "agent.created",
            To = spec.Id,
            Message = created is null ? "Visible thread bound." : "Visible thread created."
        });

        if (setName && (policy.EnsureThreadNameBeforePrime || created is null || !prime))
        {
            try
            {
                await EnsureThreadNameAsync(appServer, threadId, spec.DisplayName, cancellationToken).ConfigureAwait(false);
                bus.RecordEvent(new AgentBusEvent
                {
                    Timestamp = DateTimeOffset.Now,
                    Type = "agent.named",
                    To = spec.Id,
                    Message = $"Visible thread name set to {spec.DisplayName}."
                });
            }
            catch (Exception ex)
            {
                bus.RecordEvent(new AgentBusEvent
                {
                    Timestamp = DateTimeOffset.Now,
                    Type = "agent.name_failed",
                    To = spec.Id,
                    Message = SafeText.Redact(ex.Message)
                });
            }
        }

        if (prime)
        {
            var prompt = BuildAgentPrimePrompt(agent, spec.InitialPrompt, policy.PrimePromptStartsWithAgentId);
            var wakeup = await SendTurnWhenReadyAsync(
                appServer,
                threadId,
                prompt,
                cwd,
                RuntimeSettings(agent),
                TimeSpan.FromSeconds(policy.WakeupTimeoutSeconds),
                cancellationToken).ConfigureAwait(false);
            var wake = wakeup.Result;
            bus.RecordEvent(new AgentBusEvent
            {
                Timestamp = DateTimeOffset.Now,
                Type = wakeup.Deferred ? "agent.prime_deferred" : "agent.primed",
                To = spec.Id,
                Message = wake.Succeeded ? "Initial prompt sent." : WakeupEventMessage(wakeup),
                Payload = WakeupEventPayload(threadId, wakeup)
            });
        }

        return agent;
    }

    private static bool ThreadExists(IReadOnlyList<CodexThreadRecord> threads, string threadId, string cwd)
    {
        return threads.Any(thread =>
            string.Equals(thread.Id, threadId, StringComparison.Ordinal) &&
            (string.IsNullOrWhiteSpace(thread.Cwd) || string.Equals(thread.Cwd, cwd, StringComparison.OrdinalIgnoreCase)));
    }

    private static async Task EnsureThreadNameAsync(
        IAppServerClient appServer,
        string threadId,
        string displayName,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return;
        }

        AppServerCallResult? nameResult = null;
        for (var attempt = 0; attempt < 10; attempt++)
        {
            nameResult = await appServer.CallAsync("thread/name/set", new { threadId, name = displayName }, cancellationToken)
                .ConfigureAwait(false);
            if (nameResult.Succeeded || !IsThreadNotFound(nameResult.Error))
            {
                break;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
        }

        if (nameResult?.Succeeded != true)
        {
            throw new InvalidOperationException($"Could not name visible Codex thread {threadId} as {displayName}: {nameResult?.Error}");
        }
    }

    private static bool IsThreadNotFound(string? error)
    {
        return !string.IsNullOrWhiteSpace(error)
            && error.Contains("thread not found", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildAgentPrimePrompt(AgentDefinition agent, string? initialPrompt, bool startsWithAgentId)
    {
        var allowedPaths = agent.AllowedPaths.Count == 0
            ? "- no explicit path restriction provided"
            : string.Join("\n", agent.AllowedPaths.Select(path => $"- {path}"));
        var instructionFiles = agent.InstructionFiles.Count == 0
            ? "- AGENTS.md\n- .codexteamup/agentbus/agents.json"
            : string.Join("\n", agent.InstructionFiles.Select(path => $"- {path}"));

        var body = $"""
        You are {agent.Id} in project {new DirectoryInfo(agent.Cwd ?? Environment.CurrentDirectory).Name}.

        Working directory:
        {agent.Cwd}

        Role:
        {agent.Role}

        Runtime:
        speed={agent.Speed ?? DefaultSpeed}
        model={agent.Model ?? "(Codex default)"}
        reasoningEffort={agent.ReasoningEffort ?? DefaultReasoningEffortForSpeed(agent.Speed)}

        Primary work areas:
        {allowedPaths}

        Read first:
        {instructionFiles}

        Communication:
        Use CodexTeamUp MCP.
        If a task needs another ctu/* agent, use team_send_message to enqueue the task, then bridge_dispatch_task to wake the target thread. Set returnTo to your own agent ID. Use short agentbus_wait_result polling if you need completion evidence.
        Read only open tasks for {agent.Id} from .codexteamup/agentbus.
        Work only on concrete AgentBus tasks addressed to {agent.Id}, or on a task ID explicitly named in a wakeup.
        Claim tasks before working on them.
        Keep your visible chat useful for humans: briefly state what you are about to do, important decisions or blockers, and the final outcome. Do not rely only on AgentBus files for human-visible progress.
        Write results with agentbus_write_result. If you edited files, set changedFiles explicitly. Put verification work in tests, or checks if your tool schema exposes that alias.
        Notify {agent.ReturnTo ?? DefaultArchitectFor(agent.Id)} afterwards with bridge_notify_result.
        Do not use PowerShell/ctu.ps1 commands as the normal communication path.
        If no matching open task file exists: do not create a replacement task, do not reconstruct a task from chat text, and do not write a result. Reply briefly in chat with a diagnosis of which task ID or file is missing.

        {initialPrompt}
        """;

        return startsWithAgentId
            ? $"{agent.Id}\n\n{body}"
            : body;
    }

    private static string BuildTaskWakeMessage(string taskId, string agentId, string returnTo)
    {
        var body = $"""
        New CodexTeamUp message/task for {agentId}: {taskId}.

        Please read exactly this task file:
        .codexteamup/agentbus/tasks/open/{taskId}.json

        Flow:
        1. Verify that the task file exists and is addressed to {agentId}.
        2. Claim exactly this task.
        3. Write a short visible-chat note about what you are doing now, so the human can inspect this thread without opening AgentBus files.
        4. Work on the task.
        5. If you make a meaningful decision, hit a blocker, ask another agent, or finish a major step, leave a short visible-chat note.
        6. Write exactly one result with agentbus_write_result. If files changed, include changedFiles. Put verification work in tests/checks.
        7. Notify {returnTo} with bridge_notify_result.

        If the task file is missing or not addressed to {agentId}: do not create a replacement task, do not reconstruct a task from chat text, and do not write a result. Reply only briefly in chat with the diagnosis.
        """;
        return $"{agentId}\n\n{body}";
    }

    private static string? TryExtractThreadId(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("thread", out var thread) && thread.TryGetProperty("id", out var nestedId))
            {
                return nestedId.GetString();
            }

            return root.TryGetProperty("id", out var id) ? id.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? TryExtractTurnId(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("turn", out var turn) && turn.TryGetProperty("id", out var nestedId))
            {
                return nestedId.GetString();
            }

            return root.TryGetProperty("id", out var id) ? id.GetString() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? JsonString(JsonElement item, string name)
    {
        return item.ValueKind == JsonValueKind.Object
            && item.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static IReadOnlyList<string> JsonStringList(JsonElement item, string name)
    {
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty(name, out var value))
        {
            return [];
        }

        if (value.ValueKind == JsonValueKind.String)
        {
            return value.GetString()?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ?? [];
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value.EnumerateArray()
            .Where(entry => entry.ValueKind == JsonValueKind.String)
            .Select(entry => entry.GetString()!)
            .Where(entry => !string.IsNullOrWhiteSpace(entry))
            .ToList();
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
            .ToDictionary(property => property.Name, property => property.Value.GetString() ?? "", StringComparer.OrdinalIgnoreCase);
    }

    private static object AppServerAdapterStatus(IAppServerClient appServer)
    {
        return appServer is IReloadableAppServerClient reloadable
            ? reloadable.Status
            : new
            {
                ActiveSource = "fixed",
                ActiveType = appServer.GetType().FullName ?? appServer.GetType().Name,
                PluginPath = (string?)null,
                PluginType = (string?)null,
                LoadedAt = DateTimeOffset.Now,
                ReloadCount = 0,
                LastError = (string?)null
            };
    }

    private static async Task<AgentDefinition> EnsureAgentBoundAsync(
        AgentBusStore bus,
        IAppServerClient appServer,
        string agentId,
        string? cwd,
        CancellationToken cancellationToken)
    {
        var existing = bus.FindAgent(agentId);
        if (!string.IsNullOrWhiteSpace(existing?.ThreadId))
        {
            if (await ThreadExistsAsync(appServer, existing.ThreadId!, existing.Cwd ?? cwd, cancellationToken).ConfigureAwait(false))
            {
                return existing;
            }

            existing = bus.RegisterAgent(existing with { ThreadId = null, Status = "missing-thread" });
            bus.RecordEvent(new AgentBusEvent
            {
                Timestamp = DateTimeOffset.Now,
                Type = "agent.binding_stale",
                To = agentId,
                Message = $"Agent {agentId} binding was stale and was cleared."
            });
        }

        var bindCwd = cwd ?? existing?.Cwd;
        var list = await appServer.ListThreadsAsync(bindCwd, 100, cancellationToken).ConfigureAwait(false);
        if (list.Succeeded && !string.IsNullOrWhiteSpace(list.ResultJson))
        {
            var threads = AppServerThreadMapper.ParseListResult(list.ResultJson);
            var names = new[] { existing?.DisplayName, agentId }
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var matchNames = names.Length == 0 ? [agentId] : names;
            var binding = AgentThreadMatcher.MatchAgents(matchNames, threads, bindCwd).FirstOrDefault(match => !string.IsNullOrWhiteSpace(match.ThreadId));
            if (!string.IsNullOrWhiteSpace(binding?.ThreadId))
            {
                return bus.RegisterAgent(new AgentDefinition
                {
                    Id = agentId,
                    Role = existing?.Role ?? RoleFromAgentId(agentId),
                    DisplayName = existing?.DisplayName ?? agentId,
                    ThreadId = binding.ThreadId,
                    Cwd = bindCwd,
                    AllowedPaths = existing?.AllowedPaths ?? [],
                    InstructionFiles = existing?.InstructionFiles ?? [],
                    ReturnTo = existing?.ReturnTo ?? (IsArchitect(agentId) ? null : DefaultArchitectFor(agentId)),
                    Model = existing?.Model,
                    ReasoningEffort = existing?.ReasoningEffort ?? DefaultReasoningEffortForSpeed(existing?.Speed),
                    Speed = existing?.Speed ?? DefaultSpeed,
                    Status = "active"
                });
            }
        }

        if (existing is not null)
        {
            throw new InvalidOperationException($"Agent {agentId} is registered but has no bound visible thread. Open or name the Codex Desktop thread as '{agentId}'.");
        }

        throw new InvalidOperationException($"Agent {agentId} could not be auto-bound. Open a visible Codex Desktop thread named '{agentId}'.");
    }

    private static async Task<NotifyTargetResolution> ResolveNotifyTargetAsync(
        AgentBusStore bus,
        IAppServerClient appServer,
        string? requestedThreadId,
        string? requestedAgentId,
        AgentBusResult? busResult,
        string? cwd,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestedThreadId))
        {
            var matchedAgent = bus.FindAgent(requestedThreadId);
            if (matchedAgent is not null)
            {
                var agent = await EnsureAgentBoundAsync(bus, appServer, requestedThreadId, cwd, cancellationToken).ConfigureAwait(false);
                return new NotifyTargetResolution(agent.ThreadId!, agent.Id, "agent-id", agent.Cwd ?? cwd);
            }

            if (await ThreadExistsAsync(appServer, requestedThreadId, cwd, cancellationToken).ConfigureAwait(false))
            {
                return new NotifyTargetResolution(requestedThreadId, requestedAgentId ?? busResult?.To, "thread-id", cwd);
            }
        }

        if (!string.IsNullOrWhiteSpace(requestedAgentId))
        {
            var agent = await EnsureAgentBoundAsync(bus, appServer, requestedAgentId, cwd, cancellationToken).ConfigureAwait(false);
            return new NotifyTargetResolution(agent.ThreadId!, agent.Id, "agent-id", agent.Cwd ?? cwd);
        }

        throw new InvalidOperationException($"No target thread found for result {busResult?.Id}.");
    }

    private sealed record NotifyTargetResolution(string ThreadId, string? AgentId, string ResolutionPath, string? Cwd);

    private static async Task<bool> ThreadExistsAsync(
        IAppServerClient appServer,
        string threadId,
        string? cwd,
        CancellationToken cancellationToken)
    {
        try
        {
            var list = await appServer.ListThreadsAsync(cwd, 100, cancellationToken).ConfigureAwait(false);
            if (list.Succeeded && !string.IsNullOrWhiteSpace(list.ResultJson))
            {
                var threads = AppServerThreadMapper.ParseListResult(list.ResultJson);
                if (threads.Any(thread => string.Equals(thread.Id, threadId, StringComparison.Ordinal)))
                {
                    return true;
                }
            }
        }
        catch (Exception)
        {
        }

        try
        {
            var resume = await appServer.ResumeThreadAsync(threadId, cancellationToken).ConfigureAwait(false);
            if (resume.Succeeded)
            {
                return true;
            }

            if (IsThreadNotFound(resume.Error))
            {
                return false;
            }
        }
        catch (Exception)
        {
        }

        try
        {
            var read = await appServer.ReadThreadAsync(threadId, includeTurns: false, cancellationToken).ConfigureAwait(false);
            if (read.Succeeded)
            {
                return true;
            }

            return !IsThreadNotFound(read.Error);
        }
        catch (Exception)
        {
            return true;
        }
    }

    private static async Task<string?> TryReadThreadStatusAsync(
        IAppServerClient appServer,
        string threadId,
        string? cwd,
        CancellationToken cancellationToken)
    {
        string? listStatus = null;
        try
        {
            var list = await appServer.ListThreadsAsync(cwd, 100, cancellationToken).ConfigureAwait(false);
            if (list.Succeeded && !string.IsNullOrWhiteSpace(list.ResultJson))
            {
                listStatus = AppServerThreadMapper.ParseListResult(list.ResultJson)
                    .FirstOrDefault(thread => string.Equals(thread.Id, threadId, StringComparison.Ordinal))?
                    .Status;

                if (!IsUncertainThreadStatus(listStatus))
                {
                    return listStatus;
                }
            }
        }
        catch (Exception)
        {
        }

        try
        {
            var read = await appServer.ReadThreadAsync(threadId, includeTurns: true, cancellationToken).ConfigureAwait(false);
            if (!read.Succeeded || string.IsNullOrWhiteSpace(read.ResultJson))
            {
                return listStatus;
            }

            var readStatus = AppServerThreadMapper.ParseReadResult(read.ResultJson)?.Thread.Status;
            if (!IsUncertainThreadStatus(readStatus))
            {
                return readStatus;
            }

            return TryFindBusyTurnStatus(read.ResultJson) ?? readStatus ?? listStatus;
        }
        catch (Exception)
        {
            return listStatus;
        }
    }

    private static bool IsUncertainThreadStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return true;
        }

        var normalized = status.Trim().ToLowerInvariant();
        return normalized is "notloaded" or "not_loaded" or "unknown";
    }

    private static string? TryFindBusyTurnStatus(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var thread = root.TryGetProperty("thread", out var threadElement) ? threadElement : root;
            if (!thread.TryGetProperty("turns", out var turns) || turns.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            string? lastStatus = null;
            foreach (var turn in turns.EnumerateArray())
            {
                var status = JsonString(turn, "status");
                if (string.IsNullOrWhiteSpace(status))
                {
                    continue;
                }

                lastStatus = status;
                if (IsBusyThreadStatus(status))
                {
                    return status;
                }
            }

            return lastStatus;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task<WakeupSendResult> SendTurnWhenReadyAsync(
        IAppServerClient appServer,
        string threadId,
        string message,
        string? cwd,
        AppServerAgentSettings? settings,
        TimeSpan sendTimeout,
        CancellationToken cancellationToken)
    {
        await DesktopWakeupGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
        var stopwatch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(sendTimeout);
        var token = timeout.Token;

        string? initialStatus;
        try
        {
            initialStatus = await TryReadThreadStatusAsync(appServer, threadId, cwd, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new WakeupSendResult(
                new AppServerCallResult(false, null, $"Wakeup status read timed out after {sendTimeout.TotalSeconds:n0}s; wakeup deferred."),
                InitialStatus: null,
                FinalStatus: null,
                Deferred: true,
                WaitedMs: 0,
                ElapsedMs: stopwatch.ElapsedMilliseconds);
        }

        var finalStatus = initialStatus;
        var waitedMs = 0L;

        while (IsBusyThreadStatus(finalStatus) && stopwatch.Elapsed < WakeupReadyTimeout)
        {
            await Task.Delay(WakeupReadyPollInterval, token).ConfigureAwait(false);
            waitedMs = stopwatch.ElapsedMilliseconds;
            finalStatus = await TryReadThreadStatusAsync(appServer, threadId, cwd, token).ConfigureAwait(false);
        }

        if (IsBusyThreadStatus(finalStatus))
        {
            stopwatch.Stop();
            return new WakeupSendResult(
                new AppServerCallResult(false, null, $"Target thread {threadId} is still busy ({finalStatus}); wakeup deferred."),
                initialStatus,
                finalStatus,
                Deferred: true,
                waitedMs,
                stopwatch.ElapsedMilliseconds);
        }

        try
        {
            var result = await appServer.SendTurnAsync(threadId, message, cwd, settings, token).ConfigureAwait(false);
            stopwatch.Stop();
            return new WakeupSendResult(result, initialStatus, finalStatus, Deferred: false, waitedMs, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();
            return new WakeupSendResult(
                new AppServerCallResult(false, null, $"turn/start timed out after {sendTimeout.TotalSeconds:n0}s; wakeup status is uncertain and the AgentBus task remains authoritative."),
                initialStatus,
                finalStatus,
                Deferred: true,
                waitedMs,
                stopwatch.ElapsedMilliseconds);
        }
        }
        finally
        {
            DesktopWakeupGate.Release();
        }
    }

    private TimeSpan ControllerWakeupTimeout()
    {
        return TimeSpan.FromSeconds(_controllerPolicy.Policy.WakeupTimeoutSeconds);
    }

    private TimeSpan WakeupTimeout(JsonElement args)
    {
        var defaultSeconds = _controllerPolicy.Policy.WakeupTimeoutSeconds;
        return TimeSpan.FromSeconds(Math.Clamp(Int(args, "wakeupTimeoutSeconds", defaultSeconds), 1, 10));
    }

    private static AgentBusResult? WaitForResultSafe(AgentBusStore bus, string taskId, TimeSpan timeout, CancellationToken cancellationToken)
    {
        bus.Initialize();
        var stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var existing = FindResultByTaskIdSafe(bus, taskId);
            if (existing is not null)
            {
                return existing;
            }

            var remaining = timeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero)
            {
                break;
            }

            var delay = remaining < TimeSpan.FromMilliseconds(200)
                ? remaining
                : TimeSpan.FromMilliseconds(200);
            if (delay <= TimeSpan.Zero)
            {
                break;
            }

            Task.Delay(delay, cancellationToken).GetAwaiter().GetResult();
        }

        return FindResultByTaskIdSafe(bus, taskId);
    }

    private static AgentBusResult? FindResultByTaskIdSafe(AgentBusStore bus, string taskId)
    {
        if (!Directory.Exists(bus.ResultsDirectory))
        {
            return null;
        }

        foreach (var path in Directory.EnumerateFiles(bus.ResultsDirectory, "*.json"))
        {
            try
            {
                var result = JsonFile.Read<AgentBusResult>(path);
                if (result is not null && result.TaskId == taskId)
                {
                    return result;
                }
            }
            catch
            {
                // Result files can be observed mid-write; polling will retry on the next pass.
            }
        }

        return null;
    }

    private static int WaitTimeoutSeconds(JsonElement args, string name, int defaultSeconds, int capSeconds)
    {
        var cap = Math.Max(1, capSeconds);
        var value = Int(args, name, defaultSeconds);
        return Math.Clamp(value, 1, cap);
    }

    private static bool IsBusyThreadStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        var normalized = status.Trim().ToLowerInvariant();
        return normalized.Contains("active", StringComparison.Ordinal)
            || normalized.Contains("running", StringComparison.Ordinal)
            || normalized.Contains("inprogress", StringComparison.Ordinal)
            || normalized.Contains("in_progress", StringComparison.Ordinal)
            || normalized.Contains("busy", StringComparison.Ordinal);
    }

    private static string WakeupEventMessage(WakeupSendResult wakeup, string? turnId = null)
    {
        if (wakeup.Deferred)
        {
            return $"turn/start deferred; latencyMs={wakeup.ElapsedMs}; waitedMs={wakeup.WaitedMs}; targetStatus={wakeup.FinalStatus ?? "unknown"}; error={SafeText.Preview(wakeup.Result.Error, 180)}";
        }

        return wakeup.Result.Succeeded
            ? $"turn/start sent; latencyMs={wakeup.ElapsedMs}; waitedMs={wakeup.WaitedMs}; turnId={turnId ?? TryExtractTurnId(wakeup.Result.ResultJson) ?? "unknown"}; targetStatus={wakeup.FinalStatus ?? "unknown"}"
            : $"turn/start failed; latencyMs={wakeup.ElapsedMs}; waitedMs={wakeup.WaitedMs}; targetStatus={wakeup.FinalStatus ?? "unknown"}; error={SafeText.Preview(wakeup.Result.Error, 180)}";
    }

    private static object WakeupEventPayload(string threadId, WakeupSendResult wakeup)
    {
        return new
        {
            targetThreadId = threadId,
            initialStatus = wakeup.InitialStatus,
            targetStatus = wakeup.FinalStatus,
            wakeup.Deferred,
            wakeup.WaitedMs,
            wakeup.ElapsedMs,
            wakeup.Result.ResultJson,
            wakeup.Result.Error
        };
    }

    private sealed record WakeupSendResult(
        AppServerCallResult Result,
        string? InitialStatus,
        string? FinalStatus,
        bool Deferred,
        long WaitedMs,
        long ElapsedMs);

    /// <summary>Result of a task wakeup attempt from controller delivery logic.</summary>
    private sealed record DispatchTaskResult(
        bool Succeeded,
        string? ResultJson,
        string? Error,
        bool Deferred,
        string? InitialStatus,
        string? FinalStatus);

    /// <summary>Result of a result-notify wakeup attempt including persisted retry metadata.</summary>
    private sealed record ResultNotifyAttempt(
        AgentBusResult Result,
        string TargetThreadId,
        string TargetAgentId,
        bool Notified,
        bool Deferred,
        string? InitialStatus,
        string? FinalStatus,
        string? ResultJson,
        string? Error,
        string? TurnId,
        long NotifyLatencyMs,
        long WaitedMs);

    private static string Required(JsonElement args, string name)
    {
        return Optional(args, name) ?? throw new ArgumentException($"Missing argument: {name}");
    }

    private static string? Optional(JsonElement args, string name)
    {
        return args.ValueKind == JsonValueKind.Object
            && args.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static AgentDefinition ApplyAgentRuntimeDefaults(AgentDefinition agent, AgentDefinition? existing = null)
    {
        var settings = RuntimeSettings(agent.Model, agent.ReasoningEffort, agent.Speed, existing);
        return agent with
        {
            ThreadId = BlankToNull(agent.ThreadId) ?? existing?.ThreadId,
            Cwd = BlankToNull(agent.Cwd) ?? existing?.Cwd,
            AllowedPaths = agent.AllowedPaths.Count > 0 ? agent.AllowedPaths : existing?.AllowedPaths ?? [],
            InstructionFiles = agent.InstructionFiles.Count > 0 ? agent.InstructionFiles : existing?.InstructionFiles ?? [],
            ReturnTo = BlankToNull(agent.ReturnTo) ?? existing?.ReturnTo,
            Model = settings?.Model,
            ReasoningEffort = settings?.ReasoningEffort,
            Speed = NormalizeSpeed(agent.Speed) ?? NormalizeSpeed(existing?.Speed) ?? DefaultSpeed
        };
    }

    private static AppServerAgentSettings? RuntimeSettings(AgentDefinition? agent)
    {
        return agent is null
            ? null
            : RuntimeSettings(agent.Model, agent.ReasoningEffort, agent.Speed, agent);
    }

    private static AppServerAgentSettings? RuntimeSettings(
        string? model,
        string? reasoningEffort,
        string? speed,
        AgentDefinition? existing)
    {
        var speedSpecified = !string.IsNullOrWhiteSpace(speed);
        var normalizedSpeed = NormalizeSpeed(speed) ?? NormalizeSpeed(existing?.Speed) ?? DefaultSpeed;
        var resolvedModel = BlankToNull(model)
            ?? (speedSpecified ? DefaultModelForSpeed(normalizedSpeed) : BlankToNull(existing?.Model) ?? DefaultModelForSpeed(normalizedSpeed));
        var resolvedEffort = NormalizeReasoningEffort(reasoningEffort)
            ?? (speedSpecified
                ? DefaultReasoningEffortForSpeed(normalizedSpeed)
                : NormalizeReasoningEffort(existing?.ReasoningEffort) ?? DefaultReasoningEffortForSpeed(normalizedSpeed));
        return string.IsNullOrWhiteSpace(resolvedModel) && string.IsNullOrWhiteSpace(resolvedEffort)
            ? null
            : new AppServerAgentSettings(resolvedModel, resolvedEffort);
    }

    private static string? DefaultModelForSpeed(string? speed)
    {
        return NormalizeSpeed(speed) switch
        {
            "fast" => "gpt-5.4-mini",
            _ => null
        };
    }

    private static string DefaultReasoningEffortForSpeed(string? speed)
    {
        return NormalizeSpeed(speed) switch
        {
            "fast" => "low",
            "deep" => "high",
            "max" => "xhigh",
            _ => "medium"
        };
    }

    private static string? NormalizeSpeed(string? value)
    {
        var normalized = BlankToNull(value)?.ToLowerInvariant();
        return normalized switch
        {
            null => null,
            "fast" or "standard" or "deep" or "max" => normalized,
            "speed" => "fast",
            "quality" => "deep",
            "extra" or "extra-high" or "xhigh" => "max",
            _ => throw new ArgumentException($"Unsupported speed: {value}. Use fast, standard, deep, or max.")
        };
    }

    private static string? NormalizeReasoningEffort(string? value)
    {
        var normalized = BlankToNull(value)?.ToLowerInvariant();
        return normalized switch
        {
            null => null,
            "none" or "minimal" or "low" or "medium" or "high" or "xhigh" => normalized,
            "extra-high" or "extra_high" => "xhigh",
            _ => throw new ArgumentException($"Unsupported reasoning effort: {value}.")
        };
    }

    private static string? BlankToNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool Bool(JsonElement args, string name)
    {
        return args.ValueKind == JsonValueKind.Object
            && args.TryGetProperty(name, out var value)
            && value.ValueKind == JsonValueKind.True;
    }

    private static bool IsExplicitFalse(JsonElement args, string name)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var value))
        {
            return false;
        }

        return value.ValueKind == JsonValueKind.False
            || (value.ValueKind == JsonValueKind.String
                && string.Equals(value.GetString(), "false", StringComparison.OrdinalIgnoreCase));
    }

    private static int Int(JsonElement args, string name, int defaultValue)
    {
        return args.ValueKind == JsonValueKind.Object
            && args.TryGetProperty(name, out var value)
            && value.TryGetInt32(out var parsed)
            ? parsed
            : defaultValue;
    }

    private static AgentBusContinuationRequest? ContinuationRequest(JsonElement args)
    {
        var hasContinuation = !string.IsNullOrWhiteSpace(Optional(args, "continuationOwner"))
            || !string.IsNullOrWhiteSpace(Optional(args, "continuationReason"))
            || !string.IsNullOrWhiteSpace(Optional(args, "continuationDedupeKey"))
            || HasProperty(args, "continuationWakeAfterSeconds")
            || HasProperty(args, "continuationMaxAttempts")
            || string.Equals(Optional(args, "outcome"), "self_continue", StringComparison.OrdinalIgnoreCase);
        if (!hasContinuation)
        {
            return null;
        }

        return new AgentBusContinuationRequest
        {
            Owner = Optional(args, "continuationOwner"),
            WakeAfterSeconds = Int(args, "continuationWakeAfterSeconds", 60),
            DedupeKey = Optional(args, "continuationDedupeKey"),
            Reason = Optional(args, "continuationReason"),
            MaxAttempts = Int(args, "continuationMaxAttempts", 5)
        };
    }

    private static bool HasProperty(JsonElement args, string name)
    {
        return args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out _);
    }

    private static IReadOnlyList<string> Csv(JsonElement args, params string[] names)
    {
        var values = names
            .Select(name => Optional(args, name))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .SelectMany(value => value!.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToList();
        return values.Count == 0
            ? []
            : values;
    }
}
