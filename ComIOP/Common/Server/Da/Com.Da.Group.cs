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
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.Threading;

using OpcRcw.Da;
using OpcRcw.Comn;
using Opc.Ua.Client;

namespace Opc.Ua.Com.Server.Da
{    
	/// <summary>
	/// A class that implements the COM-DA interfaces.
	/// </summary>
	public class Group : 
		ConnectionPointContainer,
		IDisposable,
		IOPCItemMgt,
		IOPCSyncIO,
		IOPCSyncIO2,
		IOPCAsyncIO2,
		IOPCAsyncIO3,
		IOPCGroupStateMgt,
		IOPCGroupStateMgt2,
		IOPCItemDeadbandMgt,
		IOPCItemSamplingMgt
	{	
		/// <summary>
		/// Initializes the object with the default values.
		/// </summary>
		public Group(
			Server                        server,
            object                        serverLock,
            string                        name,
            int                           serverHandle,
            int                           clientHandle,
            int                           updateRate,
            bool                          active,
            float                         deadband,
			int                           lcid,
			int                           timebias,
            Subscription                  subscription,
            NodeIdDictionary<CachedValue> cache)
		{
            // register interface for a group object as a connection point.
			RegisterInterface(typeof(OpcRcw.Da.IOPCDataCallback).GUID);

            // all the groups use the same lock as the server object. 
            // this is necessary because the session/subscription objects are not thread safe.
            m_lock = serverLock;

            // set default values.
			m_server                = server;
            m_subscription          = subscription;
            m_session               = subscription.Session;
			m_name                  = name;
			m_serverHandle          = serverHandle;
            m_clientHandle          = clientHandle;
            m_updateRate            = updateRate;
            m_active                = false;
            m_deadband              = deadband;
			m_lcid                  = lcid;
			m_timebias              = timebias;
            m_enabled               = true;
            m_advised               = false;
            m_cache                 = cache;
            m_defaultKeepAliveCount = subscription.KeepAliveCount;

            this.PublishStateChanged(active, null);
        }

        #region Public Properties
		/// <summary>
		/// The unique server assigned handle for the group.
		/// </summary>
		public int ServerHandle
		{
            get { return m_serverHandle; }
        }
        #endregion

        #region IDisposable Members
        /// <summary>
		/// Frees the group and all of its resources.
		/// </summary>
		public void Dispose()
		{
			lock (m_lock)
			{
                // delete the subscription with supressing an exception - (true).
                m_subscription.Delete(true); 
            }
		}
		#endregion

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^//
		#region IOPCItemMgt Members
        /// <summary>
        /// IOPCItemMgt::SetActiveState - Sets one or more items in a group to active or inactive state.
        /// </summary>
		public void SetActiveState(
            int dwCount, 
            int[] phServer, 
            int bActive, 
            out System.IntPtr ppErrors)
	    {
            // validate arguments.
            if (dwCount == 0 || phServer == null || dwCount != phServer.Length)
            {
                throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
            }

            try
            {
                int[] errors = new int[dwCount];                    
                Item[] itemsToModify = new Item[dwCount];
    
                List<MonitoredItem> monitoredItems = new List<MonitoredItem>();                     
                MonitoringMode monitoringMode = (bActive != 0)?MonitoringMode.Reporting:MonitoringMode.Disabled;
                
		        lock (m_lock)
		        {
                    if (m_subscription == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

                    // update locally cached client handle.
                    for (int ii = 0; ii < dwCount; ii++)
                    {
                        Item itemToModify = null;

                        if (!m_items.TryGetValue(phServer[ii], out itemToModify))
                        {
                            errors[ii] = ResultIds.E_INVALIDHANDLE;
                            continue;
                        }
                        
                        monitoredItems.Add(itemToModify.MonitoredItem);

                        itemsToModify[ii] = itemToModify;
                        errors[ii] = ResultIds.S_OK;
                    }

                    // update items.
                    if (monitoredItems.Count > 0)
                    {
                        m_subscription.SetMonitoringMode(monitoringMode, monitoredItems);
                    }

                    // check for any errors.
                    for (int ii = 0; ii < itemsToModify.Length; ii++)
                    {
                        Item itemToModify = itemsToModify[ii];

                        if (itemToModify == null)
                        {
                            continue;
                        }
                        
                        // note that the old sampling rate may no longer work if an error occurred here.
                        if (ServiceResult.IsBad(itemToModify.MonitoredItem.Status.Error))
                        {
                            // restore monitoring mode.
                            itemToModify.MonitoredItem.MonitoringMode = (itemToModify.Active)?MonitoringMode.Reporting:MonitoringMode.Disabled;                            
                            errors[ii] = ResultIds.E_FAIL;
                            continue;
                        }
                        
                        itemToModify.Active = bActive != 0;
                        itemToModify.MonitoredItem.DequeueValues();

                        if (!itemToModify.Active)
                        {
                            itemToModify.LastQualitySent = OpcRcw.Da.Qualities.OPC_QUALITY_OUT_OF_SERVICE;
                        }
                    }
                }

                // marshal error codes.
                ppErrors = ComUtils.GetInt32s(errors);
		    }
			catch (Exception e)
			{
				throw ComUtils.CreateComException(e);
			}
		}

        /// <summary>
        /// IOPCItemMgt::AddItems - Adds one or more items to a group.
        /// </summary>
		public void AddItems(
            int dwCount, 
            OPCITEMDEF[] pItemArray, 
            out System.IntPtr ppAddResults, 
            out System.IntPtr ppErrors)
		{
			lock (m_lock)
			{
                if (m_subscription == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

                // validate arguments.
                if (dwCount == 0 || pItemArray == null || dwCount != pItemArray.Length)
                {
                    throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
                }

                try
                {
                    // compile set of item modifications.
                    int[] errors = new int[dwCount];
                    OPCITEMRESULT[] results = new OPCITEMRESULT[dwCount];

                    List<Item> itemsToAdd = new List<Item>();

                    for (int ii = 0; ii < dwCount; ii++)
                    {
                        OPCITEMDEF itemToAdd = pItemArray[ii];
                           
                        // initialize result structure.
                        results[ii].hServer             = 0;
                        results[ii].dwBlobSize          = 0;
                        results[ii].pBlob               = IntPtr.Zero;
                        results[ii].vtCanonicalDataType = (short)VarEnum.VT_EMPTY;
                        results[ii].dwAccessRights      = 0;
                        results[ii].wReserved           = 0;

                        // parse node id.
                        NodeId nodeId = Server.ItemIdToNodeId(itemToAdd.szItemID);

                        if (nodeId == null)
                        {
                            errors[ii] = ResultIds.E_INVALIDITEMID;
                            continue;
                        }

                        // find node.
                        VariableNode variable = m_session.NodeCache.Find(nodeId) as VariableNode;
                        
                        if (variable == null)
                        {
                            errors[ii] = ResultIds.E_INVALIDITEMID;
                            continue;
                        }

                        // validated the requested datatype.
                        if (itemToAdd.vtRequestedDataType != 0)
                        {
                            if (ComUtils.GetSystemType(itemToAdd.vtRequestedDataType) == null)
                            {
                                errors[ii] = ResultIds.E_BADTYPE;
                                continue;
                            }
                        }
                                                
                        // fill in metadata.
                        results[ii].vtCanonicalDataType = (short)m_server.DataTypeToVarType(variable.DataType, variable.ValueRank);
                        results[ii].dwAccessRights      = ComUtils.GetAccessRights(variable.AccessLevel);

                        if (results[ii].vtCanonicalDataType == (short)VarEnum.VT_VARIANT)
                        {
                            results[ii].vtCanonicalDataType = (short)VarEnum.VT_EMPTY;
                        }

                        // create an item.
                        Item item = new Item();

                        item.ItemId       = itemToAdd.szItemID;
                        item.ClientHandle = itemToAdd.hClient;
                        item.ServerHandle = ii; // save this temporarily to correlate response to request list.
                        item.Active       = itemToAdd.bActive != 0;
                        item.ReqType      = (VarEnum)itemToAdd.vtRequestedDataType;
                        item.Variable     = variable;

                        // check if the item supports deadband.
                        INode euRange = m_session.NodeCache.Find(nodeId, ReferenceTypeIds.HasProperty, false, true, Opc.Ua.BrowseNames.EURange);

                        if (euRange != null)
                        {
                            item.DeadbandSupported = true;
                        }

                        // create a monitored item.
                        MonitoredItem monitoredItem = new MonitoredItem();

                        monitoredItem.StartNodeId      = nodeId;
                        monitoredItem.AttributeId      = Attributes.Value;
                        monitoredItem.MonitoringMode   = (item.Active)?MonitoringMode.Reporting:MonitoringMode.Disabled;
                        monitoredItem.SamplingInterval = m_updateRate;
                        monitoredItem.QueueSize        = 0;
                        monitoredItem.DiscardOldest    = true;
                        monitoredItem.Encoding         = null;
                        monitoredItem.Filter           = null;
                        monitoredItem.IndexRange       = null;
                        
                        if (m_deadband != 0 && item.DeadbandSupported)
                        {
                            DataChangeFilter filter = new DataChangeFilter();
                            
                            filter.DeadbandType  = (uint)(int)DeadbandType.Percent;
                            filter.DeadbandValue = m_deadband;
                            filter.Trigger       = DataChangeTrigger.StatusValue;

                            monitoredItem.Filter = filter;
                        }

                        item.MonitoredItem = monitoredItem;
                        itemsToAdd.Add(item); 

                        // update the subscription.
                        m_subscription.AddItem(monitoredItem);
                    }
                    
                    if (itemsToAdd.Count > 0)
                    {                    
                        // create monitored items on the UA server.
                        m_subscription.ApplyChanges();

                        foreach (Item item in itemsToAdd)
                        {
                            // check for error during add.
                            int index = item.ServerHandle;
                            MonitoredItem monitoredItem = item.MonitoredItem;

                            if (ServiceResult.IsBad(monitoredItem.Status.Error))
                            {
                                errors[index] = Server.MapReadStatusToErrorCode(monitoredItem.Status.Error.StatusCode);
                                m_subscription.RemoveItem(monitoredItem);
                                continue;
                            }

                            // save server handle.
                            results[index].hServer = item.ServerHandle = Utils.ToInt32(monitoredItem.ClientHandle);

                            // add an entry in the cache.
                            CreateCacheEntry(item);
                            
                            // index item.
                            m_items[item.ServerHandle] = item;
                        }
                    }
                        
                    // marshal the results.
                    ppAddResults = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(OPCITEMRESULT))*dwCount);
                    IntPtr pos = ppAddResults;
                                        
                    for (int ii = 0; ii < dwCount; ii++)
                    {
                        Marshal.StructureToPtr(results[ii], pos, false);
                        pos = (IntPtr)(pos.ToInt32() + Marshal.SizeOf(typeof(OpcRcw.Da.OPCITEMRESULT)));
                    }
                    
                    // marshal error codes.
                    ppErrors = ComUtils.GetInt32s(errors);
                }
                catch (Exception e)
                {
                    throw ComUtils.CreateComException(e);
                }
            }
		}

        /// <summary>
        /// IOPCItemMgt::SetClientHandles - Changes the client handle for one or more items in a group.
        /// </summary>
		public void SetClientHandles(
            int dwCount, 
            int[] phServer, 
            int[] phClient, 
            out System.IntPtr ppErrors)
		{
            lock (m_lock)
            {
                if (m_subscription == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

                // validate arguments.
                if (dwCount == 0 || phServer == null || phClient == null || dwCount != phServer.Length || dwCount != phClient.Length)
                {
                    throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
                }

                try
                {
                    int[] errors = new int[dwCount];

                    // update locally cached client handle.
                    for (int ii = 0; ii < dwCount; ii++)
                    {
                        Item itemToModify = null;

                        if (!m_items.TryGetValue(phServer[ii], out itemToModify))
                        {
                            errors[ii] = ResultIds.E_INVALIDHANDLE;
                            continue;
                        }

                        itemToModify.ClientHandle = phClient[ii];
                        errors[ii] = ResultIds.S_OK;
                    }

                    // marshal error codes.
                    ppErrors = ComUtils.GetInt32s(errors);
                }
                catch (Exception e)
                {
                    throw ComUtils.CreateComException(e);
                }
            }
		}

        /// <summary>
        /// IOPCItemMgt::SetDatatypes - Changes the requested data type for one or more items in a group.
        /// </summary>
		public void SetDatatypes(
            int dwCount, 
            int[] phServer, 
            short[] pRequestedDatatypes, 
            out System.IntPtr ppErrors)
		{
			lock (m_lock)
			{
				if (m_subscription == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

				// validate arguments.
				if (dwCount == 0 || phServer == null || pRequestedDatatypes == null)
				{
					throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
				}

				try
				{
                    int[] errors = new int[dwCount];

                    // update locally cached client handle.
                    for (int ii = 0; ii < dwCount; ii++)
                    {
                        Item itemToModify = null;

                        if (!m_items.TryGetValue(phServer[ii], out itemToModify))
                        {
                            errors[ii] = ResultIds.E_INVALIDHANDLE;
                            continue;
                        }
                        
                        // validate the requested datatype.
                        if (pRequestedDatatypes[ii] != 0)
                        {
                            if (ComUtils.GetSystemType(pRequestedDatatypes[ii]) == null)
                            {
                                errors[ii] = ResultIds.E_BADTYPE;
                                continue;
                            }
                        }
                                
                        itemToModify.ReqType = (VarEnum)pRequestedDatatypes[ii];
                        errors[ii] = ResultIds.S_OK;
                    }

                    // marshal error codes.
                    ppErrors = ComUtils.GetInt32s(errors);
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}

        /// <summary>
        /// IOPCItemMgt::ValidateItems - Determines if an item is valid.
        ///                              Also returns information about the item such as canonical datatype, etc.
        /// </summary>
		public void ValidateItems(
            int               dwCount, 
            OPCITEMDEF[]      pItemArray, 
            int               bBlobUpdate, 
            out System.IntPtr ppValidationResults,
            out System.IntPtr ppErrors)
		{
			lock (m_lock)
			{
				if (m_subscription == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

				// validate arguments.
				if (dwCount == 0 || pItemArray == null || dwCount != pItemArray.Length)
				{
					throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
				}

				try
				{
                    int[] errors = new int[dwCount];                    
                    OPCITEMRESULT[] results = new OPCITEMRESULT[dwCount];

                    for (int ii = 0; ii < dwCount; ii++)
					{
						results[ii].hServer             = 0;
						results[ii].dwBlobSize          = 0;
						results[ii].pBlob               = IntPtr.Zero;
						results[ii].vtCanonicalDataType = (short)VarEnum.VT_EMPTY;
						results[ii].dwAccessRights      = 0;
						results[ii].wReserved           = 0;
                						
                        // parse node id.
                        NodeId nodeId = Server.ItemIdToNodeId(pItemArray[ii].szItemID);

                        if (nodeId == null)
                        {
                            errors[ii] = ResultIds.E_INVALIDITEMID;
                            continue;
                        }

                        // find node.
                        VariableNode variable = m_session.NodeCache.Find(nodeId) as VariableNode;
                        
                        if (variable == null)
                        {
                            errors[ii] = ResultIds.E_INVALIDITEMID;
                            continue;
                        }
                        
                        // validated the requested datatype.
                        if (pItemArray[ii].vtRequestedDataType != 0)
                        {
                            if (ComUtils.GetSystemType(pItemArray[ii].vtRequestedDataType) == null)
                            {
                                errors[ii] = ResultIds.E_BADTYPE;
                                continue;
                            }
                        }
                                
                        // fill in metadata.
                        results[ii].vtCanonicalDataType = (short)m_server.DataTypeToVarType(variable.DataType, variable.ValueRank);
                        results[ii].dwAccessRights      = ComUtils.GetAccessRights(variable.AccessLevel);

                        if (results[ii].vtCanonicalDataType == (short)VarEnum.VT_VARIANT)
                        {
                            results[ii].vtCanonicalDataType = (short)VarEnum.VT_EMPTY;
                        }

                        errors[ii] = ResultIds.S_OK;
                    }
                    
                    // marshal the results.
                    ppValidationResults = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(OPCITEMRESULT))*dwCount);
                    IntPtr pos = ppValidationResults;
                                        
                    for (int ii = 0; ii < dwCount; ii++)
                    {
                        Marshal.StructureToPtr(results[ii], pos, false);
                        pos = (IntPtr)(pos.ToInt32() + Marshal.SizeOf(typeof(OpcRcw.Da.OPCITEMRESULT)));
                    }
                    
					// marshal error codes.
					ppErrors = ComUtils.GetInt32s(errors);
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}

        /// <summary>
        /// IOPCItemMgt::CreateEnumerator - Creates an enumerator for the items in the group.
        /// </summary>
		public void CreateEnumerator(ref Guid riid, out object ppUnk)
		{
			lock (m_lock)
			{
				if (m_subscription == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

				if (riid != typeof(OpcRcw.Da.IEnumOPCItemAttributes).GUID)
				{
					throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
				}

				try
				{			

                    List<EnumOPCItemAttributes.ItemAttributes> itemsToEnumerate = new List<EnumOPCItemAttributes.ItemAttributes>();

                    foreach (Item item in m_items.Values)
                    {
						EnumOPCItemAttributes.ItemAttributes attributes = new EnumOPCItemAttributes.ItemAttributes();

						attributes.ItemID            = item.ItemId;
						attributes.AccessPath        = String.Empty;
						attributes.ClientHandle      = item.ClientHandle;
						attributes.ServerHandle      = item.ServerHandle;
						attributes.Active            = item.Active;
						attributes.RequestedDataType = item.ReqType;
						attributes.AccessRights      = ComUtils.GetAccessRights(item.Variable.AccessLevel);
						attributes.CanonicalDataType = m_server.DataTypeToVarType(item.Variable.DataType, item.Variable.ValueRank);
						attributes.EuType            = OpcRcw.Da.OPCEUTYPE.OPC_NOENUM;
						attributes.MaxValue          = 0; 
						attributes.MinValue          = 0; 
						attributes.EuInfo            = null;

                        if (attributes.CanonicalDataType == VarEnum.VT_VARIANT)
                        {
                            attributes.CanonicalDataType = VarEnum.VT_EMPTY;
                        }

                        // find euRange.
                        VariableNode euRange = m_session.NodeCache.Find(
                            item.Variable.NodeId, 
                            ReferenceTypeIds.HasProperty, 
                            false, 
                            true, 
                            Opc.Ua.BrowseNames.EURange) as VariableNode;

                        if (euRange != null)
                        {						
                            attributes.EuType = OpcRcw.Da.OPCEUTYPE.OPC_ANALOG;

                            Range range = ExtensionObject.ToEncodeable(euRange.Value.Value as ExtensionObject) as Range;

                            if (range != null)
                            {
						        attributes.MaxValue = range.High; 
						        attributes.MinValue = range.Low; 
                            }
                        }
                        
                        // find enumStrings.
                        VariableNode enumStrings = m_session.NodeCache.Find(
                            item.Variable.NodeId, 
                            ReferenceTypeIds.HasProperty, 
                            false, 
                            true, 
                            Opc.Ua.BrowseNames.EnumStrings) as VariableNode;

                        if (enumStrings != null)
                        {						
                            attributes.EuType = OpcRcw.Da.OPCEUTYPE.OPC_ENUMERATED;

                            LocalizedText[] strings = enumStrings.Value.Value as LocalizedText[];

                            if (strings != null)
                            {
                                attributes.EuInfo = new string[strings.Length];

                                for (int ii = 0; ii < strings.Length; ii++)
                                {
                                    attributes.EuInfo[ii] = strings[ii].Text;
                                }
                            }
                        }

                        // add item
                        itemsToEnumerate.Add(attributes);
                    }

                    // create enumerator.
					ppUnk = new EnumOPCItemAttributes(itemsToEnumerate);
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}

        /// <summary>
        /// IOPCItemMgt::RemoveItems - Removes/Deletes one or more items from a group.
        /// </summary>
		public void RemoveItems(
            int               dwCount, 
            int[]             phServer, 
            out System.IntPtr ppErrors)
		{
			lock (m_lock)
			{
				// validate arguments.
				if (dwCount == 0 || phServer == null)
				{
					throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
				}

				try
				{
                    int[] errors = new int[dwCount];

                    // get list of items to delete.
                    for (int ii = 0; ii < dwCount; ii++)
                    {
                        Item itemToDelete = null;

                        if (!m_items.TryGetValue(phServer[ii], out itemToDelete))
                        {
                            errors[ii] = ResultIds.E_INVALIDHANDLE;
                            continue;
                        }
                        
                        // release entry in the cache.
                        DeleteCacheEntry(itemToDelete);

                        m_subscription.RemoveItem(itemToDelete.MonitoredItem);
                        errors[ii] = ResultIds.S_OK; 
                    }

                    // apply changes.
                    m_subscription.ApplyChanges();

                    // update local state.
                    for (int ii = 0; ii < dwCount; ii++)
                    {
                        m_items.Remove(phServer[ii]);
                    }

                    // marshal error codes.
                    ppErrors = ComUtils.GetInt32s(errors);
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
            }
		}
		#endregion

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^//
        #region IOPCSyncIO Members
        /// <summary>
        /// IOPCSyncIO::Read - Reads the value, quality and timestamp information for one or more items in a group
        /// </summary>
		public void Read(
            OpcRcw.Da.OPCDATASOURCE dwSource, 
            int                     dwCount, 
            int[]                   phServer, 
            out System.IntPtr       ppItemValues, 
            out System.IntPtr       ppErrors)
		{
            // validate arguments.
            if (dwCount == 0 || phServer == null || dwCount != phServer.Length)
            {
                throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
            }

			try
			{
				OpcRcw.Da.OPCITEMSTATE[] results = new OpcRcw.Da.OPCITEMSTATE[dwCount];
                int[] errors = new int[dwCount];
                VarEnum[] reqTypes = new VarEnum[dwCount];

                // use the minimum max age for all items.
                int maxAge = (dwSource == OPCDATASOURCE.OPC_DS_CACHE)?Int32.MaxValue:0;

                // build list of values to read.
                ReadValueIdCollection nodesToRead = new ReadValueIdCollection();
                List<Item> itemsToRead = new List<Item>();
                        
		        lock (m_lock)
		        {
			        if (m_subscription == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

                    for (int ii = 0; ii < dwCount; ii++)
                    {
                        results[ii].hClient     = 0;
                        results[ii].vDataValue  = null;
                        results[ii].wQuality    = OpcRcw.Da.Qualities.OPC_QUALITY_BAD;  
                        results[ii].ftTimeStamp = ComUtils.GetFILETIME(DateTime.MinValue);
                        results[ii].wReserved   = 0;

                        Item itemToRead = null;

                        if (!m_items.TryGetValue(phServer[ii], out itemToRead))
                        {
                            errors[ii] = ResultIds.E_INVALIDHANDLE;
                            continue;
                        }

                        results[ii].hClient = itemToRead.ClientHandle;

                        // check if reading from the cache.
                        if (dwSource == OPCDATASOURCE.OPC_DS_CACHE)
                        {
                            // read the value from cache.
                            DataValue cachedValue = ReadCachedValue(itemToRead, Int32.MaxValue);

                            if (cachedValue != null)
                            {
                                // get value from the cache.
                                object value = null;
                                short quality = Qualities.OPC_QUALITY_BAD;
                                DateTime timestamp = DateTime.MinValue;

                                errors[ii] = ProcessReadResult(
                                    phServer[ii],
                                    itemToRead.ReqType,
                                    cachedValue,
                                    out value,
                                    out quality,
                                    out timestamp);

                                // all done if a suitable value is in the cache.
                                if (!m_active || !itemToRead.Active)
                                {
                                    quality = Qualities.OPC_QUALITY_OUT_OF_SERVICE;
                                }

                                results[ii].vDataValue  = value;
                                results[ii].wQuality    = quality; 
                                results[ii].ftTimeStamp = ComUtils.GetFILETIME(timestamp); 
                                continue;
                            }
                        }

                        // save the requested data type.
                        reqTypes[ii] = itemToRead.ReqType;

                        ReadValueId nodeToRead = new ReadValueId();

                        nodeToRead.NodeId      = itemToRead.MonitoredItem.ResolvedNodeId;
                        nodeToRead.AttributeId = Attributes.Value;

                        // needed to correlate results to input.
                        nodeToRead.Handle = ii;

                        nodesToRead.Add(nodeToRead);
                        itemsToRead.Add(itemToRead);
                    }
                }
                    
                // read values from server.
                DataValueCollection values = null;
                DiagnosticInfoCollection diagnosticInfos = null;
                
                if (nodesToRead.Count > 0)
                {
                    m_session.Read(
                        null,
                        maxAge,
                        TimestampsToReturn.Both,
                        nodesToRead,
                        out values,
                        out diagnosticInfos);
                
                    // validate response from the UA server.
                    ClientBase.ValidateResponse(values, nodesToRead);
                    ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);
                }
                
                for (int ii = 0; ii < nodesToRead.Count; ii++)
                {
                    // get index in original array.
                    int index = (int)nodesToRead[ii].Handle;
                                     
                    // process the read result.
                    object value = null;
                    short quality = Qualities.OPC_QUALITY_BAD;
                    DateTime timestamp = DateTime.MinValue;

                    int error = ProcessReadResult(
                        phServer[index], 
                        reqTypes[index], 
                        values[ii],
                        out value, 
                        out quality,
                        out timestamp);

                    // update the cache.
                    UpdateCachedValue(itemsToRead[ii], values[ii]);

                    // check for error.
                    if (error < 0)
                    {                            
                        errors[index] = error;
                        continue;
                    }

                    // update response.                                            
                    results[index].vDataValue  = value;
                    results[index].wQuality    = quality; 
                    results[index].ftTimeStamp = ComUtils.GetFILETIME(timestamp);  
                  
                    errors[index] = ResultIds.S_OK;
                }
                
                // marshal the results.
                ppItemValues = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(OPCITEMSTATE))*dwCount);
                IntPtr pos = ppItemValues;
                                    
                for (int ii = 0; ii < dwCount; ii++)
                {
                    Marshal.StructureToPtr(results[ii], pos, false);
                    pos = (IntPtr)(pos.ToInt32() + Marshal.SizeOf(typeof(OpcRcw.Da.OPCITEMSTATE)));
                }
                
				// marshal error codes.
				ppErrors = ComUtils.GetInt32s(errors);
			}
			catch (Exception e)
			{
                Utils.Trace(e, "Error reading items.");
				throw ComUtils.CreateComException(e);
			}
		}
        
        /// <summary>
        /// Processes a value read from the server.
        /// </summary>
        private int ProcessReadResult(
            int           serverHandle, 
            VarEnum       reqType, 
            DataValue     dataValue, 
            out object    value,
            out short     quality,
            out DateTime  timestamp)
        {
            value = null;
            quality = Qualities.OPC_QUALITY_BAD;
            timestamp = DateTime.MinValue;

            // check for invalid response from server.
            if (dataValue == null)
            {
                return ResultIds.E_FAIL;
            }
            
            // check for error.
            int error = Server.MapReadStatusToErrorCode(dataValue.StatusCode);

            if (error < 0)
            {
                return error;
            }

            // convert UA value to VARIANT value.
            value = ComUtils.GetVARIANT(dataValue.Value);
            quality = ComUtils.GetQualityCode(dataValue.StatusCode); 
            timestamp = dataValue.SourceTimestamp;
            
            // do any data conversion.
            if (dataValue.Value != null && reqType != VarEnum.VT_EMPTY)
            {
                object changedValue = null;
                error = ComUtils.ChangeTypeForCOM(value, reqType, out changedValue);

                if (error < 0)
                {
                    value = null;
                    quality = OpcRcw.Da.Qualities.OPC_QUALITY_BAD;
                    return error;
                }

                value = changedValue;
            }

            return ResultIds.S_OK;
        }
        
        /// <summary>
        /// IOPCSyncIO::Write - Writes values to one or more items in a group.
        /// </summary>
		public void Write(
            int dwCount, 
            int[] phServer, 
            object[] pItemValues, 
            out System.IntPtr ppErrors)
		{
			// validate arguments.
			if (dwCount == 0 || phServer == null || pItemValues == null || dwCount != phServer.Length || dwCount != pItemValues.Length)
			{
				throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
			}

			try
			{
                int[] errors = new int[dwCount];

                // build list of values to write.
                WriteValueCollection valuesToWrite = new WriteValueCollection();

		        lock (m_lock)
		        {
			        if (m_subscription == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);
                                            
                    for (int ii = 0; ii < dwCount; ii++)
                    {
                        Item itemToWrite = null;

                        if (!m_items.TryGetValue(phServer[ii], out itemToWrite))
                        {
                            errors[ii] = ResultIds.E_INVALIDHANDLE;
                            continue;
                        }

                        VariableNode variable = itemToWrite.Variable;

                        WriteValue valueToWrite = new WriteValue();

                        valueToWrite.NodeId      = variable.NodeId;
                        valueToWrite.IndexRange  = null;
                        valueToWrite.AttributeId = Attributes.Value;

                        DataValue value = new DataValue();
                        
                        int error = 0;
                        value.Value = m_server.VariantValueToValue(variable, pItemValues[ii], out error);

                        if (error != ResultIds.S_OK)
                        {
                            errors[ii] = error;
                            continue;
                        }

                        valueToWrite.Value = value;
                        
                        // needed to correlate results to input.
                        valueToWrite.Handle = ii;
                        
                        valuesToWrite.Add(valueToWrite);
                    }
                }

                // write values from server.
                StatusCodeCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;
                
                if (valuesToWrite.Count > 0)
                {
                    m_session.Write(
                        null,
                        valuesToWrite,
                        out results,
                        out diagnosticInfos);
                
                    // validate response from the UA server.
                    ClientBase.ValidateResponse(results, valuesToWrite);
                    ClientBase.ValidateDiagnosticInfos(diagnosticInfos, valuesToWrite);
                                    
                    //Utils.Trace(
                    //   "SyncWrite: GroupHandle={0}, ServerHandle={1}, Value={2}", 
                    //   m_clientHandle,
                    //   phServer[0],
                    //   valuesToWrite[0].Value.WrappedValue);
                }
                
                for (int ii = 0; ii < valuesToWrite.Count; ii++)
                {
                    // get index in original array.
                    int index = (int)valuesToWrite[ii].Handle;

                    // map UA code to DA code. 
                    errors[index] = Server.MapWriteStatusToErrorCode(results[ii]);
                }

                // marshal error codes.
                ppErrors = ComUtils.GetInt32s(errors);
			}
			catch (Exception e)
			{
                Utils.Trace(e, "Error writing items.");
				throw ComUtils.CreateComException(e);
			}
		}
		#endregion

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^//
		#region IOPCSyncIO2 Members
        /// <summary>
        /// IOPCSyncIO2::ReadMaxAge - Reads one or more values, qualities and timestamps for the items specified.
		/// </summary>
		public void ReadMaxAge(
            int               dwCount, 
            int[]             phServer, 
            int[]             pdwMaxAge, 
            out System.IntPtr ppvValues, 
            out System.IntPtr ppwQualities, 
            out System.IntPtr ppftTimeStamps, 
            out System.IntPtr ppErrors)
		{
			// validate arguments.
			if (dwCount == 0 || phServer == null || pdwMaxAge == null || dwCount != phServer.Length || dwCount != pdwMaxAge.Length)
			{
				throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
			}

			try
			{ 
                object[] values = new object[dwCount];
                short[] qualities = new short[dwCount];
                DateTime[] timestamps = new DateTime[dwCount];
                int[] errors = new int[dwCount];
                VarEnum[] reqTypes = new VarEnum[dwCount];
                
                // use the minimum max age for all items.
                int maxAge = Int32.MaxValue;

                // build list of values to read.
                ReadValueIdCollection nodesToRead = new ReadValueIdCollection();
                List<Item> itemsToRead = new List<Item>();
                
			    lock (m_lock)
			    {
				    if (m_subscription == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

                    for (int ii = 0; ii < dwCount; ii++)
                    {
                        values[ii]     = null;
                        qualities[ii]  = OpcRcw.Da.Qualities.OPC_QUALITY_BAD;                            
                        timestamps[ii] = DateTime.MinValue;

                        Item itemToRead = null;

                        if (!m_items.TryGetValue(phServer[ii], out itemToRead))
                        {
                            errors[ii] = ResultIds.E_INVALIDHANDLE;
                            continue;
                        }
                        
                        // check if reading from the cache.
                        if (pdwMaxAge[ii] > 0)
                        {
                            DataValue cachedValue = ReadCachedValue(itemToRead, pdwMaxAge[ii]);

                            if (cachedValue != null)
                            {
                                // get value from the cache.
                                object value = null;
                                short quality = Qualities.OPC_QUALITY_BAD;
                                DateTime timestamp = DateTime.MinValue;

                                errors[ii] = ProcessReadResult(
                                    phServer[ii],
                                    itemToRead.ReqType,
                                    cachedValue,
                                    out value,
                                    out quality,
                                    out timestamp);

                                // all done if a suitable value is in the cache.
                                if (!m_active || !itemToRead.Active)
                                {
                                    quality = Qualities.OPC_QUALITY_OUT_OF_SERVICE;
                                }

                                values[ii]     = value;
                                qualities[ii]  = quality; 
                                timestamps[ii] = timestamp; 
                                continue;
                            }
                        }

                        ReadValueId nodeToRead = new ReadValueId();

                        nodeToRead.NodeId      = itemToRead.Variable.NodeId;
                        nodeToRead.AttributeId = Attributes.Value;
                        
                        // save the requested data type.
                        reqTypes[ii] = itemToRead.ReqType;

                        // needed to correlate results to input.
                        nodeToRead.Handle = ii;
                        
                        nodesToRead.Add(nodeToRead);
                        itemsToRead.Add(itemToRead);

                        // calculate max age.
                        if (maxAge > pdwMaxAge[ii])
                        {
                            maxAge = pdwMaxAge[ii];
                        }
                    }
                }

                // read values from server.
                DataValueCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;
                
                if (nodesToRead.Count > 0)
                {
                    m_session.Read(
                        null,
                        maxAge,
                        TimestampsToReturn.Both,
                        nodesToRead,
                        out results,
                        out diagnosticInfos);
                
                    // validate response from the UA server.
                    ClientBase.ValidateResponse(results, nodesToRead);
                    ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);
                }
                
                for (int ii = 0; ii < nodesToRead.Count; ii++)
                {
                    // get index in original array.
                    int index = (int)nodesToRead[ii].Handle;
                    
                    // process the read result.
                    object value = null;
                    short quality = Qualities.OPC_QUALITY_BAD;
                    DateTime timestamp = DateTime.MinValue;

                    int error = ProcessReadResult(
                        phServer[index], 
                        reqTypes[index], 
                        results[ii],
                        out value, 
                        out quality,
                        out timestamp);

                    // update the read cache.
                    UpdateCachedValue(itemsToRead[ii], results[ii]);

                    // check for error.
                    if (error < 0)
                    {                            
                        errors[index] = error;
                        continue;
                    }

                    // update response.                                            
                    values[index]     = value;
                    qualities[index]  = quality; 
                    timestamps[index] = timestamp; 

                    errors[index] = ResultIds.S_OK;
                }

				// marshal results.
				ppvValues      = ComUtils.GetVARIANTs(values, false);
				ppwQualities   = ComUtils.GetInt16s(qualities);
				ppftTimeStamps = ComUtils.GetFILETIMEs(timestamps);

                // marshal error codes.
                ppErrors = ComUtils.GetInt32s(errors);
            }
			catch (Exception e)
			{
				throw ComUtils.CreateComException(e);
			}
		}
		
		/// <summary>
        /// IOPCSyncIO2::WriteVQT - Writes one or more values, qualities and timestamps for the items specified.
		/// </summary>
		public void WriteVQT(
            int dwCount, 
            int[] phServer, 
            OPCITEMVQT[] pItemVQT, 
            out System.IntPtr ppErrors)
		{
			// validate arguments.
			if (dwCount == 0 || phServer == null || pItemVQT == null || dwCount != phServer.Length || dwCount != pItemVQT.Length)
			{
				throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
			}

			try
            {
                int[] errors = new int[dwCount];
                
                // build list of values to write.
                WriteValueCollection valuesToWrite = new WriteValueCollection();
                    
			    lock (m_lock)
			    {
				    if (m_subscription == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

                    for (int ii = 0; ii < dwCount; ii++)
                    {
                        Item itemToWrite = null;

                        if (!m_items.TryGetValue(phServer[ii], out itemToWrite))
                        {
                            errors[ii] = ResultIds.E_INVALIDHANDLE;
                            continue;
                        }

                        WriteValue valueToWrite = new WriteValue();

                        valueToWrite.NodeId      = itemToWrite.Variable.NodeId;
                        valueToWrite.IndexRange  = null;
                        valueToWrite.AttributeId = Attributes.Value;

                        DataValue value = new DataValue();
                        
                        int error = 0;
                        value.Value = m_server.VariantValueToValue(itemToWrite.Variable, pItemVQT[ii].vDataValue, out error);

                        if (error != ResultIds.S_OK)
                        {
                            errors[ii] = error;
                            continue;
                        }

                        if (pItemVQT[ii].bQualitySpecified != 0)
                        {
                            value.StatusCode = ComUtils.GetQualityCode(pItemVQT[ii].wQuality);
                        }
                        
                        if (pItemVQT[ii].bTimeStampSpecified != 0)
                        {
                            value.SourceTimestamp = ComUtils.GetDateTime(pItemVQT[ii].ftTimeStamp);
                        }

                        valueToWrite.Value = value;
                        
                        // needed to correlate results to input.
                        valueToWrite.Handle = ii;
                        
                        valuesToWrite.Add(valueToWrite);
                    }                
			    }

                // write values from server.
                StatusCodeCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;
                
                if (valuesToWrite.Count > 0)
                {
                    m_session.Write(
                        null,
                        valuesToWrite,
                        out results,
                        out diagnosticInfos);
                
                    // validate response from the UA server.
                    ClientBase.ValidateResponse(results, valuesToWrite);
                    ClientBase.ValidateDiagnosticInfos(diagnosticInfos, valuesToWrite);
                }
                
                for (int ii = 0; ii < valuesToWrite.Count; ii++)
                {
                    // get index in original array.
                    int index = (int)valuesToWrite[ii].Handle;

                    // map UA code to DA code. 
                    errors[index] = Server.MapWriteStatusToErrorCode(results[ii]);
                }

                // marshal error codes.
                ppErrors = ComUtils.GetInt32s(errors);
            }
			catch (Exception e)
			{
				throw ComUtils.CreateComException(e);
			}
		}
        //vvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvvv//
		#endregion

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^//
        #region IOPCAsyncIO2 Members
        /// <summary>
        /// IOPCAsyncIO2::Read - Read one or more items in a group. The results are returned via the client�s 
        ///                      IOPCDataCallback connection established through the server�s IConnectionPointContainer.
        /// </summary>
		public void Read(
            int dwCount, 
            int[] phServer, 
            int dwTransactionID, 
            out int pdwCancelID, 
            out System.IntPtr ppErrors)
		{
            pdwCancelID = 0;

			// validate arguments.
			if (dwCount == 0 || phServer == null || dwCount != phServer.Length)
			{
				throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
			}

            // get callback object - nothing more to do if missing.
            IOPCDataCallback callback = (IOPCDataCallback)GetCallback(typeof(IOPCDataCallback).GUID);

            if (callback == null)
            {
		        throw ComUtils.CreateComException(ResultIds.CONNECT_E_NOCONNECTION);
            }

            try
			{
                int[] errors = new int[dwCount];

                // build list of values to read.
                ReadValueIdCollection nodesToRead = new ReadValueIdCollection();

		        lock (m_lock)
		        {                        
		            if (m_subscription == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

                    for (int ii = 0; ii < dwCount; ii++)
                    {
                        Item itemToRead = null;

                        if (!m_items.TryGetValue(phServer[ii], out itemToRead))
                        {
                            errors[ii] = ResultIds.E_INVALIDHANDLE;
                            continue;
                        }

                        ReadValueId nodeToRead = new ReadValueId();

                        nodeToRead.NodeId      = itemToRead.MonitoredItem.ResolvedNodeId;
                        nodeToRead.AttributeId = Attributes.Value;

                        // needed to correlate results to input.
                        nodeToRead.Handle = itemToRead;

                        nodesToRead.Add(nodeToRead);
                    }

                    // create a transaction.         
                    if (nodesToRead.Count > 0)
                    {
                        pdwCancelID = Utils.IncrementIdentifier(ref m_nextHandle);
                        m_transactions[pdwCancelID] = new AsyncReadTransaction(dwTransactionID, nodesToRead, false);
                    }
			    }
                    
                // read values from server.                    
                if (nodesToRead.Count > 0)
                {
                    m_session.BeginRead(
                        null,
                        0,
                        TimestampsToReturn.Both,
                        nodesToRead,
                        new AsyncCallback(OnReadComplete),
                        pdwCancelID);
                }
                                    
				// marshal error codes.
				ppErrors = ComUtils.GetInt32s(errors);
			}
			catch (Exception e)
			{
                Utils.Trace(e, "Error reading items.");
				throw ComUtils.CreateComException(e);
			}
		}
        
        /// <summary>
        /// Called when an asynchronous read operation completes.
        /// </summary>
        private void OnReadComplete(IAsyncResult result)
        {
            AsyncReadTransaction transaction = null;
            
            lock (m_lock)
			{
                // complete the operation.
                DataValueCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;
                
                int serviceError = ResultIds.S_OK;

                try
                {
                    m_session.EndRead(
                        result,
                        out results,
                        out diagnosticInfos);
                }
                catch (Exception)
                {
                    serviceError = ResultIds.E_FAIL;
                }                         

                try
                {
                    // check if transaction has been cancelled.
                    int cancelId = (int)result.AsyncState;

                    Transaction transaction2 = null;

                    if (!m_transactions.TryGetValue(cancelId, out transaction2))
                    {
                        return;
                    }

                    transaction = (AsyncReadTransaction)transaction2;
                    m_transactions.Remove(cancelId);                   
                    
                    // get callback object - nothing more to do if missing.
                    IOPCDataCallback callback = (IOPCDataCallback)GetCallback(typeof(IOPCDataCallback).GUID);

                    if (callback == null)
                    {
                        return;
                    }
     
                    // process results.
                    ReadValueIdCollection nodesToRead = transaction.NodesToRead;

                    List<CallbackValue> callbackValues = new List<CallbackValue>();

                    for (int ii = 0; ii < nodesToRead.Count; ii++)
                    {
                        // get item in originally read.
                        Item itemToRead = (Item)nodesToRead[ii].Handle;
                        
                        // process the read result.
                        object value = null;
                        short quality = Qualities.OPC_QUALITY_BAD;
                        DateTime timestamp = DateTime.MinValue;
                        int error = serviceError;

                        if (serviceError >= 0)
                        {
                            error = ProcessReadResult(
                                itemToRead.ServerHandle, 
                                itemToRead.ReqType, 
                                results[ii],
                                out value, 
                                out quality,
                                out timestamp);
                            
                            // update the cache.
                            UpdateCachedValue(itemToRead, results[ii]);
                        }

                        CallbackValue callbackValue = new CallbackValue();

                        callbackValue.ClientHandle = itemToRead.ClientHandle;
                        callbackValue.Value        = value;
                        callbackValue.Quality      = quality;
                        callbackValue.Timestamp    = timestamp;
                        callbackValue.Error        = error;

                        callbackValues.Add(callbackValue);
				    }

                    // queue the callback.
                    CallbackRequest request = new CallbackRequest();

                    request.CallbackType = (transaction.IsRefresh)?CallbackType.DataChange:CallbackType.Read;
                    request.Callback = callback;
                    request.TransactionId = transaction.TransactionId;
                    request.GroupHandle = m_clientHandle;
                    request.ServerHandle = m_serverHandle;
                    request.Values = callbackValues;

                    QueueCallbackRequest(request);
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Unexpected error processing asynchronous read callback.");
                }
			}
        }

        /// <summary>
        /// IOPCAsyncIO2::Write - Write one or more items in a group. The results are returned via the client�s 
        ///                       IOPCDataCallback connection established through the server�s IConnectionPointContainer.
        /// </summary>
		public void Write(
            int dwCount, 
            int[] phServer, 
            object[] pItemValues, 
            int dwTransactionID, 
            out int pdwCancelID, 
            out System.IntPtr ppErrors)
		{
            pdwCancelID = 0;

			// validate arguments.
			if (dwCount == 0 || phServer == null || pItemValues == null || dwCount != phServer.Length || dwCount != pItemValues.Length)
			{
				throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
			}

            // get callback object - nothing more to do if missing.
            IOPCDataCallback callback = (IOPCDataCallback)GetCallback(typeof(IOPCDataCallback).GUID);

            if (callback == null)
            {
		        throw ComUtils.CreateComException(ResultIds.CONNECT_E_NOCONNECTION);
            }
     
			try
			{
                int[] errors = new int[dwCount];
                
                // build list of values to write.
                WriteValueCollection valuesToWrite = new WriteValueCollection();
                    
			    lock (m_lock)
			    {
				    if (m_subscription == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);
                    
                    CallbackValue[] conversionErrors = null;

                    for (int ii = 0; ii < dwCount; ii++)
                    {
                        Item itemToWrite = null;

                        if (!m_items.TryGetValue(phServer[ii], out itemToWrite))
                        {
                            errors[ii] = ResultIds.E_INVALIDHANDLE;
                            continue;
                        }

                        VariableNode variable = itemToWrite.Variable;

                        WriteValue valueToWrite = new WriteValue();

                        valueToWrite.NodeId      = variable.NodeId;
                        valueToWrite.IndexRange  = null;
                        valueToWrite.AttributeId = Attributes.Value;

                        DataValue value = new DataValue();
                        
                        int error = 0;
                        value.Value = m_server.VariantValueToValue(variable, pItemValues[ii], out error);

                        if (error != ResultIds.S_OK)
                        {
                            // only allocate this array when it is needed.
                            if (conversionErrors == null)
                            {
                                conversionErrors = new CallbackValue[dwCount];
                            }

                            // create the callback item.
                            CallbackValue conversionError = new CallbackValue();

                            conversionError.ClientHandle = itemToWrite.ClientHandle;
                            conversionError.Error = error;

                            conversionErrors[ii] = conversionError;

                            errors[ii] = error;
                            continue;
                        }

                        valueToWrite.Value = value;
                        
                        // needed to correlate results to input.
                        valueToWrite.Handle = itemToWrite;
                        
                        valuesToWrite.Add(valueToWrite);
                    }

                    // create transaction.
                    if (valuesToWrite.Count > 0 || conversionErrors != null)
                    {
                        pdwCancelID = Utils.IncrementIdentifier(ref m_nextHandle);
                        m_transactions[pdwCancelID] = new AsyncWriteTransaction(dwTransactionID, valuesToWrite);
                    }
                    
                    // send conversion errors in the callback if no valid items available (CTT bug workaround).
                    if (valuesToWrite.Count == 0 && conversionErrors != null)
                    {   
                        // must return S_OK from this function if sending the errors in the callback.
                        List<CallbackValue> errorsToSend = new List<CallbackValue>();
             
                        for (int ii = 0; ii < conversionErrors.Length; ii++)
                        {
                            if (conversionErrors[ii] != null)
                            {
                                errors[ii] = ResultIds.S_OK;
                                errorsToSend.Add(conversionErrors[ii]);
                            }
                        }

                        // queue the request.
                        CallbackRequest request = new CallbackRequest();

                        request.CallbackType = CallbackType.Write;
                        request.Callback = callback;
                        request.TransactionId = dwTransactionID;
                        request.GroupHandle = m_clientHandle;
                        request.ServerHandle = m_serverHandle;
                        request.Values = errorsToSend;

                        QueueCallbackRequest(request);
                    }
			    }
                    
                // write values to server.                    
                if (valuesToWrite.Count > 0)
                {
                    m_session.BeginWrite(
                        null,
                        valuesToWrite,
                        new AsyncCallback(OnWriteComplete),
                        pdwCancelID);
                                    
                    // Utils.Trace(
                    //     "AsyncWrite: GroupHandle={0}, ServerHandle={1}, Value={2}, CancelID={3}", 
                    //     m_clientHandle,
                    //     phServer[0],
                    //     valuesToWrite[0].Value.WrappedValue,
                    //     pdwCancelID);
                }
                
                // marshal error codes.
                ppErrors = ComUtils.GetInt32s(errors);
            }
			catch (Exception e)
			{
                Utils.Trace(e, "Error writing items.");
				throw ComUtils.CreateComException(e);
			}
		}
        
        /// <summary>
        /// Called when an asynchronous write operation completes.
        /// </summary>
        private void OnWriteComplete(IAsyncResult result)
        {
            AsyncWriteTransaction transaction = null;
            
            lock (m_lock)
			{
                // complete the write operation.
                StatusCodeCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;
                
                int serviceError = ResultIds.S_OK;

                try
                {
                    m_session.EndWrite(
                        result,
                        out results,
                        out diagnosticInfos);
                }
                catch (Exception)
                {
                    serviceError = ResultIds.E_FAIL;
                }                         

                try
                {
                    // check if transaction has been cancelled.
                    int cancelId = (int)result.AsyncState;

                    Transaction transaction2 = null;

                    if (!m_transactions.TryGetValue(cancelId, out transaction2))
                    {
                        return;
                    }

                    transaction = (AsyncWriteTransaction)transaction2;
                    m_transactions.Remove(cancelId);                   
                    
                    // get callback object - nothing more to do if missing.
                    IOPCDataCallback callback = (IOPCDataCallback)GetCallback(typeof(IOPCDataCallback).GUID);

                    if (callback == null)
                    {
                        return;
                    }
     
                    // process results.
                    WriteValueCollection valuesToWrite = transaction.ValuesToWrite;
                    
                    List<CallbackValue> callbackValues = new List<CallbackValue>();

                    for (int ii = 0; ii < valuesToWrite.Count; ii++)
                    {
                        // get item originally written.
                        Item itemToWrite = (Item)valuesToWrite[ii].Handle;
                        
                        int error = serviceError;
                        
                        // get for operation level error.
                        if (serviceError >= 0)
                        {
                            error = Server.MapWriteStatusToErrorCode(results[ii]);
                        }

                        CallbackValue callbackValue = new CallbackValue();

                        callbackValue.ClientHandle = itemToWrite.ClientHandle;
                        callbackValue.Error        = error;

                        callbackValues.Add(callbackValue);
				    }

                    // queue the callback.
                    CallbackRequest request = new CallbackRequest();

                    request.CallbackType = CallbackType.Write;
                    request.Callback = callback;
                    request.TransactionId = transaction.TransactionId;
                    request.GroupHandle = m_clientHandle;
                    request.ServerHandle = m_serverHandle;
                    request.Values = callbackValues;

                    QueueCallbackRequest(request);
                }
                catch (Exception e)
                {
                    Utils.Trace(e, "Unexpected error processing asynchronous write callback.");
                }
			}
        }

		/// <summary>
        /// IOPCAsyncIO2::Cancel2 - Request that the server cancel an outstanding transaction.
		/// </summary>
		public void Cancel2(int dwCancelID)
		{
			lock (m_lock)
			{
				if (m_subscription == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);
				
                // get callback object - error if missing.
                IOPCDataCallback callback = (IOPCDataCallback)GetCallback(typeof(IOPCDataCallback).GUID);

                if (callback == null)
                {
					throw ComUtils.CreateComException(ResultIds.CONNECT_E_NOCONNECTION);
                }

				try
				{
                    // see if transaction has already completed.
                    Transaction transaction = null;

                    if (!m_transactions.TryGetValue(dwCancelID, out transaction))
                    {
					    throw ComUtils.CreateComException(ResultIds.E_FAIL);
                    }

                    // remove transaction - results will be discarded.
                    m_transactions.Remove(dwCancelID);
                                                        
                    // Utils.Trace(
                    //     "AsyncCancel: GroupHandle={0}, CancelID={1}", 
                    //     m_clientHandle,
                    //     dwCancelID);

                    // queue the callback.
                    CallbackRequest request = new CallbackRequest();

                    request.CallbackType = CallbackType.Cancel;
                    request.Callback = callback;
                    request.TransactionId = transaction.TransactionId;
                    request.GroupHandle = m_clientHandle;
                    request.ServerHandle = m_serverHandle;

                    QueueCallbackRequest(request);
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}

        /// <summary>
        /// IOPCAsyncIO2::Refresh2 - Forces a callback to IOPCDataCallback::OnDataChange for all active items 
        ///                          in the group (whether they have changed or not). Inactive items are not included in the callback.
        /// </summary>
		public void Refresh2(
            OpcRcw.Da.OPCDATASOURCE dwSource, 
            int dwTransactionID, 
            out int pdwCancelID)
		{
			// calculate max age.
			int maxAge = (dwSource == OPCDATASOURCE.OPC_DS_DEVICE)?0:Int32.MaxValue;

			// call refresh.
			RefreshMaxAge(maxAge, dwTransactionID, out pdwCancelID);
		}

        /// <summary>
        /// IOPCAsyncIO2::GetEnable - Retrieves the last Callback Enable value set with SetEnable.
        /// </summary>
        public void GetEnable(out int pbEnable)
        {
            lock (m_lock)
            {
				// check for callback.
				if (!IsConnected(typeof(IOPCDataCallback).GUID))
				{
					throw ComUtils.CreateComException(ResultIds.CONNECT_E_NOCONNECTION);
				}

                try
                {
                    pbEnable = (m_enabled)?1:0;
                }
                catch (Exception e)
                {
                    throw ComUtils.CreateComException(e);
                }
            }
        }

        /// <summary>
        /// IOPCAsyncIO2::SetEnable - Controls the operation of OnDataChange. 
        ///                           Setting Enable to FALSE will disable any OnDataChange callbacks with 
        ///                           a transaction ID of 0 (those which are not the result of a Refresh).
        /// </summary>
		public void SetEnable(int bEnable)
		{
			// check for callback.
			if (!IsConnected(typeof(IOPCDataCallback).GUID))
			{
				throw ComUtils.CreateComException(ResultIds.CONNECT_E_NOCONNECTION);
			}

            try
            {
                PublishStateChanged(null, (bEnable != 0));
            }
            catch (Exception e)
            {
                throw ComUtils.CreateComException(e);
            }
		}

        /// <summary>
        /// Updates publishing mode after the enabled or active state changed.
        /// </summary>
        private void PublishStateChanged(bool? active, bool? enabled)
        {
            bool stateChanged = false;
            bool publishingEnabled = false;

            lock (m_lock)
            {
                bool wasPublishing = m_active;

                if (active != null)
                {
                    m_active  = active.Value;
                }

                if (enabled != null)
                {
                    m_enabled  = enabled.Value;
                }

                publishingEnabled =  m_active;
                stateChanged = publishingEnabled != wasPublishing;

                if (stateChanged)
                {   
                    // flag the items as out of service.
                    if (!m_active)
                    {
                        foreach (Item item in m_items.Values)
                        {
                            item.LastQualitySent = OpcRcw.Da.Qualities.OPC_QUALITY_OUT_OF_SERVICE;
                        }
                    }

                    //Utils.Trace(
                    //     "PublishStateChange: GroupHandle={0}, Active {1}, Enabled {2}, Advised {3}", 
                    //     m_clientHandle,
                    //     m_active,
                    //     m_enabled,
                    //     m_advised);

                    // must always send an update immidiately.
                    if (publishingEnabled)
                    {
                        QueueDataChange(true);
                    }

                    // schedule the next update.
                    m_nextUpdate = DateTime.UtcNow.Ticks + m_updateRate*TimeSpan.TicksPerMillisecond;
                }
            }
                
            // enable publishing.
            if (stateChanged)
            {
                m_subscription.SetPublishingMode(publishingEnabled);
            }
        }
        
        /// <summary>
        /// Called when the client adds the callback.
        /// </summary>
        public override void OnAdvise(Guid riid)
        {
            base.OnAdvise(riid);

            if (riid == typeof(IOPCDataCallback).GUID)
            {
                lock (m_lock)
                {
                    if (!m_advised && m_active)
                    {                        
                        QueueDataChange(true);
                    }

                    m_advised = true;
                }
            }
        }

        /// <summary>
        /// Called when the client removes the callback.
        /// </summary>
        public override void OnUnadvise(Guid riid)
        {
            base.OnAdvise(riid);

            if (riid == typeof(IOPCDataCallback).GUID)
            {
                m_advised = false;
            }
        }

        /// <summary>
        /// Processes a Publish repsonse from the server.
        /// </summary>
        public void Update(long ticks)
        {                    
            lock (m_lock)
			{
                // check if it is time for the next update.
                if (m_nextUpdate > ticks)
                {
                    return;
                }

                // Utils.Trace(
                //    "Checking For Updates: Group={0}, Rate={1}, Delta={2}", 
                //    m_clientHandle,
                //    m_updateRate,
                //    (ticks - m_nextUpdate)/TimeSpan.TicksPerMillisecond);

                // queue a data channge.
                QueueDataChange(false);

                // schedule the next update.
                m_nextUpdate = DateTime.UtcNow.Ticks + m_updateRate*TimeSpan.TicksPerMillisecond;
            }
        }

        /// <summary>
        /// Queues a datachange.
        /// </summary>
        private void QueueDataChange(bool sendAll)
        {
            // dequeue values from monitored item caches.
            List<CallbackValue> changes = new List<CallbackValue>();
    
            foreach (Item item in this.m_items.Values)
            {
                IList<DataValue> values = item.MonitoredItem.DequeueValues();

                // send the cached value if an update must be sent.
                if (sendAll && values.Count == 0)
                {
                    values = new DataValue[1];

                    // look of value in the publishing cache.
                    DataValue dataValue = ReadCachedValue(item, Int32.MaxValue);

                    // create a dummy value if nothing in the cache.
                    if (dataValue == null)
                    {
                        dataValue = new DataValue();
                        dataValue.ServerTimestamp = DateTime.UtcNow;
                        dataValue.StatusCode = StatusCodes.BadWaitingForInitialData;
                    }

                    values[0] = dataValue;
                }
                
                //Utils.Trace(
                //    "Values Found: Group={0}, Changes={1}",
                //    m_clientHandle,
                //    values.Count);

                for (int ii = 0; ii < values.Count; ii++)
                {
                    // apply any type conversions.
                    object value;
                    short quality;
                    DateTime timestamp;
                    
                    int error = ProcessReadResult(
                        item.ServerHandle, 
                        item.ReqType, 
                        values[ii],
                        out value, 
                        out quality,
                        out timestamp);

                    item.LastValueSent = value;
                    item.LastQualitySent = quality;
                    item.LastTimestampSent = timestamp;

                    // update value in cache.
                    UpdateCachedValue(item, values[ii]);

                    // create callback value for active items
                    if (item.Active && m_active && m_enabled)
                    {
                        CallbackValue change = new CallbackValue();

                        change.ServerHandle = item.ServerHandle;
                        change.ClientHandle = item.ClientHandle;
                        change.Value        = value;
                        change.Quality      = quality;
                        change.Timestamp    = timestamp;
                        change.Error        = error;

                        changes.Add(change);
                    }
                }
            }

            // get callback object - do nothing if missing.
            IOPCDataCallback callback = (IOPCDataCallback)GetCallback(typeof(IOPCDataCallback).GUID);

            if (callback == null)
            {
                return;
            }
            
            // check if there are changes to send.
            if (changes.Count > 0)
            {                   
                CallbackRequest request = new CallbackRequest();

                request.CallbackType = CallbackType.DataChange;
                request.Callback = callback;
                request.GroupHandle = m_clientHandle;
                request.ServerHandle = m_serverHandle;
                request.Values = changes;

                //Utils.Trace(
                //    "Callback Queued: Group={0}, Changes={1}, Value[0]={2}",
                //    m_clientHandle,
                //    changes.Count,
                //    changes[0].Value);
                
                QueueCallbackRequest(request);
            }
        }

        #region CallbackType Enumeration
        /// <summary>
        /// The type of callback to send.
        /// </summary>
        private enum CallbackType
        {
            /// <summary>
            /// A data change callback.
            /// </summary>
            DataChange,

            /// <summary>
            /// A read operation complete callback.
            /// </summary>
            Read,

            /// <summary>
            /// A write operation complete callback.
            /// </summary>
            Write,

            /// <summary>
            /// A cancel operation complete callback.
            /// </summary>
            Cancel
        }
        #endregion
        
        #region CallbackValue Class
        /// <summary>
        /// A value to send in the callback.
        /// </summary>
        private class CallbackValue
        {
            /// <summary>
            /// The handle assigned by the server to the associated item.
            /// </summary>
            public int ServerHandle;

            /// <summary>
            /// The handle assigned by the client to the associated item.
            /// </summary>
            public int ClientHandle;

            /// <summary>
            /// The value in a COM VARIANT compatible form.
            /// </summary>
            public object Value;

            /// <summary>
            /// The COM-DA quality code.
            /// </summary>
            public short Quality;

            /// <summary>
            /// The source timestamp.
            /// </summary>
            public DateTime Timestamp;

            /// <summary>
            /// The COM error code associated with the value or operation.
            /// </summary>
            public int Error;
        }
        #endregion
        
        #region CallbackValue Class
        /// <summary>
        /// A request to send a callback to the client.
        /// </summary>
        private class CallbackRequest
        {
            /// <summary>
            /// The type of callback,
            /// </summary>
            public CallbackType CallbackType;
         
            /// <summary>
            /// The callback interface to use.
            /// </summary>
            public IOPCDataCallback Callback;

            /// <summary>
            /// The handle assigned by the server to the group.
            /// </summary>
            public int ServerHandle;

            /// <summary>
            /// The handle assigned by the client to the group.
            /// </summary>
            public int GroupHandle;

            /// <summary>
            /// The transaction id assigned by the client to the operation.
            /// </summary>
            public int TransactionId;

            /// <summary>
            /// The set of values or error codes to send to the client.
            /// </summary>
            public IList<CallbackValue> Values;
        }
        #endregion

        /// <summary>
        /// Queues a callback request. Assigns a worker thread if one is not already assigned.
        /// </summary>
        private void QueueCallbackRequest(CallbackRequest callback)
        {
            lock (m_callbacks)
            {
                if (m_callbacks.Count == 0)
                {
                    ThreadPool.QueueUserWorkItem(OnCallback);
                }

                m_callbacks.Enqueue(callback);
            }     
        }

        /// <summary>
        /// Sends all callbacks in the queue and then exits.
        /// </summary>
        private void OnCallback(object state)
        {
            try
            {
                while (true)
                {
                    CallbackRequest callback = null;

                    lock (m_callbacks)
                    {
                        // exit the thread if nothing to send.
                        if (m_callbacks.Count == 0)
                        {
                            return;
                        }
                            
                        // dob't extract the callback yet - we don't want other threads starting until after this callback is sent.
                        callback = m_callbacks.Peek();
                    }
                    
                    // send the callback.
                    try
                    {
                        switch (callback.CallbackType)
                        {
                            case CallbackType.DataChange:
                            case CallbackType.Read:
                            {
                                SendReadCallback(callback);
                                break;
                            }

                            case CallbackType.Write:
                            {
                                SendWriteCallback(callback);
                                break;
                            }

                            case CallbackType.Cancel:
                            {
                                SendCancelCallback(callback);
                                break;
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Utils.Trace(e, "Unexpected error sending callback.");
                    }

                    lock (m_callbacks)
                    {
                        // remove the callback that was just sent.
                        m_callbacks.Dequeue();

                        // exit the thread if nothing to send.
                        if (m_callbacks.Count == 0)
                        {
                            return;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error processing callback queue.");
            }
        }
     
        /// <summary>
        /// Sends a read or datachange callback to the client.
        /// </summary>
        private void SendReadCallback(CallbackRequest callback)
        {      
            int count = callback.Values.Count;

            int masterError = ResultIds.S_OK;
            int masterQuality = ResultIds.S_OK;

            int[] clientHandles = new int[count];
            object[] values = new object[count];
            short[] qualities = new short[count];
            System.Runtime.InteropServices.ComTypes.FILETIME[] timestamps = new System.Runtime.InteropServices.ComTypes.FILETIME[count];
            int[] errors = new int[count];

            for (int ii = 0; ii < callback.Values.Count; ii++)
            {
                CallbackValue change = callback.Values[ii];
                
                clientHandles[ii] = change.ClientHandle;
                values[ii]        = change.Value;   
                qualities[ii]     = change.Quality;
                timestamps[ii]    = ComUtils.GetFILETIME(change.Timestamp);
                errors[ii]        = change.Error;

                // check error.
                if (change.Error < 0)
                {             
                    masterError = ResultIds.S_FALSE;
                    continue;
                }                           

                // set the master quality if any bad qualities are found.
                if ((change.Quality & Qualities.OPC_QUALITY_GOOD) != Qualities.OPC_QUALITY_GOOD)
                {
                    masterQuality = ResultIds.S_FALSE; 
                }
	        }
                 
            //Utils.Trace(
            //    "DataChange: GroupHandle={0}, Count={1}, ServerHandle={2}, Value={3}", 
            //   callback.ServerHandle,
            //    callback.Values.Count,
            //    callback.Values[0].ServerHandle,
            //    callback.Values[0].Value);

            if (callback.CallbackType == CallbackType.DataChange)
            {   
                callback.Callback.OnDataChange(
                    callback.TransactionId,
                    callback.GroupHandle,
                    masterQuality,
                    masterError,
                    count,
                    clientHandles,
                    values,
                    qualities,
                    timestamps,
                    errors);

                m_server.SetLastUpdate();
            }
            else
            {                
                callback.Callback.OnReadComplete(
                    callback.TransactionId,
                    callback.GroupHandle,
                    masterQuality,
                    masterError,
                    count,
                    clientHandles,
                    values,
                    qualities,
                    timestamps,
                    errors);
            }
        }
     
        /// <summary>
        /// Sends a write callback to the client.
        /// </summary>
        private void SendWriteCallback(CallbackRequest callback)
        {      
            int count = callback.Values.Count;

            int masterError = ResultIds.S_OK;

            int[] clientHandles = new int[count];
            int[] errors = new int[count];

            for (int ii = 0; ii < callback.Values.Count; ii++)
            {
                CallbackValue result = callback.Values[ii];
                
                clientHandles[ii] = result.ClientHandle;
                errors[ii] = result.Error;

                if (result.Error < 0)
                {             
                    masterError = ResultIds.S_FALSE;
                }
	        }
            
            // Utils.Trace(
            //    "WriteComplete: GroupHandle={0}, ServerHandle={1}, Error={2}", 
            //    callback.GroupHandle,
            //    callback.Values[0].ClientHandle,
            //    callback.Values[0].Error);

            callback.Callback.OnWriteComplete(
                callback.TransactionId,
                callback.GroupHandle,
                masterError,
                count,
                clientHandles,
                errors);
        }

        /// <summary>
        /// Sends a cancel callback to the client.
        /// </summary>
        private void SendCancelCallback(CallbackRequest callback)
        {                
            // Utils.Trace(
            //     "CancelComplete: GroupHandle={0}, TransactionId={1}", 
            //     callback.GroupHandle,
            //     callback.TransactionId);

            callback.Callback.OnCancelComplete(callback.TransactionId, callback.GroupHandle);
        }
		#endregion

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^//
		#region IOPCAsyncIO3 Members
        /// <summary>
        /// IOPCAsyncIO3::ReadMaxAge - Reads one or more values, qualities and timestamps for the items specified. 
        ///                            This is functionally similar to the OPCSyncIO::Read method except it is asynchronous 
        ///                            and no source is specified (DEVICE or CACHE).
        /// </summary>
		public void ReadMaxAge(
            int dwCount, 
            int[] phServer, 
            int[] pdwMaxAge, 
            int dwTransactionID, 
            out int pdwCancelID, 
            out System.IntPtr ppErrors)
		{
            pdwCancelID = 0;

			// validate arguments.
            if (dwCount == 0 || phServer == null || pdwMaxAge == null || dwCount != phServer.Length)
			{
				throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
			}
			                
            // get callback object - error if missing.
            IOPCDataCallback callback = (IOPCDataCallback)GetCallback(typeof(IOPCDataCallback).GUID);

            if (callback == null)
            {
				throw ComUtils.CreateComException(ResultIds.CONNECT_E_NOCONNECTION);
            }

			try
			{ 
                int[] errors = new int[dwCount];

                // build list of values to read.
                ReadValueIdCollection nodesToRead = new ReadValueIdCollection();
                
                // use the minimum max age for all items.
                int maxAge = Int32.MaxValue;
                    
		        lock (m_lock)
		        {
			        if (m_subscription == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

                    for (int ii = 0; ii < dwCount; ii++)
                    {
                        Item itemToRead = null;

                        if (!m_items.TryGetValue(phServer[ii], out itemToRead))
                        {
                            errors[ii] = ResultIds.E_INVALIDHANDLE;
                            continue;
                        }

                        ReadValueId nodeToRead = new ReadValueId();

                        nodeToRead.NodeId      = itemToRead.MonitoredItem.ResolvedNodeId;
                        nodeToRead.AttributeId = Attributes.Value;

                        // needed to correlate results to input.
                        nodeToRead.Handle = itemToRead;

                        nodesToRead.Add(nodeToRead);

                        // update max age.
                        if (maxAge > pdwMaxAge[ii])
                        {
                            maxAge = pdwMaxAge[ii];
                        }
                    }

                    // create transaction.       
                    if (nodesToRead.Count > 0)
                    {
                        pdwCancelID = Utils.IncrementIdentifier(ref m_nextHandle);
                        m_transactions[pdwCancelID] = new AsyncReadTransaction(dwTransactionID, nodesToRead, false);
                    }
				}
                    
                // read values from server.                    
                if (nodesToRead.Count > 0)
                {
                    m_session.BeginRead(
                        null,
                        maxAge,
                        TimestampsToReturn.Both,
                        nodesToRead,
                        new AsyncCallback(OnReadComplete),
                        pdwCancelID);
                }
                                    
				// marshal error codes.
				ppErrors = ComUtils.GetInt32s(errors);
			}
			catch (Exception e)
			{
                Utils.Trace(e, "Error reading items.");
				throw ComUtils.CreateComException(e);
			}
		}

        /// <summary>
        /// IOPCAsyncIO3::WriteVQT - Writes one or more values, qualities and timestamps for the items specified. 
        ///                          The results are returned via the client�s IOPCDataCallback connection established 
        ///                          through the server�s IConnectionPointContainer.
        /// </summary>
		public void WriteVQT(
            int dwCount, 
            int[] phServer, 
            OPCITEMVQT[] pItemVQT, 
            int dwTransactionID, 
            out int pdwCancelID, 
            out System.IntPtr ppErrors)
		{
            pdwCancelID = 0;

			// validate arguments.
			if (dwCount == 0 || phServer == null || pItemVQT == null || dwCount != phServer.Length || dwCount != pItemVQT.Length)
			{
				throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
			}
			
            // get callback object - nothing more to do if missing.
            IOPCDataCallback callback = (IOPCDataCallback)GetCallback(typeof(IOPCDataCallback).GUID);

            if (callback == null)
            {
		        throw ComUtils.CreateComException(ResultIds.CONNECT_E_NOCONNECTION);
            }
            
			try
			{ 
                int[] errors = new int[dwCount];
                
                // build list of values to write.
                WriteValueCollection valuesToWrite = new WriteValueCollection();
                
		        lock (m_lock)
		        {
				    if (m_subscription == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);
                    
                    CallbackValue[] conversionErrors = null;

                    for (int ii = 0; ii < dwCount; ii++)
                    {
                        Item itemToWrite = null;

                        if (!m_items.TryGetValue(phServer[ii], out itemToWrite))
                        {
                            errors[ii] = ResultIds.E_INVALIDHANDLE;
                            continue;
                        }

                        VariableNode variable = itemToWrite.Variable;

                        WriteValue valueToWrite = new WriteValue();

                        valueToWrite.NodeId      = variable.NodeId;
                        valueToWrite.IndexRange  = null;
                        valueToWrite.AttributeId = Attributes.Value;

                        DataValue value = new DataValue();
                        
                        int error = 0;
                        value.Value = m_server.VariantValueToValue(variable, pItemVQT[ii].vDataValue, out error);

                        if (error != ResultIds.S_OK)
                        {
                            // only allocate this array when it is needed.
                            if (conversionErrors == null)
                            {
                                conversionErrors = new CallbackValue[dwCount];
                            }

                            // create the callback item.
                            CallbackValue conversionError = new CallbackValue();

                            conversionError.ClientHandle = itemToWrite.ClientHandle;
                            conversionError.Error = error;

                            conversionErrors[ii] = conversionError;

                            errors[ii] = error;
                            continue;
                        }

                        valueToWrite.Value = value;
                        
                        if (pItemVQT[ii].bQualitySpecified != 0)
                        {
                            value.StatusCode = ComUtils.GetQualityCode(pItemVQT[ii].wQuality);
                        }
                        
                        if (pItemVQT[ii].bTimeStampSpecified != 0)
                        {
                            value.SourceTimestamp = ComUtils.GetDateTime(pItemVQT[ii].ftTimeStamp);
                        }

                        // needed to correlate results to input.
                        valueToWrite.Handle = itemToWrite;
                        
                        valuesToWrite.Add(valueToWrite);
                    }
                    
                    // create transaction.
                    if (valuesToWrite.Count > 0 || conversionErrors != null)
                    {
                        pdwCancelID = Utils.IncrementIdentifier(ref m_nextHandle);
                        m_transactions[pdwCancelID] = new AsyncWriteTransaction(dwTransactionID, valuesToWrite);
                    }
                    
                    // send conversion errors in the callback if no valid items available (CTT bug workaround).
                    if (valuesToWrite.Count == 0 && conversionErrors != null)
                    {   
                        // must return S_OK from this function if sending the errors in the callback.
                        List<CallbackValue> errorsToSend = new List<CallbackValue>();
             
                        for (int ii = 0; ii < conversionErrors.Length; ii++)
                        {
                            if (conversionErrors[ii] != null)
                            {
                                errors[ii] = ResultIds.S_OK;
                                errorsToSend.Add(conversionErrors[ii]);
                            }
                        }

                        // queue the request.
                        CallbackRequest request = new CallbackRequest();

                        request.CallbackType = CallbackType.Write;
                        request.Callback = callback;
                        request.TransactionId = dwTransactionID;
                        request.GroupHandle = m_clientHandle;
                        request.ServerHandle = m_serverHandle;
                        request.Values = errorsToSend;

                        QueueCallbackRequest(request);
                    }
				}
                   
                // write values from server.                    
                if (valuesToWrite.Count > 0)
                {
                    m_session.BeginWrite(
                        null,
                        valuesToWrite,
                        new AsyncCallback(OnWriteComplete),
                        pdwCancelID);
                }
                
                // marshal error codes.
                ppErrors = ComUtils.GetInt32s(errors); 
			}
			catch (Exception e)
			{
                Utils.Trace(e, "Error writing items.");
				throw ComUtils.CreateComException(e);
			}
		}

        /// <summary>
        /// IOPCAsyncIO3::RefreshMaxAge - Force a callback to IOPCDataCallback::OnDataChange for all active items in the group 
        ///                               (whether they have changed or not). Inactive items are not included in the callback.
        /// </summary>
		public void RefreshMaxAge(
            int dwMaxAge, 
            int dwTransactionID, 
            out int pdwCancelID)
		{
            pdwCancelID = 0;
				
            // get callback object - error if missing.
            IOPCDataCallback callback = (IOPCDataCallback)GetCallback(typeof(IOPCDataCallback).GUID);

            if (callback == null)
            {
				throw ComUtils.CreateComException(ResultIds.CONNECT_E_NOCONNECTION);
            }

			try
			{			   
                // build list of values to read.
                ReadValueIdCollection nodesToRead = new ReadValueIdCollection();

			    lock (m_lock)
			    {
				    if (m_subscription == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

                    foreach (Item itemToRead in m_items.Values)
                    {
                        // check if active.
                        if (!m_active || !itemToRead.Active)
                        {
                            continue;
                        }

                        ReadValueId nodeToRead = new ReadValueId();

                        nodeToRead.NodeId      = itemToRead.MonitoredItem.ResolvedNodeId;
                        nodeToRead.AttributeId = Attributes.Value;

                        // needed to correlate results to input.
                        nodeToRead.Handle = itemToRead;
                        
                        nodesToRead.Add(nodeToRead);
                    }
                     
                    // check if nothing to read.
                    if (nodesToRead.Count == 0)
                    {
					    throw ComUtils.CreateComException(ResultIds.E_FAIL);
                    }

                    // read values from server.   
                    pdwCancelID = Utils.IncrementIdentifier(ref m_nextHandle);
                    m_transactions[pdwCancelID] = new AsyncReadTransaction(dwTransactionID, nodesToRead, true);
                }

                m_session.BeginRead(
                    null,
                    dwMaxAge,
                    TimestampsToReturn.Both,
                    nodesToRead,
                    new AsyncCallback(OnReadComplete),
                    pdwCancelID);
			}
			catch (Exception e)
			{
                Utils.Trace(e, "Error refreshing group.");
				throw ComUtils.CreateComException(e);
			}
		}
		#endregion

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^//
		#region IOPCGroupStateMgt Members
        /// <summary>
        /// IOPCGroupStateMgt::GetState - Gets the current state of the group.
        /// </summary>
		public void GetState(
            out int pUpdateRate, 
            out int pActive, 
            out string ppName, 
            out int pTimeBias, 
            out float pPercentDeadband, 
            out int pLCID, 
            out int phClientGroup, 
            out int phServerGroup)
		{
			lock (m_lock)
			{
				if (m_subscription == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);
                
                // get the state info.
				try
				{
                    pUpdateRate = m_updateRate;
                    pActive = (m_active)?1:0;
                    ppName = m_name;
                    pTimeBias = m_timebias;
                    pPercentDeadband = m_deadband;
                    pLCID = m_lcid;
                    phClientGroup = m_clientHandle;
                    phServerGroup = m_serverHandle;
				}
				catch (Exception e)
				{
                    Utils.Trace(e, "Error reading group state.");
					throw ComUtils.CreateComException(e);
				}
			}
		}

        /// <summary>
        /// IOPCGroupStateMgt::CloneGroup - Creates a second copy of a group with a unique name.
        /// </summary>
		public void CloneGroup(
            string     szName, 
            ref Guid   riid, 
            out object ppUnk)
		{
			lock (m_lock)
			{
				if (m_subscription == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

				Group group = null;

				try
				{
					// create new group.
                    int serverHandle = 0;
                    int revisedUpdateRate = 0;

					group = m_server.CreateGroup(
                        szName,
                        m_clientHandle,
                        m_updateRate,
                        false,
                        m_deadband,
                        m_lcid, 
                        m_timebias,
                        out serverHandle,
                        out revisedUpdateRate);

					// copy items.
					group.CloneItems(m_items.Values);

					// return new group
					ppUnk = group;
				}
				catch (Exception e)
				{
					// remove new group on error.
					if (group != null)
					{
						try   { m_server.RemoveGroup((int)group.ServerHandle, 0); }
						catch {}
					}
                    
                    Utils.Trace(e, "Error cloning group.");
					throw ComUtils.CreateComException(e);
				}
			}
		}
        
		/// <summary>
		/// Adds the items to group.
		/// </summary>
		private void CloneItems(IEnumerable<Item> itemsToClone)
		{
			lock (m_lock)
			{
				if (m_subscription == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);
  
                List<Item> itemsToAdd = new List<Item>();

                foreach (Item itemToClone in itemsToClone)
                {             
                    // create an item.
                    Item item = new Item();

                    item.ItemId                   = itemToClone.ItemId;
                    item.ClientHandle             = itemToClone.ClientHandle;
                    item.ServerHandle             = 0;
                    item.Active                   = itemToClone.Active;
                    item.ReqType                  = itemToClone.ReqType;
                    item.Variable                 = itemToClone.Variable;
                    item.SamplingRate             = itemToClone.SamplingRate;
                    item.SamplingRateSpecified    = itemToClone.SamplingRateSpecified;
                    item.DeadbandSupported        = itemToClone.DeadbandSupported;
                    item.Deadband                 = itemToClone.Deadband;
                    item.DeadbandSpecified        = itemToClone.DeadbandSpecified;
                    item.EnableBuffering          = itemToClone.EnableBuffering;

                    // create a monitored item.
                    MonitoredItem monitoredItem = new MonitoredItem();

                    monitoredItem.StartNodeId      = item.Variable.NodeId;
                    monitoredItem.AttributeId      = Attributes.Value;
                    monitoredItem.MonitoringMode   = (item.Active)?MonitoringMode.Reporting:MonitoringMode.Disabled;
                    monitoredItem.SamplingInterval = (item.SamplingRateSpecified)?item.SamplingRate:m_updateRate;
                    monitoredItem.QueueSize        = 0;
                    monitoredItem.DiscardOldest    = true;
                    monitoredItem.Encoding         = null;
                    monitoredItem.Filter           = null;
                    monitoredItem.IndexRange       = null;
                    
                    if (item.DeadbandSupported)
                    {
                        float deadband = (item.DeadbandSpecified)?item.Deadband:m_deadband;

                        DataChangeFilter filter = null;
                        
                        if (deadband > 0)
                        {
                            filter = new DataChangeFilter();
                            
                            filter.DeadbandType  = (uint)(int)DeadbandType.Percent;
                            filter.DeadbandValue = m_deadband;
                            filter.Trigger       = DataChangeTrigger.StatusValue;
                        }

                        monitoredItem.Filter = filter;
                    }

                    item.MonitoredItem = monitoredItem;
                    itemsToAdd.Add(item); 

                    // update the subscription.
                    m_subscription.AddItem(monitoredItem);
                }
                
                if (itemsToAdd.Count > 0)
                {                    
                    // create monitored items on the UA server.
                    m_subscription.ApplyChanges();

                    foreach (Item item in itemsToAdd)
                    {
                        // check for error during add.
                        int index = item.ServerHandle;
                        MonitoredItem monitoredItem = item.MonitoredItem;

                        if (ServiceResult.IsBad(monitoredItem.Status.Error))
                        {
                            m_subscription.RemoveItem(monitoredItem);
                            continue;
                        }
                        
                        // save server handle.
                        item.ServerHandle = Utils.ToInt32(monitoredItem.ClientHandle);

                        // add an entry in the cache.
                        CreateCacheEntry(item);

                        // index item.
                        m_items[item.ServerHandle] = item;
                    }
                }          
			}
		}

        /// <summary>
        /// IOPCGroupStateMgt::SetName - Changes the name of a private group. The name must be unique.
        /// </summary>
		public void SetName(string szName)
		{
			lock (m_lock)
			{
				if (m_subscription == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

                // validate argument.
                if (szName == null)
                {
                    throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
                }

				try
				{
					// check the name.
                    int error = m_server.SetGroupName(this.m_name, szName);
                    
                    if (error < 0)
                    {
					    throw ComUtils.CreateComException(error);
                    }

                    // update name if successful.
                    this.m_name = szName;
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}

        /// <summary>
        /// IOPCGroupStateMgt::SetState - Sets various properties of the group.
        /// </summary>
		public void SetState(
            System.IntPtr pRequestedUpdateRate, 
            out int       pRevisedUpdateRate, 
            System.IntPtr pActive, 
            System.IntPtr pTimeBias, 
            System.IntPtr pPercentDeadband, 
            System.IntPtr pLCID, 
            System.IntPtr phClientGroup)
		{
            int updateRate = 0;
            uint keepAliveCount = 0;
            float deadband = 0;
            int lcid = 0;
            int timebias = 0;
            bool updateRateChanged;

			try
			{                    
			    lock (m_lock)
			    {
				    if (m_subscription == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

					// validate update rate.
					if (pRequestedUpdateRate != IntPtr.Zero)
					{
						updateRate = Marshal.ReadInt32(pRequestedUpdateRate);

                        // throttle the publishing rate.
                        if (updateRate < 100)
                        {
                            updateRate = 100;
                        }

                        // calculate the new keep alive count based on the previous setting.
                        keepAliveCount = m_defaultKeepAliveCount;

                        if (m_keepAliveTime != 0)
                        {
                            keepAliveCount = (uint)((m_keepAliveTime/(uint)updateRate)+1);
                        }

                        // check if it is changing.
                        updateRateChanged = updateRate != m_updateRate;
                    }

					// validate deadband.
					if (pPercentDeadband != IntPtr.Zero)
					{
						float[] buffer = new float[1];
						Marshal.Copy(pPercentDeadband, buffer, 0, 1);
                        deadband = buffer[0];
                        
                        if (deadband < 0 || deadband > 100)
                        {
                            throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
                        }
                    }
                    
					// validate locale.
					if (pLCID != IntPtr.Zero)
					{
						lcid = Marshal.ReadInt32(pLCID);
                        
                        if (lcid != 0 && !m_server.IsLocaleSupported(lcid))
                        {
                            throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
                        }
					}

					// validate time bias.
					if (pTimeBias != IntPtr.Zero)
					{
						timebias = Marshal.ReadInt32(pTimeBias);
					}
                    
					// apply update rate.
					if (pRequestedUpdateRate != IntPtr.Zero)
					{
                        m_subscription.PublishingInterval = updateRate;
                        
                        // calculate the new keep alive count based on the previous setting.
                        if (keepAliveCount != 0)
                        {
                            m_subscription.KeepAliveCount = keepAliveCount;
                        }

                        // must update the individual items.
                        foreach (Item item in m_items.Values)
                        {
                            if (!item.SamplingRateSpecified)
                            {
                                item.MonitoredItem.SamplingInterval = updateRate;
                            }
                        }
					}

					// apply time bias.
					if (pTimeBias != IntPtr.Zero)
					{
						m_timebias = timebias;
					}

					// apply deadband.
					if (pPercentDeadband != IntPtr.Zero)
					{
						m_deadband = deadband;
                                                
                        DataChangeFilter filter = new DataChangeFilter();
                        
                        filter.DeadbandType  = (uint)(int)DeadbandType.Percent;
                        filter.DeadbandValue = m_deadband;
                        filter.Trigger       = DataChangeTrigger.StatusValue;

                        // must update the individual items.
                        foreach (Item item in m_items.Values)
                        {
                            if (item.DeadbandSupported)
                            {
                                if (m_deadband > 0)
                                {
                                    item.MonitoredItem.Filter = filter;
                                }
                            }
                        }
					}

					// apply locale.
					if (pLCID != IntPtr.Zero)
					{
						m_lcid = lcid;
					}

					// apply client handle.
					if (phClientGroup != IntPtr.Zero)
					{
						m_clientHandle = Marshal.ReadInt32(phClientGroup);
					}

                    // modify subscription.
                    m_subscription.Modify();

                    // apply changes to items.
                    m_subscription.ApplyChanges();
                    
                    // update keep alive time if it changed.
                    if (keepAliveCount != 0)
                    {
                        m_keepAliveTime = (int)(m_subscription.CurrentPublishingInterval*m_subscription.CurrentKeepAliveCount);
                    }
                    
                    // return the actual update rate.
					pRevisedUpdateRate = m_updateRate = (int)m_subscription.CurrentPublishingInterval;
            
                    // reset the update counter.
                    m_nextUpdate = DateTime.UtcNow.Ticks + m_updateRate*TimeSpan.TicksPerMillisecond;
                                        
                    // Utils.Trace(
                    //     "SetState: GroupHandle={0}, UpdateRate={1}, Active={2}", 
                    //     m_clientHandle,
                    //     m_updateRate,
                    //     m_active);

			    }		
                                        
			    // apply active.
			    if (pActive != IntPtr.Zero)
			    {                        
                    bool active = Marshal.ReadInt32(pActive) != 0;
                    PublishStateChanged(active, null);
			    }
			}
			catch (Exception e)
			{
                Utils.Trace(e, "Error setting group state.");
				throw ComUtils.CreateComException(e);
			}
		}
		#endregion

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^//
        #region IOPCGroupStateMgt2 Members
        /// <summary>
        /// IOPCGroupStateMgt2::GetKeepAlive - Returns the currently active keep-alive time for the subscription.
        /// </summary>
		public void GetKeepAlive(out int pdwKeepAliveTime)
		{
			lock (m_lock)
			{
				try
				{
					pdwKeepAliveTime = m_keepAliveTime;
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}

        /// <summary>
        /// IOPCGroupStateMgt2::SetKeepAlive - Sets the keep-alive time for a subscription to cause the server to provide 
        ///                                    client callbacks on the subscription when there are no new events to report.
        /// </summary>
		public void SetKeepAlive(
            int dwKeepAliveTime, 
            out int pdwRevisedKeepAliveTime)
		{
			lock (m_lock)
			{
				if (m_subscription == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

				try
				{
                    // re-calculate keepalive based on update rate.
                    uint keepAliveCount = (uint)((dwKeepAliveTime/(uint)m_updateRate)+1);

                    // update subscription.
                    m_subscription.KeepAliveCount = keepAliveCount;                   
                    m_subscription.Modify();

                    // return the actual keep alive rate.
                    pdwRevisedKeepAliveTime = m_keepAliveTime = (int)(m_subscription.CurrentPublishingInterval*m_subscription.CurrentKeepAliveCount);
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}
		#endregion

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^//
		#region IOPCItemDeadbandMgt Members
        /// <summary>
        /// IOPCItemDeadbandMgt::SetItemDeadband - Overrides the deadband specified for the group for each requested item.
        /// </summary>
		public void SetItemDeadband(
            int dwCount, 
            int[] phServer, 
            float[] pPercentDeadband, 
            out System.IntPtr ppErrors)
		{
			lock (m_lock)
			{
                if (m_subscription == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

                // validate arguments.
                if (dwCount == 0 || phServer == null || pPercentDeadband == null || dwCount != phServer.Length || dwCount != pPercentDeadband.Length)
                {
                    throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
                }

                try
                {
                    int[] errors = new int[dwCount];
                    Item[] itemsToModify = new Item[dwCount];

                    // update items.
                    for (int ii = 0; ii < dwCount; ii++)
                    {
                        // find the item.
                        Item itemToModify = null;

                        if (!m_items.TryGetValue(phServer[ii], out itemToModify))
                        {
                            errors[ii] = ResultIds.E_INVALIDHANDLE;
                            continue;
                        }

                        // check if deadbands are supported.
                        if (!itemToModify.DeadbandSupported)
                        {
                            errors[ii] = ResultIds.E_DEADBANDNOTSUPPORTED;
                            continue;
                        }

                        // validate the deadband.
                        float deadband = pPercentDeadband[ii];

                        if (deadband < 0 || deadband > 100)
                        {
                            errors[ii] = ResultIds.E_INVALIDARG;
                            continue;
                        }
                        
                        // change filter.
                        SetDataChangeFilter(itemToModify.MonitoredItem, deadband);
                        
                        itemsToModify[ii] = itemToModify;
                        errors[ii] = ResultIds.S_OK;
                    }

                    // update the subscription.
                    m_subscription.ApplyChanges();

                    // check for any errors.
                    for (int ii = 0; ii < dwCount; ii++)
                    {
                        Item itemToModify = itemsToModify[ii];

                        if (itemToModify == null)
                        {
                            continue;
                        }
                        
                        // note that the old deadband may no longer work if an error occurred here.
                        if (ServiceResult.IsBad(itemToModify.MonitoredItem.Status.Error))
                        {
                            // restore deadband.
                            SetDataChangeFilter(itemToModify.MonitoredItem, (itemToModify.DeadbandSpecified)?itemToModify.Deadband:m_deadband);

                            errors[ii] = ResultIds.E_FAIL;
                            continue;
                        }
                        
                        itemToModify.Deadband = pPercentDeadband[ii];
                        itemToModify.DeadbandSpecified = true;
                    }

                    // marshal error codes.
                    ppErrors = ComUtils.GetInt32s(errors);
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}

        /// <summary>
        /// Sets the data change filter for the monitored item.
        /// </summary>
        private void SetDataChangeFilter(MonitoredItem item, float deadband)
        {
            // construct filter.
            DataChangeFilter filter = null;

            if (deadband > 0)
            {
                filter = new DataChangeFilter();
                
                filter.DeadbandType  = (uint)DeadbandType.Percent;
                filter.DeadbandValue = deadband;
                filter.Trigger       = DataChangeTrigger.StatusValue;
            }

            item.Filter = filter;
        }

        /// <summary>
        /// IOPCItemDeadbandMgt::GetItemDeadband - Gets the deadband values for each of the requested items.
        /// </summary>
		public void GetItemDeadband(
            int dwCount, 
            int[] phServer, 
            out System.IntPtr ppPercentDeadband, 
            out System.IntPtr ppErrors)
		{
			lock (m_lock)
			{
				if (m_subscription == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

				// validate arguments.
				if (dwCount == 0 || phServer == null || dwCount != phServer.Length)
				{
					throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
				}

				try
				{			
					// read information from cached item objects.
					float[] deadbands = new float[dwCount];
					int[] errors = new int[dwCount];
                    
                    // update items.
                    for (int ii = 0; ii < dwCount; ii++)
                    {
                        // find the item.
                        Item itemToModify = null;

                        if (!m_items.TryGetValue(phServer[ii], out itemToModify))
                        {
                            errors[ii] = ResultIds.E_INVALIDHANDLE;
                            continue;
                        }

                        // check if deadbands are supported.
                        if (!itemToModify.DeadbandSupported)
                        {
                            errors[ii] = ResultIds.E_DEADBANDNOTSUPPORTED;
                            continue;
                        }

                        // check if deadband was set
                        if (!itemToModify.DeadbandSpecified)
                        {
                            errors[ii] = ResultIds.E_DEADBANDNOTSET;
                            continue;
                        }

                        deadbands[ii] = itemToModify.Deadband;
                        errors[ii] = ResultIds.S_OK;
                    }

					// marshal deadbands.
					ppPercentDeadband = Marshal.AllocCoTaskMem(Marshal.SizeOf(typeof(float))*dwCount);
					Marshal.Copy(deadbands, 0, ppPercentDeadband, dwCount);
                    
					// marshal error codes.
					ppErrors = ComUtils.GetInt32s(errors);
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}

        /// <summary>
        /// IOPCItemDeadbandMgt::ClearItemDeadband - Clears the individual item deadband set for the item, 
        ///                                          sets thems back to the deadband value of the group.
        /// </summary>
		public void ClearItemDeadband(
            int dwCount, 
            int[] phServer, 
            out System.IntPtr ppErrors)
		{
			lock (m_lock)
			{
				if (m_subscription == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

				// validate arguments.
				if (dwCount == 0 || phServer == null || dwCount != phServer.Length)
				{
					throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
				}

				try
				{
                    int[] errors = new int[dwCount];
                    Item[] itemsToModify = new Item[dwCount];

                    // update items.
                    for (int ii = 0; ii < dwCount; ii++)
                    {
                        // find the item.
                        Item itemToModify = null;

                        if (!m_items.TryGetValue(phServer[ii], out itemToModify))
                        {
                            errors[ii] = ResultIds.E_INVALIDHANDLE;
                            continue;
                        }

                        // check if deadbands are supported.
                        if (!itemToModify.DeadbandSupported)
                        {
                            errors[ii] = ResultIds.E_DEADBANDNOTSUPPORTED;
                            continue;
                        }

                        // check if deadband was set
                        if (!itemToModify.DeadbandSpecified)
                        {
                            errors[ii] = ResultIds.E_DEADBANDNOTSET;
                            continue;
                        }

                        // change filter.
                        SetDataChangeFilter(itemToModify.MonitoredItem, m_deadband);
                        
                        itemsToModify[ii] = itemToModify;
                        errors[ii] = ResultIds.S_OK;
                    }

                    // update the subscription.
                    m_subscription.ApplyChanges();

                    // check for any errors.
                    for (int ii = 0; ii < dwCount; ii++)
                    {
                        Item itemToModify = itemsToModify[ii];

                        if (itemToModify == null)
                        {
                            continue;
                        }
                        
                        // note that the old deadband may no longer work if an error occurred here.
                        if (ServiceResult.IsBad(itemToModify.MonitoredItem.Status.Error))
                        {
                            // restore filter.
                            SetDataChangeFilter(itemToModify.MonitoredItem, itemToModify.Deadband);

                            errors[ii] = ResultIds.E_FAIL;
                            continue;
                        }
                        
                        itemToModify.Deadband = 0;
                        itemToModify.DeadbandSpecified = false;
                    }

                    // marshal error codes.
                    ppErrors = ComUtils.GetInt32s(errors);
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		} 
		#endregion

        ///////////////////////////////////////////////////////////////////////////////////////////////////////////////
        //^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^^//
        #region IOPCItemSamplingMgt Members

        /// <summary>
        /// IOPCItemSamplingMgt::SetItemSamplingRate - Sets sampling rate on individual items.
        /// </summary>
		public void SetItemSamplingRate(
            int dwCount, 
            int[] phServer, 
            int[] pdwRequestedSamplingRate, 
            out System.IntPtr ppdwRevisedSamplingRate, 
            out System.IntPtr ppErrors)
		{
			lock (m_lock)
			{
                if (m_subscription == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

                // validate arguments.
                if (dwCount == 0 || phServer == null || pdwRequestedSamplingRate == null || dwCount != phServer.Length || dwCount != pdwRequestedSamplingRate.Length)
                {
                    throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
                }

                try
                {
                    int[] errors = new int[dwCount];
                    int[] revisedSamplingRates = new int[dwCount];
                    Item[] itemsToModify = new Item[dwCount];

                    // update items.
                    for (int ii = 0; ii < dwCount; ii++)
                    {
                        // find the item.
                        Item itemToModify = null;

                        if (!m_items.TryGetValue(phServer[ii], out itemToModify))
                        {
                            errors[ii] = ResultIds.E_INVALIDHANDLE;
                            continue;
                        }

                        // update sampling rate.
                        ChangeSamplingInterval(itemToModify.MonitoredItem, pdwRequestedSamplingRate[ii], itemToModify.EnableBuffering);

                        itemsToModify[ii] = itemToModify;
                        errors[ii] = ResultIds.S_OK;
                    }

                    // update the subscription.
                    m_subscription.ApplyChanges();

                    // check for any errors.
                    for (int ii = 0; ii < dwCount; ii++)
                    {
                        Item itemToModify = itemsToModify[ii];

                        if (itemToModify == null)
                        {
                            continue;
                        }
                        
                        // note that the old sampling rate may no longer work if an error occurred here.
                        if (ServiceResult.IsBad(itemToModify.MonitoredItem.Status.Error))
                        {
                            // restore sampling interval.
                            ChangeSamplingInterval(itemToModify.MonitoredItem, itemToModify.SamplingRate, itemToModify.EnableBuffering);

                            errors[ii] = ResultIds.E_FAIL;
                            continue;
                        }
                        
                        // update item on success.
                        itemToModify.SamplingRate = revisedSamplingRates[ii] = (int)itemToModify.MonitoredItem.Status.SamplingInterval;
                        itemToModify.SamplingRateSpecified = true;
                    }

                    // marshal error codes.
                    ppdwRevisedSamplingRate = ComUtils.GetInt32s(revisedSamplingRates);
                    ppErrors = ComUtils.GetInt32s(errors);
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}
                
        /// <summary>
        /// Updates the sampling interval and queue size.
        /// </summary>
        private void ChangeSamplingInterval(MonitoredItem monitoredItem, int samplingInterval, bool bufferEnabled)
        {
            // update sampling interval.
            if (samplingInterval < 0)
            {
                monitoredItem.SamplingInterval = m_updateRate;
            }
            else
            {
                monitoredItem.SamplingInterval = samplingInterval;
            }

            // update the queue size if buffering is enabled.
            if (bufferEnabled)
            {
                if (monitoredItem.SamplingInterval > 0)
                {
                    monitoredItem.QueueSize = (uint)(m_updateRate/monitoredItem.SamplingInterval);
                }
                else
                {
                    monitoredItem.QueueSize = 100;
                }
            }
            else
            {
                monitoredItem.QueueSize = 1;
            }
        }

        /// <summary>
        /// IOPCItemSamplingMgt:: GetItemSamplingRate - Gets the sampling rate on individual items, 
        ///                       which was previously set with SetItemSamplingRate.
        /// </summary>
		public void GetItemSamplingRate(
            int dwCount, 
            int[] phServer, 
            out System.IntPtr ppdwSamplingRate, 
            out System.IntPtr ppErrors)
		{
			lock (m_lock)
			{
				if (m_subscription == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

				// validate arguments.
				if (dwCount == 0 || phServer == null || dwCount != phServer.Length)
				{
					throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
				}

				try
				{	
					int[] samplingRates = new int[dwCount];
					int[] errors = new int[dwCount];
                    
                    // update items.
                    for (int ii = 0; ii < dwCount; ii++)
                    {
                        // find the item.
                        Item itemToModify = null;

                        if (!m_items.TryGetValue(phServer[ii], out itemToModify))
                        {
                            errors[ii] = ResultIds.E_INVALIDHANDLE;
                            continue;
                        }

                        // check if sampling rate was set
                        if (!itemToModify.SamplingRateSpecified)
                        {
                            errors[ii] = ResultIds.E_RATENOTSET;
                            continue;
                        }

                        samplingRates[ii] = itemToModify.SamplingRate;
                        errors[ii] = ResultIds.S_OK;
                    }

					// marshal results.
					ppdwSamplingRate = ComUtils.GetInt32s(samplingRates);
					ppErrors = ComUtils.GetInt32s(errors);
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}

        /// <summary>
        /// IOPCItemSamplingMgt:: ClearItemSamplingRate - Clears the sampling rate on individual items, 
        ///                       which was previously set with SetItemSamplingRate.
        /// </summary>
		public void ClearItemSamplingRate(
            int dwCount, 
            int[] phServer, 
            out System.IntPtr ppErrors)
		{
			lock (m_lock)
			{
                if (m_subscription == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

                // validate arguments.
                if (dwCount == 0 || phServer == null || dwCount != phServer.Length)
                {
                    throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
                }

                try
                {                   
                    int[] errors = new int[dwCount];
                    Item[] itemsToModify = new Item[dwCount];

                    // update items.
                    for (int ii = 0; ii < dwCount; ii++)
                    {
                        // find the item.
                        Item itemToModify = null;

                        if (!m_items.TryGetValue(phServer[ii], out itemToModify))
                        {
                            errors[ii] = ResultIds.E_INVALIDHANDLE;
                            continue;
                        }

                        // check if sampling rate was set
                        if (!itemToModify.SamplingRateSpecified)
                        {
                            errors[ii] = ResultIds.E_RATENOTSET;
                            continue;
                        }

                        // update sampling rate.
                        ChangeSamplingInterval(itemToModify.MonitoredItem, -1, itemToModify.EnableBuffering);
                        
                        itemsToModify[ii] = itemToModify;
                        errors[ii] = ResultIds.S_OK;
                    }

                    // update the subscription.
                    m_subscription.ApplyChanges();

                    // check for any errors.
                    for (int ii = 0; ii < dwCount; ii++)
                    {
                        Item itemToModify = itemsToModify[ii];

                        if (itemToModify == null)
                        {
                            continue;
                        }
                        
                        // note that the old sampling rate may no longer work if an error occurred here.
                        if (ServiceResult.IsBad(itemToModify.MonitoredItem.Status.Error))
                        {
                            // restore sampling rate.
                            ChangeSamplingInterval(itemToModify.MonitoredItem, itemToModify.SamplingRate, itemToModify.EnableBuffering);

                            errors[ii] = ResultIds.E_FAIL;
                            continue;
                        }
                        
                        // update item on success.
                        itemToModify.SamplingRate = -1;
                        itemToModify.SamplingRateSpecified = false;
                    }

                    // marshal error codes.
                    ppErrors = ComUtils.GetInt32s(errors);
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}

        /// <summary>
        /// IOPCItemSamplingMgt:: SetItemBufferEnable - Requests that the server turns on/off the buffering of data for requested items.
        /// </summary>
		public void SetItemBufferEnable(
            int dwCount, 
            int[] phServer, 
            int[] pbEnable, 
            out System.IntPtr ppErrors)
		{
			lock (m_lock)
			{
                if (m_subscription == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

                // validate arguments.
                if (dwCount == 0 || phServer == null || pbEnable == null || dwCount != phServer.Length || dwCount != pbEnable.Length)
                {
                    throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
                }

                try
                {                   
                   
                    int[] errors = new int[dwCount];
                    Item[] itemsToModify = new Item[dwCount];

                    // update items.
                    for (int ii = 0; ii < dwCount; ii++)
                    {
                        // find the item.
                        Item itemToModify = null;

                        if (!m_items.TryGetValue(phServer[ii], out itemToModify))
                        {
                            errors[ii] = ResultIds.E_INVALIDHANDLE;
                            continue;
                        }

                        // change queue size based on the sampling interval.
                        ChangeSamplingInterval(
                            itemToModify.MonitoredItem, 
                            (itemToModify.SamplingRateSpecified)?itemToModify.SamplingRate:-1, 
                            true);
                        
                        itemsToModify[ii] = itemToModify;
                        errors[ii] = ResultIds.S_OK;
                    }

                    // update the subscription.
                    m_subscription.ApplyChanges();

                    // check for any errors.
                    for (int ii = 0; ii < dwCount; ii++)
                    {
                        Item itemToModify = itemsToModify[ii];

                        if (itemToModify == null)
                        {
                            continue;
                        }
                        
                        // note that the old sampling rate may no longer work if an error occurred here.
                        if (ServiceResult.IsBad(itemToModify.MonitoredItem.Status.Error))
                        {
                            // restore queue size based on the sampling interval.
                            ChangeSamplingInterval(
                                itemToModify.MonitoredItem, 
                                (itemToModify.SamplingRateSpecified)?itemToModify.SamplingRate:-1, 
                                false);

                            errors[ii] = ResultIds.E_FAIL;
                            continue;
                        }
                        
                        // update item on success.
                        itemToModify.EnableBuffering = true;
                    }

                    // marshal error codes.
                    ppErrors = ComUtils.GetInt32s(errors);
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		}

        /// <summary>
        /// IOPCItemSamplingMgt:: GetItemBufferEnable - Queries the current state of the servers buffering for requested items.
        /// </summary>
		public void GetItemBufferEnable(
            int dwCount, 
            int[] phServer, 
            out System.IntPtr ppbEnable, 
            out System.IntPtr ppErrors)
		{
			lock (m_lock)
			{
				if (m_subscription == null) throw ComUtils.CreateComException(ResultIds.E_FAIL);

				// validate arguments.
				if (dwCount == 0 || phServer == null || dwCount != phServer.Length)
				{
					throw ComUtils.CreateComException(ResultIds.E_INVALIDARG);
				}

				try
				{			
					// read information from cached item objects.
					int[] enableBuffering = new int[dwCount];
					int[] errors = new int[dwCount];
                    
                    // update items.
                    for (int ii = 0; ii < dwCount; ii++)
                    {
                        // find the item.
                        Item itemToModify = null;

                        if (!m_items.TryGetValue(phServer[ii], out itemToModify))
                        {
                            errors[ii] = ResultIds.E_INVALIDHANDLE;
                            continue;
                        }

                        enableBuffering[ii] = (itemToModify.EnableBuffering)?1:0;
                        errors[ii] = ResultIds.S_OK;
                    }

					// marshal results.
					ppbEnable = ComUtils.GetInt32s(enableBuffering);
					ppErrors = ComUtils.GetInt32s(errors);
				}
				catch (Exception e)
				{
					throw ComUtils.CreateComException(e);
				}
			}
		} 
		#endregion
        
        #region Transaction Class
        /// <summary>
        /// Holds the state for an asynchronous operation.
        /// </summary>
        private class Transaction
        {
            public int TransactionId;
        }
        
        /// <summary>
        /// Holds the state for an asynchronous read operation.
        /// </summary>
        private class AsyncReadTransaction : Transaction
        {
            public ReadValueIdCollection NodesToRead;
            public bool IsRefresh;

            public AsyncReadTransaction(int transactionId, ReadValueIdCollection nodesToRead, bool isRefresh)
            {
                TransactionId = transactionId;
                NodesToRead = nodesToRead;
                IsRefresh = isRefresh;
            }
        }
        
        /// <summary>
        /// Holds the state for an asynchronous write operation.
        /// </summary>
        private class AsyncWriteTransaction : Transaction
        {
            public WriteValueCollection ValuesToWrite;

            public AsyncWriteTransaction(int transactionId, WriteValueCollection valuesToWrite)
            {
                TransactionId = transactionId;
                ValuesToWrite = valuesToWrite;
            }
        }
        #endregion
                
		#region Item Cache Functions
        /// <summary>
        /// Adds a cache entry for the item.
        /// </summary>
        private void CreateCacheEntry(Item item)
        {
            lock (m_cache)
            {
                CachedValue cachedValue = null;

                if (!m_cache.TryGetValue(item.Variable.NodeId, out cachedValue))
                {
                    m_cache[item.Variable.NodeId] = cachedValue = new CachedValue(item.Variable.NodeId);
                }

                cachedValue.AddRef();
                item.CachedValue = cachedValue;
            }   
        }

        /// <summary>
        /// Deletes the cache entry for the item.
        /// </summary>
        private void DeleteCacheEntry(Item item)
        {
            lock (m_cache)
            {
                if (item.CachedValue.Release() == 0)
                {
                    m_cache.Remove(item.CachedValue.NodeId);
                }
            }   
        }

        /// <summary>
        /// Reads a value from the cache.
        /// </summary>
        private DataValue ReadCachedValue(Item item, int maxAge)
        {
            lock (m_cache)
            {
                if (DateTime.UtcNow.AddMilliseconds(maxAge) >= item.CachedValue.Timestamp)
                {
                    return item.CachedValue.LastValue;
                }

                return null;
            }
        }

        /// <summary>
        /// Updates a value in the cache.
        /// </summary>
        private void UpdateCachedValue(Item item, DataValue value)
        {
            lock (m_cache)
            {
                if (value != null)
                {
                    item.CachedValue.Update(value);
                }
            }
        }
        #endregion

		#region Private Members
        private object m_lock = new object();
		private Server m_server = null;
        private Session m_session;
        private Subscription m_subscription;
		private int m_serverHandle = 0;
		private int m_clientHandle = 0;
		private int m_updateRate = 0;
		private float m_deadband = 0;
		private string m_name = null;
		private int m_timebias = 0;
		private int m_lcid = ComUtils.LOCALE_SYSTEM_DEFAULT;
        private int m_keepAliveTime = 0;
        private bool m_active;      
        private bool m_enabled;
        private bool m_advised;
        private long m_nextUpdate;
        private NodeIdDictionary<CachedValue> m_cache;
        private uint m_defaultKeepAliveCount = 10;
                
        private SortedDictionary<int,Item> m_items = new SortedDictionary<int, Item>(); 
        private Dictionary<int,Transaction> m_transactions = new Dictionary<int,Transaction>();  
        private Queue<CallbackRequest> m_callbacks = new Queue<CallbackRequest>();

        private int m_nextHandle = 1000;
        #endregion

        #region Private Member Functions
		#endregion
	}
    
    #region Item Class
	/// <summary>
	/// Describes how an item in the server address space should be accessed. 
	/// </summary>
	public class Item
    {
        #region Properties
        /// <summary>
		/// The identifier for the item.
		/// </summary>
		public string ItemId
		{
			get { return m_itemId;  }
			set { m_itemId = value; }
		}
        
        /// <summary>
		/// The variable which the item is connected to.
		/// </summary>
		public VariableNode Variable
		{
			get { return m_variable;  }
			set { m_variable = value; }
		}

		/// <summary>
		/// The server handle assigned to the item
		/// </summary>
		public int ServerHandle
		{
			get { return m_serverHandle;  }
			set { m_serverHandle = value; }
		}

		/// <summary>
		/// The client handle assigned to the item
		/// </summary>
		public int ClientHandle
		{
			get { return m_clientHandle;  }
			set { m_clientHandle = value; }
		}

		/// <summary>
		/// The data type to use when returning the item value.
		/// </summary>
		public VarEnum ReqType
		{
			get { return m_reqType;  }
			set { m_reqType = value; }
		}

		/// <summary>
		/// Whether the server should send data change updates. 
		/// </summary>
		public bool Active
		{
			get { return m_active;  }
			set { m_active = value; }
		}

		/// <summary>
		/// The minimum percentage change required to trigger a data update for an item. 
		/// </summary>
		public float Deadband
		{
			get { return m_deadband;  }
			set { m_deadband = value; }
		}

		/// <summary>
		/// Whether the Deadband is specified.
		/// </summary>
		public bool DeadbandSpecified
		{
			get { return m_deadbandSpecified;  }
			set { m_deadbandSpecified = value; }
		}

        /// <summary>
        /// Whether the Deadband is supported.
        /// </summary>
        public bool DeadbandSupported
        {
            get { return m_deadbandSupported; }
            set { m_deadbandSupported = value; }
        }

		/// <summary>
		/// How frequently the server should sample the item value.
		/// </summary>
		public int SamplingRate
		{
			get { return m_samplingRate;  }
			set { m_samplingRate = value; }
		}

		/// <summary>
		/// Whether the Sampling Rate is specified.
		/// </summary>
		public bool SamplingRateSpecified
		{
			get { return m_samplingRateSpecified;  }
			set { m_samplingRateSpecified = value; }
		}

		/// <summary>
		/// Whether the server should buffer multiple data changes between data updates.
		/// </summary>
		public bool EnableBuffering
		{
			get { return m_enableBuffering;  }
			set { m_enableBuffering = value; }
		}

        /// <summary>
        /// Monitored Item.
        /// </summary>
        public MonitoredItem MonitoredItem
        {
            get { return m_monitoredItem; }
            set { m_monitoredItem = value; }
        }

        /// <summary>
        /// The last value cached for the item.
        /// </summary>
        public CachedValue CachedValue
        {
            get { return m_cachedValue; }
            set { m_cachedValue = value; }
        }

        /// <summary>
        /// The last data change sent.
        /// </summary>
        public object LastValueSent
        {
            get { return m_lastValueSent; }
            set { m_lastValueSent = value; }
        }

        /// <summary>
        /// The last data quality sent.
        /// </summary>
        public short LastQualitySent
        {
            get { return m_lastQualitySent; }
            set { m_lastQualitySent = value; }
        }

        /// <summary>
        /// The last data timestamp sent.
        /// </summary>
        public DateTime LastTimestampSent
        {
            get { return m_lastTimestampSent; }
            set { m_lastTimestampSent = value; }
        }
        #endregion

		#region Constructors
		/// <summary>
		/// Initializes the object with default values.
		/// </summary>
		public Item() {}

		/// <summary>
		/// Initializes object with the specified ItemIdentifier object.
		/// </summary>
		public Item(string itemId) 
		{
            m_itemId = itemId; 
		}

		/// <summary>
		/// Initializes object with the specified Item object.
		/// </summary>
		public Item(Item item)
		{
			if (item != null)
			{
				ItemId                   = item.ItemId;
				ServerHandle             = item.ServerHandle;
				ClientHandle             = item.ClientHandle;
				ReqType                  = item.ReqType;
				Active                   = item.Active;
				Deadband                 = item.Deadband;
				DeadbandSpecified        = item.DeadbandSpecified;
				SamplingRate             = item.SamplingRate;
				SamplingRateSpecified    = item.SamplingRateSpecified;
				EnableBuffering          = item.EnableBuffering;
			}
		}
		#endregion
		
		#region Private Members
		private string m_itemId = null;
        private VariableNode m_variable = null;
		private int m_serverHandle = 0;
		private int m_clientHandle = 0;
		private VarEnum m_reqType = VarEnum.VT_EMPTY;
		private bool m_active = true;
		private float m_deadband = 0;
		private bool m_deadbandSpecified = false;
        private bool m_deadbandSupported = false;
		private int m_samplingRate = 0;
		private bool m_samplingRateSpecified = false;
		private bool m_enableBuffering = false;
        private MonitoredItem m_monitoredItem = null;
        private CachedValue m_cachedValue;
        private object m_lastValueSent;
        private short m_lastQualitySent;
        private DateTime m_lastTimestampSent;
		#endregion
	}
	#endregion
}
