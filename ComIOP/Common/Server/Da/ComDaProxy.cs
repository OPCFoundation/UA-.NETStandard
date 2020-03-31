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
            m_mapper = new ComNamespaceMapper();
            m_browseCacheManager = new ComDaBrowseCache(m_mapper);
            m_browseManager = new ComDaBrowseManager(m_mapper, m_browseCacheManager);
            m_groupManager = new ComDaGroupManager(m_mapper, m_browseManager);
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
                    Utils.SilentDispose(m_groupManager);
                    m_groupManager = null;
                }
            }

            base.Dispose(disposing);
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Called when a new session is created.
        /// </summary>
        protected override void OnSessionCreated()
        {
            lock (Lock)
            {
                // fetch the configuration.
                m_configuration = Endpoint.ParseExtension<ComProxyConfiguration>(null);

                if (m_configuration == null)
                {
                    m_configuration = new ComProxyConfiguration();
                }

                // update the mapping and pass the new session to other objects.
                m_mapper.Initialize(Session, m_configuration);
                m_groupManager.Session = Session;
                m_browseCacheManager.BrowseBlockSize = m_configuration.BrowseBlockSize;

                // save the configuration.
                Endpoint.UpdateExtension<ComProxyConfiguration>(null, m_configuration);
                SaveConfiguration();
            }
        }

        /// <summary>
        /// Called when a session is reconnected.
        /// </summary>
        protected override void OnSessionReconected()
        {
            // TBD
        }

        /// <summary>
        /// Called when a session is removed.
        /// </summary>
        protected override void OnSessionRemoved()
        {
            m_groupManager.OnSessionRemoved();            
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
                return m_groupManager.GroupCount;
            }
        }

        /// <summary>
        /// Gets the last update time.
        /// </summary>
        /// <value>The last update time.</value>
        public DateTime LastUpdateTime
        {
            get
            {
                return m_groupManager.LastUpdateTime;
            }
        }

        /// <summary>
        /// Adds the group.
        /// </summary>
        /// <param name="groupName">The group name.</param>
        /// <param name="active">if set to <c>true</c> the group is active.</param>
        /// <param name="updateRate">The update rate.</param>
        /// <param name="clientHandle">The client handle.</param>
        /// <param name="timeBias">The time bias.</param>
        /// <param name="deadband">The deadband.</param>
        /// <param name="lcid">The lcid.</param>
        /// <returns>The new group.</returns>
        public ComDaGroup AddGroup(
            string groupName,
            bool active,
            int updateRate,
            int clientHandle,
            int timeBias,
            float deadband,
            int lcid)
        {
            return m_groupManager.AddGroup(groupName, active, updateRate, clientHandle, timeBias, deadband, lcid);
        }

        /// <summary>
        /// Removes the group.
        /// </summary>
        /// <param name="group">The group.</param>
        public void RemoveGroup(ComDaGroup group)
        {
            m_groupManager.RemoveGroup(group);
        }

        /// <summary>
        /// Gets the group with the specified name.
        /// </summary>
        /// <param name="groupName">Name of the group.</param>
        /// <returns>The group. Null if it does not exist.</returns>
        public ComDaGroup GetGroupByName(string groupName)
        {
            return m_groupManager.GetGroupByName(groupName);
        }

        /// <summary>
        /// Gets the group by handle.
        /// </summary>
        /// <param name="serverHandle">The server handle.</param>
        /// <returns>The group.</returns>
        public ComDaGroup GetGroupByHandle(int serverHandle)
        {
            return m_groupManager.GetGroupByHandle(serverHandle);
        }

        /// <summary>
        /// Returns the current set of groups.
        /// </summary>
        /// <returns>The list of groups.</returns>
        public ComDaGroup[] GetGroups()
        {
            return m_groupManager.GetGroups();
        }

        /// <summary>
        /// Reads the values for the specified item ids.
        /// </summary>
        /// <param name="itemIds">The item ids.</param>
        /// <returns>The values.</returns>
        public DaValue[] Read(string[] itemIds)
        {
            ReadValueIdCollection valuesToRead = new ReadValueIdCollection();

            for (int ii = 0; ii < itemIds.Length; ii++)
            {
                ReadValueId valueToRead = new ReadValueId();

                valueToRead.NodeId = m_mapper.GetRemoteNodeId(itemIds[ii]);
                valueToRead.AttributeId = Attributes.Value;

                valuesToRead.Add(valueToRead);
            }

            return m_groupManager.Read(valuesToRead);
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
            WriteValueCollection valuesToWrite = new WriteValueCollection();

            ComDaReadPropertiesRequest[] requests = new ComDaReadPropertiesRequest[values.Length];
            
            // prepare request.
            for (int ii = 0; ii < itemIds.Length; ii++)
            {
                ComDaReadPropertiesRequest request = requests[ii] = new ComDaReadPropertiesRequest();
                request.ItemId = itemIds[ii];
            }

            // need to get the data type of the remote node.
            m_browseManager.GetPropertyValues(Session, requests, PropertyIds.UaBuiltInType, PropertyIds.UaValueRank);

            // validate items.
            for (int ii = 0; ii < requests.Length; ii++)
            {
                ComDaReadPropertiesRequest request = requests[ii];

                if (request.Error < 0)
                {
                    results[ii] = request.Error;
                    continue;
                }

                int? builtInType = request.Values[0].Value as int?;
                int? valueRank = request.Values[1].Value as int?;

                if (builtInType == null || valueRank == null)
                {
                    results[ii] = ResultIds.E_UNKNOWNITEMID;
                    continue;
                }

                // convert value to UA data type.
                WriteValue valueToWrite = new WriteValue();

                valueToWrite.NodeId = m_mapper.GetRemoteNodeId(itemIds[ii]);
                valueToWrite.AttributeId = Attributes.Value;
                valueToWrite.Handle = ii;

                // convert value to UA data type.
                try
                {
                    TypeInfo remoteType = new TypeInfo((BuiltInType)builtInType.Value, valueRank.Value);
                    valueToWrite.Value = m_mapper.GetRemoteDataValue(values[ii], remoteType);
                }
                catch (Exception e)
                {
                    results[ii] = ComUtils.GetErrorCode(e, ResultIds.E_BADTYPE);
                    continue;
                }

                valuesToWrite.Add(valueToWrite);
            }

            // check if nothing to do.
            if (valuesToWrite.Count  == 0)
            {
                return results;
            }

            // write the values to the server.
            int[] remoteResults = m_groupManager.Write(valuesToWrite);

            // copy results.
            for (int ii = 0; ii < valuesToWrite.Count; ii++)
            {
                results[(int)valuesToWrite[ii].Handle] = remoteResults[ii];
            }

            return results;
        }
        #endregion
                
        #region Browse Implementation
        /// <summary>
        /// Moves the current browse position up.
        /// </summary>
        public void BrowseUp()
        {
            Session session = Session;

            if (session == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_FAIL);
            }

            m_browseManager.BrowseUp(session);
        }

        /// <summary>
        /// Moves the current browse position down.
        /// </summary>
        public void BrowseDown(string targetName)
        {
            Session session = Session;

            if (session == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_FAIL);
            }

            m_browseManager.BrowseDown(session, targetName);
        }

        /// <summary>
        /// Moves the current browse position to the specified item.
        /// </summary>
        public void BrowseTo(string itemId)
        {
            Session session = Session;

            if (session == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_FAIL);
            }

            m_browseManager.BrowseTo(session, itemId);           
        }

        /// <summary>
        /// Browses the current branch.
        /// </summary>
        /// <param name="isBranch">if set to <c>true</c> the return branches.</param>
        /// <param name="filter">The filter.</param>
        /// <param name="dataTypeFilter">The data type filter.</param>
        /// <param name="accessRightsFilter">The access rights filter.</param>
        /// <returns>The list of names that meet the criteria.</returns>
        public IList<string> BrowseForNames(bool isBranch, string filter, short dataTypeFilter, int accessRightsFilter)
        {
            Session session = Session;

            if (session == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_FAIL);
            }

            return m_browseManager.BrowseForNames(
                session, 
                (isBranch)?BrowseElementFilter.Branch:BrowseElementFilter.Item,
                filter, 
                dataTypeFilter, 
                accessRightsFilter);
        }

        /// <summary>
        /// Browse for all items below the current branch.
        /// </summary>
        /// <param name="filter">The filter.</param>
        /// <param name="dataTypeFilter">The data type filter.</param>
        /// <param name="accessRightsFilter">The access rights filter.</param>
        /// <returns>
        /// The list of item ids that meet the criteria.
        /// </returns>
        public IList<string> BrowseForItems(string filter, short dataTypeFilter, int accessRightsFilter)
        {
            Session session = Session;

            if (session == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_FAIL);
            }

            return m_browseManager.BrowseFlat(
                session,
                filter,
                dataTypeFilter,
                accessRightsFilter);
        }

        /// <summary>
        /// Browses the specified item id.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="continuationPoint">The continuation point.</param>
        /// <param name="maxElementsReturned">The max elements returned.</param>
        /// <param name="elementTypeMask">The element type mask.</param>
        /// <param name="nameFilter">The name filter.</param>
        /// <param name="revisedContinuationPoint">The revised continuation point.</param>
        /// <returns></returns>
        public IList<ComDaBrowseElement> BrowseForElements(
            string itemId,
            string continuationPoint,
            int maxElementsReturned,
            int elementTypeMask,
            string nameFilter,
            out string revisedContinuationPoint)
        {
            Session session = Session;

            if (session == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_FAIL);
            }

            return m_browseManager.BrowseForElements(
                session, 
                itemId,
                continuationPoint, 
                maxElementsReturned,
                elementTypeMask, 
                nameFilter, 
                out revisedContinuationPoint);
        }

        /// <summary>
        /// Gets the item id for the specified browse element.
        /// </summary>
        /// <param name="browseName">The name of the browse element.</param>
        /// <returns></returns>
        public string GetItemId(string browseName)
        {
            Session session = Session;

            if (session == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_FAIL);
            }

            return m_browseManager.GetItemId(session, browseName);
        }

        /// <summary>
        /// Gets the properties.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <returns>The list of properities.</returns>
        public IList<DaProperty> GetProperties(string itemId)
        {
            Session session = Session;

            if (session == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_FAIL);
            }

            return m_browseManager.GetAvailableProperties(session, itemId);
        }

        /// <summary>
        /// Gets the properties.
        /// </summary>
        /// <param name="requests">The requests.</param>
        /// <param name="propertyIds">The property ids.</param>
        /// <returns>The list of properities.</returns>
        public IList<DaProperty> GetProperties(ComDaReadPropertiesRequest[] requests, params int[] propertyIds)
        {
            Session session = Session;

            if (session == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_FAIL);
            }

            return m_browseManager.GetPropertyValues(session, requests, propertyIds);
        }

        /// <summary>
        /// Gets the property values.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="propertyIds">The property ids.</param>
        /// <returns>The property values.</returns>
        public DaValue[] GetPropertyValues(string itemId, int[] propertyIds)
        {
            Session session = Session;

            if (session == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_FAIL);
            }

            return m_browseManager.GetPropertyValues(session, itemId, propertyIds);
        }

        /// <summary>
        /// Gets the item ids for the properties.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="propertyIds">The property ids.</param>
        /// <param name="itemIds">The item ids.</param>
        /// <returns>Any errors.</returns>
        public IList<int> GetItemIds(string itemId, int[] propertyIds, out string[] itemIds)
        {
            Session session = Session;

            if (session == null)
            {
                throw ComUtils.CreateComException(ResultIds.E_FAIL);
            }

            return m_browseManager.GetItemIds(session, itemId, propertyIds, out itemIds);
        }
        #endregion

        #region Static Functions
        /// <summary>
        /// Converts a StatusCode returned during a Read to an HRESULT.
        /// </summary>
        internal static int MapPropertyReadStatusToErrorCode(StatusCode statusCode)
        {
            // map good status.
            if (StatusCode.IsGood(statusCode))
            {
                return ResultIds.S_OK;
            }

            // map bad status codes.
            if (StatusCode.IsBad(statusCode))
            {
                switch (statusCode.Code)
                {
                    case StatusCodes.BadOutOfMemory: { return ResultIds.E_OUTOFMEMORY; }
                    case StatusCodes.BadNodeIdInvalid: { return ResultIds.E_INVALID_PID; }
                    case StatusCodes.BadNodeIdUnknown: { return ResultIds.E_INVALID_PID; }
                    case StatusCodes.BadNotReadable: { return ResultIds.E_BADRIGHTS; }
                    case StatusCodes.BadUserAccessDenied: { return ResultIds.E_ACCESSDENIED; }
                    case StatusCodes.BadAttributeIdInvalid: { return ResultIds.E_INVALID_PID; }
                    case StatusCodes.BadTypeMismatch: { return ResultIds.E_BADTYPE; }
                }

                return ResultIds.E_FAIL;
            }

            // uncertain values for property reads are errors.
            return ResultIds.E_FAIL;
        }

        /// <summary>
        /// Converts a StatusCode returned during a Read to an HRESULT.
        /// </summary>
        internal static int MapReadStatusToErrorCode(StatusCode statusCode)
        {
            // map bad well known status codes.
            if (StatusCode.IsBad(statusCode))
            {
                switch (statusCode.Code)
                {
                    case StatusCodes.BadOutOfMemory: { return ResultIds.E_OUTOFMEMORY; }
                    case StatusCodes.BadNodeIdInvalid: { return ResultIds.E_INVALIDITEMID; }
                    case StatusCodes.BadNodeIdUnknown: { return ResultIds.E_UNKNOWNITEMID; }
                    case StatusCodes.BadNotReadable: { return ResultIds.E_BADRIGHTS; }
                    case StatusCodes.BadUserAccessDenied: { return ResultIds.E_ACCESSDENIED; }
                    case StatusCodes.BadAttributeIdInvalid: { return ResultIds.E_INVALIDITEMID; }
                    case StatusCodes.BadUnexpectedError: { return ResultIds.E_FAIL; }
                    case StatusCodes.BadInternalError: { return ResultIds.E_FAIL; }
                    case StatusCodes.BadSessionClosed: { return ResultIds.E_FAIL; }
                    case StatusCodes.BadTypeMismatch: { return ResultIds.E_BADTYPE; }
                }
            }

            // all other values are mapped to quality codes.
            return ResultIds.S_OK;
        }

        /// <summary>
        /// Converts a StatusCode returned during a Write to an HRESULT.
        /// </summary>
        internal static int MapWriteStatusToErrorCode(DataValue value, StatusCode statusCode)
        {
            // map bad status codes.
            if (StatusCode.IsBad(statusCode))
            {
                switch (statusCode.Code)
                {
                    case StatusCodes.BadTypeMismatch: 
                    {
                        // server may reject a null value without checking the status code.
                        if (StatusCode.IsBad(value.StatusCode) && value.Value == null)
                        {
                            return ResultIds.E_NOTSUPPORTED;
                        }

                        return ResultIds.E_BADTYPE;
                    }

                    case StatusCodes.BadOutOfMemory: { return ResultIds.E_OUTOFMEMORY; }
                    case StatusCodes.BadNodeIdInvalid: { return ResultIds.E_INVALIDITEMID; }
                    case StatusCodes.BadNodeIdUnknown: { return ResultIds.E_UNKNOWNITEMID; }
                    case StatusCodes.BadNotWritable: { return ResultIds.E_BADRIGHTS; }
                    case StatusCodes.BadUserAccessDenied: { return ResultIds.E_ACCESSDENIED; }
                    case StatusCodes.BadAttributeIdInvalid: { return ResultIds.E_UNKNOWNITEMID; }
                    case StatusCodes.BadWriteNotSupported: { return ResultIds.E_NOTSUPPORTED; }
                    case StatusCodes.BadOutOfRange: { return ResultIds.E_RANGE; }
                }

                return ResultIds.E_FAIL;
            }

            // ignore uncertain and success codes.
            return ResultIds.S_OK;
        }
        #endregion

        #region Private Fields
        private static object m_sharedCacheTableLock = new object();
        private ComProxyConfiguration m_configuration;
        private ComNamespaceMapper m_mapper;
        private ComDaBrowseCache m_browseCacheManager;
        private ComDaBrowseManager m_browseManager;
        private ComDaGroupManager m_groupManager;
        #endregion
	}
}
