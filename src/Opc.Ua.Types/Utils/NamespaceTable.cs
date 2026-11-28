// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Opc.Ua.Types;

namespace Opc.Ua
{
    /// <summary>
    /// A thread safe table of string constants.
    /// </summary>
    public class StringTable
    {
        /// <summary>
        /// Creates an empty collection.
        /// </summary>
        public StringTable()
        {
            m_strings = [];

#if DEBUG
            InstanceId = Interlocked.Increment(ref s_globalInstanceCount);
#endif
        }

        /// <summary>
        /// Creates an empty collection which is marked as shared.
        /// </summary>
        public StringTable(bool shared)
        {
            m_strings = [];

#if DEBUG
            m_shared = shared;
            InstanceId = Interlocked.Increment(ref s_globalInstanceCount);
#endif
        }

        /// <summary>
        /// Copies the table
        /// </summary>
        /// <param name="table"></param>
        public StringTable(StringTable table)
        {
            m_strings = [];
            Update(table.ToArray());
#if DEBUG
            InstanceId = Interlocked.Increment(ref s_globalInstanceCount);
#endif
        }

        /// <summary>
        /// Copies a list of strings.
        /// </summary>
        public StringTable(IEnumerable<string> strings)
        {
            m_strings = [];
            Update(strings);

#if DEBUG
            InstanceId = Interlocked.Increment(ref s_globalInstanceCount);
#endif
        }

#if DEBUG
        /// <summary>
        /// Returns a unique identifier for the table instance. Used to debug problems with shared tables.
        /// </summary>
        public int InstanceId { get; }
#endif

        /// <summary>
        /// Gets the monotonic mutation version of this table.
        /// </summary>
        public long Version
        {
            get
            {
                lock (m_syncRoot)
                {
                    return m_version;
                }
            }
        }

        /// <summary>
        /// Updates the table of namespace uris.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="strings"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException">More entries than a UInt16 index can address.</exception>
        public void Update(IEnumerable<string> strings)
        {
            if (strings == null)
            {
                throw new ArgumentNullException(nameof(strings));
            }

            // same bound as Append (see ThrowIfFull), so the (ushort) index casts cannot wrap.
            List<string> updated = [.. strings];

            if (updated.Count > ushort.MaxValue)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "The string table cannot hold more than {0} entries.",
                    ushort.MaxValue);
            }

            lock (m_syncRoot)
            {
                EnsureMutable();
                m_strings.Clear();
                m_strings.AddRange(updated);
                Interlocked.Increment(ref m_version);

#if DEBUG
                if (m_shared)
                {
                    for (int ii = 0; ii < m_strings.Count; ii++)
                    {
                        Debug.WriteLine(
                            "WARNING: Adding '{0}' to shared StringTable #{1}.",
                            m_strings[ii],
                            InstanceId);
                    }
                }
#endif
            }
        }

        /// <summary>
        /// Adds a string to the end of the table.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public int Append(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentNullException(nameof(value));
            }

#if DEBUG
            if (m_shared)
            {
                Debug.WriteLine(
                    "WARNING: Adding '{0}' to shared StringTable #{1}.",
                    value,
                    InstanceId);
            }
