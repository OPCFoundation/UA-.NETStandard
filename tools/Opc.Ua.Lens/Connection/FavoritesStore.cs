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
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using UaLens.Storage;

namespace UaLens.Connection
{
    /// <summary>
    /// Sibling helper to <see cref="SessionFile"/> that persists the user's
    /// list of saved endpoint URLs (the "Custom Discovery favourites" shown
    /// under the GDS Discovery tree).  Mirrors
    /// <c>Samples/ClientControls.Net4/Endpoints/ConfiguredServerListDlg.cs</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="SessionFile"/> models a self-contained, per-document
    /// snapshot driven by explicit File → Save Session / Load Session paths
    /// chosen by the user, so it would be wrong to lump global, app-wide
    /// favourites into it.  Instead this class writes a separate
    /// <c>favorites.json</c> in a stable per-user location
    /// (<see cref="Environment.SpecialFolder.LocalApplicationData"/> +
    /// <c>UaLens</c>). Missing files represent an empty list; malformed or
    /// inaccessible files are reported to the caller and never overwritten on load.
    /// </para>
    /// <para>
    /// The file is intended for single-user, single-process access.  No
    /// cross-process locking is performed; concurrent saves from multiple
    /// UaLens instances would race and the last writer wins.  Schema
    /// versioning is captured via the <see cref="FavoritesDocument.Version"/>
    /// field on the document so future migrations can branch on it without
    /// breaking existing files.
    /// </para>
    /// </remarks>
    internal static class FavoritesStore
    {
        /// <summary>
        /// Current schema version stamped into <c>favorites.json</c>.
        /// </summary>
        public const string CurrentVersion = "1";

        /// <summary>
        /// Folder name under <c>%LocalAppData%</c> that hosts UaLens state.
        /// </summary>
        private const string AppFolderName = "UaLens";

        /// <summary>
        /// File name of the favourites store.
        /// </summary>
        private const string FileName = "favorites.json";

        /// <summary>
        /// Returns the absolute path to the favourites JSON file.  Always
        /// resolves the same path for the current user; the parent
        /// directory is created on save when needed.
        /// </summary>
        public static string FilePath
        {
            get
            {
                string baseDir = Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);
                return Path.Combine(baseDir, AppFolderName, FileName);
            }
        }

        /// <summary>
        /// Loads favorite endpoints without hiding malformed data or access failures.
        /// Equivalent URLs are deduplicated while preserving case-sensitive endpoint paths.
        /// </summary>
        /// <exception cref="JsonException"></exception>
        public static async Task<List<string>> LoadAsync(
            ILogger? log = null,
            string? path = null,
            CancellationToken cancellationToken = default)
        {
            _ = log;
            path ??= FilePath;
            FavoritesDocument doc;
            try
            {
                doc = await JsonFileStore.ReadAsync(
                    path, FavoritesJsonContext.Default.FavoritesDocument, cancellationToken).ConfigureAwait(false);
            }
            catch (FileNotFoundException)
            {
                return [];
            }
            catch (DirectoryNotFoundException)
            {
                return [];
            }
            if (doc.Version != CurrentVersion || doc.FavouriteEndpoints is null)
            {
                throw new JsonException("The favorites document has an unsupported version or missing endpoint list.");
            }
            return Normalize(doc.FavouriteEndpoints);
        }

        /// <summary>
        /// Persists the supplied list of favourite endpoint URLs.  Creates
        /// the parent directory if it does not exist.  Writes atomically by
        /// emitting to a sibling <c>.tmp</c> file and then replacing the
        /// target.
        /// </summary>
        public static Task SaveAsync(
            IEnumerable<string> urls,
            ILogger? log = null,
            string? path = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(urls);
            _ = log;
            return JsonFileStore.WriteAsync(path ?? FilePath, new FavoritesDocument
            {
                FavouriteEndpoints = Normalize(urls)
            }, FavoritesJsonContext.Default.FavoritesDocument, cancellationToken);
        }

        private static List<string> Normalize(IEnumerable<string> urls)
        {
            var result = new List<string>();
            foreach (string value in urls)
            {
                string url = value?.Trim() ?? string.Empty;
                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? parsed) ||
                    string.IsNullOrEmpty(parsed.Host) ||
                    !string.IsNullOrEmpty(parsed.UserInfo) ||
                    !string.IsNullOrEmpty(parsed.Fragment))
                {
                    throw new JsonException("Favorites require absolute endpoint URLs without embedded credentials.");
                }
                if (!result.Exists(existing => ConnectionProfile.EndpointUrlsMatch(existing, url)))
                {
                    result.Add(url);
                }
            }
            return result;
        }

        /// <summary>
        /// JSON-serializable shape for <c>favorites.json</c>.  Versioned so
        /// that future schema additions can be migrated without breaking
        /// previously-saved files.
        /// </summary>
        internal sealed class FavoritesDocument
        {
            public string Version { get; set; } = CurrentVersion;
            public List<string> FavouriteEndpoints { get; set; } = [];
        }
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
    [JsonSerializable(typeof(FavoritesStore.FavoritesDocument))]
    internal sealed partial class FavoritesJsonContext : JsonSerializerContext;
}
