using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using NzbDrone.Core.Datastore;

namespace NzbDrone.Core.Backup
{
    internal class UpstreamBackupManifest
    {
        [JsonRequired]
        public int FormatVersion { get; set; }

        [JsonRequired]
        public DatabaseType DatabaseType { get; set; }

        [JsonRequired]
        public Dictionary<string, string> Files { get; set; }

        internal static void ValidateIfPresent(string folder)
        {
            var path = Path.Combine(folder, "manifest.json");
            if (!File.Exists(path) || (!File.Exists(Path.Combine(folder, "chaptarr.db")) && !File.Exists(Path.Combine(folder, "chaptarr.dump"))))
            {
                return;
            }

            if (new FileInfo(path).Length > 64 * 1024)
            {
                throw new InvalidDataException("The upstream backup manifest is too large.");
            }

            UpstreamBackupManifest manifest;
            try
            {
                using var stream = File.OpenRead(path);
                manifest = JsonSerializer.Deserialize<UpstreamBackupManifest>(stream);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("The upstream backup manifest is invalid.", ex);
            }

            if (manifest == null || manifest.FormatVersion is not (1 or 2 or 3) || manifest.Files == null)
            {
                throw new InvalidDataException("The upstream backup manifest format is unsupported.");
            }

            if (manifest.DatabaseType != DatabaseType.SQLite)
            {
                throw new InvalidDataException("Only upstream SQLite backups can be imported here. PostgreSQL dumps require a separate restore.");
            }

            var allowed = new[] { "chaptarr.db", "config.xml", "INFO", "staging.db", "logs.db", "cache.db" };
            var components = Directory.GetFileSystemEntries(folder).Select(Path.GetFileName).Where(name => name != "manifest.json").ToHashSet(StringComparer.Ordinal);
            if (!components.SetEquals(manifest.Files.Keys) || !components.IsSubsetOf(allowed) ||
                !components.Contains("chaptarr.db") || !components.Contains("config.xml"))
            {
                throw new InvalidDataException("The upstream backup components do not match its manifest.");
            }

            foreach (var component in components)
            {
                using var stream = File.OpenRead(Path.Combine(folder, component));
                if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(manifest.Files[component], StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException($"The upstream backup checksum for {component} is invalid.");
                }
            }
        }
    }
}
