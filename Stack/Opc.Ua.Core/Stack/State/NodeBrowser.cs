/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

using System;
using System.Collections.Generic;

namespace Opc.Ua
{
    /// <summary>
    /// An interface to an object that browses the references of an node.
    /// </summary>
    public interface INodeBrowser : IDisposable
    {
        /// <summary>
        /// Returns the next reference.
        /// </summary>
        IReference Next();

        /// <summary>
        /// Pushes a previously returned reference back into the browser.
        /// </summary>
        void Push(IReference reference);
    }

    /// <summary>
    /// A thread safe object which browses the references for an node.
    /// </summary>
    public class NodeBrowser : INodeBrowser
    {
        /// <summary>
        /// Creates a new browser object with a set of filters.
        /// </summary>
        public NodeBrowser(
            ISystemContext context,
            ViewDescription view,
            NodeId referenceType,
            bool includeSubtypes,
            BrowseDirection browseDirection,
            QualifiedName browseName,
            IEnumerable<IReference> additionalReferences,
            bool internalOnly)
        {
            SystemContext = context;
            View = view;
            ReferenceType = referenceType;
            IncludeSubtypes = includeSubtypes;
            BrowseDirection = browseDirection;
            BrowseName = browseName;
            InternalOnly = internalOnly;
            m_references = [];
            m_index = 0;

            // add any additional references if they meet the criteria.
            if (additionalReferences != null)
            {
                foreach (IReference reference in additionalReferences)
                {
                    if (IsRequired(reference.ReferenceTypeId, reference.IsInverse))
                    {
                        m_references.Add(reference);
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
        public virtual IReference Next()
        {
            lock (DataLock)
            {
                IReference reference = null;

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
        }

        /// <summary>
        /// Pushes a previously returned reference back into the browser.
        /// </summary>
        public virtual void Push(IReference reference)
        {
            lock (DataLock)
            {
                m_pushBack = reference;
            }
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
            if (NodeId.IsNull(referenceType))
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
            if (NodeId.IsNull(ReferenceType) || referenceType == ReferenceType)
            {
                return true;
            }

            // check subtypes if possible.
            if (IncludeSubtypes &&
                SystemContext != null &&
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
            lock (DataLock)
            {
                m_references.Add(reference);
            }
        }

        /// <summary>
        /// Adds a reference to target entity.
        /// </summary>
        /// <remarks>
        /// Will not add the reference if the browse name does not match the browse name filter.
        /// </remarks>
        public virtual void Add(NodeId referenceTypeId, bool isInverse, NodeState target)
        {
            lock (DataLock)
            {
                // do not return add target unless the browse name matches.
                if (!QualifiedName.IsNull(BrowseName) && target.BrowseName != BrowseName)
                {
                    return;
                }

                m_references.Add(new NodeStateReference(referenceTypeId, isInverse, target));
            }
        }

        /// <summary>
        /// Adds a reference to target identified by its node id.
        /// </summary>
        public virtual void Add(NodeId referenceTypeId, bool isInverse, ExpandedNodeId targetId)
        {
            lock (DataLock)
            {
                m_references.Add(new NodeStateReference(referenceTypeId, isInverse, targetId));
            }
        }

        /// <summary>
        /// Thr synchronization lock used by the browser.
        /// </summary>
        protected object DataLock { get; } = new object();

        /// <summary>
        /// The table of types known to the UA server.
        /// </summary>
        public ISystemContext SystemContext { get; }

        /// <summary>
        /// The view being browsed.
        /// </summary>
        public ViewDescription View { get; }

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

        private IReference m_pushBack;
        private readonly List<IReference> m_references;
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
        public NodeState Target { get; }

        /// <inheritdoc/>
        public NodeId ReferenceTypeId { get; }

        /// <inheritdoc/>
        public bool IsInverse { get; }

        /// <inheritdoc/>
        public ExpandedNodeId TargetId { get; }
    }
}
