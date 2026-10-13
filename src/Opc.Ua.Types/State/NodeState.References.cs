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
using System.Linq;

namespace Opc.Ua
{
    /// <summary>
    /// The base class for custom nodes.
    /// </summary>
    public abstract partial class NodeState
    {
        private ReferenceSnapshot CurrentReferenceSnapshot =>
            m_referenceSelector?.Invoke(this) ?? m_referenceSnapshot ?? s_emptyReferenceSnapshot;

        /// <summary>
        /// Allocates per-node storage only for a mutation or publication owner.
        /// </summary>
        private ReferenceSnapshot GetMutableReferenceSnapshot()
        {
            return m_referenceSelector?.Invoke(this) ?? (m_referenceSnapshot ??= new ReferenceSnapshot());
        }

        /// <summary>
        /// Prepares isolated reference changes without publishing them or notifying observers.
        /// </summary>
        /// <param name="selector">The owner's serving-image selector.</param>
        /// <param name="additions">The references to add.</param>
        /// <param name="removals">The references to remove.</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        internal ReferenceUpdate PrepareReferences(
            Func<NodeState, ReferenceSnapshot?> selector,
            ArrayOf<IReference> additions,
            ArrayOf<IReference> removals)
        {
            if (selector is null)
            {
                throw new ArgumentNullException(nameof(selector));
            }
            lock (m_referencesLock)
            {
                if (m_referenceSelector is not null && m_referenceSelector != selector)
                {
                    throw new InvalidOperationException("The node references already have a publication owner.");
                }
                ReferenceSnapshot source = GetMutableReferenceSnapshot();
                source.EnsureMutable();
                ReferenceSnapshot next = source.Copy();
                var added = new List<IReference>();
                var removed = new List<IReference>();
                foreach (IReference reference in removals)
                {
                    if (next.References?.Remove(reference) == true)
                    {
                        removed.Add(CopyReference(reference));
                    }
                }
                foreach (IReference reference in additions)
                {
                    IReference copy = CopyReference(reference);
                    next.References ??= [];
                    if (!next.References.ContainsKey(copy))
                    {
                        next.References.Add(copy, null);
                        added.Add(copy);
                    }
                }
                m_referenceSelector = selector;
                return new ReferenceUpdate(this, source, next, [.. added], [.. removed]);
            }
        }

        /// <summary>
        /// Releases the selected image into independent node-owned reference storage.
        /// </summary>
        /// <param name="selector">The owner's serving-image selector.</param>
        /// <param name="image">The reference snapshot.</param>
        /// <exception cref="InvalidOperationException"></exception>
        internal void ReleaseReferenceView(
            Func<NodeState, ReferenceSnapshot?> selector,
            ReferenceSnapshot? image)
        {
            lock (m_referencesLock)
            {
                if (m_referenceSelector != selector)
                {
                    throw new InvalidOperationException("The node reference publication owner changed.");
                }
                image ??= m_referenceSnapshot ??
                    throw new InvalidOperationException("The node has no owned reference image.");
                image.EnsureMutable();
                m_referenceSnapshot = image.Copy();
                image.Retired = true;
                m_referenceSelector = null;
            }
        }

        private ReferenceDictionary<object?> GetWritableReferences()
        {
            ReferenceSnapshot image = GetMutableReferenceSnapshot();
            image.EnsureMutable();
            image.Revision++;
            return image.References ??= [];
        }

        private void ResetReferences()
        {
            lock (m_referencesLock)
            {
                ReferenceSnapshot image = CurrentReferenceSnapshot;
                image.EnsureMutable();
                if (ReferenceEquals(image, s_emptyReferenceSnapshot))
                {
                    return;
                }
                image.Revision++;
                image.References = null;
            }
        }

        private IReference[]? GetReferenceKeys()
        {
            lock (m_referencesLock)
            {
                return CurrentReferenceSnapshot.References?.Keys.ToArray();
            }
        }

        private static NodeStateReference CopyReference(IReference reference)
        {
            if (reference is null)
            {
                throw new ArgumentNullException(nameof(reference));
            }
            if (reference.ReferenceTypeId.IsNull || reference.TargetId.IsNull)
            {
                throw new ArgumentException("A reference must have a type and target.", nameof(reference));
            }
            return new NodeStateReference(
                reference.ReferenceTypeId, reference.IsInverse, reference.TargetId,
                (reference as NodeStateReference)?.Target);
        }

        private ReferenceSnapshot? m_referenceSnapshot;
        private Func<NodeState, ReferenceSnapshot?>? m_referenceSelector;

        /// <summary>An empty read view that is never mutated or reserved.</summary>
        private static readonly ReferenceSnapshot s_emptyReferenceSnapshot = new();

        /// <summary>
        /// Holds an isolated reference table and its publication state.
        /// </summary>
        internal sealed class ReferenceSnapshot
        {
            /// <summary>
            /// Copies reference keys while preserving intentional internal target identities.
            /// </summary>
            internal ReferenceSnapshot Copy()
            {
                var copy = new ReferenceSnapshot();
                if (References is not null)
                {
                    copy.References = [];
                    foreach (IReference reference in References.Keys)
                    {
                        copy.References.Add(CopyReference(reference), null);
                    }
                }
                return copy;
            }

