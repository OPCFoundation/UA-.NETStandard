// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Opc.Ua.Gds.Server.Database.Linq
{
    /// <summary>
    /// A GDS database with JSON storage.
    /// </summary>
    /// <remarks>
    /// This db is good for testing but not for production use.
    /// </remarks>
    public class JsonApplicationsDatabase : LinqApplicationsDatabase
    {
        /// <summary>
        /// Create a JSON database.
        /// </summary>
        public JsonApplicationsDatabase(string fileName)
        {
            FileName = fileName;
        }

        /// <summary>
        /// Load the JSON application database.
        /// </summary>
        /// <remarks>
        /// A missing or empty file yields an empty database. A file that
        /// cannot be read or parsed is reported instead of being replaced by
        /// an empty database on the next save.
        /// </remarks>
        /// <exception cref="ArgumentNullException"><paramref name="fileName"/> is <c>null</c>.</exception>
        /// <exception cref="IOException">The file exists but cannot be read.</exception>
        /// <exception cref="InvalidDataException">The file does not contain a valid database.</exception>
        public static JsonApplicationsDatabase Load(string fileName)
        {
            if (fileName == null)
            {
                throw new ArgumentNullException(nameof(fileName));
            }

            if (!File.Exists(fileName))
            {
                return new JsonApplicationsDatabase(fileName);
            }

            string json = File.ReadAllText(fileName);
            if (string.IsNullOrWhiteSpace(json))
            {
                return new JsonApplicationsDatabase(fileName);
            }

            JsonApplicationsDatabase? db;
            try
            {
                db = JsonSerializer.Deserialize(json, GdsApplicationsDatabaseJsonContext.Default.JsonApplicationsDatabase);
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(
                    $"The GDS applications database '{fileName}' is not valid JSON.", ex);
            }

            if (db == null)
            {
                throw new InvalidDataException(
                    $"The GDS applications database '{fileName}' does not contain a database.");
            }

            db.FileName = fileName;
            lock (db.Lock)
            {
                if (db.AssignServerEndpointIds())
                {
                    // Persist the identifiers of endpoints saved
                    // before endpoints had an identifier.
                    db.Save();
                }
            }
            return db;
        }

        /// <summary>
        /// Save the complete database.
        /// </summary>
        /// <remarks>
        /// The database is written to a temporary file which then replaces
        /// the database file, so a crash during the write does not leave a
        /// truncated database behind. A symbolic link is followed so its
        /// target is updated, the file mode of the database file is kept on
        /// Unix, and a database file that cannot be replaced (e.g. a
        /// single-file bind mount) is written in place instead.
        /// </remarks>
        public override void Save()
        {
            string json = JsonSerializer.Serialize(
                this, GdsApplicationsDatabaseJsonContext.Default.JsonApplicationsDatabase);
            string fileName = ResolveLinkTarget(FileName);
            string tempFileName = fileName + ".tmp";
            File.WriteAllText(tempFileName, json);
            try
            {
                if (File.Exists(fileName))
                {
#if NET8_0_OR_GREATER
                    if (!OperatingSystem.IsWindows())
                    {
                        File.SetUnixFileMode(tempFileName, File.GetUnixFileMode(fileName));
                    }
#endif
                    File.Replace(tempFileName, fileName, null);
                }
                else
                {
                    File.Move(tempFileName, fileName);
                }
            }
            catch (IOException)
            {
                // The database file cannot be replaced by another file (a
                // bind mount, or a handle open without delete sharing):
                // update it in place as a last resort.
                File.WriteAllText(fileName, json);
                File.Delete(tempFileName);
            }
        }

        /// <summary>
        /// Returns the final target of <paramref name="fileName"/> when it is
        /// a symbolic link, so a save replaces the target and not the link.
        /// </summary>
        private static string ResolveLinkTarget(string fileName)
        {
#if NET8_0_OR_GREATER
            try
            {
                FileSystemInfo? target = new FileInfo(fileName).ResolveLinkTarget(returnFinalTarget: true);
                if (target != null)
                {
                    return target.FullName;
                }
            }
            catch (IOException)
            {
                // The database file does not exist yet.
            }
#endif
            return fileName;
        }

        /// <summary>
        /// Get or set the filename.
        /// </summary>
        [JsonIgnore]
        public string FileName { get; private set; }
    }

    [JsonSerializable(typeof(JsonApplicationsDatabase))]
    [JsonSourceGenerationOptions(
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        IncludeFields = false)]
    internal partial class GdsApplicationsDatabaseJsonContext : JsonSerializerContext;
}
