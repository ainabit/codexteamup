namespace CodexTeamUp.Core;

public static class Table
{
    public static void Write(TextWriter writer, IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string?>> rows)
    {
        var materialized = rows.Select(r => r.Select(c => SafeText.Preview(c, 120)).ToArray()).ToList();
        var widths = headers.Select(h => h.Length).ToArray();

        foreach (var row in materialized)
        {
            for (var i = 0; i < headers.Count && i < row.Length; i++)
            {
                widths[i] = Math.Max(widths[i], row[i].Length);
            }
        }

        WriteRow(writer, headers, widths);
        WriteRow(writer, widths.Select(w => new string('-', w)).ToArray(), widths);
        foreach (var row in materialized)
        {
            WriteRow(writer, row, widths);
        }
    }

    private static void WriteRow(TextWriter writer, IReadOnlyList<string> row, IReadOnlyList<int> widths)
    {
        for (var i = 0; i < widths.Count; i++)
        {
            if (i > 0)
            {
                writer.Write("  ");
            }

            var value = i < row.Count ? row[i] : string.Empty;
            writer.Write(value.PadRight(widths[i]));
        }

        writer.WriteLine();
    }
}
