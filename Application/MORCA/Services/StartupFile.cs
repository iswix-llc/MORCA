namespace MORCA.Services;

/// <summary>
/// The MSI file MORCA was asked to open when it was started, for example by "Edit with MORCA" in Windows Explorer,
/// which runs <c>MORCA.exe "C:\path\file.msi"</c>.
/// </summary>
public static class StartupFile
{
    private static readonly string[] Extensions = { ".msi", ".msm" };
    private static string? _path = Find();
    private static readonly object Lock = new();

    private static Task<string?>? _pending;

    /// <summary>Platform code that is still fetching the file (Android copies it out of the app that sent it) hands the work in here.</summary>
    public static void Expect(Task<string?> path)
    {
        lock (Lock) _pending = path;
    }

    /// <summary>The file to open, once. Later calls (a reloaded page, say) return null so it is not opened again.</summary>
    public static async Task<string?> TakeAsync()
    {
        Task<string?>? pending;
        string? path;
        lock (Lock)
        {
            path = _path;
            _path = null;
            pending = _pending;
            _pending = null;
        }

        if (path != null || pending == null) return path;
        try { return await pending; }
        catch { return null; }
    }

    private static string? Find()
    {
#if WINDOWS
        try
        {
            foreach (var arg in Environment.GetCommandLineArgs().Skip(1))
            {
                if (Extensions.Contains(Path.GetExtension(arg), StringComparer.OrdinalIgnoreCase) && File.Exists(arg))
                    return Path.GetFullPath(arg);
            }
        }
        catch
        {
            // an unreadable command line just means there is nothing to open
        }
#endif
        return null;
    }
}
