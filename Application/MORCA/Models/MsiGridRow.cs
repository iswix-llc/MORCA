namespace MORCA.Models;

/// <summary>One row of the table data grid.</summary>
public sealed class MsiGridRow
{
    public MsiGridRow(Dictionary<string, object?> values) => Values = values;

    /// <summary>The row's stored values by column name (int, string or null; a Binary cell holds a placeholder).</summary>
    public Dictionary<string, object?> Values { get; }

    /// <summary>Text typed into cells that are being edited, by column name, until the edit is committed.</summary>
    public Dictionary<string, string> Pending { get; } = new();
}
