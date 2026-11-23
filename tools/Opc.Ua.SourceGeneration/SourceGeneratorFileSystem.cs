// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
            // A project can list the same file as an AdditionalFile more than
            // once (globs overlapping an explicit item, for instance). Keep the
            // first entry rather than letting ToDictionary throw out of the
            // generator's constructor, which happens before any guarded region
            // and surfaces as a bare generator crash.
            m_files = [];
            foreach (AdditionalText text in additionalTexts)
            {
                if (text?.Path != null && !m_files.ContainsKey(text.Path))
                {
                    m_files.Add(text.Path, text);
                }
            }
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
        public void Replace(string sourcePath, string destinationPath)
        {
            throw new NotSupportedException("Write not allowed");
        }

        /// <inheritdoc/>
        public Stream OpenWrite(string path)
        {
            throw new NotSupportedException("Write not allowed");
        }

        private readonly Dictionary<string, AdditionalText> m_files;
    }
}
