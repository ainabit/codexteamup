namespace CodexTeamUp.AgentBus;

/// <summary>
/// Resolves the standard CTU state directories for a checkout or AgentBus root.
/// </summary>
public static class CtuProjectLayout
{
    public static string NormalizeCheckoutPath(string checkoutPath)
        => Path.GetFullPath(checkoutPath);

    public static string NormalizeBusRoot(string busRoot)
        => Path.GetFullPath(busRoot);

    public static string ProjectRootForBusRoot(string busRoot)
    {
        var directory = new DirectoryInfo(NormalizeBusRoot(busRoot));
        if (directory.Name.Equals("agentbus", StringComparison.OrdinalIgnoreCase)
            && directory.Parent is not null
            && directory.Parent.Name.Equals(".codexteamup", StringComparison.OrdinalIgnoreCase)
            && directory.Parent.Parent is not null)
        {
            return directory.Parent.Parent.FullName;
        }

        if (directory.Name.Equals(".codexteamup", StringComparison.OrdinalIgnoreCase)
            && directory.Parent is not null)
        {
            return directory.Parent.FullName;
        }

        return directory.FullName;
    }

    public static string StateRootForBusRoot(string busRoot)
        => Path.Combine(ProjectRootForBusRoot(busRoot), ".codexteamup");

    public static string DefaultBusRootForCheckout(string checkoutPath)
        => Path.Combine(NormalizeCheckoutPath(checkoutPath), ".codexteamup", "agentbus");

    public static string RestartRootForBusRoot(string busRoot)
        => Path.Combine(StateRootForBusRoot(busRoot), "restart");

    public static string ExchangeRootForBusRoot(string busRoot)
        => Path.Combine(StateRootForBusRoot(busRoot), "exchange");

    public static string RuntimeCheckpointRootForBusRoot(string busRoot)
        => Path.Combine(StateRootForBusRoot(busRoot), "runtime", "checkpoints");
}
