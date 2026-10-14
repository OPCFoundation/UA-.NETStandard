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
using System.Collections.Generic;
using System.Text.Json;
using Opc.Ua.Export;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// A thing resolver that also supplies the native NodeSet context a
    /// single-document conversion validates preserved readable facts against.
    /// </summary>
    /// <remarks>
    /// A document of a partitioned export states references whose authoritative
    /// facts (the reverse reference, a symmetric ReferenceType) live in sibling
    /// partitions. A document-set conversion validates against the whole set;
    /// a caller that converts one document at a time supplies the same context
    /// through this interface.
    /// </remarks>
    public interface IWotArchiveContextProvider
    {
        /// <summary>
        /// Gets the native NodeSet context for the document, or <c>null</c>
        /// when the document has no siblings that share its namespace table.
        /// </summary>
        /// <param name="document">The document being converted.</param>
        /// <returns>The context, or <c>null</c>.</returns>
        UANodeSet? GetArchiveContext(WotDocument document);
    }

    public static partial class WotNodeSetConverter
    {
        /// <summary>
        /// Groups the preserved native nodes of documents by namespace table, so
        /// each document is validated against the partitions of its own model only.
        /// </summary>
        public sealed class ArchiveContextIndex
        {
            /// <summary>
            /// Initializes an empty index.
            /// </summary>
            /// <param name="options">The options the preserved nodes are restored with.</param>
            public ArchiveContextIndex(WotNodeSetConverterOptions? options = null)
            {
                m_options = options ?? new WotNodeSetConverterOptions();
                m_options.Validate();
            }

            /// <summary>
            /// Adds the preserved native nodes of a document. A document without
            /// them, or whose preserved nodes cannot be restored, adds nothing.
            /// </summary>
            /// <param name="document">The document.</param>
            /// <exception cref="ArgumentNullException"><paramref name="document"/> is <c>null</c>.</exception>
            public void Add(WotDocument document)
            {
                if (document is null)
                {
                    throw new ArgumentNullException(nameof(document));
                }
                if (RestoreArchivePart(document, m_options) is not { } part)
                {
                    return;
                }
                string key = NamespaceKey(part);
                if (!m_groups.TryGetValue(key, out Group? group))
                {
                    group = new Group(part);
                    m_groups.Add(key, group);
                }
                group.Items.AddRange(part.Items ?? []);
                group.Context = null;
            }

            /// <summary>
            /// Gets the context of the document's namespace table, or <c>null</c>.
            /// </summary>
            /// <param name="document">The document.</param>
            /// <returns>The context, or <c>null</c>.</returns>
            /// <exception cref="ArgumentNullException"><paramref name="document"/> is <c>null</c>.</exception>
            public UANodeSet? Find(WotDocument document)
            {
                if (document is null)
                {
                    throw new ArgumentNullException(nameof(document));
                }
                if (RestoreArchivePart(document, m_options) is not { } part ||
                    !m_groups.TryGetValue(NamespaceKey(part), out Group? group))
                {
                    return null;
                }
                if (group.Context is null)
                {
                    UANodeSet context = CopyDocumentSetHeader(group.Header);
                    context.Items = [.. group.Items];
                    group.Context = context;
                }
                return group.Context;
            }

            private static string NamespaceKey(UANodeSet part)
            {
                return string.Join("\n", part.NamespaceUris ?? []);
            }

            private sealed class Group
            {
                public Group(UANodeSet header)
                {
                    Header = header;
                }

                public UANodeSet Header { get; }

                public List<UANode> Items { get; } = [];

                public UANodeSet? Context { get; set; }
            }

            private readonly WotNodeSetConverterOptions m_options;
            private readonly Dictionary<string, Group> m_groups = new(StringComparer.Ordinal);
        }

        private static UANodeSet? RestoreArchivePart(WotDocument document, WotNodeSetConverterOptions options)
        {
            var ignored = new List<WotDiagnostic>();
            UANodeSet? part = null;
            if (document.TryGetEnvelope(out JsonElement envelope))
            {
                part = RestoreFromEnvelope(envelope, options, ignored);
            }
            else if (document.TryGetNativeProjection(out JsonElement projection) &&
                !WotNativeProjection.HasUnsupportedProfile(projection))
            {
                part = WotNativeProjection.Read(projection, options, ignored);
            }
            return part is not null && !HasErrors(ignored) ? part : null;
        }

        private static UANodeSet? GetProvidedArchiveContext(IWotThingResolver? resolver, WotDocument document)
        {
            return resolver switch
            {
                DocumentSetThingResolver set => set.ArchiveContext,
                IWotArchiveContextProvider provider => provider.GetArchiveContext(document),
                _ => null
            };
        }
    }
}
