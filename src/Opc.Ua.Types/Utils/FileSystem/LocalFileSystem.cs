/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.IO;

namespace Opc.Ua
{
    /// <summary>
    /// Default file system implementation that uses System.IO
    /// </summary>
    public class LocalFileSystem : IFileSystem
    {
        /// <summary>
        /// Get singleton instance
        /// </summary>
        public static readonly LocalFileSystem Instance = new();

        /// <inheritdoc/>
        public bool Exists(string path, bool isDirectory)
        {
            return isDirectory ? Directory.Exists(path) : File.Exists(path);
        }

        /// <inheritdoc/>
        public void Delete(string path, bool isDirectory)
        {
            if (isDirectory)
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
            }
            else if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        /// <inheritdoc/>
        public Stream OpenWrite(string path)
        {
            string? directoryName = Path.GetDirectoryName(path);
            if (directoryName != null && !Directory.Exists(directoryName))
            {
                Directory.CreateDirectory(directoryName);
            }
            return File.Open(path, FileMode.Create, FileAccess.ReadWrite);
        }

        /// <inheritdoc/>
        public Stream OpenRead(string path)
        {
            // FileMode.Open alone requests ReadWrite access with FileShare.None,
            // which fails on a read-only file and locks out concurrent readers.
            return File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        /// <inheritdoc/>
        public DateTime GetLastWriteTime(string path)
        {
            return new FileInfo(path).LastWriteTimeUtc;
        }

        /// <inheritdoc/>
        public long GetLength(string path)
        {
            return new FileInfo(path).Length;
        }

        /// <inheritdoc/>
        public void Replace(string sourcePath, string destinationPath)
        {
            string? directoryName = Path.GetDirectoryName(destinationPath);
            if (directoryName != null && !Directory.Exists(directoryName))
            {
                Directory.CreateDirectory(directoryName);
            }

#if NETCOREAPP3_0_OR_GREATER
            File.Move(sourcePath, destinationPath, overwrite: true);
#else
            if (File.Exists(destinationPath))
            {
                // Replace keeps the destination's identity and discards the source, but
                // it refuses to run when the two paths sit on different volumes.
                File.Replace(sourcePath, destinationPath, null);
                return;
            }

            File.Move(sourcePath, destinationPath);
#endif
        }
    }
}
