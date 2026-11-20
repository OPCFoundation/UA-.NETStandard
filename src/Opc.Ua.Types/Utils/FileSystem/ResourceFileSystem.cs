// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.IO;
using System.Reflection;

namespace Opc.Ua
{
    /// <summary>
    /// Embedded resources
    /// </summary>
    public class ResourceFileSystem : IFileSystem
    {
        /// <summary>
        /// Create file system for embedded resources. Embedded resource names are
        /// matched ignoring case.
        /// </summary>
        /// <param name="resourceAssembly">The assembly containing embedded files</param>
        /// <param name="resourcePath">An optional path in the assembly that should
        /// be used. If not specified, file will be matched only by name</param>
        public ResourceFileSystem(Assembly resourceAssembly, string? resourcePath = null)
        {
            m_resourceAssembly = resourceAssembly;
            m_resourcePath = resourcePath;
        }

        /// <inheritdoc/>
        public bool Exists(string path, bool isDirectory = false)
        {
            string fileName = Path.GetFileName(path);
            if (!string.IsNullOrEmpty(m_resourcePath))
            {
                fileName = m_resourcePath + "." + fileName;
            }
            foreach (string name in m_resourceAssembly.GetManifestResourceNames())
            {
                if (m_resourcePath != null)
                {
                    // match exactly when a resource path is specified
                    if (name.Equals(fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                    continue;
                }
                if (name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <inheritdoc/>
        public long GetLength(string path)
        {
            try
            {
                using Stream resource = OpenRead(path);
                return resource.Length;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <inheritdoc/>
        public Stream OpenRead(string path)
        {
            string fileName = Path.GetFileName(path);
            if (m_resourcePath != null)
            {
                // If we have a resource path, do exact matching when opening
                if (m_resourcePath.Length > 0)
                {
                    fileName = m_resourcePath + "." + fileName;
                }
                Stream? stream = m_resourceAssembly.GetManifestResourceStream(fileName);
                if (stream != null)
                {
                    return stream;
                }
            }
            else
            {
                foreach (string name in m_resourceAssembly.GetManifestResourceNames())
                {
                    if (name.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        Stream? stream = m_resourceAssembly.GetManifestResourceStream(name);
                        if (stream != null)
                        {
                            return stream;
                        }
                    }
                }
            }
            throw new FileNotFoundException("Resource not found", path);
        }

        /// <inheritdoc/>
        public Stream OpenWrite(string path)
        {
            throw new IOException("Resource file system is not writeable");
        }

        /// <inheritdoc/>
        public void Delete(string path, bool isDirectory = false)
        {
            throw new IOException("Resource file system is not writeable");
        }

        /// <inheritdoc/>
        public void Replace(string sourcePath, string destinationPath)
        {
            throw new IOException("Resource file system is not writeable");
        }

        /// <inheritdoc/>
        public DateTime GetLastWriteTime(string path)
        {
            return DateTime.MinValue;
        }

        private readonly Assembly m_resourceAssembly;
        private readonly string? m_resourcePath;
    }
}
