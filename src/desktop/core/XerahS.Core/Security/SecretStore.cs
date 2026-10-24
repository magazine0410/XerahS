#region License Information (GPL v3)

/*
    XerahS - The Avalonia UI implementation of ShareX
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using Newtonsoft.Json;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using XerahS.Common;
using XerahS.Uploaders.PluginSystem;

namespace XerahS.Core.Security;

public sealed class SecretStore : ISecretStore, ISecretStoreInfo
{
    private readonly ISecretStore _backend;
    private readonly string _indexFilePath;
    private readonly object _indexLock = new();
    private readonly string _backendName;
    private readonly string _backendDetails;
    private readonly bool _isFallback;

    public SecretStore(string filePath)
    {
        var backend = CreateBackend(filePath);
        _backend = backend.Backend;
        _indexFilePath = filePath + ".index.json";
        _backendName = backend.Name;
        _backendDetails = backend.Details;
        _isFallback = backend.IsFallback;
    }

    public string BackendName => _backendName;

    public string BackendDetails => _backendDetails;

    public bool IsFallback => _isFallback;

    public string? GetSecret(string providerId, string secretKey, string name)
    {
        string? value = _backend.GetSecret(providerId, secretKey, name);
        if (value != null)
        {
            RememberReference(new PortableSecretReference(providerId, secretKey, name));
        }

        return value;
    }

    public void SetSecret(string providerId, string secretKey, string name, string value)
    {
        _backend.SetSecret(providerId, secretKey, name, value);
        RememberReference(new PortableSecretReference(providerId, secretKey, name));
    }

    public void DeleteSecret(string providerId, string secretKey, string name)
    {
        _backend.DeleteSecret(providerId, secretKey, name);
        ForgetReference(new PortableSecretReference(providerId, secretKey, name));
    }

    public bool HasSecret(string providerId, string secretKey, string name)
        => _backend.HasSecret(providerId, secretKey, name);

    /// <summary>
    /// Exports plaintext values for secrets that have been used through this store.
    /// The caller is responsible for protecting the returned values.
    /// </summary>
    public IReadOnlyList<PortableSecret> ExportIndexedSecrets()
    {
        var result = new List<PortableSecret>();
        foreach (PortableSecretReference reference in LoadReferences())
        {
            string? value = _backend.GetSecret(reference.ProviderId, reference.SecretKey, reference.Name);
            if (value != null)
            {
                result.Add(new PortableSecret(reference.ProviderId, reference.SecretKey, reference.Name, value));
            }
        }

        return result;
    }

    private void RememberReference(PortableSecretReference reference)
    {
        if (!reference.IsValid)
        {
            return;
        }

        lock (_indexLock)
        {
            List<PortableSecretReference> references = LoadReferencesCore();
            if (references.Contains(reference))
            {
                return;
            }

            references.Add(reference);
            SaveReferencesCore(references);
        }
    }

    private void ForgetReference(PortableSecretReference reference)
    {
        lock (_indexLock)
        {
            List<PortableSecretReference> references = LoadReferencesCore();
            if (references.Remove(reference))
            {
                SaveReferencesCore(references);
            }
        }
    }

    private IReadOnlyList<PortableSecretReference> LoadReferences()
    {
        lock (_indexLock)
        {
            return LoadReferencesCore();
        }
    }

    private List<PortableSecretReference> LoadReferencesCore()
    {
        try
        {
            if (!File.Exists(_indexFilePath))
            {
                return new List<PortableSecretReference>();
            }

            string json = File.ReadAllText(_indexFilePath);
            return JsonConvert.DeserializeObject<List<PortableSecretReference>>(json)?
                .Where(reference => reference.IsValid)
                .Distinct()
                .ToList()
                ?? new List<PortableSecretReference>();
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "SecretStore failed to load its portable backup index.");
            return new List<PortableSecretReference>();
        }
    }

    private void SaveReferencesCore(List<PortableSecretReference> references)
    {
        try
        {
            string? directory = Path.GetDirectoryName(_indexFilePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string tempFilePath = _indexFilePath + ".temp";
            string json = JsonConvert.SerializeObject(references, Formatting.Indented);
            File.WriteAllText(tempFilePath, json);
            File.Move(tempFilePath, _indexFilePath, overwrite: true);
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "SecretStore failed to save its portable backup index.");
        }
    }

    /// <summary>
    /// Set to 1 to keep secrets in the encrypted file next to the settings instead of the system keyring (macOS
    /// Keychain, Linux Secret Service). The tests set it, so that they never write to the user's keyring.
    /// </summary>
    public const string FileStoreOverrideVariable = "XERAHS_SECRET_STORE_FILE";

    private static BackendSelection CreateBackend(string filePath)
    {
        const string serviceName = "XerahS";

        if (OperatingSystem.IsWindows())
        {
            return BackendSelection.Create(
                new DpapiFileSecretStore(filePath),
                "Windows DPAPI file store",
                "DPAPI-protected SecretsStore.json",
                isFallback: false);
        }

        // -sandbox: credentials stay in the temporary sandbox folder with the other settings, never in the system keyring.
        if (PathsManager.IsSandbox)
        {
            return BackendSelection.Create(
                new AesFileSecretStore(filePath, GetKeyPath(filePath)),
                "AES file store (sandbox)",
                "AES-GCM SecretsStore.json + SecretsStore.key in the temporary sandbox folder",
                isFallback: true);
        }

        if (Environment.GetEnvironmentVariable(FileStoreOverrideVariable) == "1")
        {
            return BackendSelection.Create(
                new AesFileSecretStore(filePath, GetKeyPath(filePath)),
                "AES file store",
                $"AES-GCM SecretsStore.json + SecretsStore.key ({FileStoreOverrideVariable}=1)",
                isFallback: true);
        }

        if (OperatingSystem.IsMacOS())
        {
            if (MacOSKeychainSecretStore.IsAvailable())
            {
                return BackendSelection.Create(
                    new MacOSKeychainSecretStore(serviceName),
                    "macOS Keychain",
                    $"Stored in Keychain service \"{serviceName}\"",
                    isFallback: false);
            }

            return BackendSelection.Create(
                new AesFileSecretStore(filePath, GetKeyPath(filePath)),
                "AES file store (fallback)",
                "AES-GCM SecretsStore.json + SecretsStore.key",
                isFallback: true);
        }

        if (OperatingSystem.IsLinux())
        {
            if (LinuxLibsecretSecretStore.IsAvailable(serviceName))
            {
                var keyring = new LinuxLibsecretSecretStore(serviceName);
                // Secrets saved while the keyring check failed are in the file store; they move to the keyring first.
                if (MoveFileSecretsToKeyring(filePath, keyring))
                {
                    return BackendSelection.Create(
                        keyring,
                        "Linux libsecret",
                        $"Stored via secret-tool service \"{serviceName}\"",
                        isFallback: false);
                }
            }

            return BackendSelection.Create(
                new AesFileSecretStore(filePath, GetKeyPath(filePath)),
                "AES file store (fallback)",
                "AES-GCM SecretsStore.json + SecretsStore.key",
                isFallback: true);
        }

        return BackendSelection.Create(
            new AesFileSecretStore(filePath, GetKeyPath(filePath)),
            "AES file store (fallback)",
            "AES-GCM SecretsStore.json + SecretsStore.key",
            isFallback: true);
    }

    /// <summary>
    /// Copies the file store's secrets into the keyring, each checked by reading it back, then deletes the file and
    /// its key, which could otherwise still decrypt the secrets. The files are left as they are when a secret could
    /// not be copied, and false is returned so that the file store stays in use.
    /// </summary>
    internal static bool MoveFileSecretsToKeyring(string filePath, IKeyringAccounts keyring)
    {
        if (!File.Exists(filePath))
        {
            // Earlier versions renamed the moved store to .migrated-to-keyring and left the key next to it.
            DeleteMovedFileStore(filePath);
            return true;
        }

        try
        {
            var fileStore = new AesFileSecretStore(filePath, GetKeyPath(filePath));
            int moved = 0;
            foreach ((string account, string? value) in fileStore.GetAllSecrets())
            {
                if (value == null)
                {
                    DebugHelper.WriteLine($"SecretStore: {account} could not be decrypted and is not moved to the keyring.");
                    continue;
                }

                // An empty value reads back as no secret.
                string? expected = value.Length == 0 ? null : value;
                if (keyring.GetSecret(account) != expected)
                {
                    keyring.SetSecret(account, value);
                    if (keyring.GetSecret(account) != expected)
                    {
                        DebugHelper.WriteLine($"SecretStore: {account} could not be stored in the keyring; the file store stays in use.");
                        return false;
                    }
                }

                moved++;
            }

            File.Delete(filePath);
            DeleteMovedFileStore(filePath);
            DebugHelper.WriteLine($"SecretStore: moved {moved} secret(s) from {Path.GetFileName(filePath)} to the keyring.");
            return true;
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, "SecretStore failed to move the file store's secrets to the keyring.");
            return false;
        }
    }

    private static void DeleteMovedFileStore(string filePath)
    {
        foreach (string path in new[] { filePath + ".migrated-to-keyring", GetKeyPath(filePath) })
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                    DebugHelper.WriteLine($"SecretStore: deleted {Path.GetFileName(path)}, which the keyring replaced.");
                }
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex, $"SecretStore could not delete {Path.GetFileName(path)}.");
            }
        }
    }

    private static string GetKeyPath(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath) ?? ".";
        return Path.Combine(directory, "SecretsStore.key");
    }

    [SupportedOSPlatform("windows")]
    private sealed class DpapiFileSecretStore : ISecretStore
    {
        private readonly object _lock = new();
        private readonly string _filePath;
        private readonly Dictionary<string, string> _secrets = new(StringComparer.OrdinalIgnoreCase);
        private bool _loaded;

        public DpapiFileSecretStore(string filePath)
        {
            _filePath = filePath;
        }

        public string? GetSecret(string providerId, string secretKey, string name)
        {
            var key = BuildKey(providerId, secretKey, name);
            lock (_lock)
            {
                EnsureLoaded();
                if (!_secrets.TryGetValue(key, out var encrypted))
                {
                    return null;
                }

                try
                {
                    return DPAPI.Decrypt(encrypted);
                }
                catch (Exception ex)
                {
                    DebugHelper.WriteException(ex, $"SecretStore failed to decrypt secret: {key}");
                    return null;
                }
            }
        }

        public void SetSecret(string providerId, string secretKey, string name, string value)
        {
            var key = BuildKey(providerId, secretKey, name);
            lock (_lock)
            {
                EnsureLoaded();
                _secrets[key] = DPAPI.Encrypt(value);
                Save();
            }
        }

        public void DeleteSecret(string providerId, string secretKey, string name)
        {
            var key = BuildKey(providerId, secretKey, name);
            lock (_lock)
            {
                EnsureLoaded();
                if (_secrets.Remove(key))
                {
                    Save();
                }
            }
        }

        public bool HasSecret(string providerId, string secretKey, string name)
        {
            var key = BuildKey(providerId, secretKey, name);
            lock (_lock)
            {
                EnsureLoaded();
                return _secrets.ContainsKey(key);
            }
        }

        private void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }

            if (File.Exists(_filePath))
            {
                try
                {
                    var json = File.ReadAllText(_filePath);
                    var model = JsonConvert.DeserializeObject<SecretStoreModel>(json);
                    if (model?.Secrets != null)
                    {
                        foreach (var pair in model.Secrets)
                        {
                            _secrets[pair.Key] = pair.Value;
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugHelper.WriteException(ex, "SecretStore failed to load secrets file.");
                }
            }

            _loaded = true;
        }

        private void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var model = new SecretStoreModel
                {
                    Version = 1,
                    Secrets = new Dictionary<string, string>(_secrets)
                };

                var json = JsonConvert.SerializeObject(model, Formatting.Indented);
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex, "SecretStore failed to save secrets file.");
            }
        }
    }

    internal sealed class AesFileSecretStore : ISecretStore
    {
        private readonly object _lock = new();
        private readonly string _filePath;
        private readonly string _keyPath;
        private readonly Dictionary<string, string> _secrets = new(StringComparer.OrdinalIgnoreCase);
        private bool _loaded;

        public AesFileSecretStore(string filePath, string keyPath)
        {
            _filePath = filePath;
            _keyPath = keyPath;
        }

        public string? GetSecret(string providerId, string secretKey, string name)
        {
            lock (_lock)
            {
                return GetSecretCore(BuildKey(providerId, secretKey, name));
            }
        }

        /// <summary>Every stored account ("providerId:secretKey:name") with its value; null when it cannot be decrypted.</summary>
        public IReadOnlyList<(string Account, string? Value)> GetAllSecrets()
        {
            lock (_lock)
            {
                EnsureLoaded();
                return _secrets.Keys.ToList().Select(key => (key, GetSecretCore(key))).ToList();
            }
        }

        private string? GetSecretCore(string key)
        {
            {
                EnsureLoaded();
                if (!_secrets.TryGetValue(key, out var encrypted))
                {
                    return null;
                }

                try
                {
                    var bytes = Convert.FromBase64String(encrypted);
                    if (bytes.Length < 12 + 16)
                    {
                        return null;
                    }

                    var nonce = bytes.AsSpan(0, 12).ToArray();
                    var tag = bytes.AsSpan(12, 16).ToArray();
                    var cipher = bytes.AsSpan(28).ToArray();

                    var keyBytes = LoadOrCreateKey();
                    var plain = new byte[cipher.Length];
                    using var aes = new AesGcm(keyBytes, 16);
                    aes.Decrypt(nonce, cipher, tag, plain);
                    return Encoding.UTF8.GetString(plain);
                }
                catch (Exception ex)
                {
                    DebugHelper.WriteException(ex, $"SecretStore AES decrypt failed for {key}");
                    return null;
                }
            }
        }

        public void SetSecret(string providerId, string secretKey, string name, string value)
        {
            var key = BuildKey(providerId, secretKey, name);
            lock (_lock)
            {
                EnsureLoaded();
                var keyBytes = LoadOrCreateKey();
                var nonce = RandomNumberGenerator.GetBytes(12);
                var plain = Encoding.UTF8.GetBytes(value);
                var cipher = new byte[plain.Length];
                var tag = new byte[16];
                using var aes = new AesGcm(keyBytes, 16);
                aes.Encrypt(nonce, plain, cipher, tag);
                var combined = new byte[nonce.Length + tag.Length + cipher.Length];
                Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
                Buffer.BlockCopy(tag, 0, combined, nonce.Length, tag.Length);
                Buffer.BlockCopy(cipher, 0, combined, nonce.Length + tag.Length, cipher.Length);
                _secrets[key] = Convert.ToBase64String(combined);
                Save();
            }
        }

        public void DeleteSecret(string providerId, string secretKey, string name)
        {
            var key = BuildKey(providerId, secretKey, name);
            lock (_lock)
            {
                EnsureLoaded();
                if (_secrets.Remove(key))
                {
                    Save();
                }
            }
        }

        public bool HasSecret(string providerId, string secretKey, string name)
        {
            var key = BuildKey(providerId, secretKey, name);
            lock (_lock)
            {
                EnsureLoaded();
                return _secrets.ContainsKey(key);
            }
        }

        private void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }

            if (File.Exists(_filePath))
            {
                try
                {
                    var json = File.ReadAllText(_filePath);
                    var model = JsonConvert.DeserializeObject<SecretStoreModel>(json);
                    if (model?.Secrets != null)
                    {
                        foreach (var pair in model.Secrets)
                        {
                            _secrets[pair.Key] = pair.Value;
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugHelper.WriteException(ex, "SecretStore failed to load AES secrets file.");
                }
            }

            _loaded = true;
        }

        private void Save()
        {
            try
            {
                var directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var model = new SecretStoreModel
                {
                    Version = 1,
                    Secrets = new Dictionary<string, string>(_secrets)
                };

                var json = JsonConvert.SerializeObject(model, Formatting.Indented);
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex, "SecretStore failed to save AES secrets file.");
            }
        }

        private byte[] LoadOrCreateKey()
        {
            try
            {
                if (File.Exists(_keyPath))
                {
                    var text = File.ReadAllText(_keyPath);
                    return Convert.FromBase64String(text);
                }

                var key = RandomNumberGenerator.GetBytes(32);
                var directory = Path.GetDirectoryName(_keyPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(_keyPath, Convert.ToBase64String(key));
                return key;
            }
            catch (Exception ex)
            {
                DebugHelper.WriteException(ex, "SecretStore failed to load or create AES key.");
                return RandomNumberGenerator.GetBytes(32);
            }
        }
    }

    private sealed class MacOSKeychainSecretStore : ISecretStore
    {
        private readonly string _service;
        private readonly string _securityPath;
        private readonly string _keychainArgument;

        public MacOSKeychainSecretStore(string service)
        {
            _service = service;
            _securityPath = File.Exists("/usr/bin/security") ? "/usr/bin/security" : "security";
            _keychainArgument = BuildKeychainArgument();
        }

        public static bool IsAvailable()
        {
            return File.Exists("/usr/bin/security") || CommandExists("security", "-h");
        }

        public string? GetSecret(string providerId, string secretKey, string name)
        {
            var account = BuildKey(providerId, secretKey, name);
            var result = RunCommand(_securityPath, $"find-generic-password -a \"{account}\" -s \"{_service}\" -w{_keychainArgument}");
            return string.IsNullOrWhiteSpace(result) ? null : result.Trim();
        }

        public void SetSecret(string providerId, string secretKey, string name, string value)
        {
            var account = BuildKey(providerId, secretKey, name);
            RunCommand(_securityPath, $"add-generic-password -a \"{account}\" -s \"{_service}\" -w \"{value}\" -U{_keychainArgument}");
        }

        public void DeleteSecret(string providerId, string secretKey, string name)
        {
            var account = BuildKey(providerId, secretKey, name);
            RunCommand(_securityPath, $"delete-generic-password -a \"{account}\" -s \"{_service}\"{_keychainArgument}");
        }

        public bool HasSecret(string providerId, string secretKey, string name)
        {
            var account = BuildKey(providerId, secretKey, name);
            var result = RunCommand(_securityPath, $"find-generic-password -a \"{account}\" -s \"{_service}\" -w{_keychainArgument}");
            return !string.IsNullOrWhiteSpace(result);
        }

        private static string BuildKeychainArgument()
        {
            foreach (string home in GetCandidateHomeDirectories())
            {
                string loginKeychain = Path.Combine(home, "Library", "Keychains", "login.keychain-db");
                if (File.Exists(loginKeychain))
                {
                    return $" \"{loginKeychain}\"";
                }
            }

            return string.Empty;
        }

        private static IEnumerable<string> GetCandidateHomeDirectories()
        {
            string specialFolder = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!string.IsNullOrWhiteSpace(specialFolder))
            {
                yield return specialFolder;
            }

            string? home = Environment.GetEnvironmentVariable("HOME");
            if (!string.IsNullOrWhiteSpace(home))
            {
                yield return home;
            }

            string userName = Environment.UserName;
            if (!string.IsNullOrWhiteSpace(userName) && OperatingSystem.IsMacOS())
            {
                yield return Path.Combine("/Users", userName);
            }
        }
    }

    /// <summary>The keyring's reads and writes by full account name, which moving the file store needs.</summary>
    internal interface IKeyringAccounts
    {
        string? GetSecret(string account);

        void SetSecret(string account, string value);
    }

    internal sealed class LinuxLibsecretSecretStore : ISecretStore, IKeyringAccounts
    {
        private readonly string _service;

        public LinuxLibsecretSecretStore(string service)
        {
            _service = service;
        }

        /// <summary>
        /// Whether the Secret Service (GNOME Keyring, KWallet, KeePassXC) answers. secret-tool has no --version: a
        /// lookup of an item that does not exist exits with 1 and no message when the service answers, and prints
        /// why when it cannot be reached.
        /// </summary>
        public static bool IsAvailable(string service)
        {
            (int exitCode, string error) = ProbeCommand("secret-tool", $"lookup service {service} key xerahs-availability-probe");
            return IsAvailableProbeResult(exitCode, error);
        }

        internal static bool IsAvailableProbeResult(int exitCode, string error) =>
            exitCode == 0 || exitCode == 1 && string.IsNullOrWhiteSpace(error);

        public string? GetSecret(string providerId, string secretKey, string name) => GetSecret(BuildKey(providerId, secretKey, name));

        public string? GetSecret(string account) =>
            ReadLookupOutput(RunCommand("secret-tool", $"lookup service {_service} key \"{account}\""));

        /// <summary>
        /// secret-tool writes the stored value as it is, adding a line break only when the output is a terminal, so
        /// the value is not trimmed: a secret with leading or trailing spaces is read back unchanged.
        /// </summary>
        internal static string? ReadLookupOutput(string output) => output.Length == 0 ? null : output;

        public void SetSecret(string providerId, string secretKey, string name, string value) => SetSecret(BuildKey(providerId, secretKey, name), value);

        public void SetSecret(string account, string value)
        {
            RunCommandWithInput("secret-tool", $"store --label=\"XerahS Secret\" service {_service} key \"{account}\"", value);
        }

        public void DeleteSecret(string providerId, string secretKey, string name)
        {
            var account = BuildKey(providerId, secretKey, name);
            RunCommand("secret-tool", $"clear service {_service} key \"{account}\"");
        }

        public bool HasSecret(string providerId, string secretKey, string name)
        {
            var account = BuildKey(providerId, secretKey, name);
            var result = RunCommand("secret-tool", $"lookup service {_service} key \"{account}\"");
            return !string.IsNullOrWhiteSpace(result);
        }
    }

    private sealed class SecretStoreModel
    {
        public int Version { get; set; }
        public Dictionary<string, string> Secrets { get; set; } = new();
    }

    private static string BuildKey(string providerId, string secretKey, string name)
    {
        return $"{providerId}:{secretKey}:{name}";
    }

    /// <summary>Runs a command and returns its exit code and error output; -1 when it cannot run or takes too long.</summary>
    private static (int ExitCode, string Error) ProbeCommand(string command, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null)
            {
                return (-1, string.Empty);
            }

            Task<string> error = process.StandardError.ReadToEndAsync();
            _ = process.StandardOutput.ReadToEndAsync();
            // Starting the Secret Service on demand (KWallet, GNOME Keyring) can take a few seconds.
            if (!process.WaitForExit(5000))
            {
                try { process.Kill(); } catch { }
                return (-1, string.Empty);
            }

            return (process.ExitCode, error.Wait(1000) ? error.Result : string.Empty);
        }
        catch
        {
            return (-1, string.Empty);
        }
    }

    private static bool CommandExists(string command, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = command,
                Arguments = args,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process == null)
            {
                return false;
            }

            if (!process.WaitForExit(2000))
            {
                try { process.Kill(); } catch { }
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static string RunCommand(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return string.Empty;
            }

            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(5000);

            if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(error))
            {
                DebugHelper.WriteLine($"SecretStore command error: {error}");
            }

            return output;
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, $"SecretStore command failed: {fileName} {arguments}");
            return string.Empty;
        }
    }

    private static void RunCommandWithInput(string fileName, string arguments, string input)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                return;
            }

            process.StandardInput.Write(input);
            process.StandardInput.Close();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit(5000);

            if (process.ExitCode != 0 && !string.IsNullOrWhiteSpace(error))
            {
                DebugHelper.WriteLine($"SecretStore command error: {error}");
            }
        }
        catch (Exception ex)
        {
            DebugHelper.WriteException(ex, $"SecretStore command failed: {fileName} {arguments}");
        }
    }

    private readonly record struct BackendSelection(ISecretStore Backend, string Name, string Details, bool IsFallback)
    {
        public static BackendSelection Create(ISecretStore backend, string name, string details, bool isFallback)
            => new(backend, name, details, isFallback);
    }
}
