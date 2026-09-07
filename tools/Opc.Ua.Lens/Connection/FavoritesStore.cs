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
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace UaLens.Connection;

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
/// <c>UaLens</c>) and is intentionally defensive — any I/O or parse
/// failure is logged and surfaces as an empty list so the GDS Discovery
/// plug-in can still come up cleanly.
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
    /// <summary>Current schema version stamped into <c>favorites.json</c>.</summary>
    public const string CurrentVersion = "1";

    /// <summary>Folder name under <c>%LocalAppData%</c> that hosts UaLens state.</summary>
    private const string AppFolderName = "UaLens";

    /// <summary>File name of the favourites store.</summary>
    private const string FileName = "favorites.json";

    private static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

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
    /// Loads the favourite endpoint URLs.  Missing / unreadable /
    /// malformed files are treated as "no favourites yet" and never
    /// throw — instead they are logged at Debug.  Duplicate entries are
    /// dropped (case-insensitive) and empty strings filtered out.
    /// </summary>
    public static async Task<List<string>> LoadAsync(ILogger? log = null)
    {
        string path = FilePath;
        if (!File.Exists(path))
        {
            return new List<string>();
        }
        try
        {
            FavoritesDocument? doc;
            FileStream fs = File.OpenRead(path);
            await using (fs.ConfigureAwait(false))
            {
                doc = await JsonSerializer
                    .DeserializeAsync<FavoritesDocument>(fs, s_json)
                    .ConfigureAwait(false);
            }
            if (doc?.FavouriteEndpoints is not { } urls)
            {
                return new List<string>();
            }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>(urls.Count);
            foreach (string u in urls)
            {
                if (!string.IsNullOrWhiteSpace(u) && seen.Add(u))
                {
                    result.Add(u);
                }
            }
            return result;
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or JsonException
            or NotSupportedException)
        {
            log?.LogDebug(ex, "FavoritesStore: load failed for {Path}.", path);
            return new List<string>();
        }
    }

    /// <summary>
    /// Persists the supplied list of favourite endpoint URLs.  Creates
    /// the parent directory if it does not exist.  Writes atomically by
    /// emitting to a sibling <c>.tmp</c> file and then replacing the
    /// target.
    /// </summary>
    public static async Task SaveAsync(IEnumerable<string> urls, ILogger? log = null)
    {
        ArgumentNullException.ThrowIfNull(urls);
        string path = FilePath;
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }
            var doc = new FavoritesDocument
            {
                Version = CurrentVersion,
                FavouriteEndpoints = new List<string>(urls)
            };
            string tmp = path + ".tmp";
            FileStream fs = File.Create(tmp);
            await using (fs.ConfigureAwait(false))
            {
                await JsonSerializer.SerializeAsync(fs, doc, s_json)
                    .ConfigureAwait(false);
            }
            // Replace existing file atomically where possible.
            if (File.Exists(path))
            {
                File.Replace(tmp, path, destinationBackupFileName: null);
            }
            else
            {
                File.Move(tmp, path);
            }
        }
        catch (Exception ex) when (ex is IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            log?.LogDebug(ex, "FavoritesStore: save failed for {Path}.", path);
        }
    }

    /// <summary>
    /// JSON-serializable shape for <c>favorites.json</c>.  Versioned so
    /// that future schema additions can be migrated without breaking
    /// previously-saved files.
    /// </summary>
    internal sealed class FavoritesDocument
    {
        public string Version { get; set; } = CurrentVersion;
        public List<string> FavouriteEndpoints { get; set; } = new();
    }
}
