using System.IO;
using System.Security.Cryptography;
using System.Text;
using UsageOverlay.Infrastructure;

namespace UsageOverlay.Services;

/// <summary>
/// Keeps the Cursor Admin API key encrypted with Windows DPAPI for the current user.
/// The key is never written to settings, logs or reports.
/// </summary>
public sealed class CursorCredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("UsageOverlay.CursorAdminKey.v1");
    private readonly string _path;

    public CursorCredentialStore(string? path = null) => _path = path ?? AppPaths.CursorKeyPath;

    public bool HasKey => File.Exists(_path);

    public string? Read()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            var bytes = ProtectedData.Unprotect(File.ReadAllBytes(_path), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or CryptographicException)
        {
            return null;
        }
    }

    public void Save(string key)
    {
        var protectedBytes = ProtectedData.Protect(
            Encoding.UTF8.GetBytes(key.Trim()),
            Entropy,
            DataProtectionScope.CurrentUser);
        var temporaryPath = _path + ".tmp";
        File.WriteAllBytes(temporaryPath, protectedBytes);
        File.Move(temporaryPath, _path, overwrite: true);
    }

    public void Delete()
    {
        if (File.Exists(_path))
        {
            File.Delete(_path);
        }
    }
}
