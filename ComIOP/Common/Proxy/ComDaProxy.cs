/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.InteropServices;
using Opc.Ua.Client;

namespace Opc.Ua.Com
{
    /// <summary>
    /// A base class for classes that implement an OPC COM specification.
    /// </summary>
    public class ComDaProxy : ComProxy
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ComDaProxy"/> class.
        /// </summary>
        public ComDaProxy()
		{
            m_groups = new List<ComDaGroup>();
        }
        #endregion
        
        #region IDisposable Members
        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected override void Dispose(bool disposing)
        {  
            if (disposing)
            {
                lock (Lock)
                {
                    for (int ii = 0; ii < m_groups.Count; ii++)
                    {
                        Utils.SilentDispose(m_groups[ii]);
                    }

                    m_groups.Clear();
                }
            }

            base.Dispose(disposing);
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Gets the group count.
        /// </summary>
        /// <value>The group count.</value>
        public int GroupCount
        {
            get
            {
                lock (Lock)
                {
                    return m_groups.Count;
                }
            }
        }

        /// <summary>
        /// Gets the last update time.
        /// </summary>
        /// <value>The group count.</value>
        public DateTime LastUpdateTime
        {
            get
            {
                lock (Lock)
                {
                    return m_lastUpdateTime;
                }
            }
        }

        /// <summary>
        /// Gets the group with the specified name.
        /// </summary>
        /// <param name="groupName">Name of the group.</param>
        /// <returns>The group. Null if it does not exist.</returns>
        public ComDaGroup GetGroupByName(string groupName)
        {
            lock (Lock)
            {
                for (int ii = 0; ii < m_groups.Count; ii++)
                {
                    ComDaGroup group = m_groups[ii];

                    if (group.Name == groupName)
                    {
                        return group;
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Gets the group by handle.
        /// </summary>
        /// <param name="serverHandle">The server handle.</param>
        /// <returns>The group.</returns>
        public ComDaGroup GetGroupByHandle(int serverHandle)
        {
            lock (Lock)
            {
                for (int ii = 0; ii < m_groups.Count; ii++)
                {
                    ComDaGroup group = m_groups[ii];

                    if (group.ServerHandle == serverHandle)
                    {
                        return group;
                    }
                }

                return null;
            }
        }

        /// <summary>
        /// Adds a group to the server.
        /// </summary>
        /// <param name="groupName">Name of the group.</param>
        /// <returns>The new group.</returns>
        public ComDaGroup AddGroup(string groupName)
        {
            lock (Lock)
            {
                // assign a unique name.
                if (String.IsNullOrEmpty(groupName))
                {
                    groupName = Guid.NewGuid().ToString();
                }

                // create the group.
                ComDaGroup group = new ComDaGroup(groupName, ++m_groupCounter);
                m_groups.Add(group);
                return group;
            }
        }

        /// <summary>
        /// Removes the group.
        /// </summary>
        /// <param name="group">The group.</param>
        public void RemoveGroup(ComDaGroup group)
        {
            lock (Lock)
            {
                for (int ii = 0; ii < m_groups.Count; ii++)
                {
                    if (Object.ReferenceEquals(group, m_groups[ii]))
                    {
                        m_groups.RemoveAt(ii);
                        group.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// Returns the current set of groups.
        /// </summary>
        /// <returns>The list of groups.</returns>
        public ComDaGroup[] GetGroups()
        {
            lock (Lock)
            {
                ComDaGroup[] groups = new ComDaGroup[m_groups.Count];

                for (int ii = 0; ii < m_groups.Count; ii++)
                {
                    groups[ii] = m_groups[ii];
                }

                return groups;
            }
        }

        /// <summary>
        /// Reads the values for the specified item ids.
        /// </summary>
        /// <param name="itemIds">The item ids.</param>
        /// <returns>The values.</returns>
        public DaValue[] Read(string[] itemIds)
        {
            DaValue[] values = new DaValue[itemIds.Length];
            
            for (int ii = 0; ii < values.Length; ii++)
            {
                values[ii] = new DaValue();
                values[ii].Error = ResultIds.E_UNKNOWNITEMID;
            }

            return values;
        }

        /// <summary>
        /// Writes the values for the specified item ids.
        /// </summary>
        /// <param name="itemIds">The item ids.</param>
        /// <param name="values">The values.</param>
        /// <returns>The results.</returns>
        public int[] Write(string[] itemIds, DaValue[] values)
        {
            int[] results = new int[itemIds.Length];

            for (int ii = 0; ii < results.Length; ii++)
            {
                results[ii] = ResultIds.E_UNKNOWNITEMID;
            }

            return results;
        }

        /// <summary>
        /// Moves the current browse position up.
        /// </summary>
        public void BrowseUp()
        {
            if (NodeId.IsNull(m_browsePosition))
            {
                throw ComUtils.CreateComException(ResultIds.E_FAIL);
            }
        }

        /// <summary>
        /// Moves the current browse position down.
        /// </summary>
        public void BrowseDown(string targetName)
        {
            throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
        }

        /// <summary>
        /// Moves the current browse position to the specified item.
        /// </summary>
        public void BrowseTo(string itemId)
        {
            if (String.IsNullOrEmpty(itemId))
            {
                m_browsePosition = null;
                return;
            }

            throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
        }

        /// <summary>
        /// Browses the current branch.
        /// </summary>
        /// <param name="isBranch">if set to <c>true</c> the return branches.</param>
        /// <param name="filter">The filter.</param>
        /// <param name="dataType">Type of the data.</param>
        /// <param name="accessRights">The access rights.</param>
        /// <returns></returns>
        public List<string> Browse(bool isBranch, string filter, short dataType, int accessRights)
        {
            return new List<string>();
        }

        /// <summary>
        /// Gets the item id for the specified browse element.
        /// </summary>
        /// <param name="browseName">The name of the browse element.</param>
        /// <returns></returns>
        public string GetItemId(string browseName)
        {
            throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
        }

        /// <summary>
        /// Gets the properties.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <returns></returns>
        public IList<DaProperty> GetProperties(string itemId)
        {
            if (String.IsNullOrEmpty(itemId))
            {
                return new DaProperty[0];
            }

            throw ComUtils.CreateComException(ResultIds.E_UNKNOWNITEMID);
        }

        /// <summary>
        /// Gets the property values.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="propertyIds">The property ids.</param>
        /// <returns>The property values.</returns>
        public DaValue[] GetPropertyValues(string itemId, int[] propertyIds)
        {
            DaValue[] results = new DaValue[propertyIds.Length];

            for (int ii = 0; ii < results.Length; ii++)
            {
                results[ii] = new DaValue();
                results[ii].Error = ResultIds.E_UNKNOWNITEMID;
            }

            return results;
        }

        /// <summary>
        /// Gets the item ids for the properties.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="propertyIds">The property ids.</param>
        /// <param name="itemIds">The item ids.</param>
        /// <returns>Any errors.</returns>
        public int[] GetItemIds(string itemId, int[] propertyIds, out string[] itemIds)
        {
            itemIds = new string[propertyIds.Length];
            int[] results = new int[propertyIds.Length];

            for (int ii = 0; ii < results.Length; ii++)
            {
                results[ii] = ResultIds.E_UNKNOWNITEMID;
            }

            return results;
        }
        #endregion

        /*
        private ushort[] m_namespaceMapping;

        private void CreateNamespaceMapping(Session session, ConfiguredEndpoint endpoint)
        {

        }

        private NodeId ParseItemId(string itemId)
        {
            try
            {
                NodeId nodeId = NodeId.Parse(itemId);

                // check if the namespace index needs to be updated.
                if (nodeId.NamespaceIndex > 1 && nodeId.NamespaceIndex < m_namespaceMapping.Length)
                {
                    if (nodeId.NamespaceIndex != m_namespaceMapping[nodeId.NamespaceIndex])
                    {
                        nodeId = new NodeId(nodeId.Identifier, m_namespaceMapping[nodeId.NamespaceIndex]);
                    }                    
                }

                return nodeId;
            }
            catch (Exception)
            {
                return null;
            }
        }
        */

        private List<DaElement> Browse(string itemId)
        {
            m_lastUpdateTime = DateTime.UtcNow;

            // get the session to use.
            Session session = Session;

            if (session == null)
            {
                return new List<DaElement>();
            }

            return null;
        }

        #region Private Fields
        private DateTime m_lastUpdateTime;
        private List<ComDaGroup> m_groups;
        private int m_groupCounter;
        private NodeId m_browsePosition;
        #endregion
	}
}
