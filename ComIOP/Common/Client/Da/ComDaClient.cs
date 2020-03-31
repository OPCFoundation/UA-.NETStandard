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
using OpcRcw.Da;
using Opc.Ua;
using Opc.Ua.Server;
using Opc.Ua.Com;
using Opc.Ua.Com.Client;

namespace Opc.Ua.Com.Client
{
    /// <summary>
    /// Provides access to a COM DA server.
    /// </summary>
    public class ComDaClient : ComClient
    {
        #region Constructors
        /// <summary>
        /// Initializes a new instance of the <see cref="ComDaClient"/> class.
        /// </summary>
        /// <param name="configuration"></param>
        public ComDaClient(ComDaClientConfiguration configuration) : base(configuration)
        {
            m_cache = new Dictionary<string, DaElement>();
            m_configuration = configuration;
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Finds the branch or item.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <returns>The metadata for the branch.</returns>
        public DaElement FindElement(string itemId)
        {
            if (String.IsNullOrEmpty(itemId))
            {
                return null;
            }

            // check the cache.
            DaElement element = null;

            lock (m_cache)
            {
                if (m_cache.TryGetValue(itemId, out element))
                {
                    return element;
                }
            }
            
            // try extracting the name by parsing the item id.
            string name = null;

            if (m_configuration.ItemIdParser.Parse(this, m_configuration, itemId, out name))
            {
                element = CreateElement(itemId, name, null);
            }

            // need to do it the hard way by searching the address space.
            else
            {
                IDaElementBrowser browser = CreateBrowser(itemId);
                element = browser.Find(itemId, false);
                browser.Dispose();
            }

            // save element in the cache.
            if (element != null)
            {
                SaveElementInCache(element);
            }

            return element;
        }

        /// <summary>
        /// Finds the property metadata for the specified item id.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="propertyId">The property id.</param>
        /// <returns>The metadata for the property.</returns>
        public DaProperty FindProperty(string itemId, int propertyId)
        {
            if (String.IsNullOrEmpty(itemId))
            {
                return null;
            }

            // check the cache.
            DaElement element = null;

            lock (m_cache)
            {
                if (m_cache.TryGetValue(itemId, out element))
                {
                    if (element.Properties != null)
                    {
                        for (int ii = 0; ii < element.Properties.Length; ii++)
                        {
                            if (element.Properties[ii].PropertyId == propertyId)
                            {
                                return element.Properties[ii];
                            }
                        }
                    }
                }
            }

            // check if the element has to be loaded.
            if (element == null)
            {
                element = FindElement(itemId);

                if (element == null)
                {
                    return null;
                }
            }
            
            // update the property list.
            element.Properties = ReadAvailableProperties(itemId, false);

            if (element.Properties != null)
            {
                for (int ii = 0; ii < element.Properties.Length; ii++)
                {
                    if (element.Properties[ii].PropertyId == propertyId)
                    {
                        return element.Properties[ii];
                    }
                }
            }

            // not found.
            return null;
        }

        /// <summary>
        /// Finds the item id for the parent of the element.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <returns>The item id for the parent of the element.</returns>
        public string FindElementParentId(string itemId)
        {
            if (String.IsNullOrEmpty(itemId))
            {
                return null;
            }

            // check in cache.
            DaElement element = null;

            lock (m_cache)
            {
                if (m_cache.TryGetValue(itemId, out element))
                {
                    if (element.ParentId != null)
                    {
                        return element.ParentId;
                    }
                }
            }

            // check if the element is a branch.
            if (!ChangeBrowsePosition(OPCBROWSEDIRECTION.OPC_BROWSE_TO, itemId))
            {
                return null;
            }

            // need to fetch list of branches to search.
            EnumString enumerator = CreateEnumerator(true);

            if (enumerator == null)
            {
                return null;
            }

            // find the child with the same item id.
            try
            {
                // process all branches.
                string name = null;

                do
                {
                    name = enumerator.Next();

                    // a null indicates the end of list.
                    if (name == null)
                    {
                        break;
                    }

                    // fetch the item id.
                    string targetId = GetItemId(name);

                    // check if target found.
                    if (targetId == itemId)
                    {
                        return GetItemId(String.Empty);
                    }
                }
                while (name != null);

            }
            finally
            {
                enumerator.Dispose();
            }

            return null;
        }

        /// <summary>
        /// Creates a browser.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <returns>The browser object.</returns>
        public IDaElementBrowser CreateBrowser(string itemId)
        {
            // check if DA3 browse interface is supported.
            if (SupportsInterface<IOPCBrowse>())
            {
                return new Da20ElementBrowser(this, itemId, m_configuration.BrowseToNotSupported);
            }

            return new Da20ElementBrowser(this, itemId, m_configuration.BrowseToNotSupported);
        }

        /// <summary>
        /// Creates a new instance of the client with the same configuration.
        /// </summary>
        /// <returns>The copy of the client.</returns>
        public ComDaClient CloneClient()
        {
            ComDaClient clone = new ComDaClient(m_configuration);
            clone.LocaleId = this.LocaleId;
            clone.UserIdentity = this.UserIdentity;
            clone.Key = this.Key;
            return clone;
        }

        /// <summary>
        /// Reads the status from the server.
        /// </summary>
        public OPCSERVERSTATUS? GetStatus()
        {
            string methodName = "IOPCServer.GetStatus";

            try
            {
                IOPCServer server = BeginComCall<IOPCServer>(methodName, true);

                IntPtr ppServerStatus;
                server.GetStatus(out ppServerStatus);

                if (ppServerStatus == IntPtr.Zero)
                {
                    return null;
                }

                OpcRcw.Da.OPCSERVERSTATUS pStatus = (OpcRcw.Da.OPCSERVERSTATUS)Marshal.PtrToStructure(ppServerStatus, typeof(OpcRcw.Da.OPCSERVERSTATUS));

                Marshal.DestroyStructure(ppServerStatus, typeof(OpcRcw.Da.OPCSERVERSTATUS));
                Marshal.FreeCoTaskMem(ppServerStatus);

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
        /// Reads the item and property values from the server.
        /// </summary>
        /// <param name="requests">The requests.</param>
        public void Read(ReadRequestCollection requests)
        {
            if (m_supportsIOPCItemIO)
            {
                Da30ReadItemValues(requests);
            }
            else
            {
                Da20ReadItemValues(requests);
            }

            ReadPropertyValues(requests);
        }

        /// <summary>
        /// Writes the item and property values to the server.
        /// </summary>
        /// <param name="requests">The requests.</param>
        public void Write(WriteRequestCollection requests)
        {
            if (m_supportsIOPCItemIO)
            {
                Da30WriteItemValues(requests);
            }
            else
            {
                Da20WriteItemValues(requests);
            }
        }

        #region Group Management
        /// <summary>
        /// Creates a group.
        /// </summary>
        public object CreateGroup(
            int groupId,
            int samplingInterval,
            float deadband,
            out int groupHandle,
            out int revisedSamplingInterval)
        {
            groupHandle = 0;
            revisedSamplingInterval = 0;
            GCHandle hDeadband = GCHandle.Alloc(deadband, GCHandleType.Pinned);

            string methodName = "IOPCServer.AddGroup";

            try
            {
                IOPCServer server = BeginComCall<IOPCServer>(methodName, true);

                object group = null;
                Guid iid = typeof(OpcRcw.Da.IOPCItemMgt).GUID;

                server.AddGroup(
                    String.Empty,
                    1,
                    samplingInterval,
                    0,
                    IntPtr.Zero,
                    hDeadband.AddrOfPinnedObject(),
                    LocaleId,
                    out groupHandle,
                    out revisedSamplingInterval,
                    ref iid,
                    out group);

                return group;
            }
            catch (Exception e)
            {
                ComUtils.TraceComError(e, methodName);
                return null;
            }
            finally
            {
                EndComCall(methodName);
                hDeadband.Free();
            }
        }

        /// <summary>
        /// Removes a group.
        /// </summary>
        public void RemoveGroup(int groupHandle)
        {
            string methodName = "IOPCServer.RemoveGroup";

            try
            {
                IOPCServer server = BeginComCall<IOPCServer>(methodName, true);
                server.RemoveGroup(groupHandle, 0);
            }
            catch (Exception e)
            {
                ComUtils.TraceComError(e, methodName);
                return;
            }
            finally
            {
                EndComCall(methodName);
            }
        }
        #endregion

        /// <summary>
        /// Reads the property values and stores the results in the request objects.
        /// </summary>
        /// <param name="requests">The requests.</param>
        public void ReadPropertyValues(List<ReadRequest> requests)
        {
            for (int ii = 0; ii < requests.Count; ii++)
            {
                ReadRequest request = requests[ii];

                if (request != null && request.PropertyIds != null)
                {
                    request.PropertyValues = ReadPropertyValues(
                        request.ItemId,
                        request.PropertyIds.ToArray());
                }
            }
        }

        /// <summary>
        /// Read the available non-built in properties from the server.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="updateCache">if set to <c>true</c> the cache is updated.</param>
        /// <returns>The array of properties.</returns>
        public DaProperty[] ReadAvailableProperties(string itemId, bool updateCache)
        {
            string methodName = "IOPCItemProperties.QueryAvailableProperties";

            // query for available properties.
            int count = 0;

            IntPtr pPropertyIds  = IntPtr.Zero;
            IntPtr pDescriptions = IntPtr.Zero;
            IntPtr pDataTypes    = IntPtr.Zero;

            try
            {
                IOPCItemProperties server = BeginComCall<IOPCItemProperties>(methodName, true);

                server.QueryAvailableProperties(
                    itemId,
                    out count,
                    out pPropertyIds,
                    out pDescriptions,
                    out pDataTypes);
            }
            catch (Exception e)
            {
                if (ComUtils.IsUnknownError(e, ResultIds.E_FAIL, ResultIds.E_INVALIDARG, ResultIds.E_UNKNOWNITEMID, ResultIds.E_INVALIDITEMID))
                {
                    ComUtils.TraceComError(e, methodName);
                }

                return null;
            }
            finally
            {
                EndComCall(methodName);
            }

            // unmarshal results.
            int[]    propertyIds  = ComUtils.GetInt32s(ref pPropertyIds, count, true);
            string[] descriptions = ComUtils.GetUnicodeStrings(ref pDescriptions, count, true);
            short[]  datatype     = ComUtils.GetInt16s(ref pDataTypes, count, true);

            List<DaProperty> properties = new List<DaProperty>();

            for (int ii = 0; ii < count; ii++)
            {
                // do not return any of the built in properties.
                if (propertyIds[ii] <= PropertyIds.TimeZone)
                {
                    continue;
                }

                DaProperty property = new DaProperty();

                property.PropertyId = propertyIds[ii];
                property.Name = descriptions[ii];
                property.DataType = datatype[ii];

                properties.Add(property);
            }

            // fetch the item ids.
            if (properties.Count > 0)
            {
                DaProperty[] array = properties.ToArray();

                GetPropertyItemIds(itemId, array);

                // update the cache.
                if (updateCache)
                {
                    lock (m_cache)
                    {
                        DaElement element = null;

                        if (m_cache.TryGetValue(itemId, out element))
                        {
                            element.Properties = array;
                        }
                    }
                }

                return array;
            }

            return null;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Saves the element in cache.
        /// </summary>
        /// <param name="element">The element.</param>
        private void SaveElementInCache(DaElement element)
        {
            lock (m_cache)
            {
                m_cache[element.ItemId] = element;
            }
        }

        /// <summary>
        /// Creates a new element.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="name">The name.</param>
        /// <param name="parentId">The parent id.</param>
        /// <returns>The element.</returns>
        private DaElement CreateElement(string itemId, string name, string parentId)
        {
            DaElement element = new DaElement();
            element.ItemId = itemId;            
            UpdateElement(element, name, parentId);
            return element;
        }

        /// <summary>
        /// Updates a element.
        /// </summary>
        /// <param name="element">The element.</param>
        /// <param name="name">The name.</param>
        /// <param name="parentId">The parent id.</param>
        private void UpdateElement(DaElement element, string name, string parentId)
        {      
            // only update the name if specified.         
            if (name != null)
            {
                element.Name = name;
            }

            // same for the parent id.
            if (parentId != null)
            {
                element.ParentId = parentId;
            }

            // read item property values.
            DaValue[] values = ReadPropertyValues(element.ItemId, m_CoreProperties);

            // handle errors gracefully.
            if (values == null || values.Length == 0)
            {
                element.ElementType = DaElementType.Branch;
                return;
            }

            // must be an item if the data type property exists.
            if (values[0].Error >= 0)
            {
                element.ElementType = DaElementType.Item;
                element.DataType = values[0].GetValue<short>();
                element.AccessRights = values[1].GetValue<int>();
                element.ScanRate = values[2].GetValue<float>();
                element.Description = values[3].GetValue<string>();
                element.EngineeringUnits = values[4].GetValue<string>();
                element.EuInfo = values[5].GetValue<string[]>();
                element.EuType = values[6].GetValue<int>();
                element.HighEU = values[7].GetValue<double>();
                element.LowEU = values[8].GetValue<double>();
                element.OpenLabel = values[9].GetValue<string>();
                element.CloseLabel = values[10].GetValue<string>();
                element.HighIR = values[11].GetValue<double>();
                element.LowIR = values[12].GetValue<double>();

                // check if the time zone is specified.
                if (values[13].Error >= 0)
                {
                    element.TimeZone = values[13].GetValue<int>();
                }

                // check for analog item (checks for HighEU if EuType property not supported).
                if ((values[6].Error < 0 && values[7].Error >= 0) || element.EuType == (int)OPCEUTYPE.OPC_ANALOG)
                {
                    element.ElementType = DaElementType.AnalogItem;
                }

                // check for enumerated item.
                else if (element.EuType == (int)OPCEUTYPE.OPC_ENUMERATED)
                {
                    element.ElementType = DaElementType.EnumeratedItem;
                }

                // check for digital item (checks for CloseLabel property).
                else if (values[10].Error >= 0)
                {
                    element.ElementType = DaElementType.DigitalItem;
                }
            }

            // the element must be a branch. 
            else
            {
                element.ElementType = DaElementType.Branch;

                // branches could have description property.
                element.Description = values[3].GetValue<string>();
            }
        }

        /// <summary>
        /// Finds the element and updates the name if it is not already cached.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="name">The name.</param>
        /// <param name="parentId">The parent id.</param>
        /// <returns>The element.</returns>
        private DaElement FindElement(string itemId, string name, string parentId)
        {
            if (String.IsNullOrEmpty(itemId))
            {
                return null;
            }

            // look in cache for existing element.
            DaElement element = null;

            lock (m_cache)
            {
                if (!m_cache.TryGetValue(itemId, out element))
                {
                    element = null;
                }
            }

            // create a new element.
            if (element == null)
            {
                element = CreateElement(itemId, name, parentId);
                SaveElementInCache(element);
            }

            // update the element.
            element.Name = name;
            element.ParentId = parentId;

            return element;
        }

        /// <summary>
        /// The core properties used to determine an item type.
        /// </summary>
        private static readonly int[] m_CoreProperties = new int[]
        {
            PropertyIds.DataType,
            PropertyIds.AccessRights,
            PropertyIds.ScanRate,
            PropertyIds.Description,
            PropertyIds.EngineeringUnits,
            PropertyIds.EuInfo,
            PropertyIds.EuType,
            PropertyIds.HighEU,
            PropertyIds.LowEU,
            PropertyIds.OpenLabel,
            PropertyIds.CloseLabel,
            PropertyIds.HighIR,
            PropertyIds.LowIR,
            PropertyIds.TimeZone
        };

        /// <summary>
        /// Changes the browse position.
        /// </summary>
        /// <param name="direction">The direction.</param>
        /// <param name="name">The name.</param>
        /// <returns>True if the operation was successful.</returns>
        private bool ChangeBrowsePosition(OPCBROWSEDIRECTION direction, string name)
        {
            string methodName = "IOPCBrowseServerAddressSpace.ChangeBrowsePosition";

            if (name == null)
            {
                name = String.Empty;
            }

            try
            {
                IOPCBrowseServerAddressSpace server = BeginComCall<IOPCBrowseServerAddressSpace>(methodName, true);
                server.ChangeBrowsePosition(direction, name);
            }
            catch (Exception e)
            {
                if (ComUtils.IsUnknownError(e, ResultIds.E_FAIL, ResultIds.E_INVALIDARG))
                {
                    ComCallError(methodName, e);
                }

                return false;
            }
            finally
            {
                EndComCall(methodName);
            }

            return true;
        }

        /// <summary>
        /// Creates an enumerator for the current browse position.
        /// </summary>
        /// <param name="branches">if set to <c>true</c> then branches are enumerated.</param>
        /// <returns>The wrapped enumerator.</returns>
        private EnumString CreateEnumerator(bool branches)
        {
            IEnumString unknown = null;

            string methodName = "IOPCBrowseServerAddressSpace.BrowseOPCItemIDs";

            try
            {
                IOPCBrowseServerAddressSpace server = BeginComCall<IOPCBrowseServerAddressSpace>(methodName, true);

                OPCBROWSETYPE browseType = OPCBROWSETYPE.OPC_BRANCH;

                if (!branches)
                {
                    browseType = OPCBROWSETYPE.OPC_LEAF;
                }

                server.BrowseOPCItemIDs(browseType, String.Empty, 0, 0, out unknown);
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

            // wrapper the enumrator. hardcoding a buffer size of 256.
            return new EnumString(unknown, 256);
        }

        /// <summary>
        /// Looks up the item id for the node relative to the current browse position.
        /// </summary>
        /// <param name="browseName">Name of the element to lookup.</param>
        /// <returns>The item id. Null if an error occurs.</returns>
        private string GetItemId(string browseName)
        {
            string methodName = "IOPCBrowseServerAddressSpace.GetItemID";

            try
            {
                IOPCBrowseServerAddressSpace server = BeginComCall<IOPCBrowseServerAddressSpace>(methodName, true);
                string itemId = null;
                server.GetItemID(browseName, out itemId);
                return itemId;
            }
            catch (Exception e)
            {
                if (ComUtils.IsUnknownError(e, ResultIds.E_FAIL))
                {
                    ComCallError(methodName, e);
                }

                return null;
            }
            finally
            {
                EndComCall(methodName);
            }
        }

        /// <summary>
        /// Reads the values of the properties from the server.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="propertyIds">The list of property ids to read. All properties are read if null.</param>
        /// <returns>True if the element has properies.</returns>
        private DaValue[] ReadPropertyValues(string itemId, params int[] propertyIds)
        {
            string methodName = "IOPCItemProperties.GetItemProperties";

            // check for trivial case.
            if (propertyIds == null)
            {
                return null;
            }

            int count = propertyIds.Length;
            DaValue[] results = new DaValue[count];
            
            // check for empty list.
            if (count == 0)
            {
                return results;
            }

            // get the values from the server.
            IntPtr pValues = IntPtr.Zero;
            IntPtr pErrors = IntPtr.Zero;

            try
            {
                IOPCItemProperties server = BeginComCall<IOPCItemProperties>(methodName, true);

                server.GetItemProperties(
                    itemId,
                    count,
                    propertyIds,
                    out pValues,
                    out pErrors);
            }
            catch (Exception e)
            {
                if (ComUtils.IsUnknownError(e, ResultIds.E_FAIL, ResultIds.E_INVALIDARG, ResultIds.E_UNKNOWNITEMID, ResultIds.E_INVALIDITEMID))
                {
                    ComUtils.TraceComError(e, methodName);
                    return null;
                }

                for (int ii = 0; ii < count; ii++)
                {
                    DaValue result = results[ii] = new DaValue();
                    result.Value = null;
                    result.Quality = OpcRcw.Da.Qualities.OPC_QUALITY_GOOD;
                    result.Timestamp = DateTime.UtcNow;
                    result.Error = Marshal.GetHRForException(e);
                }

                return results;
            }
            finally
            {
                EndComCall(methodName);
            }

            // unmarshal results.
            object[] values = ComUtils.GetVARIANTs(ref pValues, count, true);
            int[]    errors = ComUtils.GetInt32s(ref pErrors, count, true);

            for (int ii = 0; ii < count; ii++)
            {
                DaValue result = results[ii] = new DaValue();

                Variant convertedValue;
                result.Error = ComDaClientNodeManager.RemoteToLocalValue(values[ii], out convertedValue);

                if (result.Error < 0)
                {
                    continue;
                }

                result.Value = convertedValue.Value;
                result.Quality = OpcRcw.Da.Qualities.OPC_QUALITY_GOOD;
                result.Timestamp = DateTime.UtcNow;
                result.Error = errors[ii];
            }

            return results;
        }

        /// <summary>
        /// Gets the property item ids.
        /// </summary>
        /// <param name="itemId">The item id.</param>
        /// <param name="properties">The properties to update.</param>
        private void GetPropertyItemIds(string itemId, DaProperty[] properties)
        {
            string methodName = "IOPCItemProperties.LookupItemIDs";

            int count = properties.Length;

            // get list of ids to request.
            int[] propertyIds = new int[count];

            for (int ii = 0; ii < propertyIds.Length; ii++)
            {
                propertyIds[ii] = properties[ii].PropertyId;
            }

            // request the item ids.
            IntPtr pItemIds = IntPtr.Zero;
            IntPtr pErrors  = IntPtr.Zero;

            try
            {
                IOPCItemProperties server = BeginComCall<IOPCItemProperties>(methodName, true);

                server.LookupItemIDs(
                    itemId,
                    count,
                    propertyIds,
                    out pItemIds,
                    out pErrors);
            }
            catch (Exception e)
            {
                if (ComUtils.IsUnknownError(e, ResultIds.E_FAIL, ResultIds.E_UNKNOWNITEMID, ResultIds.E_INVALIDITEMID))
                {
                    ComUtils.TraceComError(e, methodName);
                }

                return;
            }
            finally
            {
                EndComCall(methodName);
            }

            // unmarshal results.
            string[] itemIds = ComUtils.GetUnicodeStrings(ref pItemIds, count, true);
            int[]    errors  = ComUtils.GetInt32s(ref pErrors, count, true);

            // update the objects.
            for (int ii = 0; ii < count; ii++)
            {
                if (errors[ii] >= 0)
                {
                    properties[ii].ItemId = itemIds[ii];
                }
                else
                {
                    properties[ii].ItemId = null;
                }
            }
        }

        //private ComDaGroup m_group = null;
        //private object m_groupLock = new object();

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                //lock (m_groupLock)
                //{
                //    if (m_group != null)
                //    {
                //        Utils.SilentDispose(m_group);
                //        m_group = null;
                //    }
                //}
            }

            base.Dispose(disposing);
        }

        /// <summary>
        /// Reads the item values and stores the results in the request object.
        /// </summary>
        /// <param name="requests">The requests.</param>
        private void Da20ReadItemValues(List<ReadRequest> requests)
        {
            // lock (m_groupLock)
            {
                // if (m_group == null)
                //{                
                    ComDaGroup m_group = new ComDaGroup(this, false);
                //}

                try
                {
                    int count1 = 0;
                    GroupItem[] items = new GroupItem[requests.Count];
                    ReadRequest[] addItemRequests = new ReadRequest[requests.Count];

                    // create the items in the temporary group.
                    for (int ii = 0; ii < requests.Count; ii++)
                    {
                        ReadRequest request = requests[ii];

                        if (request == null)
                        {
                            continue;
                        }

                        if (!request.ValueRequired)
                        {
                            continue;
                        }

                        // add the item.
                        items[count1] = m_group.CreateItem(request.ItemId, 0, 0, true);
                        addItemRequests[count1] = request;
                        count1++;
                    }

                    // create the items on the server.
                    m_group.ApplyChanges();

                    // build the list of values to write.
                    int count2 = 0;
                    int[] serverHandles = new int[count1];
                    ReadRequest[] readRequests = new ReadRequest[count1];

                    for (int ii = 0; ii < count1; ii++)
                    {
                        // check for error on create.
                        GroupItem item = items[ii];
                        ReadRequest request = addItemRequests[ii];

                        if (item.ErrorId < 0)
                        {
                            request.Value = new DaValue();
                            request.Value.Error = item.ErrorId;
                            continue;
                        }

                        serverHandles[count2] = item.ServerHandle;
                        readRequests[count2] = request;
                        count2++;
                    }

                    if (count2 > 0)
                    {
                        // write values to the server.
                        DaValue[] values = m_group.SyncRead(serverHandles, count2);

                        // read the values.
                        for (int ii = 0; ii < count2; ii++)
                        {
                            if (values != null && values.Length > ii)
                            {
                                readRequests[ii].Value = values[ii];
                            }
                            else
                            {
                                readRequests[ii].Value = new DaValue() { Error = ResultIds.E_FAIL, Timestamp = DateTime.UtcNow };
                            }
                        }

                        // delete the items.
                        for (int ii = 0; ii < count1; ii++)
                        {
                            GroupItem item = items[ii];

                            if (item.ErrorId >= 0)
                            {
                                m_group.RemoveItem(item);
                            }
                        }

                        m_group.ApplyChanges();
                    }
                }
                finally
                {
                    // delete the group and items.
                   m_group.Delete();
                }
            }
        }

        /// <summary>
        /// Writes the item values to servers.
        /// </summary>
        /// <param name="requests">The requests.</param>
        private void Da20WriteItemValues(List<WriteRequest> requests)
        {
            //lock (m_groupLock)
            {
                //if (m_group == null)
                //{
                    ComDaGroup m_group = new ComDaGroup(this, false);
                //}

                try
                {
                    int count1 = 0;
                    GroupItem[] items = new GroupItem[requests.Count];
                    WriteRequest[] addItemRequests = new WriteRequest[requests.Count];
                    object[] convertedValues = new object[requests.Count];

                    // create the items in the temporary group.
                    for (int ii = 0; ii < requests.Count; ii++)
                    {
                        WriteRequest request = requests[ii];

                        if (request == null)
                        {
                            continue;
                        }

                        // status code writes not supported.
                        if (request.Value.StatusCode != StatusCodes.Good)
                        {
                            request.Error = ResultIds.E_NOTSUPPORTED;
                            continue;
                        }

                        // timestamp writes not supported.
                        if (request.Value.ServerTimestamp != DateTime.MinValue)
                        {
                            request.Error = ResultIds.E_NOTSUPPORTED;
                            continue;
                        }

                        // timestamp writes not supported.
                        if (request.Value.SourceTimestamp != DateTime.MinValue)
                        {
                            request.Error = ResultIds.E_NOTSUPPORTED;
                            continue;
                        }

                        // convert to a DA compatible type.
                        object convertedValue = null;
                        request.Error = ComDaClientNodeManager.LocalToRemoteValue(request.Value.WrappedValue, out convertedValue);

                        if (request.Error < 0)
                        {
                            continue;
                        }

                        // add the item.
                        items[count1] = m_group.CreateItem(request.ItemId, 0, 0, true);
                        addItemRequests[count1] = request;
                        convertedValues[count1] = convertedValue;
                        count1++;
                    }

                    // create the items on the server.
                    m_group.ApplyChanges();

                    // build the list of values to write.
                    int count2 = 0;
                    int[] serverHandles = new int[count1];
                    object[] values = new object[count1];
                    WriteRequest[] writeRequests = new WriteRequest[count1];

                    for (int ii = 0; ii < count1; ii++)
                    {
                        // check for error on create.
                        GroupItem item = items[ii];
                        WriteRequest request = addItemRequests[ii];

                        if (item.ErrorId < 0)
                        {
                            request.Error = item.ErrorId;
                            continue;
                        }

                        serverHandles[count2] = item.ServerHandle;
                        values[count2] = convertedValues[ii];
                        writeRequests[count2] = request;
                        count2++;
                    }

                    if (count2 > 0)
                    {
                        // write values to the server.
                        int[] errors = m_group.SyncWrite(serverHandles, values, count2);

                        // read the errors.
                        for (int ii = 0; ii < count2; ii++)
                        {
                            if (errors != null && errors.Length > ii)
                            {
                                writeRequests[ii].Error = errors[ii];
                            }
                            else
                            {
                                writeRequests[ii].Error = ResultIds.E_FAIL;
                            }
                        }

                        // delete the items.
                        for (int ii = 0; ii < count1; ii++)
                        {
                            GroupItem item = items[ii];

                            if (item.ErrorId >= 0)
                            {
                                m_group.RemoveItem(item);
                            }
                        }

                        m_group.ApplyChanges();
                    }
                }
                finally
                {
                    // delete the group and items.
                    m_group.Delete();
                }
            }
        }

        /// <summary>
        /// Reads the values using the DA3 interfaces.
        /// </summary>
        /// <param name="requests">The requests.</param>
        private void Da30ReadItemValues(List<ReadRequest> requests)
        {
            string[] itemIds = new string[requests.Count];
            int[] maxAge = new int[requests.Count];
            List<int> requestIndexes = new List<int>();

            int count = 0;

            for (int ii = 0; ii < requests.Count; ii++)
            {
                if (!requests[ii].ValueRequired)
                {
                    continue;
                }

                itemIds[count] = requests[ii].ItemId;
                maxAge[count] = 0;
                requestIndexes.Add(ii);
                count++;
            }

            if (count <= 0)
            {
                return;
            }

            IntPtr ppValues = IntPtr.Zero;
            IntPtr ppQualities = IntPtr.Zero;
            IntPtr ppTimeStamps = IntPtr.Zero;
            IntPtr ppErrors = IntPtr.Zero;

            string methodName = "IOPCItemIO.Read";

            try
            {
                IOPCItemIO server = BeginComCall<IOPCItemIO>(methodName, true);
                
                server.Read(
                    requestIndexes.Count, 
                    itemIds, 
                    maxAge, 
                    out ppValues, 
                    out ppQualities, 
                    out ppTimeStamps, 
                    out ppErrors);
            }
            catch (Exception e)
            {
                ComUtils.TraceComError(e, methodName);
                return;
            }
            finally
            {
                EndComCall(methodName);
            }

            System.Runtime.InteropServices.ComTypes.FILETIME[] timeStamps = ComUtils.GetFILETIMEs(ref ppTimeStamps, requestIndexes.Count);
            object[] values = ComUtils.GetVARIANTs(ref ppValues, count, true);
            short[] qualities = ComUtils.GetInt16s(ref ppQualities, count, true);
            int[] errors = ComUtils.GetInt32s(ref ppErrors, count, true);

            DaValue[] daValues = ComDaGroup.GetItemValues(count, values, qualities, timeStamps, errors);

            for (int ii = 0; ii < requestIndexes.Count; ii++)
            {
                requests[requestIndexes[ii]].Value = daValues[ii];
            }
        }

        /// <summary>
        /// Writes the values using the DA3 interfaces.
        /// </summary>
        /// <param name="requests">The requests.</param>
        private void Da30WriteItemValues(List<WriteRequest> requests)
        {
            int count = 0;
            string[] itemIDs = new string[requests.Count];
            OpcRcw.Da.OPCITEMVQT[] values = new OpcRcw.Da.OPCITEMVQT[requests.Count];
            WriteRequest[] writeRequests = new WriteRequest[requests.Count];

            for (int ii = 0; ii < requests.Count; ii++)
            {
                WriteRequest request = requests[ii];

                if (request == null)
                {
                    continue;
                }

                // convert to a DA compatible type.
                object convertedValue = null;
                request.Error = ComDaClientNodeManager.LocalToRemoteValue(request.Value.WrappedValue, out convertedValue);

                if (request.Error < 0)
                {
                    continue;
                }

                itemIDs[count] = request.ItemId;

                values[count].vDataValue = convertedValue;
                values[count].bQualitySpecified = 0;
                values[count].bTimeStampSpecified = 0;
                
                // check for quality.
                values[count].wQuality = ComUtils.GetQualityCode(request.Value.StatusCode);

                if (values[count].wQuality != Qualities.OPC_QUALITY_GOOD)
                {
                    values[count].bQualitySpecified = 1;
                }

                // check for server timestamp.
                if (request.Value.ServerTimestamp != DateTime.MinValue)
                {
                    values[count].ftTimeStamp = ComUtils.GetFILETIME(request.Value.ServerTimestamp);
                    values[count].bTimeStampSpecified = 1;
                }

                // ignore server timestamp if source timestamp is provided.
                if (request.Value.SourceTimestamp != DateTime.MinValue)
                {
                    values[count].ftTimeStamp = ComUtils.GetFILETIME(request.Value.SourceTimestamp);
                    values[count].bTimeStampSpecified = 1;
                }

                writeRequests[count] = request;
                count++;
            }

            IntPtr ppErrors = IntPtr.Zero;
            string methodName = "IOPCItemIO.WriteVQT";
            
            try
            {
                IOPCItemIO server = BeginComCall<IOPCItemIO>(methodName, true);
                
                server.WriteVQT(
                    count, 
                    itemIDs,
                    values, 
                    out ppErrors);
            }
            catch (Exception e)
            {
                ComUtils.TraceComError(e, methodName);
                return;
            }
            finally
            {
                EndComCall(methodName);
            }

            int[] errors = ComUtils.GetInt32s(ref ppErrors, count, true);
            
            for (int ii = 0; ii < count; ii++)
            {
                writeRequests[ii].Error = errors[ii];
            }
        }

        /// <summary>
        /// Called immediately after connecting to the server.
        /// </summary>
        protected override void OnConnected()
        {
            m_supportsIOPCItemIO = SupportsInterface<IOPCItemIO>();
        }
        #endregion

        #region Da20ElementBrowser Class
        /// <summary>
        /// Browses for DA elements using the DA 2.0 interfaces.
        /// </summary>
        private class Da20ElementBrowser : IDaElementBrowser
        {
            #region Constructors
            /// <summary>
            /// Initializes a new instance of the <see cref="Da20ElementBrowser"/> class.
            /// </summary>
            /// <param name="client">The client.</param>
            /// <param name="itemId">The item id.</param>
            /// <param name="browseToNotSupported">if set to <c>true</c> BROWSE_TO not supported.</param>
            public Da20ElementBrowser(
                ComDaClient client, 
                string itemId,
                bool browseToNotSupported)
            {
                m_client = client;
                m_itemId = itemId;
                m_branches = false;
                m_browseToNotSupported = browseToNotSupported;
            }
            #endregion

            #region IDisposable Members
            /// <summary>
            /// Frees any unmanaged resources.
            /// </summary>
            public void Dispose()
            {   
                Dispose(true);
            }

            /// <summary>
            /// An overrideable version of the Dispose.
            /// </summary>
            protected virtual void Dispose(bool disposing)
            {  
                if (disposing)
                {
                    Utils.SilentDispose(m_enumerator);
                    m_enumerator = null;

                    Utils.SilentDispose(m_clone);
                    m_clone = null;
                }
            }
            #endregion

            /// <summary>
            /// Sets the browse position using the BROWSE_DOWN operator.
            /// </summary>
            /// <param name="itemId">The item id.</param>
            /// <returns>True if the item id was found.</returns>
            private bool SearchAndSetBrowsePosition(string itemId)
            {
                EnumString enumerator = m_clone.CreateEnumerator(true);

                // a null indicates an error.
                if (enumerator == null)
                {
                    return false;
                }

                // collect names.
                Queue<string> names = new Queue<string>();

                try
                {
                    do
                    {
                        // fetch the next name.
                        string name = enumerator.Next();

                        // a null indicates the end of list.
                        if (name == null)
                        {
                            break;
                        }

                        // fetch the item id.
                        string branchId = m_clone.GetItemId(name);

                        if (branchId == itemId)
                        {
                            return m_clone.ChangeBrowsePosition(OPCBROWSEDIRECTION.OPC_BROWSE_DOWN, name);
                        }

                        names.Enqueue(name);
                    }
                    while (true);
                }
                finally
                {
                    enumerator.Dispose();
                }

                // recursively search tree.
                while (names.Count > 0)
                {
                    string name = names.Dequeue();

                    if (!m_clone.ChangeBrowsePosition(OPCBROWSEDIRECTION.OPC_BROWSE_DOWN, name))
                    {
                        continue;
                    }

                    if (SearchAndSetBrowsePosition(itemId))
                    {
                        return true;
                    }

                    m_clone.ChangeBrowsePosition(OPCBROWSEDIRECTION.OPC_BROWSE_UP, null);
                }

                // not found anywhere.
                return false;
            }

            /// <summary>
            /// Sets the browse position.
            /// </summary>
            /// <param name="itemId">The item id.</param>
            /// <returns>True if the item id was found.</returns>
            private bool SetBrowsePosition(string itemId)
            {
                // go directly to item id.
                if (m_clone.ChangeBrowsePosition(OPCBROWSEDIRECTION.OPC_BROWSE_TO, itemId))
                {
                    return true;
                }

                // nothing more to do isf browse to supported.
                if (!m_browseToNotSupported)
                {
                    return false;
                }
                
                // do it the hard way.
                while (m_clone.ChangeBrowsePosition(OPCBROWSEDIRECTION.OPC_BROWSE_UP, null))
                {
                    // skip to root.
                }

                // nothing more to do at root.
                if (String.IsNullOrEmpty(itemId))
                {
                    return true;
                }

                // find item id in tree.
                return SearchAndSetBrowsePosition(itemId);
            }

            #region IDaElementBrowser Members
            /// <summary>
            /// Returns the next DA element.
            /// </summary>
            /// <returns>A DA element. Null if nothing left to browse.</returns>
            public DaElement Next()
            {
                // check if already completed.
                if (m_completed)
                {
                    return null;
                }

                // create the enumerator if not already created.
                if (m_enumerator == null)
                {
                    // need to clone the client since ChangeBrowsePosition prevents multiple
                    // simultaneous browse operations.
                    m_clone = m_client.CloneClient();
                    m_clone.CreateInstance();

                    // nothing to browse if change browse position failed.
                    if (!SetBrowsePosition(m_itemId))
                    {
                        return null;
                    }

                    m_enumerator = m_clone.CreateEnumerator(false);
                    m_branches = false;

                    // a null indicates an error.
                    if (m_enumerator == null)
                    {
                        m_completed = true;
                        return null;
                    }
                }

                // need a loop in case errors occur fetching element metadata.
                DaElement element = null;

                do
                {
                    // fetch the next name.
                    string name = m_enumerator.Next();

                    // a null indicates the end of list.
                    if (name == null)
                    {
                        if (!m_branches)
                        {
                            m_branches = true;
                            m_enumerator.Dispose();
                            m_enumerator = m_clone.CreateEnumerator(true);
                            continue;
                        }

                        m_completed = true;
                        return null;
                    }

                    // suppress duplicates when a item is also a branch.
                    if (m_branches)
                    {
                        if (m_itemNames != null)
                        {
                            if (m_itemNames.ContainsKey(name))
                            {
                                continue;
                            }
                        }
                    }

                    // save the item name to allow checks for duplicates.
                    else
                    {
                        if (m_itemNames == null)
                        {
                            m_itemNames = new Dictionary<string, object>();
                        }

                        m_itemNames[name] = null;
                    }

                    // fetch the item id.
                    string itemId = m_clone.GetItemId(name);

                    // fetch the metadata.
                    element = m_client.FindElement(itemId, name, m_itemId);
                }
                while (element == null);
                
                // return element.
                return element;
            }

            /// <summary>
            /// Finds the element with the specified item id.
            /// </summary>
            /// <param name="targetId">The target id.</param>
            /// <param name="isItem">if set to <c>true</c> [is item].</param>
            /// <returns>The element if found.</returns>
            public DaElement Find(string targetId, bool isItem)
            {
                // check for root.
                if (String.IsNullOrEmpty(targetId))
                {
                    m_client.FindElement(targetId, String.Empty, String.Empty);
                }

                // create the clone used to find the item.
                if (m_clone == null)
                {
                    m_clone = m_client.CloneClient();
                    m_clone.CreateInstance();
                }

                bool recursive = false;

                // check if it is possible to browse to the item.
                if (m_clone.ChangeBrowsePosition(OPCBROWSEDIRECTION.OPC_BROWSE_TO, targetId))
                {
                    if (!m_clone.ChangeBrowsePosition(OPCBROWSEDIRECTION.OPC_BROWSE_UP, String.Empty))
                    {
                        return null;
                    }
                }
                
                // need to do it the hard way from the root of the hierarchy.
                else
                {
                    while (m_clone.ChangeBrowsePosition(OPCBROWSEDIRECTION.OPC_BROWSE_UP, String.Empty));
                    recursive = true;
                }

                // get the item id for the parent.
                string parentId = m_clone.GetItemId(String.Empty);

                // find the target.
                return Find(parentId, targetId, false, recursive);
            }

            /// <summary>
            /// Finds the element with the specified item id.
            /// </summary>
            /// <param name="parentId">The parent id.</param>
            /// <param name="targetId">The target id.</param>
            /// <param name="isItem">if set to <c>true</c> the element is a item.</param>
            /// <param name="recursive">if set to <c>true</c> [recursive].</param>
            /// <returns>The element if found.</returns>
            public DaElement Find(string parentId, string targetId, bool isItem, bool recursive)
            {
                // create the enumerator if not already created.
                m_enumerator = m_clone.CreateEnumerator(false);

                // a null indicates an error.
                if (m_enumerator == null)
                {
                    return null;
                }

                string name = null;
                
                // process all items.
                do
                {
                    // fetch the next name.
                    name = m_enumerator.Next();

                    // a null indicates the end of list.
                    if (name == null)
                    {
                        break;
                    }

                    // fetch the item id.
                    string itemId = m_clone.GetItemId(name);

                    // fetch the metadata.
                    DaElement element = m_client.FindElement(itemId, name, parentId);

                    // check if target found.
                    if (targetId == itemId)
                    {
                        return element;
                    }
                }
                while (true);

                m_enumerator.Dispose();
                m_enumerator = null;

                List<DaElement> branches = new List<DaElement>();

                // fetch the branches of the target is a branch or if a recursive search is required.
                if (!isItem || recursive)
                {
                    // need to fetch list of branches to search.
                    m_enumerator = m_clone.CreateEnumerator(true);

                    if (m_enumerator == null)
                    {
                        return null;
                    }

                    // process all branches.
                    do
                    {
                        name = m_enumerator.Next();

                        // a null indicates the end of list.
                        if (name == null)
                        {
                            break;
                        }

                        // fetch the item id.
                        string itemId = m_clone.GetItemId(name);

                        // fetch the metadata.
                        DaElement element = m_client.FindElement(itemId, name, parentId);

                        // save branch for recursive search if not found at this level.
                        if (recursive)
                        {
                            branches.Add(element);
                        }

                        // check if target found.
                        if (targetId == itemId)
                        {
                            return element;
                        }
                    }
                    while (name != null);

                    m_enumerator.Dispose();
                    m_enumerator = null;
                }

                // all done if not doing a recursive search.
                if (!recursive)
                {
                    return null;
                }

                // recursively search hierarchy.
                for (int ii = 0; ii < branches.Count; ii++)
                {
                    m_clone.ChangeBrowsePosition(OPCBROWSEDIRECTION.OPC_BROWSE_DOWN, branches[ii].Name);

                    DaElement element = Find(branches[ii].ItemId, targetId, isItem, recursive);

                    if (element != null)
                    {
                        return element;
                    }

                    m_clone.ChangeBrowsePosition(OPCBROWSEDIRECTION.OPC_BROWSE_UP, String.Empty);
                }

                // not found.
                return null;
            }
            #endregion

            #region Private Fields
            private ComDaClient m_client;
            private ComDaClient m_clone;
            private string m_itemId;
            private Opc.Ua.Com.Client.EnumString m_enumerator;
            private bool m_branches;
            private bool m_completed;
            private Dictionary<string,object> m_itemNames;
            private bool m_browseToNotSupported;
            #endregion
        }
        #endregion
        
        #region Private Fields
        private Dictionary<string,DaElement> m_cache;
        private ComDaClientConfiguration m_configuration;
        private bool m_supportsIOPCItemIO;
        #endregion
    }

    /// <summary>
    /// An interface to an object that browses a DA COM server.
    /// </summary>
    public interface IDaElementBrowser : IDisposable
    {
        /// <summary>
        /// Returns the next element.
        /// </summary>
        /// <returns>The next element. Null if nothing found or an error occurs.</returns>
        DaElement Next();

        /// <summary>
        /// Finds the element with the specified item id.
        /// </summary>
        /// <param name="targetId">The target id.</param>
        /// <param name="isBranch">if set to <c>true</c> the element is a branch.</param>
        /// <returns>The element if found.</returns>
        DaElement Find(string targetId, bool isBranch);
    }
}
