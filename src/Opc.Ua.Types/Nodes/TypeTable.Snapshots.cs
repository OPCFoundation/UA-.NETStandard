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
using System.Threading;

namespace Opc.Ua
{
    /// <summary>
    /// Stores the type tree for a server.
    /// </summary>
    public partial class TypeTable
    {
        /// <summary>
        /// Captures an isolated view that copies the type graph when first mutated.
        /// </summary>
        /// <param name="source">The type table.</param>
        /// <param name="revision">The captured source revision.</param>
        /// <param name="namespaceUris">Private staging namespaces, or null to retain the source namespaces.</param>
        internal TypeTable CaptureSnapshot(
            out TypeTable source,
            out long revision,
            NamespaceTable? namespaceUris = null)
        {
            lock (m_lock)
            {
                source = this;
                revision = m_revision;
                var storage = new Storage();
                storage.ShareGraphFrom(m_storage);
                return new TypeTable(namespaceUris ?? m_namespaceUris)
                {
                    m_captureSource = new WeakReference<TypeTable>(this),
                    m_captureRevision = m_revision,
                    m_storage = storage
                };
            }
        }

        private static RegistrationGraph CopyGraph(RegistrationGraph source)
        {
            var graph = new RegistrationGraph();
            var copies = new Dictionary<TypeInfo, TypeInfo>();
            var pending = new Queue<TypeInfo>();
            foreach (KeyValuePair<NodeId, TypeInfo> entry in source.Nodes)
            {
                graph.Nodes.Add(entry.Key, Copy(entry.Value));
            }
            foreach (KeyValuePair<NodeId, TypeInfo> entry in source.Encodings)
            {
                graph.Encodings.Add(entry.Key, Copy(entry.Value));
            }
            foreach (KeyValuePair<QualifiedName, TypeInfo> entry in source.References)
            {
                graph.References.Add(entry.Key, Copy(entry.Value));
            }
            while (pending.Count != 0)
            {
                TypeInfo original = pending.Dequeue();
                TypeInfo copy = copies[original];
                copy.SuperType = original.SuperType is null ? null : Copy(original.SuperType);
                if (original.SubTypes is not null)
                {
                    copy.SubTypes = [];
                    foreach (KeyValuePair<NodeId, TypeInfo> child in original.SubTypes)
                    {
                        copy.SubTypes.Add(child.Key, Copy(child.Value));
                    }
                }
            }
            return graph;

            TypeInfo Copy(TypeInfo original)
            {
                if (copies.TryGetValue(original, out TypeInfo? existing))
                {
                    return existing;
                }
                var copy = new TypeInfo
                {
                    NodeId = original.NodeId,
                    BrowseName = original.BrowseName,
                    Deleted = original.Deleted,
                    Encodings = original.Encodings is null ? null : [.. original.Encodings]
                };
                copies.Add(original, copy);
                pending.Enqueue(original);
                return copy;
            }
        }

