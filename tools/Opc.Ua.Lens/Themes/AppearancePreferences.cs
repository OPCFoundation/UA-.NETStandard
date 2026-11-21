// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
