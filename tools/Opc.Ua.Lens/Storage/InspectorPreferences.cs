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

    public async Task<SidePanelMode> LoadAsync(CancellationToken cancellationToken = default)
    {
        InspectorSnapshot snapshot;
        try
        {
            snapshot = await JsonFileStore.ReadAsync(
                m_path, InspectorJsonContext.Default.InspectorSnapshot, cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return SidePanelMode.AttrsAndRefs;
        }
        catch (DirectoryNotFoundException)
        {
            return SidePanelMode.AttrsAndRefs;
        }
        if (!Enum.TryParse(snapshot.Mode, ignoreCase: true, out SidePanelMode mode) || !Enum.IsDefined(mode))
        {
            throw new JsonException($"Unknown inspector preference '{snapshot.Mode}'.");
        }
        return mode;
    }

    public Task SaveAsync(SidePanelMode mode, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(mode))
        {
            throw new ArgumentOutOfRangeException(nameof(mode));
        }
        return JsonFileStore.WriteAsync(
            m_path, new InspectorSnapshot { Mode = mode.ToString() },
            InspectorJsonContext.Default.InspectorSnapshot, cancellationToken);
    }

    private readonly string m_path;
}

internal sealed class InspectorSnapshot
{
    public string Mode { get; set; } = nameof(SidePanelMode.AttrsAndRefs);
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(InspectorSnapshot))]
internal sealed partial class InspectorJsonContext : JsonSerializerContext;
