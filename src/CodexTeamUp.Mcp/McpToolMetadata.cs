namespace CodexTeamUp.Mcp;

/// <summary>
/// Describes CodexTeamUp MCP tools for the HTTP transport.
/// </summary>
public static class McpToolMetadata
{
    public static string ToolDescription(string name)
    {
        return name switch
        {
            "agentbus_init" => "Initialize the repo-local AgentBus directories.",
            "agentbus_list_agents" => "List registered CodexTeamUp agents.",
            "agentbus_register_agent" => "Register or update an agent-to-thread binding.",
            "agentbus_create_task" => "Create an AgentBus task.",
            "agentbus_list_tasks" => "List AgentBus tasks.",
            "agentbus_claim_task" => "Claim an open AgentBus task.",
            "agentbus_write_result" => "Write an AgentBus result.",
            "agentbus_wait_result" => "Wait for an AgentBus task result.",
            "codex_thread_list" => "List Codex Desktop threads through the wrapper.",
            "codex_thread_read" => "Read a Codex Desktop thread through the wrapper.",
            "codex_thread_archive" => "Archive a Codex Desktop thread through the wrapper.",
            "codex_turn_start" => "Wake a Codex Desktop thread with turn/start.",
            "codex_appserver_adapter_status" => "Show the active hot-swappable Codex Desktop app-server adapter.",
            "codex_appserver_adapter_reload" => "Reload the hot-swappable Codex Desktop app-server adapter without restarting Desktop.",
            "codex_controller_status" => "Show the active hot-swappable CTU controller runtime.",
            "codex_controller_reload" => "Reload the hot-swappable CTU controller runtime without restarting the service.",
            "codex_controller_policy_status" => "Show the active hot-swappable CTU controller policy.",
            "codex_controller_policy_reload" => "Reload the hot-swappable CTU controller policy without restarting the service.",
            "bridge_dispatch_task" => "Wake the agent assigned to an AgentBus task.",
            "bridge_notify_result" => "Wake the return agent/thread with an AgentBus result notification.",
            "team_create_agent" => "Create or bind one visible CodexTeamUp agent thread and prime it.",
            "team_ensure_agents" => "Create or bind the exact CodexTeamUp agent team requested by the caller.",
            "team_discover_agents" => "Discover visible Desktop threads by team names and bind them as project agents.",
            "team_send_message" => "Create a project AgentBus message/task. Defaults to enqueue-only; use bridge_dispatch_task for wakeup.",
            "team_dashboard_export" => "Export a local HTML dashboard for project communication.",
            _ => "CodexTeamUp tool."
        };
    }

    public static object ToolAnnotations(string name)
    {
        return name switch
        {
            "agentbus_list_agents" or
            "agentbus_list_tasks" or
            "agentbus_wait_result" or
            "codex_thread_list" or
            "codex_thread_read" or
            "codex_appserver_adapter_status" or
            "codex_controller_status" or
            "codex_controller_policy_status" => new
            {
                readOnlyHint = true,
                destructiveHint = false,
                idempotentHint = true,
                openWorldHint = false
            },
            "agentbus_init" or
            "agentbus_register_agent" or
            "team_discover_agents" => new
            {
                readOnlyHint = false,
                destructiveHint = false,
                idempotentHint = true,
                openWorldHint = false
            },
            "agentbus_create_task" or
            "agentbus_claim_task" or
            "agentbus_write_result" or
            "codex_thread_archive" or
            "codex_turn_start" or
            "codex_appserver_adapter_reload" or
            "codex_controller_reload" or
            "codex_controller_policy_reload" or
            "bridge_dispatch_task" or
            "bridge_notify_result" or
            "team_create_agent" or
            "team_ensure_agents" or
            "team_send_message" or
            "team_dashboard_export" => new
            {
                readOnlyHint = false,
                destructiveHint = false,
                idempotentHint = false,
                openWorldHint = false
            },
            _ => new
            {
                readOnlyHint = false,
                destructiveHint = false,
                idempotentHint = false,
                openWorldHint = false
            }
        };
    }

