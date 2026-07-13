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
using System.Threading;

namespace Opc.Ua
{
    /// <summary>
    /// Extensible registry for source-generated event-record decoders.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Generated per-model registration extensions
    /// (<c>Register{ModelPrefix}Decoders(this EventRecordDecoderRegistry)</c>)
    /// register one decoder per emitted event-record type. The
    /// registry composes the union of every registered decoder's
    /// <c>StandardFields</c> into a single <see cref="StandardFields"/>
    /// array that drives event filter construction, and routes
    /// <see cref="Decode"/> calls to the most specific registered
    /// decoder using the event's <c>EventType</c> field with an
    /// optional super-type fallback walk.
    /// </para>
    /// <para>
    /// The <see cref="Default"/> singleton is the process-wide
    /// registry intended for the simple case. Tests and applications
    /// that need isolation should construct a fresh
    /// <see cref="EventRecordDecoderRegistry"/> or derive a child
    /// scope from <see cref="Default"/> via
    /// <see cref="CreateChildScope"/>.
    /// </para>
    /// <para>
    /// All operations are thread-safe. Registrations may happen
    /// concurrently with reads, though the composed
    /// <see cref="StandardFields"/> array is rebuilt lazily after
    /// every registration so callers that read it should treat the
    /// returned array as a snapshot.
    /// </para>
    /// </remarks>
    public sealed class EventRecordDecoderRegistry
    {
        private static readonly Lazy<EventRecordDecoderRegistry> s_default =
            new(CreateDefault, LazyThreadSafetyMode.ExecutionAndPublication);

        private readonly EventRecordDecoderRegistry? m_parent;
        private readonly Lock m_lock = new();
        private readonly Dictionary<NodeId, Entry> m_decoders = [];
        private QualifiedName[][]? m_composedFields;

        /// <summary>
        /// Initializes a new empty registry.
        /// </summary>
        public EventRecordDecoderRegistry()
            : this(parent: null)
        {
        }

        private EventRecordDecoderRegistry(EventRecordDecoderRegistry? parent)
        {
            m_parent = parent;
        }

        /// <summary>
        /// Process-wide default registry. Pre-populated with every
        /// standard UA model decoder discovered via the static
        /// <see cref="DefaultRegistrar"/> hook (the generated
        /// <c>RegisterOpcUaDecoders</c> extension wires itself
        /// into this hook).
        /// </summary>
        public static EventRecordDecoderRegistry Default => s_default.Value;

        /// <summary>
        /// Action invoked exactly once when <see cref="Default"/> is
        /// first accessed. The generated
        /// <c>RegisterOpcUaDecoders</c> extension method (and any
        /// other model's registration extension that wishes to
        /// pre-populate the default registry) appends to this
        /// delegate. Must not be modified after
        /// <see cref="Default"/> has been observed for the first
        /// time — set during static initialization only.
        /// </summary>
        public static Action<EventRecordDecoderRegistry>? DefaultRegistrar { get; set; }

        /// <summary>
        /// Optional super-type resolver used by <see cref="Decode"/>
        /// when the exact <c>EventType</c> is not registered. A
        /// typical resolver consults the session's
        /// <see cref="ITypeTable"/> to look up the immediate parent
        /// type id. Returning <c>null</c> stops the fallback walk.
        /// </summary>
        public Func<NodeId, NodeId?>? SuperTypeResolver { get; set; }

        /// <summary>
        /// Creates a child registry that inherits every registration
        /// from this registry (parent reads are transparent). New
        /// registrations on the child do not affect the parent.
        /// </summary>
        public EventRecordDecoderRegistry CreateChildScope()
        {
            return new(parent: this) { SuperTypeResolver = SuperTypeResolver };
        }

        /// <summary>
        /// Registers a decoder for the given event type. Throws if
        /// a decoder for the same type id is already registered.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        public EventRecordDecoderRegistry Register(
            NodeId eventTypeId,
            QualifiedName[][] standardFields,
            Func<IReadOnlyList<Variant>, EventRecord?> decode)
        {
            if (!TryRegister(eventTypeId, standardFields, decode))
            {
                throw new InvalidOperationException(
                    $"A decoder is already registered for event type {eventTypeId}.");
            }
            return this;
        }

