// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
            return CaptureSnapshot(out source, out revision, namespaceUris, copyNamespaceUris: false);
        }

        private TypeTable CaptureSnapshot(
            out TypeTable source,
            out long revision,
            NamespaceTable? namespaceUris,
            bool copyNamespaceUris)
        {
            lock (m_lock)
            {
                source = this;
                revision = m_revision;
                var storage = new Storage();
                storage.ShareGraphFrom(m_storage);
                storage.StagedRegistrations = [];
                NamespaceTable capturedNamespaces = namespaceUris
                    ?? (copyNamespaceUris
                        ? new NamespaceTable(m_namespaceUris.GetSnapshot(out _).ToArray()!)
                        : m_namespaceUris);
                return new TypeTable(capturedNamespaces)
                {
                    m_captureSource = new WeakReference<TypeTable>(this),
                    m_captureRevision = m_revision,
                    m_candidateLocalTypes = new HashSet<NodeId>(m_candidateLocalTypes),
                    m_storage = storage
                };
            }
        }

        /// <summary>
        /// Captures a type image with an independent namespace mapping for immutable published views.
        /// </summary>
        /// <returns>The isolated type image.</returns>
        internal TypeTable CaptureImmutableSnapshot()
        {
            return CaptureSnapshot(out _, out _, namespaceUris: null, copyNamespaceUris: true);
        }

        /// <summary>
        /// Gets whether another live storage handle currently shares the registration graph.
        /// </summary>
        internal bool HasSharedRegistrationGraph
        {
            get
            {
                lock (m_lock)
                {
                    return m_storage.IsShared;
                }
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
                    RegistrationVersion = original.RegistrationVersion,
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
            return PrepareAdditiveMerge(additions, source, revision, rebase: false, out _);
        }

        /// <summary>
        /// Builds a compatible additive graph without changing either input, optionally rebasing the private
        /// registrations onto a newer serving revision.
        /// </summary>
        /// <remarks>
        /// Without <paramref name="rebase"/> the serving revision must still match preparation. With it, a newer
        /// revision is accepted: only the types the candidate registered are merged, and each must still have the
        /// serving registration it had when the batch was prepared. A type another publication registered or
        /// removed meanwhile, or a supertype that disappeared, is an <see cref="OptimisticPublicationConflictException"/>.
        /// Publication must reserve <paramref name="mergedRevision"/> before the durable decision.
        /// </remarks>
        /// <param name="additions">The privately prepared type graph.</param>
        /// <param name="source">The serving table captured before preparation.</param>
        /// <param name="revision">The captured serving revision.</param>
        /// <param name="rebase">Whether a newer serving revision may be merged.</param>
        /// <param name="mergedRevision">The serving revision the result was merged onto.</param>
        /// <returns>The independently owned merged graph.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        internal TypeTable PrepareAdditiveMerge(
            TypeTable additions,
            TypeTable source,
            long revision,
            bool rebase,
            out long mergedRevision)
        {
            if (additions is null)
            {
                throw new ArgumentNullException(nameof(additions));
            }
            TypeTable merged = CaptureSnapshot(out TypeTable current, out long currentRevision);
            mergedRevision = currentRevision;
            bool rebased = revision != currentRevision;
            if (!ReferenceEquals(source, current) || (rebased && !rebase))
            {
                throw new OptimisticPublicationConflictException("Serving types changed after the batch was prepared.");
            }
            if (additions.IsUnchangedCapture(source, revision))
            {
                return merged;
            }
            HashSet<NodeId>? only = null;
            if (rebased)
            {
                Dictionary<NodeId, long> staged;
                lock (additions.m_lock)
                {
                    if (additions.m_stagedRegistrations is null ||
                        additions.m_captureSource is null ||
                        !additions.m_captureSource.TryGetTarget(out TypeTable? captured) ||
                        !ReferenceEquals(captured, source) ||
                        additions.m_captureRevision != revision)
                    {
                        throw new OptimisticPublicationConflictException(
                            "Serving types changed after the batch was prepared.");
                    }
                    staged = new Dictionary<NodeId, long>(additions.m_stagedRegistrations);
                }
                foreach (KeyValuePair<NodeId, long> registration in staged)
                {
                    long serving = merged.m_nodes.TryGetValue(registration.Key, out TypeInfo? existing)
                        ? existing.RegistrationVersion
                        : 0;
                    if (serving != registration.Value)
                    {
                        throw new OptimisticPublicationConflictException(
                            $"Type '{registration.Key}' changed after the batch was prepared.");
                    }
                }
                only = [.. staged.Keys];
            }
            TypeTable proposed = additions.CaptureSnapshot(out _, out _);
            merged.EnsureWritableGraph();
            foreach (KeyValuePair<NodeId, TypeInfo> entry in proposed.m_nodes)
            {
                if (only is not null && !only.Contains(entry.Key))
                {
                    continue;
                }
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
                    existing.RegistrationVersion = candidate.RegistrationVersion;
                }
                else
                {
                    merged.m_nodes.Add(entry.Key, new TypeInfo
                    {
                        NodeId = candidate.NodeId,
                        BrowseName = candidate.BrowseName,
                        RegistrationVersion = candidate.RegistrationVersion
                    });
                }
            }
            foreach (KeyValuePair<NodeId, TypeInfo> entry in proposed.m_nodes)
            {
                if (only is not null && !only.Contains(entry.Key))
                {
                    continue;
                }
                TypeInfo candidate = entry.Value;
                TypeInfo target = merged.m_nodes[entry.Key];
                if (candidate.SuperType is { } parent)
                {
                    if (!merged.m_nodes.TryGetValue(parent.NodeId, out TypeInfo? superType))
                    {
                        // An unchanged capture may keep a deleted ancestor; a rebased registration may not, because
                        // its supertype was removed after the batch saw it.
                        if (only is null && target.SuperType?.NodeId == parent.NodeId)
                        {
                            continue;
                        }
                        // The capture had the supertype, so another publication removed it meanwhile.
                        string missing = $"Type '{entry.Key}' requires an unavailable supertype '{parent.NodeId}'.";
                        throw only is not null
                            ? new OptimisticPublicationConflictException(missing)
                            : new InvalidOperationException(missing);
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
                if (only is not null && !only.Contains(encoding.Value.NodeId))
                {
                    continue;
                }
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

        private bool m_rejectConflictingDefinitions;

        private HashSet<NodeId> m_candidateLocalTypes = [];

        /// <summary>
        /// The types a captured table registered privately, with the serving registration version each replaced.
        /// </summary>
        private Dictionary<NodeId, long>? m_stagedRegistrations => m_storage.StagedRegistrations;

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
            m_rejectConflictingDefinitions = false;
            m_candidateLocalTypes.Clear();
        }

        /// <summary>
        /// Rejects registrations that conflict with definitions in the captured graph.
        /// </summary>
        internal void RejectCandidateConflicts()
        {
            m_rejectConflictingDefinitions = true;
        }

        private bool ShouldRejectCandidateConflict(NodeId typeId)
        {
            return m_rejectConflictingDefinitions || m_candidateLocalTypes.Contains(typeId);
        }

        /// <summary>
        /// Records the first private registration of a type in a captured table, with the serving registration
        /// version it replaces (zero when the capture did not contain the type). Called under the table lock.
        /// </summary>
        private void TrackStagedRegistration(NodeId typeId)
        {
            Dictionary<NodeId, long>? staged = m_stagedRegistrations;
            if (staged is not null && !staged.ContainsKey(typeId))
            {
                staged.Add(
                    typeId,
                    m_nodes.TryGetValue(typeId, out TypeInfo? captured) ? captured.RegistrationVersion : 0);
            }
        }

        private void TrackCandidateType(NodeId typeId)
        {
            if (m_captureSource is not null)
            {
                m_candidateLocalTypes.Add(typeId);
            }
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
            /// The types registered through a captured storage, with the serving registration version each
            /// replaced (zero when the capture did not contain the type). Null for serving storage.
            /// </summary>
            internal Dictionary<NodeId, long>? StagedRegistrations;

            /// <summary>
            /// Initializes an empty registration graph.
            /// </summary>
            internal Storage()
            {
                m_graph = new RegistrationGraph();
                m_graph.Track(this);
            }

            /// <summary>
            /// Shares a graph with another storage handle until either handle mutates it.
            /// </summary>
            /// <param name="source">The storage handle to share registrations with.</param>
            internal void ShareGraphFrom(Storage source)
            {
                m_graph = source.m_graph;
                m_graph.Track(this);
            }

            /// <summary>
            /// Detaches from a graph that may be read by another handle.
            /// </summary>
            internal void EnsureWritableGraph()
            {
                if (m_graph.HasOtherOwner(this))
                {
                    m_graph = CopyGraph(m_graph);
                    m_graph.Track(this);
                }
            }

            /// <summary>Indicates that another live storage handle shares the registration graph.</summary>
            internal bool IsShared => m_graph.HasOtherOwner(this);

            /// <summary>Checks whether this storage still uses the specified graph.</summary>
            /// <param name="graph">The graph to check.</param>
            internal bool UsesGraph(RegistrationGraph graph) => ReferenceEquals(m_graph, graph);

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

            /// <summary>Checks whether another live storage handle references this graph.</summary>
            /// <param name="storage">The storage handle making the check.</param>
            internal bool HasOtherOwner(Storage storage)
            {
                lock (m_ownerLock)
                {
                    for (int index = m_owners.Count - 1; index >= 0; index--)
                    {
                        if (!m_owners[index].TryGetTarget(out Storage? current) ||
                            !current.UsesGraph(this))
                        {
                            m_owners.RemoveAt(index);
                            continue;
                        }
                        if (!ReferenceEquals(current, storage))
                        {
                            return true;
                        }
                    }
                    return false;
                }
            }

            /// <summary>Tracks a storage handle that currently references this graph.</summary>
            /// <param name="storage">The storage handle.</param>
            internal void Track(Storage storage)
            {
                lock (m_ownerLock)
                {
                    for (int index = m_owners.Count - 1; index >= 0; index--)
                    {
                        if (!m_owners[index].TryGetTarget(out Storage? current) ||
                            !current.UsesGraph(this))
                        {
                            m_owners.RemoveAt(index);
                            continue;
                        }
                        if (ReferenceEquals(current, storage))
                        {
                            return;
                        }
                    }
                    m_owners.Add(new WeakReference<Storage>(storage));
                }
            }

            private readonly Lock m_ownerLock = new();
            private readonly List<WeakReference<Storage>> m_owners = [];
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
