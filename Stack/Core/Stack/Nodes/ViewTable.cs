/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

using System;
using System.Collections.Generic;

namespace Opc.Ua
{
    /// <summary>
    /// The table of all reference types known to the server.
    /// </summary>
    /// <remarks>This class is thread safe.</remarks>
    public class ViewTable
    {
        #region Constructors
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public ViewTable()
        {
            m_views = new Dictionary<NodeId, ViewNode>();
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Determines whether a node id is a valid view id.
        /// </summary>
        /// <param name="description">The description.</param>
        /// <returns>
        /// 	<c>true</c> if the specified description is valid; otherwise, <c>false</c>.
        /// </returns>
        public bool IsValid(ViewDescription description)
        {
            if (ViewDescription.IsDefault(description))
            {
                return true;
            }

            lock (m_lock)
            {
                return m_views.ContainsKey(description.ViewId);
            }
        }
        
        /// <summary>
        /// Determines whether a node is in a view.
        /// </summary>
        /// <param name="description">The description.</param>
        /// <param name="nodeId">The node id.</param>
        /// <returns>
        /// 	<c>true</c> whether a node is in a view; otherwise, <c>false</c>.
        /// </returns>
        public bool IsNodeInView(ViewDescription description, NodeId nodeId)
        {
            // everything is in the default view.
            if (ViewDescription.IsDefault(description))
            {
                return true;
            }

            lock (m_lock)
            {
                ViewNode view = null;

                if (m_views.TryGetValue(description.ViewId, out view))
                {
                    throw new ServiceResultException(StatusCodes.BadViewIdUnknown);
                }

                return false;
            }
        }

        /// <summary>
        /// Determines whether a reference is in a view.
        /// </summary>
        /// <param name="description">The description.</param>
        /// <param name="reference">The reference.</param>
        /// <returns>
        /// 	<c>true</c> whether a reference is in a view; otherwise, <c>false</c>.
        /// </returns>
        public bool IsReferenceInView(ViewDescription description, ReferenceDescription reference)
        {
            // everything is in the default view.
            if (ViewDescription.IsDefault(description))
            {
                return true;
            }

            lock (m_lock)
            {
                ViewNode view = null;

                if (m_views.TryGetValue(description.ViewId, out view))
                {
                    throw new ServiceResultException(StatusCodes.BadViewIdUnknown);
                }

                return false;
            }
        }

        /// <summary>
        /// Adds a view to the table.
        /// </summary>
        /// <param name="view">The view.</param>
        public void Add(ViewNode view)
        {
            if (view == null) throw new ArgumentNullException("view");

            if (NodeId.IsNull(view.NodeId))
            {
                throw new ServiceResultException(
                    StatusCodes.BadNodeIdInvalid,
                    Utils.Format("A view may not have a null node id."));
            }

            lock (m_lock)
            {
                // check for duplicate id.
                if (m_views.ContainsKey(view.NodeId))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadNodeIdExists,
                        Utils.Format("A view with the node id '{0}' already exists.", view.NodeId));
                }

                // save view.
                m_views.Add(view.NodeId, view);
            }
        }

        /// <summary>
        /// Removes a view from the table.
        /// </summary>
        /// <param name="viewId">The view identifier.</param>
        public void Remove(NodeId viewId)
        {
            if (NodeId.IsNull(viewId)) throw new ArgumentNullException("viewId");
                        
            lock (m_lock)
            {
                // find view.
                ViewNode view = null;

                if (!m_views.TryGetValue(viewId, out view))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadViewIdUnknown,
                        Utils.Format("A reference type with the node id '{0}' does not exist.", viewId));
                }

                // remove view node.
                m_views.Remove(viewId);
            }
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private Dictionary<NodeId, ViewNode> m_views;
        #endregion
    }
}
