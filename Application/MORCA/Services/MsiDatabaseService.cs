using IsWiX.WindowsInstaller;
using MORCA.Models;

namespace MORCA.Services;

/// <summary>
/// Service for working with MSI files using IsWiX.WindowsInstaller
/// </summary>
public class MsiDatabaseService : IDisposable
{
    private Database? _database;
    private string? _filePath;      // where the document is saved; null while untitled
    private string? _workingPath;   // temp working copy that every read and edit goes through

    /// <summary>Path of the saved file, or null while the database is untitled.</summary>
    public string? FilePath => _filePath;
    public bool IsOpen => _database != null;
    public bool IsUntitled => _database != null && _filePath == null;
    public string DisplayName => _filePath != null ? FriendlyName(_filePath) : "Untitled";
    public string SuggestedFileName => IsUntitled ? "Untitled.msi" : DisplayName;

    /// <summary>
    /// True when Save can write straight back to <see cref="FilePath"/>. On Android and iOS the
    /// picker hands us a private copy (or a content URI), so Save has to go through Save As.
    /// </summary>
    public bool CanSaveInPlace =>
        _filePath != null && !(OperatingSystem.IsAndroid() || OperatingSystem.IsIOS());

    /// <summary>True when there are changes that have not been saved.</summary>
    public bool IsDirty { get; private set; }

    /// <summary>Raised after a database is opened, created, modified, saved or closed.</summary>
    public event Action? StateChanged;

    /// <summary>Call after any change to the database (summary info, row edits, table import, ...).</summary>
    public void MarkDirty()
    {
        if (_database == null || IsDirty) return;
        IsDirty = true;
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Opens an MSI file. The file itself is never modified until <see cref="Save"/>: all work
    /// happens on a temporary copy.
    /// </summary>
    public void Open(string path)
    {
        Close();

        var working = NewWorkingPath();
        try
        {
            File.Copy(path, working);
            File.SetAttributes(working, FileAttributes.Normal); // the original may be read-only
            _database = new Database(working, DatabaseOpenMode.Direct);
        }
        catch
        {
            DeleteQuietly(working);
            throw;
        }

        _workingPath = working;
        _filePath = path;
        IsDirty = false;
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Creates a new, empty "Untitled" database. It has no path until it is saved with
    /// <see cref="AdoptSavedFile"/>.
    /// </summary>
    public void CreateUntitled()
    {
        Close();

        var working = NewWorkingPath();
        try
        {
            using (var db = new Database(working, DatabaseOpenMode.CreateDirect))
            {
                db.Commit();
            }
            _database = new Database(working, DatabaseOpenMode.Direct);
        }
        catch
        {
            DeleteQuietly(working);
            throw;
        }

        _workingPath = working;
        _filePath = null;
        IsDirty = false;
        StateChanged?.Invoke();
    }

    /// <summary>Writes the working copy back over the saved file.</summary>
    public void Save()
    {
        if (_database == null) throw new InvalidOperationException("No database open");
        if (!CanSaveInPlace) throw new NotSupportedException("This database must be saved with Save As.");

        CopyWorkingTo(_filePath!);
        IsDirty = false;
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Returns the current database as bytes for a Save As operation. The database handle is
    /// released while the working copy is read and then reopened.
    /// </summary>
    public byte[] ReadAllBytes()
    {
        if (_database == null) throw new InvalidOperationException("No database open");

        if (IsDirty) _database.Commit();
        _database.Close();
        _database.Dispose();
        try
        {
            return File.ReadAllBytes(_workingPath!);
        }
        finally
        {
            _database = new Database(_workingPath!, DatabaseOpenMode.Direct);
        }
    }

    /// <summary>
    /// Records that Save As just wrote the database to <paramref name="newPath"/>. The working
    /// copy stays open; only the document's name and clean state change.
    /// </summary>
    public void AdoptSavedFile(string newPath)
    {
        if (_database == null) throw new InvalidOperationException("No database open");

        _filePath = newPath;
        IsDirty = false;
        StateChanged?.Invoke();
    }

    private void CopyWorkingTo(string destination)
    {
        if (IsDirty) _database!.Commit();
        _database!.Close();
        _database.Dispose();
        try
        {
            File.Copy(_workingPath!, destination, overwrite: true);
        }
        finally
        {
            _database = new Database(_workingPath!, DatabaseOpenMode.Direct);
        }
    }

    private static string NewWorkingPath() =>
        Path.Combine(Path.GetTempPath(), $"MORCA-{Guid.NewGuid():N}.msi");

    private static void DeleteQuietly(string? path)
    {
        try { if (path != null && File.Exists(path)) File.Delete(path); } catch { /* best effort */ }
    }

    // Android's Save As returns a content:// URI rather than a path; show just its last segment.
    private static string FriendlyName(string path)
    {
        if (!path.Contains("://")) return Path.GetFileName(path);

        var decoded = Uri.UnescapeDataString(path);
        var i = decoded.LastIndexOfAny(new[] { '/', ':' });
        return i >= 0 ? decoded[(i + 1)..] : decoded;
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

    /// <summary>
    /// Writes the Summary Information stream from the decoded model and marks the database dirty.
    /// </summary>
    public void SetSummaryInformation(SummaryInformationModel model)
    {
        if (_database == null) throw new InvalidOperationException("No database open");

        // InstallerException usually has no message, so say which step failed.
        static void Step(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                var code = ex.GetType().GetProperty("ErrorCode")?.GetValue(ex);
                var detail = string.IsNullOrEmpty(ex.Message) ? "" : $": {ex.Message}";
                throw new InvalidOperationException(
                    $"{what} failed ({ex.GetType().Name}{(code != null ? $", error {code}" : "")}){detail}", ex);
            }
        }

        var template = string.IsNullOrEmpty(model.Languages) ? model.Platform : $"{model.Platform};{model.Languages}";
        var wordCount =
            (model.ShortFileNames ? 1 : 0) |
            (model.Compressed ? 2 : 0) |
            (model.AdministrativeImage ? 4 : 0) |
            (model.UacCompliant ? 8 : 0);

        // The summary information handle that Database.SummaryInfo returns is read-only, so
        // release the database and open a writable one on the working file instead.
        Step("Commit database", () => _database.Commit());
        Step("Close database", () =>
        {
            _database.Close();
            _database.Dispose();
        });

        SummaryInfo? info = null;
        try
        {
            Step("Open summary information for writing", () => info = new SummaryInfo(_workingPath!, true));

            Step("Title", () => info!.Title = model.Title);
            Step("Author", () => info!.Author = model.Author);
            Step("Subject", () => info!.Subject = model.Subject);
            Step("Comments", () => info!.Comments = model.Comments);
            Step("Keywords", () => info!.Keywords = model.Keywords);
            Step("Template", () => info!.Template = template);
            Step("Package Code", () => info!.RevisionNumber = model.PackageCode);
            Step("Schema", () => info!.PageCount = model.Schema);
            Step("Security", () => info!.Security = model.Security);
            Step("Source image flags", () => info!.WordCount = wordCount);
            Step("Persist", () => info!.Persist());
            Step("Close summary information", () => info!.Dispose());
        }
        finally
        {
            try { info?.Dispose(); } catch { /* already reported above if it mattered */ }
            Step("Reopen database", () => _database = new Database(_workingPath!, DatabaseOpenMode.Direct));
        }

        MarkDirty();
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

        DeleteQuietly(_workingPath);
        _workingPath = null;

        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        _database?.Close();
        _database?.Dispose();
        DeleteQuietly(_workingPath);
        GC.SuppressFinalize(this);
    }
}
