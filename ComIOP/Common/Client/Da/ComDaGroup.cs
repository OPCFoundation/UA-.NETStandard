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
using System.Globalization;
using System.Security.Principal;
using System.Threading;
using System.Runtime.InteropServices;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Com;
using OpcRcw.Da;

namespace Opc.Ua.Com.Client
{
    /// <summary>
    /// A wrapper for COM-DA group.
    /// </summary>
    internal class ComDaGroup : ComObject
    {
        #region Constructor
        /// <summary>
        /// Creates an empty group.
        /// </summary>
        /// <param name="server">The server that the group belongs to.</param>
        /// <param name="callbacksRequired">if set to <c>true</c> if the group will received callbacks.</param>
        public ComDaGroup(ComDaClient server, bool callbacksRequired)
        {
            m_server = server;
            m_clientHandle = Utils.IncrementIdentifier(ref m_groupCounter);
            m_serverHandle = 0;
            m_items = new List<GroupItem>();

            if (callbacksRequired)
            {
                m_monitoredItems = new Dictionary<int, DataChangeInfo>();
            }

            // Utils.Trace("GROUP {0}", m_clientHandle);
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
                Utils.SilentDispose(m_callback);
                m_callback = null;
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Releases all references to the server.
        /// </summary>
        protected override void ReleaseServer()
        {
            Utils.SilentDispose(m_callback);
            m_callback = null;
            base.ReleaseServer();
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// Gets the group's client handle.
        /// </summary>
        /// <value>The client handle.</value>
        public int ClientHandle
        {
            get { return m_clientHandle; }
        }

        /// <summary>
        /// Gets the group's server handle.
        /// </summary>
        /// <value>The server handle.</value>
        public int ServerHandle
        {
            get { return m_serverHandle; }
        }

        /// <summary>
        /// The requested sampling interval for the group.
        /// </summary>
        public int SamplingInterval
        {
            get { return m_samplingInterval; }
        }

        /// <summary>
        /// The actual sampling interval for the group.
        /// </summary>
        public int ActualSamplingInterval
        {
            get { return m_actualSamplingInterval; }
        }

        /// <summary>
        /// The deadband applied to the group.
        /// </summary>
        public float Deadband
        {
            get { return m_deadband; }
        }
        #endregion

        /// <summary>
        /// Sets the monitored items associated with the item.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="monitoredItems">The monitored items.</param>
        public void SetMonitoredItems(GroupItem item, MonitoredItem[] monitoredItems)
        {
            // check if callbacks are enabled.
            if (item == null || m_monitoredItems == null)
            {
                return;
            }

            // save the monitored items.
            lock (m_monitoredItems)
            {
                DataChangeInfo info = null;

                if (!m_monitoredItems.TryGetValue(item.ClientHandle, out info))
                {
                    m_monitoredItems[item.ClientHandle] = info = new DataChangeInfo();
                }

                info.MonitoredItems = monitoredItems;

                // resend the last cached value.
                if (info.LastError != null || info.LastValue != null)
                {
                    for (int ii = 0; ii < monitoredItems.Length; ii++)
                    {
                        monitoredItems[ii].QueueValue(info.LastValue, info.LastError);
                    }
                }
            }
        }

        /// <summary>
        /// Stores the information used to report data changes.
        /// </summary>
        private class DataChangeInfo
        {
            public MonitoredItem[] MonitoredItems;
            public DataValue LastValue;
            public ServiceResult LastError;
        }

        /// <summary>
        /// Called when a data change event arrives.
        /// </summary>
        /// <param name="clientHandles">The client handles.</param>
        /// <param name="values">The values.</param>
        internal void OnDataChange(int[] clientHandles, DaValue[] values)
        {
            // check if callbacks are enabled.
            if (m_monitoredItems == null)
            {
                return;
            }

            // lookup client handle a report change directly to monitored item.
            lock (m_monitoredItems)
            {
                for (int ii = 0; ii < clientHandles.Length; ii++)
                {
                    DataChangeInfo info = null;

                    if (!m_monitoredItems.TryGetValue(clientHandles[ii], out info))
                    {
                        continue;
                    }

                    MonitoredItem[] monitoredItems = info.MonitoredItems;

                    // convert the value to a UA value.
                    info.LastValue = new DataValue();
                    info.LastError = ReadRequest.GetItemValue(values[ii], info.LastValue, DiagnosticsMasks.All);
                    info.LastValue.ServerTimestamp = DateTime.UtcNow;

                    // queue the values.
                    for (int jj = 0; jj < monitoredItems.Length; jj++)
                    {

                        if (info.LastValue.Value != null
                            && info.LastValue.Value.GetType().IsArray
                            && monitoredItems[jj].IndexRange.Count != info.LastValue.Value.GetType().GetArrayRank()
                            && StatusCode.IsBad(info.LastValue.StatusCode))
                        {
                            info.LastValue.StatusCode = StatusCodes.BadIndexRangeNoData;
                        }

                        monitoredItems[jj].QueueValue(info.LastValue, info.LastError);
                    }
                }
            }
        }

        /// <summary>
        /// Called when a asynchronous read completes.
        /// </summary>
        /// <param name="requestId">The request id.</param>
        /// <param name="clientHandles">The client handles.</param>
        /// <param name="values">The values.</param>
        internal void OnReadComplete(int requestId, int[] clientHandles, DaValue[] values)
        {
        }

        /// <summary>
        /// Called when a asynchronous write completes.
        /// </summary>
        /// <param name="requestId">The request id.</param>
        /// <param name="clientHandles">The client handles.</param>
        /// <param name="errors">The errors.</param>
        internal void OnWriteComplete(int requestId, int[] clientHandles, int[] errors)
        {
        }

        /// <summary>
        /// Creates the group on the server if not already created.
        /// </summary>
        public void Create()
        {
            // Utils.Trace("CREATE");

            if (Unknown != null)
            {
                return;
            }

            int serverHandle = 0;
            int actualSamplingInterval = 0;

            object unknown = m_server.CreateGroup(
                m_clientHandle,
                m_samplingInterval,
                m_deadband,
                out serverHandle,
                out actualSamplingInterval);

            Unknown = unknown;
            m_serverHandle = serverHandle;
            m_actualSamplingInterval = actualSamplingInterval;

            // Utils.Trace(
            //    "Group {0}/{1} Created({5}) {2}/{3}ms {4}%",
            //    m_clientHandle,
            //    m_serverHandle,
            //    m_samplingInterval,
            //    m_actualSamplingInterval,
            //    m_deadband,
            //    m_items.Count);

            // set up data change callback.
            if (m_monitoredItems != null)
            {
                try
                {
                    m_callback = new ComDaDataCallback(this);
                }
                catch (Exception e)
                {
                    Utils.Trace("Could not establish IOPCDataCallback.", e);
                }
            }
        }

        /// <summary>
        /// Deletes the group on the server if it has been created.
        /// </summary>
        public void Delete()
        {
            // Utils.Trace("DELETE");

            if (Unknown == null)
            {
                return;
            }

            // Utils.Trace(
            //    "Group {0}/{1} Deleted({5}) {2}/{3}ms {4}%",
            //    m_clientHandle,
            //    m_serverHandle,
            //    m_samplingInterval,
            //    m_actualSamplingInterval,
            //    m_deadband,
            //    m_items.Count);

            m_server.RemoveGroup(m_serverHandle);
            ReleaseServer();
        }

        /// <summary>
        /// Modifies the group.
        /// </summary>
        /// <returns>The error. S_OK on success.</returns>
        public int ModifyGroup()
        {
            m_actualSamplingInterval = 0;

            int localeId = m_server.LocaleId;

            GCHandle hSamplingInterval = GCHandle.Alloc(m_samplingInterval, GCHandleType.Pinned);
            GCHandle hDeadband = GCHandle.Alloc(m_deadband, GCHandleType.Pinned);

            string methodName = "IOPCGroupStateMgt.SetState";

            try
            {
                IOPCGroupStateMgt server = BeginComCall<IOPCGroupStateMgt>(methodName, true);

                server.SetState(
                    hSamplingInterval.AddrOfPinnedObject(),
                    out m_actualSamplingInterval,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    hDeadband.AddrOfPinnedObject(),
                    IntPtr.Zero,
                    IntPtr.Zero);

                /*
                Utils.Trace(
                    "Group {0} Modified({4}) {1}/{2}ms {3}%",
                    m_clientHandle,
                    m_samplingInterval,
                    m_actualSamplingInterval,
                    m_deadband,
                    m_items.Count);
                */

                return ResultIds.S_OK;
            }
            catch (Exception e)
            {
                ComUtils.TraceComError(e, methodName);
                return Marshal.GetHRForException(e);
            }
            finally
            {
                EndComCall(methodName);
                hSamplingInterval.Free();
                hDeadband.Free();
            }
        }

        /// <summary>
        /// Creates the item.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="samplingInterval">The sampling interval.</param>
        /// <param name="deadband">The deadband.</param>
        /// <param name="active">if set to <c>true</c> [active].</param>
        /// <returns>
        /// The item that was added to the group. Null if the item could not be added.
        /// </returns>
        public GroupItem CreateItem(string itemId, int samplingInterval, float deadband, bool active)
        {
            // set the group parameters if this is the first item.
            if (m_items.Count == 0)
            {
                m_samplingInterval = samplingInterval;
                m_deadband = deadband;
            }

            // check if the item can be added to the group.
            if (m_samplingInterval != samplingInterval || m_deadband != deadband)
            {
                return null;
            }

            // create the item.
            GroupItem item = new GroupItem();

            item.ItemId = itemId;
            item.ClientHandle = Utils.IncrementIdentifier(ref m_itemCounter);
            item.ServerHandle = 0;
            item.Active = active;
            item.ActiveChanged = false;
            item.Deleted = false;
            item.Created = false;
            item.ErrorId = 0;

            lock (Lock)
            {
                m_items.Add(item);
            }

            return item;
        }

        /// <summary>
        /// Modifies the item.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <param name="samplingInterval">The sampling interval.</param>
        /// <param name="deadband">The deadband.</param>
        /// <param name="active">if set to <c>true</c> [active].</param>
        /// <returns>True if the item is in the group.</returns>
        public bool ModifyItem(GroupItem item, int samplingInterval, float deadband, bool active)
        {
            // check if the item can be added to the group.
            if (m_samplingInterval != samplingInterval || m_deadband != deadband)
            {
                // check if the item needs to be removed from the group.
                if (m_items.Count > 1)
                {
                    item.Deleted = true;
                    return false;
                }

                // update active state.
                item.ActiveChanged = active != item.Active;
                item.Active = active;

                // update the group parameters.
                m_samplingInterval = samplingInterval;
                m_deadband = deadband;
                m_updateRequired = true;

                return true;
            }

            // undelete the item.
            item.Deleted = false;

            // update active state.
            item.ActiveChanged = active != item.Active;
            item.Active = active;

            // nothing to do - the group matches the item.
            return true;
        }

        /// <summary>
        /// Removes the item from the group.
        /// </summary>
        /// <param name="item">The item.</param>
        /// <returns>True if the item exists.</returns>
        public bool RemoveItem(GroupItem item)
        {
            // add the item if it does not exist.
            if (item == null)
            {
                return false;
            }

            // flag the item as deleted.
            item.Deleted = true;
            return true;
        }

        /// <summary>
        /// Adds all items to the group that have not already been added.
        /// </summary>
        public void AddItems()
        {
            // count the number of items to add.
            List<GroupItem> itemsToAdd = new List<GroupItem>();

            lock (Lock)
            {
                for (int ii = 0; ii < m_items.Count; ii++)
                {
                    if (!m_items[ii].Created)
                    {
                        itemsToAdd.Add(m_items[ii]);
                    }
                }
            }

            // check if nothing to do.
            if (itemsToAdd.Count == 0)
            {
                return;
            }

            // create item definitions.
            int count = itemsToAdd.Count;
            OpcRcw.Da.OPCITEMDEF[] definitions = new OpcRcw.Da.OPCITEMDEF[count];

            for (int ii = 0; ii < count; ii++)
            {
                definitions[ii] = new OpcRcw.Da.OPCITEMDEF();

                definitions[ii].szItemID = itemsToAdd[ii].ItemId;
                definitions[ii].bActive = (itemsToAdd[ii].Active) ? 1 : 0;
                definitions[ii].szAccessPath = String.Empty;
                definitions[ii].vtRequestedDataType = (short)VarEnum.VT_EMPTY;
                definitions[ii].hClient = itemsToAdd[ii].ClientHandle;
            }

            // initialize output parameters.
            IntPtr pResults = IntPtr.Zero;
            IntPtr pErrors = IntPtr.Zero;

            // add items to group.
            string methodName = "IOPCItemMgt.AddItems";

            try
            {
                IOPCItemMgt server = BeginComCall<IOPCItemMgt>(methodName, true);

                server.AddItems(
                    count,
                    definitions,
                    out pResults,
                    out pErrors);
            }
            catch (Exception e)
            {
                ComUtils.TraceComError(e, methodName);

                for (int ii = 0; ii < itemsToAdd.Count; ii++)
                {
                    itemsToAdd[ii].ErrorId = Marshal.GetHRForException(e);
                }

                return;
            }
            finally
            {
                EndComCall(methodName);
            }

            // unmarshal output parameters.
            int[] serverHandles = GetItemResults(ref pResults, count, true);
            int[] errors = ComUtils.GetInt32s(ref pErrors, count, true);

            // save handles and error codes.
            for (int ii = 0; ii < count; ii++)
            {
                GroupItem item = itemsToAdd[ii];

                item.ServerHandle = serverHandles[ii];
                item.ErrorId = errors[ii];

                if (item.ErrorId >= 0)
                {
                    itemsToAdd[ii].Created = true;
                }
            }

            /*
            Utils.Trace(
                "Group {0} AddItems({4}/{5}) {1}/{2}ms {3}%", 
                m_clientHandle, 
                m_samplingInterval,
                m_actualSamplingInterval,
                m_deadband, 
                itemsToAdd.Count,
                m_items.Count);
            */
        }

        /// <summary>
        /// Sets the active state for a set of items in a group.
        /// </summary>
        public void ActivateItems(bool active)
        {
            // count the number of items to activate.
            List<GroupItem> itemsToActivate = new List<GroupItem>();

            lock (Lock)
            {
                for (int ii = 0; ii < m_items.Count; ii++)
                {
                    if (m_items[ii].ActiveChanged && m_items[ii].Active == active && m_items[ii].Created)
                    {
                        itemsToActivate.Add(m_items[ii]);
                    }
                }
            }

            // check if nothing to do.
            if (itemsToActivate.Count == 0)
            {
                return;
            }

            // build list of items to remove.
            int count = itemsToActivate.Count;
            int[] serverHandles = new int[count];

            for (int ii = 0; ii < itemsToActivate.Count; ii++)
            {
                serverHandles[ii] = itemsToActivate[ii].ServerHandle;
            }

            // initialize output parameters.
            IntPtr pErrors = IntPtr.Zero;

            string methodName = "IOPCItemMgt.SetActiveState";

            try
            {
                IOPCItemMgt server = BeginComCall<IOPCItemMgt>(methodName, true);

                server.SetActiveState(
                    count,
                    serverHandles,
                    (active) ? 1 : 0,
                    out pErrors);
            }
            catch (Exception e)
            {
                ComUtils.TraceComError(e, methodName);

                for (int ii = 0; ii < itemsToActivate.Count; ii++)
                {
                    itemsToActivate[ii].ActiveChanged = false;
                    itemsToActivate[ii].ErrorId = Marshal.GetHRForException(e);
                }
            }
            finally
            {
                EndComCall(methodName);
            }

            // free returned error array.
            int[] errors = ComUtils.GetInt32s(ref pErrors, count, true);

            // save error codes.
            for (int ii = 0; ii < count; ii++)
            {
                itemsToActivate[ii].ActiveChanged = false;
                itemsToActivate[ii].ErrorId = errors[ii];
            }

            /*
            Utils.Trace(
                "Group {0} ActivateItems({4}/{5}) {1}/{2}ms {3}%",
                m_clientHandle,
                m_samplingInterval,
                m_actualSamplingInterval,
                m_deadband,
                active,
                itemsToActivate.Count);
            */
        }

        /// <summary>
        /// Removes the items from the group that have been marked as deleted.
        /// </summary>
        public void RemoveItems()
        {
            // count the number of items to remove.
            List<GroupItem> itemsToRemove = new List<GroupItem>();

            lock (Lock)
            {
                List<GroupItem> itemsToKeep = new List<GroupItem>();

                for (int ii = 0; ii < m_items.Count; ii++)
                {
                    if (m_items[ii].Deleted && m_items[ii].Created)
                    {
                        itemsToRemove.Add(m_items[ii]);
                        continue;
                    }

                    itemsToKeep.Add(m_items[ii]);
                }

                m_items = itemsToKeep;
            }

            // check if nothing to do.
            if (itemsToRemove.Count == 0)
            {
                return;
            }

            // build list of items to remove.
            int count = itemsToRemove.Count;
            int[] serverHandles = new int[count];

            for (int ii = 0; ii < itemsToRemove.Count; ii++)
            {
                serverHandles[ii] = itemsToRemove[ii].ServerHandle;

                // remove the associated monitored items.
                if (m_monitoredItems != null)
                {
                    lock (m_monitoredItems)
                    {
                        m_monitoredItems.Remove(itemsToRemove[ii].ClientHandle);
                    }
                }
            }

            IntPtr pErrors = IntPtr.Zero;

            string methodName = "IOPCItemMgt.RemoveItems";

            try
            {
                IOPCItemMgt server = BeginComCall<IOPCItemMgt>(methodName, true);

                // remove items.
                server.RemoveItems(
                    count,
                    serverHandles,
                    out pErrors);
            }
            catch (Exception e)
            {
                ComUtils.TraceComError(e, methodName);

                for (int ii = 0; ii < itemsToRemove.Count; ii++)
                {
                    itemsToRemove[ii].Created = false;
                    itemsToRemove[ii].ErrorId = Marshal.GetHRForException(e);
                }

                return;
            }
            finally
            {
                EndComCall(methodName);
            }

            // free returned error array.
            int[] errors = ComUtils.GetInt32s(ref pErrors, count, true);

            // save error codes.
            for (int ii = 0; ii < count; ii++)
            {
                itemsToRemove[ii].Created = false;
                itemsToRemove[ii].ErrorId = errors[ii];
            }

            /*
            Utils.Trace(
                "Group {0} RemoveItems({4}/{5}) {1}/{2}ms {3}%",
                m_clientHandle,
                m_samplingInterval,
                m_actualSamplingInterval,
                m_deadband,
                itemsToRemove.Count,
                m_items.Count);
            */
        }

        /// <summary>
        /// Reads the values for a set of items.
        /// </summary>
        public DaValue[] SyncRead(int[] serverHandles, int count)
        {
            // initialize output parameters.
            IntPtr pValues = IntPtr.Zero;
            IntPtr pErrors = IntPtr.Zero;

            if (count > 0)
            {
                string methodName = "IOPCSyncIO.Read";

                try
                {
                    IOPCSyncIO server = BeginComCall<IOPCSyncIO>(methodName, true);

                    server.Read(
                        OPCDATASOURCE.OPC_DS_DEVICE,
                        count,
                        serverHandles,
                        out pValues,
                        out pErrors);
                }
                catch (Exception e)
                {
                    ComUtils.TraceComError(e, methodName);
                    return null;
                }
                finally
                {
                    EndComCall(methodName);
                }
            }

            // unmarshal output parameters.
            DaValue[] values = GetItemValues(ref pValues, count, true);

            int[] errors = ComUtils.GetInt32s(ref pErrors, count, true);

            // save error codes.
            for (int ii = 0; ii < count; ii++)
            {
                values[ii].Error = errors[ii];
            }

            return values;
        }

        /// <summary>
        /// Writes the values for a set of items.
        /// </summary>
        public int[] SyncWrite(int[] serverHandles, object[] values, int count)
        {
            // initialize output parameters.
            IntPtr pErrors = IntPtr.Zero;

            string methodName = "IOPCSyncIO.Write";

            try
            {
                IOPCSyncIO server = BeginComCall<IOPCSyncIO>(methodName, true);

                server.Write(
                    count,
                    serverHandles,
                    values,
                    out pErrors);
            }
            catch (Exception e)
            {
                ComUtils.TraceComError(e, methodName);
                return null;
            }
            finally
            {
                EndComCall(methodName);
            }

            // unmarshal output parameters.
            return ComUtils.GetInt32s(ref pErrors, count, true);
        }

        #region Public Members
        /// <summary>
        /// Applys any changes to the group.
        /// </summary>
        /// <returns>True if the group contains a valid item.</returns>
        public bool ApplyChanges()
        {
            // create the group if it does not already exist.
            if (Unknown == null)
            {
                Create();
            }

            if (m_updateRequired)
            {
                ModifyGroup();
                m_updateRequired = false;
            }

            RemoveItems();
            AddItems();
            ActivateItems(true);
            ActivateItems(false);

            // check if at least one valid item.
            lock (Lock)
            {
                List<int> clientHandles = new List<int>();
                List<DaValue> values = new List<DaValue>();

                bool result = false;

                for (int ii = 0; ii < m_items.Count; ii++)
                {
                    if (m_items[ii].Created)
                    {
                        result = true;
                    }

                    if (m_items[ii].ErrorId < 0)
                    {
                        if (clientHandles == null)
                        {
                            clientHandles = new List<int>();
                            values = new List<DaValue>();
                        }

                        clientHandles.Add(m_items[ii].ClientHandle);
                        values.Add(new DaValue() { Error = m_items[ii].ErrorId, Timestamp = DateTime.UtcNow });
                    }

                    if (clientHandles != null)
                    {
                        OnDataChange(clientHandles.ToArray(), values.ToArray());
                    }
                }

                return result;
            }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Unmarshals and deallocates a OPCITEMRESULT structures.
        /// </summary>
        internal static int[] GetItemResults(ref IntPtr pInput, int count, bool deallocate)
        {
            int[] output = null;

            if (pInput != IntPtr.Zero && count > 0)
            {
                output = new int[count];

                IntPtr pos = pInput;

                for (int ii = 0; ii < count; ii++)
                {
                    OpcRcw.Da.OPCITEMRESULT result = (OpcRcw.Da.OPCITEMRESULT)Marshal.PtrToStructure(pos, typeof(OpcRcw.Da.OPCITEMRESULT));

                    output[ii] = result.hServer;

                    if (deallocate)
                    {
                        if (result.pBlob != IntPtr.Zero)
                        {
                            Marshal.FreeCoTaskMem(result.pBlob);
                            result.pBlob = IntPtr.Zero;
                            result.dwBlobSize = 0;
                        }

                        Marshal.DestroyStructure(pos, typeof(OpcRcw.Da.OPCITEMRESULT));
                    }

                    pos = (IntPtr)(pos.ToInt32() + Marshal.SizeOf(typeof(OpcRcw.Da.OPCITEMRESULT)));
                }

                if (deallocate)
                {
                    Marshal.FreeCoTaskMem(pInput);
                    pInput = IntPtr.Zero;
                }
            }

            return output;
        }

        /// <summary>
        /// Unmarshals and deallocates a OPCITEMSTATE structures.
        /// </summary>
        internal static DaValue[] GetItemValues(ref IntPtr pInput, int count, bool deallocate)
        {
            DaValue[] output = null;

            if (pInput != IntPtr.Zero && count > 0)
            {
                output = new DaValue[count];

                IntPtr pos = pInput;

                for (int ii = 0; ii < count; ii++)
                {
                    OpcRcw.Da.OPCITEMSTATE result = (OpcRcw.Da.OPCITEMSTATE)Marshal.PtrToStructure(pos, typeof(OpcRcw.Da.OPCITEMSTATE));

                    DaValue value = new DaValue();

                    value.Value = ComUtils.ProcessComValue(result.vDataValue);
                    value.Quality = result.wQuality;
                    value.Timestamp = ComUtils.GetDateTime(result.ftTimeStamp);

                    output[ii] = value;

                    if (deallocate)
                    {
                        Marshal.DestroyStructure(pos, typeof(OpcRcw.Da.OPCITEMSTATE));
                    }

                    pos = (IntPtr)(pos.ToInt32() + Marshal.SizeOf(typeof(OpcRcw.Da.OPCITEMSTATE)));
                }

                if (deallocate)
                {
                    Marshal.FreeCoTaskMem(pInput);
                    pInput = IntPtr.Zero;
                }
            }

            return output;
        }

        /// <summary>
        /// Creates an array of item value result objects from the callback data.
        /// </summary>
        internal static DaValue[] GetItemValues(
            int dwCount,
            object[] pvValues,
            short[] pwQualities,
            System.Runtime.InteropServices.ComTypes.FILETIME[] pftTimeStamps,
            int[] pErrors)
        {
            // contruct the item value results.
            DaValue[] values = new DaValue[dwCount];

            for (int ii = 0; ii < dwCount; ii++)
            {
                DaValue value = values[ii] = new DaValue();

                value.Error = pErrors[ii];

                if (pErrors[ii] >= 0)
                {
                    value.Value = ComUtils.ProcessComValue(pvValues[ii]);
                    value.Quality = pwQualities[ii];
                    value.Timestamp = ComUtils.GetDateTime(pftTimeStamps[ii]);
                }
            }

            // return results
            return values;
        }
        #endregion

        #region Private Fields
        private static int m_groupCounter = 0;
        private static int m_itemCounter = 0;
        private ComDaClient m_server;
        private int m_clientHandle;
        private int m_serverHandle;
        private int m_samplingInterval;
        private int m_actualSamplingInterval;
        private float m_deadband;
        private bool m_updateRequired;
        private List<GroupItem> m_items;
        private ComDaDataCallback m_callback;
        private Dictionary<int, DataChangeInfo> m_monitoredItems;
        #endregion
    }

    #region GroupItem Class
    /// <summary>
    /// An item that belongs to group.
    /// </summary>
    internal class GroupItem
    {
        public string ItemId;
        public int ServerHandle;
        public int ClientHandle;
        public int ErrorId;
        public bool Created;
        public bool Deleted;
        public bool Active;
        public bool ActiveChanged;
    }
    #endregion
}
