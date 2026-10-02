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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.OpenUsd.Client;

namespace UaLens.Plugins.Companions.Providers;

/// <summary>
/// Explicit local-output seam. Only already-verified, bounded bytes cross this boundary.
/// </summary>
internal interface IOpenUsdCompanionExporter
{
    Task WriteAsync(
        string destination,
        string rootLayerIdentifier,
        string primPath,
        ArrayOf<OpenUsdVerifiedAsset> assets,
        ArrayOf<OpenUsdCompanionBindingValue> snapshot,
        CancellationToken cancellationToken);
}

/// <summary>
/// Authors a new local export using the existing USD file sink, without loading a stage or resolving dependencies.
/// </summary>
internal sealed class OpenUsdCompanionExporter : IOpenUsdCompanionExporter
{
    public async Task WriteAsync(
        string destination,
        string rootLayerIdentifier,
        string primPath,
        ArrayOf<OpenUsdVerifiedAsset> assets,
        ArrayOf<OpenUsdCompanionBindingValue> snapshot,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string root = ValidateDestination(destination);
        ValidateAssetIdentifier(rootLayerIdentifier);
        ValidatePrimPath(primPath);
        CellCompanionSupport.CheckCount(assets.Count, 32, "Export assets");
        CellCompanionSupport.CheckCount(snapshot.Count, 1024, "Export values");
        var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        for (int index = 0; index < assets.Count; index++)
        {
            ValidateAssetIdentifier(assets[index].Identifier);
            totalBytes += assets[index].Content.Length;
            CellCompanionSupport.CheckCount(
                assets[index].Content.Length, OpenUsdCompanionProvider.MaxAssetBytes, "Asset bytes");
            if (!identifiers.Add(assets[index].Identifier))
            {
                throw new IOException("Asset identifiers collide in the local export.");
            }
        }
        if (totalBytes > OpenUsdCompanionProvider.MaxTotalAssetBytes || !identifiers.Contains(rootLayerIdentifier))
        {
            throw new IOException("The export exceeds its total byte limit or is missing its root asset.");
        }
        for (int index = 0; index < snapshot.Count; index++)
        {
            ValidatePrimPath(snapshot[index].PrimPath);
            ValidatePropertyName(snapshot[index].PropertyName);
        }

        var written = new List<string>();
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool completed = false;
        try
        {
            Directory.CreateDirectory(root);
            directories.Add(root);
            for (int index = 0; index < assets.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                OpenUsdVerifiedAsset asset = assets[index];
                string path = Path.Combine(root, asset.Identifier.Replace('/', Path.DirectorySeparatorChar));
                string parent = Path.GetDirectoryName(path)!;
                EnsureDirectories(root, parent, directories);
                await WriteNewFileAsync(path, asset.Content, written, cancellationToken).ConfigureAwait(false);
            }
            string snapshotPath = Path.Combine(root, SnapshotFileName);
            await WriteNewFileAsync(snapshotPath, ByteString.Empty, written, cancellationToken).ConfigureAwait(false);
            var sink = new UsdFileSink(snapshotPath);
            using (sink.BeginBatch())
            {
                sink.ComposePrim(primPath, OpenUsdCompositionArc.Child, null, true);
                for (int index = 0; index < snapshot.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    OpenUsdCompanionBindingValue value = snapshot[index];
                    sink.SetAttribute(value.PrimPath, value.PropertyName, value.Value);
                }
            }
            string stage = "#usda 1.0\n(\n    subLayers = [\n" +
                $"        @./{SnapshotFileName}@,\n        @./{rootLayerIdentifier}@\n    ]\n)\n";
            await WriteNewFileAsync(
                Path.Combine(root, StageFileName),
                new ByteString(Encoding.UTF8.GetBytes(stage)),
                written,
                cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            completed = true;
        }
        finally
        {
            if (!completed)
            {
                // Delete only files created by this operation, never a pre-existing directory tree.
                for (int index = written.Count - 1; index >= 0; index--)
                {
                    File.Delete(written[index]);
                }
                foreach (string directory in directories.OrderByDescending(static path => path.Length))
                {
                    if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                    {
                        Directory.Delete(directory);
                    }
                }
            }
        }
    }

    internal static string ValidateDestination(string? input)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 240 || !Path.IsPathFullyQualified(input) ||
            input.StartsWith(@"\\", StringComparison.Ordinal) || input.StartsWith("//", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "An absolute local directory path is required; UNC and device paths are refused.", nameof(input));
        }
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(input));
        if (Path.GetFileName(full).IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException("The destination contains an invalid directory name.", nameof(input));
        }
        string? parent = Path.GetDirectoryName(full);
        if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent))
        {
            throw new ArgumentException("The export needs an existing local parent directory.", nameof(input));
        }
        if (Directory.Exists(full) || File.Exists(full))
        {
            throw new IOException("Choose a new export directory; existing paths are never overwritten.");
        }
        ValidateParents(parent);
        string? driveRoot = Path.GetPathRoot(full);
        if (!string.IsNullOrEmpty(driveRoot) && new DriveInfo(driveRoot).DriveType == DriveType.Network)
        {
            throw new ArgumentException("An export cannot target a network drive.", nameof(input));
        }
        return full;
    }

    internal static void ValidateAssetIdentifier(string? identifier)
    {
        if (string.IsNullOrEmpty(identifier) || identifier.Length > 200 ||
            identifier.StartsWith('/') ||
            identifier.Contains('\\', StringComparison.Ordinal))
        {
            throw new ServiceResultException(
                StatusCodes.BadSecurityChecksFailed, "Asset identifiers must be relative paths.");
        }
        string[] segments = identifier.Split('/');
        foreach (string segment in segments)
        {
            if (string.IsNullOrEmpty(segment) || segment is "." or ".." ||
                segment.EndsWith('.') ||
                string.Equals(segment, SnapshotFileName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(segment, StageFileName, StringComparison.OrdinalIgnoreCase))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "An asset path is not safe to export.");
            }
            foreach (char character in segment)
            {
                if (!char.IsAsciiLetterOrDigit(character) && character is not ('_' or '-' or '.'))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadSecurityChecksFailed,
                        "An asset identifier contains a URI, escape or unsafe character.");
                }
            }
            string device = segment.Split('.')[0];
            if (device.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
                device.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                device.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
                device.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                (device.Length == 4 && device[3] is >= '0' and <= '9' &&
                    (device.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                        device.StartsWith("LPT", StringComparison.OrdinalIgnoreCase))))
            {
                throw new ServiceResultException(
                    StatusCodes.BadSecurityChecksFailed, "An asset identifier is a device name.");
            }
        }
    }

    internal static void ValidatePrimPath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 512 || !path.StartsWith('/'))
        {
            throw new ServiceResultException(
                StatusCodes.BadTypeMismatch, "A canonical absolute USD prim path is required.");
        }
        foreach (string segment in path[1..].Split('/'))
        {
            ValidateIdentifier(segment);
        }
    }

    internal static void ValidatePropertyName(string? name)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 128)
        {
            throw new ServiceResultException(StatusCodes.BadTypeMismatch, "A valid USD property name is required.");
        }
        foreach (string segment in name.Split(':'))
        {
            ValidateIdentifier(segment);
        }
    }

    private static void ValidateIdentifier(string value)
    {
        if (string.IsNullOrEmpty(value) || (!char.IsAsciiLetter(value[0]) && value[0] != '_'))
        {
            throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Invalid USD identifier.");
        }
        foreach (char character in value)
        {
            if (!char.IsAsciiLetterOrDigit(character) && character != '_')
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch, "Invalid USD identifier.");
            }
        }
    }

    private static void ValidateParents(string parent)
    {
        for (DirectoryInfo? directory = new(parent); directory is not null; directory = directory.Parent)
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new IOException("An export cannot traverse a symbolic link, junction or other reparse point.");
            }
        }
    }

    private static void EnsureDirectories(string root, string parent, HashSet<string> created)
    {
        if (!string.Equals(parent, root, StringComparison.OrdinalIgnoreCase) &&
            !parent.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException("The export path escapes its selected directory.");
        }
        if (!Directory.Exists(parent))
        {
            EnsureDirectories(root, Path.GetDirectoryName(parent)!, created);
            Directory.CreateDirectory(parent);
            created.Add(parent);
        }
        ValidateParents(parent);
    }

    private static async Task WriteNewFileAsync(
        string path,
        ByteString content,
        List<string> written,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, useAsync: true);
        await using (stream.ConfigureAwait(false))
        {
            written.Add(path);
            await stream.WriteAsync(content.Memory, cancellationToken).ConfigureAwait(false);
        }
    }

    private const string SnapshotFileName = "ualens-snapshot.usda";
    private const string StageFileName = "ualens-stage.usda";
}
