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
using System.Runtime.InteropServices;
using Opc.Ua.Client;

namespace Opc.Ua.Com.Server
{
    /// <summary>
    /// A class which provides the COM DA browse features.
    /// </summary>
    public class ComDaBrowseManager
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ComDaBrowseManager"/> class.
        /// </summary>
        /// <param name="mapper">The object used to map namespace indexes.</param>
        /// <param name="cache">The cache.</param>
        public ComDaBrowseManager(ComNamespaceMapper mapper, ComDaBrowseCache cache)
		{
            m_mapper = mapper;
            m_cache = cache;
            m_continuationPoints = new Dictionary<string, ContinuationPoint>();
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Browses up.
        /// </summary>
        /// <param name="session">The session.</param>
        public void BrowseUp(Session session)
        {
            TraceState("BrowseUp");

            // find the id of the current node.
            string itemId = null;
                       
            lock (m_lock)
            {
                // check if already at root.
                if (m_browsePosition == null || String.IsNullOrEmpty(m_browsePosition.ItemId))
                {
                    throw ComUtils.CreateComException(ResultIds.E_FAIL);
                }

                itemId = m_browsePosition.ItemId;
            }

            // find the parent - revert to root if parent does not exist.
            ComDaBrowseElement parent = m_cache.FindParent(session, itemId);
            
            lock (m_lock)
            {
                m_browsePosition = parent; 
            }
        }

        /// <summary>
        /// Moves the current browse position down.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="targetName">Name of the target.</param>
        public void BrowseDown(Session session, string targetName)
        {
            TraceState("BrowseDown", targetName);

            // find the id of the current element.
            string itemId = null;

            lock (m_lock)
            {
                if (m_browsePosition != null)
                {
                    itemId = m_browsePosition.ItemId;
                }
            }

            // try to fetch the child.
            ComDaBrowseElement child = m_cache.FindChild(session, itemId, targetName);

            if (child != null)
            {
                TraceState("child", child.ItemId, child.BrowseName, child.IsItem, child.HasChildren);
            }

            if (child == null || (!child.HasChildren && child.IsItem))
            {
                throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
            }

            // update the browse position.
            lock (m_lock)
            {
                m_browsePosition = child;
            }
        }

        /// <summary>
        /// Moves the current browse position to the specified item.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="itemId">The item id.</param>
        public void BrowseTo(Session session, string itemId)
        {
            TraceState("BrowseTo", itemId);

            // try to fetch the target.
            ComDaBrowseElement target = m_cache.FindElement(session, itemId);

            if (target == null)
            {
                BrowseDown(session, itemId);
                return;
            }

            if (!target.HasChildren)
            {
                throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
            }

            // update the browse position.
            lock (m_lock)
            {
                m_browsePosition = target;
            }
        }

        /// <summary>
        /// Gets the browse position.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <returns></returns>
        public ComDaBrowseElement GetBrowsePosition(Session session)
        {
            return m_browsePosition;
        }
        
        /// <summary>
        /// Browses for children of the specified item.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="elementFilter">The element filter.</param>
        /// <param name="nameFilter">The name filter.</param>
        /// <param name="dataTypeFilter">The data type filter.</param>
        /// <param name="accessRightsFilter">The access rights filter.</param>
        /// <returns>
        /// The list of names that meet the criteria.
        /// </returns>
        public IList<string> BrowseForNames(
            Session session,
            BrowseElementFilter elementFilter,
            string nameFilter,
            short dataTypeFilter,
            int accessRightsFilter)
        {
            // find the id of the current element.
            string itemId = null;

            lock (m_lock)
            {
                if (m_browsePosition != null)
                {
                    itemId = m_browsePosition.ItemId;
                }
            }

            // find the names.
            IList<string> hits = m_cache.BrowseForNames(
                session,
                itemId, 
                elementFilter, 
                nameFilter, 
                dataTypeFilter, 
                accessRightsFilter);

            if (hits == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_UNKNOWNITEMID);
            }

            // return the names.
            return hits;
        }