#endif

            lock (m_syncRoot)
            {
                EnsureMutable();
                ThrowIfFull();
                m_strings.Add(value);
                Interlocked.Increment(ref m_version);
                return m_strings.Count - 1;
            }
        }

        /// <summary>
        /// Throws if another entry would not fit a UInt16 index. The indexes of the table
        /// are namespace / server indexes (UInt16) and 0xFFFF is the "not mapped" marker,
        /// so an entry past index 0xFFFE would silently wrap onto an existing index.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void ThrowIfFull()
        {
            if (m_strings.Count >= ushort.MaxValue)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadEncodingLimitsExceeded,
                    "The string table cannot hold more than {0} entries.",
                    ushort.MaxValue);
            }
        }

        /// <summary>
        /// Returns the namespace uri at the specified index.
        /// </summary>
        public string? GetString(uint index)
        {
            lock (m_syncRoot)
            {
                if (index < m_strings.Count)
                {
                    return m_strings[(int)index];
                }

                return null;
            }
        }

        /// <summary>
        /// Returns the index of the specified namespace uri.
        /// </summary>
        public int GetIndex(string value)
        {
            return GetIndex(value, 0);
        }

        /// <summary>
        /// Returns the index of the specified namespace uri, adds it if it does not exist.
        /// </summary>
        /// <exception cref="ArgumentNullException"></exception>
        public ushort GetIndexOrAppend(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                throw new ArgumentNullException(nameof(value));
            }

            lock (m_syncRoot)
            {
                int index = m_strings.IndexOf(value);

                if (index == -1)
                {
                    EnsureMutable();
#if DEBUG
                    if (m_shared)
                    {
                        Debug.WriteLine(
                            "WARNING: Adding '{0}' to shared StringTable #{1}.",
                            value,
                            InstanceId);
                    }
#endif

                    ThrowIfFull();
                    m_strings.Add(value);
                    Interlocked.Increment(ref m_version);
                    return (ushort)(m_strings.Count - 1);
                }

                return (ushort)index;
            }
        }

        /// <summary>
        /// Returns the contexts of the table.
        /// </summary>
        public string[] ToArray()
        {
            lock (m_syncRoot)
            {
                return [.. m_strings];
            }
        }

        /// <summary>
        /// Returns the contexts of the table.
        /// </summary>
        public ArrayOf<string> ToArrayOf()
        {
            lock (m_syncRoot)
            {
                return [.. m_strings];
            }
        }

        /// <summary>
        /// Captures the mapping and its mutation version in one observation.
        /// </summary>
        public ArrayOf<string> GetSnapshot(out long version)
        {
            lock (m_syncRoot)
            {
                version = m_version;
                return [.. m_strings];
            }
        }

        /// <summary>
        /// Returns the number of entries in the table.
        /// </summary>
        public int Count
        {
            get
            {
                lock (m_syncRoot)
                {
                    return m_strings.Count;
                }
            }
        }

        /// <summary>
        /// Creates a mapping between the URIs in a source table and the indexes in the current table.
        /// </summary>
        /// <param name="source">The string table to map.</param>
        /// <param name="updateTable">
        /// If set to <c>true</c>, missing URIs should be added to the current tables.
        /// </param>
        /// <returns>A list of indexes in the current table.</returns>
        public ushort[]? CreateMapping(StringTable source, bool updateTable)
        {
            return CreateMapping(source, updateTable, preserveLocalServerIndex: false);
        }

        /// <summary>
        /// Maps tables with an optional reserved local server slot at index zero.
        /// </summary>
        /// <remarks>
        /// An initially empty destination reserves index zero with an empty local placeholder
        /// before appending any remote server URI. Read-only mapping never changes either table.
        /// </remarks>
        /// <param name="source">The string table.</param>
        /// <param name="updateTable">The update-table flag supplied by the caller.</param>
        /// <param name="preserveLocalServerIndex">The preserve-local-server-index flag supplied by the caller.</param>
        internal ushort[]? CreateMapping(StringTable source, bool updateTable, bool preserveLocalServerIndex)
        {
            if (source == null)
            {
                return null;
            }

            ushort[] mapping = new ushort[source.Count];
            int startIndex = preserveLocalServerIndex ? 1 : 0;

            for (int ii = startIndex; ii < source.Count; ii++)
            {
                string uri = source.GetString((uint)ii)!;

                int index = GetIndex(uri, startIndex);

                if (index < 0)
                {
                    if (!updateTable)
                    {
                        mapping[ii] = ushort.MaxValue;
                        continue;
                    }

                    if (preserveLocalServerIndex)
                    {
                        EnsureLocalServerSlot();
                    }
                    index = Append(uri);
                }

                mapping[ii] = (ushort)index;
            }

            return mapping;
        }

        /// <summary>
        /// Reserves a local-only slot without relaxing public URI validation or replacing live entries.
        /// </summary>
        private void EnsureLocalServerSlot()
        {
            lock (m_syncRoot)
            {
                if (m_strings.Count == 0)
                {
                    EnsureMutable();
                    m_strings.Add(string.Empty);
                    Interlocked.Increment(ref m_version);
                }
            }
        }

        private int GetIndex(string value, int startIndex)
        {
            lock (m_syncRoot)
            {
                if (string.IsNullOrEmpty(value))
                {
                    return -1;
                }

                return m_strings.IndexOf(value, Math.Min(startIndex, m_strings.Count));
            }
        }

        /// <summary>
        /// Access the string table from sub classes
        /// </summary>
        protected List<string> m_strings;
        private Lock m_syncRoot => m_storage.Gate;
        private ref long m_version => ref m_storage.Version;
        private Storage m_storage = new();

