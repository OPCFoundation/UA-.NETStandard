// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Server;
using Quickstarts.Servers;

namespace MemoryBuffer
{
#pragma warning disable CA1001 // Using timers that are disposed in OnAfterDelete
    public partial class MemoryBufferState
    {
#pragma warning restore CA1001 // Using timers that are disposed in OnAfterDelete
        /// <summary>
        /// Initializes the buffer from the configuration.
        /// </summary>
        public MemoryBufferState(
            ISystemContext context,
            MemoryBufferInstance? configuration)
            : base(null)
        {
            m_logger = context.Telemetry.CreateLogger<MemoryBufferState>();

            Initialize(context);

            string dataType = "UInt32";
            string name = dataType;
            int count = 10;

            if (configuration != null)
            {
                count = configuration.TagCount;

                if (!string.IsNullOrEmpty(configuration.DataType))
                {
                    dataType = configuration.DataType;
                }

                if (!string.IsNullOrEmpty(configuration.Name))
                {
                    name = dataType;
                }
            }

            SymbolicName = name;

            BuiltInType elementType = dataType != "Double" ? BuiltInType.UInt32 : BuiltInType.Double;

            CreateBuffer(elementType, count);
        }

        /// <summary>
        /// The server that the buffer belongs to.
        /// </summary>
        public IServerInternal Server { get; private set; } = null!;

        /// <summary>
        /// The built-in type for the values stored in the buffer.
        /// </summary>
        public BuiltInType ElementType { get; private set; }

        /// <summary>
        /// The size of each element in the buffer.
        /// </summary>
        public uint ElementSize => (uint)m_elementSize;

        /// <summary>
        /// The rate at which the buffer is scanned.
        /// </summary>
        public int MaximumScanRate { get; private set; }

        /// <inheritdoc/>
        protected override void OnAfterDelete(ISystemContext context)
        {
            base.OnAfterDelete(context);
            StopScanning();
        }

        /// <summary>
        /// Stops the buffer scan.
        /// </summary>
        public void StopScanning()
        {
            lock (m_dataLock)
            {
                m_scanTimer?.Dispose();
                m_scanTimer = null;
            }
        }

        /// <summary>
        /// Initializes the buffer with enough space to hold the specified number of elements.
        /// </summary>
        /// <param name="elementName">The type of element.</param>
        /// <param name="noOfElements">The number of elements.</param>
        public void CreateBuffer(string elementName, int noOfElements)
        {
            if (string.IsNullOrEmpty(elementName))
            {
                elementName = "UInt32";
            }

            BuiltInType elementType = elementName != "Double" ? BuiltInType.UInt32 : BuiltInType.Double;

            CreateBuffer(elementType, noOfElements);
        }

        /// <summary>
        /// Initializes the buffer with enough space to hold the specified number of elements.
        /// </summary>
        /// <param name="elementType">The type of element.</param>
        /// <param name="noOfElements">The number of elements.</param>
        /// <exception cref="ServiceResultException"></exception>
        public void CreateBuffer(BuiltInType elementType, int noOfElements)
        {
            lock (m_dataLock)
            {
                ElementType = elementType;
                switch (ElementType)
                {
                    case BuiltInType.UInt32:
                        m_elementSize = 4;
                        break;
                    case BuiltInType.Double:
                        m_elementSize = 8;
                        break;
                    case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                        m_elementSize = 1;
                        break;
                    default:
                        throw ServiceResultException.Unexpected($"Unexpected BuiltInType {ElementType}");
                }

                m_lastScanTime = DateTimeUtc.Now;
                MaximumScanRate = 1000;

                m_buffer = new byte[m_elementSize * noOfElements];
                SizeInBytes!.Value = (uint)m_buffer.Length;
            }
        }

        /// <summary>
        /// Creates an object which can browse the tags in the buffer.
        /// </summary>
        public override INodeBrowser CreateBrowser(
            ISystemContext context,
            ViewDescription? view,
            NodeId referenceType,
            bool includeSubtypes,
            BrowseDirection browseDirection,
            QualifiedName browseName,
            IEnumerable<IReference>? additionalReferences,
            bool internalOnly)
        {
            NodeBrowser browser = new MemoryBufferBrowser(
                context,
                view!,
                referenceType,
                includeSubtypes,
                browseDirection,
                browseName,
                additionalReferences!,
                internalOnly,
                this);

            PopulateBrowserSynchronized(context, browser);

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
            ref Variant value,
            ref StatusCode statusCode,
            ref DateTimeUtc timestamp)
        {
            if (node is not MemoryTagState tag)
            {
                return StatusCodes.BadNodeIdUnknown;
            }

            if (!indexRange.IsNull)
            {
                return StatusCodes.BadIndexRangeInvalid;
            }

            if (!dataEncoding.IsNull)
            {
                return StatusCodes.BadDataEncodingUnsupported;
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

                value = GetValueAtOffset(offset);
            }

            statusCode = StatusCodes.Good;
            timestamp = m_lastScanTime;

            return ServiceResult.Good;
        }

