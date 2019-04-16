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
using System.Diagnostics;
using System.Xml;
using System.IO;
using System.Threading;
using System.Reflection;
using System.Data;
using Opc.Ua;
using Opc.Ua.Server;

namespace Quickstarts.HistoricalEvents.Server
{
    /// <summary>
    /// A node manager for a server that exposes several variables.
    /// </summary>
    public class HistoricalEventsNodeManager : QuickstartNodeManager
    {
        #region Constructors
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public HistoricalEventsNodeManager(IServerInternal server, ApplicationConfiguration configuration)
        :
            base(server, configuration)
        {
            SystemContext.NodeIdFactory = this;

            // set one namespace for the type model and one names for dynamically created nodes.
            string[] namespaceUrls = new string[1];
            namespaceUrls[0] = Namespaces.HistoricalEvents;
            SetNamespaces(namespaceUrls);

            // get the configuration for the node manager.
            m_configuration = configuration.ParseExtension<HistoricalEventsServerConfiguration>();

            // use suitable defaults if no configuration exists.
            if (m_configuration == null)
            {
                m_configuration = new HistoricalEventsServerConfiguration();
            }

            // initilize the report generator.
            m_generator = new ReportGenerator();
            m_generator.Initialize();
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
                if (m_simulationTimer != null)
                {
                    Utils.SilentDispose(m_simulationTimer);
                    m_simulationTimer = null;
                }
            }
        }
        #endregion

        #region INodeIdFactory Members
        /// <summary>
        /// Creates the NodeId for the specified node.
        /// </summary>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            return node.NodeId;
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Loads a node set from a file or resource and addes them to the set of predefined nodes.
        /// </summary>
        protected override NodeStateCollection LoadPredefinedNodes(ISystemContext context)
        {
            NodeStateCollection predefinedNodes = new NodeStateCollection();
            predefinedNodes.LoadFromBinaryResource(context, 
                "Quickstarts.HistoricalEvents.Server.Model.Quickstarts.HistoricalEvents.PredefinedNodes.uanodes",
                typeof(HistoricalEventsNodeManager).GetTypeInfo().Assembly,
                true);
            return predefinedNodes;
        }
        #endregion

