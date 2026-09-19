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
    public abstract partial class NodeState
    {
        private ReferenceSnapshot CurrentReferenceSnapshot =>
            m_referenceSelector?.Invoke(this) ?? m_referenceSnapshot;

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
                ReferenceSnapshot source = CurrentReferenceSnapshot;
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
                image ??= m_referenceSnapshot;
                image.EnsureMutable();
                m_referenceSnapshot = image.Copy();
                image.Retired = true;
                m_referenceSelector = null;
            }
        }

        private ReferenceDictionary<object?> GetWritableReferences()
        {
            ReferenceSnapshot image = CurrentReferenceSnapshot;
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

        private ReferenceSnapshot m_referenceSnapshot = new();
        private Func<NodeState, ReferenceSnapshot?>? m_referenceSelector;

        internal sealed class ReferenceSnapshot
        {
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
            internal bool PublicationPending;
            internal bool Retired;
        }

        internal sealed class ReferenceUpdate : IDisposable
        {
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

            internal ReferenceSnapshot Next { get; }

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