        /// <summary>
        /// Idempotent counterpart to <see cref="Register"/>. Returns
        /// <c>true</c> when the registration was added; <c>false</c>
        /// when the registry already contained a decoder for the
        /// same type id. Intended for inline registration patterns
        /// where duplicate calls are safe.
        /// </summary>
        /// <exception cref="ArgumentException"></exception>
        /// <exception cref="ArgumentNullException"><paramref name="standardFields"/> is <c>null</c>.</exception>
        public bool TryRegister(
            NodeId eventTypeId,
            QualifiedName[][] standardFields,
            Func<IReadOnlyList<Variant>, EventRecord?> decode)
        {
            if (eventTypeId.IsNull)
            {
                throw new ArgumentException(
                    "Event type id must not be null.", nameof(eventTypeId));
            }
            if (standardFields == null)
            {
                throw new ArgumentNullException(nameof(standardFields));
            }
            if (decode == null)
            {
                throw new ArgumentNullException(nameof(decode));
            }

            lock (m_lock)
            {
                if (m_decoders.ContainsKey(eventTypeId))
                {
                    return false;
                }

                m_decoders[eventTypeId] = new Entry(standardFields, decode);
                m_composedFields = null;
                return true;
            }
        }

        /// <summary>
        /// Decodes <paramref name="fields"/> into an
        /// <see cref="EventRecord"/> by routing on the event's
        /// <c>EventType</c> field. The position of the
        /// <c>EventType</c> field is resolved structurally against
        /// <see cref="StandardFields"/>; the registry walks the
        /// parent-type chain via
        /// <see cref="SuperTypeResolver"/> if the exact type is not
        /// registered. Returns <c>null</c> when
        /// <paramref name="fields"/> is null/empty or no ancestor
        /// decoder is registered.
        /// </summary>
        public EventRecord? Decode(IReadOnlyList<Variant> fields)
        {
            if (fields == null || fields.Count == 0)
            {
                return null;
            }

            int eventTypeIndex = FindEventTypeIndex();
            if (eventTypeIndex < 0 || eventTypeIndex >= fields.Count)
            {
                return null;
            }

            if (!fields[eventTypeIndex].TryGetValue(out NodeId eventType) || eventType.IsNull)
            {
                return null;
            }

            return DecodeAs(eventType, fields);
        }

        /// <summary>
        /// Decodes <paramref name="fields"/> using the decoder
        /// registered for <paramref name="eventType"/> (or the
        /// closest registered ancestor). Returns <c>null</c> when
        /// no ancestor decoder is registered.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="fields"/> is <c>null</c>.</exception>
        public EventRecord? DecodeAs(NodeId eventType, IReadOnlyList<Variant> fields)
        {
            if (fields == null)
            {
                throw new ArgumentNullException(nameof(fields));
            }
            if (eventType.IsNull)
            {
                return null;
            }

            NodeId current = eventType;
            while (!current.IsNull)
            {
                if (TryGet(current, out Entry entry))
                {
                    IReadOnlyList<Variant> remapped = RemapFieldsForDecoder(entry, fields);
                    return entry.Decode(remapped);
                }
                NodeId? parent = SuperTypeResolver?.Invoke(current);
                if (!parent.HasValue || parent.Value.IsNull)
                {
                    return null;
                }
                current = parent.Value;
            }
            return null;
        }

        /// <summary>
        /// Composed filter superset. Returns the union of own +
        /// inherited (from <see cref="CreateChildScope"/> parent)
        /// decoder field paths, deduplicated by browse-path
        /// equality. Rebuilt lazily after every registration on
        /// this scope; thread-safe.
        /// </summary>
        public QualifiedName[][] StandardFields
        {
            get
            {
                lock (m_lock)
                {
                    return m_composedFields ??= ComposeFields();
                }
            }
        }

        private QualifiedName[][] ComposeFields()
        {
            var seen = new Dictionary<PathKey, int>();
            var result = new List<QualifiedName[]>();

            void Visit(EventRecordDecoderRegistry registry)
            {
                if (registry.m_parent != null)
                {
                    Visit(registry.m_parent);
                }
                // Snapshot the decoder list under the source
                // registry's lock so concurrent mutations on the
                // parent are observed atomically.
                List<Entry> entries;
                lock (registry.m_lock)
                {
                    entries = [.. registry.m_decoders.Values];
                }
                foreach (Entry entry in entries)
                {
                    foreach (QualifiedName[] path in entry.StandardFields)
                    {
                        if (!seen.ContainsKey(new PathKey(path)))
                        {
                            seen[new PathKey(path)] = result.Count;
                            result.Add(path);
                        }
                    }
                }
            }

            Visit(this);
            return [.. result];
        }