        #region INodeManager Members
        /// <summary>
        /// Does any initialization required before the address space can be used.
        /// </summary>
        /// <remarks>
        /// The externalReferences is an out parameter that allows the node manager to link to nodes
        /// in other node managers. For example, the 'Objects' node is managed by the CoreNodeManager and
        /// should have a reference to the root folder node(s) exposed by this node manager.  
        /// </remarks>
        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            lock (Lock)
            {
                LoadPredefinedNodes(SystemContext, externalReferences);

                BaseObjectState platforms = (BaseObjectState)FindPredefinedNode(new NodeId(Objects.Plaforms, NamespaceIndex), typeof(BaseObjectState));
                platforms.EventNotifier = EventNotifiers.SubscribeToEvents | EventNotifiers.HistoryRead | EventNotifiers.HistoryWrite;
                base.AddRootNotifier(platforms);

                foreach (string areaName in m_generator.GetAreas())
                {
                    BaseObjectState area = CreateArea(SystemContext, platforms, areaName);

                    foreach (ReportGenerator.WellInfo well in m_generator.GetWells(areaName))
                    {
                        CreateWell(SystemContext, area, well.Id, well.Name);
                    }
                }
                
                // start the simulation.
                m_simulationTimer = new Timer(this.DoSimulation, null, 10000, 10000);
            }
        }

        /// <summary>
        /// Creates a new area.
        /// </summary>
        private BaseObjectState CreateArea(SystemContext context, BaseObjectState platforms, string areaName)
        {
            FolderState area = new FolderState(null);

            area.NodeId = new NodeId(areaName, NamespaceIndex);
            area.BrowseName = new QualifiedName(areaName, NamespaceIndex);
            area.DisplayName = area.BrowseName.Name;
            area.EventNotifier = EventNotifiers.SubscribeToEvents | EventNotifiers.HistoryRead | EventNotifiers.HistoryWrite;
            area.TypeDefinitionId = Opc.Ua.ObjectTypeIds.FolderType;

            platforms.AddNotifier(SystemContext, Opc.Ua.ReferenceTypeIds.HasNotifier, false, area);
            area.AddNotifier(SystemContext, Opc.Ua.ReferenceTypeIds.HasNotifier, true, platforms);

            AddPredefinedNode(SystemContext, area);

            return area;
        }

        /// <summary>
        /// Creates a new well.
        /// </summary>
        private void CreateWell(SystemContext context, BaseObjectState area, string wellId, string wellName)
        {
            WellState well = new WellState(null);

            well.NodeId = new NodeId(wellId, NamespaceIndex);
            well.BrowseName = new QualifiedName(wellName, NamespaceIndex);
            well.DisplayName = wellName;
            well.EventNotifier = EventNotifiers.SubscribeToEvents | EventNotifiers.HistoryRead | EventNotifiers.HistoryWrite;
            well.TypeDefinitionId = new NodeId(ObjectTypes.WellType, NamespaceIndex);

            area.AddNotifier(SystemContext, Opc.Ua.ReferenceTypeIds.HasNotifier, false, well);
            well.AddNotifier(SystemContext, Opc.Ua.ReferenceTypeIds.HasNotifier, true, area);

            AddPredefinedNode(SystemContext, well);
        }

        /// <summary>
        /// Frees any resources allocated for the address space.
        /// </summary>
        public override void DeleteAddressSpace()
        {
            lock (Lock)
            {
                base.DeleteAddressSpace();
            }
        }

        /// <summary>
        /// Returns a unique handle for the node.
        /// </summary>
        protected override NodeHandle GetManagerHandle(ServerSystemContext context, NodeId nodeId, IDictionary<NodeId, NodeState> cache)
        {
            lock (Lock)
            {
                // quickly exclude nodes that are not in the namespace.
                if (!IsNodeIdInNamespace(nodeId))
                {
                    return null;
                }

                // check for predefined nodes.
                if (PredefinedNodes != null)
                {
                    NodeState node = null;

                    if (PredefinedNodes.TryGetValue(nodeId, out node))
                    {
                        NodeHandle handle = new NodeHandle();

                        handle.NodeId = nodeId;
                        handle.Validated = true;
                        handle.Node = node;

                        return handle;
                    }
                }
                
                return null;
            }
        }

        /// <summary>
        /// Verifies that the specified node exists.
        /// </summary>
        protected override NodeState ValidateNode(
            ServerSystemContext context,
            NodeHandle handle,
            IDictionary<NodeId, NodeState> cache)
        {
            // not valid if no root.
            if (handle == null)
            {
                return null;
            }

            // check if previously validated.
            if (handle.Validated)
            {
                return handle.Node;
            }
            
            // TBD

            return null;
        }
        #endregion

        #region Historian Functions
        /// <summary>
        /// Reads history events.
        /// </summary>
        protected override void HistoryReadEvents(
            ServerSystemContext context,
            ReadEventDetails details,
            TimestampsToReturn timestampsToReturn,
            IList<HistoryReadValueId> nodesToRead,
            IList<HistoryReadResult> results,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToProcess,
            IDictionary<NodeId, NodeState> cache)
        {
            for (int ii = 0; ii < nodesToProcess.Count; ii++)
            {
                NodeHandle handle = nodesToProcess[ii];
                HistoryReadValueId nodeToRead = nodesToRead[handle.Index];
                HistoryReadResult result = results[handle.Index];

                HistoryReadRequest request = null;

                // load an exising request.
                if (nodeToRead.ContinuationPoint != null)
                {
                    request = LoadContinuationPoint(context, nodeToRead.ContinuationPoint);

                    if (request == null)
                    {
                        errors[handle.Index] = StatusCodes.BadContinuationPointInvalid;
                        continue;
                    }
                }

                // create a new request.
                else
                {
                    request = CreateHistoryReadRequest(
                        context,
                        details,
                        handle,
                        nodeToRead);
                }

                // process events until the max is reached.
                HistoryEvent events = new HistoryEvent();

                while (request.NumValuesPerNode == 0 || events.Events.Count < request.NumValuesPerNode)
                {
                    if (request.Events.Count == 0)
                    {
                        break;
                    }

                    BaseEventState e = null;

                    if (request.TimeFlowsBackward)
                    {
                        e = request.Events.Last.Value;
                        request.Events.RemoveLast();
                    }
                    else
                    {
                        e = request.Events.First.Value;
                        request.Events.RemoveFirst();
                    }

                    events.Events.Add(GetEventFields(request, e));
                }

                errors[handle.Index] = ServiceResult.Good;

                // check if a continuation point is requred.
                if (request.Events.Count > 0)
                {
                    // only set if both end time and start time are specified.
                    if (details.StartTime != DateTime.MinValue && details.EndTime != DateTime.MinValue)
                    {
                        result.ContinuationPoint = SaveContinuationPoint(context, request);
                    }
                }

                // check if no data returned.
                else
                {
                    errors[handle.Index] = StatusCodes.GoodNoData;
                }

                // return the data.
                result.HistoryData = new ExtensionObject(events);
            }
        }

        /// <summary>
        /// Updates or inserts events.
        /// </summary>
        protected override void HistoryUpdateEvents(
            ServerSystemContext context,
            IList<UpdateEventDetails> nodesToUpdate,
            IList<HistoryUpdateResult> results,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToProcess,
            IDictionary<NodeId, NodeState> cache)
        {
            for (int ii = 0; ii < nodesToProcess.Count; ii++)
            {
                NodeHandle handle = nodesToProcess[ii];
                UpdateEventDetails nodeToUpdate = nodesToUpdate[handle.Index];
                HistoryUpdateResult result = results[handle.Index];

                // validate the event filter.
                FilterContext filterContext = new FilterContext(context.NamespaceUris, context.TypeTable, context);
                EventFilter.Result filterResult = nodeToUpdate.Filter.Validate(filterContext);

                if (ServiceResult.IsBad(filterResult.Status))
                {
                    errors[handle.Index] = filterResult.Status;
                    continue;
                }

                // all done.
                errors[handle.Index] = StatusCodes.BadNotImplemented;
            }
        }

        /// <summary>
        /// Deletes history events.
        /// </summary>
        protected override void HistoryDeleteEvents(
            ServerSystemContext context,
            IList<DeleteEventDetails> nodesToUpdate,
            IList<HistoryUpdateResult> results,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToProcess,
            IDictionary<NodeId, NodeState> cache)
        {
            for (int ii = 0; ii < nodesToProcess.Count; ii++)
            {
                NodeHandle handle = nodesToProcess[ii];
                DeleteEventDetails nodeToUpdate = nodesToUpdate[handle.Index];
                HistoryUpdateResult result = results[handle.Index];

                // delete events.
                bool failed = false;

                for (int jj = 0; jj < nodeToUpdate.EventIds.Count; jj++)
                {
                    try
                    {
                        string eventId = new Guid(nodeToUpdate.EventIds[jj]).ToString();

                        if (!m_generator.DeleteEvent(eventId))
                        {
                            result.OperationResults.Add(StatusCodes.BadEventIdUnknown);
                            failed = true;
                            continue;
                        }

                        result.OperationResults.Add(StatusCodes.Good);
                    }
                    catch
                    {
                        result.OperationResults.Add(StatusCodes.BadEventIdUnknown);
                        failed = true;
                    }
                }

                // check if diagnostics are required.
                if (failed)
                {
                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        for (int jj = 0; jj < nodeToUpdate.EventIds.Count; jj++)
                        {
                            if (StatusCode.IsBad(result.OperationResults[jj]))
                            {
                                result.DiagnosticInfos.Add(ServerUtils.CreateDiagnosticInfo(Server, context.OperationContext, result.OperationResults[jj]));
                            }
                        }
                    }
                }

                // clear operation results if all good.
                else
                {
                    result.OperationResults.Clear();
                }

                // all done.
                errors[handle.Index] = ServiceResult.Good;
            }
        }

        #region History Helpers
        /// <summary>
        /// Fetches the requested event fields from the event.
        /// </summary>
        private HistoryEventFieldList GetEventFields(HistoryReadRequest request, IFilterTarget instance)
        {
            // fetch the event fields.
            HistoryEventFieldList fields = new HistoryEventFieldList();

            foreach (SimpleAttributeOperand clause in request.Filter.SelectClauses)
            {
                // get the value of the attribute (apply localization).
                object value = instance.GetAttributeValue(
                    request.FilterContext,
                    clause.TypeDefinitionId,
                    clause.BrowsePath,
                    clause.AttributeId,
                    clause.ParsedIndexRange);

                // add the value to the list of event fields.
                if (value != null)
                {
                    // translate any localized text.
                    LocalizedText text = value as LocalizedText;

                    if (text != null)
                    {
                        value = Server.ResourceManager.Translate(request.FilterContext.PreferredLocales, text);
                    }

                    // add value.
                    fields.EventFields.Add(new Variant(value));
                }

                // add a dummy entry for missing values.
                else
                {
                    fields.EventFields.Add(Variant.Null);
                }
            }

            return fields;
        }

        /// <summary>
        /// Creates a new history request.
        /// </summary>
        private HistoryReadRequest CreateHistoryReadRequest(
            ServerSystemContext context,
            ReadEventDetails details,
            NodeHandle handle,
            HistoryReadValueId nodeToRead)
        {
            FilterContext filterContext = new FilterContext(context.NamespaceUris, context.TypeTable, context.PreferredLocales);
            LinkedList<BaseEventState> events = new LinkedList<BaseEventState>();

            for (ReportType ii = ReportType.FluidLevelTest; ii <= ReportType.InjectionTest; ii++)
            {
                DataView view = null;

                if (handle.Node is WellState)
                {
                    view = m_generator.ReadHistoryForWellId(
                        ii,
                        (string)handle.Node.NodeId.Identifier,
                        details.StartTime,
                        details.EndTime);
                }
                else
                {
                    view = m_generator.ReadHistoryForArea(
                        ii,
                        handle.Node.NodeId.Identifier as string,
                        details.StartTime,
                        details.EndTime);
                }

                LinkedListNode<BaseEventState> pos = events.First;
                bool sizeLimited = (details.StartTime == DateTime.MinValue || details.EndTime == DateTime.MinValue);

                foreach (DataRowView row in view)
                {
                    // check if reached max results.
                    if (sizeLimited)
                    {
                        if (events.Count >= details.NumValuesPerNode)
                        {
                            break;
                        }
                    }

                    BaseEventState e = m_generator.GetReport(context, NamespaceIndex, ii, row.Row);

                    if (details.Filter.WhereClause != null && details.Filter.WhereClause.Elements.Count > 0)
                    {
                        if (!details.Filter.WhereClause.Evaluate(filterContext, e))
                        {
                            continue;
                        }
                    }

                    bool inserted = false;

                    for (LinkedListNode<BaseEventState> jj = pos; jj != null; jj = jj.Next)
                    {
                        if (jj.Value.Time.Value > e.Time.Value)
                        {
                            events.AddBefore(jj, e);
                            pos = jj;
                            inserted = true;
                            break;
                        }
                    }

                    if (!inserted)
                    {
                        events.AddLast(e);
                        pos = null;
                    }
                }
            }

            HistoryReadRequest request = new HistoryReadRequest();
            request.Events = events;
            request.TimeFlowsBackward = details.StartTime == DateTime.MinValue || (details.EndTime != DateTime.MinValue && details.EndTime < details.StartTime);
            request.NumValuesPerNode = details.NumValuesPerNode;
            request.Filter = details.Filter;
            request.FilterContext = filterContext;
            return request;
        }

        /// <summary>
        /// Stores a read history request.
        /// </summary>
        private class HistoryReadRequest
        {
            public byte[] ContinuationPoint;
            public LinkedList<BaseEventState> Events;
            public bool TimeFlowsBackward;
            public uint NumValuesPerNode;
            public EventFilter Filter;
            public FilterContext FilterContext;
        }

        /// <summary>
        /// Releases the history continuation point.
        /// </summary>
        protected override void HistoryReleaseContinuationPoints(
            ServerSystemContext context,
            IList<HistoryReadValueId> nodesToRead,
            IList<ServiceResult> errors,
            List<NodeHandle> nodesToProcess,
            IDictionary<NodeId, NodeState> cache)
        {
            for (int ii = 0; ii < nodesToProcess.Count; ii++)
            {
                NodeHandle handle = nodesToProcess[ii];
                HistoryReadValueId nodeToRead = nodesToRead[handle.Index];

                // find the continuation point.
                HistoryReadRequest request = LoadContinuationPoint(context, nodeToRead.ContinuationPoint);

                if (request == null)
                {
                    errors[handle.Index] = StatusCodes.BadContinuationPointInvalid;
                    continue;
                }

                // all done.
                errors[handle.Index] = StatusCodes.Good;
            }
        }

        /// <summary>
        /// Loads a history continuation point.
        /// </summary>
        private HistoryReadRequest LoadContinuationPoint(
            ServerSystemContext context,
            byte[] continuationPoint)
        {
            Session session = context.OperationContext.Session;

            if (session == null)
            {
                return null;
            }

            HistoryReadRequest request = session.RestoreHistoryContinuationPoint(continuationPoint) as HistoryReadRequest;

            if (request == null)
            {
                return null;
            }

            return request;
        }

        /// <summary>
        /// Saves a history continuation point.
        /// </summary>
        private byte[] SaveContinuationPoint(
            ServerSystemContext context,
            HistoryReadRequest request)
        {
            Session session = context.OperationContext.Session;

            if (session == null)
            {
                return null;
            }

            Guid id = Guid.NewGuid();
            session.SaveHistoryContinuationPoint(id, request);
            request.ContinuationPoint = id.ToByteArray();
            return request.ContinuationPoint;
        }
        #endregion
        #endregion

        #region Private Methods
        /// <summary>
        /// Does the simulation.
        /// </summary>
        /// <param name="state">The state.</param>
        private void DoSimulation(object state)
        {
            try
            {
                {
                    DataRow row = m_generator.GenerateFluidLevelTestReport();
                    BaseObjectState well = (BaseObjectState)FindPredefinedNode(new NodeId((string)row[BrowseNames.UidWell], NamespaceIndex), typeof(BaseObjectState));

                    if (well != null && well.AreEventsMonitored)
                    {
                        BaseEventState e = m_generator.GetFluidLevelTestReport(SystemContext, NamespaceIndex, row);
                        well.ReportEvent(SystemContext, e);
                    }
                }

                {
                    DataRow row = m_generator.GenerateInjectionTestReport();
                    BaseObjectState well = (BaseObjectState)FindPredefinedNode(new NodeId((string)row[BrowseNames.UidWell], NamespaceIndex), typeof(BaseObjectState));

                    if (well != null && well.AreEventsMonitored)
                    {
                        BaseEventState e = m_generator.GetInjectionTestReport(SystemContext, NamespaceIndex, row);
                        well.ReportEvent(SystemContext, e);
                    }
                }
            }
            catch (Exception e)
            {
                Utils.Trace(e, "Unexpected error during simulation.");
            }
        }
        #endregion

        #region Private Fields
        private HistoricalEventsServerConfiguration m_configuration;
        private Timer m_simulationTimer;
        private ReportGenerator m_generator;
        #endregion
    }
}
