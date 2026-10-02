/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;
using UaLens.Storage;

namespace UaLens.Themes
{
    /// <summary>
    /// Stores appearance preferences at the existing per-user location.
    /// A different path can be supplied by an isolated workspace or a test.
    /// </summary>
    internal sealed class AppearancePreferences
    {
        public AppearancePreferences()
            : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "UaLens", "theme.json"))
        {
        }

        public AppearancePreferences(string path)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            m_path = Path.GetFullPath(path);
        }

        public async Task<ThemePreset> LoadAsync(CancellationToken cancellationToken = default)
        {
            AppearanceSnapshot snapshot;
            try
            {
                snapshot = await JsonFileStore.ReadAsync(
                    m_path, AppearanceJsonContext.Default.AppearanceSnapshot, cancellationToken).ConfigureAwait(false);
            }
            catch (FileNotFoundException)
            {
                return ThemePreset.System;
            }
            catch (DirectoryNotFoundException)
            {
                return ThemePreset.System;
            }
            if (!Enum.TryParse(snapshot.Theme, ignoreCase: true, out ThemePreset preset) || !Enum.IsDefined(preset))
            {
                throw new JsonException($"Unknown appearance preference '{snapshot.Theme}'.");
            }
            return preset;
        }

        public Task SaveAsync(ThemePreset preset, CancellationToken cancellationToken = default)
        {
            if (!Enum.IsDefined(preset))
            {
                throw new ArgumentOutOfRangeException(nameof(preset));
            }
            return JsonFileStore.WriteAsync(
                m_path, new AppearanceSnapshot { Theme = preset.ToString() },
                AppearanceJsonContext.Default.AppearanceSnapshot, cancellationToken);
        }

        private readonly string m_path;
    }

    internal sealed class AppearanceSnapshot
    {
        public string Theme { get; set; } = nameof(ThemePreset.System);
    }

    [JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
    [JsonSerializable(typeof(AppearanceSnapshot))]
    internal sealed partial class AppearanceJsonContext : JsonSerializerContext;
}
