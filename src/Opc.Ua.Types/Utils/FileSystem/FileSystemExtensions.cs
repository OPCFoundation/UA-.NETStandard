/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System.IO;
using System.Reflection;
using System.Text;

namespace Opc.Ua
{
    /// <summary>
    /// Extensions for file system
    /// </summary>
    public static class FileSystemExtensions
    {
        /// <summary>
        /// Create a text writer
        /// </summary>
        public static TextWriter CreateTextWriter(this IFileSystem fileSystem, string path)
        {
            return new StreamWriter(fileSystem.OpenWrite(path), Encoding.UTF8, 16 * 1024);
        }

        /// <summary>
        /// Create a text reader
        /// </summary>
        public static TextReader CreateTextReader(this IFileSystem fileSystem, string path)
        {
            return new StreamReader(fileSystem.OpenRead(path), Encoding.UTF8, true, 16 * 1024);
        }

        /// <summary>
        /// Combine file systems. This can be used to chain resource assembly file systems
        /// first, and finally a writeable local file system. Or to combine a local with a
        /// virtual file system (although the virtual has fallback capabilities)
        /// </summary>
        /// <param name="primary"></param>
        /// <param name="fallback"></param>
        /// <returns></returns>
        public static IFileSystem WithFallback(
            this IFileSystem primary,
            params IFileSystem[] fallback)
        {
            IFileSystem combined = primary;
            foreach (IFileSystem fs in fallback)
            {
                combined = new CombinedFileSystem(combined, fs);
            }
            return combined;
        }

        /// <summary>
        /// Convert assembly to a file system with an optional resource path.
        /// <see cref="ResourceFileSystem"/> for more information.
        /// </summary>
        /// <param name="resourceAssembly"></param>
        /// <param name="resourcePath"></param>
        /// <returns></returns>
        public static IFileSystem AsFileSystem(
            this Assembly resourceAssembly,
            string? resourcePath = null)
        {
            return new ResourceFileSystem(resourceAssembly, resourcePath);
        }
    }
}
