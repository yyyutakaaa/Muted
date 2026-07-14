using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;

namespace Muted.App.Services;

internal sealed class UpdateService
{
    private const string LatestReleaseUrl =
        "https://api.github.com/repos/yyyutakaaa/Muted/releases/latest";
    private static readonly string[] UninstallKeyNames =
    [
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{B3B7E6C1-6E6A-4C6B-9C1E-7B6E7E9A0F3D}}_is1",
        @"Software\Microsoft\Windows\CurrentVersion\Uninstall\{B3B7E6C1-6E6A-4C6B-9C1E-7B6E7E9A0F3D}_is1"
    ];
    private const long MaximumInstallerSize = 250L * 1024 * 1024;

    private static readonly HttpClient HttpClient = CreateHttpClient();
    private readonly FileLog _log;

    public UpdateService(FileLog log)
    {
        _log = log;
    }

    public async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        var currentVersion = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0);
        if (!IsInstalledCopy())
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.NotInstalled,
                "Automatic updates are available for installed copies of Muted only.");
        }

        try
        {
            using var response = await HttpClient.GetAsync(LatestReleaseUrl, cancellationToken)
                .ConfigureAwait(false);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult(
                    UpdateCheckStatus.Unavailable,
                    "No published Muted release is available yet.");
            }

            response.EnsureSuccessStatusCode();
            await using var releaseStream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            var release = await JsonSerializer.DeserializeAsync<GitHubRelease>(
                    releaseStream,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            if (release is null || release.Draft || release.Prerelease ||
                !TryParseVersion(release.TagName, out var availableVersion))
            {
                return new UpdateCheckResult(
                    UpdateCheckStatus.Unavailable,
                    "The latest release information is invalid.");
            }

            if (availableVersion <= currentVersion)
            {
                return new UpdateCheckResult(
                    UpdateCheckStatus.UpToDate,
                    $"Muted {currentVersion.ToString(3)} is up to date.");
            }

            var versionText = availableVersion.ToString(3);
            var installerName = $"Muted-Setup-{versionText}.exe";
            var checksumName = installerName + ".sha256";
            var installerAsset = release.Assets?.FirstOrDefault(asset => asset.Name == installerName);
            var checksumAsset = release.Assets?.FirstOrDefault(asset => asset.Name == checksumName);
            if (installerAsset is null || checksumAsset is null ||
                installerAsset.Size <= 0 || installerAsset.Size > MaximumInstallerSize ||
                checksumAsset.Size <= 0 || checksumAsset.Size > 4096)
            {
                _log.WriteMessage($"Release {release.TagName} has no valid installer and checksum assets.");
                return new UpdateCheckResult(
                    UpdateCheckStatus.Unavailable,
                    $"Muted {versionText} is published, but its installer is unavailable.");
            }

            var update = new AvailableUpdate(
                currentVersion,
                availableVersion,
                installerName,
                installerAsset.DownloadUrl,
                installerAsset.Size,
                checksumAsset.DownloadUrl);
            return new UpdateCheckResult(
                UpdateCheckStatus.UpdateAvailable,
                $"Muted {versionText} is available.",
                update);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            _log.Write(exception, "Check for updates");
            return new UpdateCheckResult(
                UpdateCheckStatus.Unavailable,
                "Muted could not check for updates. Check your internet connection and try again.");
        }
    }

    public async Task<bool> DownloadAndStartAsync(
        AvailableUpdate update,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var expectedHash = await DownloadChecksumAsync(update.ChecksumUrl, cancellationToken)
                .ConfigureAwait(false);
            var updateDirectory = Path.Combine(AppPaths.DataDirectory, "Updates");
            Directory.CreateDirectory(updateDirectory);
            var installerPath = Path.Combine(updateDirectory, update.InstallerName);
            var temporaryPath = installerPath + ".download";

            try
            {
                var actualHash = await DownloadInstallerAsync(
                        update.InstallerUrl,
                        temporaryPath,
                        update.InstallerSize,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!CryptographicOperations.FixedTimeEquals(actualHash, expectedHash))
                {
                    throw new InvalidDataException(
                        "The downloaded update checksum does not match the release checksum.");
                }

                File.Move(temporaryPath, installerPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }

            var process = Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTMUTED=1",
                UseShellExecute = true,
                WorkingDirectory = updateDirectory
            });
            if (process is null)
            {
                throw new InvalidOperationException("Windows could not start the update installer.");
            }

            _log.WriteMessage(
                $"Started approved update from {update.CurrentVersion} to {update.AvailableVersion}.");
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
        catch (Exception exception)
        {
            _log.Write(exception, "Install update");
            return false;
        }
    }

    internal static bool TryParseVersion(string? tagName, out Version version)
    {
        version = new Version(0, 0);
        var value = tagName?.Trim();
        if (value?.StartsWith('v') == true)
        {
            value = value[1..];
        }

        var parts = value?.Split('.');
        if (parts?.Length != 3 || !parts.All(part =>
                int.TryParse(part, out var component) && component >= 0) ||
            !Version.TryParse(value, out var parsedVersion))
        {
            return false;
        }

        version = parsedVersion;
        return true;
    }

    private static bool IsInstalledCopy()
    {
        var executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return false;
        }

        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        {
            foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                foreach (var uninstallKeyName in UninstallKeyNames)
                {
                    using var uninstallKey = baseKey.OpenSubKey(uninstallKeyName);
                    if (uninstallKey?.GetValue("InstallLocation") is not string installLocation ||
                        string.IsNullOrWhiteSpace(installLocation))
                    {
                        continue;
                    }

                    var installedExecutable = Path.GetFullPath(Path.Combine(installLocation, "Muted.exe"));
                    if (string.Equals(
                            installedExecutable,
                            Path.GetFullPath(executablePath),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static async Task<byte[]> DownloadChecksumAsync(
        string url,
        CancellationToken cancellationToken)
    {
        var checksum = (await HttpClient.GetStringAsync(url, cancellationToken).ConfigureAwait(false)).Trim();
        var firstField = checksum.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (firstField is null || firstField.Length != 64)
        {
            throw new InvalidDataException("The release checksum has an invalid format.");
        }

        return Convert.FromHexString(firstField);
    }

    private static async Task<byte[]> DownloadInstallerAsync(
        string url,
        string path,
        long expectedSize,
        CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken)
            .ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumInstallerSize)
        {
            throw new InvalidDataException("The update installer is unexpectedly large.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);
        await using var destination = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[81920];
        long totalBytes = 0;
        int bytesRead;
        while ((bytesRead = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            totalBytes += bytesRead;
            if (totalBytes > MaximumInstallerSize)
            {
                throw new InvalidDataException("The update installer exceeded the size limit.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken)
                .ConfigureAwait(false);
            hash.AppendData(buffer, 0, bytesRead);
        }

        if (totalBytes != expectedSize)
        {
            throw new InvalidDataException("The downloaded update size does not match the release metadata.");
        }

        return hash.GetHashAndReset();
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Muted", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private sealed record GitHubRelease(
        [property: System.Text.Json.Serialization.JsonPropertyName("tag_name")] string TagName,
        [property: System.Text.Json.Serialization.JsonPropertyName("draft")] bool Draft,
        [property: System.Text.Json.Serialization.JsonPropertyName("prerelease")] bool Prerelease,
        [property: System.Text.Json.Serialization.JsonPropertyName("assets")] GitHubAsset[]? Assets);

    private sealed record GitHubAsset(
        [property: System.Text.Json.Serialization.JsonPropertyName("name")] string Name,
        [property: System.Text.Json.Serialization.JsonPropertyName("size")] long Size,
        [property: System.Text.Json.Serialization.JsonPropertyName("browser_download_url")] string DownloadUrl);
}

internal enum UpdateCheckStatus
{
    UpdateAvailable,
    UpToDate,
    NotInstalled,
    Unavailable
}

internal sealed record AvailableUpdate(
    Version CurrentVersion,
    Version AvailableVersion,
    string InstallerName,
    string InstallerUrl,
    long InstallerSize,
    string ChecksumUrl);

internal sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    string Message,
    AvailableUpdate? Update = null);
