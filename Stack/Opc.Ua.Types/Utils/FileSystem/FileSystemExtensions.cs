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

#nullable enable

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
