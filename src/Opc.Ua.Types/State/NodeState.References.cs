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
        /// <summary>
        /// Precomputes ordered edits without selecting per-request reference storage.
        /// </summary>
        internal ReferenceUpdate PrepareReferences(ArrayOf<IReference> additions, ArrayOf<IReference> removals)
        {
            lock (m_referencesLock)
            {
                EnsureReferencesMutable();
                ReferenceDictionary<object?> afterRemovals = CopyReferences(m_references);
                var removed = new List<IReference>();
                foreach (IReference reference in removals)
                {
                    NodeStateReference copy = CopyReference(reference);
                    if (afterRemovals.Remove(copy))
                    {
                        removed.Add(copy);
                    }
                }
                ReferenceDictionary<object?> afterAdditions = CopyReferences(afterRemovals);
                var added = new List<IReference>();
                foreach (IReference reference in additions)
                {
                    NodeStateReference copy = CopyReference(reference);
                    if (!afterAdditions.ContainsKey(copy))
                    {
                        afterAdditions.Add(copy, null);
                        added.Add(copy);
                    }
                }
                return new ReferenceUpdate(
                    this, m_referenceRevision, afterRemovals, afterAdditions, [.. added], [.. removed]);
            }
        }

        /// <summary>
        /// Gets node-owned writable storage, allocated only on mutation.
        /// </summary>
        private ReferenceDictionary<object?> GetWritableReferences()
        {
            EnsureReferencesMutable();
            m_referenceRevision++;
            return m_references ??= [];
        }

        /// <summary>
        /// Resets node-owned reference storage.
        /// </summary>
        private void ResetReferences()
        {
            lock (m_referencesLock)
            {
                EnsureReferencesMutable();
                m_referenceRevision++;
                m_references = null;
            }
        }

        /// <summary>
        /// Copies keys for callers that enumerate without holding the node lock.
        /// </summary>
        private IReference[]? GetReferenceKeys()
        {
            lock (m_referencesLock)
            {
                return m_references?.Keys.ToArray();
            }
        }

        /// <summary>
        /// Rejects mutation of references reserved by a pending lifecycle decision.
        /// </summary>
        private void EnsureReferencesMutable()
        {
            if (m_referencePublicationPending)
            {
                throw new InvalidOperationException("The node references have a pending publication.");
            }
        }

        /// <summary>
        /// Copies and validates a reference before the durable decision.
        /// </summary>
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

        /// <summary>
        /// Materializes edits before publication; no reference comparer or user code runs during installation.
        /// </summary>
        private static ReferenceDictionary<object?> CopyReferences(ReferenceDictionary<object?>? source)
        {
            var copy = new ReferenceDictionary<object?>();
            if (source is not null)
            {
                foreach (IReference reference in source.Keys)
                {
                    copy.Add(CopyReference(reference), null);
                }
            }
            return copy;
        }

        /// <summary>Stores references directly on the node and remains lazy for empty nodes.</summary>
        private ReferenceDictionary<object?>? m_references;

        /// <summary>Detects changes between preparing and reserving reference edits.</summary>
        private long m_referenceRevision;

        /// <summary>Rejects writes while an undecided publication reserves this node.</summary>
        private bool m_referencePublicationPending;

        /// <summary>
        /// Reserves direct node-owned reference edits until publication or abandonment.
        /// </summary>
        internal sealed class ReferenceUpdate(
            NodeState owner,
            long revision,
            ReferenceDictionary<object?> afterRemovals,
            ReferenceDictionary<object?> afterAdditions,
            ArrayOf<IReference> added,
            ArrayOf<IReference> removed) : IDisposable
        {
            /// <summary>
            /// Validates and reserves the node before the decision callback.
            /// </summary>
            internal void Reserve()
            {
                lock (owner.m_referencesLock)
                {
                    if (m_reserved || m_completed || m_disposed || owner.m_referenceRevision != revision)
                    {
                        throw new InvalidOperationException("Serving references changed during preparation.");
                    }
                    owner.EnsureReferencesMutable();
                    owner.m_referencePublicationPending = true;
                    m_reserved = true;
                }
            }

            /// <summary>
            /// Removes dropped targets before routes can withdraw their owners.
            /// </summary>
            internal void Remove()
            {
                lock (owner.m_referencesLock)
                {
                    if (!m_reserved || m_disposed || m_removalsApplied)
                    {
                        throw new InvalidOperationException("Reference removals require an unused reservation.");
                    }
                    owner.m_references = afterRemovals;
                    owner.m_referenceRevision++;
                    m_removalsApplied = true;
                }
            }

            /// <summary>
            /// Installs additions only after their target owners have become visible.
            /// </summary>
            internal void Complete()
            {
                lock (owner.m_referencesLock)
                {
                    if (!m_reserved || m_disposed || !m_removalsApplied || m_completed)
                    {
                        throw new InvalidOperationException("Reference additions must follow reserved removals.");
                    }
                    owner.m_references = afterAdditions;
                    owner.m_referenceRevision++;
                    m_completed = true;
                }
            }

            /// <summary>
            /// Notifies observers after routing and reference publication, never during the swap.
            /// </summary>
            internal void Notify(Action<Exception> reportFailure)
            {
                lock (owner.m_referencesLock)
                {
                    if (!m_completed)
                    {
                        throw new InvalidOperationException("Private reference changes cannot notify observers.");
                    }
                    if (m_notified)
                    {
                        return;
                    }
                    m_notified = true;
                }
                if (added.Count != 0 || removed.Count != 0)
                {
                    owner.m_changeMasks |= NodeStateChangeMasks.References;
                }
                foreach (IReference reference in removed)
                {
                    try
                    {
                        owner.OnReferenceRemoved?.Invoke(
                            owner, reference.ReferenceTypeId, reference.IsInverse, reference.TargetId);
                    }
                    catch (Exception failure) when (failure is not OutOfMemoryException)
                    {
                        reportFailure(failure);
                    }
                }
                foreach (IReference reference in added)
                {
                    try
                    {
                        owner.OnReferenceAdded?.Invoke(
                            owner, reference.ReferenceTypeId, reference.IsInverse, reference.TargetId);
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
                lock (owner.m_referencesLock)
                {
                    if (m_reserved)
                    {
                        owner.m_referencePublicationPending = false;
                        m_reserved = false;
                    }
                    m_disposed = true;
                }
            }

            /// <summary>Tracks ownership of the node's mutation reservation.</summary>
            private bool m_reserved;

            /// <summary>Tracks the ordered removal phase.</summary>
            private bool m_removalsApplied;

            /// <summary>Tracks installation of the post-publication additions.</summary>
            private bool m_completed;

            /// <summary>Prevents reuse after releasing the reservation.</summary>
            private bool m_disposed;

            /// <summary>Prevents duplicate observer callbacks.</summary>
            private bool m_notified;
        }
    }
}
