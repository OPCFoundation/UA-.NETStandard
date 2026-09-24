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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Server.Registry;

namespace Opc.Ua.WotCon.Server.Materialization
{
    internal sealed class WotDeclarationInputCache(IReadOnlyDictionary<string, ByteString> contents) : IDisposable
    {
        public void Dispose()
        {
            lock (m_gate)
            {
                if (m_disposed)
                {
                    return;
                }
                m_disposed = true;
                foreach (ParsedDeclarations parsed in m_parsed.Values)
                {
                    parsed.Document.Dispose();
                }
                m_parsed.Clear();
            }
        }

        internal ArrayOf<WotDataTypeDefinitionSource> GetDefinitions(
            WotResource source,
            ArrayOf<WotResource> members,
            bool ownsResolutionDefinitions,
            WotNodeSetConverterOptions options,
            CancellationToken cancellationToken)
        {
            _ = options ?? throw new ArgumentNullException(nameof(options));
            options.Validate();
            var definitions = new List<WotDataTypeDefinitionSource>();
            var indexed = new HashSet<string>(StringComparer.Ordinal);
            long bytes = 0;
            lock (m_gate)
            {
                if (m_disposed)
                {
                    throw new ObjectDisposedException(nameof(WotDeclarationInputCache));
                }
                foreach (WotResource member in members)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    WotResourceVersion? version = member.DefaultVersion;
                    if (version is null || !contents.TryGetValue(version.DigestHex, out ByteString content) ||
                        !indexed.Add(version.DigestHex))
                    {
                        continue;
                    }
                    bytes = checked(bytes + content.Length);
                    if (indexed.Count > options.MaxResolverDocuments || bytes > options.MaxResolverTotalBytes ||
                        content.Length > options.MaxJsonDocumentSize)
                    {
                        throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                            "The captured sibling documents exceed the resolver budget.");
                    }
                    if (member.Xid == source.Xid ||
                        version.Dependencies is { } metadata && metadata.DataTypeDefinitionIds.IsEmpty)
                    {
                        continue;
                    }
                    var key = (version.DigestHex, options.MaxJsonDepth);
                    if (!m_parsed.TryGetValue(key, out ParsedDeclarations? parsed))
                    {
                        WotDocument document = WotDocument.Parse(content.Memory, options);
                        try
                        {
                            parsed = new ParsedDeclarations(
                                document, WotNodeSetConverter.ReadDataTypeDefinitions(document));
                            m_parsed.Add(key, parsed);
                        }
                        catch
                        {
                            document.Dispose();
                            throw;
                        }
                    }
                    foreach (WotDataTypeDefinitionSource definition in parsed.Definitions)
                    {
                        definitions.Add(new WotDataTypeDefinitionSource(parsed.Document, definition.Definition)
                        {
                            ProjectedSeparately = member.Enabled || !ownsResolutionDefinitions
                        });
                    }
                    if (definitions.Count > options.MaxNodeCount)
                    {
                        throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                            "The captured DataType definitions exceed the configured limit.");
                    }
                }
            }
            return definitions.ToArrayOf();
        }

        private readonly Lock m_gate = new();
        private readonly Dictionary<(string Digest, int Depth), ParsedDeclarations> m_parsed = [];
        private bool m_disposed;

        private sealed record ParsedDeclarations(
            WotDocument Document, ArrayOf<WotDataTypeDefinitionSource> Definitions);
    }

    internal sealed class WotDocumentConversionContents(
        IReadOnlyDictionary<string, ByteString> contents,
        WotDeclarationInputCache declarations,
        WotResource source,
        ArrayOf<WotResource> members,
        bool ownsResolutionDefinitions) : IReadOnlyDictionary<string, ByteString>, IWotDocumentConversionContext
    {
        public ByteString this[string key] => contents[key];
        public IEnumerable<string> Keys => contents.Keys;
        public IEnumerable<ByteString> Values => contents.Values;
        public int Count => contents.Count;

        public ArrayOf<WotDataTypeDefinitionSource> GetDataTypeDefinitions(
            WotNodeSetConverterOptions options, CancellationToken cancellationToken = default)
        {
            return declarations.GetDefinitions(source, members, ownsResolutionDefinitions, options, cancellationToken);
        }

        public bool ContainsKey(string key)
        {
            return contents.ContainsKey(key);
        }

        public bool TryGetValue(string key, [MaybeNullWhen(false)] out ByteString value)
        {
            return contents.TryGetValue(key, out value);
        }

        public IEnumerator<KeyValuePair<string, ByteString>> GetEnumerator()
        {
            return contents.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
