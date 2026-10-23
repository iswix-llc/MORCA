using System.Globalization;

namespace MORCA.Services;

/// <summary>A cell found by a search: the table, the row's position in that table and the column name.</summary>
public readonly record struct FindHit(string Table, int Row, string Column);

/// <summary>The columns and rows of one table, as the search reads them.</summary>
public sealed record FindTableContent(IReadOnlyList<string> Columns, IReadOnlyList<Dictionary<string, object?>> Rows);

/// <summary>
/// Find / Find Next across every table. Cells are visited table by table in the given order, row by row,
/// column by column, and the search wraps from the last table back to the first.
/// </summary>
public static class TableFinder
{
    /// <summary>What the grid shows for a binary cell; it is not searchable text.</summary>
    public const string BinaryPlaceholder = "[Binary/Stream]";

    /// <summary>
    /// The next cell after <paramref name="after"/> whose text contains <paramref name="text"/> (ignoring case).
    /// With no <paramref name="after"/> the search starts at the first cell of <paramref name="startTable"/>.
    /// The search goes all the way round, so the result can be <paramref name="after"/> itself when it is the
    /// only match. Returns null when nothing matches. A table that <paramref name="load"/> cannot read (returns
    /// null) is skipped.
    /// </summary>
    public static FindHit? FindNext(IReadOnlyList<string> tables, Func<string, FindTableContent?> load,
        string text, string startTable, FindHit? after)
    {
        if (string.IsNullOrEmpty(text) || tables.Count == 0) return null;

        var first = tables.ToList().IndexOf(after?.Table ?? startTable);
        if (first < 0) first = 0;

        // Passes 0..N-1 visit each table once, starting just after "after" in the first. The extra pass N
        // returns to the first table for the cells up to and including "after".
        var passes = after == null ? tables.Count : tables.Count + 1;
        for (var pass = 0; pass < passes; pass++)
        {
            var table = tables[(first + pass) % tables.Count];
            var content = load(table);
            if (content == null || content.Columns.Count == 0) continue;

            var width = content.Columns.Count;
            var last = content.Rows.Count * width - 1;
            var from = 0;
            var to = last;

            if (pass == 0 && after != null)
                from = after.Value.Row * width + content.Columns.ToList().IndexOf(after.Value.Column) + 1;
            else if (pass == tables.Count)
                to = Math.Min(last, after!.Value.Row * width + Math.Max(0, content.Columns.ToList().IndexOf(after.Value.Column)));

            for (var cell = Math.Max(from, 0); cell <= to; cell++)
            {
                var row = content.Rows[cell / width];
                var column = content.Columns[cell % width];
                if (!row.TryGetValue(column, out var value) || value == null) continue;

                var cellText = Convert.ToString(value, CultureInfo.InvariantCulture);
                if (cellText == null || cellText == BinaryPlaceholder) continue;
                if (cellText.Contains(text, StringComparison.OrdinalIgnoreCase))
                    return new FindHit(table, cell / width, column);
            }
        }

        return null;
    }
}
