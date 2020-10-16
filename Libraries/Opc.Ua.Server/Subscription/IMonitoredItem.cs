/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Diagnostics;
using System.Xml;
using System.Threading;

namespace Opc.Ua.Server 
{
	/// <summary>
	/// Manages a monitored item created by a client.
	/// </summary>
	public interface IMonitoredItem
    {        
		/// <summary>
		/// The node manager that created the item.
		/// </summary>
        INodeManager NodeManager { get; }

		/// <summary>
		/// The session that owns the monitored item.
		/// </summary>
		Session Session { get; }

		/// <summary>
		/// The identifier for the item that is unique within the server.
		/// </summary>
		uint Id { get; } 
        
		/// <summary>
		/// The identifier for the subscription that is unique within the server.
		/// </summary>
        uint SubscriptionId { get; }

        /// <summary>
        /// The identifier for the client handle assigned to the monitored item.
        /// </summary>
        uint ClientHandle { get; } 

        /// <summary>
        /// The object to call when item is ready to publish.
        /// </summary>
        ISubscription SubscriptionCallback { get; set; } 

		/// <summary>
		/// The handle assigned by the NodeManager.
		/// </summary>
		object ManagerHandle { get; } 

        /// <summary>
        /// A bit mask that indicates what the monitored item is.
        /// </summary>
        /// <remarks>
        /// Predefined bits are defined by the MonitoredItemTypeMasks class.
        /// NodeManagers may use the remaining bits.
        /// </remarks>
        int MonitoredItemType { get; }

        /// <summary>
        /// Checks if the monitored item is ready to publish.
        /// </summary>
        bool IsReadyToPublish { get; }

        /// <summary>
        /// Gets or Sets a value indicating whether the monitored item is ready to trigger the linked items.
        /// </summary>
        bool IsReadyToTrigger { get; set; }

		/// <summary>
		/// Returns the result after creating the monitor item.
		/// </summary>
        ServiceResult GetCreateResult(out MonitoredItemCreateResult result);

		/// <summary>
		/// Returns the result after modifying the monitor item.
		/// </summary>
        ServiceResult GetModifyResult(out MonitoredItemModifyResult result);

        /// <summary>
        /// The monitoring mode specified for the item.
        /// </summary>
        MonitoringMode MonitoringMode { get; }

        /// <summary>
        /// The sampling interval for the item.
        /// </summary>
        double SamplingInterval { get; }
	}
    
	/// <summary>
	/// A monitored item that can be triggered.
	/// </summary>
    public interface ITriggeredMonitoredItem
    {
        /// <summary>
        /// The identifier for the item that is unique within the server.
        /// </summary>
        uint Id { get; } 
        
        /// <summary>
        /// Flags the monitored item as triggered.
        /// </summary>
        /// <returns>True if there is something to publish.</returns>
        bool SetTriggered();
    }

	/// <summary>
	/// Manages a monitored item created by a client.
	/// </summary>
	public interface IEventMonitoredItem : IMonitoredItem
    {
        /// <summary>
        /// Whether the item is monitoring all events produced by the server.
        /// </summary>
        bool MonitoringAllEvents { get; }

        /// <summary>
        /// Adds an event to the queue.
        /// </summary>
        void QueueEvent(IFilterTarget instance);
        
		/// <summary>
		/// The filter used by the monitored item.
		/// </summary>
		EventFilter EventFilter { get; }

		/// <summary>
		/// Publishes all available event notifications.
        /// </summary>
        /// <returns>True if the caller should re-queue the item for publishing after the next interval elaspses.</returns>
        bool Publish(OperationContext context, Queue<EventFieldList> notifications);

        /// <summary>
        /// Modifies the attributes for monitored item.
        /// </summary>
        ServiceResult ModifyAttributes(
            DiagnosticsMasks diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            uint clientHandle,
            MonitoringFilter originalFilter,
            MonitoringFilter filterToUse,
            Range range,
            double samplingInterval,
            uint queueSize,
            bool discardOldest);