        /// <summary>
        /// Builds a decoder-local view of <paramref name="composedFields"/>
        /// so the decoder reads at its own positional layout even when
        /// the caller built the variant array against the registry's
        /// composed <see cref="StandardFields"/> superset.
        /// </summary>
        private Variant[] RemapFieldsForDecoder(
            Entry entry, IReadOnlyList<Variant> composedFields)
        {
            int[] remap = GetOrBuildRemap(entry);
            var local = new Variant[remap.Length];
            for (int i = 0; i < remap.Length; i++)
            {
                int composedIndex = remap[i];
                if (composedIndex >= 0 && composedIndex < composedFields.Count)
                {
                    local[i] = composedFields[composedIndex];
                }
            }
            return local;
        }

        private int[] GetOrBuildRemap(Entry entry)
        {
            lock (m_lock)
            {
                // Force composed-fields rebuild so the index map is
                // consistent with the latest StandardFields snapshot.
                QualifiedName[][] composed = m_composedFields ??= ComposeFields();
                if (entry.RemapVersion != composed.GetHashCode())
                {
                    entry.RemapTo = BuildRemap(entry.StandardFields, composed);
                    entry.RemapVersion = composed.GetHashCode();
                }
                return entry.RemapTo!;
            }
        }

        private static int[] BuildRemap(
            QualifiedName[][] localFields, QualifiedName[][] composedFields)
        {
            var composedIndex = new Dictionary<PathKey, int>(composedFields.Length);
            for (int i = 0; i < composedFields.Length; i++)
            {
                composedIndex[new PathKey(composedFields[i])] = i;
            }
            int[] remap = new int[localFields.Length];
            for (int i = 0; i < localFields.Length; i++)
            {
                remap[i] = composedIndex.TryGetValue(new PathKey(localFields[i]), out int idx)
                    ? idx : -1;
            }
            return remap;
        }

        private bool TryGet(NodeId eventTypeId, out Entry entry)
        {
            lock (m_lock)
            {
                if (m_decoders.TryGetValue(eventTypeId, out entry!))
                {
                    return true;
                }
            }
            return m_parent != null && m_parent.TryGet(eventTypeId, out entry);
        }

        private int FindEventTypeIndex()
        {
            // EventType is always at a stable position in the
            // composed StandardFields — find it once by browse path.
            QualifiedName[][] paths = StandardFields;
            for (int i = 0; i < paths.Length; i++)
            {
                QualifiedName[] path = paths[i];
                if (path.Length == 1 &&
                    path[0].NamespaceIndex == 0 &&
                    string.Equals(path[0].Name, BrowseNames.EventType, StringComparison.Ordinal))
                {
                    return i;
                }
            }
            return -1;
        }

        private static EventRecordDecoderRegistry CreateDefault()
        {
            var registry = new EventRecordDecoderRegistry();
            // The standard UA model's generated registration extension
            // lives in this same assembly (Opc.Ua.Core) — call it
            // directly so the Default registry always offers the
            // standard event-type decoders out of the box.
            registry.RegisterOpcUaDecoders();
            DefaultRegistrar?.Invoke(registry);
            return registry;
        }

        private sealed class Entry
        {
            public Entry(
                QualifiedName[][] standardFields,
                Func<IReadOnlyList<Variant>, EventRecord?> decode)
            {
                StandardFields = standardFields;
                Decode = decode;
            }

            public QualifiedName[][] StandardFields { get; }
            public Func<IReadOnlyList<Variant>, EventRecord?> Decode { get; }

            /// <summary>
            /// Composed-layout → local-layout remap cache. Rebuilt when
            /// RemapVersion differs from the composed-fields snapshot's
            /// identity hash.
            /// </summary>
            public int[]? RemapTo { get; set; }
            public int RemapVersion { get; set; } = -1;
        }

        /// <summary>
        /// Structural equality key for a browse path used to
        /// deduplicate the composed <see cref="StandardFields"/>.
        /// </summary>
        private readonly struct PathKey : IEquatable<PathKey>
        {
            private readonly QualifiedName[] m_segments;

            public PathKey(QualifiedName[] segments)
            {
                m_segments = segments;
            }

            public bool Equals(PathKey other)
            {
                if (m_segments.Length != other.m_segments.Length)
                {
                    return false;
                }
                for (int i = 0; i < m_segments.Length; i++)
                {
                    if (m_segments[i] != other.m_segments[i])
                    {
                        return false;
                    }
                }
                return true;
            }

            public override bool Equals(object? obj)
            {
                return obj is PathKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                var hash = new HashCode();
                foreach (QualifiedName segment in m_segments)
                {
                    hash.Add(segment);
                }
                return hash.ToHashCode();
            }
        }
    }
}
