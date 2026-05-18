namespace CodexTeamUp.Controller;

public static class CtuControllerTools
{
    public static readonly IReadOnlyList<string> KnownToolNames =
    [
        "agentbus_init",
        "agentbus_list_agents",
        "agentbus_register_agent",
        "agentbus_create_task",
        "agentbus_list_tasks",
        "agentbus_clear_tasks",
        "agentbus_list_events",
        "agentbus_claim_task",
        "agentbus_write_result",
        "agentbus_wait_result",
        "codex_thread_list",
        "codex_thread_read",
        "codex_thread_archive",
        "codex_turn_start",
        "codex_appserver_adapter_status",
        "codex_appserver_adapter_reload",
        "codex_controller_status",
        "codex_controller_reload",
        "codex_controller_policy_status",
        "codex_controller_policy_reload",
        "bridge_dispatch_task",
        "bridge_notify_result",
        "team_create_agent",
        "team_ensure_agents",
        "team_discover_agents",
            "team_send_message",
            "team_dashboard_export",
            "ctu_restart_request",
            "ctu_restart_status"
        ];
}
