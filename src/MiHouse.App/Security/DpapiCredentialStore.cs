using System.Security.Cryptography;
using System.Text.Json;
using MiHouse.Core.Auth;
using MiHouse.Core.Session;

namespace MiHouse.App.Security;

/// <summary>
/// Stores the Mi Home session in the current Windows user profile, protected with DPAPI.
/// </summary>
public sealed class DpapiCredentialStore : ICredentialStore
{
    private static readonly JsonSerializerOptions _serializerOptions = new(JsonSerializerDefaults.Web);

    private static readonly SemaphoreSlim _gate = new(1, 1);
    private readonly string _directory;
    private readonly string _path;

    /// <summary>
    /// Initializes a new instance of the <see cref="DpapiCredentialStore"/> class.
    /// </summary>
    public DpapiCredentialStore()
    {
        _directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MiHouse");
        _path = Path.Combine(_directory, "session.dat");
    }

    /// <inheritdoc />
    public async Task<MiHomeSession?> LoadAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!File.Exists(_path))
            {
                return null;
            }

            byte[] protectedBytes;
            await using (var stream = new FileStream(
                _path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan))
            {
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
                protectedBytes = buffer.ToArray();
            }

            var plainBytes = ProtectedData.Unprotect(
                protectedBytes,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);

            var session = JsonSerializer.Deserialize<MiHomeSession>(plainBytes, _serializerOptions);
            if (session is null
                || string.IsNullOrEmpty(session.UserId)
                || string.IsNullOrEmpty(session.ServiceToken)
                || string.IsNullOrEmpty(session.Ssecurity)
                || string.IsNullOrEmpty(session.Region))
            {
                TryDelete(_path);
                return null;
            }

            return session;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or CryptographicException or JsonException)
        {
            // Missing or corrupt state is the same as being signed out; do not carry it forward.
            TryDelete(_path);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task SaveAsync(MiHomeSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        var temporaryPath = $"{_path}.{Guid.NewGuid():N}.tmp";

        try
        {
            Directory.CreateDirectory(_directory);

            var plainBytes = JsonSerializer.SerializeToUtf8Bytes(session, _serializerOptions);
            var protectedBytes = ProtectedData.Protect(
                plainBytes,
                optionalEntropy: null,
                DataProtectionScope.CurrentUser);

            await using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.Asynchronous))
            {
                await stream.WriteAsync(protectedBytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, _path, overwrite: true);
        }
        finally
        {
            TryDelete(temporaryPath);
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task ClearAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Sign-out must report a real failure rather than leave the session behind.
            File.Delete(_path);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
