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
            if (view == null) throw new ArgumentNullException(nameof(view));

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
            if (NodeId.IsNull(viewId)) throw new ArgumentNullException(nameof(viewId));
                        
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
