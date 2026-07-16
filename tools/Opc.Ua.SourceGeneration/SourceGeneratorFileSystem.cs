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
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Generator file system that reads from additional texts
    /// </summary>
    internal sealed class SourceGeneratorFileSystem : IFileSystem
    {
        /// <summary>
        /// Create generator file system
        /// </summary>
        public SourceGeneratorFileSystem(
            IEnumerable<AdditionalText> additionalTexts)
        {
            m_files = additionalTexts.ToDictionary(
                text => text.Path,
                text => text);
        }

        /// <inheritdoc/>
        public void Delete(string path, bool isDirectory = false)
        {
            if (!isDirectory)
            {
                m_files.Remove(path);
            }
        }

        /// <inheritdoc/>
        public bool Exists(string path, bool isDirectory = false)
        {
            if (isDirectory)
            {
                return false;
            }
            return m_files.ContainsKey(path);
        }

        /// <inheritdoc/>
        public DateTime GetLastWriteTime(string path)
        {
            return DateTime.MinValue;
        }

        /// <inheritdoc/>
        public long GetLength(string path)
        {
            if (m_files.TryGetValue(path, out AdditionalText text))
            {
                SourceText sourceText = text.GetText();
                if (sourceText != null)
                {
                    return sourceText.Length;
                }
            }
            throw new FileNotFoundException($"File not found: {path}");
        }

        /// <inheritdoc/>
        public Stream OpenRead(string path)
        {
            if (m_files.TryGetValue(path, out AdditionalText text))
            {
                SourceText sourceText = text.GetText();
                if (sourceText != null)
                {
                    var memoryStream = new MemoryStream();
                    using (var writer = new StreamWriter(
                        memoryStream, Encoding.UTF8, 8 * 1024, true))
                    {
                        sourceText.Write(writer);
                    }
                    memoryStream.Position = 0;
                    return memoryStream;
                }
            }
            throw new FileNotFoundException($"File not found: {path}");
        }

        /// <inheritdoc/>
        public Stream OpenWrite(string path)
        {
            throw new NotSupportedException("Write not allowed");
        }

        private readonly Dictionary<string, AdditionalText> m_files;
    }
}
