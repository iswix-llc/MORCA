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

        // Database.SummaryInfo is writable on a Direct database (IsWiX 0.1.6+); Commit() persists it.
        var info = _database.SummaryInfo;
        Step("Title", () => info.Title = model.Title);
        Step("Author", () => info.Author = model.Author);
        Step("Subject", () => info.Subject = model.Subject);
        Step("Comments", () => info.Comments = model.Comments);
        Step("Keywords", () => info.Keywords = model.Keywords);
        Step("Template", () => info.Template = template);
        Step("Package Code", () => info.RevisionNumber = model.PackageCode);
        Step("Schema", () => info.PageCount = model.Schema);
        Step("Security", () => info.Security = model.Security);
        Step("Source image flags", () => info.WordCount = wordCount);
        Step("Commit", () => _database.Commit());

        MarkDirty();
    }

    /// <summary>
    /// Creates a new, empty custom table from a definition (Orca's Table Schema dialog) and marks
    /// the database dirty. Throws <see cref="InvalidOperationException"/> if the definition is invalid.
    /// </summary>
    public void CreateTable(MsiTableDefinition definition)
    {
        if (_database == null) throw new InvalidOperationException("No database open");

        var problem = definition.Validate(GetTableNames());
        if (problem != null) throw new InvalidOperationException(problem);

        try
        {
            using (var view = _database.OpenView(definition.ToCreateTableSql()))
                view.Execute();
            _database.Commit();
        }
        catch (Exception ex)
        {
            var code = ex.GetType().GetProperty("ErrorCode")?.GetValue(ex);
            var detail = string.IsNullOrEmpty(ex.Message) ? "" : $": {ex.Message}";
            throw new InvalidOperationException(
                $"Creating table '{definition.Name}' failed ({ex.GetType().Name}{(code != null ? $", error {code}" : "")}){detail}", ex);
        }

        MarkDirty();
    }

    /// <summary>
    /// Decodes a _Columns.Type value. It is Windows Installer's type word: the low byte is the size,
    /// 0x100 valid, 0x200 localizable, 0x400 string, 0x800 not an integer (a string with 0x400, else
    /// binary), 0x1000 nullable, 0x2000 primary key.
    /// </summary>
    private static (MsiColumnKind Kind, int Length, bool Nullable, bool IsKey) ParseColumnType(string typeWord)
    {
        if (!int.TryParse(typeWord, out var type))
            throw new InvalidOperationException($"Unrecognised column type '{typeWord}'.");

        var size = type & 0xFF;
        var kind = (type & 0x0800) == 0
            ? (size == 4 ? MsiColumnKind.LongInteger : MsiColumnKind.Integer)
            : (type & 0x0400) == 0
                ? MsiColumnKind.Binary
                : (type & 0x0200) != 0 ? MsiColumnKind.LocalizableString : MsiColumnKind.String;

        return (kind, kind.HasLength() ? size : 0, (type & 0x1000) != 0, (type & 0x2000) != 0);
    }

    /// <summary>
    /// Reads an existing table's schema into a definition for the Table Schema dialog. Each column
    /// remembers its current name so <see cref="AdjustTable"/> can carry its data across.
    /// </summary>
    public MsiTableDefinition GetTableDefinition(string tableName)
    {
        if (_database == null) throw new InvalidOperationException("No database open");

        var columns = GetTableColumns(tableName);
        if (columns.Count == 0)
            throw new InvalidOperationException($"Table '{tableName}' was not found.");

        var definition = new MsiTableDefinition { Name = tableName };
        var keys = 0;
        foreach (var (name, typeCode) in columns)
        {
            var (kind, length, nullable, isKey) = ParseColumnType(typeCode);
            if (isKey) keys++;
            definition.Columns.Add(new MsiColumnDefinition
            {
                Name = name,
                OriginalName = name,
                Kind = kind,
                Length = length,
                Nullable = nullable
            });
        }
        definition.PrimaryKeys = Math.Clamp(keys, 1, definition.Columns.Count);

        return definition;
    }

    /// <summary>
    /// Replaces an existing table's schema (Orca's "Adjust Schema"). The table is rebuilt and its rows
    /// are copied across for every column that keeps its <see cref="MsiColumnDefinition.OriginalName"/>.
    /// If the existing data cannot fit the new schema nothing is changed and an
    /// <see cref="InvalidOperationException"/> explains why.
    /// </summary>
    public void AdjustTable(MsiTableDefinition definition)
    {
        if (_database == null) throw new InvalidOperationException("No database open");

        var current = GetTableDefinition(definition.Name);
        var problem = definition.Validate(GetTableNames().Where(t => !t.Equals(definition.Name, StringComparison.Ordinal)));
        if (problem != null) throw new InvalidOperationException(problem);

        var oldRows = ReadRawRows(current);
        var newRows = ConvertRows(current, definition, oldRows); // throws before anything is modified

        try
        {
            RunDdl($"DROP TABLE `{definition.Name}`");
            RunDdl(definition.ToCreateTableSql());
            InsertRawRows(definition, newRows);
            _database.Commit();
        }
        catch (Exception ex)
        {
            // Put the table back the way it was, as far as possible.
            try
            {
                try { RunDdl($"DROP TABLE `{definition.Name}`"); } catch { /* it may not exist */ }
                RunDdl(current.ToCreateTableSql());
                InsertRawRows(current, oldRows);
                _database.Commit();
            }
            catch
            {
                // best effort; report the original failure
            }
            throw Describe($"Adjusting table '{definition.Name}'", ex);
        }

        MarkDirty();
    }

    /// <summary>Drops a table and all of its rows.</summary>
    public void DropTable(string tableName)
    {
        if (_database == null) throw new InvalidOperationException("No database open");
        if (!GetTableNames().Contains(tableName, StringComparer.Ordinal))
            throw new InvalidOperationException($"Table '{tableName}' was not found.");

        try
        {
            RunDdl($"DROP TABLE `{tableName}`");
            _database.Commit();
        }
        catch (Exception ex)
        {
            throw Describe($"Dropping table '{tableName}'", ex);
        }

        MarkDirty();
    }

    /// <summary>Adds a row. <paramref name="values"/> maps column names to int, string or null; Binary columns are left empty.</summary>
    public void InsertRow(string tableName, IReadOnlyDictionary<string, object?> values)
    {
        if (_database == null) throw new InvalidOperationException("No database open");

        var table = GetTableDefinition(tableName);
        if (table.Columns.Any(c => c.Kind == MsiColumnKind.Binary && !c.Nullable))
            throw new InvalidOperationException("This table has a required Binary column, which cannot be filled in yet.");

        try
        {
            using var view = _database.OpenView($"SELECT * FROM `{tableName}`");
            view.Execute();
            using var record = BuildRecord(table.Columns, values);
            view.Insert(record);
            _database.Commit();
        }
        catch (Exception ex)
        {
            throw Describe($"Adding the row to '{tableName}'", ex, "A row with the same primary key may already exist.");
        }

        MarkDirty();
    }

    /// <summary>
    /// Changes the non-key values of the row whose primary key is given in <paramref name="values"/>.
    /// Binary columns and primary key columns are left as they are.
    /// </summary>
    public void UpdateRow(string tableName, IReadOnlyDictionary<string, object?> values)
    {
        if (_database == null) throw new InvalidOperationException("No database open");

        var table = GetTableDefinition(tableName);
        // The view names the key plus every non-Binary column, so binary data is never overwritten.
        var columns = table.Columns.Where(c => c.Kind != MsiColumnKind.Binary).ToList();
        if (columns.Count < table.PrimaryKeys)
            throw new InvalidOperationException("The primary key columns must come first.");

        try
        {
            using var view = _database.OpenView($"SELECT {ColumnList(columns)} FROM `{tableName}`");
            view.Execute();
            using var record = BuildRecord(columns, values);
            view.Update(record);
            _database.Commit();
        }
        catch (Exception ex)
        {
            throw Describe($"Updating the row in '{tableName}'", ex, "The row may no longer exist.");
        }

        MarkDirty();
    }

    /// <summary>Deletes the row whose primary key columns are given in <paramref name="keyValues"/>.</summary>
    public void DeleteRow(string tableName, IReadOnlyDictionary<string, object?> keyValues)
    {
        if (_database == null) throw new InvalidOperationException("No database open");

        var table = GetTableDefinition(tableName);
        var keys = table.Columns.Take(table.PrimaryKeys).ToList();

        try
        {
            using var view = _database.OpenView($"SELECT {ColumnList(keys)} FROM `{tableName}`");
            view.Execute();
            using var record = BuildRecord(keys, keyValues);
            view.Delete(record);
            _database.Commit();
        }
        catch (Exception ex)
        {
            throw Describe($"Deleting the row from '{tableName}'", ex, "The row may no longer exist.");
        }

        MarkDirty();
    }

    private static string ColumnList(IEnumerable<MsiColumnDefinition> columns) =>
        string.Join(", ", columns.Select(c => $"`{c.Name}`"));

    /// <summary>A record whose fields follow <paramref name="columns"/>; missing or null values stay null.</summary>
    private static Record BuildRecord(IReadOnlyList<MsiColumnDefinition> columns, IReadOnlyDictionary<string, object?> values)
    {
        var record = new Record(columns.Count);
        for (var i = 0; i < columns.Count; i++)
        {
            if (!values.TryGetValue(columns[i].Name, out var value)) continue;

            switch (value)
            {
                case int n: record.SetInteger(i + 1, n); break;
                case string text when text.Length > 0: record.SetString(i + 1, text); break;
            }
        }
        return record;
    }

    /// <summary>
    /// Exports the named tables to <paramref name="folder"/> as Windows Installer text (.idt) files, plus a
    /// file for each binary cell. The files sit side by side, which is where Import expects binary files.
    /// </summary>
    public void ExportTables(IEnumerable<string> tableNames, string folder)
    {
        if (_database == null) throw new InvalidOperationException("No database open");
        if (folder.Contains("://"))
            throw new NotSupportedException("This platform returned a folder that cannot be written to directly.");

        Directory.CreateDirectory(folder);
        foreach (var table in tableNames)
        {
            try
            {
                _database.Export(table, Path.Combine(folder, table + ".idt"));
            }
            catch (Exception ex)
            {
                throw Describe($"Exporting table '{table}'", ex);
            }
        }
    }

    /// <summary>The table an .idt file describes (the first value on its third line), or null if it is not an IDT file.</summary>
    public static string? ReadIdtTableName(string idtPath)
    {
        try
        {
            using var reader = new StreamReader(idtPath, detectEncodingFromByteOrderMarks: true);
            reader.ReadLine();
            reader.ReadLine();
            var parts = reader.ReadLine()?.Split('\t');
            if (parts == null || parts[0].Length == 0) return null;
            return parts.Length == 2 && parts[1] == "_ForceCodepage" ? "_ForceCodepage" : parts[0];
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Imports .idt files. A table that already exists is replaced. Tables whose names start with an underscore
    /// cannot be imported and are skipped. A file that fails does not stop the others.
    /// </summary>
    public TableImportResult ImportIdtFiles(IEnumerable<string> idtPaths)
    {
        if (_database == null) throw new InvalidOperationException("No database open");

        var imported = new List<string>();
        var skipped = new List<string>();
        var failed = new List<(string File, string Error)>();

        foreach (var path in idtPaths)
        {
            var file = Path.GetFileName(path);
            var table = ReadIdtTableName(path);
            if (table == null)
            {
                failed.Add((file, "Not a valid IDT file."));
            }
            else if (table.StartsWith('_'))
            {
                skipped.Add(table);
            }
            else
            {
                try
                {
                    _database.Import(path);
                    imported.Add(table);
                }
                catch (Exception ex)
                {
                    failed.Add((file, Describe($"Importing '{file}'", ex).Message));
                }
            }
        }

        if (imported.Count > 0)
        {
            try { _database.Commit(); }
            catch (Exception ex) { throw Describe("Saving the imported tables", ex); }
            MarkDirty();
        }

        return new TableImportResult(imported, skipped, failed);
    }

    private void RunDdl(string sql)
    {
        using var view = _database!.OpenView(sql);
        view.Execute();
    }

    // InstallerException usually has no message, so say what failed and with which error code.
    private static InvalidOperationException Describe(string what, Exception ex, string? hint = null)
    {
        var code = ex.GetType().GetProperty("ErrorCode")?.GetValue(ex);
        var detail = string.IsNullOrEmpty(ex.Message) ? "" : $": {ex.Message}";
        return new InvalidOperationException(
            $"{what} failed ({ex.GetType().Name}{(code != null ? $", error {code}" : "")}){detail}{(hint != null ? " " + hint : "")}", ex);
    }

    /// <summary>Every row of a table as null, int, string or byte[] values in column order.</summary>
    private List<object?[]> ReadRawRows(MsiTableDefinition table)
    {
        var rows = new List<object?[]>();

        using var view = _database!.OpenView($"SELECT * FROM `{table.Name}`");
        view.Execute();

        foreach (var record in view)
        {
            var row = new object?[table.Columns.Count];
            for (var i = 0; i < row.Length; i++)
            {
                var field = i + 1;
                if (record.IsNull(field)) continue;

                var kind = table.Columns[i].Kind;
                if (kind.IsNumber())
                {
                    row[i] = record.GetInteger(field);
                }
                else if (kind.IsText())
                {
                    row[i] = record.GetString(field);
                }
                else
                {
                    using var stream = record.GetStream(field);
                    using var buffer = new MemoryStream();
                    stream?.CopyTo(buffer);
                    row[i] = buffer.ToArray();
                }
            }
            rows.Add(row);
        }

        return rows;
    }

    private void InsertRawRows(MsiTableDefinition table, List<object?[]> rows)
    {
        using var view = _database!.OpenView($"SELECT * FROM `{table.Name}`");
        view.Execute();

        foreach (var row in rows)
        {
            using var record = new Record(table.Columns.Count);
            for (var i = 0; i < row.Length; i++)
            {
                switch (row[i])
                {
                    case int n: record.SetInteger(i + 1, n); break;
                    case string text: record.SetString(i + 1, text); break;
                    case byte[] bytes: record.SetStream(i + 1, new MemoryStream(bytes)); break;
                }
            }
            view.Insert(record);
        }
    }

    /// <summary>
    /// Maps rows of the existing table onto the new schema, converting values where the type changed.
    /// Throws with every problem found when the data does not fit, before the database is touched.
    /// </summary>
    private static List<object?[]> ConvertRows(MsiTableDefinition current, MsiTableDefinition target, List<object?[]> oldRows)
    {
        var sources = target.Columns
            .Select(c => c.OriginalName == null ? -1 : current.Columns.FindIndex(o => o.Name == c.OriginalName))
            .ToArray();

        var problems = new List<string>();
        var counts = new Dictionary<string, int>();
        void Problem(string text) => counts[text] = counts.GetValueOrDefault(text) + 1;

        var result = new List<object?[]>(oldRows.Count);
        foreach (var oldRow in oldRows)
        {
            var row = new object?[target.Columns.Count];
            for (var j = 0; j < row.Length; j++)
            {
                var column = target.Columns[j];
                var value = sources[j] < 0 ? null : oldRow[sources[j]];

                switch (value)
                {
                    case int n when column.Kind.IsNumber():
                        if (column.Kind == MsiColumnKind.Integer && (n < short.MinValue + 1 || n > short.MaxValue))
                        { Problem($"'{column.Name}' has numbers too large for an Integer column; use Long Integer."); value = null; }
                        break;
                    case int n when column.Kind.IsText():
                        value = n.ToString(System.Globalization.CultureInfo.InvariantCulture);
                        break;
                    case string s when column.Kind.IsNumber():
                        if (int.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
                            && (column.Kind == MsiColumnKind.LongInteger || (parsed >= short.MinValue + 1 && parsed <= short.MaxValue)))
                            value = parsed;
                        else
                        { Problem($"'{column.Name}' has text that is not a valid {column.Kind.Display()}."); value = null; }
                        break;
                    case byte[] when column.Kind != MsiColumnKind.Binary:
                    case int or string when column.Kind == MsiColumnKind.Binary:
                        value = null; // data cannot move to or from a Binary column
                        break;
                }

                if (value is string text && column.Kind.HasLength() && column.Length > 0 && text.Length > column.Length)
                    Problem($"'{column.Name}' has values longer than {column.Length} characters.");

                if (value == null && !column.Nullable)
                    Problem($"'{column.Name}' is not nullable but has rows with no value for it; make it Nullable.");

                row[j] = value;
            }
            result.Add(row);
        }

        // Primary key values must stay unique.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        if (result.Any(r => !seen.Add(string.Join('\u0001', r.Take(target.PrimaryKeys).Select(v => v?.ToString())))))
            Problem("The new primary key would not be unique for the existing rows.");

        if (counts.Count > 0)
        {
            problems.AddRange(counts.Select(kv => kv.Value > 1 ? $"{kv.Key} ({kv.Value} rows)" : kv.Key));
            throw new InvalidOperationException(
                "The existing rows do not fit the new schema, so nothing was changed:\n" + string.Join("\n", problems.Take(5)));
        }

        return result;
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
                        var kind = ParseColumnType(colType).Kind;
                        object? value = record.IsNull(i + 1) ? null
                            : kind.IsNumber() ? record.GetInteger(i + 1)
                            : kind == MsiColumnKind.Binary ? "[Binary/Stream]"
                            : record.GetString(i + 1);
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

/// <summary>The outcome of importing .idt files.</summary>
public record TableImportResult(
    IReadOnlyList<string> Imported,
    IReadOnlyList<string> Skipped,
    IReadOnlyList<(string File, string Error)> Failed);
