namespace CodexTeamUp.Controller;

public sealed class DefaultCtuControllerPlugin : ICtuControllerPlugin
{
    public string Name => "CodexTeamUp default controller";

    public string Version => "0.1.0";

    public ICtuController Create(CtuControllerPluginContext context)
        => new DefaultCtuController(context.DefaultBusRoot, context.AppServer, context.Policy, context.Logger);
}
