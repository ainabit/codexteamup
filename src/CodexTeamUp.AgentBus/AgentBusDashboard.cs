using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using CodexTeamUp.Core;

namespace CodexTeamUp.AgentBus;

/// <summary>
/// Renders a local HTML dashboard for AgentBus communication.
/// </summary>
public static class AgentBusDashboard
{
    private static readonly JsonSerializerOptions EmbeddedJsonOptions = new(JsonFile.Options)
    {
        Encoder = JavaScriptEncoder.Default,
        WriteIndented = false
    };

    /// <summary>
    /// Writes an HTML dashboard and returns the generated file path.
    /// </summary>
    public static string Export(AgentBusStore bus, string? outputPath = null)
    {
        bus.Initialize();
        var path = string.IsNullOrWhiteSpace(outputPath)
            ? Path.Combine(bus.RootDirectory, "dashboard", "index.html")
            : Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Render(bus), Encoding.UTF8);
        return path;
    }

    /// <summary>
    /// Builds a compact snapshot used by both the HTTP API and the embedded dashboard bootstrap.
    /// </summary>
    public static AgentBusDashboardSnapshot CreateSnapshot(AgentBusStore bus)
    {
        var agents = bus.ListAgents();
        var tasks = bus.ListTasks();
        var results = bus.ListResults();
        var events = bus.ListEvents(500);

        return new AgentBusDashboardSnapshot
        {
            BusRoot = bus.RootDirectory,
            GeneratedAt = DateTimeOffset.Now,
            Stats = new AgentBusDashboardStats
            {
                Agents = agents.Count,
                Tasks = tasks.Count,
                OpenTasks = tasks.Count(t => t.Status == "open"),
                ClaimedTasks = tasks.Count(t => t.Status == "claimed"),
                DoneTasks = tasks.Count(t => t.Status == "done"),
                FailedTasks = tasks.Count(t => t.Status == "failed"),
                Results = results.Count,
                Events = events.Count
            },
            Agents = agents,
            Tasks = tasks,
            Results = results,
            Events = events
        };
    }

    /// <summary>
    /// Renders AgentBus state into a standalone no-build single page dashboard.
    /// </summary>
    public static string Render(AgentBusStore bus)
    {
        var snapshot = CreateSnapshot(bus);
        var initialJson = JsonSerializer.Serialize(snapshot, EmbeddedJsonOptions);

        var html = new StringBuilder();
        html.AppendLine("<!doctype html>");
        html.AppendLine("<html lang=\"en\">");
        html.AppendLine("<head>");
        html.AppendLine("<meta charset=\"utf-8\">");
        html.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
        html.AppendLine("<title>CodexTeamUp Dashboard</title>");
        html.AppendLine("<style>");
        html.AppendLine("""
:root{
  color-scheme:light;
  --bg:#f4f7fb;
  --bg-accent:#e9eef5;
  --panel:#ffffff;
  --panel-alt:#f8fafc;
  --panel-soft:#edf3f8;
  --text:#17202a;
  --muted:#5c6978;
  --border:#ced8e4;
  --line:#e2e8f0;
  --codex:#2563eb;
  --team:#0f9f8f;
  --up:#f59e0b;
  --status-open:#dbeafe;
  --status-claimed:#fef3c7;
  --status-done:#dcfce7;
  --status-failed:#fee2e2;
  --shadow:0 14px 32px rgba(20,36,56,.08);
}
:root[data-theme='light']{color-scheme:light}
:root[data-theme='dark']{
  color-scheme:dark;
  --bg:#161616;
  --bg-accent:#202020;
  --panel:#1f1f1f;
  --panel-alt:#262626;
  --panel-soft:#2d2d2d;
  --text:#f4f7fb;
  --muted:#a7b1be;
  --border:#3a4553;
  --line:#2e3845;
  --codex:#60a5fa;
  --team:#2dd4bf;
  --up:#fbbf24;
  --status-open:#1d4750;
  --status-claimed:#5d4510;
  --status-done:#26472a;
  --status-failed:#5a2d25;
  --shadow:0 20px 44px rgba(0,0,0,.35);
}
*{box-sizing:border-box}
html,body,#app{height:100%;overflow:hidden}
body{
  margin:0;
  background:linear-gradient(180deg,var(--bg),var(--bg-accent));
  color:var(--text);
  font-family:"Segoe UI",Arial,sans-serif;
  font-size:14px;
  line-height:1.45;
}
button,input,select{font:inherit}
button{
  border:1px solid var(--border);
  background:var(--panel);
  border-radius:8px;
  padding:7px 11px;
  color:var(--text);
  cursor:pointer;
}
button:hover:not(:disabled){background:var(--panel-alt)}
button:disabled{opacity:.45;cursor:default}
.dashboard-shell{height:100%;display:grid;grid-template-rows:auto minmax(0,1fr);overflow:hidden}
.topbar{
  display:flex;
  align-items:center;
  justify-content:space-between;
  gap:18px;
  padding:16px 20px 12px;
  border-bottom:1px solid var(--border);
  background:rgba(255,255,255,.88);
  backdrop-filter:blur(14px);
}
:root[data-theme='dark'] .topbar{background:rgba(31,31,31,.82)}
.brand{display:flex;align-items:center;gap:16px}
.brand-mark{
  width:42px;
  height:42px;
  border-radius:8px;
  background:linear-gradient(135deg,var(--codex),var(--team) 55%,var(--up));
  box-shadow:var(--shadow);
  position:relative;
}
.brand-mark::before,.brand-mark::after{
  content:"";
  position:absolute;
  border-radius:999px;
  background:rgba(255,255,255,.86);
}
.brand-mark::before{width:18px;height:6px;left:12px;top:10px;transform:rotate(28deg)}
.brand-mark::after{width:6px;height:20px;left:18px;top:12px}
.brand-copy h1{margin:0;font-size:30px;font-weight:800;letter-spacing:.01em}
.brand-copy h1 .codex{color:var(--codex)}
.brand-copy h1 .team{color:var(--team)}
.brand-copy h1 .up{color:var(--up)}
.brand-copy p{margin:2px 0 0;color:var(--muted);font-size:13px}
.actions{display:flex;align-items:center;gap:8px;flex-wrap:wrap;justify-content:flex-end}
.segment{
  display:inline-grid;
  grid-template-columns:repeat(3,1fr);
  gap:4px;
  padding:4px;
  border:1px solid var(--border);
  border-radius:8px;
  background:var(--panel-alt);
}
.segment button{
  border:0;
  background:transparent;
  border-radius:8px;
  padding:6px 10px;
}
.segment button.active{
  background:var(--panel);
  box-shadow:0 1px 0 rgba(0,0,0,.04);
}
.last-update{font-size:12px;color:var(--muted);min-width:154px;text-align:right}
.content{
  min-height:0;
  width:100%;
  margin:0;
  padding:10px 12px 12px;
  display:grid;
  grid-template-rows:auto auto minmax(0,1fr);
  gap:10px;
  overflow:hidden;
}
.watermark{color:var(--muted);font-size:12px;min-height:16px;overflow-wrap:anywhere}
.meta-toolbar{
  display:grid;
  grid-template-columns:minmax(220px,1.25fr) minmax(120px,.8fr) minmax(130px,.8fr) minmax(130px,.8fr) auto;
  gap:8px 10px;
  align-items:center;
  padding:10px 12px;
  min-height:72px;
  background:var(--panel);
  border:1px solid var(--border);
  border-radius:8px;
  box-shadow:var(--shadow);
}
.project-inline{display:grid;gap:4px;min-width:0}
.project-inline-name{font-size:20px;font-weight:800;overflow-wrap:anywhere}
.project-inline-meta{font-size:12px;color:var(--muted);overflow-wrap:anywhere}
.metric-pills{display:flex;flex-wrap:wrap;gap:8px;min-width:0}
.metric-pill{
  display:inline-flex;
  align-items:center;
  gap:6px;
  padding:6px 10px;
  border:1px solid var(--line);
  border-radius:999px;
  background:var(--panel-alt);
  font-size:12px;
  white-space:nowrap;
}
.metric-pill strong{font-size:13px}
.toolbar-actions{display:flex;align-items:center;gap:8px;justify-content:flex-end;flex-wrap:wrap}
.toolbar-controls{
  display:grid;
  grid-template-columns:minmax(240px,1.4fr) minmax(150px,.8fr) minmax(150px,.8fr);
  gap:8px;
  grid-column:1 / span 4;
}
.toolbar-controls label{display:grid;gap:4px;color:var(--muted);font-size:12px}
.toolbar-controls input,.toolbar-controls select,.pager select{
  width:100%;
  border:1px solid var(--border);
  border-radius:8px;
  background:var(--panel);
  padding:9px 10px;
  color:var(--text);
}
.layout{min-height:0;display:grid;grid-template-columns:minmax(0,7fr) minmax(320px,3fr);gap:10px}
.panel{
  min-height:0;
  min-width:0;
  overflow:hidden;
  display:grid;
  grid-template-rows:auto minmax(0,1fr) auto;
  background:var(--panel);
  border:1px solid var(--border);
  border-radius:8px;
  box-shadow:var(--shadow);
}
.panel-head{
  display:flex;
  align-items:center;
  justify-content:space-between;
  gap:8px;
  padding:12px 14px;
  border-bottom:1px solid var(--line);
}
.panel-head h2{margin:0;font-size:14px}
.count{color:var(--muted);font-size:12px;overflow-wrap:anywhere}
.list{min-height:0;overflow:auto;display:grid;align-content:start}
.list-stack{display:grid;gap:10px;padding:12px}
.flow-card{
  border:1px solid var(--line);
  border-radius:8px;
  background:linear-gradient(180deg,var(--panel),var(--panel-alt));
  overflow:hidden;
}
.flow-summary{
  padding:12px 14px;
  display:grid;
  grid-template-columns:minmax(0,1fr) auto;
  gap:12px;
  align-items:start;
}
.flow-main{display:grid;gap:8px}
.flow-title-row{display:flex;align-items:flex-start;gap:10px}
.flow-title{font-size:16px;font-weight:800;overflow-wrap:anywhere}
.intent{color:var(--muted);font-size:13px}
.chip-row{display:flex;flex-wrap:wrap;gap:8px}
.chip{
  display:inline-flex;
  align-items:center;
  gap:6px;
  padding:4px 8px;
  border:1px solid var(--line);
  border-radius:999px;
  background:var(--panel-alt);
  font-size:12px;
  overflow-wrap:anywhere;
}
.chip-dot{width:7px;height:7px;border-radius:999px;background:var(--team)}
.lane{
  position:relative;
  display:grid;
  gap:10px;
  padding:0 14px 14px 32px;
}
.lane::before{
  content:"";
  position:absolute;
  left:18px;
  top:0;
  bottom:14px;
  width:2px;
  border-radius:999px;
  background:linear-gradient(180deg,var(--codex),var(--team),var(--up));
}
.node{
  position:relative;
  display:grid;
  gap:8px;
  padding:12px;
  border:1px solid var(--line);
  border-radius:8px;
  background:var(--panel);
}
.node::before{
  content:"";
  position:absolute;
  left:-20px;
  top:18px;
  width:10px;
  height:10px;
  border-radius:999px;
  background:var(--team);
  border:2px solid var(--panel);
  box-shadow:0 0 0 1px var(--border);
}
.node.result::before{background:var(--up)}
.node.event::before{background:var(--codex)}
.node-top{display:flex;align-items:flex-start;gap:8px}
.node-title{font-weight:700;overflow-wrap:anywhere}
.item-time{margin-left:auto;color:var(--muted);font-size:12px;white-space:nowrap}
.node-meta{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:6px 12px;color:var(--muted);font-size:12px}
.metric-grid{display:grid;grid-template-columns:repeat(4,minmax(0,1fr));gap:8px}
.metric{
  background:var(--panel-soft);
  border:1px solid var(--line);
  border-radius:8px;
  padding:8px;
}
.metric-label{font-size:11px;color:var(--muted);text-transform:uppercase;letter-spacing:.04em}
.metric-value{margin-top:4px;font-weight:700;overflow-wrap:anywhere}
.pill{
  display:inline-flex;
  align-items:center;
  border-radius:999px;
  padding:3px 9px;
  font-size:12px;
  border:1px solid var(--border);
  background:var(--panel-soft);
  white-space:nowrap;
}
.pill.open{background:var(--status-open)}
.pill.claimed{background:var(--status-claimed)}
.pill.done,.pill.completed{background:var(--status-done)}
.pill.failed{background:var(--status-failed)}
.ghost-button{background:transparent}
.project-badge{
  display:inline-flex;
  align-items:center;
  gap:6px;
  padding:5px 10px;
  border-radius:999px;
  background:var(--panel-soft);
  border:1px solid var(--line);
  font-size:12px;
}
.operations-card{
  border-top:1px solid var(--line);
  background:var(--panel-alt);
  padding:12px 14px;
  display:grid;
  gap:10px;
}
.operations-title{font-size:12px;font-weight:800;letter-spacing:.08em;text-transform:uppercase;color:var(--muted)}
.operation-row{
  display:flex;
  align-items:flex-start;
  justify-content:space-between;
  gap:10px;
  padding:8px 0;
  border-bottom:1px solid var(--line);
}
.operation-row:last-child{border-bottom:0}
.operation-main{display:grid;gap:4px}
.operation-name{font-weight:700;overflow-wrap:anywhere}
.operation-meta{font-size:12px;color:var(--muted);overflow-wrap:anywhere}
.side-panel{min-height:0;display:grid;grid-template-rows:auto minmax(0,1fr)}
.inspector-tabs{
  display:flex;
  gap:8px;
  padding:10px 12px;
  border-bottom:1px solid var(--line);
  background:var(--panel-alt);
  flex-wrap:wrap;
}
.inspector-tabs button.active{
  background:var(--panel);
  border-color:var(--border);
}
.inspector{
  min-height:0;
  overflow:auto;
  padding:14px;
  display:grid;
  gap:14px;
}
.inspector h3{margin:0;font-size:16px}
.inspector-block{
  border:1px solid var(--line);
  border-radius:8px;
  background:var(--panel-alt);
  padding:12px;
  display:grid;
  gap:8px;
}
.inspector-label{font-size:11px;text-transform:uppercase;letter-spacing:.05em;color:var(--muted)}
.inspector pre{
  margin:0;
  white-space:pre-wrap;
  overflow-wrap:anywhere;
  font-family:Consolas,Monaco,monospace;
  font-size:12px;
}
.pager{
  display:flex;
  align-items:center;
  gap:8px;
  flex-wrap:wrap;
  padding:10px 14px 12px;
  border-top:1px solid var(--line);
  background:var(--panel-alt);
}
.pager .range{color:var(--muted);font-size:12px;margin-right:auto}
.pager .page-size{display:flex;align-items:center;gap:6px;color:var(--muted);font-size:12px}
.drawer{
  position:fixed;
  right:16px;
  bottom:16px;
  width:min(720px,calc(100vw - 32px));
  max-height:min(620px,calc(100vh - 32px));
  display:grid;
  grid-template-rows:auto minmax(0,1fr);
  background:var(--panel);
  border:1px solid var(--border);
  border-radius:8px;
  box-shadow:var(--shadow);
  z-index:5;
}
.drawer[hidden]{display:none}
.drawer-head{display:flex;align-items:center;justify-content:space-between;gap:8px;padding:12px 14px;border-bottom:1px solid var(--line)}
.drawer-title{font-weight:700;overflow-wrap:anywhere}
.drawer pre{margin:0;padding:14px;overflow:auto;white-space:pre-wrap;overflow-wrap:anywhere;font-family:Consolas,Monaco,monospace;font-size:12px}
.empty{padding:20px 12px;color:var(--muted);font-size:13px}
@media (max-width:980px){
  .topbar{align-items:flex-start;flex-direction:column}
  .actions{justify-content:flex-start}
  .last-update{text-align:left}
  .content{padding:10px}
  .meta-toolbar{grid-template-columns:1fr}
  .toolbar-controls{grid-template-columns:1fr;grid-column:auto}
  .toolbar-actions{justify-content:flex-start}
  .layout{grid-template-columns:1fr}
  .metric-grid{grid-template-columns:repeat(2,minmax(0,1fr))}
}
""");
        html.AppendLine("</style>");
        html.AppendLine("</head>");
        html.AppendLine("<body>");
        html.AppendLine($"<div id=\"app\" data-bus-root=\"{H(snapshot.BusRoot)}\"></div>");
        html.AppendLine("<script>");
        html.Append("window.__CTU_INITIAL_SNAPSHOT__ = ");
        html.Append(initialJson);
        html.AppendLine(";");
        html.AppendLine("""
(() => {
  const initial = window.__CTU_INITIAL_SNAPSHOT__ || { busRoot: "", generatedAt: new Date().toISOString(), stats: {}, agents: [], tasks: [], results: [], events: [] };
  const app = document.getElementById("app");
  const busRoot = app.dataset.busRoot || initial.busRoot || "";
  const clearStorageKey = `ctu-dashboard-clear-after:${busRoot}`;
  const themeStorageKey = "ctu-dashboard-theme";
  const root = document.documentElement;
  const state = {
    snapshot: initial,
    live: true,
    timer: null,
    clearAfter: readStorage(clearStorageKey),
    filters: { text: "", agent: "", status: "" },
    page: { index: 1, size: 10 },
    theme: readStorage(themeStorageKey) || "system",
    inspectorMode: "flow",
    selectedFlowId: "",
    selectedOperationId: "",
    expandedFlows: {},
    lastError: ""
  };
  const refs = {};

  applyTheme();

  function mount() {
    app.innerHTML = `
      <div class="dashboard-shell">
        <header class="topbar">
          <div class="brand">
            <div class="brand-mark" aria-hidden="true"></div>
            <div class="brand-copy">
              <h1><span class="codex">Codex</span><span class="team">Team</span><span class="up">Up</span></h1>
              <p>CodexTeamUp communication flows</p>
            </div>
          </div>
          <div class="actions">
            <div class="segment" data-ref="themeSegment">
              <button type="button" data-theme-value="system">System</button>
              <button type="button" data-theme-value="light">Light</button>
              <button type="button" data-theme-value="dark">Dark</button>
            </div>
            <button type="button" data-ref="liveToggle">Pause</button>
            <span class="last-update" data-ref="lastUpdate">Last update pending</span>
            <button type="button" data-ref="clearView">Clear view</button>
            <button type="button" data-ref="resetView">Reset view</button>
          </div>
        </header>
        <div class="content">
          <div class="watermark" data-ref="watermark"></div>
          <section class="meta-toolbar">
            <div class="project-inline">
              <div class="project-inline-name" data-ref="projectTitle"></div>
              <div class="project-inline-meta" data-ref="projectMeta"></div>
            </div>
            <div class="metric-pills" data-ref="metricPills"></div>
            <section class="toolbar-controls">
              <label>Text <input type="search" data-ref="textFilter" placeholder="Filter communication"></label>
              <label>Agent <select data-ref="agentFilter"><option value="">All agents</option></select></label>
              <label>Status <select data-ref="statusFilter"><option value="">All statuses</option></select></label>
            </section>
            <div class="toolbar-actions">
              <button type="button" data-ref="relationshipsButton">Relationships</button>
              <button type="button" data-ref="projectButton">Project</button>
            </div>
            <div class="count" data-ref="summaryCount"></div>
          </section>
          <main class="layout">
            <section class="panel">
              <div class="panel-head"><h2>Communication</h2><span class="count" data-ref="flowCount"></span></div>
              <div class="list"><div class="list-stack" data-ref="communication"></div></div>
              <div class="pager" data-ref="pager"></div>
            </section>
            <aside class="panel side-panel">
              <div class="panel-head"><h2>Inspector</h2><span class="count" data-ref="inspectorHint"></span></div>
              <div class="inspector-tabs">
                <button type="button" data-ref="flowTab">Flow</button>
                <button type="button" data-ref="relationshipsTab">Relationships</button>
                <button type="button" data-ref="projectTab">Project</button>
              </div>
              <div class="inspector" data-ref="inspector"></div>
            </aside>
          </main>
        </div>
        <aside class="drawer" data-ref="drawer" hidden>
          <div class="drawer-head"><div class="drawer-title" data-ref="drawerTitle"></div><button type="button" data-ref="drawerClose">Close</button></div>
          <pre data-ref="drawerBody"></pre>
        </aside>
      </div>`;

    for (const node of app.querySelectorAll("[data-ref]")) {
      refs[node.dataset.ref] = node;
    }

    refs.liveToggle.addEventListener("click", () => {
      state.live = !state.live;
      refs.liveToggle.textContent = state.live ? "Pause" : "Live";
      schedule();
      render();
      if (state.live) {
        refresh();
      }
    });
    refs.clearView.addEventListener("click", () => {
      state.clearAfter = new Date().toISOString();
      writeStorage(clearStorageKey, state.clearAfter);
      resetPaging();
      render();
    });
    refs.resetView.addEventListener("click", () => {
      state.clearAfter = "";
      writeStorage(clearStorageKey, "");
      resetPaging();
      render();
    });
    refs.drawerClose.addEventListener("click", () => {
      refs.drawer.hidden = true;
    });
    refs.textFilter.addEventListener("input", () => {
      state.filters.text = refs.textFilter.value.trim().toLowerCase();
      resetPaging();
      render();
    });
    refs.agentFilter.addEventListener("change", () => {
      state.filters.agent = refs.agentFilter.value;
      resetPaging();
      render();
    });
    refs.statusFilter.addEventListener("change", () => {
      state.filters.status = refs.statusFilter.value;
      resetPaging();
      render();
    });

    refs.relationshipsButton.addEventListener("click", () => {
      state.inspectorMode = "relationships";
      render();
    });
    refs.projectButton.addEventListener("click", () => {
      state.inspectorMode = "project";
      render();
    });
    refs.flowTab.addEventListener("click", () => {
      state.inspectorMode = "flow";
      render();
    });
    refs.relationshipsTab.addEventListener("click", () => {
      state.inspectorMode = "relationships";
      render();
    });
    refs.projectTab.addEventListener("click", () => {
      state.inspectorMode = "project";
      render();
    });

    for (const button of refs.themeSegment.querySelectorAll("button[data-theme-value]")) {
      button.addEventListener("click", () => {
        state.theme = button.dataset.themeValue;
        writeStorage(themeStorageKey, state.theme);
        applyTheme();
        renderThemeSegment();
      });
    }

    renderThemeSegment();
    render();
    refresh();
    schedule();
  }

  function schedule() {
    if (state.timer) {
      clearInterval(state.timer);
      state.timer = null;
    }
    if (state.live && canFetch()) {
      state.timer = setInterval(refresh, 2000);
    }
  }

  async function refresh() {
    if (!canFetch()) {
      state.lastError = "Standalone snapshot";
      render();
      return;
    }

    try {
      const url = new URL("/api/snapshot", window.location.origin);
      if (busRoot) {
        url.searchParams.set("busRoot", busRoot);
      }
      const response = await fetch(url, { cache: "no-store" });
      if (!response.ok) {
        throw new Error(`HTTP ${response.status}`);
      }
      state.snapshot = await response.json();
      state.lastError = "";
      render();
    } catch (error) {
      state.lastError = error instanceof Error ? error.message : String(error);
      render();
    }
  }

  function render() {
    const snapshot = normalizeSnapshot(state.snapshot);
    updateFilters(snapshot);
    const model = buildCommunicationModel(snapshot);
    ensureSelection(model);

    renderWatermark();
    refs.lastUpdate.textContent = lastUpdateText(snapshot);
    refs.flowCount.textContent = `${model.flows.length} flows, ${model.operations.length} operations`;
    refs.summaryCount.textContent = `${model.filteredTaskCount} task threads visible`;
    renderMetaToolbar(snapshot, model);
    renderCommunication(model);
    renderInspector(model, snapshot);
    renderPager(refs.pager, state.page, model.flows.length, render);
  }

  function normalizeSnapshot(snapshot) {
    return {
      busRoot: snapshot.busRoot || busRoot,
      generatedAt: snapshot.generatedAt || new Date().toISOString(),
      stats: snapshot.stats || {},
      agents: Array.isArray(snapshot.agents) ? snapshot.agents : [],
      tasks: Array.isArray(snapshot.tasks) ? snapshot.tasks : [],
      results: Array.isArray(snapshot.results) ? snapshot.results : [],
      events: Array.isArray(snapshot.events) ? snapshot.events : []
    };
  }

  function renderMetaToolbar(snapshot, model) {
    const projectName = snapshot.tasks.find(task => task.project)?.project || "codexteamup";
    refs.projectTitle.textContent = projectName;
    refs.projectMeta.textContent = `${snapshot.agents.length} agents across ${model.edgeCount} communication edges`;
    const stats = snapshot.stats || {};
    const pills = [
      ["Flows", model.flows.length],
      ["Agents", stats.agents ?? snapshot.agents.length],
      ["Open", stats.openTasks ?? countStatus(snapshot.tasks, "open")],
      ["Claimed", stats.claimedTasks ?? countStatus(snapshot.tasks, "claimed")],
      ["Done", stats.doneTasks ?? countStatus(snapshot.tasks, "done")],
      ["Failed", stats.failedTasks ?? countStatus(snapshot.tasks, "failed")]
    ];
    refs.metricPills.replaceChildren(...pills.map(([label, value]) => {
      const pill = el("div", "metric-pill");
      pill.append(el("span", "", label), el("strong", "", String(value)));
      return pill;
    }));
  }

  function renderWatermark() {
    const parts = [];
    if (state.clearAfter) {
      parts.push(`View cleared since ${formatDate(state.clearAfter)}. Open and claimed tasks stay visible.`);
    }
    if (state.lastError) {
      parts.push(state.lastError);
    }
    refs.watermark.textContent = parts.join(" ");
  }

  function renderCommunication(model) {
    const visibleFlows = pageSlice(model.flows, state.page);
    if (visibleFlows.length === 0 && model.operations.length === 0) {
      refs.communication.replaceChildren(el("div", "empty", "No communication matches the current view."));
      return;
    }

    const nodes = visibleFlows.map(renderFlowCard);
    if (model.operations.length > 0) {
      nodes.push(renderOperationsCard(model.operations));
    }
    refs.communication.replaceChildren(...nodes);
  }

  function renderFlowCard(flow) {
    const card = el("article", "flow-card");
    const summary = el("div", "flow-summary");
    const main = el("div", "flow-main");
    const titleRow = el("div", "flow-title-row");
    titleRow.append(pill(flow.status));
    titleRow.append(el("div", "flow-title", flow.label));
    main.append(titleRow);
    main.append(el("div", "intent", flow.intent));

    const chipRow = el("div", "chip-row");
    chipRow.append(el("div", "project-badge", flow.project));
    for (const participant of flow.participants) {
      chipRow.append(agentChip(participant));
    }
    main.append(chipRow);

    const meta = el("div", "node-meta");
    meta.append(metricCell("Route", `${flow.from} -> ${flow.to}`));
    meta.append(metricCell("Timing", flow.timingText));
    meta.append(metricCell("Counts", `${flow.taskCount} tasks / ${flow.resultCount} results / ${flow.eventCount} events`));
    meta.append(metricCell("Latest", formatDate(flow.lastUpdate)));
    main.append(meta);
    summary.append(main);

    const side = el("div", "metric-grid");
    side.append(metricBlock("Open", String(flow.openCount)));
    side.append(metricBlock("Results", String(flow.resultCount)));
    side.append(metricBlock("Events", String(flow.eventCount)));
    side.append(metricBlock("Speed", flow.responseSpeed));
    summary.append(side);
    card.append(summary);

    const controls = el("div", "operations-card");
    const actions = el("div", "chip-row");
    const expanded = !!state.expandedFlows[flow.id];
    const toggle = el("button", "ghost-button", expanded ? "Collapse flow" : "Expand flow");
    toggle.type = "button";
    toggle.addEventListener("click", () => {
      state.expandedFlows[flow.id] = !expanded;
      render();
    });
    const inspect = el("button", "ghost-button", "Open inspector");
    inspect.type = "button";
    inspect.addEventListener("click", () => {
      state.selectedFlowId = flow.id;
      state.selectedOperationId = "";
      state.inspectorMode = "flow";
      render();
    });
    const detail = el("button", "ghost-button", "Details");
    detail.type = "button";
    detail.addEventListener("click", () => openDrawer(`Flow: ${flow.label}`, serializeFlow(flow)));
    actions.append(toggle, inspect, detail);
    controls.append(actions);
    card.append(controls);

    if (expanded) {
      const lane = el("div", "lane");
      for (const item of flow.timeline) {
        lane.append(renderTimelineNode(item));
      }
      card.append(lane);
    }

    return card;
  }

  function renderTimelineNode(item) {
    const node = el("article", `node ${item.kind}`);
    const top = el("div", "node-top");
    top.append(pill(item.status || item.kind));
    top.append(el("div", "node-title", item.title));
    top.append(el("div", "item-time", item.timeText));
    node.append(top);

    const chips = el("div", "chip-row");
    if (item.project) {
      chips.append(el("div", "project-badge", item.project));
    }
    for (const agent of item.participants || []) {
      chips.append(agentChip(agent));
    }
    node.append(chips);

    const meta = el("div", "node-meta");
    meta.append(metricCell("Route", item.routeText || "system"));
    meta.append(metricCell("Timing", item.durationText || "n/a"));
    meta.append(metricCell("Intent", item.intent || "General coordination"));
    meta.append(metricCell("Counts", item.countText || ""));
    node.append(meta);

    const actions = el("div", "chip-row");
    const inspect = el("button", "ghost-button", "Inspect");
    inspect.type = "button";
    inspect.addEventListener("click", () => {
      if (item.kind === "event" && !item.taskId) {
        state.selectedOperationId = item.id;
        state.inspectorMode = "relationships";
      } else {
        state.selectedFlowId = item.flowId;
        state.selectedOperationId = "";
        state.inspectorMode = "flow";
      }
      render();
    });
    const detail = el("button", "ghost-button", "Full text");
    detail.type = "button";
    detail.addEventListener("click", () => openDrawer(item.title, item.fullText));
    actions.append(inspect, detail);
    node.append(actions);
    return node;
  }

  function renderOperationsCard(operations) {
    const card = el("section", "flow-card");
    const summary = el("div", "flow-summary");
    const main = el("div", "flow-main");
    main.append(el("div", "flow-title", "Operations / System"));
    main.append(el("div", "intent", "Agent setup, result notifications, and other system events detached from a task thread."));
    summary.append(main);
    const side = el("div", "metric-grid");
    side.append(metricBlock("Operations", String(operations.length)));
    summary.append(side);
    card.append(summary);

    const body = el("div", "operations-card");
    for (const op of operations) {
      const row = el("div", "operation-row");
      const left = el("div", "operation-main");
      left.append(el("div", "operation-name", op.title));
      left.append(el("div", "operation-meta", `${op.routeText || "system"} | ${formatDate(op.timestamp)}`));
      row.append(left);
      const actions = el("div", "chip-row");
      const inspect = el("button", "ghost-button", "Inspect");
      inspect.type = "button";
      inspect.addEventListener("click", () => {
        state.selectedOperationId = op.id;
        state.inspectorMode = "relationships";
        render();
      });
      actions.append(inspect);
      row.append(actions);
      body.append(row);
    }
    card.append(body);
    return card;
  }

  function renderInspector(model, snapshot) {
    refs.flowTab.classList.toggle("active", state.inspectorMode === "flow");
    refs.relationshipsTab.classList.toggle("active", state.inspectorMode === "relationships");
    refs.projectTab.classList.toggle("active", state.inspectorMode === "project");

    if (state.inspectorMode === "project") {
      refs.inspectorHint.textContent = "Project overview";
      refs.inspector.replaceChildren(renderProjectInspector(snapshot, model));
      return;
    }
    if (state.inspectorMode === "relationships") {
      refs.inspectorHint.textContent = "Relationships";
      refs.inspector.replaceChildren(renderRelationshipsInspector(model));
      return;
    }

    refs.inspectorHint.textContent = "Flow selected";
    const flow = model.flows.find(item => item.id === state.selectedFlowId) || model.flows[0];
    if (!flow) {
      refs.inspector.replaceChildren(el("div", "empty", "Select a communication flow or operation to inspect."));
      return;
    }
    state.selectedFlowId = flow.id;
    refs.inspector.replaceChildren(renderFlowInspector(flow));
  }

  function renderFlowInspector(flow) {
    const container = document.createDocumentFragment();
    container.append(inspectorBlock("Flow", flow.label, flow.intent));
    container.append(inspectorBlock("Participants", flow.participants.join(", "), `${flow.from} -> ${flow.to}`));
    container.append(inspectorBlock("Timeline", flow.timeline.map(item => `${item.kind.toUpperCase()} | ${item.title} | ${item.routeText || "system"} | ${item.timeText}`).join("\n")));
    container.append(inspectorBlock("Payload", serializeFlow(flow)));
    return container;
  }

  function renderRelationshipsInspector(model) {
    const container = document.createDocumentFragment();
    container.append(inspectorBlock("Relationships", `${model.edgeCount} communication edges`, `${model.flows.length} visible flows`));
    container.append(inspectorBlock("Edges", model.edgeSummary.map(edge => `${edge.from} -> ${edge.to} (${edge.count})`).join("\n") || "No edges"));
    if (state.selectedOperationId) {
      const operation = model.operations.find(item => item.id === state.selectedOperationId);
      if (operation) {
        container.append(inspectorBlock("Operation", operation.title, operation.routeText || "system"));
        container.append(inspectorBlock("Operation payload", JSON.stringify(operation.raw, null, 2)));
      }
    }
    return container;
  }

  function renderProjectInspector(snapshot, model) {
    const container = document.createDocumentFragment();
    const projectName = snapshot.tasks.find(task => task.project)?.project || "codexteamup";
    container.append(inspectorBlock("Project", projectName, `${snapshot.agents.length} registered agents`));
    container.append(inspectorBlock("Agents", snapshot.agents.map(agent => `${agent.id} | ${agent.role || "unknown role"}`).join("\n")));
    container.append(inspectorBlock("Communication summary", `${model.filteredTaskCount} task threads visible\n${model.edgeCount} edges observed`));
    return container;
  }

  function inspectorBlock(label, body, extra) {
    const block = el("section", "inspector-block");
    block.append(el("div", "inspector-label", label));
    block.append(el("h3", "", body || "n/a"));
    if (extra) {
      const pre = document.createElement("pre");
      pre.textContent = extra;
      block.append(pre);
    }
    return block;
  }

  function buildCommunicationModel(snapshot) {
    const resultsByTask = new Map();
    for (const result of snapshot.results.filter(result => passesWatermark("result", result))) {
      const key = result.taskId;
      const list = resultsByTask.get(key) || [];
      list.push(result);
      resultsByTask.set(key, list);
    }

    const taskById = new Map(snapshot.tasks.map(task => [task.id, task]));
    const eventsByFlow = new Map();
    const operations = [];
    for (const evt of snapshot.events.filter(evt => passesWatermark("event", evt))) {
      const flowId = flowIdForEvent(evt, snapshot.results);
      const eventEntry = eventToTimeline(evt, flowId, taskById.get(flowId), flowId);
      if (flowId) {
        const list = eventsByFlow.get(flowId) || [];
        list.push(eventEntry);
        eventsByFlow.set(flowId, list);
      } else {
        operations.push(eventEntry);
      }
    }

    const flowMap = new Map();
    for (const task of snapshot.tasks) {
      const flowId = flowIdFor(task);
      const relatedResults = (resultsByTask.get(task.id) || []).map(result => resultToTimeline(result, flowId, task));
      const relatedEvents = eventsByFlow.get(task.id) || [];

      if (!passesFilters("task", task) && !relatedResults.some(item => passesFilters("result", item.raw)) && !relatedEvents.some(item => passesFilters("event", item.raw))) {
        continue;
      }

      const flow = flowMap.get(flowId) || createFlowShell(task, flowId);
      flow.tasks.push(task);
      flow.results.push(...relatedResults);
      flow.events.push(...relatedEvents);
      addSet(flow.participants, task.from);
      addSet(flow.participants, task.to);
      addSet(flow.participants, task.owner);
      flow.openCount += String(task.status || "").toLowerCase() === "open" ? 1 : 0;
      flow.lastUpdate = maxDate(flow.lastUpdate, task.completedAt, task.claimedAt, task.createdAt);
      flowMap.set(flowId, flow);
    }

    const flows = [...flowMap.values()].map(flow => finalizeFlow(flow)).sort((left, right) => timeValue(right.lastUpdate) - timeValue(left.lastUpdate));
    const edgeMap = new Map();
    for (const flow of flows) {
      const key = `${flow.from}|${flow.to}`;
      const edge = edgeMap.get(key) || { from: flow.from, to: flow.to, label: `${flow.from} -> ${flow.to}`, count: 0 };
      edge.count += 1;
      edgeMap.set(key, edge);
    }

    return {
      flows,
      operations: operations.sort((left, right) => timeValue(right.timestamp) - timeValue(left.timestamp)),
      filteredTaskCount: flows.reduce((sum, flow) => sum + flow.taskCount, 0),
      edgeCount: edgeMap.size,
      edgeSummary: [...edgeMap.values()].sort((left, right) => right.count - left.count)
    };
  }

  function createFlowShell(task, flowId) {
    const title = task.title || flowId;
    return {
      id: flowId,
      label: title,
      intent: inferIntent(task.prompt || "", title),
      from: task.from || "unknown",
      to: task.to || "unknown",
      project: task.project || "codexteamup",
      participants: new Set(),
      tasks: [],
      results: [],
      events: [],
      lastUpdate: task.createdAt || new Date(0).toISOString(),
      openCount: 0
    };
  }

  function finalizeFlow(flow) {
    const timeline = [];
    for (const task of flow.tasks.sort((left, right) => timeValue(left.createdAt) - timeValue(right.createdAt))) {
      timeline.push(taskToTimeline(task, flow.id));
      for (const result of flow.results.filter(item => item.taskId === task.id).sort((left, right) => timeValue(left.timestamp) - timeValue(right.timestamp))) {
        timeline.push(result);
      }
      for (const evt of flow.events.filter(item => item.taskId === task.id || item.resultId && item.resultTaskId === task.id).sort((left, right) => timeValue(left.timestamp) - timeValue(right.timestamp))) {
        timeline.push(evt);
      }
    }

    const latestTask = flow.tasks.at(-1);
    const latestResult = flow.results.at(-1);
    return {
      id: flow.id,
      label: flow.label,
      intent: flow.intent,
      from: flow.from,
      to: flow.to,
      project: flow.project,
      participants: [...flow.participants].filter(Boolean).sort(),
      taskCount: flow.tasks.length,
      resultCount: flow.results.length,
      eventCount: flow.events.length,
      openCount: flow.openCount,
      status: latestResult?.status || latestTask?.status || "open",
      lastUpdate: maxDate(flow.lastUpdate, latestResult?.timestamp),
      timingText: formatTiming(flow.tasks[0]?.createdAt, latestResult?.timestamp || latestTask?.completedAt || latestTask?.claimedAt),
      responseSpeed: formatTiming(flow.tasks[0]?.createdAt, latestResult?.timestamp || latestTask?.claimedAt),
      timeline,
      raw: flow
    };
  }

  function taskToTimeline(task, flowId) {
    return {
      id: task.id,
      flowId,
      kind: "task",
      status: task.status || "open",
      title: task.title || task.id,
      intent: inferIntent(task.prompt || "", task.title || task.id),
      project: task.project || "codexteamup",
      routeText: `${task.from || "unknown"} -> ${task.to || "unknown"}`,
      durationText: formatTiming(task.createdAt, task.claimedAt || task.completedAt),
      countText: task.resultExpected === false ? "No result expected" : "Result expected",
      participants: [task.from, task.to, task.owner].filter(Boolean),
      timeText: age(task.createdAt),
      timestamp: task.createdAt,
      fullText: task.prompt || task.title || task.id,
      taskId: task.id,
      raw: task
    };
  }

  function resultToTimeline(result, flowId, task) {
    return {
      id: result.id,
      flowId,
      kind: "result",
      status: result.status || "completed",
      title: "Result",
      intent: result.summary || "Worker outcome",
      project: task?.project || "codexteamup",
      routeText: `${result.from || "unknown"} -> ${result.to || "unknown"}`,
      durationText: formatTiming(task?.createdAt, result.completedAt),
      countText: result.tests?.length ? `${result.tests.length} tests` : "No tests listed",
      participants: [result.from, result.to].filter(Boolean),
      timeText: formatDate(result.completedAt),
      timestamp: result.completedAt,
      fullText: [result.summary, result.tests?.length ? `Tests: ${result.tests.join(", ")}` : "", result.openQuestions?.length ? `Open questions: ${result.openQuestions.join(", ")}` : ""].filter(Boolean).join("\n"),
      taskId: result.taskId,
      raw: result
    };
  }

  function eventToTimeline(evt, flowId, task, resultTaskId) {
    return {
      id: `${evt.type}-${evt.timestamp}-${evt.taskId || evt.resultId || "system"}`,
      flowId: flowId || "",
      kind: "event",
      status: evt.type,
      title: evt.message || evt.type,
      intent: "System operation",
      project: task?.project || "codexteamup",
      routeText: evt.from || evt.to ? `${evt.from || "system"} -> ${evt.to || "system"}` : "system",
      durationText: "event",
      countText: evt.resultId ? `result ${evt.resultId}` : evt.taskId ? `task ${evt.taskId}` : "system event",
      participants: [evt.from, evt.to].filter(Boolean),
      timeText: formatDate(evt.timestamp),
      timestamp: evt.timestamp,
      fullText: evt.message || evt.type,
      taskId: evt.taskId,
      resultId: evt.resultId,
      resultTaskId: resultTaskId || "",
      raw: evt
    };
  }

  function flowIdFor(task) {
    return task.conversationId || task.id;
  }

  function flowIdForEvent(evt, results) {
    if (evt.taskId) {
      return evt.taskId;
    }
    if (evt.resultId) {
      const result = results.find(item => item.id === evt.resultId);
      return result?.taskId || "";
    }
    return "";
  }

  function inferIntent(prompt, title) {
    const source = `${title} ${prompt}`.toLowerCase();
    if (source.includes("dashboard")) return "Dashboard work";
    if (source.includes("test")) return "Verification";
    if (source.includes("ux")) return "UX analysis";
    if (source.includes("review")) return "Review";
    if (source.includes("flow")) return "Communication flow";
    return title || "General coordination";
  }

  function ensureSelection(model) {
    if (state.selectedFlowId && model.flows.some(item => item.id === state.selectedFlowId)) {
      return;
    }
    state.selectedFlowId = model.flows[0]?.id || "";
  }

  function updateFilters(snapshot) {
    const selectedAgent = refs.agentFilter.value;
    const selectedStatus = refs.statusFilter.value;
    const agents = new Set(snapshot.agents.map(agent => agent.id).filter(Boolean));
    const statuses = new Set();
    for (const task of snapshot.tasks) {
      addSet(agents, task.from);
      addSet(agents, task.to);
      addSet(agents, task.owner);
      addSet(statuses, task.status);
    }
    for (const result of snapshot.results) {
      addSet(agents, result.from);
      addSet(agents, result.to);
      addSet(statuses, result.status);
    }
    for (const evt of snapshot.events) {
      addSet(agents, evt.from);
      addSet(agents, evt.to);
      addSet(statuses, evt.type);
    }
    replaceOptions(refs.agentFilter, "All agents", [...agents].sort(), selectedAgent);
    replaceOptions(refs.statusFilter, "All statuses", [...statuses].sort(), selectedStatus);
  }

  function replaceOptions(select, firstText, values, selected) {
    const options = [new Option(firstText, "")].concat(values.map(value => new Option(value, value)));
    select.replaceChildren(...options);
    select.value = values.includes(selected) ? selected : "";
    if (select.dataset.ref === "agentFilter") {
      state.filters.agent = select.value;
    } else {
      state.filters.status = select.value;
    }
  }

  function passesFilters(kind, item) {
    if (!passesWatermark(kind, item)) {
      return false;
    }
    if (state.filters.text && !JSON.stringify(item).toLowerCase().includes(state.filters.text)) {
      return false;
    }
    if (state.filters.agent) {
      const fields = [item.id, item.from, item.to, item.owner].filter(Boolean);
      if (!fields.some(value => value.toLowerCase() === state.filters.agent.toLowerCase())) {
        return false;
      }
    }
    if (state.filters.status) {
      const status = kind === "event" ? item.type : item.status;
      if (!status || status.toLowerCase() !== state.filters.status.toLowerCase()) {
        return false;
      }
    }
    return true;
  }

  function passesWatermark(kind, item) {
    if (!state.clearAfter) {
      return true;
    }
    if (kind === "task" && ["open", "claimed"].includes(String(item.status || "").toLowerCase())) {
      return true;
    }
    const source = item.completedAt || item.timestamp || item.claimedAt || item.createdAt;
    const itemTime = Date.parse(source || "");
    const clearTime = Date.parse(state.clearAfter);
    return Number.isNaN(itemTime) || Number.isNaN(clearTime) || itemTime >= clearTime;
  }

  function renderPager(container, pageState, total, rerender) {
    const maxPage = Math.max(1, Math.ceil(total / pageState.size));
    if (pageState.index > maxPage) {
      pageState.index = maxPage;
    }
    const start = total === 0 ? 0 : (pageState.index - 1) * pageState.size + 1;
    const end = Math.min(total, pageState.index * pageState.size);
    const range = el("span", "range", `${start}-${end} of ${total} | Page ${pageState.index} of ${maxPage}`);
    const prev = pagerButton("Prev", pageState.index <= 1, () => {
      pageState.index = Math.max(1, pageState.index - 1);
      rerender();
    });
    const next = pagerButton("Next", pageState.index >= maxPage, () => {
      pageState.index = Math.min(maxPage, pageState.index + 1);
      rerender();
    });
    const sizeWrap = el("label", "page-size", "Page size");
    const size = document.createElement("select");
    for (const value of [10, 25, 50]) {
      size.append(new Option(String(value), String(value)));
    }
    size.value = String(pageState.size);
    size.addEventListener("change", () => {
      pageState.size = Number(size.value);
      pageState.index = 1;
      rerender();
    });
    sizeWrap.append(size);
    container.replaceChildren(range, prev, next, sizeWrap);
  }

  function pageSlice(items, pageState) {
    const maxPage = Math.max(1, Math.ceil(items.length / pageState.size));
    if (pageState.index > maxPage) {
      pageState.index = maxPage;
    }
    const start = (pageState.index - 1) * pageState.size;
    return items.slice(start, start + pageState.size);
  }

  function resetPaging() {
    state.page.index = 1;
  }

  function pagerButton(text, disabled, onClick) {
    const node = el("button", "", text);
    node.type = "button";
    node.disabled = disabled;
    node.addEventListener("click", onClick);
    return node;
  }

  function renderThemeSegment() {
    for (const button of refs.themeSegment.querySelectorAll("button[data-theme-value]")) {
      button.classList.toggle("active", button.dataset.themeValue === state.theme);
    }
  }

  function applyTheme() {
    root.setAttribute("data-theme", state.theme);
    root.style.colorScheme = state.theme === "system" ? "light dark" : state.theme;
  }

  function openDrawer(title, body) {
    refs.drawerTitle.textContent = title;
    refs.drawerBody.textContent = body;
    refs.drawer.hidden = false;
  }

  function serializeFlow(flow) {
    return JSON.stringify(flow.raw || flow, null, 2);
  }

  function countStatus(items, status) {
    return items.filter(item => String(item.status || "").toLowerCase() === status).length;
  }

  function pill(value) {
    const text = String(value || "unknown");
    return el("span", `pill ${text.toLowerCase().replace(/[^a-z0-9_-]+/g, "-")}`, text);
  }

  function agentChip(value) {
    const chip = el("div", "chip");
    chip.append(el("span", "chip-dot"));
    chip.append(document.createTextNode(value));
    return chip;
  }

  function metricBlock(label, value) {
    const block = el("div", "metric");
    block.append(el("div", "metric-label", label), el("div", "metric-value", value || "n/a"));
    return block;
  }

  function metricCell(label, value) {
    return metricBlock(label, value || "n/a");
  }

  function addSet(set, value) {
    if (value) {
      set.add(value);
    }
  }

  function maxDate(...values) {
    let best = "";
    let bestValue = 0;
    for (const value of values.filter(Boolean)) {
      const parsed = timeValue(value);
      if (parsed >= bestValue) {
        bestValue = parsed;
        best = value;
      }
    }
    return best;
  }

  function formatTiming(start, end) {
    const startValue = timeValue(start);
    const endValue = timeValue(end);
    if (!startValue || !endValue || endValue < startValue) {
      return "n/a";
    }
    const minutes = Math.round((endValue - startValue) / 60000);
    if (minutes < 60) {
      return `${minutes}m`;
    }
    return `${(minutes / 60).toFixed(1)}h`;
  }

  function timeValue(value) {
    const parsed = Date.parse(value || "");
    return Number.isNaN(parsed) ? 0 : parsed;
  }

  function lastUpdateText(snapshot) {
    const prefix = state.live ? "Last update" : "Paused at";
    return `${prefix} ${formatDate(snapshot.generatedAt)}`;
  }

  function formatDate(value) {
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? "" : date.toLocaleString();
  }

  function age(value) {
    const time = Date.parse(value || "");
    if (Number.isNaN(time)) {
      return "";
    }
    const minutes = Math.max(0, Math.round((Date.now() - time) / 60000));
    if (minutes < 60) {
      return `${minutes}m ago`;
    }
    return `${(minutes / 60).toFixed(1)}h ago`;
  }

  function el(tag, className, text) {
    const node = document.createElement(tag);
    if (className) {
      node.className = className;
    }
    if (text !== undefined) {
      node.textContent = text;
    }
    return node;
  }

  function canFetch() {
    return window.location.protocol === "http:" || window.location.protocol === "https:";
  }

  function readStorage(key) {
    try {
      return localStorage.getItem(key) || "";
    } catch {
      return "";
    }
  }

  function writeStorage(key, value) {
    try {
      if (value) {
        localStorage.setItem(key, value);
      } else {
        localStorage.removeItem(key);
      }
    } catch {
    }
  }

  mount();
})();
""");
        html.AppendLine("</script>");
        html.AppendLine("</body>");
        html.AppendLine("</html>");
        return html.ToString();
    }

    private static string H(string? value)
    {
        return WebUtility.HtmlEncode(value ?? string.Empty);
    }
}

public sealed record AgentBusDashboardSnapshot
{
    public required string BusRoot { get; init; }
    public required DateTimeOffset GeneratedAt { get; init; }
    public required AgentBusDashboardStats Stats { get; init; }
    public required IReadOnlyList<AgentDefinition> Agents { get; init; }
    public required IReadOnlyList<AgentBusTask> Tasks { get; init; }
    public required IReadOnlyList<AgentBusResult> Results { get; init; }
    public required IReadOnlyList<AgentBusEvent> Events { get; init; }
}

public sealed record AgentBusDashboardStats
{
    public required int Agents { get; init; }
    public required int Tasks { get; init; }
    public required int OpenTasks { get; init; }
    public required int ClaimedTasks { get; init; }
    public required int DoneTasks { get; init; }
    public required int FailedTasks { get; init; }
    public required int Results { get; init; }
    public required int Events { get; init; }
}
