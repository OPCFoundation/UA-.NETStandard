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
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
using OpcRcw.Comn;
using OpcRcw.Hda;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Com;
using Opc.Ua.Com.Client;

namespace Opc.Ua.Com.Client
{
    /// <summary>
    /// Provides access to a COM DA server.
    /// </summary>
    public class ComHdaClient : ComClient
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ComHdaClient"/> class.
        /// </summary>
        /// <param name="configuration"></param>
        public ComHdaClient(ComHdaClientConfiguration configuration) : base(configuration)
        {
            m_configuration = configuration;
        }
        #endregion

        #region ServerStatus Structure
        /// <summary>
        /// Stores the status of the server.
        /// </summary>
        public struct ServerStatus
        {
            /// <summary>
            /// The server status.
            /// </summary>
            public OPCHDA_SERVERSTATUS wStatus;

            /// <summary>
            /// The current server time. 
            /// </summary>
            public DateTime ftCurrentTime;

            /// <summary>
            /// The current server time. 
            /// </summary>
            public DateTime ftStartTime;

            /// <summary>
            /// The server major version.
            /// </summary>
            public short wMajorVersion;

            /// <summary>
            /// The server minor version.
            /// </summary>
            public short wMinorVersion;

            /// <summary>
            /// The server build number.
            /// </summary>
            public short wBuildNumber;

            /// <summary>
            /// The maximum number of values returned.
            /// </summary>
            public int dwMaxReturnValues;

            /// <summary>
            /// The server status information.
            /// </summary>
            public string szStatusString;

            /// <summary>
            /// The server vendor information.
            /// </summary>
            public string szVendorInfo;
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Reads the status from the server.
        /// </summary>
        public ServerStatus? GetStatus()
        {
            string methodName = "IOPCHDA_Server.GetStatus";

            try
            {

                IOPCHDA_Server server = BeginComCall<IOPCHDA_Server>(methodName, true);

                ServerStatus pStatus;
                IntPtr pftCurrentTime;
                IntPtr pftStartTime;

                server.GetHistorianStatus(
                    out pStatus.wStatus,
                    out pftCurrentTime,
                    out pftStartTime,
                    out pStatus.wMajorVersion,
                    out pStatus.wMinorVersion,
                    out pStatus.wBuildNumber,
                    out pStatus.dwMaxReturnValues,
                    out pStatus.szStatusString,
                    out pStatus.szVendorInfo);

                pStatus.ftCurrentTime = ComUtils.GetDateTime(pftCurrentTime);
                pStatus.ftStartTime = ComUtils.GetDateTime(pftStartTime);

                Marshal.FreeCoTaskMem(pftCurrentTime);
                Marshal.FreeCoTaskMem(pftStartTime);

                // cache the capabilities.
                m_maxReturnValues = pStatus.dwMaxReturnValues;

                return pStatus;
            }
            catch (Exception e)
            {
                ComCallError(methodName, e);
                return null;
            }
            finally
            {
                EndComCall(methodName);
            }
        }

        /// <summary>
        /// Updates the server metadata.
        /// </summary>
        public void UpdateServerMetadata()
        {
            HdaAttribute[] supportedAttributes = m_supportedAttributes;

            if (supportedAttributes == null)
            {
                lock (m_lock)
                {
                    m_supportedAttributes = GetSupportedAttributes();
                    m_annotationAccessLevel = GetAnnotationAccessLevel();
                    m_updateCapabilities = GetUpdateCapabilities();
                }
            }
        }

        /// <summary>
        /// Gets the history access level for the HDA server.
        /// </summary>
        public byte GetHistoryAccessLevel()
        {
            if (m_supportedAttributes == null)
            {
                UpdateServerMetadata();
            }

            if (m_updateCapabilities != 0)
            {
                return AccessLevels.HistoryReadOrWrite;
            }

            return AccessLevels.HistoryRead;
        }

        /// <summary>
        /// Gets the server capabilities.
        /// </summary>
        /// <param name="propertyId">The property id.</param>
        /// <param name="defaultValue">The default value.</param>
        /// <returns></returns>
        public object GetServerCapabilities(uint propertyId, object defaultValue)
        {
            if (m_supportedAttributes == null)
            {
                UpdateServerMetadata();
            }

            switch (propertyId)
            {
                case Opc.Ua.Variables.HistoryServerCapabilitiesType_MaxReturnDataValues:
                {
                    return (uint)m_maxReturnValues;
                }

                case Opc.Ua.Variables.HistoryServerCapabilitiesType_InsertDataCapability:
                {
                    return (m_updateCapabilities & OPCHDA_UPDATECAPABILITIES.OPCHDA_INSERTCAP) != 0;
                }

                case Opc.Ua.Variables.HistoryServerCapabilitiesType_ReplaceDataCapability:
                {
                    return (m_updateCapabilities & OPCHDA_UPDATECAPABILITIES.OPCHDA_REPLACECAP) != 0;
                }

                case Opc.Ua.Variables.HistoryServerCapabilitiesType_UpdateDataCapability:
                {
                    return (m_updateCapabilities & OPCHDA_UPDATECAPABILITIES.OPCHDA_INSERTREPLACECAP) != 0;
                }

                case Opc.Ua.Variables.HistoryServerCapabilitiesType_DeleteAtTimeCapability:
                {
                    return (m_updateCapabilities & OPCHDA_UPDATECAPABILITIES.OPCHDA_DELETEATTIMECAP) != 0;
                }

                case Opc.Ua.Variables.HistoryServerCapabilitiesType_DeleteRawCapability:
                {
                    return (m_updateCapabilities & OPCHDA_UPDATECAPABILITIES.OPCHDA_DELETERAWCAP) != 0;
                }
            }

            return defaultValue;
        }

        /// <summary>
        /// Creates the browser.
        /// </summary>
        /// <returns>An object which browses branches and items.</returns>
        public IOPCHDA_Browser CreateBrowser()
        {
            IOPCHDA_Browser browser = null;

            string methodName = "IOPCHDA_Server.CreateBrowser";

            try
            {
                IOPCHDA_Server server = BeginComCall<IOPCHDA_Server>(methodName, true);

                IntPtr ppErrors;

                server.CreateBrowse(
                    0,
                    new int[0],
                    new OPCHDA_OPERATORCODES[0],
                    new object[0],
                    out browser,
                    out ppErrors);

                Marshal.FreeCoTaskMem(ppErrors);
            }
            catch (Exception e)
            {
               ComCallError(methodName, e);
                return null;
            }
            finally
            {
                EndComCall(methodName);
            }

            return browser;
        }

        /// <summary>
        /// Gets the supported attributes.
        /// </summary>
        public HdaAttribute[] GetSupportedAttributes()
        {
            string methodName = "IOPCHDA_Server.GetItemAttributes";

            int pdwCount;
            IntPtr ppdwAttrID;
            IntPtr ppszAttrName;
            IntPtr ppszAttrDesc;
            IntPtr ppvtAttrDataType;

            try
            {
                IOPCHDA_Server server = BeginComCall<IOPCHDA_Server>(methodName, true);

                server.GetItemAttributes(
                    out pdwCount,
                    out ppdwAttrID,
                    out ppszAttrName,
                    out ppszAttrDesc,
                    out ppvtAttrDataType);
            }
            catch (Exception e)
            {
                ComCallError(methodName, e);
                return null;
            }
            finally
            {
                EndComCall(methodName);
            }

            int[] attributeIds = ComUtils.GetInt32s(ref ppdwAttrID, pdwCount, true);
            string[] names = ComUtils.GetUnicodeStrings(ref ppszAttrName, pdwCount, true);
            string[] descriptions = ComUtils.GetUnicodeStrings(ref ppszAttrDesc, pdwCount, true);
            short[] datatypes = ComUtils.GetInt16s(ref ppvtAttrDataType, pdwCount, true);

            HdaAttribute[] results = new HdaAttribute[pdwCount];

            for (int ii = 0; ii < results.Length; ii++)
            {
                HdaAttribute result = results[ii] = new HdaAttribute();

                result.Id = Utils.ToUInt32(attributeIds[ii]);
                result.Name = names[ii];
                result.Description = descriptions[ii];
                result.DataType = datatypes[ii];
            }

            return results;
        }
        
