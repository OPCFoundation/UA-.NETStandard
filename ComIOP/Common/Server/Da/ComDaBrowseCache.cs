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
using System.Text;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using Opc.Ua.Client;

namespace Opc.Ua.Com.Server
{
    /// <summary>
    /// Maintains a shared cache of browse information.
    /// </summary>
    public class ComDaBrowseCache
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ComDaBrowseCache"/> class.
        /// </summary>
        /// <param name="mapper">The object used to map namespace indexes.</param>
        public ComDaBrowseCache(ComNamespaceMapper mapper)
		{
            m_mapper = mapper;
            m_cache = new Dictionary<string,BrowseElement>();
            m_browseBlockSize = 10;
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Gets or sets the size of the blocks to use when browsing.
        /// </summary>
        /// <value>The size of the block.</value>
        public int BrowseBlockSize
        {
            get { return m_browseBlockSize; }
            set { m_browseBlockSize = value; }               
        }

        /// <summary>
        /// Finds the element.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="itemId">The item id.</param>
        /// <returns>Null if the item does not exist.</returns>
        public ComDaBrowseElement FindElement(Session session, string itemId)
        {
            BrowseElement element = Lookup(session, itemId);

            if (element == null)
            {
                return null;
            }

            return element;
        }

        /// <summary>
        /// Finds the parent.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="itemId">The item id.</param>
        /// <returns>Null if the item or parent does not exist.</returns>
        public ComDaBrowseElement FindParent(Session session, string itemId)
        {           
            BrowseElement element = Lookup(session, itemId);

            if (element == null)
            {
                return null;
            }

            BrowseElement parent = Lookup(session, element.ParentId);

            if (parent == null)
            {
                return null;
            }

            return parent;
        }

        /// <summary>
        /// Finds the child with the specified name.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="itemId">The item id.</param>
        /// <param name="childName">Name of the child.</param>
        /// <returns>
        /// Null if the item or child does not exist.
        /// </returns>
        public ComDaBrowseElement FindChild(Session session, string itemId, string childName)
        {
            TraceState("FindChild", itemId, childName);

            // find the element in the cache.
            BrowseElement parent = null;

            lock (m_lock)
            {
                if (itemId == null)
                {
                    itemId = String.Empty;
                }

                if (!m_cache.TryGetValue(itemId, out parent))
                {
                    parent = null;
                }
            }

            // fetch the element from the server.
            if (parent == null)
            {
                NodeId nodeId = m_mapper.GetRemoteNodeId(itemId);
                parent = CreateBrowseElement(session, nodeId);

                if (parent == null)
                {
                    return null;
                }
            }

            // name make actually be an item id.
            string childId = childName;

            // look up the child reference.
            ReferenceDescription reference = null;

            if (parent.ReferencesByName.TryGetValue(childName, out reference))
            {
                childId = m_mapper.GetLocalItemId((NodeId)reference.NodeId);
            }

            // look up target of reference in the cache.
            BrowseElement child = null;

            lock (m_lock)
            {
                if (m_cache.TryGetValue(childId, out child))
                {
                    return child;
                }
            }

            // return the child.
            return Lookup(session, childId);
        }
        
        /// <summary>
        /// Looks up the cached element using the specified session.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="itemId">The item id.</param>
        /// <returns>The element. Null if the item id does not exist.</returns>
        private BrowseElement Lookup(Session session, string itemId)
        {           
            BrowseElement element = null;

            lock (m_lock)
            {
                if (itemId == null)
                {
                    itemId = String.Empty;
                }

                if (m_cache.TryGetValue(itemId, out element))
                {
                    return element;
                }
            }

            NodeId nodeId = m_mapper.GetRemoteNodeId(itemId);
            
            element = CreateBrowseElement(session, nodeId);

            if (element != null)
            {
                lock (m_lock)
                {
                    m_cache[itemId] = element;
                }
            }

            return element;
        }

        /// <summary>
        /// Browses for children of the specified item.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="itemId">The item id.</param>
        /// <param name="elementFilter">The element filter.</param>
        /// <param name="nameFilter">The name filter.</param>
        /// <param name="dataTypeFilter">The data type filter.</param>
        /// <param name="accessRightsFilter">The access rights filter.</param>
        /// <returns>
        /// The list of names that meet the criteria.
        /// </returns>
        public IList<string> BrowseForNames(
            Session session,
            string itemId,
            BrowseElementFilter elementFilter,
            string nameFilter,
            short dataTypeFilter,
            int accessRightsFilter)
        {
            TraceState("BrowseForNames", itemId, elementFilter, nameFilter, dataTypeFilter, accessRightsFilter);

            // look up the parent.
            BrowseElement parent = Lookup(session, itemId);

            if (parent == null)
            {
                return null;
            }

            // fetch the children.
            List<BrowseElement> children = LookupChildElements(session, parent);

            // search the children.
            List<string> hits = new List<string>();

            for (int ii = 0; ii < children.Count; ii++)
            {
                BrowseElement child = children[ii];

                // apply the name filter.
                if (!String.IsNullOrEmpty(nameFilter))
                {
                    if (!ComUtils.Match(child.BrowseName, nameFilter, false))
                    {
                        continue;
                    }
                }

                // branches must have children.
                if (elementFilter == BrowseElementFilter.Branch)
                {
                    if (child.NodeClass == NodeClass.Variable && child.ReferencesByName.Count == 0)
                    {
                        continue;
                    }
                }

                // items must be variables.
                if (elementFilter == BrowseElementFilter.Item)
                {
                    if (child.NodeClass != NodeClass.Variable)
                    {
                        continue;
                    }
                }

                if (child.NodeClass == NodeClass.Variable)
                {
                    // apply data type filter.
                    if (dataTypeFilter != 0)
                    {
                        if (child.CanonicalDataType != dataTypeFilter)
                        {
                            continue;
                        }
                    }

                    // apply access rights filter.
                    if (accessRightsFilter != 0)
                    {
                        if ((child.AccessRights & accessRightsFilter) == 0)
                        {
                            continue;
                        }
                    }
                }

                // a match.
                hits.Add(child.BrowseName);
            }

            // return all of the matching names.
            return hits;
        }

        /// <summary>
        /// Browses for children of the specified item.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="itemId">The item id.</param>
        /// <param name="nameFilter">The name filter.</param>
        /// <param name="dataTypeFilter">The data type filter.</param>
        /// <param name="accessRightsFilter">The access rights filter.</param>
        /// <returns>
        /// The list of names that meet the criteria.
        /// </returns>
        public IList<string> BrowseFlat(
            Session session,
            string itemId,
            string nameFilter,
            short dataTypeFilter,
            int accessRightsFilter)
        {
            TraceState("BrowseFlat", itemId, nameFilter, dataTypeFilter, accessRightsFilter);

            // look up the parent.
            BrowseElement parent = Lookup(session, itemId);

            if (parent == null)
            {
                return null;
            }

            // search the children.
            List<string> hits = new List<string>();
            Dictionary<string,BrowseElement> branches = new Dictionary<string,BrowseElement>();
            branches.Add(parent.ItemId, parent);

            // recusively find the item ids.
            BrowseFlat(
                session,
                parent,
                nameFilter,
                dataTypeFilter,
                accessRightsFilter,
                branches,
                hits);

            // return all of the matching item ids.
            return hits;
        }

        /// <summary>
        /// Recursively browses the address space.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="parent">The parent.</param>
        /// <param name="nameFilter">The name filter.</param>
        /// <param name="dataTypeFilter">The data type filter.</param>
        /// <param name="accessRightsFilter">The access rights filter.</param>
        /// <param name="branches">The table of followed branches.</param>
        /// <param name="hits">The item ids found for variables that meet the criteria.</param>
        private void BrowseFlat(
            Session session,
            BrowseElement parent,
            string nameFilter,
            short dataTypeFilter,
            int accessRightsFilter,
            Dictionary<string, BrowseElement> branches,
            List<string> hits)
        {
            // fetch the children.
            List<BrowseElement> children = LookupChildElements(session, parent);

            for (int ii = 0; ii < children.Count; ii++)
            {
                BrowseElement child = children[ii];

                // recusively follow branches but need to guard against loops.
                if (child.ReferencesByName.Count > 0)
                {
                    if (!branches.ContainsKey(child.ItemId))
                    {
                        branches.Add(child.ItemId, child);
                        BrowseFlat(session, child, nameFilter, dataTypeFilter, accessRightsFilter, branches, hits);
                    }
                }

                // nothing more to do if not a variable.
                if (child.NodeClass != NodeClass.Variable)
                {
                    continue;
                }

                // apply the name filter to the item id.
                if (!String.IsNullOrEmpty(nameFilter))
                {
                    if (!ComUtils.Match(child.ItemId, nameFilter, false))
                    {
                        continue;
                    }
                }

                // apply data type filter.
                if (dataTypeFilter != 0)
                {
                    if (child.CanonicalDataType != dataTypeFilter)
                    {
                        continue;
                    }
                }

                // apply access rights filter.
                if (accessRightsFilter != 0)
                {
                    if ((child.AccessRights & accessRightsFilter) == 0)
                    {
                        continue;
                    }
                }

                // a match.
                hits.Add(child.ItemId);
            }
        }
        
        /// <summary>
        /// Browses for the children of the specified item.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="itemId">The item id.</param>
        /// <returns>The queue of elements that meet the criteria.</returns>
        public Queue<ComDaBrowseElement> BrowseForElements(Session session, string itemId)
        {
            TraceState("BrowseForElements", itemId);

            // look up the parent.
            BrowseElement parent = Lookup(session, itemId);

            if (parent == null)
            {
                return null;
            }

            // fetch the children.
            List<BrowseElement> children = LookupChildElements(session, parent);

            // search the children.
            Queue<ComDaBrowseElement> hits = new Queue<ComDaBrowseElement>();

            for (int ii = 0; ii < children.Count; ii++)
            {
                hits.Enqueue(children[ii]);
            }

            // return all of the matching names.
            return hits;
        }

        /// <summary>
        /// Gets the available properties for the specified item.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="itemId">The item id.</param>
        /// <returns>Null if the item is not value. The list of supported properties otherwise.</returns>
        public IList<DaProperty> GetAvailableProperties(Session session, string itemId)
        {
            // no properties for the root.
            if (String.IsNullOrEmpty(itemId))
            {
                return null;
            }

            // find the element.
            BrowseElement element = Lookup(session, itemId);

            if (element == null)
            {
                return null;
            }

            // check which supported properties are available for the element.
            List<DaProperty> availableProperties = new List<DaProperty>();

            for (int ii = 0; ii < s_SupportedProperties.Length; ii++)
            {
                DaValue value = GetPropertyValue(element, s_SupportedProperties[ii].PropertyId);

                if (value != null && value.Error != ResultIds.E_INVALID_PID)
                {
                    availableProperties.Add(s_SupportedProperties[ii]);
                }
            }

            // return the list.
            return availableProperties;
        }
        
        /// <summary>
        /// Gets the properties.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="requests">The requests.</param>
        /// <param name="propertyIds">The property ids.</param>
        /// <returns>The list of properities.</returns>
        public IList<DaProperty> GetPropertyValues(Session session, ComDaReadPropertiesRequest[] requests, params int[] propertyIds)
        {
            TraceState("GetPropertyValues", requests.Length);

            // select all supported properties if none provided
            IList<DaProperty> properties = s_SupportedProperties;

            if (propertyIds == null || propertyIds.Length == 0)
            {
                propertyIds = new int[s_SupportedProperties.Length];

                for (int ii = 0; ii < propertyIds.Length; ii++)
                {
                    propertyIds[ii] = s_SupportedProperties[ii].PropertyId;
                }
            }

            // return the descriptions that match the requested properties.
            else
            {
                properties = new DaProperty[propertyIds.Length];

                for (int ii = 0; ii < propertyIds.Length; ii++)
                {
                    for (int jj = 0; jj < s_SupportedProperties.Length; jj++)
                    {
                        if (propertyIds[ii] == s_SupportedProperties[jj].PropertyId)
                        {
                            properties[ii] = s_SupportedProperties[jj];
                            break;
                        }
                    }
                }
            }

            // build a list of elements to create.
            BrowseElement[] elements = new BrowseElement[requests.Length];
            int[] indexes = new int[requests.Length];

            BrowseDescriptionCollection nodesToBrowse = new BrowseDescriptionCollection();

            for (int ii = 0; ii < requests.Length; ii++)
            {
                BrowseElement element = null;

                // lookup element in cache.
                lock (m_lock)
                {
                    string itemId = requests[ii].ItemId;

                    if (String.IsNullOrEmpty(itemId))
                    {
                        requests[ii].Error = ResultIds.E_INVALIDITEMID;
                        elements[ii] = null;
                        continue;
                    }

                    // if (m_cache.TryGetValue(itemId, out element))
                    // {
                    //    UpdateReadPropertyRequest(requests[ii], element, propertyIds);
                    //    continue;
                    // }

                    // create a new element.
                    elements[ii] = element = new BrowseElement();
                }

                element.ItemId = requests[ii].ItemId;
                element.NodeId = m_mapper.GetRemoteNodeId(element.ItemId);

                // prepare a request to browse the children.
                indexes[ii] = PrepareBrowseElementBrowseRequest(element.NodeId, nodesToBrowse);
            }

            // check if nothing more to do.
            if (nodesToBrowse.Count == 0)
            {
                return properties;
            }

            // browse all elements at once.
            BrowseResultCollection results = Browse(session, nodesToBrowse);

            // validate results and prepare read requests.
            ReadValueIdCollection nodesToRead = new ReadValueIdCollection();

            for (int ii = 0; ii < elements.Length; ii++)
            {
                BrowseElement element = elements[ii];

                if (element == null)
                {
                    continue;
                }

                // update the element with the children found.
                if (!UpdateBrowseElement(element, nodesToBrowse, results, indexes[ii]))
                {
                    requests[ii].Error = ResultIds.E_UNKNOWNITEMID;
                    elements[ii] = null;
                    continue;
                }

                // prepare to read the properties from the server.
                indexes[ii] = PrepareBrowseElementReadRequest(
                    element.NodeId, 
                    element.ReferencesByName, 
                    nodesToRead, 
                    NodeClass.Unspecified,
                    false);
            }

            // check if nothing to do.
            if (nodesToRead.Count == 0)
            {
                return properties;
            }

            // read all child properties at once.
            DataValueCollection values = Read(session, nodesToRead);

            // process results and build final table.
            for (int ii = 0; ii < elements.Length; ii++)
            {
                BrowseElement element = elements[ii];

                if (element == null)
                {
                    continue;
                }

                // update the browse element with the property values.
                if (!UpdateBrowseElement(session.TypeTree, element, nodesToRead, values, NodeClass.Unspecified, false, indexes[ii]))
                {
                    requests[ii].Error = ResultIds.E_UNKNOWNITEMID;
                    continue;
                }

                UpdateReadPropertyRequest(requests[ii], element, propertyIds);

                // save element in cache.
                lock (m_lock)
                {
                    element.CacheTimestamp = DateTime.UtcNow;
                    m_cache[element.ItemId] = element;
                }
            }

            // return the descriptions.
            return properties;
        }
        #endregion

        #region Static Fields
        /// <summary>
        /// The set of supported properties for variables.
        /// </summary>
        private static DaProperty[] s_SupportedProperties = new DaProperty[] 
        {
           new DaProperty(PropertyIds.DataType),
           new DaProperty(PropertyIds.Value),
           new DaProperty(PropertyIds.Quality),
           new DaProperty(PropertyIds.Timestamp),
           new DaProperty(PropertyIds.AccessRights),
           new DaProperty(PropertyIds.ScanRate),
           new DaProperty(PropertyIds.EuType),
           new DaProperty(PropertyIds.EuInfo),
           new DaProperty(PropertyIds.UaBuiltInType),
           new DaProperty(PropertyIds.UaDataTypeId),
           new DaProperty(PropertyIds.UaValueRank),
           new DaProperty(PropertyIds.UaBrowseName),
           new DaProperty(PropertyIds.UaDescription),
           new DaProperty(PropertyIds.Description),
           new DaProperty(PropertyIds.HighEU),
           new DaProperty(PropertyIds.LowEU),
           new DaProperty(PropertyIds.HighIR),
           new DaProperty(PropertyIds.LowIR),
           new DaProperty(PropertyIds.EngineeringUnits),
           new DaProperty(PropertyIds.CloseLabel),
           new DaProperty(PropertyIds.OpenLabel),
           new DaProperty(PropertyIds.TimeZone)
        };
        #endregion

        #region Private Methods
        /// <summary>
        /// Dumps the current state of the browser.
        /// </summary>
        private void TraceState(string context, params object[] args)
        {
            #if TRACESTATE
            if ((Utils.TraceMask & Utils.TraceMasks.Information) == 0)
            {
                return;
            }

            StringBuilder buffer = new StringBuilder();

            buffer.AppendFormat("ComDaBrowseCache::{0}", context);

            if (args != null)
            {
                buffer.Append("( ");

                for (int ii = 0; ii < args.Length; ii++)
                {
                    if (ii > 0)
                    {
                        buffer.Append(", ");
                    }

                    buffer.Append(new Variant(args[ii]));
                }

                buffer.Append(" )");
            }

            Utils.Trace("{0}", buffer.ToString());
            #endif
        }

        /// <summary>
        /// Updates the read property request with the property values.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <param name="element">The element.</param>
        /// <param name="propertyIds">The property ids.</param>
        private void UpdateReadPropertyRequest(ComDaReadPropertiesRequest request, BrowseElement element, int[] propertyIds)
        {
            if (element == null)
            {
                request.Error = ResultIds.E_UNKNOWNITEMID;
                return;
            }

            request.Values = new DaValue[propertyIds.Length];

            for (int ii = 0; ii < propertyIds.Length; ii++)
            {
                request.Values[ii] = GetPropertyValue(element, propertyIds[ii]);
            }
        }

        /// <summary>
        /// Gets the property value from the browse element.
        /// </summary>
        /// <param name="element">The element.</param>
        /// <param name="propertyId">The property id.</param>
        /// <returns>The value containing the property value.</returns>
        private DaValue GetPropertyValue(BrowseElement element, int propertyId)
        {
            DaValue value = new DaValue();
            value.Quality = OpcRcw.Da.Qualities.OPC_QUALITY_GOOD;
            value.Timestamp = element.CacheTimestamp;

            // check for objects - they only support the description property.
            if (element.NodeClass == NodeClass.Object)
            {
                switch (propertyId)
                {
                    case PropertyIds.Description: { value.Value = element.BrowseName; break; }
                    case PropertyIds.UaBrowseName: { value.Value = element.UaBrowseName; break; }
                    case PropertyIds.UaDescription: { value.Value = element.UaDescription; break; }

                    default:
                    {
                        value.Error = ResultIds.E_INVALID_PID;
                        return value;
                    }
                }

                return value;
            }

            // handle variable properties.
            switch (propertyId)
            {
                case PropertyIds.Description: { value.Value = element.BrowseName; break; }
                case PropertyIds.DataType: { value.Value = element.CanonicalDataType; break; }
                case PropertyIds.ScanRate: { value.Value = element.ScanRate; break; }
                case PropertyIds.AccessRights: { value.Value = element.AccessRights; break; }
                case PropertyIds.EuType: { value.Value = element.EuType; break; }
                case PropertyIds.EuInfo: { value.Value = element.EuInfo; break; }
                case PropertyIds.UaBuiltInType: { value.Value = (int)element.BuiltInType; break; }
                case PropertyIds.UaDataTypeId: { value.Value = element.DataTypeId; break; }
                case PropertyIds.UaValueRank: { value.Value = element.ValueRank; break; }
                case PropertyIds.UaBrowseName: { value.Value = element.UaBrowseName; break; }
                case PropertyIds.UaDescription: { value.Value = element.UaDescription; break; }

                case PropertyIds.EngineeringUnits:
                {
                    if (element.EngineeringUnits == null)
                    {
                        value.Error = ResultIds.E_INVALID_PID;
                        break;
                    }

                    value.Value = element.EngineeringUnits;
                    value.Value = element.EngineeringUnits;
                    break;
                }

                case PropertyIds.HighEU: 
                {
                    if (element.HighEU == Double.MaxValue)
                    {
                        value.Error = ResultIds.E_INVALID_PID;
                        break;
                    }

                    value.Value = element.HighEU;
                    break;
                }

                case PropertyIds.LowEU:
                {
                    if (element.LowEU == Double.MaxValue)
                    {
                        value.Error = ResultIds.E_INVALID_PID;
                        break;
                    }

                    value.Value = element.LowEU;
                    break;
                }

                case PropertyIds.HighIR:
                {
                    if (element.HighIR == Double.MaxValue)
                    {
                        value.Error = ResultIds.E_INVALID_PID;
                        break;
                    }

                    value.Value = element.HighIR;
                    break;
                }

                case PropertyIds.LowIR:
                {
                    if (element.LowIR == Double.MaxValue)
                    {
                        value.Error = ResultIds.E_INVALID_PID;
                        break;
                    }

                    value.Value = element.LowIR;
                    break;
                }

                case PropertyIds.CloseLabel:
                {
                    if (element.CloseLabel == null)
                    {
                        value.Error = ResultIds.E_INVALID_PID;
                        break;
                    }

                    value.Value = element.CloseLabel;
                    break;
                }

                case PropertyIds.OpenLabel:
                {
                    if (element.OpenLabel == null)
                    {
                        value.Error = ResultIds.E_INVALID_PID;
                        break;
                    }

                    value.Value = element.OpenLabel;
                    break;
                }

                case PropertyIds.TimeZone:
                {
                    if (element.TimeZone == Int32.MaxValue)
                    {
                        value.Error = ResultIds.E_INVALID_PID;
                        break;
                    }

                    value.Value = element.TimeZone;
                    break;
                }
                
                case PropertyIds.Value:
                {
                    if (element.LastValue == null)
                    {
                        value.Error = ResultIds.E_INVALID_PID;
                        break;
                    }

                    value.Value = element.LastValue.Value;
                    break;
                }

                case PropertyIds.Quality:
                {
                    if (element.LastValue == null)
                    {
                        value.Error = ResultIds.E_INVALID_PID;
                        break;
                    }

                    value.Value = element.LastValue.Quality;
                    break;
                }

                case PropertyIds.Timestamp:
                {
                    if (element.LastValue == null)
                    {
                        value.Error = ResultIds.E_INVALID_PID;
                        break;
                    }

                    value.Value = element.LastValue.Timestamp;
                    break;
                }

                default:
                {
                    value.Error = ResultIds.E_INVALID_PID;
                    break;
                }
            }

            return value;
        }

        /// <summary>
        /// Creates the browse element.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="nodeId">The node id.</param>
        /// <returns>The browse element; null if the node id does not refers to a valid element.</returns>
        private BrowseElement CreateBrowseElement(Session session, NodeId nodeId)
        {
            TraceState("CreateBrowseElement", nodeId);
            BrowseElement element = new BrowseElement();

            element.NodeId = nodeId;
            element.ItemId = m_mapper.GetLocalItemId(nodeId);

            // browse the server for the children.
            BrowseDescriptionCollection nodesToBrowse = new BrowseDescriptionCollection();
            int index = PrepareBrowseElementBrowseRequest(nodeId, nodesToBrowse);

            // browse all elements at once.
            BrowseResultCollection results = Browse(session, nodesToBrowse);

            // update the element with the children found.
            if (!UpdateBrowseElement(element, nodesToBrowse, results, index))
            {
                return null;
            }

            // read the properties from the server.
            ReadValueIdCollection nodesToRead = new ReadValueIdCollection();
            index = PrepareBrowseElementReadRequest(nodeId, element.ReferencesByName, nodesToRead, NodeClass.Unspecified, true);

            DataValueCollection values = Read(session, nodesToRead);

            // update the browse element with the property values.
            if (!UpdateBrowseElement(session.TypeTree, element, nodesToRead, values, NodeClass.Unspecified, true, index))
            {
                return null;
            }

            return element;
        }

        /// <summary>
        /// Creates the children of the browse element.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="parent">The parent.</param>
        /// <returns>
        /// The browse element; null if the node id does not refers to a valid element.
        /// </returns>
        private List<BrowseElement> LookupChildElements(Session session, BrowseElement parent)
        {
            List<BrowseElement> children = new List<BrowseElement>();
            List<BrowseElement> childrenToFind = new List<BrowseElement>();
            List<int> indexes = new List<int>();

            BrowseDescriptionCollection nodesToBrowse = new BrowseDescriptionCollection();

            // create an element for each child reference.
            foreach (ReferenceDescription reference in parent.ReferencesByName.Values)
            {
                NodeId nodeId = (NodeId)reference.NodeId;
                string itemId = m_mapper.GetLocalItemId(nodeId);

                BrowseElement child = null;

                lock (m_lock)
                {
                    // check if child has already been cached.
                    if (m_cache.TryGetValue(itemId, out child))
                    {
                        child.ParentId = parent.ItemId;
                        children.Add(child);
                        continue;
                    }

                    // create a new element.
                    child = new BrowseElement();
                }

                child.NodeId = nodeId;
                child.ItemId = itemId;
                child.NodeClass = reference.NodeClass;
                child.BrowseName = child.UaBrowseName = m_mapper.GetLocalBrowseName(reference.BrowseName);

                if (reference.DisplayName != null)
                {
                    child.BrowseName = reference.DisplayName.Text;
                }

                int index = PrepareBrowseElementBrowseRequest(child.NodeId, nodesToBrowse);

                childrenToFind.Add(child);
                indexes.Add(index);
            }

            // check if nothing to do because everything was in the cache.
            if (childrenToFind.Count == 0)
            {
                return children;
            }

            // browse all elements at once.
            BrowseResultCollection results = Browse(session, nodesToBrowse);

            // validate results and prepare read requests.
            ReadValueIdCollection nodesToRead = new ReadValueIdCollection();

            for (int ii = 0; ii < childrenToFind.Count; ii++)
            {
                BrowseElement child = childrenToFind[ii];

                // update the element with the children found.
                if (!UpdateBrowseElement(child, nodesToBrowse, results, indexes[ii]))
                {
                    children[ii] = null;
                    continue;
                }

                // all done with objects.
                if (child.NodeClass == NodeClass.Object)
                {
                    child.ParentId = parent.ItemId;
                    children.Add(child);

                    lock (m_lock)
                    {
                        child.CacheTimestamp = DateTime.UtcNow;
                        m_cache[child.ItemId] = child;
                    }
                }

                // prepare to read the properties from the server.
                indexes[ii] = PrepareBrowseElementReadRequest(child.NodeId, child.ReferencesByName, nodesToRead, child.NodeClass, true);
            }

            // check if nothing to do because only objects with no properties..
            if (nodesToRead.Count == 0)
            {
                return children;
            }

            // read all child properties at once.
            DataValueCollection values = Read(session, nodesToRead);

            // process results and build final table.
            for (int ii = 0; ii < childrenToFind.Count; ii++)
            {
                BrowseElement child = childrenToFind[ii];

                if (child == null || child.NodeClass == NodeClass.Object)
                {
                    continue;
                }

                // update the browse element with the property values.
                if (!UpdateBrowseElement(session.TypeTree, child, nodesToRead, values, child.NodeClass, true, indexes[ii]))
                {
                    continue;
                }

                // add variables to the cache.
                child.ParentId = parent.ItemId;
                children.Add(child);

                lock (m_lock)
                {
                    child.CacheTimestamp = DateTime.UtcNow;
                    m_cache[child.ItemId] = child;
                }
            }

            return children;
        }

        /// <summary>
        /// Prepares a browse request for the children of a node.
        /// </summary>
        /// <param name="nodeId">The node id.</param>
        /// <param name="nodesToBrowse">The nodes to browse.</param>
        private int PrepareBrowseElementBrowseRequest(
            NodeId nodeId,
            BrowseDescriptionCollection nodesToBrowse)
        {
            int index = nodesToBrowse.Count;

            BrowseDescription nodeToBrowse = new BrowseDescription();

            nodeToBrowse.NodeId = nodeId;
            nodeToBrowse.BrowseDirection = BrowseDirection.Forward;
            nodeToBrowse.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.Organizes;
            nodeToBrowse.IncludeSubtypes = true;
            nodeToBrowse.NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable);
            nodeToBrowse.ResultMask = (uint)(BrowseResultMask.DisplayName | BrowseResultMask.BrowseName | BrowseResultMask.NodeClass);

            nodesToBrowse.Add(nodeToBrowse);

            nodeToBrowse = new BrowseDescription();

            nodeToBrowse.NodeId = nodeId;
            nodeToBrowse.BrowseDirection = BrowseDirection.Forward;
            nodeToBrowse.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasChild;
            nodeToBrowse.IncludeSubtypes = true;
            nodeToBrowse.NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable);
            nodeToBrowse.ResultMask = (uint)(BrowseResultMask.DisplayName | BrowseResultMask.BrowseName | BrowseResultMask.NodeClass);

