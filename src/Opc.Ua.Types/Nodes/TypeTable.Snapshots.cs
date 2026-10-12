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
                TypeTable? view = m_viewSelector?.Invoke();
                return ReferenceEquals(view, this) ? null : view;
            }
        }

        /// <summary>
        /// Acquires publication ownership and selects the serving type image.
        /// </summary>
        /// <param name="selector">The current image selector.</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        internal void SetViewSelector(Func<TypeTable?> selector)
        {
            if (selector is null)
            {
                throw new ArgumentNullException(nameof(selector));
            }
            if (Interlocked.CompareExchange(ref m_viewSelector, selector, null) is not null)
            {
                throw new InvalidOperationException("The type table already has a publication owner.");
            }
        }

        /// <summary>
        /// Copies the type graph without sharing mutable hierarchy or encoding storage.
        /// </summary>
        /// <param name="source">The type table.</param>
        /// <param name="revision">The captured source revision.</param>
        internal TypeTable CaptureSnapshot(out TypeTable source, out long revision)
        {
            if (CurrentView is { } view)
            {
                return view.CaptureSnapshot(out source, out revision);
            }
            lock (m_lock)
            {
                source = this;
                revision = m_revision;
                var snapshot = new TypeTable(m_namespaceUris);
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
        /// Checks both the identity and revision of a captured source image.
        /// </summary>
        /// <param name="source">The type table.</param>
        /// <param name="revision">The revision observed during capture.</param>
        internal bool IsCurrentSnapshot(TypeTable source, long revision)
        {
            if (CurrentView is { } view)
            {
                return view.IsCurrentSnapshot(source, revision);
            }
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
            if (CurrentView is { } view)
            {
                return view.BeginPublication(source, revision);
            }
            lock (m_lock)
            {
                if (!ReferenceEquals(source, this) || revision != m_revision)
                {
                    throw new InvalidOperationException("Serving types changed after the batch was prepared.");
                }
                EnsureMutable();
                m_publicationPending = true;
                return new Publication(this);
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
        private long m_revision;
        private bool m_publicationPending;
        private bool m_retired;

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