#if DEBUG
        /// <summary>
        /// Whether the table is shared.
        /// </summary>
        protected bool m_shared;
        private static int s_globalInstanceCount;
#endif

        /// <summary>
        /// Rebinds a staging table while preserving the protected list field for external subclasses.
        /// </summary>
        internal void BindTo(StringTable canonical)
        {
            m_strings = canonical.m_strings;
            m_storage = canonical.m_storage;
        }

        /// <summary>
        /// Detaches a cached table before an unpublished or retired manager is cleaned up.
        /// </summary>
        internal void Detach()
        {
            ArrayOf<string> entries = GetSnapshot(out long version);
            m_strings = [.. entries];
            m_storage = new Storage { Version = version };
        }

        /// <summary>
        /// Captures explicit private namespace storage and its source revision without ambient selection.
        /// </summary>
        internal StringTable CaptureSnapshot()
        {
            string[] entries = GetSnapshot(out long version).ToArray()!;
            StringTable copy = this is NamespaceTable ? new NamespaceTable(entries) : new StringTable(entries);
            copy.m_version = version;
            return copy;
        }

        /// <summary>
        /// Reserves an unchanged namespace prefix and preallocates additions before the decision callback.
        /// </summary>
        /// <exception cref="OptimisticPublicationConflictException">
        /// The serving namespace prefix changed after preparation.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Another publication already reserves the namespace prefix.
        /// </exception>
        internal Publication BeginPublication(StringTable additions, long revision)
        {
            return BeginPublication(additions, revision, rebase: false);
        }

        /// <summary>
        /// Reserves the namespace prefix a publication was prepared against and preallocates its additions.
        /// </summary>
        /// <remarks>
        /// Without <paramref name="rebase"/> the table must be unchanged since preparation. With it, entries
        /// appended meanwhile are accepted as long as every index the prepared table uses still maps to the same
        /// string; two publications that appended different strings at the same index conflict.
        /// </remarks>
        /// <param name="additions">The prepared table, the captured entries followed by its additions.</param>
        /// <param name="revision">The revision observed during capture.</param>
        /// <param name="rebase">Whether entries appended after preparation are accepted.</param>
        /// <exception cref="OptimisticPublicationConflictException">
        /// The serving namespace prefix changed after preparation.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// Another publication already reserves the namespace prefix.
        /// </exception>
        internal Publication BeginPublication(StringTable additions, long revision, bool rebase)
        {
            string[] proposed = additions.ToArray();
            lock (m_syncRoot)
            {
                EnsureMutable();
                bool consistent = rebase
                    ? (proposed.Length >= m_strings.Count
                        ? m_strings.SequenceEqual(proposed.Take(m_strings.Count))
                        : proposed.SequenceEqual(m_strings.Take(proposed.Length)))
                    : m_version == revision && proposed.Length >= m_strings.Count &&
                        m_strings.SequenceEqual(proposed.Take(m_strings.Count));
                if (!consistent)
                {
                    throw new OptimisticPublicationConflictException("The string table changed after preparation.");
                }
                if (m_strings.Capacity < proposed.Length)
                {
                    m_strings.Capacity = proposed.Length;
                }
                var publication = new Publication(this, proposed);
                m_storage.PublicationPending = publication;
                return publication;
            }
        }

        /// <summary>
        /// Rejects changes while a lifecycle decision reserves the namespace prefix.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// A publication reserves the namespace prefix.
        /// </exception>
        private void EnsureMutable()
        {
            if (m_storage.PublicationPending is not null)
            {
                throw new InvalidOperationException("The string table has a pending publication.");
            }
        }

        /// <summary>
        /// Shares synchronization and version state between cached namespace handles.
        /// </summary>
        private sealed class Storage
        {
            /// <summary>Serializes access through every bound table.</summary>
            internal readonly Lock Gate = new();

            /// <summary>Identifies the current string mapping revision.</summary>
            internal long Version;

            /// <summary>Reserves the captured namespace prefix during a decision.</summary>
            internal Publication? PublicationPending;
        }

        /// <summary>
        /// Holds a prevalidated append-only namespace publication.
        /// </summary>
        internal sealed class Publication(StringTable owner, string[] proposed) : IDisposable
        {
            /// <summary>
            /// Appends an unrelated URI under this exact reservation, preserving every staged index.
            /// Reserved URI entries become permanent if needed to reach the unrelated entry's index.
            /// </summary>
            /// <exception cref="ArgumentNullException">
            /// <paramref name="value"/> is null or empty.
            /// </exception>
            /// <exception cref="InvalidOperationException">
            /// This publication no longer owns the reservation.
            /// </exception>
            /// <exception cref="ServiceResultException">
            /// Appending the URI would exceed the namespace index limit.
            /// </exception>
            internal ushort GetIndexOrAppend(string value)
            {
                if (string.IsNullOrEmpty(value))
                {
                    throw new ArgumentNullException(nameof(value));
                }
                lock (owner.m_syncRoot)
                {
                    if (m_disposed || !ReferenceEquals(owner.m_storage.PublicationPending, this))
                    {
                        throw new InvalidOperationException("The string table publication is no longer reserved.");
                    }
                    int existing = owner.m_strings.IndexOf(value);
                    if (existing >= 0)
                    {
                        return (ushort)existing;
                    }
                    int nextIndex = Math.Max(owner.m_strings.Count, proposed.Length);
                    if (nextIndex >= ushort.MaxValue)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadEncodingLimitsExceeded,
                            "The string table cannot hold more than {0} entries.",
                            ushort.MaxValue);
                    }
                    if (owner.m_strings.Capacity <= nextIndex)
                    {
                        owner.m_strings.Capacity = nextIndex + 1;
                    }
                    Complete();
                    owner.m_strings.Add(value);
                    owner.m_version++;
                    return (ushort)nextIndex;
                }
            }

            /// <summary>
            /// Appends the reserved entries without renumbering any published namespace.
            /// </summary>
            internal void Complete()
            {
                lock (owner.m_syncRoot)
                {
                    for (int index = owner.m_strings.Count; index < proposed.Length; index++)
                    {
                        owner.m_strings.Add(proposed[index]);
                        owner.m_version++;
                    }
                }
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                lock (owner.m_syncRoot)
                {
                    if (m_disposed)
                    {
                        return;
                    }
                    owner.m_storage.PublicationPending = null;
                    m_disposed = true;
                }
            }

            private bool m_disposed;
        }
    }

    /// <summary>
    /// The table of namespace uris for a server.
    /// </summary>
    public class NamespaceTable : StringTable
    {
        /// <summary>
        /// Creates an empty collection.
        /// </summary>
        public NamespaceTable()
        {
            Append(Types.Namespaces.OpcUa);
        }

        /// <summary>
        /// Creates an empty collection which is marked as shared.
        /// </summary>
        public NamespaceTable(bool shared)
        {
            Append(Types.Namespaces.OpcUa);
#if DEBUG
            m_shared = shared;
#endif
        }

        /// <summary>
        /// Create a copy
        /// </summary>
        /// <param name="namespaceTable"></param>
        public NamespaceTable(NamespaceTable namespaceTable)
            : base(namespaceTable)
        {
            Debug.Assert(m_strings[0] == Types.Namespaces.OpcUa);
        }

        /// <summary>
        /// Copies a list of strings.
        /// </summary>
        public NamespaceTable(IEnumerable<string> namespaceUris)
        {
            Update(namespaceUris);
        }

        /// <summary>
        /// Updates the table of namespace uris.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="namespaceUris"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException"></exception>
        /// <param name="namespaceUris">The namespace URIs.</param>
        public new void Update(IEnumerable<string> namespaceUris)
        {
            if (namespaceUris == null)
            {
                throw new ArgumentNullException(nameof(namespaceUris));
            }

            base.Update(namespaceUris);

            // check that first entry is the UA namespace.
            if (GetString(0) != Namespaces.OpcUa)
            {
                throw new ArgumentException(
                    "The first namespace in the table must be the OPC-UA namespace.");
            }
        }
    }
}
