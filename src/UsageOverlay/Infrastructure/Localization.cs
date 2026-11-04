using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using CodexUsage.Core.Formatting;
using CodexUsage.Core.Settings;

namespace UsageOverlay.Infrastructure;

internal static class Localization
{
    private static readonly ResourceManager Resources = new("UsageOverlay.Resources.Messages", typeof(Localization).Assembly);
    private static readonly HashSet<string> KnownKeys = Resources.GetResourceSet(CultureInfo.InvariantCulture, true, true)!
        .Cast<DictionaryEntry>().Select(entry => (string)entry.Key).ToHashSet(StringComparer.Ordinal);
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru");

    internal static AppLanguage Language { get; set; }

    internal static string Text(string text) => KnownKeys.Contains(text)
        ? Resources.GetString(text, Language == AppLanguage.Russian ? Russian : CultureInfo.InvariantCulture) ?? text : text;

    internal static string Format(string text, params object[] values) => string.Format(CultureInfo.CurrentCulture, Text(text), values);

    internal static string Status(string text)
    {
        if (Language != AppLanguage.Russian || text.Length > 128) return Text(text);
        var match = Regex.Match(text, @"^Updated (\d+)([mhd]) ago$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (!match.Success) return Text(text);
        var key = match.Groups[2].Value switch { "m" => "Updated {0}m ago", "h" => "Updated {0}h ago", _ => "Updated {0}d ago" };
        return Format(key, match.Groups[1].Value);
    }

    internal static string Limit(string label) => label.EndsWith(" · secondary limit", StringComparison.Ordinal)
        ? Format("{0} · secondary limit", label[..^" · secondary limit".Length]) : Text(label);

    internal static string Reset(DateTimeOffset? resetsAt, DateTimeOffset now)
    {
        if (Language != AppLanguage.Russian) return ResetTimeFormatter.Format(resetsAt, now);
        if (resetsAt is null) return Text("Reset time isn’t available");
        var remaining = resetsAt.Value - now;
        if (remaining <= TimeSpan.Zero) return Text("Resets now");
        if (remaining.TotalDays >= 1) return Format("Resets in {0}d {1}h", (int)remaining.TotalDays, remaining.Hours);
        if (remaining.TotalHours >= 1) return Format("Resets in {0}h {1}m", (int)remaining.TotalHours, remaining.Minutes);
        return Format("Resets in {0}m", Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes)));
    }

    internal static void Apply(DependencyObject root)
    {
        if (Language != AppLanguage.Russian) return;
        var visited = new HashSet<DependencyObject>();
        Visit(root);
        void Visit(DependencyObject item)
        {
            if (!visited.Add(item)) return;
            if (item is Window window) window.Title = Text(window.Title);
            if (item is Run run && !System.Windows.Data.BindingOperations.IsDataBound(run, Run.TextProperty))
            {
                var translated = Text(run.Text);
                if (translated != run.Text) run.Text = translated;
            }
            if (item is TextBlock block && block.Inlines.Count <= 1 &&
                !System.Windows.Data.BindingOperations.IsDataBound(block, TextBlock.TextProperty))
            {
                var translated = Text(block.Text);
                if (translated != block.Text) block.Text = translated;
            }
            if (item is ContentControl control && control.Content is string content &&
                !System.Windows.Data.BindingOperations.IsDataBound(control, ContentControl.ContentProperty))
            {
                var translated = Text(content);
                if (translated != content) control.Content = translated;
            }
            if (item is HeaderedItemsControl header && header.Header is string heading &&
                !System.Windows.Data.BindingOperations.IsDataBound(header, HeaderedItemsControl.HeaderProperty))
            {
                var translated = Text(heading);
                if (translated != heading) header.Header = translated;
            }
            if (item is FrameworkElement element)
            {
                if (element.ToolTip is string tip) element.ToolTip = Text(tip);
                var name = AutomationProperties.GetName(element);
                if (!string.IsNullOrEmpty(name)) AutomationProperties.SetName(element, Text(name));
                if (element.ContextMenu is { } menu) Visit(menu);
            }
            foreach (var child in LogicalTreeHelper.GetChildren(item).OfType<DependencyObject>().ToArray()) Visit(child);
        }
    }
}