    public static object ToolInputSchema(string name)
    {
        var properties = name switch
        {
            "agentbus_init" => WithBusContext(new Dictionary<string, object>()),
            "agentbus_list_agents" => WithBusContext(new Dictionary<string, object>()),
            "agentbus_register_agent" => WithBusContext(new Dictionary<string, object>
            {
                ["id"] = StringSchema(),
                ["role"] = StringSchema(),
                ["displayName"] = StringSchema(),
                ["threadId"] = StringSchema(),
                ["cwd"] = StringSchema(),
                ["returnTo"] = StringSchema(),
                ["model"] = StringSchema(),
                ["reasoningEffort"] = StringSchema(),
                ["speed"] = StringSchema(),
                ["status"] = StringSchema()
            }),
            "agentbus_create_task" => WithBusContext(new Dictionary<string, object>
            {
                ["from"] = StringSchema(),
                ["to"] = StringSchema(),
                ["title"] = StringSchema(),
                ["prompt"] = StringSchema(),
                ["project"] = StringSchema(),
                ["cwd"] = StringSchema(),
                ["allowedPaths"] = StringSchema(),
                ["returnTo"] = StringSchema()
            }),
            "agentbus_list_tasks" => WithBusContext(new Dictionary<string, object>
            {
                ["to"] = StringSchema(),
                ["status"] = StringSchema()
            }),
            "agentbus_claim_task" => WithBusContext(new Dictionary<string, object>
            {
                ["taskId"] = StringSchema(),
                ["owner"] = StringSchema()
            }),
            "agentbus_write_result" => WithBusContext(new Dictionary<string, object>
            {
                ["taskId"] = StringSchema(),
                ["summary"] = StringSchema(),
                ["status"] = StringSchema(),
                ["from"] = StringSchema(),
                ["to"] = StringSchema(),
                ["commit"] = StringSchema(),
                ["changedFiles"] = StringSchema(),
                ["checks"] = StringSchema(),
                ["tests"] = StringSchema(),
                ["artifacts"] = StringSchema(),
                ["openQuestions"] = StringSchema(),
                ["nextSuggestedAction"] = StringSchema()
            }),
            "agentbus_wait_result" => WithBusContext(new Dictionary<string, object>
            {
                ["taskId"] = StringSchema(),
                ["timeoutSeconds"] = new { type = "integer" }
            }),
            "codex_thread_list" => new Dictionary<string, object>
            {
                ["cwd"] = StringSchema(),
                ["limit"] = new { type = "integer" }
            },
            "codex_thread_read" => new Dictionary<string, object>
            {
                ["threadId"] = StringSchema(),
                ["includeTurns"] = new { type = "boolean" }
            },
            "codex_thread_archive" => new Dictionary<string, object>
            {
                ["threadId"] = StringSchema()
            },
            "codex_turn_start" => new Dictionary<string, object>
            {
                ["threadId"] = StringSchema(),
                ["message"] = StringSchema(),
                ["cwd"] = StringSchema(),
                ["model"] = StringSchema(),
                ["reasoningEffort"] = StringSchema(),
                ["speed"] = StringSchema()
            },
            "codex_appserver_adapter_status" => new Dictionary<string, object>(),
            "codex_controller_policy_status" => new Dictionary<string, object>(),
            "codex_appserver_adapter_reload" => new Dictionary<string, object>
            {
                ["pluginPath"] = StringSchema(),
                ["pluginType"] = StringSchema(),
                ["path"] = StringSchema(),
                ["type"] = StringSchema(),
                ["options"] = new
                {
                    type = "object",
                    additionalProperties = new { type = "string" }
                }
            },
            "codex_controller_policy_reload" => new Dictionary<string, object>
            {
                ["policyPath"] = StringSchema(),
                ["path"] = StringSchema()
            },
            "codex_controller_status" => new Dictionary<string, object>(),
            "codex_controller_reload" => new Dictionary<string, object>
            {
                ["pluginPath"] = StringSchema(),
                ["pluginType"] = StringSchema(),
                ["path"] = StringSchema(),
                ["type"] = StringSchema(),
                ["policyPath"] = StringSchema(),
                ["reloadPolicy"] = new { type = "boolean" },
                ["options"] = new
                {
                    type = "object",
                    additionalProperties = new { type = "string" }
                }
            },
            "bridge_dispatch_task" => WithBusContext(new Dictionary<string, object>
            {
                ["taskId"] = StringSchema()
            }),
            "bridge_notify_result" => WithBusContext(new Dictionary<string, object>
            {
                ["resultId"] = StringSchema(),
                ["toAgent"] = StringSchema(),
                ["toThread"] = StringSchema()
            }),
            "team_create_agent" => WithBusContext(new Dictionary<string, object>
            {
                ["id"] = StringSchema(),
                ["role"] = StringSchema(),
                ["displayName"] = StringSchema(),
                ["cwd"] = StringSchema(),
                ["allowedPaths"] = StringSchema(),
                ["instructionFiles"] = StringSchema(),
                ["returnTo"] = StringSchema(),
                ["initialPrompt"] = StringSchema(),
                ["model"] = StringSchema(),
                ["reasoningEffort"] = StringSchema(),
                ["speed"] = StringSchema()
            }),
            "team_ensure_agents" => WithBusContext(new Dictionary<string, object>
            {
                ["agentsJson"] = StringSchema(),
                ["agents"] = StringSchema(),
                ["cwd"] = StringSchema(),
                ["project"] = StringSchema(),
                ["createMissing"] = StringSchema(),
                ["prime"] = StringSchema()
            }),
            "team_discover_agents" => WithBusContext(new Dictionary<string, object>
            {
                ["agents"] = StringSchema(),
                ["cwd"] = StringSchema(),
                ["project"] = StringSchema(),
                ["limit"] = new { type = "integer" }
            }),
            "team_send_message" => WithBusContext(new Dictionary<string, object>
            {
                ["from"] = StringSchema(),
                ["to"] = StringSchema(),
                ["title"] = StringSchema(),
                ["message"] = StringSchema(),
                ["project"] = StringSchema(),
                ["cwd"] = StringSchema(),
                ["allowedPaths"] = StringSchema(),
                ["returnTo"] = StringSchema(),
                ["dispatchMode"] = StringSchema(),
                ["dispatch"] = StringSchema(),
                ["waitResult"] = new { type = "boolean" },
                ["timeoutSeconds"] = new { type = "integer" },
                ["wakeupTimeoutSeconds"] = new { type = "integer" }
            }),
            "team_dashboard_export" => WithBusContext(new Dictionary<string, object>
            {
                ["outputPath"] = StringSchema()
            }),
            _ => new Dictionary<string, object>()
        };

        return new
        {
            type = "object",
            properties,
            additionalProperties = false
        };
    }

    private static object StringSchema()
    {
        return new { type = "string" };
    }

    private static Dictionary<string, object> WithBusContext(Dictionary<string, object> properties)
    {
        properties["cwd"] = StringSchema();
        properties["busRoot"] = StringSchema();
        return properties;
    }
}
