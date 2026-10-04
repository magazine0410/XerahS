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
using Newtonsoft.Json.Linq;

namespace XerahS.Uploaders.PluginSystem;

/// <summary>
/// Shared secret-store handling for providers whose settings JSON holds a <c>SecretKey</c>: the references for
/// backups, and moving plaintext values from a ShareX configuration import into the secret store.
/// </summary>
public static class InstanceSecretHelper
{
    public const string SecretKeyProperty = "SecretKey";

    public static string? ReadSecretKey(string? settingsJson)
    {
        if (string.IsNullOrWhiteSpace(settingsJson)) return null;
        try
        {
            return JObject.Parse(settingsJson).Value<string>(SecretKeyProperty);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static IReadOnlyList<InstanceSecretReference> GetReferences(string providerId, string settingsJson, params string[] names)
    {
        string? secretKey = ReadSecretKey(settingsJson);
        return string.IsNullOrWhiteSpace(secretKey) ? [] : names.Select(name => new InstanceSecretReference(providerId, secretKey, name)).ToArray();
    }

    /// <summary>
    /// Moves each non-empty plaintext property named in <paramref name="propertyToSecret"/> into the secret store
    /// and removes it from the JSON. Returns false when there is nothing to move or a value could not be stored.
    /// </summary>
    public static bool MigratePlaintext(string settingsJson, ISecretStore secrets, string providerId,
        IReadOnlyDictionary<string, string> propertyToSecret, out string updatedSettingsJson, out int migratedSecretCount)
    {
        updatedSettingsJson = settingsJson;
        migratedSecretCount = 0;

        JObject json;
        try { json = JObject.Parse(settingsJson); }
        catch (JsonException) { return false; }

        var values = propertyToSecret
            .Select(pair => (Property: pair.Key, Secret: pair.Value, Value: json.Value<string>(pair.Key)))
            .Where(item => !string.IsNullOrEmpty(item.Value))
            .ToList();
        if (values.Count == 0) return false;

        string secretKey = json.Value<string>(SecretKeyProperty) is { Length: > 0 } key ? key : Guid.NewGuid().ToString("N");
        foreach (var item in values)
        {
            secrets.SetSecret(providerId, secretKey, item.Secret, item.Value!);
            if (!secrets.HasSecret(providerId, secretKey, item.Secret)) return false;
        }

        json[SecretKeyProperty] = secretKey;
        foreach (var item in values) json.Remove(item.Property);
        updatedSettingsJson = json.ToString(Formatting.Indented);
        migratedSecretCount = values.Count;
        return true;
    }
}
