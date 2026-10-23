using System.Text.RegularExpressions;

namespace MORCA.Models;

/// <summary>Column data types offered by Orca's Table Schema dialog.</summary>
public enum MsiColumnKind
{
    String,
    LocalizableString,
    Integer,      // 2-byte (SHORT)
    LongInteger,  // 4-byte (LONG)
    Binary        // OBJECT (stream)
}

public static class MsiColumnKindExtensions
{
    public static string Display(this MsiColumnKind kind) => kind switch
    {
        MsiColumnKind.String => "String",
        MsiColumnKind.LocalizableString => "Localizable String",
        MsiColumnKind.Integer => "Integer",
        MsiColumnKind.LongInteger => "Long Integer",
        MsiColumnKind.Binary => "Binary",
        _ => kind.ToString()
    };

    /// <summary>Only string columns have a length.</summary>
    public static bool HasLength(this MsiColumnKind kind) =>
        kind is MsiColumnKind.String or MsiColumnKind.LocalizableString;

    public static bool IsText(this MsiColumnKind kind) => kind.HasLength();

    public static bool IsNumber(this MsiColumnKind kind) =>
        kind is MsiColumnKind.Integer or MsiColumnKind.LongInteger;
}

/// <summary>A column being defined in the Table Schema dialog.</summary>
public class MsiColumnDefinition
{
    public string Name { get; set; } = "";
    public MsiColumnKind Kind { get; set; } = MsiColumnKind.String;
    /// <summary>Maximum characters for string columns; 0 means unlimited.</summary>
    public int Length { get; set; } = 72;
    public bool Nullable { get; set; }

    /// <summary>
    /// When adjusting an existing table, the name this column has in the database now. Its data is
    /// carried over to this column. Null for a column added in the dialog.
    /// </summary>
    public string? OriginalName { get; set; }

    public MsiColumnDefinition Clone() => (MsiColumnDefinition)MemberwiseClone();
}

/// <summary>A table being defined in the Table Schema dialog. The first <see cref="PrimaryKeys"/> columns form the key.</summary>
public class MsiTableDefinition
{
    public const int MaxTableNameLength = 31;

    private static readonly Regex Identifier = new(@"^[A-Za-z_][A-Za-z0-9_.]*$", RegexOptions.Compiled);

    public string Name { get; set; } = "";
    public int PrimaryKeys { get; set; }
    public List<MsiColumnDefinition> Columns { get; } = new();

    public static bool IsValidIdentifier(string? name) => !string.IsNullOrEmpty(name) && Identifier.IsMatch(name);

    public MsiTableDefinition Clone()
    {
        var copy = new MsiTableDefinition { Name = Name, PrimaryKeys = PrimaryKeys };
        copy.Columns.AddRange(Columns.Select(c => c.Clone()));
        return copy;
    }

    /// <summary>
    /// First problem that stops the table from being created, or null when it is valid.
    /// <paramref name="otherTables"/> are the names that this table's name may not collide with.
    /// </summary>
    public string? Validate(IEnumerable<string> otherTables)
    {
        if (!IsValidIdentifier(Name))
            return "Table name must start with a letter or underscore and contain only letters, digits, '_' and '.'.";
        if (Name.Length > MaxTableNameLength)
            return $"Table name can be at most {MaxTableNameLength} characters.";
        if (otherTables.Contains(Name, StringComparer.Ordinal))
            return $"A table named '{Name}' already exists.";
        if (Columns.Count == 0)
            return "Add at least one column.";
        if (PrimaryKeys < 1 || PrimaryKeys > Columns.Count)
            return "Primary Keys must be between 1 and the number of columns.";
        if (Columns.Count(c => c.Kind == MsiColumnKind.Binary) > 1)
            return "A table can have only one Binary column.";

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < Columns.Count; i++)
        {
            var c = Columns[i];
            if (!IsValidIdentifier(c.Name))
                return $"Column {i + 1} needs a valid name.";
            if (!seen.Add(c.Name))
                return $"Duplicate column name '{c.Name}'.";
            if (c.Kind.HasLength() && (c.Length < 0 || c.Length > 255))
                return $"Length of '{c.Name}' must be between 0 and 255.";
            // A nullable key column is allowed: several standard tables have one (Upgrade.VersionMin, say).
            if (i < PrimaryKeys && c.Kind == MsiColumnKind.Binary)
                return $"Primary key column '{c.Name}' cannot be binary.";
        }

        return null;
    }

    /// <summary>Builds the Windows Installer SQL CREATE TABLE statement. Call <see cref="Validate"/> first.</summary>
    public string ToCreateTableSql()
    {
        var cols = Columns.Select(c =>
        {
            var type = c.Kind switch
            {
                MsiColumnKind.String or MsiColumnKind.LocalizableString => c.Length > 0 ? $"CHAR({c.Length})" : "CHAR",
                MsiColumnKind.Integer => "SHORT",
                MsiColumnKind.LongInteger => "LONG",
                _ => "OBJECT"
            };
            var notNull = c.Nullable ? "" : " NOT NULL";
            var localizable = c.Kind == MsiColumnKind.LocalizableString ? " LOCALIZABLE" : "";
            return $"`{c.Name}` {type}{notNull}{localizable}";
        });

        var keys = string.Join(", ", Columns.Take(PrimaryKeys).Select(c => $"`{c.Name}`"));
        return $"CREATE TABLE `{Name}` ({string.Join(", ", cols)} PRIMARY KEY {keys})";
    }
}
