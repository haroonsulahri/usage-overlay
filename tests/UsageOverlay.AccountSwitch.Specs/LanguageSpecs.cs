using System.Text.Json;
using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using CodexUsage.Core.Settings;
using UsageOverlay;
using Localization = UsageOverlay.Infrastructure.Localization;

internal static class LanguageSpecs
{
    internal static void Run()
    {
        Check(new OverlaySettings().Language == AppLanguage.English, "Existing settings default to English");
        Check(OverlaySettings.FromJson("{}").Language == AppLanguage.English, "Legacy settings default to English");
        Check((new OverlaySettings { Language = (AppLanguage)42 }).Normalize().Language == AppLanguage.English, "Invalid language falls back to English");
        var saved = JsonSerializer.Serialize(new OverlaySettings { Language = AppLanguage.Russian });
        Check(OverlaySettings.FromJson(saved).Language == AppLanguage.Russian, "Language survives settings round-trip");
        var resources = new ResourceManager("UsageOverlay.Resources.Messages", typeof(App).Assembly);
        var english = resources.GetResourceSet(CultureInfo.InvariantCulture, true, true)!;
        var russianResources = resources.GetResourceSet(CultureInfo.GetCultureInfo("ru"), true, false)!;
        foreach (DictionaryEntry entry in english)
        {
            var key = (string)entry.Key;
            var translated = russianResources.GetString(key);
            if (string.IsNullOrEmpty(translated)) throw new InvalidOperationException("Missing Russian resource: " + key);
            static string[] Placeholders(string value) => Regex.Matches(value, @"\{\d+\}")
                .Select(match => match.Value).OrderBy(value => value, StringComparer.Ordinal).ToArray();
            if (!Placeholders((string)entry.Value!).SequenceEqual(Placeholders(translated)))
                throw new InvalidOperationException("Mismatched format placeholders: " + key);
        }
        Console.WriteLine("PASS Russian resources and format placeholders match English");

        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App();
                app.InitializeComponent();
                Localization.Language = AppLanguage.English;
                SettingsSaveRequest? request = null;
                var window = new SettingsWindow(new OverlaySettings(), false, false,
                    new ProviderSettingsStatus("", "", "", false, "settings.json"),
                    value => { request = value; return Task.FromResult(""); }, () => { });
                window.Show();
                ((RadioButton)window.FindName("RussianLanguageRadio")).IsChecked = true;
                ((Button)window.FindName("SaveButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(request?.Settings.Language == AppLanguage.Russian, "Save sends the selected language");
                Check(((TextBlock)window.FindName("SavedStatusText")).Text.Contains("Restart", StringComparison.Ordinal), "Save explains the required restart");
                window.Close();

                Localization.Language = AppLanguage.Russian;
                var russian = new SettingsWindow(new OverlaySettings { Language = AppLanguage.Russian }, false, false,
                    new ProviderSettingsStatus("", "", "", false, "settings.json"), _ => Task.FromResult(""), () => { });
                Check(russian.Title == "Настройки Usage Overlay", "Russian settings title is translated");
                Check(((RadioButton)russian.FindName("RussianLanguageRadio")).IsChecked == true, "Russian choice is restored");
                var now = DateTimeOffset.UtcNow;
                Check(Localization.Reset(now.AddDays(2).AddHours(3), now) == "Сброс через 2 д 3 ч", "Russian countdown uses translated units");
                Check(Localization.Status("Updated 5m ago") == "5 мин назад", "Relative status is translated");
                Check(Localization.Text("Unrecognised service message") == "Unrecognised service message", "Unknown service messages remain readable");
                russian.Close();
                app.Shutdown();
            }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException("Language UI test failed.", failure);
        Localization.Language = AppLanguage.English;
        Console.WriteLine("All language checks passed.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
        Console.WriteLine("PASS " + message);
    }
}
