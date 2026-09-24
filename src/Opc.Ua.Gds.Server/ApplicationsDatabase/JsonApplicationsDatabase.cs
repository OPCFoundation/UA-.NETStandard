/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

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
        /// truncated database behind.
        /// </remarks>
        public override void Save()
        {
            string json = JsonSerializer.Serialize(
                this, GdsApplicationsDatabaseJsonContext.Default.JsonApplicationsDatabase);
            string tempFileName = FileName + ".tmp";
            File.WriteAllText(tempFileName, json);
            if (File.Exists(FileName))
            {
                File.Replace(tempFileName, FileName, null);
            }
            else
            {
                File.Move(tempFileName, FileName);
            }
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