        /// <summary>
        /// Builds a compatible additive graph without changing either input.
        /// </summary>
        /// <remarks>
        /// The serving identity and revision must still match preparation. Concurrent changes are rejected,
        /// including compatible additions; publication reserves this same revision before the durable decision.
        /// </remarks>
        /// <param name="additions">The privately prepared type graph.</param>
        /// <param name="source">The serving table captured before preparation.</param>
        /// <param name="revision">The captured serving revision.</param>
        /// <returns>The independently owned merged graph.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        internal TypeTable PrepareAdditiveMerge(TypeTable additions, TypeTable source, long revision)
        {
            if (additions is null)
            {
                throw new ArgumentNullException(nameof(additions));
            }
            TypeTable merged = CaptureSnapshot(out TypeTable current, out long currentRevision);
            if (!ReferenceEquals(source, current) || revision != currentRevision)
            {
                throw new OptimisticPublicationConflictException("Serving types changed after the batch was prepared.");
            }
            if (additions.IsUnchangedCapture(source, revision))
            {
                return merged;
            }
            TypeTable proposed = additions.CaptureSnapshot(out _, out _);
            merged.EnsureWritableGraph();
            foreach (KeyValuePair<NodeId, TypeInfo> entry in proposed.m_nodes)
            {
                TypeInfo candidate = entry.Value;
                if (merged.m_nodes.TryGetValue(entry.Key, out TypeInfo? existing))
                {
                    if ((existing.SuperType?.NodeId ?? NodeId.Null) !=
                            (candidate.SuperType?.NodeId ?? NodeId.Null) ||
                        existing.BrowseName != candidate.BrowseName)
                    {
                        throw new InvalidOperationException(
                            $"Type '{entry.Key}' cannot redefine a serving supertype or reference name.");
                    }
                }
                else
                {
                    merged.m_nodes.Add(entry.Key, new TypeInfo
                    {
                        NodeId = candidate.NodeId,
                        BrowseName = candidate.BrowseName
                    });
                }
            }
            foreach (KeyValuePair<NodeId, TypeInfo> entry in proposed.m_nodes)
            {
                TypeInfo candidate = entry.Value;
                TypeInfo target = merged.m_nodes[entry.Key];
                if (candidate.SuperType is { } parent)
                {
                    if (!merged.m_nodes.TryGetValue(parent.NodeId, out TypeInfo? superType))
                    {
                        if (target.SuperType?.NodeId == parent.NodeId)
                        {
                            continue;
                        }
                        throw new InvalidOperationException(
                            $"Type '{entry.Key}' requires an unavailable supertype '{parent.NodeId}'.");
                    }
                    target.SuperType = superType;
                    superType.AddSubType(target);
                }
                if (!candidate.BrowseName.IsNull)
                {
                    if (merged.m_referenceTypes.TryGetValue(candidate.BrowseName, out TypeInfo? named) &&
                        named.NodeId != candidate.NodeId)
                    {
                        throw new InvalidOperationException(
                            $"Reference name '{candidate.BrowseName}' already identifies another type.");
                    }
                    merged.m_referenceTypes[candidate.BrowseName] = target;
                }
            }
            var complete = new HashSet<TypeInfo>();
            var active = new HashSet<TypeInfo>();
            var path = new List<TypeInfo>();
            foreach (TypeInfo node in merged.m_nodes.Values)
            {
                TypeInfo? ancestor = node;
                while (ancestor is not null && !complete.Contains(ancestor))
                {
                    if (!active.Add(ancestor))
                    {
                        throw new InvalidOperationException("Prepared types contain a cyclic supertype hierarchy.");
                    }
                    path.Add(ancestor);
                    ancestor = ancestor.SuperType;
                }
                foreach (TypeInfo visited in path)
                {
                    complete.Add(visited);
                }
                active.Clear();
                path.Clear();
            }
            foreach (KeyValuePair<NodeId, TypeInfo> encoding in proposed.m_encodings)
            {
                if (!merged.m_nodes.TryGetValue(encoding.Value.NodeId, out TypeInfo? target))
                {
                    throw new InvalidOperationException(
                        $"Encoding '{encoding.Key}' requires an unavailable data type.");
                }
                if (merged.m_encodings.TryGetValue(encoding.Key, out TypeInfo? existing) &&
                    existing.NodeId != target.NodeId)
                {
                    throw new InvalidOperationException(
                        $"Encoding '{encoding.Key}' cannot redefine a serving data type.");
                }
                merged.m_encodings[encoding.Key] = target;
                if (target.Encodings is null)
                {
                    target.Encodings = [encoding.Key];
                }
                else if (System.Array.IndexOf(target.Encodings, encoding.Key) < 0)
                {
                    target.Encodings = [.. target.Encodings, encoding.Key];
                }
            }
            merged.m_storage.MarkGraphShared();
            return merged;
        }

        /// <summary>
        /// Checks both the identity and revision of a captured source image.
        /// </summary>
        /// <param name="source">The type table.</param>
        /// <param name="revision">The revision observed during capture.</param>
        internal bool IsCurrentSnapshot(TypeTable source, long revision)
        {
            lock (m_lock)
            {
                return ReferenceEquals(source, this) && revision == m_revision;
            }
        }

        /// <summary>
        /// Reserves an unchanged source image until publication completes or is abandoned.
        /// </summary>
        /// <param name="source">The type table.</param>
        /// <param name="revision">The revision observed during capture.</param>
        /// <exception cref="InvalidOperationException"></exception>
        internal Publication BeginPublication(TypeTable source, long revision)
        {
            lock (m_lock)
            {
                if (!ReferenceEquals(source, this) || revision != m_revision)
                {
                    throw new OptimisticPublicationConflictException("Serving types changed after the batch was prepared.");
                }
                EnsureMutable();
                m_publicationPending = true;
                return new Publication(this);
            }
        }

        /// <summary>
        /// Checks whether a private graph still contains exactly its captured serving registrations.
        /// </summary>
        /// <param name="source">The expected capture source.</param>
        /// <param name="revision">The expected capture revision.</param>
        /// <returns>Whether no private mutation has occurred since capture.</returns>
        private bool IsUnchangedCapture(TypeTable source, long revision)
        {
            lock (m_lock)
            {
                return m_captureSource is { } origin &&
                    origin.TryGetTarget(out TypeTable? captured) &&
                    ReferenceEquals(captured, source) &&
                    m_captureRevision == revision &&
                    m_revision == 0;
            }
        }

        private void EnsureMutable()
        {
            if (m_storage.PublicationPending)
            {
                throw new InvalidOperationException(
                    "The type table has a pending publication and cannot be changed.");
            }
        }

        private void EnsureWritableGraph()
        {
            m_storage.EnsureWritableGraph();
        }