        /// <summary>
        /// Browses for children of the specified item.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="nameFilter">The name filter.</param>
        /// <param name="dataTypeFilter">The data type filter.</param>
        /// <param name="accessRightsFilter">The access rights filter.</param>
        /// <returns>
        /// The list of names that meet the criteria.
        /// </returns>
        public IList<string> BrowseFlat(
            Session session,
            string nameFilter,
            short dataTypeFilter,
            int accessRightsFilter)
        {
            // find the id of the current element.
            string itemId = null;

            lock (m_lock)
            {
                if (m_browsePosition != null)
                {
                    itemId = m_browsePosition.ItemId;
                }
            }

            // find the item ids.
            IList<string> hits = m_cache.BrowseFlat(
                session,
                itemId,
                nameFilter,
                dataTypeFilter,
                accessRightsFilter);

            if (hits == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_UNKNOWNITEMID);
            }

            // return the item ids.
            return hits;
        }

        /// <summary>
        /// Browses for the children of the specified item.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="itemId">The item id.</param>
        /// <param name="continuationPoint">The continuation point.</param>
        /// <param name="maxElementsReturned">The max elements returned.</param>
        /// <param name="elementFilter">The element filter.</param>
        /// <param name="nameFilter">The name filter.</param>
        /// <param name="revisedContinuationPoint">The revised continuation point.</param>
        /// <returns>The list of elements that meet the criteria.</returns>
        public List<ComDaBrowseElement> BrowseForElements(
            Session session,
            string itemId,
            string continuationPoint,
            int maxElementsReturned,
            int elementFilter,
            string nameFilter,
            out string revisedContinuationPoint)
        {
            TraceState("BrowseForElements", itemId, continuationPoint, maxElementsReturned, elementFilter, nameFilter);

            revisedContinuationPoint = String.Empty;

            // look up continuation point.
            ContinuationPoint cp = null;

            if (!String.IsNullOrEmpty(continuationPoint))
            {
                if (!m_continuationPoints.TryGetValue(continuationPoint, out cp))
                {
                    throw ComUtils.CreateComException(ResultIds.E_INVALIDCONTINUATIONPOINT);
                }

                m_continuationPoints.Remove(continuationPoint);
            }

            // get the element queue.
            Queue<ComDaBrowseElement> elements = null;

            // get element from continuation point.
            if (cp != null)
            {
                elements = cp.Elements;
            }

            // get list from cache.
            else
            {
                elements = m_cache.BrowseForElements(session, itemId);

                // check if nothing found.
                if (elements == null)
                {
                    throw ComUtils.CreateComException(ResultIds.E_UNKNOWNITEMID);
                }
            }

            // apply filters.
            List<ComDaBrowseElement> hits = new List<ComDaBrowseElement>();

            while (elements.Count > 0)
            {
                ComDaBrowseElement hit = elements.Dequeue();

                // apply name filter.
                if (!String.IsNullOrEmpty(nameFilter))
                {
                    if (!ComUtils.Match(hit.BrowseName, nameFilter, true))
                    {
                        continue;
                    }
                }

                // apply element filter
                if (elementFilter == (int)OpcRcw.Da.OPCBROWSEFILTER.OPC_BROWSE_FILTER_BRANCHES)
                {
                    if (!hit.HasChildren)
                    {
                        continue;
                    }
                }

                if (elementFilter == (int)OpcRcw.Da.OPCBROWSEFILTER.OPC_BROWSE_FILTER_ITEMS)
                {
                    if (!hit.IsItem)
                    {
                        continue;
                    }
                }

                // check max reached.
                if (maxElementsReturned > 0 && hits.Count == maxElementsReturned)
                {
                    elements.Enqueue(hit);

                    cp = new ContinuationPoint();
                    cp.Id = Guid.NewGuid().ToString();
                    cp.Elements = elements;

                    m_continuationPoints.Add(cp.Id, cp);
                    revisedContinuationPoint = cp.Id;
                    break;
                }

                // add the result.
                hits.Add(hit);
            }

            // return results.
            return hits;
        }

        /// <summary>
        /// Gets the item id for the specified browse element.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="browseName">The name of the browse element.</param>
        /// <returns>Null if the browseName is not valid for the current position.</returns>
        public string GetItemId(Session session, string browseName)
        {
            TraceState("GetItemId", browseName);

            // find the id of the current element.
            string itemId = null;

            lock (m_lock)
            {
                if (m_browsePosition != null)
                {
                    itemId = m_browsePosition.ItemId;
                }
            }

            // return the current element.
            if (String.IsNullOrEmpty(browseName))
            {
                return itemId;
            }

            // try to fetch the child.
            ComDaBrowseElement child = m_cache.FindChild(session, itemId, browseName);

            if (child == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
            }

            // return child id.
            return child.ItemId;
        }

        /// <summary>
        /// Gets the properties.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="itemId">The item id.</param>
        /// <returns>The list of properities.</returns>
        public IList<DaProperty> GetAvailableProperties(Session session, string itemId)
        {
            IList<DaProperty> properties = m_cache.GetAvailableProperties(session, itemId);

            if (properties == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_UNKNOWNITEMID);
            }

            return properties;
        }

        /// <summary>
        /// Gets the item ids for the properties.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="itemId">The item id.</param>
        /// <param name="propertyIds">The property ids.</param>
        /// <param name="itemIds">The item ids.</param>
        /// <returns>Any errors.</returns>
        public IList<int> GetItemIds(Session session, string itemId, int[] propertyIds, out string[] itemIds)
        {
            TraceState("GetItemId", itemId);

            // no properties for the root.
            if (String.IsNullOrEmpty(itemId))
            {
                throw ComUtils.CreateComException(ResultIds.E_INVALIDITEMID);
            }

            // select all available properties if none provided.
            IList<DaProperty> properties = GetAvailableProperties(session, itemId);

            if (propertyIds == null || propertyIds.Length == 0)
            {
                propertyIds = new int[properties.Count];

                for (int ii = 0; ii < propertyIds.Length; ii++)
                {
                    propertyIds[ii] = properties[ii].PropertyId;
                }
            }

            itemIds = new string[propertyIds.Length];
            int[] results = new int[propertyIds.Length];

            for (int ii = 0; ii < propertyIds.Length; ii++)
            {
                results[ii] = ResultIds.E_INVALID_PID;

                // must return E_INVALID_PID for standard properties.
                if (propertyIds[ii] <= PropertyIds.EuInfo)
                {
                    continue;
                }

                // supported properties must return E_FAIL.
                for (int jj = 0; jj < properties.Count; jj++)
                {
                    if (properties[jj].PropertyId == propertyIds[ii])
                    {
                        results[ii] = ResultIds.E_FAIL;
                        break;
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Gets the properties for list of items.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="requests">The requests.</param>
        /// <param name="propertyIds">The property ids.</param>
        /// <returns>The list of properities.</returns>
        public IList<DaProperty> GetPropertyValues(Session session, ComDaReadPropertiesRequest[] requests, params int[] propertyIds)
        {
            return m_cache.GetPropertyValues(session, requests, propertyIds);
        }

        /// <summary>
        /// Gets the property values for a single item.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="itemId">The item id.</param>
        /// <param name="propertyIds">The property ids.</param>
        /// <returns>The list of properities.</returns>
        public DaValue[] GetPropertyValues(Session session, string itemId, params int[] propertyIds)
        {
            TraceState("GetPropertyValues", itemId);

            ComDaReadPropertiesRequest request = new ComDaReadPropertiesRequest();
            request.ItemId = itemId;

            GetPropertyValues(session, new ComDaReadPropertiesRequest[] { request }, propertyIds);
            
            return request.Values;
        }
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

            buffer.AppendFormat("ComDaBrowser::{0}", context);

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
        #endregion

        #region ContinuationPoint Class
        /// <summary>
        /// Stores a continuation point.
        /// </summary>
        private class ContinuationPoint
        {
            public string Id;
            public Queue<ComDaBrowseElement> Elements;
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
           new DaProperty(PropertyIds.ScanRate),
           new DaProperty(PropertyIds.AccessRights),
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

        #region Private Fields
        private object m_lock = new object();
        private ComNamespaceMapper m_mapper;
        private ComDaBrowseElement m_browsePosition;
        private Dictionary<string,ContinuationPoint> m_continuationPoints;
        private ComDaBrowseCache m_cache;
        #endregion
	}
}
