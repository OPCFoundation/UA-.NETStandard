/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
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
using System.Xml;
using System.IO;
using System.Reflection;
using System.Threading;
using Opc.Ua;
using Opc.Ua.Server;
using System.Diagnostics;

namespace MemoryBuffer
{    
    public partial class MemoryBufferState
    {
        #region Constructors
        /// <summary>
        /// Initializes the buffer from the configuration.
        /// </summary>
        public MemoryBufferState(ISystemContext context, MemoryBufferInstance configuration) : base(null)
        {
            Initialize(context);

            string dataType = "UInt32";
            string name = dataType;
            int count = 10;

            if (configuration != null)
            {
                count = configuration.TagCount;

                if (!String.IsNullOrEmpty(configuration.DataType))
                {
                    dataType = configuration.DataType;                
                }

                if (!String.IsNullOrEmpty(configuration.Name))
                {
                    name = dataType;                
                }
            }

            this.SymbolicName = name;
            
            BuiltInType elementType = BuiltInType.UInt32;

            switch (dataType)
            {
                case "Double":
                {
                    elementType = BuiltInType.Double;
                    break;
                }
            }

            CreateBuffer(elementType, count);
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The server that the buffer belongs to.
        /// </summary>
        public IServerInternal Server
        {
            get { return m_server; }
        }

        /// <summary>
        /// The node manager that the buffer belongs to.
        /// </summary>
        public INodeManager NodeManager
        {
            get { return m_nodeManager; }
        }

        /// <summary>
        /// The built-in type for the values stored in the buffer.
        /// </summary>
        public BuiltInType ElementType
        {
            get { return m_elementType; }
        }

        /// <summary>
        /// The size of each element in the buffer.
        /// </summary>
        public uint ElementSize
        {
            get { return (uint)m_elementSize; }
        }

        /// <summary>
        /// The rate at which the buffer is scanned.
        /// </summary>
        public int MaximumScanRate
        {
            get { return m_maximumScanRate; }
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// Initializes the buffer with enough space to hold the specified number of elements.
        /// </summary>
        /// <param name="elementName">The type of element.</param>
        /// <param name="noOfElements">The number of elements.</param>
        public void CreateBuffer(string elementName, int noOfElements)
        {
            if (String.IsNullOrEmpty(elementName))
            {
                elementName = "UInt32";
            }
            
            BuiltInType elementType = BuiltInType.UInt32;

            switch (elementName)
            {
                case "Double":
                {
                    elementType = BuiltInType.Double;
                    break;
                }
            }

            CreateBuffer(elementType, noOfElements);
        }

        /// <summary>
        /// Initializes the buffer with enough space to hold the specified number of elements.
        /// </summary>
        /// <param name="elementType">The type of element.</param>
        /// <param name="noOfElements">The number of elements.</param>
        public void CreateBuffer(BuiltInType elementType, int noOfElements)
        {
            lock (m_dataLock)
            {
                m_elementType = elementType;
                m_elementSize = 1;

                switch (m_elementType)
                {
                    case BuiltInType.UInt32:
                    {
                        m_elementSize = 4;
                        break;
                    }

                    case BuiltInType.Double:
                    {
                        m_elementSize = 8;
                        break;
                    }
                }
                
                m_lastScanTime = DateTime.UtcNow;
                m_maximumScanRate = 1000;

                m_buffer = new byte[m_elementSize * noOfElements];
                SizeInBytes.Value = (uint)m_buffer.Length;
            }
        }

        /// <summary>
        /// Creates an object which can browser the tags in the buffer.
        /// </summary>
        public override INodeBrowser CreateBrowser(
            ISystemContext context, 
            ViewDescription view, 
            NodeId referenceType, 
            bool includeSubtypes, 
            BrowseDirection browseDirection, 
            QualifiedName browseName,
            IEnumerable<IReference> additionalReferences,
            bool internalOnly)
        {
            NodeBrowser browser = new MemoryBufferBrowser(
                context,
                view,
                referenceType,
                includeSubtypes,
                browseDirection,
                browseName,
                additionalReferences,
                internalOnly,
                this);

            PopulateBrowser(context, browser);

            return browser;
        }

        /// <summary>
        /// Handles the read operation for an invidual tag.
        /// </summary>
        public ServiceResult ReadTagValue(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            MemoryTagState tag = node as MemoryTagState;

            if (tag == null)
            {
                return StatusCodes.BadNodeIdUnknown;
            }

            if (NumericRange.Empty != indexRange)
            {
                return StatusCodes.BadIndexRangeInvalid;
            }

            if (!QualifiedName.IsNull(dataEncoding))
            {
                return StatusCodes.BadDataEncodingInvalid;
            }

            int offset = (int)tag.Offset;

            lock (m_dataLock)
            {
                if (offset < 0 || offset >= m_buffer.Length)
                {
                    return StatusCodes.BadNodeIdUnknown;
                }

                if (m_buffer == null)
                {
                    return StatusCodes.BadOutOfService;
                }

                value = GetValueAtOffset(offset).Value;
            }

            statusCode = StatusCodes.Good;
            timestamp = m_lastScanTime;

            return ServiceResult.Good;
        }

        /// <summary>
        /// Handles a write operation for an individual tag.
        /// </summary>
        public ServiceResult WriteTagValue(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            MemoryTagState tag = node as MemoryTagState;

            if (tag == null)
            {
                return StatusCodes.BadNodeIdUnknown;
            }

            if (NumericRange.Empty != indexRange)
            {
                return StatusCodes.BadIndexRangeInvalid;
            }

            if (!QualifiedName.IsNull(dataEncoding))
            {
                return StatusCodes.BadDataEncodingInvalid;
            }

            if (statusCode != StatusCodes.Good)
            {
                return StatusCodes.BadWriteNotSupported;
            }

            if (timestamp != DateTime.MinValue)
            {
                return StatusCodes.BadWriteNotSupported;
            }

            bool changed = false;
            int offset = (int)tag.Offset;

            lock (m_dataLock)
            {
                if (offset < 0 || offset >= m_buffer.Length)
                {
                    return StatusCodes.BadNodeIdUnknown;
                }

                if (m_buffer == null)
                {
                    return StatusCodes.BadOutOfService;
                }

                byte[] bytes = null;

                switch (m_elementType)
                {
                    case BuiltInType.UInt32:
                    {
                        uint? valueToWrite = value as uint?;

                        if (valueToWrite == null)
                        {
                            return StatusCodes.BadTypeMismatch;
                        }

                        bytes = BitConverter.GetBytes(valueToWrite.Value);
                        break;
                    }

                    case BuiltInType.Double:
                    {
                        double? valueToWrite = value as double?;

                        if (valueToWrite == null)
                        {
                            return StatusCodes.BadTypeMismatch;
                        }

                        bytes = BitConverter.GetBytes(valueToWrite.Value);
                        break;
                    }

                    default:
                    {
                        return StatusCodes.BadNodeIdUnknown;
                    }
                }

                for (int ii = 0; ii < bytes.Length; ii++)
                {
                    if (!changed)
                    {
                        if (m_buffer[offset + ii] != bytes[ii])
                        {
                            changed = true;
                        }
                    }

                    m_buffer[offset + ii] = bytes[ii];
                }
            }

            if (changed)
            {
                OnBufferChanged(offset);
            }

            return ServiceResult.Good;
        }

        /// <summary>
        /// Returns the value at the specified offset.
        /// </summary>
        public Variant GetValueAtOffset(int offset)
        {
            lock (m_dataLock)
            {
                if (offset < 0 || offset >= m_buffer.Length)
                {
                    return Variant.Null;
                }

                if (m_buffer == null)
                {
                    return Variant.Null;
                }

                switch (m_elementType)
                {
                    case BuiltInType.UInt32:
                    {
                        return new Variant(BitConverter.ToUInt32(m_buffer, offset));
                    }

                    case BuiltInType.Double:
                    {
                        return new Variant(BitConverter.ToDouble(m_buffer, offset));
                    }
                }

                return Variant.Null;
            }
        }
        #endregion

        #region Monitoring Support Functions
        /// <summary>
        /// Initializes the instance with the context for the node being monitored.
        /// </summary>
        public void InitializeMonitoring(
            IServerInternal server,
            INodeManager nodeManager)
        {
            lock (m_dataLock)
            {
                m_server = server;
                m_nodeManager = nodeManager;
                m_nonValueMonitoredItems = new Dictionary<uint, MemoryBufferMonitoredItem>();
            }
        }

        /// <summary>
        /// Creates a new data change monitored item.
        /// </summary>
        public MemoryBufferMonitoredItem CreateDataChangeItem(
            ServerSystemContext context,
            MemoryTagState      tag,
            uint                subscriptionId,
            uint                monitoredItemId,
            ReadValueId         itemToMonitor,
            DiagnosticsMasks    diagnosticsMasks,
            TimestampsToReturn  timestampsToReturn,
            MonitoringMode      monitoringMode,
            uint                clientHandle,
            double              samplingInterval)

            /*
            ISystemContext context,
            MemoryTagState tag,
            uint monitoredItemId,
            uint attributeId,
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            MonitoringMode monitoringMode,
            uint clientHandle,
            double samplingInterval)*/
        {
            lock (m_dataLock)
            {
                MemoryBufferMonitoredItem monitoredItem = new MemoryBufferMonitoredItem(
                    m_server,
                    m_nodeManager,
                    this,
                    tag.Offset,
                    0,
                    monitoredItemId,
                    context.OperationContext.Session,
                    itemToMonitor,
                    diagnosticsMasks,
                    timestampsToReturn,
                    monitoringMode,
                    clientHandle,
                    null,
                    null,
                    null,
                    samplingInterval,
                    0,
                    false,
                    0);

                /*
                MemoryBufferMonitoredItem monitoredItem = new MemoryBufferMonitoredItem(
                    this,
                    monitoredItemId,
                    tag.Offset,
                    attributeId,
                    diagnosticsMasks,
                    timestampsToReturn,
                    monitoringMode,
                    clientHandle,
                    samplingInterval);
                */

                if (itemToMonitor.AttributeId != Attributes.Value)
                {
                    m_nonValueMonitoredItems.Add(monitoredItem.Id, monitoredItem);
                    return monitoredItem;
                }

                int elementCount = (int)(SizeInBytes.Value / ElementSize);

                if (m_monitoringTable == null)
                {
                    m_monitoringTable = new MemoryBufferMonitoredItem[elementCount][];
                    m_scanTimer = new Timer(DoScan, null, 100, 100);
                }

                int elementOffet = (int)(tag.Offset / ElementSize);

                MemoryBufferMonitoredItem[] monitoredItems = m_monitoringTable[elementOffet];

                if (monitoredItems == null)
                {
                    monitoredItems = new MemoryBufferMonitoredItem[1];
                }
                else
                {
                    monitoredItems = new MemoryBufferMonitoredItem[monitoredItems.Length + 1];
                    m_monitoringTable[elementOffet].CopyTo(monitoredItems, 0);
                }

                monitoredItems[monitoredItems.Length - 1] = monitoredItem;
                m_monitoringTable[elementOffet] = monitoredItems;
                m_itemCount++;

                return monitoredItem;
            }
        }

        /// <summary>
        /// Scans the buffer and updates every other element.
        /// </summary>
        void DoScan(object state)
        {
            DateTime start1 = DateTime.UtcNow;

            lock (m_dataLock)
            {
                for (int ii = 0; ii < m_buffer.Length; ii += m_elementSize)
                {
                    m_buffer[ii]++;

                    // notify any monitored items that the value has changed.
                    OnBufferChanged(ii);
                }

                m_lastScanTime = DateTime.UtcNow;
            }

            DateTime end1 = DateTime.UtcNow;
            
            double delta1 = ((double)(end1.Ticks-start1.Ticks))/TimeSpan.TicksPerMillisecond;

            if (delta1 > 100)
            {
                Debug.WriteLine("SAMPLING DELAY ({0}ms)", delta1);
            }
        }

        /// <summary>
        /// Deletes the monitored item.
        /// </summary>
        public void DeleteItem(MemoryBufferMonitoredItem monitoredItem)
        {
            lock (m_dataLock)
            {
                if (monitoredItem.AttributeId != Attributes.Value)
                {
                    m_nonValueMonitoredItems.Remove(monitoredItem.Id);
                    return;
                }

                if (m_monitoringTable != null)
                {
                    int elementOffet = (int)(monitoredItem.Offset / ElementSize);

                    MemoryBufferMonitoredItem[] monitoredItems = m_monitoringTable[elementOffet];

                    if (monitoredItems != null)
                    {
                        int index = -1;

                        for (int ii = 0; ii < monitoredItems.Length; ii++)
                        {
                            if (Object.ReferenceEquals(monitoredItems[ii], monitoredItem))
                            {
                                index = ii;
                                break;
                            }
                        }

                        if (index >= 0)
                        {
                            m_itemCount--;

                            if (monitoredItems.Length == 1)
                            {
                                monitoredItems = null;
                            }
                            else
                            {
                                monitoredItems = new MemoryBufferMonitoredItem[monitoredItems.Length - 1];

                                Array.Copy(m_monitoringTable[elementOffet], 0, monitoredItems, 0, index);
                                Array.Copy(m_monitoringTable[elementOffet], index + 1, monitoredItems, index, monitoredItems.Length - index);
                            }

                            m_monitoringTable[elementOffet] = monitoredItems;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Handles change events raised by the node.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="state">The node that raised the event.</param>
        /// <param name="masks">What caused the event to be raised</param>
        public void OnBufferChanged(int offset)
        {
            lock (m_dataLock)
            {
                if (m_monitoringTable != null)
                {
                    int elementOffet = (int)(offset / ElementSize);

                    MemoryBufferMonitoredItem[] monitoredItems = m_monitoringTable[elementOffet];

                    if (monitoredItems != null)
                    {
                        DataValue value = new DataValue();

                        value.WrappedValue = GetValueAtOffset(offset);
                        value.StatusCode = StatusCodes.Good;
                        value.ServerTimestamp = DateTime.UtcNow;
                        value.SourceTimestamp = m_lastScanTime;

                        for (int ii = 0; ii < monitoredItems.Length; ii++)
                        {
                            monitoredItems[ii].QueueValue(value, null);
                            m_updateCount++;
                        }
                    }
                }
            }
        }
        
        void ScanTimer_Tick(object sender, EventArgs e)
        {
            DoScan(null);
        }
        
        void PublishTimer_Tick(object sender, EventArgs e)
        {
            DateTime start1 = DateTime.UtcNow;

            lock (m_dataLock)
            {
                if (m_itemCount > 0 && m_updateCount < m_itemCount)
                {
                    Debug.WriteLine("{0:HH:mm:ss.fff} MEMORYBUFFER Reported  {1}/{2} items ***.", DateTime.Now, m_updateCount, m_itemCount);
                }

                m_updateCount = 0;
            }

            DateTime end1 = DateTime.UtcNow;
            
            double delta1 = ((double)(end1.Ticks-start1.Ticks))/TimeSpan.TicksPerMillisecond;

            if (delta1 > 100)
            {
                Debug.WriteLine("****** PUBLISH DELAY ({0}ms) ******", delta1);
            }
        }
        #endregion

        #region Private Fields
        private object m_dataLock = new object();
        private IServerInternal m_server;
        private INodeManager m_nodeManager;
        private MemoryBufferMonitoredItem[][] m_monitoringTable;
        private Dictionary<uint,MemoryBufferMonitoredItem> m_nonValueMonitoredItems;
        private BuiltInType m_elementType;
        private int m_elementSize;
        private DateTime m_lastScanTime;
        private int m_maximumScanRate;
        private byte[] m_buffer;
        private Timer m_scanTimer;
        private int m_updateCount;
        private int m_itemCount;
        #endregion
    }
}