            nodesToBrowse.Add(nodeToBrowse);

            return index;
        }

        /// <summary>
        /// Updates the browse element with the children returned in the browse results.
        /// </summary>
        /// <param name="element">The element.</param>
        /// <param name="nodesToBrowse">The nodes to browse.</param>
        /// <param name="results">The results.</param>
        /// <param name="first">The index of the first browse result associated with the element.</param>
        /// <returns></returns>
        private bool UpdateBrowseElement(
            BrowseElement element,
            BrowseDescriptionCollection nodesToBrowse,
            BrowseResultCollection results,
            int first)
        {
            // check for a valid range within the collection.
            if (first < 0 || first >= nodesToBrowse.Count)
            {
                return false;
            }

            bool missingReferences = false;

            // process all references.
            Dictionary<string,ReferenceDescription> referencesByName = new Dictionary<string, ReferenceDescription>();
            Dictionary<string,ReferenceDescription> duplicateNames = new Dictionary<string, ReferenceDescription>();

            for (int ii = first; ii < first+2; ii++)
            {
                BrowseResult result = results[ii];

                // check for errors - rejected node id are fatal; others can be ignored.
                if (StatusCode.IsBad(result.StatusCode))
                {
                    if (result.StatusCode == StatusCodes.BadNodeIdInvalid || result.StatusCode == StatusCodes.BadNodeIdInvalid || result.StatusCode == StatusCodes.BadNodeNotInView)
                    {
                        return false;
                    }

                    missingReferences = true;
                    continue;
                }

                // eliminate duplicates and index references by browse name.
                for (int jj = 0; jj < result.References.Count; jj++)
                {
                    ReferenceDescription reference = result.References[jj];

                    // ignore off server references.
                    if (reference.NodeId == null || reference.NodeId.IsAbsolute)
                    {
                        continue;
                    }

                    // construct the browse name.
                    string browseName = m_mapper.GetLocalBrowseName(reference.BrowseName);

                    if (reference.DisplayName != null)
                    {
                        browseName = reference.DisplayName.Text;
                    }

                    // check for duplicates.
                    ReferenceDescription duplicate = null;

                    if (referencesByName.TryGetValue(browseName, out duplicate))
                    {
                        if (reference.NodeId != duplicate.NodeId)
                        {
                            duplicateNames[browseName] = duplicate;
                        }

                        continue;
                    }

                    // add to table.
                    referencesByName.Add(browseName, reference);
                }
            }

            // remove duplicates.
            foreach (string duplicateName in duplicateNames.Keys)
            {
                referencesByName.Remove(duplicateName);
            }

            // save child lookup table.
            element.ReferencesByName = referencesByName;
            element.MissingReferences = missingReferences;
            
            // update the masks.
            SetElementMasks(element);
            return true;
        }

        /// <summary>
        /// Prepares a read request for the browse element properties.
        /// </summary>
        /// <param name="nodeId">The node id.</param>
        /// <param name="childrenByName">The children indexed by name.</param>
        /// <param name="nodesToRead">The nodes to read.</param>
        /// <param name="nodeClass">The node class - passed only if all of the information in the ReferenceDescription is available.</param>
        /// <param name="onlyEssentialProperties">If true the only properties essential for browing will be fetched.</param>
        /// <returns></returns>
        private int PrepareBrowseElementReadRequest(
            NodeId nodeId,
            Dictionary<string, ReferenceDescription> childrenByName,
            ReadValueIdCollection nodesToRead,
            NodeClass nodeClass,
            bool onlyEssentialProperties)
        {
            int index = nodesToRead.Count;

            ReadValueId nodeToRead = new ReadValueId();

            if (nodeClass == NodeClass.Unspecified)
            {
                nodeToRead.NodeId = nodeId;
                nodeToRead.AttributeId = Attributes.NodeClass;
                nodesToRead.Add(nodeToRead);

                nodeToRead = new ReadValueId();
                nodeToRead.NodeId = nodeId;
                nodeToRead.AttributeId = Attributes.BrowseName;
                nodesToRead.Add(nodeToRead);

                nodeToRead = new ReadValueId();
                nodeToRead.NodeId = nodeId;
                nodeToRead.AttributeId = Attributes.DisplayName;
                nodesToRead.Add(nodeToRead);
            }

            if (!onlyEssentialProperties)
            {
                nodeToRead = new ReadValueId();
                nodeToRead.NodeId = nodeId;
                nodeToRead.AttributeId = Attributes.Description;
                nodesToRead.Add(nodeToRead);
            }

            // nothing more to fetch for objects.
            if (nodeClass == NodeClass.Object)
            {
                return index;
            }

            nodeToRead = new ReadValueId();
            nodeToRead.NodeId = nodeId;
            nodeToRead.AttributeId = Attributes.DataType;
            nodesToRead.Add(nodeToRead);

            nodeToRead = new ReadValueId();
            nodeToRead.NodeId = nodeId;
            nodeToRead.AttributeId = Attributes.ValueRank;
            nodesToRead.Add(nodeToRead);

            if (!onlyEssentialProperties)
            {
                nodeToRead = new ReadValueId();
                nodeToRead.NodeId = nodeId;
                nodeToRead.AttributeId = Attributes.MinimumSamplingInterval;
                nodesToRead.Add(nodeToRead);

                nodeToRead = new ReadValueId();
                nodeToRead.NodeId = nodeId;
                nodeToRead.AttributeId = Attributes.UserAccessLevel;
                nodesToRead.Add(nodeToRead);

                nodeToRead = new ReadValueId();
                nodeToRead.NodeId = nodeId;
                nodeToRead.AttributeId = Attributes.Value;
                nodesToRead.Add(nodeToRead);

                ReferenceDescription property = null;

                if (childrenByName.TryGetValue(Opc.Ua.BrowseNames.EURange, out property))
                {
                    nodeToRead = new ReadValueId();
                    nodeToRead.NodeId = (NodeId)property.NodeId;
                    nodeToRead.AttributeId = Attributes.Value;
                    nodeToRead.Handle = Opc.Ua.BrowseNames.EURange;
                    nodesToRead.Add(nodeToRead);
                }

                if (childrenByName.TryGetValue(Opc.Ua.BrowseNames.InstrumentRange, out property))
                {
                    nodeToRead = new ReadValueId();
                    nodeToRead.NodeId = (NodeId)property.NodeId;
                    nodeToRead.AttributeId = Attributes.Value;
                    nodeToRead.Handle = Opc.Ua.BrowseNames.InstrumentRange;
                    nodesToRead.Add(nodeToRead);
                }

                if (childrenByName.TryGetValue(Opc.Ua.BrowseNames.EngineeringUnits, out property))
                {
                    nodeToRead = new ReadValueId();
                    nodeToRead.NodeId = (NodeId)property.NodeId;
                    nodeToRead.AttributeId = Attributes.Value;
                    nodeToRead.Handle = Opc.Ua.BrowseNames.EngineeringUnits;
                    nodesToRead.Add(nodeToRead);
                }

                if (childrenByName.TryGetValue(Opc.Ua.BrowseNames.EnumStrings, out property))
                {
                    nodeToRead = new ReadValueId();
                    nodeToRead.NodeId = (NodeId)property.NodeId;
                    nodeToRead.AttributeId = Attributes.Value;
                    nodeToRead.Handle = Opc.Ua.BrowseNames.EnumStrings;
                    nodesToRead.Add(nodeToRead);
                }

                if (childrenByName.TryGetValue(Opc.Ua.BrowseNames.TrueState, out property))
                {
                    nodeToRead = new ReadValueId();
                    nodeToRead.NodeId = (NodeId)property.NodeId;
                    nodeToRead.AttributeId = Attributes.Value;
                    nodeToRead.Handle = Opc.Ua.BrowseNames.TrueState;
                    nodesToRead.Add(nodeToRead);
                }

                if (childrenByName.TryGetValue(Opc.Ua.BrowseNames.FalseState, out property))
                {
                    nodeToRead = new ReadValueId();
                    nodeToRead.NodeId = (NodeId)property.NodeId;
                    nodeToRead.AttributeId = Attributes.Value;
                    nodeToRead.Handle = Opc.Ua.BrowseNames.FalseState;
                    nodesToRead.Add(nodeToRead);
                }

                if (childrenByName.TryGetValue(Opc.Ua.BrowseNames.LocalTime, out property))
                {
                    nodeToRead = new ReadValueId();
                    nodeToRead.NodeId = (NodeId)property.NodeId;
                    nodeToRead.AttributeId = Attributes.Value;
                    nodeToRead.Handle = Opc.Ua.BrowseNames.LocalTime;
                    nodesToRead.Add(nodeToRead);
                }
            }

            return index;
        }

        /// <summary>
        /// Updates the browse element with the properties return in the read results.
        /// </summary>
        /// <param name="typeTree">The type tree.</param>
        /// <param name="element">The element.</param>
        /// <param name="nodesToRead">The nodes to read.</param>
        /// <param name="values">The values.</param>        
        /// <param name="nodeClass">The node class - passed only if all of the information in the ReferenceDescription is available.</param>
        /// <param name="onlyEssentialProperties">If true the only properties essential for browing were fetched.</param>
        /// <param name="first">The first.</param>
        /// <returns></returns>
        private bool UpdateBrowseElement(
            ITypeTable typeTree,
            BrowseElement element,
            ReadValueIdCollection nodesToRead,
            DataValueCollection values,
            NodeClass nodeClass,
            bool onlyEssentialProperties,
            int first)
        {
            // check for a valid range within the collection.
            if (first < 0 || first >= nodesToRead.Count)
            {
                return false;
            }

            if (nodeClass == NodeClass.Unspecified)
            {
                // verify node class.
                NodeClass actualNodeClass = (NodeClass)values[first++].GetValue<int>((int)NodeClass.Unspecified);

                if (actualNodeClass != NodeClass.Variable && actualNodeClass != NodeClass.Object)
                {
                    return false;
                }

                element.NodeClass = actualNodeClass;

                // verify browse name.
                QualifiedName browseName = values[first++].GetValue<QualifiedName>(null);

                if (QualifiedName.IsNull(browseName))
                {
                    return false;
                }

                element.BrowseName = element.UaBrowseName = m_mapper.GetLocalBrowseName(browseName);

                // verify display name.
                LocalizedText displayName = values[first++].GetValue<LocalizedText>(null);

                if (LocalizedText.IsNullOrEmpty(displayName))
                {
                    return false;
                }

                element.BrowseName = displayName.Text;
            }

            if (!onlyEssentialProperties)
            {
                // check if long description exists.
                LocalizedText description = values[first++].GetValue<LocalizedText>(null);

                if (!LocalizedText.IsNullOrEmpty(description))
                {
                    element.UaDescription = description.Text;
                }
                else
                {
                    element.UaDescription = "";
                }
            }

            // update the masks.
            SetElementMasks(element);

            // nothing more to do.
            if (nodeClass == NodeClass.Object)
            {
                return true;
            }

            // verify data type.
            NodeId dataTypeId = values[first++].GetValue<NodeId>(null);

            if (dataTypeId == null && element.NodeClass == NodeClass.Variable)
            {
                return false;
            }

            int valueRank = values[first++].GetValue<int>(ValueRanks.Scalar);

            // update data type information.
            if (dataTypeId != null)
            {
                element.BuiltInType = DataTypes.GetBuiltInType(dataTypeId, typeTree);
                element.DataTypeId = m_mapper.GetLocalItemId(dataTypeId);
                element.ValueRank = valueRank;
                element.CanonicalDataType = (short)ComUtils.GetVarType(new TypeInfo(element.BuiltInType, element.ValueRank));
            }

            if (!onlyEssentialProperties)
            {
                // update scan rate.
                element.ScanRate = (float)values[first++].GetValue<double>(MinimumSamplingIntervals.Indeterminate);

                // update access rights.
                byte userAccessLevel = values[first++].GetValue<byte>(0);

                if ((userAccessLevel & AccessLevels.CurrentRead) != 0)
                {
                    element.AccessRights |= OpcRcw.Da.Constants.OPC_READABLE;
                }

                if ((userAccessLevel & AccessLevels.CurrentWrite) != 0)
                {
                    element.AccessRights |= OpcRcw.Da.Constants.OPC_WRITEABLE;
                }

                if ((userAccessLevel & AccessLevels.HistoryRead) != 0)
                {
                    element.IsHistoricalItem = true;
                }

                // cache the latest value.
                DataValue value = values[first++];

                if (element.NodeClass == NodeClass.Variable)
                {
                    element.LastValue = m_mapper.GetLocalDataValue(value);
                }

                // update HighEU and LowEU
                element.EuType = (int)OpcRcw.Da.OPCEUTYPE.OPC_NOENUM;
                element.HighEU = Double.MaxValue;
                element.LowEU = Double.MaxValue;

                if (element.ReferencesByName.ContainsKey(Opc.Ua.BrowseNames.EURange))
                {
                    Range euRange = values[first++].GetValue<Range>(null);

                    if (euRange != null)
                    {
                        element.EuType = (int)OpcRcw.Da.OPCEUTYPE.OPC_ANALOG;
                        element.HighEU = euRange.High;
                        element.LowEU = euRange.Low;
                    }
                }

                // update HighIR and LowIR
                element.HighIR = Double.MaxValue;
                element.LowIR = Double.MaxValue;

                if (element.ReferencesByName.ContainsKey(Opc.Ua.BrowseNames.InstrumentRange))
                {
                    Range instrumentRange = values[first++].GetValue<Range>(null);

                    if (instrumentRange != null)
                    {
                        element.HighIR = instrumentRange.High;
                        element.LowIR = instrumentRange.Low;
                    }
                }

                // update EngineeringUnits
                element.EngineeringUnits = null;

                if (element.ReferencesByName.ContainsKey(Opc.Ua.BrowseNames.EngineeringUnits))
                {
                    EUInformation engineeringUnits = values[first++].GetValue<EUInformation>(null);

                    if (engineeringUnits != null && engineeringUnits.DisplayName != null)
                    {
                        element.EngineeringUnits = engineeringUnits.DisplayName.Text;
                    }
                }

                // update EUInfo
                element.EuInfo = null;

                if (element.ReferencesByName.ContainsKey(Opc.Ua.BrowseNames.EnumStrings))
                {
                    LocalizedText[] enumStrings = values[first++].GetValue<LocalizedText[]>(null);

                    if (enumStrings != null)
                    {
                        string[] strings = new string[enumStrings.Length];

                        for (int ii = 0; ii < enumStrings.Length; ii++)
                        {
                            if (enumStrings[ii] != null)
                            {
                                strings[ii] = enumStrings[ii].Text;
                            }
                        }

                        element.EuType = (int)OpcRcw.Da.OPCEUTYPE.OPC_ENUMERATED;
                        element.EuInfo = strings;
                    }
                }

                // update CloseLabel
                element.CloseLabel = null;

                if (element.ReferencesByName.ContainsKey(Opc.Ua.BrowseNames.TrueState))
                {
                    LocalizedText trueState = values[first++].GetValue<LocalizedText>(null);

                    if (trueState != null)
                    {
                        element.CloseLabel = trueState.Text;
                    }
                }

                // update OpenLabel
                element.OpenLabel = null;

                if (element.ReferencesByName.ContainsKey(Opc.Ua.BrowseNames.FalseState))
                {
                    LocalizedText falseState = values[first++].GetValue<LocalizedText>(null);

                    if (falseState != null)
                    {
                        element.OpenLabel = falseState.Text;
                    }
                }

                // update TimeZone
                element.TimeZone = Int32.MaxValue;

                if (element.ReferencesByName.ContainsKey(Opc.Ua.BrowseNames.LocalTime))
                {
                    TimeZoneDataType timeZone = values[first++].GetValue<TimeZoneDataType>(null);

                    if (timeZone != null)
                    {
                        element.TimeZone = timeZone.Offset;
                    }
                }
            }

            return true;
        }

        /// <summary>
        /// Sets the element masks.
        /// </summary>
        /// <param name="element">The element to update.</param>
        private void SetElementMasks(BrowseElement element)
        {
            element.HasChildren = (element.ReferencesByName.Count > 0);
            element.IsItem = (element.NodeClass == NodeClass.Variable);
        }
        
        /// <summary>
        /// Sends the browse request to the server.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="nodesToBrowse">The nodes to browse.</param>
        /// <returns></returns>
        private BrowseResultCollection Browse(Session session, BrowseDescriptionCollection nodesToBrowse)
        {
            BrowseResultCollection results = null;

            // break the request into smaller blocks.
            if (m_browseBlockSize > 0 && nodesToBrowse.Count > m_browseBlockSize)
            {
                results = new BrowseResultCollection();

                for (int ii = 0; ii < nodesToBrowse.Count; ii += m_browseBlockSize)
                {
                    BrowseDescriptionCollection x = new BrowseDescriptionCollection();

                    for (int jj = ii; jj < ii + m_browseBlockSize && jj < nodesToBrowse.Count; jj++)
                    {
                        x.Add(nodesToBrowse[jj]);
                    }

                    BrowseResultCollection y = BrowseBlock(session, x);
                    results.AddRange(y);
                }

                return results;
            }

            // small enough to do directly.
            return BrowseBlock(session, nodesToBrowse);
        }

        /// <summary>
        /// Sends the browse request to the server.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="nodesToBrowse">The nodes to browse.</param>
        /// <returns></returns>
        private BrowseResultCollection BrowseBlock(Session session, BrowseDescriptionCollection nodesToBrowse)
        {
            try
            {
                // Utils.Trace("Browsing {0} Nodes", nodesToBrowse.Count);

                ViewDescription view = new ViewDescription();
                Dictionary<int,BrowseResult> combinedResults = new Dictionary<int, BrowseResult>();

                // initialize the table of indexes used to correlate results.   
                BrowseDescriptionCollection browseOperations = nodesToBrowse;             
                List<int> browseIndexes = new List<int>();

                for (int ii = 0; ii < nodesToBrowse.Count; ii++)
                {
                    browseIndexes.Add(ii);
                }

                BrowseDescriptionCollection unprocessedOperations = new BrowseDescriptionCollection();
                List<int> unprocessedBrowseIndexes = new List<int>();

                while (browseOperations.Count > 0)
                {
                    // start the browse operation.
                    BrowseResultCollection results = null;
                    DiagnosticInfoCollection diagnosticInfos = null;

                    session.Browse(
                        null,
                        view,
                        0,
                        browseOperations,
                        out results,
                        out diagnosticInfos);

                    ClientBase.ValidateResponse(results, browseOperations);
                    ClientBase.ValidateDiagnosticInfos(diagnosticInfos, browseOperations);

                    unprocessedOperations.Clear();
                    unprocessedBrowseIndexes.Clear();

                    ByteStringCollection continuationPoints = new ByteStringCollection();
                    List<int> continuationPointIndexes = new List<int>();

                    for (int ii = 0; ii < browseOperations.Count; ii++)
                    {
                        int index = browseIndexes[ii];

                        // Utils.Trace("{0}/{1}/{2}", browseOperations[ii].NodeId, browseOperations[ii].ReferenceTypeId, results[ii].References.Count);

                        // look up results.
                        BrowseResult combinedResult = null;

                        if (!combinedResults.TryGetValue(index, out combinedResult))
                        {
                            combinedResults[index] = combinedResult = new BrowseResult();
                        }

                        // check for error.
                        if (StatusCode.IsBad(results[ii].StatusCode))
                        {
                            // this error indicates that the server does not have enough simultaneously active 
                            // continuation points. This request will need to be resent after the other operations
                            // have been completed and their continuation points released.
                            if (results[ii].StatusCode == StatusCodes.BadNoContinuationPoints)
                            {
                                unprocessedOperations.Add(browseOperations[ii]);
                                unprocessedBrowseIndexes.Add(index);
                                continue;
                            }

                            // save error.
                            if (StatusCode.IsGood(combinedResult.StatusCode))
                            {
                                combinedResult.StatusCode = results[ii].StatusCode;
                            }

                            continue;
                        }

                        // check if all references have been fetched.
                        if (results[ii].References.Count == 0)
                        {
                            continue;
                        }

                        // save results.
                        combinedResult.References.AddRange(results[ii].References);

                        // check for continuation point.
                        if (results[ii].ContinuationPoint != null && results[ii].ContinuationPoint.Length > 0)
                        {
                            continuationPoints.Add(results[ii].ContinuationPoint);
                            continuationPointIndexes.Add(index);
                        }
                    }

                    // process continuation points.
                    ByteStringCollection revisedContinuationPoints = new ByteStringCollection();
                    List<int> revisedContinuationPointIndexes = new List<int>();

                    while (continuationPoints.Count > 0)
                    {
                        bool releaseContinuationPoints = false;

                        // continue browse operation.
                        session.BrowseNext(
                            null,
                            releaseContinuationPoints,
                            continuationPoints,
                            out results,
                            out diagnosticInfos);

                        ClientBase.ValidateResponse(results, continuationPoints);
                        ClientBase.ValidateDiagnosticInfos(diagnosticInfos, continuationPoints);

                        revisedContinuationPoints.Clear();
                        revisedContinuationPointIndexes.Clear();

                        for (int ii = 0; ii < continuationPoints.Count; ii++)
                        {
                            int index = continuationPointIndexes[ii];

                            // look up results.
                            BrowseResult combinedResult = null;

                            if (!combinedResults.TryGetValue(index, out combinedResult))
                            {
                                combinedResults[index] = new BrowseResult();
                            }

                            // check for error.
                            if (StatusCode.IsBad(results[ii].StatusCode))
                            {
                                // save error.
                                if (StatusCode.IsGood(combinedResult.StatusCode))
                                {
                                    combinedResult.StatusCode = results[ii].StatusCode;
                                }

                                continue;
                            }

                            // check if all references have been fetched.
                            if (results[ii].References.Count == 0)
                            {
                                continue;
                            }

                            // save results.
                            combinedResult.References.AddRange(results[ii].References);

                            // check for continuation point.
                            if (results[ii].ContinuationPoint != null && results[ii].ContinuationPoint.Length > 0)
                            {
                                revisedContinuationPoints.Add(results[ii].ContinuationPoint);
                                revisedContinuationPointIndexes.Add(index);
                            }
                        }

                        // check if browsing must continue;
                        continuationPoints = revisedContinuationPoints;
                        continuationPointIndexes = revisedContinuationPointIndexes;
                    }

                    // check if unprocessed results exist.
                    browseOperations = unprocessedOperations;
                    browseIndexes = unprocessedBrowseIndexes;
                }

                // reconstruct list of combined results.
                BrowseResultCollection finalResults = new BrowseResultCollection();

                for (int ii = 0; ii < nodesToBrowse.Count; ii++)
                {
                    BrowseResult combinedResult = null;

                    if (!combinedResults.TryGetValue(ii, out combinedResult))
                    {
                        combinedResult = new BrowseResult();
                    }

                    finalResults.Add(combinedResult);
                }

                // return complete list.
                return finalResults;
            }
            catch (Exception e)
            {
                throw ComUtils.CreateComException(e, ResultIds.E_FAIL);
            }
        }
        
        /// <summary>
        /// Sends the read request to the server.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="nodesToRead">The nodes to read.</param>
        /// <returns></returns>
        private DataValueCollection Read(Session session, ReadValueIdCollection nodesToRead)
        {
            // read attribute values from the server.
            DataValueCollection results = null;
            DiagnosticInfoCollection diagnosticInfos = null;

            try
            {
                session.Read(
                    null,
                    0,
                    TimestampsToReturn.Source,
                    nodesToRead,
                    out results,
                    out diagnosticInfos);

                ClientBase.ValidateResponse(results, nodesToRead);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);
            }
            catch (Exception e)
            {
                // convert to item level errors.
                ServiceResult error = new ServiceResult(e, StatusCodes.BadUnexpectedError);

                results = new DataValueCollection();

                for (int ii = 0; ii < nodesToRead.Count; ii++)
                {
                    results.Add(new DataValue(error.StatusCode));
                }
            }

            return results;
        }
        #endregion

        #region BrowseElement Class
        /// <summary>
        /// Stores a element in the address space.
        /// </summary>
        private class BrowseElement : ComDaBrowseElement
        {
            public NodeId NodeId;
            public NodeClass NodeClass;
            public string UaBrowseName;
            public string UaDescription;
            public BuiltInType BuiltInType;
            public string DataTypeId;
            public int ValueRank;
            public short CanonicalDataType;
            public DaValue LastValue;
            public float ScanRate;
            public int AccessRights;
            public int EuType;
            public string[] EuInfo;
            public double HighEU;
            public double LowEU;
            public double HighIR;
            public double LowIR;
            public string EngineeringUnits;
            public string CloseLabel;
            public string OpenLabel;
            public int TimeZone;
            public DateTime CacheTimestamp;
            public Dictionary<string,ReferenceDescription> ReferencesByName;
            public bool MissingReferences;
            public string ParentId;
        }
        #endregion

        #region Private Fields
        private object m_lock = new object();
        private ComNamespaceMapper m_mapper;
        private Dictionary<string,BrowseElement> m_cache;
        private int m_browseBlockSize;
        #endregion
    }
}