        /// <summary>Weak provenance avoids retaining earlier serving graphs through prepared copies.</summary>
        private WeakReference<TypeTable>? m_captureSource;

        /// <summary>The serving revision from which this private graph was copied.</summary>
        private long m_captureRevision;
        private long m_revision
        {
            get => m_storage.Revision;
            set => m_storage.Revision = value;
        }

        private bool m_publicationPending
        {
            get => m_storage.PublicationPending;
            set => m_storage.PublicationPending = value;
        }

        private Storage m_storage = new();

        /// <summary>
        /// Shares canonical metadata with a published staging table, including future additions.
        /// </summary>
        /// <param name="canonical">The serving table.</param>
        internal void BindTo(TypeTable canonical)
        {
            m_storage = canonical.m_storage;
        }

        /// <summary>
        /// Detaches cached staging tables before manager cleanup can remove shared registrations.
        /// </summary>
        internal void Detach()
        {
            m_storage = CaptureSnapshot(out _, out _).m_storage;
        }

        /// <summary>
        /// Owns the canonical graph and mutation reservation, independently of cached table handles.
        /// </summary>
        private sealed class Storage
        {
            /// <summary>
            /// Initializes an empty registration graph.
            /// </summary>
            internal Storage()
            {
                m_graph = new RegistrationGraph();
            }

            /// <summary>
            /// Shares a graph with another storage handle until either handle mutates it.
            /// </summary>
            /// <param name="source">The storage handle to share registrations with.</param>
            internal void ShareGraphFrom(Storage source)
            {
                m_graph = source.m_graph;
                m_graph.MarkShared();
            }

            /// <summary>
            /// Detaches from a graph that may be read by another handle.
            /// </summary>
            internal void EnsureWritableGraph()
            {
                if (m_graph.IsShared)
                {
                    m_graph = CopyGraph(m_graph);
                }
            }

            /// <summary>
            /// Marks the graph immutable until all writer handles detach.
            /// </summary>
            internal void MarkGraphShared()
            {
                m_graph.MarkShared();
            }

            /// <summary>Serializes access through every bound handle.</summary>
            internal readonly Lock Gate = new();

            /// <summary>Indexes registered reference names.</summary>
            internal SortedDictionary<QualifiedName, TypeInfo> References
            {
                get => m_graph.References;
                set => m_graph.References = value;
            }

            /// <summary>Indexes registered types.</summary>
            internal NodeIdDictionary<TypeInfo> Nodes
            {
                get => m_graph.Nodes;
                set => m_graph.Nodes = value;
            }

            /// <summary>Indexes data type encoding ownership.</summary>
            internal NodeIdDictionary<TypeInfo> Encodings
            {
                get => m_graph.Encodings;
                set => m_graph.Encodings = value;
            }

            private RegistrationGraph m_graph;

            /// <summary>Identifies the current canonical graph revision.</summary>
            internal long Revision;

            /// <summary>Rejects writes reserved by an undecided publication.</summary>
            internal bool PublicationPending;
        }

        /// <summary>
        /// Holds mutable type registrations that become immutable while shared by snapshots.
        /// </summary>
        private sealed class RegistrationGraph
        {
            /// <summary>Indexes registered reference names.</summary>
            internal SortedDictionary<QualifiedName, TypeInfo> References = [];

            /// <summary>Indexes registered types.</summary>
            internal NodeIdDictionary<TypeInfo> Nodes = [];

            /// <summary>Indexes data type encoding ownership.</summary>
            internal NodeIdDictionary<TypeInfo> Encodings = [];

            /// <summary>Indicates that a snapshot may still read this graph.</summary>
            internal bool IsShared => Volatile.Read(ref m_shared) != 0;

            /// <summary>Marks the graph immutable until a writer detaches it.</summary>
            internal void MarkShared()
            {
                Interlocked.Exchange(ref m_shared, 1);
            }

            private int m_shared;
        }

        /// <summary>
        /// Reserves type mutations until additive publication completes or is abandoned.
        /// </summary>
        /// <param name="owner">The type table.</param>
        internal sealed class Publication(TypeTable owner) : IDisposable
        {
            /// <summary>
            /// Installs the prevalidated additive graph without replacing canonical table handles.
            /// </summary>
            /// <exception cref="ObjectDisposedException"></exception>
            public void Complete(TypeTable merged)
            {
                TypeTable current = m_owner ?? throw new ObjectDisposedException(nameof(Publication));
                lock (current.m_lock)
                {
                    current.m_storage.ShareGraphFrom(merged.m_storage);
                    current.m_revision++;
                }
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                TypeTable? current = Interlocked.Exchange(ref m_owner, null);
                if (current is not null)
                {
                    lock (current.m_lock)
                    {
                        current.m_publicationPending = false;
                    }
                }
            }

            private TypeTable? m_owner = owner;
        }
    }
}