        /// <summary>
        /// Finds the specified aggregate.
        /// </summary>
        public BaseObjectState FindAggregate(uint aggregateId, ushort namespaceIndex)
        {
            BaseObjectState[] aggregates = GetSupportedAggregates(namespaceIndex);

            for (int ii = 0; ii < aggregates.Length; ii++)
            {
                if ((uint)aggregates[ii].Handle == aggregateId)
                {
                    return aggregates[ii];
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the supported aggregates.
        /// </summary>
        public BaseObjectState[] GetSupportedAggregates(ushort namespaceIndex)
        {
            string methodName = "IOPCHDA_Server.GetAggregates";

            int pdwCount;
            IntPtr ppdwAggrID;
            IntPtr ppszAggrName;
            IntPtr ppszAggrDesc;

            try
            {
                IOPCHDA_Server server = BeginComCall<IOPCHDA_Server>(methodName, true);

                server.GetAggregates(
                    out pdwCount,
                    out ppdwAggrID,
                    out ppszAggrName,
                    out ppszAggrDesc);
            }
            catch (Exception e)
            {
                ComCallError(methodName, e);
                return null;
            }
            finally
            {
                EndComCall(methodName);
            }

            int[] aggregateIds = ComUtils.GetInt32s(ref ppdwAggrID, pdwCount, true);
            string[] names = ComUtils.GetUnicodeStrings(ref ppszAggrName, pdwCount, true);
            string[] descriptions = ComUtils.GetUnicodeStrings(ref ppszAggrDesc, pdwCount, true);

            BaseObjectState[] results = new BaseObjectState[pdwCount];

            for (int ii = 0; ii < results.Length; ii++)
            {
                BaseObjectState aggregate = results[ii] = new BaseObjectState(null);

                aggregate.NodeId = HdaModelUtils.ConstructIdForHdaAggregate(Utils.ToUInt32(aggregateIds[ii]), namespaceIndex);
                aggregate.SymbolicName = aggregateIds[ii].ToString();
                aggregate.BrowseName = new QualifiedName(aggregate.SymbolicName, namespaceIndex);
                aggregate.DisplayName = names[ii];
                aggregate.Description = descriptions[ii];
                aggregate.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
                aggregate.TypeDefinitionId = Opc.Ua.ObjectTypeIds.AggregateFunctionType;
                aggregate.Handle = (uint)aggregateIds[ii];
            }

            return results;
        }

        /// <summary>
        /// Get handles for the specified items.
        /// </summary>
        public HdaItem[] GetItems(params string[] itemIds)
        {
            string methodName = "IOPCHDA_Server.GetItemHandles";

            if (itemIds == null)
            {
                return null;
            }

            IntPtr pphServer;
            IntPtr ppErrors;

            try
            {
                IOPCHDA_Server server = BeginComCall<IOPCHDA_Server>(methodName, true);

                server.GetItemHandles(
                    itemIds.Length,
                    itemIds,
                    new int[itemIds.Length],
                    out pphServer,
                    out ppErrors);
            }
            catch (Exception e)
            {
                ComCallError(methodName, e);
                return null;
            }
            finally
            {
                EndComCall(methodName);
            }

            int[] serverHandles = ComUtils.GetInt32s(ref pphServer, itemIds.Length, true);
            int[] errors = ComUtils.GetInt32s(ref ppErrors, itemIds.Length, true);

            HdaItem[] results = new HdaItem[itemIds.Length];

            for (int ii = 0; ii < results.Length; ii++)
            {
                HdaItem result = results[ii] = new HdaItem();

                result.ItemId = itemIds[ii];
                result.ServerHandle = serverHandles[ii];
                result.Error = errors[ii];
            }

            return results;
        }

        /// <summary>
        /// Validates the item ids.
        /// </summary>
        public bool[] ValidateItemIds(params string[] itemIds)
        {
            string methodName = "IOPCHDA_Server.ValidateItemIDs";

            if (itemIds == null)
            {
                return null;
            }

            IntPtr ppErrors;

            try
            {
                IOPCHDA_Server server = BeginComCall<IOPCHDA_Server>(methodName, true);

                server.ValidateItemIDs(
                    itemIds.Length,
                    itemIds,
                    out ppErrors);
            }
            catch (Exception e)
            {
                ComCallError(methodName, e);
                return null;
            }
            finally
            {
                EndComCall(methodName);
            }

            int[] errors = ComUtils.GetInt32s(ref ppErrors, itemIds.Length, true);

            bool[] results = new bool[itemIds.Length];

            for (int ii = 0; ii < results.Length; ii++)
            {
                results[ii] = errors[ii] >= 0;
            }

            return results;
        }

        /// <summary>
        /// Releases the handles for the items.
        /// </summary>
        public void ReleaseItemHandles(HdaItem[] items)
        {
            if (items == null)
            {
                return;
            }

            int[] serverHandles = new int[items.Length];

            for (int ii = 0; ii < items.Length; ii++)
            {
                serverHandles[ii] = items[ii].ServerHandle;
            }

            int[] errors = ReleaseItemHandles(serverHandles);

            for (int ii = 0; ii < items.Length; ii++)
            {
                items[ii].ServerHandle = 0;
                items[ii].Error = ResultIds.E_FAIL;

                if (errors != null)
                {
                    items[ii].Error = errors[ii];
                }
            }
        }

        /// <summary>
        /// Releases the handles for the items.
        /// </summary>
        public int[] ReleaseItemHandles(params int[] serverHandles)
        {
            string methodName = "IOPCHDA_Server.GetItemHandles";

            IntPtr ppErrors;

            try
            {
                IOPCHDA_Server server = BeginComCall<IOPCHDA_Server>(methodName, true);

                server.ReleaseItemHandles(
                    serverHandles.Length,
                    serverHandles,
                    out ppErrors);
            }
            catch (Exception e)
            {
                ComCallError(methodName, e);
                return null;
            }
            finally
            {
                EndComCall(methodName);
            }

            return ComUtils.GetInt32s(ref ppErrors, serverHandles.Length, true);
        }

        /// <summary>
        /// Updates the attributes for the items.
        /// </summary>
        public HdaAttributeValue[] ReadAttributeValues(int serverHandle, params int[] attributeIds)
        {
            string methodName = "IOPCHDA_SyncRead.ReadAttribute";

            OPCHDA_TIME htStartTime;
            htStartTime.bString = 1;
            htStartTime.szTime = "NOW";
            htStartTime.ftTime.dwHighDateTime = 0;
            htStartTime.ftTime.dwLowDateTime = 0;

            OPCHDA_TIME htEndTime;
            htEndTime.bString = 1;
            htEndTime.szTime = String.Empty;
            htEndTime.ftTime.dwHighDateTime = 0;
            htEndTime.ftTime.dwLowDateTime = 0;
            
            IntPtr ppAttributeValues;
            IntPtr ppErrors;

            try
            {
                IOPCHDA_SyncRead server = BeginComCall<IOPCHDA_SyncRead>(methodName, true);

                server.ReadAttribute(
                    ref htStartTime,
                    ref htEndTime,
                    serverHandle,
                    attributeIds.Length,
                    attributeIds,
                    out ppAttributeValues,
                    out ppErrors);
            }
            catch (Exception e)
            {
                ComCallError(methodName, e);
                return null;
            }
            finally
            {
                EndComCall(methodName);
            }

            int[] errors = ComUtils.GetInt32s(ref ppErrors, attributeIds.Length, true);

            HdaAttributeValue[] results = new HdaAttributeValue[attributeIds.Length];
            IntPtr pos = ppAttributeValues;

            for (int ii = 0; ii < attributeIds.Length; ii++)
            {
                HdaAttributeValue result = results[ii] = new HdaAttributeValue();

                result.AttributeId = Utils.ToUInt32(attributeIds[ii]);
                result.Error = errors[ii];

                OPCHDA_ATTRIBUTE attributes = (OPCHDA_ATTRIBUTE)Marshal.PtrToStructure(pos, typeof(OPCHDA_ATTRIBUTE));

                if (attributes.dwNumValues > 0)
                {
                    object[] values = ComUtils.GetVARIANTs(ref attributes.vAttributeValues, attributes.dwNumValues, true);
                    DateTime[] timestamps = ComUtils.GetDateTimes(ref attributes.ftTimeStamps, attributes.dwNumValues, true);

                    result.Value = ComUtils.ProcessComValue(values[0]);
                    result.Timestamp = timestamps[0];
                }

                pos = (IntPtr)(pos.ToInt64() + Marshal.SizeOf(typeof(OPCHDA_ATTRIBUTE)));
            }

            Marshal.FreeCoTaskMem(ppAttributeValues);

            return results;
        }

        /// <summary>
        /// Reads the values for all available attributes.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <returns>The list of attribute values for the item.</returns>
        public HdaAttributeValue[] ReadAvailableAttributes(HdaItem item)
        {
            if (item == null)
            {
                return null;
            }

            // get the supported attributes.
            if (m_supportedAttributes == null)
            {
                UpdateServerMetadata();
            }

            HdaAttribute[] supportedAttributes = m_supportedAttributes;

            if (supportedAttributes == null || supportedAttributes.Length == 0)
            {
                return null;
            }

            // read the attribute values.
            int[] attributeIds = new int[supportedAttributes.Length];

            for (int ii = 0; ii < supportedAttributes.Length; ii++)
            {
                attributeIds[ii] = Utils.ToInt32(supportedAttributes[ii].Id);
            }

            return ReadAttributeValues(item.ServerHandle, attributeIds);
        }

        /// <summary>
        /// Finds the UA defined component of the item.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="attributeId">The attribute id.</param>
        /// <param name="namespaceIndex">Index of the namespace.</param>
        /// <returns></returns>
        public PropertyState FindItemAttribute(string itemId, uint attributeId, ushort namespaceIndex)
        {
            if (itemId == null)
            {
                return null;
            }

            // get the supported attributes.
            if (m_supportedAttributes == null)
            {
                UpdateServerMetadata();
            }
            
            HdaAttribute[] supportedAttributes = m_supportedAttributes;

            if (supportedAttributes == null || supportedAttributes.Length == 0)
            {
                return null;
            }

            // validate the attribute.
            HdaAttribute attribute = null;

            for (int ii = 0; ii < supportedAttributes.Length; ii++)
            {
                if (attributeId == supportedAttributes[ii].Id)
                {
                    attribute = supportedAttributes[ii];
                    break; 
                }
            }

            if (attribute == null)
            {
                return null;
            }
            
            // check for attributes which are not exposed.
            switch (attributeId)
            {
                case Constants.OPCHDA_ITEMID:
                case Constants.OPCHDA_DATA_TYPE:
                case Constants.OPCHDA_DESCRIPTION:
                case Constants.OPCHDA_ARCHIVING:
                case Constants.OPCHDA_NORMAL_MINIMUM:
                case Constants.OPCHDA_LOW_ENTRY_LIMIT:
                {
                    return null;
                }
            }

            // create the property.
            HdaAttributeState property = new HdaAttributeState(
                m_configuration,
                itemId,
                attribute,
                namespaceIndex);

            return property;
        }

        /// <summary>
        /// Finds the item annotations.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="namespaceIndex">Index of the namespace.</param>
        /// <returns></returns>
        public PropertyState FindItemAnnotations(string itemId, ushort namespaceIndex)
        {
            if (itemId == null)
            {
                return null;
            }

            // check if the server supports annotations.
            if (m_supportedAttributes == null)
            {
                UpdateServerMetadata();
            }

            if (m_annotationAccessLevel == 0)
            {
                return null;
            }


            // create the property.
            PropertyState property = HdaModelUtils.GetItemAnnotationsNode(itemId, namespaceIndex);

            property.AccessLevel = m_annotationAccessLevel;
            property.UserAccessLevel = m_annotationAccessLevel;

            return property;
        }
        
        /// <summary>
        /// Reads the specified requests.
        /// </summary>
        /// <param name="requests">The requests.</param>
        /// <param name="useExistingHandles">if set to <c>true</c> use the handles specified in the request objects.</param>
        public void Read(HdaReadRequestCollection requests, bool useExistingHandles)
        {
            // check if nothing to do.
            if (requests == null || requests.Count == 0)
            {
                return;
            }

            int[] serverHandles = new int[requests.Count];
            int[] itemErrors = null;
            HdaItem[] items = null;

            // check if using existing handles.
            if (useExistingHandles)
            {
                for (int ii = 0; ii < requests.Count; ii++)
                {
                    serverHandles[ii] = requests[ii].ServerHandle;
                }
            }

            // create new handles to use for the request.
            else
            {
                string[] itemIds = new string[requests.Count];
                itemErrors = new int[requests.Count];

                for (int ii = 0; ii < requests.Count; ii++)
                {
                    itemIds[ii] = requests[ii].ItemId;
                }

                items = GetItems(itemIds);

                for (int ii = 0; ii < requests.Count; ii++)
                {
                    serverHandles[ii] = items[ii].ServerHandle;
                    itemErrors[ii] = items[ii].Error;
                }
            }

            // read attributes.
            try
            {
                for (int ii = 0; ii < serverHandles.Length; ii++)
                {
                    HdaReadRequest request = requests[ii];

                    if (itemErrors != null && itemErrors[ii] < 0)
                    {
                        request.SetError(request.AttributeIds.Count);
                        continue;
                    }

                    try
                    {
                        int[] attributeIds = new int[request.AttributeIds.Count];

                        for (int jj = 0; jj < attributeIds.Length; jj++)
                        {
                            attributeIds[jj] = Utils.ToInt32(request.AttributeIds[jj]);
                        }

                        request.AttributeValues = ReadAttributeValues(serverHandles[ii], attributeIds);
                    }
                    catch (Exception e)
                    {
                        request.SetError(Marshal.GetHRForException(e));
                    }
                }
            }
            finally
            {
                if (items != null)
                {
                    ReleaseItemHandles(items);
                }
            }
        }

        /// <summary>
        /// Reads the raw data history.
        /// </summary>
        /// <param name="request">The request.</param>
        public StatusCode ReadHistory(HdaHistoryReadRawModifiedRequest request)
        {
            // check if nothing to do.
            if (request == null)
            {
                return StatusCodes.BadNothingToDo;
            }

            HdaItem[] items = GetItems(request.ItemId);

            if (items == null || items[0].Error < 0)
            {
                return StatusCodes.BadNodeIdUnknown;
            }

            request.ServerHandle = items[0].ServerHandle;

            try
            {
                if (request.IsReadModified)
                {
                    return ReadModified(request);
                }
                    
                return ReadRaw(request);
            }
            catch (Exception)
            {
                return StatusCodes.BadUnexpectedError;
            }
            finally
            {
                ReleaseItemHandles(request.ServerHandle);
                request.ServerHandle = 0;
            }
        }

        /// <summary>
        /// Reads the data history at specified times.
        /// </summary>
        public StatusCode ReadHistory(HdaHistoryReadAtTimeRequest request)
        {
            if (request == null)
            {
                return StatusCodes.BadNothingToDo;
            }

            HdaItem[] items = GetItems(request.ItemId);

            if (items == null || items[0].Error < 0)
            {
                return StatusCodes.BadNodeIdUnknown;
            }

            request.ServerHandle = items[0].ServerHandle;

            try
            {
                return ReadAtTime(request);
            }
            catch (Exception)
            {
                return StatusCodes.BadUnexpectedError;
            }
            finally
            {
                ReleaseItemHandles(request.ServerHandle);
                request.ServerHandle = 0;
            }
        }

        /// <summary>
        /// Updates the history of an item.
        /// </summary>
        public StatusCode UpdateData(string itemId, UpdateDataDetails details, HistoryUpdateResult result)
        {
            // create handles.
            HdaItem[] items = GetItems(itemId);

            if (items == null || items[0].Error < 0)
            {
                result.StatusCode = StatusCodes.BadNodeIdUnknown;
                return result.StatusCode;
            }

            int[] errors = null;

            try
            {
                // update data.
                result.StatusCode = UpdateData(
                    items[0], 
                    details.PerformInsertReplace,
                    details.UpdateValues, 
                    out errors);

                // update error codes.
                for (int ii = 0; ii < errors.Length; ii++)
                {
                    StatusCode statusCode = MapErrorCodeToUpdateStatus(errors[ii]);
                    result.OperationResults.Add(statusCode);
                }
            }
            catch (Exception)
            {
                result.StatusCode = StatusCodes.BadUnexpectedError;
            }
            finally
            {
                // reelase handles.
                ReleaseItemHandles(items);
            }

            return result.StatusCode;
        }

        /// <summary>
        /// Updates the history of an item.
        /// </summary>
        public StatusCode DeleteRaw(string itemId, DeleteRawModifiedDetails details, HistoryUpdateResult result)
        {
            // create handles.
            HdaItem[] items = GetItems(itemId);

            if (items == null || items[0].Error < 0)
            {
                result.StatusCode = StatusCodes.BadNodeIdUnknown;
                return result.StatusCode;
            }

            try
            {
                // update data.
                result.StatusCode = DeleteRaw(
                    items[0],
                    details.StartTime,
                    details.EndTime);
            }
            catch (Exception)
            {
                result.StatusCode = StatusCodes.BadUnexpectedError;
            }
            finally
            {
                // release handles.
                ReleaseItemHandles(items);
            }

            return result.StatusCode;
        }

        /// <summary>
        /// Updates the history of an item.
        /// </summary>
        public StatusCode DeleteAtTime(string itemId, DeleteAtTimeDetails details, HistoryUpdateResult result)
        {
            // create handles.
            HdaItem[] items = GetItems(itemId);

            if (items == null || items[0].Error < 0)
            {
                result.StatusCode = StatusCodes.BadNodeIdUnknown;
                return result.StatusCode;
            }

            try
            {
                int[] errors = DeleteAtTime(items[0], details.ReqTimes);

                // update error codes.
                for (int ii = 0; ii < errors.Length; ii++)
                {
                    StatusCode statusCode = MapErrorCodeToUpdateStatus(errors[ii]);
                    result.OperationResults.Add(statusCode);
                }
            }
            catch (Exception)
            {
                result.StatusCode = StatusCodes.BadUnexpectedError;
            }
            finally
            {
                // release handles.
                ReleaseItemHandles(items);
            }

            return result.StatusCode;
        }

        /// <summary>
        /// Inserts annotations for item.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="details">The details.</param>
        /// <param name="result">The result.</param>
        /// <returns></returns>
        public StatusCode InsertAnnotations(string itemId, UpdateDataDetails details, HistoryUpdateResult result)
        {
            // only insert supported.
            if (details.PerformInsertReplace != PerformUpdateType.Insert)
            {
                result.StatusCode = StatusCodes.BadHistoryOperationUnsupported;
                return result.StatusCode;
            }

            // create handles.
            HdaItem[] items = GetItems(itemId);

            if (items == null || items[0].Error < 0)
            {
                result.StatusCode = StatusCodes.BadNodeIdUnknown;
                return result.StatusCode;
            }

            int[] errors = null;

            try
            {
                // update data.
                result.StatusCode = InsertAnnotations(
                    items[0],
                    details.UpdateValues, 
                    out errors);

                // update error codes.
                for (int ii = 0; ii < errors.Length; ii++)
                {
                    StatusCode statusCode = MapErrorCodeToUpdateStatus(errors[ii]);
                    result.OperationResults.Add(statusCode);
                }
            }
            catch (Exception)
            {
                result.StatusCode = StatusCodes.BadUnexpectedError;
            }
            finally
            {
                // reelase handles.
                ReleaseItemHandles(items);
            }

            return result.StatusCode;
        }

        /// <summary>
        /// Converts a HRESULT returned during a HistoryUpdate to an StatusCode.
        /// </summary>
        internal static StatusCode MapErrorCodeToUpdateStatus(int error)
        {
            // map bad status codes.
            if (error < 0)
            {
                switch (error)
                {
                    case ResultIds.E_OUTOFMEMORY: { return StatusCodes.BadOutOfMemory; }
                    case ResultIds.E_BADRIGHTS: { return StatusCodes.BadNotWritable; }
                    case ResultIds.E_ACCESSDENIED: { return StatusCodes.BadUserAccessDenied; }
                    case ResultIds.E_RANGE: { return StatusCodes.BadOutOfRange; }
                    case ResultIds.E_BADTYPE: { return StatusCodes.BadTypeMismatch; }
                    case ResultIds.DISP_E_OVERFLOW: { return StatusCodes.BadOutOfRange; }
                    case ResultIds.DISP_E_TYPEMISMATCH: { return StatusCodes.BadTypeMismatch; }
                    case ResultIds.E_DATAEXISTS: { return StatusCodes.BadEntryExists; }
                    case ResultIds.E_NODATAEXISTS: { return StatusCodes.BadNoEntryExists; }
                }

                return StatusCodes.BadUnexpectedError;
            }

            // ignore uncertain and success codes.
            return ResultIds.S_OK;
        }
        
        /// <summary>
        /// Update data
        /// </summary>
        private StatusCode UpdateData(
            HdaItem item,
            PerformUpdateType updateType,
            DataValueCollection values, 
            out int[] errors)
        {
            errors = null;
            string methodName = "IOPCHDA_SyncUpdate.Insert";

            int dwNumItems = values.Count;
            int[] phServer = new int[dwNumItems];
            System.Runtime.InteropServices.ComTypes.FILETIME[] ftTimeStamps = new System.Runtime.InteropServices.ComTypes.FILETIME[dwNumItems];
            object[] vDataValues = new object[dwNumItems];
            int[] pdwQualities = new int[dwNumItems];

            for (int ii = 0; ii < dwNumItems; ii++)
            {
                DataValue value = values[ii];

                phServer[ii] = item.ServerHandle;
                vDataValues[ii] = ComUtils.GetVARIANT(value.WrappedValue);
                pdwQualities[ii] = Utils.ToInt32(ComUtils.GetHdaQualityCode(value.StatusCode));
                ftTimeStamps[ii] = ComUtils.GetFILETIME(value.SourceTimestamp);
            }

            IntPtr ppErrors = IntPtr.Zero;

            try
            {
                IOPCHDA_SyncUpdate server = BeginComCall<IOPCHDA_SyncUpdate>(methodName, true);

                switch (updateType)
                {
                    case PerformUpdateType.Insert:
                    {
                        server.Insert(
                            dwNumItems,
                            phServer,
                            ftTimeStamps,
                            vDataValues,
                            pdwQualities,
                            out ppErrors);

                        break;
                    }

                    case PerformUpdateType.Update:
                    {
                        server.InsertReplace(
                            dwNumItems,
                            phServer,
                            ftTimeStamps,
                            vDataValues,
                            pdwQualities,
                            out ppErrors);

                        break;
                    }

                    case PerformUpdateType.Replace:
                    {
                        server.Replace(
                            dwNumItems,
                            phServer,
                            ftTimeStamps,
                            vDataValues,
                            pdwQualities,
                            out ppErrors);

                        break;
                    }
                }

                // check for error.
                errors = ComUtils.GetInt32s(ref ppErrors, dwNumItems, true);
            }
            catch (Exception e)
            {
                ComCallError(methodName, e);
                return StatusCodes.BadUnexpectedError;
            }
            finally
            {
                EndComCall(methodName);
            }

            return StatusCodes.Good;
        }

        /// <summary>
        /// Insert annotations.
        /// </summary>
        private StatusCode InsertAnnotations(
            HdaItem item,
            DataValueCollection values,
            out int[] errors)
        {
            errors = null;
            string methodName = "IOPCHDA_SyncAnnotations.Insert";

            int dwNumItems = values.Count;
            int[] phServer = new int[dwNumItems];
            System.Runtime.InteropServices.ComTypes.FILETIME[] ftTimeStamps = new System.Runtime.InteropServices.ComTypes.FILETIME[dwNumItems];
            OPCHDA_ANNOTATION[] pAnnotationValues = new OPCHDA_ANNOTATION[dwNumItems];

            IntPtr ppErrors;

            try
            {
                for (int ii = 0; ii < dwNumItems; ii++)
                {
                    DataValue value = values[ii];

                    phServer[ii] = item.ServerHandle;
                    ftTimeStamps[ii] = ComUtils.GetFILETIME(value.SourceTimestamp);
                    pAnnotationValues[ii] = new OPCHDA_ANNOTATION();

                    // pass an empty structure if the annotation is not valid.
                    pAnnotationValues[ii].dwNumValues = 0;

                    Annotation annotation = value.GetValue<Annotation>(null);

                    if (annotation != null && !String.IsNullOrEmpty(annotation.Message))
                    {
                        pAnnotationValues[ii].dwNumValues = 1;
                        pAnnotationValues[ii].ftAnnotationTime = ComUtils.GetFILETIMEs(new DateTime[] { annotation.AnnotationTime });
                        pAnnotationValues[ii].ftTimeStamps = ComUtils.GetFILETIMEs(new DateTime[] { value.SourceTimestamp });
                        pAnnotationValues[ii].szAnnotation = ComUtils.GetUnicodeStrings(new string[] { annotation.Message });
                        pAnnotationValues[ii].szUser = ComUtils.GetUnicodeStrings(new string[] { annotation.UserName });
                    }
                }

                IOPCHDA_SyncAnnotations server = BeginComCall<IOPCHDA_SyncAnnotations>(methodName, true);

                server.Insert(
                    dwNumItems,
                    phServer,
                    ftTimeStamps,
                    pAnnotationValues,
                    out ppErrors);

                // check for error.
                errors = ComUtils.GetInt32s(ref ppErrors, dwNumItems, true);
                
                // set bad type error for invalid annotations.
                for (int ii = 0; ii < dwNumItems; ii++)
                {
                    if (pAnnotationValues[ii].dwNumValues == 0)
                    {
                        errors[ii] = ResultIds.E_BADTYPE;
                    }
                }
            }
            catch (Exception e)
            {
                ComCallError(methodName, e);
                return StatusCodes.BadUnexpectedError;
            }
            finally
            {
                EndComCall(methodName);

                // free allocated memory.
                for (int ii = 0; ii < dwNumItems; ii++)
                {
                    if (pAnnotationValues[ii].dwNumValues == 0)
                    {
                        continue;
                    }

                    IntPtr[] pointers = new IntPtr[1];
                    Marshal.Copy(pAnnotationValues[ii].szUser, pointers, 0, 1);
                    Marshal.FreeCoTaskMem(pointers[0]);

                    Marshal.Copy(pAnnotationValues[ii].szAnnotation, pointers, 0, 1);
                    Marshal.FreeCoTaskMem(pointers[0]);

                    Marshal.FreeCoTaskMem(pAnnotationValues[ii].ftAnnotationTime);
                    Marshal.FreeCoTaskMem(pAnnotationValues[ii].ftTimeStamps);
                    Marshal.FreeCoTaskMem(pAnnotationValues[ii].szUser);
                    Marshal.FreeCoTaskMem(pAnnotationValues[ii].szAnnotation);
                }
            }

            return StatusCodes.Good;
        }

        /// <summary>
        /// Reads the processed data history.
        /// </summary>
        public StatusCode ReadHistory(HdaHistoryReadProcessedRequest request)
        {
            if (request == null)
            {
                return StatusCodes.BadNothingToDo;
            }

            HdaItem[] items = GetItems(request.ItemId);

            if (items == null || items[0].Error < 0)
            {
                return StatusCodes.BadNodeIdUnknown;
            }

            request.ServerHandle = items[0].ServerHandle;

            try
            {
                return ReadProcessed(request);
            }
            catch (Exception)
            {
                return StatusCodes.BadUnexpectedError;
            }
            finally
            {
                ReleaseItemHandles(request.ServerHandle);
                request.ServerHandle = 0;
            }
        }

        /// <summary>
        /// Reads the raw data history.
        /// </summary>
        /// <param name="request">The request.</param>
        public StatusCode ReadAttributeHistory(HdaHistoryReadAttributeRequest request)
        {
            // check if nothing to do.
            if (request == null)
            {
                return StatusCodes.BadNothingToDo;
            }

            // create the handle.
            HdaItem[] items = GetItems(request.ItemId);

            if (items == null || items[0].Error < 0)
            {
                return StatusCodes.BadNodeIdUnknown;
            }

            request.ServerHandle = items[0].ServerHandle;

            try
            {
                // read modified not supported for attributes.
                if (request.IsReadModified)
                {
                    return StatusCodes.BadNoData;
                }

                // convert the requested attribute to a list of attributes to read.
                int[] attributeIds = null;

                switch (request.AttributeId)
                {
                    case Constants.OPCHDA_NORMAL_MAXIMUM:
                    {
                        attributeIds = new int[] { Constants.OPCHDA_NORMAL_MAXIMUM, Constants.OPCHDA_NORMAL_MINIMUM };
                        break;
                    }

                    case Constants.OPCHDA_HIGH_ENTRY_LIMIT:
                    {
                        attributeIds = new int[] { Constants.OPCHDA_HIGH_ENTRY_LIMIT, Constants.OPCHDA_LOW_ENTRY_LIMIT };
                        break;
                    }

                    default:
                    {
                        attributeIds = new int[] { Utils.ToInt32(request.AttributeId) };
                        break;
                    }
                }

                // read the values and them in the request object.
                return ReadAttributes(request, attributeIds);
            }
            catch (Exception)
            {
                return StatusCodes.BadUnexpectedError;
            }
            finally
            {
                ReleaseItemHandles(request.ServerHandle);
                request.ServerHandle = 0;
            }
        }
        
        /// <summary>
        /// Reads the raw data history.
        /// </summary>
        /// <param name="request">The request.</param>
        public StatusCode ReadAnnotationHistory(HdaHistoryReadAnnotationRequest request)
        {
            // check if nothing to do.
            if (request == null)
            {
                return StatusCodes.BadNothingToDo;
            }

            // create the handle.
            HdaItem[] items = GetItems(request.ItemId);

            if (items == null || items[0].Error < 0)
            {
                return StatusCodes.BadNodeIdUnknown;
            }

            request.ServerHandle = items[0].ServerHandle;

            try
            {
                // read modified not supported for annotations.
                if (request.IsReadModified)
                {
                    return StatusCodes.BadNoData;
                }

                // read the values and them in the request object.
                return ReadAnnotations(request);
            }
            catch (Exception)
            {
                return StatusCodes.BadUnexpectedError;
            }
            finally
            {
                ReleaseItemHandles(request.ServerHandle);
                request.ServerHandle = 0;
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Gets the access level for annotations provided by the server.
        /// </summary>
        private byte GetAnnotationAccessLevel()
        {
            string methodName = "IOPCHDA_SyncAnnotations.QueryCapabilities";

            OPCHDA_ANNOTATIONCAPABILITIES pCapabilities = 0;

            try
            {
                IOPCHDA_SyncAnnotations server = BeginComCall<IOPCHDA_SyncAnnotations>(methodName, false);

                if (server == null)
                {
                    return 0;
                }

                server.QueryCapabilities(out pCapabilities);
            }
            catch (Exception e)
            {
                ComCallError(methodName, e);
                return 0;
            }
            finally
            {
                EndComCall(methodName);
            }

            byte accessLevel = AccessLevels.None;

            if ((pCapabilities & OPCHDA_ANNOTATIONCAPABILITIES.OPCHDA_READANNOTATIONCAP) != 0)
            {
                accessLevel |= AccessLevels.HistoryRead;
            }

            if ((pCapabilities & OPCHDA_ANNOTATIONCAPABILITIES.OPCHDA_INSERTANNOTATIONCAP) != 0)
            {
                accessLevel |= AccessLevels.HistoryWrite;
            }

            return accessLevel;
        }

        /// <summary>
        /// Gets the update capabilities for the server.
        /// </summary>
        private OPCHDA_UPDATECAPABILITIES GetUpdateCapabilities()
        {
            string methodName = "IOPCHDA_SyncUpdate.QueryCapabilities";

            OPCHDA_UPDATECAPABILITIES pCapabilities = 0;

            try
            {
                IOPCHDA_SyncUpdate server = BeginComCall<IOPCHDA_SyncUpdate>(methodName, false);

                if (server == null)
                {
                    return 0;
                }

                server.QueryCapabilities(out pCapabilities);
            }
            catch (Exception e)
            {
                ComCallError(methodName, e);
                return 0;
            }
            finally
            {
                EndComCall(methodName);
            }

            return pCapabilities;
        }

        /// <summary>
        /// Converts the DateTime to an OPCHDA_TIME.
        /// </summary>
        /// <param name="time">The time.</param>
        private OPCHDA_TIME ConvertTime(DateTime time)
        {
            OPCHDA_TIME htTime = new OPCHDA_TIME();

            if (time != DateTime.MinValue)
            {
                htTime.bString = 0;
                htTime.ftTime = ComUtils.GetFILETIME(time);
            }
            else
            {
                htTime.bString = 1;
                htTime.szTime = String.Empty;
            }

            return htTime;
        }
                
        /// <summary>
        /// Deletes the raw data for an item.
        /// </summary>
        private StatusCode DeleteRaw(HdaItem item, DateTime startTime, DateTime endTime)
        {
            string methodName = "IOPCHDA_SyncUpdate.DeleteRaw";

            OPCHDA_TIME htStartTime = ConvertTime(startTime);
            OPCHDA_TIME htEndTime = ConvertTime(endTime);

            IntPtr ppErrors;

            try
            {
                IOPCHDA_SyncUpdate server = BeginComCall<IOPCHDA_SyncUpdate>(methodName, true);

                server.DeleteRaw(
                    ref htStartTime,
                    ref htEndTime,
                    1,
                    new int[] { item.ServerHandle },
                    out ppErrors);
            }
            catch (Exception e)
            {
                ComCallError(methodName, e);
                return StatusCodes.BadUnexpectedError;
            }
            finally
            {
                EndComCall(methodName);
            }

            int[] errors = ComUtils.GetInt32s(ref ppErrors, 1, true);
            return MapErrorCodeToUpdateStatus(errors[0]);
        }

        /// <summary>
        /// Deletes the raw data for an item.
        /// </summary>
        private int[] DeleteAtTime(HdaItem item, DateTimeCollection timestamps)
        {
            string methodName = "IOPCHDA_SyncUpdate.DeleteAtTime";

            int count = timestamps.Count;
            int[] serverHandles = new int[count];
            System.Runtime.InteropServices.ComTypes.FILETIME[] pTimestamps = new System.Runtime.InteropServices.ComTypes.FILETIME[count];

            for (int ii = 0; ii < count; ii++)
            {
                serverHandles[ii] = item.ServerHandle;
                pTimestamps[ii] = ComUtils.GetFILETIME(timestamps[ii]);
            }

            IntPtr ppErrors;

            try
            {
                IOPCHDA_SyncUpdate server = BeginComCall<IOPCHDA_SyncUpdate>(methodName, true);

                server.DeleteAtTime(
                    count,
                    serverHandles,
                    pTimestamps,
                    out ppErrors);
            }
            catch (Exception e)
            {
                ComCallError(methodName, e);

                int[] errors = new int[count];

                for (int ii = 0; ii < count; ii++)
                {
                    errors[ii] = ResultIds.E_FAIL;
                }

                return errors;
            }
            finally
            {
                EndComCall(methodName);
            }

            return ComUtils.GetInt32s(ref ppErrors, count, true);
        }

        /// <summary>
        /// Reads the raw data for an item.
        /// </summary>
        private StatusCode ReadRaw(HdaHistoryReadRawModifiedRequest request)
        {
            string methodName = "IOPCHDA_SyncRead.ReadRaw";

            OPCHDA_TIME htStartTime = ConvertTime(request.StartTime);
            OPCHDA_TIME htEndTime = ConvertTime(request.EndTime);

            int maxReturnValues = request.MaxReturnValues;

            if (m_maxReturnValues > 0 && maxReturnValues > m_maxReturnValues)
            {
                maxReturnValues = m_maxReturnValues;
            }

            // must have a least two values.
            if (request.MaxReturnValues == 1 && request.TotalValuesReturned > 0)
            {
                maxReturnValues = 2;
            }

            IntPtr ppItemValues;
            IntPtr ppErrors;

            try
            {
                IOPCHDA_SyncRead server = BeginComCall<IOPCHDA_SyncRead>(methodName, true);

                server.ReadRaw(
                    ref htStartTime,
                    ref htEndTime,
                    maxReturnValues,
                    (request.ReturnBounds)?1:0,
                    1,
                    new int[] { request.ServerHandle },
                    out ppItemValues,
                    out ppErrors);
            }
            catch (Exception e)
            {
                ComCallError(methodName, e);
                return StatusCodes.BadUnexpectedError;
            }
            finally
            {
                EndComCall(methodName);
            }

            // check for error.
            int[] errors = ComUtils.GetInt32s(ref ppErrors, 1, true);

            if (errors[0] < 0)
            {
                return StatusCodes.BadNodeIdUnknown;
            }

            // check if operation halted.
            request.Completed = errors[0] != ResultIds.S_MOREDATA;

            // unmarshal the results.
            OPCHDA_ITEM result = (OPCHDA_ITEM)Marshal.PtrToStructure(ppItemValues, typeof(OPCHDA_ITEM));

            if (result.dwCount > 0)
            {
                object[] values = ComUtils.GetVARIANTs(ref result.pvDataValues, result.dwCount, true);
                int[] qualities = ComUtils.GetInt32s(ref result.pdwQualities, result.dwCount, true);
                DateTime[] timestamps = ComUtils.GetDateTimes(ref result.pftTimeStamps, result.dwCount, true);

                request.Results = new DataValueCollection(result.dwCount);

                for (int ii = 0; ii < result.dwCount; ii++)
                {
                    // suppress previously returned values.
                    if (ii == 0 && request.TotalValuesReturned > 0)
                    {
                        if (request.StartTime < request.EndTime && timestamps[ii] <= request.StartTime)
                        {
                            continue;
                        }

                        if (request.StartTime > request.EndTime && timestamps[ii] >= request.StartTime)
                        {
                            continue;
                        }
                    }

                    DataValue value = new DataValue();
                    value.Value = ComUtils.ProcessComValue(values[ii]);
                    value.StatusCode = ComUtils.GetHdaQualityCode(Utils.ToUInt32(qualities[ii]));
                    value.SourceTimestamp = timestamps[ii];
                    request.Results.Add(value);
                }

                request.TotalValuesReturned += request.Results.Count;

                if (!request.Completed)
                {
                    request.StartTime = request.Results[request.Results.Count-1].SourceTimestamp;
                }
            }

            Marshal.FreeCoTaskMem(ppItemValues);

            if (result.dwCount == 0)
            {
                return StatusCodes.GoodNoData;
            }

            if (maxReturnValues > 0 && !request.Completed)
            {
                return StatusCodes.GoodMoreData;
            }

            return StatusCodes.Good;
        }

        /// <summary>
        /// Reads the raw modified data for an item.
        /// </summary>
        private StatusCode ReadModified(HdaHistoryReadRawModifiedRequest request)
        {
            string methodName = "IOPCHDA_SyncRead.ReadModified";

            OPCHDA_TIME htStartTime = ConvertTime(request.StartTime);
            OPCHDA_TIME htEndTime = ConvertTime(request.EndTime);

            int maxReturnValues = request.MaxReturnValues;

            if (m_maxReturnValues > 0 && maxReturnValues > m_maxReturnValues)
            {
                maxReturnValues = m_maxReturnValues;
            }

            IntPtr ppItemValues;
            IntPtr ppErrors;

            try
            {
                IOPCHDA_SyncRead server = BeginComCall<IOPCHDA_SyncRead>(methodName, true);

                server.ReadModified(
                    ref htStartTime,
                    ref htEndTime,
                    maxReturnValues,
                    1,
                    new int[] { request.ServerHandle },
                    out ppItemValues,
                    out ppErrors);
            }
            catch (Exception e)
            {
                if (ComUtils.IsUnknownError(e, ResultIds.E_NOTIMPL))
                {
                    ComCallError(methodName, e);
                }

                return StatusCodes.BadNoData;
            }
            finally
            {
                EndComCall(methodName);
            }

            // check for error.
            int[] errors = ComUtils.GetInt32s(ref ppErrors, 1, true);

            if (errors[0] < 0)
            {
                return StatusCodes.BadNodeIdUnknown;
            }

            // check if operation halted.
            request.Completed = errors[0] != ResultIds.S_MOREDATA;

            // unmarshal the results.
            OPCHDA_MODIFIEDITEM result = (OPCHDA_MODIFIEDITEM)Marshal.PtrToStructure(ppItemValues, typeof(OPCHDA_MODIFIEDITEM));

            if (result.dwCount > 0)
            {
                object[] values = ComUtils.GetVARIANTs(ref result.pvDataValues, result.dwCount, true);
                int[] qualities = ComUtils.GetInt32s(ref result.pdwQualities, result.dwCount, true);
                DateTime[] timestamps = ComUtils.GetDateTimes(ref result.pftTimeStamps, result.dwCount, true);
                DateTime[] modificationTimes = ComUtils.GetDateTimes(ref result.pftModificationTime, result.dwCount, true);
                string[] userNames = ComUtils.GetUnicodeStrings(ref result.szUser, result.dwCount, true);
                int[] editTypes = ComUtils.GetInt32s(ref result.pEditType, result.dwCount, true);

                request.Results = new DataValueCollection(result.dwCount);
                request.ModificationInfos = new ModificationInfoCollection(result.dwCount);

                for (int ii = 0; ii < result.dwCount; ii++)
                {
                    DataValue value = new DataValue();
                    value.Value = ComUtils.ProcessComValue(values[ii]);
                    value.StatusCode = ComUtils.GetHdaQualityCode(Utils.ToUInt32(qualities[ii]));
                    value.SourceTimestamp = timestamps[ii];
                    request.Results.Add(value);

                    ModificationInfo modification = new ModificationInfo();
                    modification.ModificationTime = modificationTimes[ii];
                    modification.UpdateType = (HistoryUpdateType)editTypes[ii];
                    modification.UserName = userNames[ii];
                    request.ModificationInfos.Add(modification);
                }

                if (!request.Completed)
                {
                    request.StartTime = request.Results[request.Results.Count-1].SourceTimestamp;
                }
            }

            Marshal.FreeCoTaskMem(ppItemValues);

            if (result.dwCount == 0)
            {
                return StatusCodes.GoodNoData;
            }

            if (maxReturnValues > 0 && !request.Completed)
            {
                return StatusCodes.GoodMoreData;
            }

            return StatusCodes.Good;
        }

        /// <summary>
        /// Reads the raw data for an item.
        /// </summary>
        private StatusCode ReadProcessed(HdaHistoryReadProcessedRequest request)
        {
            string methodName = "IOPCHDA_SyncRead.ReadProcessed";

            DateTime startTime = request.StartTime;
            DateTime endTime = request.EndTime;

            // adjust the resample interval.
            if (m_maxReturnValues > 0 && request.ResampleInterval > 0)
            {
                double range = (startTime - endTime).TotalMilliseconds;
                double resampleInterval = request.ResampleInterval;

                if (Math.Abs(range/resampleInterval) > m_maxReturnValues)
                {
                    range = resampleInterval*m_maxReturnValues;

                    if (startTime > endTime)
                    {
                        range = -range;
                    }

                    endTime = startTime.AddMilliseconds(range);
                }
            }

            OPCHDA_TIME htStartTime = ConvertTime(startTime);
            OPCHDA_TIME htEndTime = ConvertTime(endTime);

            System.Runtime.InteropServices.ComTypes.FILETIME ftResampleInterval;

            ulong ticks = (ulong)request.ResampleInterval*TimeSpan.TicksPerMillisecond;
            ftResampleInterval.dwHighDateTime = (int)((0xFFFFFFFF00000000 & ticks) >> 32);
            ftResampleInterval.dwLowDateTime = (int)(ticks & 0x00000000FFFFFFFF);

            IntPtr ppItemValues;
            IntPtr ppErrors;

            try
            {
                IOPCHDA_SyncRead server = BeginComCall<IOPCHDA_SyncRead>(methodName, true);

                server.ReadProcessed(
                    ref htStartTime,
                    ref htEndTime,
                    ftResampleInterval,
                    1,
                    new int[] { request.ServerHandle },
                    new int[] { Utils.ToInt32(request.AggregateId) },
                    out ppItemValues,
                    out ppErrors);
            }
            catch (Exception e)
            {
                ComCallError(methodName, e);
                return StatusCodes.BadUnexpectedError;
            }
            finally
            {
                EndComCall(methodName);
            }

            // check for error.
            int[] errors = ComUtils.GetInt32s(ref ppErrors, 1, true);

            if (errors[0] < 0)
            {
                if (errors[0] == ResultIds.E_NOT_AVAIL)
                {
                    return StatusCodes.BadAggregateNotSupported;
                }

                return StatusCodes.BadNodeIdUnknown;
            }

            // check if operation halted.
            request.Completed = endTime == request.EndTime;

            // unmarshal the results.
            OPCHDA_ITEM result = (OPCHDA_ITEM)Marshal.PtrToStructure(ppItemValues, typeof(OPCHDA_ITEM));

            if (result.dwCount > 0)
            {
                object[] values = ComUtils.GetVARIANTs(ref result.pvDataValues, result.dwCount, true);
                int[] qualities = ComUtils.GetInt32s(ref result.pdwQualities, result.dwCount, true);
                DateTime[] timestamps = ComUtils.GetDateTimes(ref result.pftTimeStamps, result.dwCount, true);

                request.Results = new DataValueCollection(result.dwCount);

                for (int ii = 0; ii < result.dwCount; ii++)
                {
                    DataValue value = new DataValue();
                    value.Value = ComUtils.ProcessComValue(values[ii]);
                    value.StatusCode = ComUtils.GetHdaQualityCode(Utils.ToUInt32(qualities[ii]));
                    value.SourceTimestamp = timestamps[ii];
                    request.Results.Add(value);
                }

                if (!request.Completed)
                {
                    request.StartTime = endTime;
                }
            }

            Marshal.FreeCoTaskMem(ppItemValues);

            if (result.dwCount == 0)
            {
                return StatusCodes.GoodNoData;
            }

            if (!request.Completed)
            {
                return StatusCodes.GoodMoreData;
            }

            return StatusCodes.Good;
        }

        /// <summary>
        /// Reads the data for an item at the specified times.
        /// </summary>
        private StatusCode ReadAtTime(HdaHistoryReadAtTimeRequest request)
        {
            string methodName = "IOPCHDA_SyncRead.ReadAtTime";

            IntPtr ppItemValues;
            IntPtr ppErrors;

            System.Runtime.InteropServices.ComTypes.FILETIME[] pTimestamps = new System.Runtime.InteropServices.ComTypes.FILETIME[request.ReqTimes.Count];

            for (int ii = 0; ii < pTimestamps.Length; ii++)
            {
                pTimestamps[ii] = ComUtils.GetFILETIME(request.ReqTimes[ii]);
            }

            try
            {
                IOPCHDA_SyncRead server = BeginComCall<IOPCHDA_SyncRead>(methodName, true);

                server.ReadAtTime(
                    pTimestamps.Length,
                    pTimestamps,
                    1,
                    new int[] { request.ServerHandle },
                    out ppItemValues,
                    out ppErrors);
            }
            catch (Exception e)
            {
                ComCallError(methodName, e);
                return StatusCodes.BadUnexpectedError;
            }
            finally
            {
                EndComCall(methodName);
            }

            // check for error.
            int[] errors = ComUtils.GetInt32s(ref ppErrors, 1, true);

            if (errors[0] < 0)
            {
                return StatusCodes.BadNodeIdUnknown;
            }

            // check if operation halted.
            request.Completed = true;

            // unmarshal the results.
            OPCHDA_ITEM result = (OPCHDA_ITEM)Marshal.PtrToStructure(ppItemValues, typeof(OPCHDA_ITEM));

            if (result.dwCount > 0)
            {
                object[] values = ComUtils.GetVARIANTs(ref result.pvDataValues, result.dwCount, true);
                int[] qualities = ComUtils.GetInt32s(ref result.pdwQualities, result.dwCount, true);
                DateTime[] timestamps = ComUtils.GetDateTimes(ref result.pftTimeStamps, result.dwCount, true);

                request.Results = new DataValueCollection(result.dwCount);

                for (int ii = 0; ii < result.dwCount; ii++)
                {
                    DataValue value = new DataValue();
                    value.Value = ComUtils.ProcessComValue(values[ii]);
                    value.StatusCode = ComUtils.GetHdaQualityCode(Utils.ToUInt32(qualities[ii]));
                    value.SourceTimestamp = timestamps[ii];
                    request.Results.Add(value);
                }
            }

            Marshal.FreeCoTaskMem(ppItemValues);

            if (result.dwCount == 0)
            {
                return StatusCodes.GoodNoData;
            }

            return StatusCodes.Good;
        }

        /// <summary>
        /// Reads the raw modified data for an item.
        /// </summary>
        private StatusCode ReadAttributes(HdaHistoryReadAttributeRequest request, params int[] attributeIds)
        {
            string methodName = "IOPCHDA_SyncRead.ReadAttribute";

            OPCHDA_TIME htStartTime = ConvertTime(request.StartTime);
            OPCHDA_TIME htEndTime = ConvertTime(request.EndTime);

            IntPtr ppAttributeValues;
            IntPtr ppErrors;

            try
            {
                IOPCHDA_SyncRead server = BeginComCall<IOPCHDA_SyncRead>(methodName, true);

                server.ReadAttribute(
                    ref htStartTime,
                    ref htEndTime,
                    request.ServerHandle,
                    attributeIds.Length,
                    attributeIds,
                    out ppAttributeValues,
                    out ppErrors);
            }
            catch (Exception e)
            {
                ComCallError(methodName, e);
                return StatusCodes.BadUnexpectedError;
            }
            finally
            {
                EndComCall(methodName);
            }

            StatusCode status = StatusCodes.Good;

            // error in one attribute means operation failed.
            int[] errors = ComUtils.GetInt32s(ref ppErrors, attributeIds.Length, true);

            for (int ii = 0; ii < errors.Length; ii++)
            {
                if (errors[ii] < 0)
                {
                    status = StatusCodes.BadNodeIdUnknown;
                    break;
                }
            }

            // unmarshal results.
            HdaAttributeValue[][] results = new HdaAttributeValue[attributeIds.Length][];
            IntPtr pos = ppAttributeValues;

            for (int ii = 0; ii < attributeIds.Length; ii++)
            {
                OPCHDA_ATTRIBUTE attributes = (OPCHDA_ATTRIBUTE)Marshal.PtrToStructure(pos, typeof(OPCHDA_ATTRIBUTE));

                if (attributes.dwNumValues > 0)
                {
                    results[ii] = new HdaAttributeValue[attributes.dwNumValues];

                    object[] values = ComUtils.GetVARIANTs(ref attributes.vAttributeValues, attributes.dwNumValues, true);
                    DateTime[] timestamps = ComUtils.GetDateTimes(ref attributes.ftTimeStamps, attributes.dwNumValues, true);

                    for (int jj = 0; jj < values.Length; jj++)
                    {
                        HdaAttributeValue result = results[ii][jj] = new HdaAttributeValue();
                        result.AttributeId = Utils.ToUInt32(attributeIds[ii]);
                        result.Value = ComUtils.ProcessComValue(values[jj]);
                        result.Timestamp = timestamps[jj];
                        result.Error = ResultIds.S_OK;
                    }
                }
                else
                {
                    results[ii] = new HdaAttributeValue[1];
                    HdaAttributeValue result = results[ii][0] = new HdaAttributeValue();
                    result.AttributeId = Utils.ToUInt32(attributeIds[ii]);
                    result.Value = null;
                    result.Timestamp = request.StartTime;
                    result.Error = ResultIds.S_NODATA;
                }
                
                pos = (IntPtr)(pos.ToInt64() + Marshal.SizeOf(typeof(OPCHDA_ATTRIBUTE)));
            }

            Marshal.FreeCoTaskMem(ppAttributeValues);
            
            // save the results.
            request.SetHistoryResults(attributeIds, results);
            
            return StatusCodes.Good;
        }

        /// <summary>
        /// Reads the annotation data for an item.
        /// </summary>
        private StatusCode ReadAnnotations(HdaHistoryReadAnnotationRequest request)
        {
            string methodName = "IOPCHDA_SyncAnnotations.Read";

            OPCHDA_TIME htStartTime = ConvertTime(request.StartTime);
            OPCHDA_TIME htEndTime = ConvertTime(request.EndTime);

            IntPtr ppAnnotationValues;
            IntPtr ppErrors;

            try
            {
                IOPCHDA_SyncAnnotations server = BeginComCall<IOPCHDA_SyncAnnotations>(methodName, true);

                server.Read(
                    ref htStartTime,
                    ref htEndTime,
                    1,
                    new int[] { request.ServerHandle },
                    out ppAnnotationValues,
                    out ppErrors);
            }
            catch (Exception e)
            {
                ComCallError(methodName, e);
                return StatusCodes.BadUnexpectedError;
            }
            finally
            {
                EndComCall(methodName);
            }

            // check for error.
            int[] errors = ComUtils.GetInt32s(ref ppErrors, 1, true);

            if (errors[0] < 0)
            {
                return StatusCodes.BadNodeIdUnknown;
            }

            // unmarshal the results.
            OPCHDA_ANNOTATION result = (OPCHDA_ANNOTATION)Marshal.PtrToStructure(ppAnnotationValues, typeof(OPCHDA_ANNOTATION));

            if (result.dwNumValues > 0)
            {
                DateTime[] timestamps = ComUtils.GetDateTimes(ref result.ftTimeStamps, result.dwNumValues, true);
                string[] annotations = ComUtils.GetUnicodeStrings(ref result.szAnnotation, result.dwNumValues, true);
                DateTime[] annotationTimes = ComUtils.GetDateTimes(ref result.ftAnnotationTime, result.dwNumValues, true);
                string[] userNames = ComUtils.GetUnicodeStrings(ref result.szUser, result.dwNumValues, true);

                request.Annotations = new List<DataValue>(result.dwNumValues);

                for (int ii = 0; ii < result.dwNumValues; ii++)
                {
                    Annotation annotation = new Annotation();
                    annotation.AnnotationTime = annotationTimes[ii];
                    annotation.Message = annotations[ii];
                    annotation.UserName = userNames[ii];

                    DataValue value = new DataValue(new Variant(new ExtensionObject(annotation)));
                    value.SourceTimestamp = timestamps[ii];
                    request.Annotations.Add(value);
                }
            }

            Marshal.FreeCoTaskMem(ppAnnotationValues);

            return StatusCodes.Good;
        }
        #endregion
        
        #region Private Fields
        private object m_lock = new object();
        private ComHdaClientConfiguration m_configuration;
        private HdaAttribute[] m_supportedAttributes;
        private byte m_annotationAccessLevel;
        private OPCHDA_UPDATECAPABILITIES m_updateCapabilities;
        private int m_maxReturnValues;
        #endregion
    }
}