        /// <summary>
        /// Handles a write operation for an individual tag.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public ServiceResult WriteTagValue(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref Variant value,
            ref StatusCode statusCode,
            ref DateTimeUtc timestamp)
        {
            if (node is not MemoryTagState tag)
            {
                return StatusCodes.BadNodeIdUnknown;
            }

            if (!indexRange.IsNull)
            {
                return StatusCodes.BadIndexRangeInvalid;
            }

            if (!dataEncoding.IsNull)
            {
                return StatusCodes.BadDataEncodingUnsupported;
            }

            if (statusCode != StatusCodes.Good)
            {
                return StatusCodes.BadWriteNotSupported;
            }

            if (timestamp != DateTimeUtc.MinValue)
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

                byte[] bytes;

                switch (ElementType)
                {
                    case BuiltInType.UInt32:
                    {
                        if (!value.TryGetValue(out uint valueToWrite))
                        {
                            return StatusCodes.BadTypeMismatch;
                        }

                        bytes = BitConverter.GetBytes(valueToWrite);
                        break;
                    }
                    case BuiltInType.Double:
                    {
                        if (!value.TryGetValue(out double valueToWrite))
                        {
                            return StatusCodes.BadTypeMismatch;
                        }

                        bytes = BitConverter.GetBytes(valueToWrite);
                        break;
                    }
                    case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                        return StatusCodes.BadNodeIdUnknown;
                    default:
                        throw ServiceResultException.Unexpected($"Unexpected BuiltInType {ElementType}");
                }

                for (int ii = 0; ii < bytes.Length; ii++)
                {
                    if (!changed && m_buffer[offset + ii] != bytes[ii])
                    {
                        changed = true;
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
        /// <exception cref="ServiceResultException"></exception>
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

                switch (ElementType)
                {
                    case BuiltInType.UInt32:
                        return new Variant(BitConverter.ToUInt32(m_buffer, offset));
                    case BuiltInType.Double:
                        return new Variant(BitConverter.ToDouble(m_buffer, offset));
                    case >= BuiltInType.Null and <= BuiltInType.Enumeration:
                        return Variant.Null;
                    default:
                        throw ServiceResultException.Unexpected($"Bad element type {ElementType}");
                }
            }
        }

        /// <summary>
        /// Initializes the instance with the server that owns the buffer.
        /// </summary>
        public void InitializeMonitoring(IServerInternal server)
        {
            lock (m_dataLock)
            {
                Server = server;
            }
        }

        /// <summary>
        /// Reads the current value of the element at the specified offset.
        /// </summary>
        public DataValue ReadDataValue(uint offset)
        {
            lock (m_dataLock)
            {
                return new DataValue(
                    GetValueAtOffset((int)offset),
                    StatusCodes.Good,
                    m_lastScanTime,
                    DateTimeUtc.Now);
            }
        }

        /// <summary>
        /// Adds a value monitored item for the element at the specified offset
        /// to the buffer scan.
        /// </summary>
        public void AddMonitoredItem(ISampledDataChangeMonitoredItem monitoredItem, uint offset)
        {
            lock (m_dataLock)
            {
                int elementCount = (int)(SizeInBytes!.Value / ElementSize);

                if (m_monitoringTable == null)
                {
                    m_monitoringTable = new ISampledDataChangeMonitoredItem[elementCount][];
                    TimeProvider timeProvider = (Server as ITimeProviderProvider)?.TimeProvider
                        ?? TimeProvider.System;
                    m_scanTimer?.Dispose();
                    m_scanTimer = timeProvider.CreateTimer(
                        DoScan,
                        null,
                        TimeSpan.FromMilliseconds(100),
                        TimeSpan.FromMilliseconds(100));
                }

                int elementOffet = (int)(offset / ElementSize);

                ISampledDataChangeMonitoredItem[]? monitoredItems = m_monitoringTable[elementOffet];

                if (monitoredItems == null)
                {
                    monitoredItems = new ISampledDataChangeMonitoredItem[1];
                }
                else
                {
                    if (Array.IndexOf(monitoredItems, monitoredItem) >= 0)
                    {
                        return;
                    }

                    monitoredItems = new ISampledDataChangeMonitoredItem[monitoredItems.Length + 1];
                    m_monitoringTable[elementOffet]!.CopyTo(monitoredItems, 0);
                }

                monitoredItems[^1] = monitoredItem;
                m_monitoringTable[elementOffet] = monitoredItems;
                m_itemCount++;
            }
        }

        /// <summary>
        /// Scans the buffer and updates every other element.
        /// </summary>
        private void DoScan(object? state)
        {
            DateTimeUtc start1 = DateTimeUtc.Now;

            lock (m_dataLock)
            {
                for (int ii = 0; ii < m_buffer.Length; ii += m_elementSize)
                {
                    m_buffer[ii]++;

                    // notify any monitored items that the value has changed.
                    OnBufferChanged(ii);
                }

                m_lastScanTime = DateTimeUtc.Now;
            }

            DateTimeUtc end1 = DateTimeUtc.Now;

            double delta1 = (end1 - start1).TotalMilliseconds;

            if (delta1 > 100)
            {
                m_logger.SamplingDelay(nameof(MemoryBufferState), delta1);
            }
        }

        /// <summary>
        /// Removes a value monitored item from the buffer scan.
        /// </summary>
        public void RemoveMonitoredItem(ISampledDataChangeMonitoredItem monitoredItem, uint offset)
        {
            lock (m_dataLock)
            {
                if (m_monitoringTable == null)
                {
                    return;
                }

                int elementOffet = (int)(offset / ElementSize);

                ISampledDataChangeMonitoredItem[]? monitoredItems = m_monitoringTable[elementOffet];

                if (monitoredItems == null)
                {
                    return;
                }

                int index = Array.IndexOf(monitoredItems, monitoredItem);

                if (index < 0)
                {
                    return;
                }

                m_itemCount--;

                if (monitoredItems.Length == 1)
                {
                    m_monitoringTable[elementOffet] = null;
                    return;
                }

                var remaining = new ISampledDataChangeMonitoredItem[monitoredItems.Length - 1];
                Array.Copy(monitoredItems, 0, remaining, 0, index);
                Array.Copy(monitoredItems, index + 1, remaining, index, remaining.Length - index);
                m_monitoringTable[elementOffet] = remaining;
            }
        }

        /// <summary>
        /// Handles change events raised by the node.
        /// </summary>
        public void OnBufferChanged(int offset)
        {
            lock (m_dataLock)
            {
                if (m_monitoringTable != null)
                {
                    int elementOffet = (int)(offset / ElementSize);

                    ISampledDataChangeMonitoredItem[]? monitoredItems = m_monitoringTable[elementOffet];

                    if (monitoredItems != null)
                    {
                        var value = new DataValue(
                            GetValueAtOffset(offset),
                            StatusCodes.Good,
                            m_lastScanTime,
                            DateTimeUtc.Now);

                        for (int ii = 0; ii < monitoredItems.Length; ii++)
                        {
                            monitoredItems[ii].QueueValue(value, null);
                            m_updateCount++;
                        }
                    }
                }
            }
        }

        private void ScanTimer_Tick(object sender, EventArgs e)
        {
            DoScan(null);
        }

        private void PublishTimer_Tick(object sender, EventArgs e)
        {
            DateTimeUtc start1 = DateTimeUtc.Now;

            lock (m_dataLock)
            {
                if (m_itemCount > 0 && m_updateCount < m_itemCount &&
                    m_logger.IsEnabled(LogLevel.Information))
                {
                    m_logger.MemoryBufferReported(
                        DateTimeUtc.Now.ToLocalTime(),
                        m_updateCount,
                        m_itemCount);
                }

                m_updateCount = 0;
            }

            DateTimeUtc end1 = DateTimeUtc.Now;

            double delta1 = (end1 - start1).TotalMilliseconds;

            if (delta1 > 100)
            {
                m_logger.PublishDelay(nameof(MemoryBufferState), delta1);
            }
        }

        private readonly ILogger m_logger;
        private readonly Lock m_dataLock = new();
        private ISampledDataChangeMonitoredItem[]?[]? m_monitoringTable;
        private int m_elementSize;
        private DateTimeUtc m_lastScanTime;
        private byte[] m_buffer = null!;
        private ITimer? m_scanTimer;
        private int m_updateCount;
        private int m_itemCount;
    }

    internal static partial class MemoryBufferStateLog
    {
        [LoggerMessage(
            EventId = QuickstartsServersEventIds.MemoryBufferState + 0, Level = LogLevel.Warning,
            Message = "{StateName} SAMPLING DELAY ({Delta}ms)")]
        public static partial void SamplingDelay(this ILogger logger, string stateName, double delta);

        [LoggerMessage(
            EventId = QuickstartsServersEventIds.MemoryBufferState + 1, Level = LogLevel.Information,
            Message = "{Now:HH:mm:ss.fff} MEMORYBUFFER Reported  {UpdateCount}/{ItemCount} items ***.")]
        public static partial void MemoryBufferReported(
            this ILogger logger,
            DateTime now,
            int updateCount,
            int itemCount);

        [LoggerMessage(
            EventId = QuickstartsServersEventIds.MemoryBufferState + 2, Level = LogLevel.Information,
            Message = "{StateName} ****** PUBLISH DELAY ({Delta}ms) ******")]
        public static partial void PublishDelay(this ILogger logger, string stateName, double delta);
    }

}
