namespace MORCA.Models;

public record MsiDatabaseModel(
    string FilePath,
    List<MsiTableModel> Tables
);

public record MsiTableModel(
    string Name,
    List<MsiColumnModel> Columns,
    List<Dictionary<string, object?>> Rows
);

public record MsiColumnModel(
    string Name,
    string Type
);
