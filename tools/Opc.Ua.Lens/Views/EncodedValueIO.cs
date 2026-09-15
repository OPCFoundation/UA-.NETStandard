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
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using UaLens.Connection;

namespace UaLens.Views;

/// <summary>
/// Shared file-picker + encoding-picker plumbing used by the Write
/// Value, Method Call and Address-space export flows.
/// </summary>
internal static class EncodedValueIO
{
    private static readonly string[] s_loadPatterns = ["*.bin", "*.uabin", "*.xml", "*.json"];
    private static readonly string[] s_allPatterns = ["*.*"];

    /// <summary>
    /// Runs the open-file picker, then the encoding picker (defaulted
    /// from the file extension), and returns the file bytes plus the
    /// chosen <see cref="EncodingFormat"/>.  Returns an empty byte
    /// array when the user cancels at any step.
    /// </summary>
    public static async Task<(byte[] Bytes, EncodingFormat Format, string FileName)> LoadAsync(Window owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var storage = owner.StorageProvider;
        var opts = new FilePickerOpenOptions
        {
            Title = "Load OPC UA value",
            AllowMultiple = false,
            FileTypeFilter = new List<FilePickerFileType>
            {
                new("OPC UA value (*.bin, *.xml, *.json)") { Patterns = s_loadPatterns },
                new("All files") { Patterns = s_allPatterns }
            }
        };
        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(opts).ConfigureAwait(true);
        if (files.Count == 0)
        {
            return (Array.Empty<byte>(), EncodingFormat.Json, string.Empty);
        }
        IStorageFile file = files[0];
        EncodingFormat? guess = DataValueCodec.GuessFromExtension(file.Name);
        var picker = new EncodingPickerDialog(guess);
        EncodingFormat? picked = await picker.ShowDialog<EncodingFormat?>(owner).ConfigureAwait(true);
        if (!picked.HasValue)
        {
            return (Array.Empty<byte>(), EncodingFormat.Json, string.Empty);
        }

        Stream src = await file.OpenReadAsync().ConfigureAwait(true);
        using var ms = new MemoryStream();
        await using (src.ConfigureAwait(false))
        {
            await src.CopyToAsync(ms).ConfigureAwait(false);
        }
        return (ms.ToArray(), picked.Value, file.Name);
    }

    /// <summary>
    /// Runs the encoding picker then the save-file picker, defaulting
    /// the suggested filename / extension from the chosen format.
    /// Returns the chosen file + format, or (null, _) when cancelled.
    /// </summary>
    public static async Task<(IStorageFile? File, EncodingFormat Format)> SaveAsync(
        Window owner, string suggestedNameWithoutExt)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var picker = new EncodingPickerDialog();
        EncodingFormat? fmt = await picker.ShowDialog<EncodingFormat?>(owner).ConfigureAwait(true);
        if (!fmt.HasValue)
        {
            return (null, EncodingFormat.Json);
        }
        string ext = DataValueCodec.DefaultExtension(fmt.Value);
        string[] patterns = [$"*.{ext}"];
        var opts = new FilePickerSaveOptions
        {
            Title = "Export OPC UA value",
            SuggestedFileName = $"{suggestedNameWithoutExt}.{ext}",
            DefaultExtension = ext,
            FileTypeChoices = new List<FilePickerFileType>
            {
                new($"OPC UA {fmt} (*.{ext})") { Patterns = patterns }
            }
        };
        IStorageFile? file = await owner.StorageProvider.SaveFilePickerAsync(opts).ConfigureAwait(true);
        return (file, fmt.Value);
    }
}
