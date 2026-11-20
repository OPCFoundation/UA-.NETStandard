// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.IO;

namespace Opc.Ua
{
    /// <summary>
    /// A deny-all file system that never resolves any path. It is used when a
    /// component must load resources exclusively from an in-memory table and
    /// must never touch the local file system - for example when validating a
    /// type dictionary received from an untrusted network peer, where an
    /// attacker-supplied import location (such as a UNC path) would otherwise
    /// trigger an outbound SMB connection or arbitrary local file access.
    /// </summary>
    public sealed class NullFileSystem : IFileSystem
    {
        /// <summary>
        /// Get singleton instance
        /// </summary>
        public static readonly NullFileSystem Instance = new();

        private NullFileSystem()
        {
        }

        /// <inheritdoc/>
        public bool Exists(string path, bool isDirectory)
        {
            return false;
        }

        /// <inheritdoc/>
        public void Delete(string path, bool isDirectory)
        {
            throw new FileNotFoundException(
                "The null file system does not provide access to any path.", path);
        }

        /// <inheritdoc/>
        public Stream OpenRead(string path)
        {
            throw new FileNotFoundException(
                "The null file system does not provide access to any path.", path);
        }

        /// <inheritdoc/>
        public Stream OpenWrite(string path)
        {
            throw new FileNotFoundException(
                "The null file system does not provide access to any path.", path);
        }

        /// <inheritdoc/>
        public void Replace(string sourcePath, string destinationPath)
        {
            throw new FileNotFoundException(
                "The null file system does not provide access to any path.", sourcePath);
        }

        /// <inheritdoc/>
        public System.DateTime GetLastWriteTime(string path)
        {
            throw new FileNotFoundException(
                "The null file system does not provide access to any path.", path);
        }

        /// <inheritdoc/>
        public long GetLength(string path)
        {
            throw new FileNotFoundException(
                "The null file system does not provide access to any path.", path);
        }
    }
}