        /// <summary>
        /// Changes the monitoring mode for the item.
        /// </summary>
        void SetMonitoringMode(MonitoringMode monitoringMode);
	}
    
	/// <summary>
	/// Manages a monitored item created by a client.
	/// </summary>
	public interface IDataChangeMonitoredItem : IMonitoredItem
    {
      	/// <summary>
		/// Updates the queue with a data value or an error.
		/// </summary>
		void QueueValue(DataValue value, ServiceResult error);
        
		/// <summary>
		/// The filter used by the monitored item.
		/// </summary>
		DataChangeFilter DataChangeFilter { get; }

		/// <summary>
		/// Publishes all available data change notifications.
		/// </summary>
        /// <returns>True if the caller should re-queue the item for publishing after the next interval elaspses.</returns>
		bool Publish(
            OperationContext                 context, 
            Queue<MonitoredItemNotification> notifications,
            Queue<DiagnosticInfo> diagnostics);
	}
    
	/// <summary>
	/// Manages a monitored item created by a client.
	/// </summary>
    public interface IDataChangeMonitoredItem2 : IDataChangeMonitoredItem
    {
        /// <summary>
        /// The attribute being monitored.
        /// </summary>
        uint AttributeId { get; }

        /// <summary>
        /// Updates the queue with a data value or an error.
        /// </summary>
        void QueueValue(DataValue value, ServiceResult error, bool ignoreFilters);
    }

    /// <summary>
    /// Manages a monitored item created by a client.
    /// </summary>
    public interface ISampledDataChangeMonitoredItem : IDataChangeMonitoredItem2
    {  
        /// <summary>
        /// The diagnostics mask specified fro the monitored item.
        /// </summary>
        DiagnosticsMasks DiagnosticsMasks { get; }

        /// <summary>
        /// The queue size for the item.
        /// </summary>
        uint QueueSize { get; }

        /// <summary>
        /// The minimum sampling interval for the item.
        /// </summary>
        double MinimumSamplingInterval { get; }
        
        /// <summary>
        /// Used to check whether the item is ready to sample.
        /// </summary>
        bool SamplingIntervalExpired();

        /// <summary>
        /// Returns the parameters that can be used to read the monitored item.
        /// </summary>
        ReadValueId GetReadValueId();

		/// <summary>
		/// Modifies the attributes for monitored item.
		/// </summary>
		ServiceResult ModifyAttributes(
            DiagnosticsMasks   diagnosticsMasks,
            TimestampsToReturn timestampsToReturn,
            uint               clientHandle,
            MonitoringFilter   originalFilter,
            MonitoringFilter   filterToUse,
            Range              range,
            double             samplingInterval,
            uint               queueSize,
            bool               discardOldest);
		
        /// <summary>
		/// Changes the monitoring mode for the item.
		/// </summary>
        void SetMonitoringMode(MonitoringMode monitoringMode);
        
		/// <summary>
		/// Updates the sampling interval for an item.
		/// </summary>
        void SetSamplingInterval(double samplingInterval);
    }

    /// <summary>
    /// Defines constants for the monitored item type.
    /// </summary>
    /// <remarks>
    /// Bits 1-8 are reserved for internal use. NodeManagers may use other bits.
    /// </remarks>
    public static class MonitoredItemTypeMask
    {
        /// <summary>
        /// The monitored item subscribes to data changes.
        /// </summary>
        public const int DataChange = 0x1;

        /// <summary>
        /// The monitored item subscribes to events.
        /// </summary>
        public const int Events = 0x2;

        /// <summary>
        /// The monitored item subscribes to all events produced by the server.
        /// </summary>
        /// <remarks>
        /// If this bit is set the Events bit must be set too.
        /// </remarks>
        public const int AllEvents = 0x4;
    }
}
