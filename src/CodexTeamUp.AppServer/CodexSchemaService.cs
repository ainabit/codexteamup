using System.Text.Json;
using CodexTeamUp.Core;

namespace CodexTeamUp.AppServer;

public sealed record CodexSchemaSummary(string OutputDirectory, IReadOnlyList<string> Methods);

public sealed class CodexSchemaService
{
    private static readonly string[] InterestingMethods =
    [
        "thread/list",
        "thread/read",
        "thread/start",
        "thread/name/set",
        "thread/resume",
        "thread/fork",
        "turn/start",
        "turn/steer",
        "thread/inject_items",
        "thread/turns/list",
        "thread/turns/items/list"
    ];

    private readonly ProcessRunner _processRunner;

    public CodexSchemaService(ProcessRunner? processRunner = null)
    {
        _processRunner = processRunner ?? new ProcessRunner();
    }

    public async Task<CodexSchemaSummary> GenerateAsync(
        string outputDirectory,
        CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(outputDirectory);
        var result = await _processRunner.RunAsync(
            "codex",
            $"app-server generate-json-schema --experimental --out {Quote(outputDirectory)}",
            timeout: TimeSpan.FromSeconds(30),
            cancellationToken: cancellationToken).ConfigureAwait(false);

        if (!result.Succeeded)
        {
            throw new InvalidOperationException(SafeText.Redact(result.StandardError));
        }

        var clientRequest = Path.Combine(outputDirectory, "ClientRequest.json");
        var methods = File.Exists(clientRequest)
            ? ExtractMethods(clientRequest)
            : [];

        return new CodexSchemaSummary(outputDirectory, methods);
    }

    public IReadOnlyList<string> ExtractMethods(string clientRequestSchemaPath)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(clientRequestSchemaPath));
        var found = new HashSet<string>(StringComparer.Ordinal);
        Scan(doc.RootElement, found);
        return InterestingMethods.Where(found.Contains).ToList();
    }

    private static void Scan(JsonElement element, HashSet<string> found)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Scan(property.Value, found);
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    Scan(item, found);
                }

                break;
            case JsonValueKind.String:
                var value = element.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    found.Add(value);
                }

                break;
        }
    }

    private static string Quote(string value)
    {
        return value.Contains(' ', StringComparison.Ordinal) ? $"\"{value}\"" : value;
    }
}
