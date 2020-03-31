/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
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

namespace Opc.Ua
{
    /// <summary>
    /// This interface is used by ContentFilterOperation to get values from the 
    /// NodeSet for use by the various filter operators. All NodeSets used in a
    /// ContentFilter must implement this interface.
    /// </summary>
    public interface IFilterTarget
    {        
        /// <summary>
        /// Checks whether the target is an instance of the specified type.
        /// </summary>
        /// <param name="context">The context to use when checking the type definition.</param>
        /// <param name="typeDefinitionId">The type of the instance.</param>
        /// <returns>
        /// True if the object is an instance of the specified type.
        /// </returns>
        bool IsTypeOf(
            FilterContext context, 
            NodeId        typeDefinitionId);

        /// <summary>
        /// Returns the value of an attribute identified by the operand.
        /// </summary>
        /// <param name="context">The context to use when evaluating the operand.</param>
        /// <param name="typeDefinitionId">The type of the instance.</param>
        /// <param name="relativePath">The path from the instance to the node which defines the attribute.</param>
        /// <param name="attributeId">The attribute to return.</param>
        /// <param name="indexRange">The sub-set of an array value to return.</param>
        /// <returns>
        /// The attribute value. Returns null if the attribute does not exist.
        /// </returns>
        object GetAttributeValue(
            FilterContext        context, 
            NodeId               typeDefinitionId,
            IList<QualifiedName> relativePath,
            uint                 attributeId,
            NumericRange         indexRange);
    }

    /// <summary>
    /// This interface is used by ContentFilterOperation to get values from the 
    /// NodeSet for use by the various filter operators. All NodeSets used in a
    /// ContentFilter must implement this interface.
    /// </summary>
    public interface IAdvancedFilterTarget : IFilterTarget
    {
        /// <summary>
        /// Checks whether the target is an instance is in the specified view.
        /// </summary>
        /// <param name="context">The context to use when checking the biew.</param>
        /// <param name="viewId">The identifier for the view.</param>
        /// <returns>True if the instance is in the view.</returns>
        bool IsInView(
            FilterContext context,
            NodeId viewId);

        /// <summary>
        /// Returns TRUE if the node is related to the current target.
        /// </summary>
        bool IsRelatedTo(
            FilterContext context,
            NodeId intermediateNodeId,
            NodeId sourceTypeId,
            NodeId targetTypeId,
            NodeId referenceTypeId,
            int hops,
            bool includeTypeDefintionSubtypes,
            bool includeReferenceSubtypes);

        /// <summary>
        /// Returns the list of nodes related to the current target.
        /// </summary>
        IList<NodeId> GetRelatedNodes(
            FilterContext context,
            NodeId intermediateNodeId,
            NodeId sourceTypeId,
            NodeId targetTypeId,
            NodeId referenceTypeId,
            int hops,
            bool includeTypeDefintionSubtypes,
            bool includeReferenceSubtypes);

        /// <summary>
        /// Returns the value of attributes for nodes which are related to the current node.
        /// </summary>
        /// <param name="context">The context to use when evaluating the operand.</param>
        /// <param name="typeDefinitionId">The type of the instance.</param>
        /// <param name="relativePath">The relative path to the attribute.</param>
        /// <param name="attributeId">The attribute to return.</param>
        /// <param name="indexRange">The sub-set of an array value to return.</param>
        /// <returns>
        /// The attribute value. Returns null if the attribute does not exist.
        /// </returns>
        object GetRelatedAttributeValue(
            FilterContext context,
            NodeId typeDefinitionId,
            RelativePath relativePath,
            uint attributeId,
            NumericRange indexRange);
    }
        
    /// <summary>
    /// Provides context information to used when searching the address space.
    /// </summary>
    public class FilterContext : IOperationContext
    {
        #region Contructors
        /// <summary>
        /// Initializes the context.
        /// </summary>
        /// <param name="namespaceUris">The namespace URIs.</param>
        /// <param name="typeTree">The type tree.</param>
        /// <param name="context">The context.</param>
        public FilterContext(NamespaceTable namespaceUris, ITypeTable typeTree, IOperationContext context)
        {
            if (namespaceUris == null) throw new ArgumentNullException(nameof(namespaceUris));
            if (typeTree == null) throw new ArgumentNullException(nameof(typeTree));

            m_namespaceUris = namespaceUris;
            m_typeTree = typeTree;
            m_context = context;
        }

