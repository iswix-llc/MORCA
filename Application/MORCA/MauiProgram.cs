using CommunityToolkit.Maui;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using MORCA.Services;

namespace MORCA
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
#if WINDOWS
            // WebView2 defaults its user data folder to next to the exe, which is read-only under
            // Program Files and leaves the app with a blank screen. Use a per-user folder instead.
            // This must be set before the first WebView2 control is created.
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER")))
            {
                Environment.SetEnvironmentVariable(
                    "WEBVIEW2_USER_DATA_FOLDER",
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MORCA", "WebView2"));
            }
#endif

            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                });

            builder.Services.AddMauiBlazorWebView();
            builder.Services.AddMudServices();
            builder.Services.AddScoped<MsiDatabaseService>();

#if DEBUG
    		builder.Services.AddBlazorWebViewDeveloperTools();
    		builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }
}
