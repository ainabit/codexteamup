namespace CodexTeamUp.AppServer;

public sealed record CodexThreadRecord(
    string Id,
    string? Name,
    string? Preview,
    string? Cwd,
    string? Source,
    string? Status,
    DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt,
    string Origin,
    string? StoragePath);

public sealed record CodexThreadItemRecord(
    string Timestamp,
    string Type,
    string Preview);

public sealed record CodexThreadReadResult(
    CodexThreadRecord Thread,
    IReadOnlyList<CodexThreadItemRecord> Items);

public sealed record AppServerCallResult(bool Succeeded, string? ResultJson, string? Error);