            /// <summary>
            /// Rejects writes to a reserved or retired image.
            /// </summary>
            /// <exception cref="InvalidOperationException"></exception>
            internal void EnsureMutable()
            {
                if (PublicationPending || Retired)
                {
                    throw new InvalidOperationException(
                        "The reference image belongs to a pending or completed publication and cannot be changed.");
                }
            }

            internal ReferenceDictionary<object?>? References;
            internal long Revision;

            /// <summary>
            /// The publication pending used by <c>ReferenceSnapshot</c>.
            /// </summary>
            internal bool PublicationPending;
            internal bool Retired;
        }

        /// <summary>
        /// Reserves a captured reference image and releases postpublication notifications once.
        /// </summary>
        internal sealed class ReferenceUpdate : IDisposable
        {
            /// <summary>
            /// Initializes a new <c>ReferenceUpdate</c> instance.
            /// </summary>
            /// <param name="owner">The node state used to initialize the instance.</param>
            /// <param name="source">The reference snapshot used to initialize the instance.</param>
            /// <param name="next">The next used to initialize the instance.</param>
            /// <param name="added">The added used to initialize the instance.</param>
            /// <param name="removed">The removed used to initialize the instance.</param>
            internal ReferenceUpdate(
                NodeState owner,
                ReferenceSnapshot source,
                ReferenceSnapshot next,
                ArrayOf<IReference> added,
                ArrayOf<IReference> removed)
            {
                m_owner = owner;
                m_source = source;
                m_revision = source.Revision;
                Next = next;
                m_added = added;
                m_removed = removed;
            }

            /// <summary>
            /// Gets the prepared image to select at publication.
            /// </summary>
            internal ReferenceSnapshot Next { get; }

            /// <summary>
            /// Reserves the unchanged source so no serving mutation can invalidate preparation.
            /// </summary>
            /// <exception cref="InvalidOperationException"></exception>
            internal void Reserve()
            {
                lock (m_owner.m_referencesLock)
                {
                    if (m_reserved || m_completed || m_disposed)
                    {
                        throw new InvalidOperationException("The reference publication is no longer preparable.");
                    }
                    if (!ReferenceEquals(m_owner.CurrentReferenceSnapshot, m_source) ||
                        m_source.Revision != m_revision)
                    {
                        throw new InvalidOperationException("Serving references changed during batch preparation.");
                    }
                    m_source.EnsureMutable();
                    m_source.PublicationPending = true;
                    m_reserved = true;
                }
            }

            /// <summary>
            /// Retires the source after its replacement has been selected.
            /// </summary>
            /// <exception cref="InvalidOperationException"></exception>
            internal void Complete()
            {
                lock (m_owner.m_referencesLock)
                {
                    if (!m_reserved || m_disposed)
                    {
                        throw new InvalidOperationException("The reference publication has not been reserved.");
                    }
                    m_source.Retired = true;
                    m_completed = true;
                }
            }

            /// <summary>
            /// Releases notifications once after publication, reporting each observer failure.
            /// </summary>
            /// <param name="reportFailure">The callback for reporting observer failures.</param>
            /// <exception cref="InvalidOperationException"></exception>
            internal void Notify(Action<Exception> reportFailure)
            {
                lock (m_owner.m_referencesLock)
                {
                    if (!m_completed)
                    {
                        throw new InvalidOperationException("Private reference changes cannot release notifications.");
                    }
                    if (m_notified)
                    {
                        return;
                    }
                    m_notified = true;
                }
                if (m_added.Count != 0 || m_removed.Count != 0)
                {
                    m_owner.m_changeMasks |= NodeStateChangeMasks.References;
                }
                foreach (IReference reference in m_removed)
                {
                    try
                    {
                        m_owner.OnReferenceRemoved?.Invoke(
                            m_owner, reference.ReferenceTypeId, reference.IsInverse, reference.TargetId);
                    }
                    catch (Exception failure) when (failure is not OutOfMemoryException)
                    {
                        reportFailure(failure);
                    }
                }
                foreach (IReference reference in m_added)
                {
                    try
                    {
                        m_owner.OnReferenceAdded?.Invoke(
                            m_owner, reference.ReferenceTypeId, reference.IsInverse, reference.TargetId);
                    }
                    catch (Exception failure) when (failure is not OutOfMemoryException)
                    {
                        reportFailure(failure);
                    }
                }
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                lock (m_owner.m_referencesLock)
                {
                    if (m_reserved)
                    {
                        m_source.PublicationPending = false;
                        m_reserved = false;
                    }
                    m_disposed = true;
                }
            }

            private readonly NodeState m_owner;
            private readonly ReferenceSnapshot m_source;
            private readonly long m_revision;
            private readonly ArrayOf<IReference> m_added;
            private readonly ArrayOf<IReference> m_removed;
            private bool m_reserved;
            private bool m_completed;
            private bool m_disposed;
            private bool m_notified;
        }
    }
}
