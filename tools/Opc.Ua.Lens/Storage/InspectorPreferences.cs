// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using UaLens.ViewModels;

namespace UaLens.Storage;

/// <summary>
/// Stores the per-user visibility of the contextual inspectors.
/// </summary>
internal sealed class InspectorPreferences
{
    public InspectorPreferences()
        : this(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "UaLens", "inspectors.json"))
    {
    }

    public InspectorPreferences(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        m_path = Path.GetFullPath(path);
    }

    public async Task<InspectorSettings> LoadSettingsAsync(CancellationToken cancellationToken = default)
    {
        InspectorSnapshot snapshot;
        try
        {
            snapshot = await JsonFileStore.ReadAsync(
                m_path, InspectorJsonContext.Default.InspectorSnapshot, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return InspectorSettings.Default;
        }
        catch (DirectoryNotFoundException)
        {
            return InspectorSettings.Default;
        }
        if (!Enum.TryParse(snapshot.Mode, ignoreCase: true, out SidePanelMode mode) || !Enum.IsDefined(mode))
        {
            throw new JsonException($"Unknown inspector preference '{snapshot.Mode}'.");
        }
        if (!Enum.TryParse(
            snapshot.ReferenceDirection,
            ignoreCase: true,
            out ReferenceDirectionFilter referenceDirection) ||
            !Enum.IsDefined(referenceDirection))
        {
            throw new JsonException($"Unknown reference direction preference '{snapshot.ReferenceDirection}'.");
        }
        return new InspectorSettings(mode, referenceDirection);
    }

    public async Task<SidePanelMode> LoadAsync(CancellationToken cancellationToken = default)
    {
        InspectorSettings settings = await LoadSettingsAsync(cancellationToken).ConfigureAwait(false);
        return settings.Mode;
    }

    public Task SaveSettingsAsync(InspectorSettings settings, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(settings.Mode))
        {
            throw new ArgumentOutOfRangeException(nameof(settings));
        }
        if (!Enum.IsDefined(settings.ReferenceDirection))
        {
            throw new ArgumentOutOfRangeException(nameof(settings));
        }
        return JsonFileStore.WriteAsync(
            m_path,
            new InspectorSnapshot
            {
                Mode = settings.Mode.ToString(),
                ReferenceDirection = settings.ReferenceDirection.ToString()
            },
            InspectorJsonContext.Default.InspectorSnapshot, cancellationToken);
    }

    public Task SaveAsync(SidePanelMode mode, CancellationToken cancellationToken = default)
    {
        return SaveSettingsAsync(
            new InspectorSettings(mode, ReferenceDirectionFilter.Both),
            cancellationToken);
    }

    private readonly string m_path;
}

internal sealed record InspectorSettings(SidePanelMode Mode, ReferenceDirectionFilter ReferenceDirection)
{
    public static InspectorSettings Default { get; } =
        new(SidePanelMode.AttrsAndRefs, ReferenceDirectionFilter.Both);
}

internal sealed class InspectorSnapshot
{
    public string Mode { get; set; } = nameof(SidePanelMode.AttrsAndRefs);

    public string ReferenceDirection { get; set; } = nameof(ReferenceDirectionFilter.Both);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(InspectorSnapshot))]
internal sealed partial class InspectorJsonContext : JsonSerializerContext;
