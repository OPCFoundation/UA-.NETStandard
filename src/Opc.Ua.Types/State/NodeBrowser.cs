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

namespace Opc.Ua
{
    /// <summary>
    /// An interface to an object that browses the references of an node.
    /// </summary>
    /// <remarks>
    /// A browser is single-consumer: it is owned by whoever created it and must not be used
    /// from more than one thread at a time. A browser that outlives a single service call -
    /// one parked in a continuation point for <c>BrowseNext</c>, for example - is serialized
    /// by its owner, not by the browser itself.
    /// </remarks>
    public interface INodeBrowser : IDisposable
    {
        /// <summary>
        /// Returns the next reference.
        /// </summary>
        IReference? Next();

        /// <summary>
        /// Pushes a previously returned reference back into the browser.
        /// </summary>
        void Push(IReference reference);
    }

    /// <summary>
    /// An object which browses the references for a node.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Instances are single-consumer, as described on <see cref="INodeBrowser"/>: the type
    /// performs no synchronization of its own and a derived browser is not expected to add
    /// any. Both server browse paths already serialize access on the owning side, so the
    /// former <c>DataLock</c> only added an inheritance-level locking contract that no
    /// derived type could reason about.
    /// </para>
    /// <para>
    /// The reference set is copied into the browser while the owning <see cref="NodeState"/>
    /// holds its browse lock, so the browser is a point-in-time copy: changes made to the node
    /// afterwards do not appear in it. It is not an atomic snapshot across the node's
    /// collections - see <see cref="NodeState.CreateBrowser"/> for what is and is not
    /// guaranteed. A derived browser that reaches an underlying system does so lazily in
    /// <see cref="Next"/>, outside any node lock.
    /// </para>
    /// </remarks>
    public class NodeBrowser : INodeBrowser
    {
        /// <summary>
        /// Creates a new browser object with a set of filters.
        /// </summary>
        public NodeBrowser(
            ISystemContext context,
            ViewDescription? view,
            NodeId referenceType,
            bool includeSubtypes,
            BrowseDirection browseDirection,
            QualifiedName browseName,
            IEnumerable<IReference>? additionalReferences,
            bool internalOnly,
            bool allowDuplicateReferences = false)
        {
            SystemContext = context;
            View = view;
            ReferenceType = referenceType;
            IncludeSubtypes = includeSubtypes;
            BrowseDirection = browseDirection;
            BrowseName = browseName;
            InternalOnly = internalOnly;
            m_references = [];
            m_seenReferences = allowDuplicateReferences ?
                null :
                new(ReferenceEqualityComparer.Default);

            // add any additional references if they meet the criteria.
            if (additionalReferences != null)
            {
                foreach (IReference reference in additionalReferences)
                {
                    if (IsRequired(reference.ReferenceTypeId, reference.IsInverse))
                    {
                        AddReference(reference);
                    }
                }
            }
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            // nothing to do.
        }

        /// <summary>
        /// Returns the next reference. Null if no more references.
        /// </summary>
        public virtual IReference? Next()
        {
            IReference reference;

            // always return the previous pushed reference first.
            if (m_pushBack != null)
            {
                reference = m_pushBack;
                m_pushBack = null;
                return reference;
            }

            if (m_index < m_references.Count)
            {
                return m_references[m_index++];
            }

            return null;
        }

        /// <summary>
        /// Pushes a previously returned reference back into the browser.
        /// </summary>
        public virtual void Push(IReference reference)
        {
            m_pushBack = reference;
        }

        /// <summary>
        /// Returns true if the target node is required (used to apply view filters);
        /// </summary>
        public virtual bool IsRequired(NodeState target)
        {
            return true;
        }

