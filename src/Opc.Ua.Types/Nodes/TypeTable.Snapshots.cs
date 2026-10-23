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
        private TypeTable? CurrentView
        {
            get
            {
                TypeTable? view = Volatile.Read(ref m_viewSelector)?.Invoke();
                return ReferenceEquals(view, this) ? null : view;
            }
        }

        /// <summary>
        /// Acquires publication ownership and selects the serving type image.
        /// </summary>
        /// <param name="selector">The current image selector.</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        internal ViewOwner SetViewSelector(Func<TypeTable?> selector)
        {
            return TryAcquireViewOwner(selector) ??
                throw new InvalidOperationException("The type table already has a publication owner.");
        }

        /// <summary>
        /// Atomically acquires publication ownership when no owner exists.
        /// </summary>
        /// <param name="selector">The current image selector.</param>
        /// <returns>The owner token, or <c>null</c> when another owner holds the table.</returns>
        /// <exception cref="ArgumentNullException"></exception>
        internal ViewOwner? TryAcquireViewOwner(Func<TypeTable?> selector)
        {
            if (selector is null)
            {
                throw new ArgumentNullException(nameof(selector));
            }
            lock (m_lock)
            {
                if (m_viewSelector is not null)
                {
                    return null;
                }
                Volatile.Write(ref m_viewSelector, selector);
                return new ViewOwner(this, selector);
            }
        }

        /// <summary>
        /// Gets whether a publication owner currently selects the serving image.
        /// </summary>
        internal bool HasViewOwner => Volatile.Read(ref m_viewSelector) is not null;

        /// <summary>
        /// Copies the type graph without sharing mutable hierarchy or encoding storage.
        /// </summary>
        /// <param name="source">The type table.</param>
        /// <param name="revision">The captured source revision.</param>
        internal TypeTable CaptureSnapshot(out TypeTable source, out long revision)
        {
            lock (m_lock)
            {
                if (CurrentView is { } view)
                {
                    return view.CaptureSnapshot(out source, out revision);
                }
                source = this;
                revision = m_revision;
                var snapshot = new TypeTable(m_namespaceUris)
                {
                    m_captureSource = new WeakReference<TypeTable>(this),
                    m_captureRevision = m_revision
                };
                var copies = new Dictionary<TypeInfo, TypeInfo>();
                var pending = new Queue<TypeInfo>();
                foreach (KeyValuePair<NodeId, TypeInfo> entry in m_nodes)
                {
                    snapshot.m_nodes.Add(entry.Key, Copy(entry.Value));
                }
                foreach (KeyValuePair<NodeId, TypeInfo> entry in m_encodings)
                {
                    snapshot.m_encodings.Add(entry.Key, Copy(entry.Value));
                }
                foreach (KeyValuePair<QualifiedName, TypeInfo> entry in m_referenceTypes)
                {
                    snapshot.m_referenceTypes.Add(entry.Key, Copy(entry.Value));
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
                return snapshot;

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
                throw new InvalidOperationException("Serving types changed after the batch was prepared.");
            }
            if (additions.IsUnchangedCapture(source, revision))
            {
                return merged;
            }
            TypeTable proposed = additions.CaptureSnapshot(out _, out _);
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
                if (CurrentView is { } view)
                {
                    return view.IsCurrentSnapshot(source, revision);
                }
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
                if (CurrentView is { } view)
                {
                    return view.BeginPublication(source, revision);
                }
                if (!ReferenceEquals(source, this) || revision != m_revision)
                {
                    throw new InvalidOperationException("Serving types changed after the batch was prepared.");
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
                if (CurrentView is { } view)
                {
                    return view.IsUnchangedCapture(source, revision);
                }
                return m_captureSource is { } origin &&
                    origin.TryGetTarget(out TypeTable? captured) &&
                    ReferenceEquals(captured, source) &&
                    m_captureRevision == revision &&
                    m_revision == 0;
            }
        }

        private void EnsureMutable()
        {
            if (m_publicationPending || m_retired)
            {
                throw new InvalidOperationException(
                    "The type image belongs to a pending or completed publication and cannot be changed.");
            }
        }

        private Func<TypeTable?>? m_viewSelector;

        /// <summary>Weak provenance avoids retaining earlier serving graphs through prepared copies.</summary>
        private WeakReference<TypeTable>? m_captureSource;

        /// <summary>The serving revision from which this private graph was copied.</summary>
        private long m_captureRevision;
        private long m_revision;
        private bool m_publicationPending;
        private bool m_retired;

        /// <summary>
        /// Retains exclusive ownership of the type table's serving-image selector.
        /// </summary>
        /// <param name="owner">The type table.</param>
        /// <param name="selector">The owned image selector.</param>
        internal sealed class ViewOwner(TypeTable owner, Func<TypeTable?> selector)
        {
            /// <summary>
            /// Releases ownership after transferring the last published hierarchy back to the table.
            /// The call is idempotent and a stale token never clears a later owner's selector.
            /// </summary>
            /// <param name="image">The serving image whose hierarchy is preserved.</param>
            /// <exception cref="ArgumentNullException"></exception>
            /// <exception cref="InvalidOperationException"></exception>
            public void Release(TypeTable image)
            {
                if (image is null)
                {
                    throw new ArgumentNullException(nameof(image));
                }
                TypeTable? current = Volatile.Read(ref m_owner);
                if (current is null)
                {
                    return;
                }
                lock (current.m_lock)
                {
                    lock (image.m_lock)
                    {
                        if (m_owner is null)
                        {
                            return;
                        }
                        if (!ReferenceEquals(current.m_viewSelector, selector))
                        {
                            throw new InvalidOperationException("The type table publication owner changed.");
                        }
                        if (!ReferenceEquals(image, current))
                        {
                            image.EnsureMutable();
                            current.m_nodes.Clear();
                            current.m_encodings.Clear();
                            current.m_referenceTypes.Clear();
                            foreach (KeyValuePair<NodeId, TypeInfo> entry in image.m_nodes)
                            {
                                current.m_nodes[entry.Key] = entry.Value;
                            }
                            foreach (KeyValuePair<NodeId, TypeInfo> entry in image.m_encodings)
                            {
                                current.m_encodings[entry.Key] = entry.Value;
                            }
                            foreach (KeyValuePair<QualifiedName, TypeInfo> entry in image.m_referenceTypes)
                            {
                                current.m_referenceTypes[entry.Key] = entry.Value;
                            }
                            current.m_revision++;
                            image.m_retired = true;
                        }
                        Volatile.Write(ref current.m_viewSelector, null);
                        Volatile.Write(ref m_owner, null);
                    }
                }
            }

            private TypeTable? m_owner = owner;
        }

        /// <summary>
        /// Reserves type mutations and retires the source after successful publication.
        /// </summary>
        /// <param name="owner">The type table.</param>
        internal sealed class Publication(TypeTable owner) : IDisposable
        {
            /// <summary>
            /// Permanently retires the reserved source image.
            /// </summary>
            /// <exception cref="ObjectDisposedException"></exception>
            public void Complete()
            {
                TypeTable current = m_owner ?? throw new ObjectDisposedException(nameof(Publication));
                lock (current.m_lock)
                {
                    current.m_retired = true;
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
