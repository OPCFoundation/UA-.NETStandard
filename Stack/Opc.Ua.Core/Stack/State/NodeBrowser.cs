/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
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
using System.Text;

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

    #region NodeBrowser Class
    /// <summary>
    /// A thread safe object which browses the references for an node.
    /// </summary>
    public class NodeBrowser : INodeBrowser
    {
        #region Constructors
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
            m_context = context;
            m_view = view;
            m_referenceType = referenceType;
            m_includeSubtypes = includeSubtypes;
            m_browseDirection = browseDirection;
            m_browseName = browseName;
            m_internalOnly = internalOnly;
            m_references = new List<IReference>();
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
        #endregion

        #region IDisposable Members
        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            // nothing to do.
        }
        #endregion

        #region INodeBrowser Methods
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
        #endregion

        #region Public Methods
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

            // easiet to check inverse flag first.
            if (isInverse)
            {
                if (m_browseDirection == BrowseDirection.Forward)
                {
                    return false;
                }
            }
            else
            {
                if (m_browseDirection == BrowseDirection.Inverse)
                {
                    return false;
                }
            }

            // check for no filter or exact match.
            if (NodeId.IsNull(m_referenceType) || referenceType == m_referenceType)
            {
                return true;
            }

            // check subtypes if possible.
            if (m_includeSubtypes && m_context != null)
            {
                if (m_context.TypeTable.IsTypeOf(referenceType, m_referenceType))
                {
                    return true;
                }
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
                if (!QualifiedName.IsNull(m_browseName))
                {
                    if (target.BrowseName != m_browseName)
                    {
                        return;
                    }
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
        #endregion

        #region Public Properties
        /// <summary>
        /// Thr synchronization lock used by the browser.
        /// </summary>
        protected object DataLock
        {
            get { return m_lock; }
        }

        /// <summary>
        /// The table of types known to the UA server.
        /// </summary>
        public ISystemContext SystemContext
        {
            get { return m_context; }
        }
        
        /// <summary>
        /// The view being browsed.
        /// </summary>
        public ViewDescription View
        {
            get { return m_view; }
        }

        /// <summary>
        /// The type of reference to return.
        /// </summary>
        public NodeId ReferenceType
        {
            get { return m_referenceType; }
        }

        /// <summary>
        /// Whether to return subtypes of the reference.
        /// </summary>
        public bool IncludeSubtypes
        {
            get { return m_includeSubtypes; }
        }

        /// <summary>
        /// The direction for the references to return.
        /// </summary>
        public BrowseDirection BrowseDirection
        {
            get { return m_browseDirection; }
        }

        /// <summary>
        /// The browse name of the targets to return.
        /// </summary>
        public QualifiedName BrowseName
        {
            get { return m_browseName; }
        }

        /// <summary>
        /// Indicates that the browser only returned easy to access references stored in memory.
        /// </summary>
        public bool InternalOnly
        {
            get { return m_internalOnly; }
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private ISystemContext m_context;
        private ViewDescription m_view;
        private NodeId m_referenceType;
        private bool m_includeSubtypes;
        private BrowseDirection m_browseDirection;
        private IReference m_pushBack;
        private List<IReference> m_references;
        private QualifiedName m_browseName;
        private bool m_internalOnly;
        private int m_index;
        #endregion
    }
    #endregion

    #region NodeStateReference Class
    /// <summary>
    /// Stores the a reference for a node.
    /// </summary>
    public class NodeStateReference : IReference
    {
        #region Constructors
        /// <summary>
        /// Constructs a reference to an internal target.
        /// </summary>
        public NodeStateReference(NodeId referenceTypeId, bool isInverse, NodeState target)
        {
            m_referenceTypeId = referenceTypeId;
            m_isInverse = isInverse;
            m_targetId = target.NodeId;
            m_target = target;
        }

        /// <summary>
        /// Constructs a reference to an external target.
        /// </summary>
        public NodeStateReference(NodeId referenceTypeId, bool isInverse, ExpandedNodeId targetId)
        {
            m_referenceTypeId = referenceTypeId;
            m_isInverse = isInverse;
            m_targetId = targetId;
            m_target = null;
        }
        #endregion

        #region Public Members
        /// <summary>
        /// The internal target of the reference.
        /// </summary>
        public NodeState Target
        {
            get { return m_target; }
        }
        #endregion

        #region IReference Members
        /// <summary cref="IReference.ReferenceTypeId" />
        public NodeId ReferenceTypeId
        {
            get { return m_referenceTypeId; }
        }

        /// <summary cref="IReference.IsInverse" />
        public bool IsInverse
        {
            get { return m_isInverse; }
        }

        /// <summary cref="IReference.TargetId" />
        public ExpandedNodeId TargetId
        {
            get { return m_targetId; }
        }
        #endregion

        #region Private Fields
        private NodeId m_referenceTypeId;
        private bool m_isInverse;
        private ExpandedNodeId m_targetId;
        private NodeState m_target;
        #endregion
    }
    #endregion
}
