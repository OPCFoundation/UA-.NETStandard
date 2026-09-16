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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Opc.Ua.Export;

namespace UaLens.NodeSets.Loading
{
    /// <summary>
    /// Bounds input before passing it to the stack's NodeSet2 serializer.
    /// </summary>
    internal static class NodeSetDocumentReader
    {
        public static async Task<NodeSetDocument> ReadAsync(
            string path,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            cancellationToken.ThrowIfCancellationRequested();
            string source = Path.GetFullPath(path);
            var stream = new FileStream(
                source, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, FileOptions.Asynchronous);
            await using (stream.ConfigureAwait(false))
            {
                return await ReadAsync(stream, source, cancellationToken).ConfigureAwait(false);
            }
        }

        internal static async Task<NodeSetDocument> ReadAsync(
            Stream stream,
            string source,
            CancellationToken cancellationToken)
        {
            using MemoryStream content = await ReadBoundedAsync(
                stream, MaxDocumentBytes, source, false, cancellationToken).ConfigureAwait(false);
            return await Task.Run(() => Parse(content, source, cancellationToken), cancellationToken)
                .ConfigureAwait(false);
        }

        internal static async Task<MemoryStream> ReadBoundedAsync(
            Stream stream,
            int maximum,
            string source,
            bool prefixOnly,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!prefixOnly && stream.CanSeek && stream.Length - stream.Position > maximum)
            {
                throw new InvalidDataException($"'{source}' exceeds the {maximum}-byte input limit.");
            }
            var content = new MemoryStream();
            try
            {
                byte[] buffer = new byte[8192];
                while (content.Length < maximum)
                {
                    int count = await stream.ReadAsync(
                        buffer.AsMemory(0, (int)Math.Min(buffer.Length, maximum - content.Length)),
                        cancellationToken).ConfigureAwait(false);
                    if (count == 0)
                    {
                        content.Position = 0;
                        return content;
                    }
                    await content.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                }
                if (!prefixOnly &&
                    await stream.ReadAsync(buffer.AsMemory(0, 1), cancellationToken).ConfigureAwait(false) != 0)
                {
                    throw new InvalidDataException($"'{source}' exceeds the {maximum}-byte input limit.");
                }
                content.Position = 0;
                return content;
            }
            catch
            {
                await content.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        internal static XmlReaderSettings CreateXmlSettings()
        {
            return new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = MaxDocumentBytes,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true
            };
        }

        private static NodeSetDocument Parse(MemoryStream content, string source, CancellationToken cancellationToken)
        {
            try
            {
                long size = content.Length;
                using (var reader = XmlReader.Create(content, CreateXmlSettings()))
                {
                    int nodes = 0;
                    while (reader.Read())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (reader.Depth > MaxXmlDepth)
                        {
                            throw new InvalidDataException($"'{source}' exceeds the XML depth limit of {MaxXmlDepth}.");
                        }
                        if (reader.NodeType == XmlNodeType.Element &&
                            reader.Depth == 1 &&
                            reader.NamespaceURI == NodeSetMetadata.XmlNamespace &&
                            reader.LocalName.StartsWith("UA", StringComparison.Ordinal) &&
                            ++nodes > MaxNodes)
                        {
                            throw new InvalidDataException($"'{source}' exceeds the node limit of {MaxNodes}.");
                        }
                    }
                }
                content.Position = 0;
                cancellationToken.ThrowIfCancellationRequested();
                UANodeSet nodeSet = UANodeSet.Read(content) ??
                    throw new InvalidDataException($"'{source}' does not contain a NodeSet2 document.");
                cancellationToken.ThrowIfCancellationRequested();
                return new NodeSetDocument(source, nodeSet) { SizeBytes = size };
            }
            catch (Exception exception) when (exception is XmlException or InvalidOperationException)
            {
                throw new InvalidDataException($"Cannot read NodeSet2 '{source}': {exception.Message}", exception);
            }
        }

        internal const int MaxDocumentBytes = 64 * 1024 * 1024;
        internal const int MaxHeaderBytes = 128 * 1024;
        internal const int MaxNodes = 500_000;
        internal const int MaxXmlDepth = 128;
    }
}