        /// <summary>
        /// Returns true if the reference type is required.
        /// </summary>
        public virtual bool IsRequired(NodeId referenceType, bool isInverse)
        {
            if (referenceType.IsNull)
            {
                return false;
            }

            // easiest to check inverse flag first.
            if (isInverse)
            {
                if (BrowseDirection == BrowseDirection.Forward)
                {
                    return false;
                }
            }
            else if (BrowseDirection == BrowseDirection.Inverse)
            {
                return false;
            }

            // check for no filter or exact match.
            if (ReferenceType.IsNull || referenceType == ReferenceType)
            {
                return true;
            }

            // check subtypes if possible.
            if (IncludeSubtypes &&
                SystemContext?.TypeTable != null &&
                SystemContext.TypeTable.IsTypeOf(referenceType, ReferenceType))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// Adds a reference to target entity.
        /// </summary>
        public virtual void Add(IReference reference)
        {
            AddReference(reference);
        }

        /// <summary>
        /// Adds a reference to target entity.
        /// </summary>
        /// <remarks>
        /// Will not add the reference if the browse name does not match the browse name filter.
        /// </remarks>
        public virtual void Add(NodeId referenceTypeId, bool isInverse, NodeState target)
        {
            // do not return add target unless the browse name matches.
            if (!BrowseName.IsNull && target.BrowseName != BrowseName)
            {
                return;
            }

            AddReference(new NodeStateReference(referenceTypeId, isInverse, target));
        }

        /// <summary>
        /// Adds a reference to target identified by its node id.
        /// </summary>
        public virtual void Add(NodeId referenceTypeId, bool isInverse, ExpandedNodeId targetId)
        {
            AddReference(new NodeStateReference(referenceTypeId, isInverse, targetId));
        }

        /// <summary>
        /// The table of types known to the UA server.
        /// </summary>
        public ISystemContext SystemContext { get; }

        /// <summary>
        /// The view being browsed.
        /// </summary>
        public ViewDescription? View { get; }

        /// <summary>
        /// The type of reference to return.
        /// </summary>
        public NodeId ReferenceType { get; }

        /// <summary>
        /// Whether to return subtypes of the reference.
        /// </summary>
        public bool IncludeSubtypes { get; }

        /// <summary>
        /// The direction for the references to return.
        /// </summary>
        public BrowseDirection BrowseDirection { get; }

        /// <summary>
        /// The browse name of the targets to return.
        /// </summary>
        public QualifiedName BrowseName { get; }

        /// <summary>
        /// Indicates that the browser only returned easy to access references stored in memory.
        /// </summary>
        public bool InternalOnly { get; }

        /// <summary>
        /// Indicates that the browser can return duplicate references during browse.
        /// </summary>
        public bool CanProduceDuplicateReferences => m_seenReferences == null;

        /// <summary>
        /// Ensure unique references are added.
        /// </summary>
        private void AddReference(IReference reference)
        {
            if (m_seenReferences == null || m_seenReferences.Add(reference))
            {
                m_references.Add(reference);
            }
        }

        private IReference? m_pushBack;
        private readonly List<IReference> m_references;
        private readonly HashSet<IReference>? m_seenReferences;
        private int m_index;
    }

    /// <summary>
    /// Stores the a reference for a node.
    /// </summary>
    public class NodeStateReference : IReference
    {
        /// <summary>
        /// Constructs a reference to an internal target.
        /// </summary>
        public NodeStateReference(NodeId referenceTypeId, bool isInverse, NodeState target)
        {
            ReferenceTypeId = referenceTypeId;
            IsInverse = isInverse;
            TargetId = target.NodeId;
            Target = target;
        }

        /// <summary>
        /// Constructs a reference to an external target.
        /// </summary>
        public NodeStateReference(NodeId referenceTypeId, bool isInverse, ExpandedNodeId targetId)
        {
            ReferenceTypeId = referenceTypeId;
            IsInverse = isInverse;
            TargetId = targetId;
            Target = null;
        }

        /// <summary>
        /// The internal target of the reference.
        /// </summary>
        public NodeState? Target { get; }

        /// <inheritdoc/>
        public NodeId ReferenceTypeId { get; }

        /// <inheritdoc/>
        public bool IsInverse { get; }

        /// <inheritdoc/>
        public ExpandedNodeId TargetId { get; }
    }
}
