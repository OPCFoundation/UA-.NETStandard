/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Threading;

namespace Opc.Ua
{
    /// <summary>
    /// The table of all reference types known to the server.
    /// </summary>
    /// <remarks>This class is thread safe.</remarks>
    public class ViewTable
    {
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public ViewTable()
        {
            m_views = [];
        }

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
        /// <exception cref="ServiceResultException"></exception>
        public bool IsNodeInView(ViewDescription description, NodeId nodeId)
        {
            // everything is in the default view.
            if (ViewDescription.IsDefault(description))
            {
                return true;
            }

            lock (m_lock)
            {
                if (!m_views.TryGetValue(description.ViewId, out _))
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
        /// <exception cref="ServiceResultException"></exception>
        public bool IsReferenceInView(ViewDescription description, ReferenceDescription reference)
        {
            // everything is in the default view.
            if (ViewDescription.IsDefault(description))
            {
                return true;
            }

            lock (m_lock)
            {
                if (!m_views.TryGetValue(description.ViewId, out _))
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
        /// <exception cref="ArgumentNullException"><paramref name="view"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public void Add(ViewNode view)
        {
            if (view == null)
            {
                throw new ArgumentNullException(nameof(view));
            }

            if (view.NodeId.IsNull)
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
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public void Remove(NodeId viewId)
        {
            if (viewId.IsNull)
            {
                throw new ArgumentNullException(nameof(viewId));
            }

            lock (m_lock)
            {
                // find view.

                if (!m_views.TryGetValue(viewId, out _))
                {
                    throw new ServiceResultException(
                        StatusCodes.BadViewIdUnknown,
                        Utils.Format(
                            "A reference type with the node id '{0}' does not exist.",
                            viewId));
                }

                // remove view node.
                m_views.Remove(viewId);
            }
        }

        private readonly Lock m_lock = new();
        private readonly Dictionary<NodeId, ViewNode> m_views;
    }
}
