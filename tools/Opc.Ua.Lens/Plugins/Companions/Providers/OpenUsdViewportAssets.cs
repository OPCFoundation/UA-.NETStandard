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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using UaLens.Connection;

namespace UaLens.Plugins.Companions.Providers
{
    /// <summary>
    /// One bounded private asset closure. Disposal removes only files created for this lease.
    /// </summary>
    internal interface IOpenUsdViewportAssets : IAsyncDisposable
    {
        /// <summary>
        /// Gets the local composed stage, retained for the whole viewport lifetime.
        /// </summary>
        string StagePath { get; }
    }

    /// <summary>
    /// Captures verified assets once. No frame or telemetry update rewrites this cache.
    /// </summary>
    internal interface IOpenUsdViewportAssetCache
    {
        /// <summary>
        /// Validates and stores one bounded closure after an explicit Start action.
        /// </summary>
        Task<IOpenUsdViewportAssets> AcquireAsync(
            string rootIdentifier, string primPath, ArrayOf<OpenUsdVerifiedAsset> assets,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Reuses the existing atomic new-directory exporter for at most four 32-MiB live closures.
    /// Existing crash leftovers count against that limit and are never silently deleted.
    /// </summary>
    internal sealed class OpenUsdViewportAssetCache : IOpenUsdViewportAssetCache
    {
        /// <summary>
        /// Creates an inert configurable cache; no directory is created during composition or restore.
        /// </summary>
        public OpenUsdViewportAssetCache(string? root = null, IOpenUsdCompanionExporter? exporter = null)
        {
            m_root = root ?? Path.Combine(Path.GetDirectoryName(FavoritesStore.FilePath)!, "OpenUsd");
            m_exporter = exporter ?? new OpenUsdCompanionExporter();
        }

        /// <inheritdoc/>
        public async Task<IOpenUsdViewportAssets> AcquireAsync(
            string rootIdentifier, string primPath, ArrayOf<OpenUsdVerifiedAsset> assets,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            OpenUsdViewportScenePolicy.Validate(rootIdentifier, assets);
            await s_acquisition.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await AcquireCoreAsync(rootIdentifier, primPath, assets, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                s_acquisition.Release();
            }
        }

        private async Task<IOpenUsdViewportAssets> AcquireCoreAsync(
            string rootIdentifier, string primPath, ArrayOf<OpenUsdVerifiedAsset> assets,
            CancellationToken cancellationToken)
        {
            RequireLocalParents(m_root);
            Directory.CreateDirectory(m_root);
            if (Directory.EnumerateFileSystemEntries(m_root).Take(4).Count() >= 4)
            {
                throw new IOException("The OpenUSD cache has four live or retained closures. Close a viewport first.");
            }
            string destination = Path.Combine(m_root, Guid.NewGuid().ToString("N"));
            await m_exporter.WriteAsync(destination, rootIdentifier, primPath, assets, [], cancellationToken)
                .ConfigureAwait(false);
            var files = new List<string>
            {
                Path.Combine(destination, "ualens-stage.usda"),
                Path.Combine(destination, "ualens-snapshot.usda")
            };
            var directories = new HashSet<string>(StringComparer.Ordinal) { destination };
            foreach (OpenUsdVerifiedAsset asset in assets)
            {
                string path = Path.Combine(destination, asset.Identifier.Replace('/', Path.DirectorySeparatorChar));
                files.Add(path);
                for (string? parent = Path.GetDirectoryName(path);
                    parent is not null && parent.Length > destination.Length;
                    parent = Path.GetDirectoryName(parent))
                {
                    directories.Add(parent);
                }
            }
            return new Lease(destination, [.. files], [.. directories.OrderByDescending(static path => path.Length)]);
        }

        private static void RequireLocalParents(string root)
        {
            if (!Path.IsPathFullyQualified(root) || root.StartsWith(@"\\", StringComparison.Ordinal) ||
                root.StartsWith("//", StringComparison.Ordinal))
            {
                throw new ArgumentException("The preview cache must use an absolute local directory.", nameof(root));
            }
            for (DirectoryInfo? parent = new(root); parent is not null; parent = parent.Parent)
            {
                if (parent.Exists && (parent.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("The preview cache cannot traverse linked directories.");
                }
            }
            string? drive = Path.GetPathRoot(root);
            if (drive is not null && new DriveInfo(drive).DriveType == DriveType.Network)
            {
                throw new IOException("The preview cache cannot reside on a network drive.");
            }
        }

        private readonly string m_root;
        private readonly IOpenUsdCompanionExporter m_exporter;
        private static readonly SemaphoreSlim s_acquisition = new(1, 1);

        private sealed class Lease(string root, ArrayOf<string> files, ArrayOf<string> directories)
            : IOpenUsdViewportAssets
        {
            public string StagePath { get; } = Path.Combine(root, "ualens-stage.usda");

            public ValueTask DisposeAsync()
            {
                if (Interlocked.Exchange(ref m_disposed, 1) != 0)
                {
                    return ValueTask.CompletedTask;
                }
                foreach (string path in files)
                {
                    File.Delete(path);
                }
                foreach (string directory in directories)
                {
                    Directory.Delete(directory, recursive: false);
                }
                return ValueTask.CompletedTask;
            }

            private int m_disposed;
        }
    }
}
