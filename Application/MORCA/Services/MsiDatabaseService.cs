using IsWiX.WindowsInstaller;
using OpenDatabaseMode = IsWiX.WindowsInstaller.OpenDatabase;

namespace MORCA.Services;

/// <summary>
/// Service for working with MSI files using IsWiX.WindowsInstaller
/// </summary>
public class MsiDatabaseService : IDisposable
{
    private Database? _database;
    private string? _filePath;

    public string? FilePath => _filePath;
    public bool IsOpen => _database != null;

    /// <summary>
    /// Opens an MSI file
    /// </summary>
    public void Open(string path, bool readOnly = true)
    {
        _database?.Close();
        _database?.Dispose();
        
        var mode = readOnly ? OpenDatabaseMode.ReadOnly : OpenDatabaseMode.Direct;
        _database = new Database(path, mode);
        _filePath = path;
    }

    /// <summary>
    /// Gets all table names from the database
    /// </summary>
    public List<string> GetTableNames()
    {
        if (_database == null) throw new InvalidOperationException("No database open");
        
        var tables = new List<string>();
        
        try
        {
            using var view = _database.OpenView("SELECT `Name` FROM `_Tables`");
            view.Execute();
            
            foreach (var record in view)
            {
                var name = record.GetString(1);
                if (!name.StartsWith("_")) // Exclude system tables by default
                    tables.Add(name);
            }
        }
        catch
        {
            // If _Tables doesn't exist, try to get table names another way
        }
        
        return tables.OrderBy(t => t).ToList();
    }

    /// <summary>
    /// Gets column information for a table
    /// </summary>
    public List<(string Name, string Type)> GetTableColumns(string tableName)
    {
        if (_database == null) throw new InvalidOperationException("No database open");

        var columns = new List<(string Name, string Type)>();
        
        try
        {
            using var view = _database.OpenView($"SELECT `Name`, `Type` FROM `_Columns` WHERE `Table` = '{tableName}'");
            view.Execute();

            foreach (var record in view)
            {
                var name = record.GetString(1);
                var typeStr = record.GetString(2);
                columns.Add((name, typeStr));
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to get columns for table '{tableName}'", ex);
        }

        return columns;
    }

    /// <summary>
    /// Gets rows from a specific table
    /// </summary>
    public List<Dictionary<string, object?>> GetTableRows(string tableName)
    {
        if (_database == null) throw new InvalidOperationException("No database open");

        var rows = new List<Dictionary<string, object?>>();
        var columns = GetTableColumns(tableName);

        if (columns.Count == 0)
            return rows;

        try
        {
            using var view = _database.OpenView($"SELECT * FROM `{tableName}`");
            view.Execute();

            foreach (var record in view)
            {
                var row = new Dictionary<string, object?>();
                
                for (int i = 0; i < columns.Count; i++)
                {
                    var colName = columns[i].Name;
                    var colType = columns[i].Type;
                    
                    // WiX column types: i (integer), s (string), v (binary)
                    try
                    {
                        object? value = colType[0] switch
                        {
                            'i' => record.GetInteger(i + 1),
                            's' or 'S' or 'l' or 'L' => record.GetString(i + 1),
                            'v' or 'V' or 'o' or 'O' => "[Binary/Stream]",
                            _ => record.GetString(i + 1)
                        };
                        row[colName] = value;
                    }
                    catch
                    {
                        row[colName] = null;
                    }
                }
                
                rows.Add(row);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to get rows from table '{tableName}'", ex);
        }

        return rows;
    }

    /// <summary>
    /// Closes the current database
    /// </summary>
    public void Close()
    {
        _database?.Close();
        _filePath = null;
    }

    public void Dispose()
    {
        _database?.Close();
        _database?.Dispose();
        GC.SuppressFinalize(this);
    }
}
