using IsWiX.WindowsInstaller;
using MORCA.Models;

namespace MORCA.Services;

/// <summary>
/// Service for working with MSI files using IsWiX.WindowsInstaller
/// </summary>
public class MsiDatabaseService : IDisposable
{
    private Database? _database;
    private string? _filePath;      // null while the database is untitled
    private string? _tempPath;      // hidden backing file for an untitled database
    private bool _readOnly;

    /// <summary>Path of the saved file, or null while the database is untitled.</summary>
    public string? FilePath => _filePath;
    public bool IsOpen => _database != null;

    /// <summary>True when there are changes that have not been saved.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>Call after any change to the database (row insert/update/delete, table import, ...).</summary>
    public void MarkDirty()
    {
        if (_database == null || IsDirty) return;
        IsDirty = true;
        StateChanged?.Invoke();
    }
    public bool IsUntitled => _database != null && _filePath == null;
    public string DisplayName => _filePath != null ? Path.GetFileName(_filePath) : "Untitled";
    public string SuggestedFileName => IsUntitled ? "Untitled.msi" : Path.GetFileName(_filePath!);

    /// <summary>Raised after a database is opened, created, saved under a new name, or closed.</summary>
    public event Action? StateChanged;

    /// <summary>
    /// Opens an MSI file
    /// </summary>
    public void Open(string path, bool readOnly = true)
    {
        Close();

        var mode = readOnly ? DatabaseOpenMode.ReadOnly : DatabaseOpenMode.Direct;
        _database = new Database(path, mode);
        _readOnly = readOnly;
        _filePath = path;
        IsDirty = false;
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Creates a new, empty "Untitled" database. It has no path until it is saved with
    /// <see cref="AdoptSavedFile"/>; until then it lives in a hidden temp file.
    /// </summary>
    public void CreateUntitled()
    {
        Close();

        _tempPath = Path.Combine(Path.GetTempPath(), $"MORCA-{Guid.NewGuid():N}.msi");
        using (var db = new Database(_tempPath, DatabaseOpenMode.CreateDirect))
        {
            db.Commit();
        }

        _database = new Database(_tempPath, DatabaseOpenMode.Direct);
        _readOnly = false;
        _filePath = null;
        IsDirty = false;
        StateChanged?.Invoke();
    }

    /// <summary>Commits pending changes to the current file.</summary>
    public void Save()
    {
        if (_database == null) throw new InvalidOperationException("No database open");
        if (!_readOnly) _database.Commit();

        if (IsDirty)
        {
            IsDirty = false;
            StateChanged?.Invoke();
        }
    }

    /// <summary>
    /// Returns the current database as bytes for a Save As operation. The database handle is
    /// released while the file is read and then reopened.
    /// </summary>
    public byte[] ReadAllBytes()
    {
        if (_database == null) throw new InvalidOperationException("No database open");

        var path = _filePath ?? _tempPath!;
        if (!_readOnly) _database.Commit();
        _database.Close();
        _database.Dispose();
        try
        {
            return File.ReadAllBytes(path);
        }
        finally
        {
            _database = new Database(path, _readOnly ? DatabaseOpenMode.ReadOnly : DatabaseOpenMode.Direct);
        }
    }

    /// <summary>
    /// Re-points the open database at a file that Save As just wrote, and discards the
    /// temp file if the database was untitled.
    /// </summary>
    public void AdoptSavedFile(string newPath)
    {
        var oldTemp = _tempPath;

        _database?.Close();
        _database?.Dispose();
        _database = new Database(newPath, DatabaseOpenMode.Direct);
        _readOnly = false;
        _filePath = newPath;
        _tempPath = null;
        IsDirty = false;

        DeleteQuietly(oldTemp);
        StateChanged?.Invoke();
    }

    private static void DeleteQuietly(string? path)
    {
        try { if (path != null && File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
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
                tables.Add(record.GetString(1));
            }
        }
        catch
        {
            // If _Tables doesn't exist, try to get table names another way
        }
        
        // _Validation is expected in every MSI but isn't required, and _Tables may not list it,
        // so look for it directly. It is only shown when the database actually has it.
        if (!tables.Contains("_Validation", StringComparer.OrdinalIgnoreCase) && TableExists("_Validation"))
            tables.Add("_Validation");

        // Regular tables first, then any other underscore (system) tables, with _Validation always last.
        return tables
            .OrderBy(t => t.Equals("_Validation", StringComparison.OrdinalIgnoreCase) ? 2 : t.StartsWith('_') ? 1 : 0)
            .ThenBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Reads the database's Summary Information stream, decoded the way Orca presents it.
    /// Properties that are not set come back empty or zero.
    /// </summary>
    public SummaryInformationModel GetSummaryInformation()
    {
        if (_database == null) throw new InvalidOperationException("No database open");

        var info = _database.SummaryInfo;

        static T Read<T>(Func<T> read, T fallback)
        {
            try { return read() ?? fallback; }
            catch { return fallback; }
        }

        var template = Read(() => info.Template, "");
        var separator = template.IndexOf(';');
        var platform = separator < 0 ? template : template[..separator];
        var languages = separator < 0 ? "" : template[(separator + 1)..];

        var wordCount = Read(() => info.WordCount, 0);

        return new SummaryInformationModel(
            Title: Read(() => info.Title, ""),
            Author: Read(() => info.Author, ""),
            Subject: Read(() => info.Subject, ""),
            Comments: Read(() => info.Comments, ""),
            Keywords: Read(() => info.Keywords, ""),
            Platform: platform,
            Languages: languages,
            PackageCode: Read(() => info.RevisionNumber, ""),
            Schema: Read(() => info.PageCount, 0),
            Security: Read(() => info.Security, 0),
            ShortFileNames: (wordCount & 1) != 0,
            Compressed: (wordCount & 2) != 0,
            AdministrativeImage: (wordCount & 4) != 0,
            UacCompliant: (wordCount & 8) != 0);
    }

    /// <summary>True if the table has any column definitions in the database's _Columns table.</summary>
    private bool TableExists(string tableName)
    {
        if (_database == null) return false;

        try
        {
            using var view = _database.OpenView($"SELECT `Name` FROM `_Columns` WHERE `Table` = '{tableName}'");
            view.Execute();
            foreach (var _ in view)
                return true;
        }
        catch
        {
            // treat any failure as "not there"
        }

        return false;
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
        if (_database == null) return;

        _database.Close();
        _database.Dispose();
        _database = null;
        _filePath = null;
        IsDirty = false;

        DeleteQuietly(_tempPath);
        _tempPath = null;

        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        _database?.Close();
        _database?.Dispose();
        DeleteQuietly(_tempPath);
        GC.SuppressFinalize(this);
    }
}