        /// <summary>
        /// Initializes the context.
        /// </summary>
        /// <param name="namespaceUris">The namespace URIs.</param>
        /// <param name="typeTree">The type tree.</param>
        public FilterContext(NamespaceTable namespaceUris, ITypeTable typeTree)
        :
            this(namespaceUris, typeTree, (IList<string>)null)
        {
        }

        /// <summary>
        /// Initializes the context.
        /// </summary>
        /// <param name="namespaceUris">The namespace URIs.</param>
        /// <param name="typeTree">The type tree.</param>
        /// <param name="preferredLocales">The preferred locales.</param>
        public FilterContext(NamespaceTable namespaceUris, ITypeTable typeTree, IList<string> preferredLocales)
        {
            if (namespaceUris == null) throw new ArgumentNullException(nameof(namespaceUris));
            if (typeTree == null) throw new ArgumentNullException(nameof(typeTree));

            m_namespaceUris = namespaceUris;
            m_typeTree = typeTree;
            m_context = null;
            m_preferredLocales = preferredLocales;
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The namespace table to use when evaluating filters.
        /// </summary>
        /// <value>The namespace URIs.</value>
        public NamespaceTable NamespaceUris
        {
            get { return m_namespaceUris; }
        }

        /// <summary>
        /// The type tree to use when evaluating filters.
        /// </summary>
        /// <value>The type tree.</value>
        public ITypeTable TypeTree
        {
            get { return m_typeTree; }
        }
        #endregion

        #region IOperationContext Members
        /// <summary>
        /// The identifier for the session (null if multiple sessions are associated with the operation).
        /// </summary>
        /// <value>The session identifier.</value>
        public NodeId SessionId
        {
            get 
            { 
                if (m_context != null)
                {
                    return m_context.SessionId;
                }

                return null;
            }
        }

        /// <summary>
        /// The identity of the user.
        /// </summary>
        /// <value>The user identity.</value>
        public IUserIdentity UserIdentity
        {
            get 
            { 
                if (m_context != null)
                {
                    return m_context.UserIdentity;
                }

                return null;
            }
        }

        /// <summary>
        /// The locales to use if available.
        /// </summary>
        /// <value>The preferred locales.</value>
        public IList<string> PreferredLocales
        {
            get 
            { 
                if (m_context != null)
                {
                    return m_context.PreferredLocales;
                }

                return m_preferredLocales;
            }
        }

        /// <summary>
        /// The mask to use when collecting any diagnostic information.
        /// </summary>
        /// <value>The diagnostics mask.</value>
        public DiagnosticsMasks DiagnosticsMask
        {
            get 
            { 
                if (m_context != null)
                {
                    return m_context.DiagnosticsMask;
                }

                return DiagnosticsMasks.SymbolicId;
            }
        }

        /// <summary>
        /// The table of strings which is used to store diagnostic string data.
        /// </summary>
        /// <value>The string table.</value>
        public StringTable StringTable
        {
            get 
            { 
                if (m_context != null)
                {
                    return m_context.StringTable;
                }

                return null;
            }
        }

        /// <summary>
        /// When the operation times out.
        /// </summary>
        /// <value>The operation deadline.</value>
        public DateTime OperationDeadline
        {
            get 
            { 
                if (m_context != null)
                {
                    return m_context.OperationDeadline;
                }

                return DateTime.MaxValue;
            }
        }

        /// <summary>
        /// The current status of the the operation (bad if the operation has been aborted).
        /// </summary>
        /// <value>The operation status.</value>
        public StatusCode OperationStatus
        {
            get 
            { 
                if (m_context != null)
                {
                    return m_context.OperationStatus;
                }

                return StatusCodes.Good;
            }
        }

        /// <summary>
        /// The audit identifier associated with the operation.
        /// </summary>
        /// <value>The audit entry identifier.</value>
        public string AuditEntryId
        {
            get 
            { 
                if (m_context != null)
                {
                    return m_context.AuditEntryId;
                }

                return null;
            }
        }
        #endregion

        #region Private Fields
        private NamespaceTable m_namespaceUris;
        private ITypeTable m_typeTree;
        private IOperationContext m_context;
        private IList<string> m_preferredLocales;
        #endregion
    }
}
