// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.IO;

namespace Opc.Ua
{
    /// <summary>
    /// The names, sizes and last write times of every file below a directory
    /// certificate store, used to tell whether the store changed since an
    /// application private key was loaded from it.
    /// </summary>
    internal sealed class CertificateStoreFileSnapshot : IEquatable<CertificateStoreFileSnapshot>
    {
        private CertificateStoreFileSnapshot(FileState[] files)
        {
            m_files = files;
        }

        /// <summary>
        /// Captures the files below <paramref name="directory"/>, or returns
        /// <see langword="null"/> when the directory does not exist or cannot
        /// be listed.
        /// </summary>
        public static CertificateStoreFileSnapshot? TryCapture(DirectoryInfo directory)
        {
            try
            {
                directory.Refresh();
                if (!directory.Exists)
                {
                    return null;
                }

                var files = new List<FileState>();
                foreach (FileInfo file in directory.EnumerateFiles("*", SearchOption.AllDirectories))
                {
                    files.Add(new FileState(
                        file.FullName,
                        file.Length,
                        file.LastWriteTimeUtc.Ticks));
                }

                FileState[] sorted = [.. files];
                Array.Sort(sorted, static (a, b) => string.CompareOrdinal(a.Path, b.Path));
                return new CertificateStoreFileSnapshot(sorted);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {
                return null;
            }
        }

        /// <inheritdoc/>
        public bool Equals(CertificateStoreFileSnapshot? other)
        {
            if (other is null)
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            if (m_files.Length != other.m_files.Length)
            {
                return false;
            }

            for (int i = 0; i < m_files.Length; i++)
            {
                if (!m_files[i].Equals(other.m_files[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <inheritdoc/>
        public override bool Equals(object? obj)
        {
            return Equals(obj as CertificateStoreFileSnapshot);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return m_files.Length;
        }

        private readonly FileState[] m_files;

        private readonly record struct FileState(string Path, long Length, long LastWriteTicks);
    }
}
