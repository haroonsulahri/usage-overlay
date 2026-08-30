using System.Reflection;

namespace UsageOverlay.Infrastructure;

public static class AppVersion
{
    public static string Current
    {
        get
        {
            var assembly = Assembly.GetEntryAssembly() ?? typeof(AppVersion).Assembly;
            var informationalVersion = assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion;

            if (!string.IsNullOrWhiteSpace(informationalVersion))
            {
                return informationalVersion.Split('+', 2)[0];
            }

            return assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        }
    }
}
