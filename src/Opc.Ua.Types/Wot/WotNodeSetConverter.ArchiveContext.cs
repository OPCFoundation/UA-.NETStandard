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
using System.Globalization;
using System.Linq;
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
        /// Groups the preserved native nodes of documents by authoritative
        /// model identity, so each document is validated against the
        /// partitions of its own model only.
        /// </summary>
        /// <remarks>
        /// A group is keyed by the model table (URI, version, publication date)
        /// together with the namespace table, never by the namespace table
        /// alone: unrelated or conflicting exports can share a table. An archive
        /// that declares no model has no authoritative identity and neither
        /// contributes to nor receives a context. Partitions of a group must agree
        /// on the server URIs and on every alias they share, and their aliases are
        /// merged into the context. A group whose partitions declare the same
        /// NodeId twice (in any equivalent spelling) has conflicting
        /// native identities, and no sibling may override another, so asking
        /// for its context fails. The index is safe for concurrent use. A
        /// returned context is a completed snapshot that the index never
        /// mutates again; callers must treat it as read-only.
        /// </remarks>
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
                if (part.Models is not { Length: > 0 })
                {
                    return;
                }
                string key = ModelKey(part);
                lock (m_lock)
                {
                    if (!m_groups.TryGetValue(key, out Group? group))
                    {
                        group = new Group(part);
                        m_groups.Add(key, group);
                    }
                    group.Merge(part);
                    foreach (UANode node in part.Items ?? [])
                    {
                        if (!group.NodeIds.Add(Canonical(node.NodeId)))
                        {
                            group.Conflicted = true;
                        }
                        group.Items.Add(node);
                    }
                    group.Context = null;
                }
            }

            /// <summary>
            /// Declares the index incomplete, for example because a bound was
            /// exhausted while it was built. Asking for the context of a
            /// document that carries preserved native nodes then fails
            /// instead of validating against an arbitrary subset.
            /// </summary>
            /// <param name="reason">Why the index is incomplete.</param>
            /// <exception cref="ArgumentNullException"><paramref name="reason"/> is <c>null</c>.</exception>
            public void MarkIncomplete(string reason)
            {
                if (reason is null)
                {
                    throw new ArgumentNullException(nameof(reason));
                }
                lock (m_lock)
                {
                    m_incomplete ??= reason;
                }
            }

            /// <summary>
            /// Gets the context of the document's model, or <c>null</c> when the
            /// document carries no preserved native nodes or its model has no
            /// partitions in the index.
            /// </summary>
            /// <param name="document">The document.</param>
            /// <returns>The context, or <c>null</c>.</returns>
            /// <exception cref="ArgumentNullException"><paramref name="document"/> is <c>null</c>.</exception>
            /// <exception cref="InvalidOperationException">
            /// The index is incomplete or the document's model has conflicting native identities.
            /// </exception>
            public UANodeSet? Find(WotDocument document)
            {
                if (document is null)
                {
                    throw new ArgumentNullException(nameof(document));
                }
                if (RestoreArchivePart(document, m_options) is not { } part || part.Models is not { Length: > 0 })
                {
                    return null;
                }
                lock (m_lock)
                {
                    if (m_incomplete is not null)
                    {
                        throw new InvalidOperationException(
                            "The native document context is incomplete: " + m_incomplete);
                    }
                    if (!m_groups.TryGetValue(ModelKey(part), out Group? group))
                    {
                        return null;
                    }
                    if (group.Conflicted)
                    {
                        throw new InvalidOperationException(
                            "The partitions of the model declare conflicting native identities.");
                    }
                    if (group.Context is null)
                    {
                        UANodeSet context = CopyDocumentSetHeader(group.Header);
                        context.Aliases =
                        [
                            .. group.Aliases.Select(a => new NodeIdAlias { Alias = a.Key, Value = a.Value })
                        ];
                        context.Items = [.. group.Items];
                        group.Context = context;
                    }
                    return group.Context;
                }
            }

            private static string Canonical(string? nodeId)
            {
                return NodeId.TryParse(nodeId ?? string.Empty, out NodeId identity)
                    ? identity.ToString()
                    : nodeId ?? string.Empty;
            }

            private static string ModelKey(UANodeSet part)
            {
                var key = new System.Text.StringBuilder();
                foreach (ModelTableEntry model in part.Models ?? [])
                {
                    Append(key, model.ModelUri);
                    Append(key, model.Version);
                    Append(
                        key,
                        model.PublicationDateSpecified
                            ? model.PublicationDate.Ticks.ToString(CultureInfo.InvariantCulture)
                            : null);
                }
                key.Append('|');
                foreach (string uri in part.NamespaceUris ?? [])
                {
                    Append(key, uri);
                }
                return key.ToString();
            }

            private static void Append(System.Text.StringBuilder key, string? value)
            {
                if (value is null)
                {
                    key.Append("-;");
                    return;
                }
                key.Append(value.Length).Append(':').Append(value).Append(';');
            }

            private sealed class Group
            {
                public Group(UANodeSet header)
                {
                    Header = header;
                }

                public UANodeSet Header { get; }

                public Dictionary<string, string> Aliases { get; } = new(StringComparer.Ordinal);

                public List<UANode> Items { get; } = [];

                public HashSet<string> NodeIds { get; } = new(StringComparer.Ordinal);

                public bool Conflicted { get; set; }

                public void Merge(UANodeSet part)
                {
                    if (!(part.ServerUris ?? []).SequenceEqual(Header.ServerUris ?? [], StringComparer.Ordinal))
                    {
                        Conflicted = true;
                    }
                    foreach (NodeIdAlias alias in part.Aliases ?? [])
                    {
                        string name = alias.Alias ?? string.Empty;
                        string value = alias.Value ?? string.Empty;
                        if (!Aliases.TryGetValue(name, out string? existing))
                        {
                            Aliases.Add(name, value);
                        }
                        else if (!string.Equals(Canonical(existing), Canonical(value), StringComparison.Ordinal))
                        {
                            Conflicted = true;
                        }
                    }
                }

                public UANodeSet? Context { get; set; }
            }

            private readonly System.Threading.Lock m_lock = new();
            private readonly WotNodeSetConverterOptions m_options;
            private readonly Dictionary<string, Group> m_groups = new(StringComparer.Ordinal);
            private string? m_incomplete;
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

        private static bool CarriesPreservedNodes(WotDocument document)
        {
            return document.TryGetEnvelope(out _) || document.TryGetNativeProjection(out _);
        }

        private static UANodeSet? GetProvidedArchiveContext(IWotThingResolver? resolver, WotDocument document)
        {
            return resolver switch
            {
                DocumentSetThingResolver set => set.ArchiveContext,
                IWotArchiveContextProvider provider when CarriesPreservedNodes(document) =>
                    provider.GetArchiveContext(document),
                _ => null
            };
        }
    }
}
