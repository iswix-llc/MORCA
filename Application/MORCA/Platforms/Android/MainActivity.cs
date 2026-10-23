using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using MORCA.Services;

namespace MORCA
{
    // "Open with MORCA" for .msi and .msm files. Android has no reliable type for them: file managers report
    // application/x-msi, application/x-msdownload or just application/octet-stream, so besides the first two types the
    // second filter matches on the file name. The path patterns are repeated because a pattern cannot say "ends in .msi"
    // when the name itself has dots.
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, Exported = true,
        ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    [IntentFilter(new[] { Intent.ActionView, Intent.ActionEdit },
        Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
        DataSchemes = new[] { "content", "file" },
        DataMimeTypes = new[] { "application/x-msi", "application/x-msdownload", "application/x-ole-storage" })]
    [IntentFilter(new[] { Intent.ActionView, Intent.ActionEdit },
        Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
        DataSchemes = new[] { "content", "file" }, DataHost = "*", DataMimeType = "*/*", DataPathPattern = ".*\\.msi")]
    [IntentFilter(new[] { Intent.ActionView, Intent.ActionEdit },
        Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
        DataSchemes = new[] { "content", "file" }, DataHost = "*", DataMimeType = "*/*", DataPathPattern = ".*\\..*\\.msi")]
    [IntentFilter(new[] { Intent.ActionView, Intent.ActionEdit },
        Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
        DataSchemes = new[] { "content", "file" }, DataHost = "*", DataMimeType = "*/*", DataPathPattern = ".*\\..*\\..*\\.msi")]
    [IntentFilter(new[] { Intent.ActionView, Intent.ActionEdit },
        Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
        DataSchemes = new[] { "content", "file" }, DataHost = "*", DataMimeType = "*/*", DataPathPattern = ".*\\.msm")]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            // Before the app starts, so the file is already on its way when the first page asks for it.
            if (Intent is { Data: { } uri } intent && (intent.Action == Intent.ActionView || intent.Action == Intent.ActionEdit))
            {
                var cache = CacheDir?.AbsolutePath;
                var resolver = ContentResolver;
                if (cache != null && resolver != null)
                    StartupFile.Expect(Task.Run(() => CopyToCache(resolver, uri, cache)));
            }

            base.OnCreate(savedInstanceState);
        }

        /// <summary>
        /// Copies the file Android handed us (a content:// address that only this call can read) into the app's cache,
        /// under its own name, and returns the path. MORCA saves such files with Save As, never over the original.
        /// </summary>
        private static string? CopyToCache(ContentResolver resolver, Android.Net.Uri uri, string cacheDirectory)
        {
            try
            {
                var root = Path.Combine(cacheDirectory, "opened");
                DeleteOldCopies(root);

                var name = DisplayNameOf(resolver, uri) ?? uri.LastPathSegment ?? "opened.msi";
                name = string.Concat(Path.GetFileName(name).Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
                if (name.Length == 0) name = "opened.msi";

                var folder = Path.Combine(root, Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(folder);
                var path = Path.Combine(folder, name);

                using var input = resolver.OpenInputStream(uri);
                if (input == null) return null;
                using var output = File.Create(path);
                input.CopyTo(output);
                return path;
            }
            catch
            {
                return null;
            }
        }

        private static string? DisplayNameOf(ContentResolver resolver, Android.Net.Uri uri)
        {
            try
            {
                using var cursor = resolver.Query(uri, new[] { OpenableColumns.DisplayName }, null, null, null);
                if (cursor != null && cursor.MoveToFirst())
                {
                    var index = cursor.GetColumnIndex(OpenableColumns.DisplayName);
                    if (index >= 0) return cursor.GetString(index);
                }
            }
            catch
            {
                // fall back to the last part of the address
            }
            return null;
        }

        /// <summary>Removes copies made by earlier runs, which are only needed until the file has been opened.</summary>
        private static void DeleteOldCopies(string root)
        {
            try
            {
                if (!Directory.Exists(root)) return;
                foreach (var folder in Directory.EnumerateDirectories(root))
                {
                    if (Directory.GetCreationTimeUtc(folder) < DateTime.UtcNow.AddDays(-1))
                        Directory.Delete(folder, recursive: true);
                }
            }
            catch
            {
                // best effort
            }
        }
    }
}
