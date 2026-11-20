// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.IO;

namespace Opc.Ua
{
    /// <summary>
    /// Provides a file system implementation that combines two underlying
    /// file systems, allowing read operations to fall back to a secondary
    /// source if the primary does not contain the requested file or directory.
    /// </summary>
    public class CombinedFileSystem : IFileSystem
    {
        /// <summary>
        /// Create a combined file system
        /// </summary>
        /// <param name="primary">First file system</param>
        /// <param name="secondary">Fallback file system</param>
        /// <param name="usePrimaryForWrite">Use first file system
        /// to perform write operations. Default is secondary.</param>
        public CombinedFileSystem(
            IFileSystem primary,
            IFileSystem secondary,
            bool usePrimaryForWrite = false)
        {
            m_primary = primary;
            m_secondary = secondary;
            m_usePrimaryForWrite = usePrimaryForWrite;
        }

        /// <inheritdoc/>
        public bool Exists(string path, bool isDirectory = false)
        {
            return
                m_primary.Exists(path, isDirectory) ||
                m_secondary.Exists(path, isDirectory);
        }

        /// <inheritdoc/>
        public long GetLength(string path)
        {
            try
            {
                if (m_primary.Exists(path))
                {
                    return m_primary.GetLength(path);
                }
            }
            catch
            {
                // Nothing found
            }
            try
            {
                if (m_secondary.Exists(path))
                {
                    return m_secondary.GetLength(path);
                }
            }
            catch
            {
                // Nothing found
            }
            return 0;
        }

        /// <inheritdoc/>
        public Stream OpenRead(string path)
        {
            try
            {
                if (m_primary.Exists(path))
                {
                    return m_primary.OpenRead(path);
                }
            }
            catch
            {
                // Fallback to secondary
            }
            return m_secondary.OpenRead(path);
        }

        /// <inheritdoc/>
        public void Delete(string path, bool isDirectory = false)
        {
            IFileSystem writeableFs = m_usePrimaryForWrite ? m_primary : m_secondary;
            writeableFs.Delete(path, isDirectory);
        }

        /// <inheritdoc/>
        public Stream OpenWrite(string path)
        {
            IFileSystem writeableFs = m_usePrimaryForWrite ? m_primary : m_secondary;
            return writeableFs.OpenWrite(path);
        }

        /// <inheritdoc/>
        public void Replace(string sourcePath, string destinationPath)
        {
            IFileSystem writeableFs = m_usePrimaryForWrite ? m_primary : m_secondary;
            writeableFs.Replace(sourcePath, destinationPath);
        }

        /// <inheritdoc/>
        public DateTime GetLastWriteTime(string path)
        {
            IFileSystem writeableFs = m_usePrimaryForWrite ? m_primary : m_secondary;
            return writeableFs.GetLastWriteTime(path);
        }

        private readonly IFileSystem m_primary;
        private readonly IFileSystem m_secondary;
        private readonly bool m_usePrimaryForWrite;
    }
}
