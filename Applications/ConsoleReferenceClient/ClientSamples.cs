/* ========================================================================
 * Copyright (c) 2005-2021 The OPC Foundation, Inc. All rights reserved.
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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.ComplexTypes;

namespace Quickstarts
{
    /// <summary>
    /// A client interface which holds an active session.
    /// The client handler may reconnect and the Session
    /// property may be updated during operation.
    /// </summary>
    public interface IUAClient
    {
        /// <summary>
        /// The session to use.
        /// </summary>
        ISession Session { get; }
    }

    /// <summary>
    /// Sample Session calls based on the reference server node model.
    /// </summary>
    public class ClientSamples
    {
        private const int kMaxSearchDepth = 128;
        public ClientSamples(TextWriter output, Action<IList, IList> validateResponse, ManualResetEvent quitEvent = null, bool verbose = false)
        {
            m_output = output;
            m_validateResponse = validateResponse ?? ClientBase.ValidateResponse;
            m_quitEvent = quitEvent;
            m_verbose = verbose;
            m_desiredEventFields = [];
            int eventIndexCounter = 0;
            m_desiredEventFields.Add(eventIndexCounter++, [.. new QualifiedName[] { BrowseNames.Time }]);
            m_desiredEventFields.Add(eventIndexCounter++, [.. new QualifiedName[] { BrowseNames.ActiveState }]);
            m_desiredEventFields.Add(eventIndexCounter++, [.. new QualifiedName[] { BrowseNames.Message }]);
            m_desiredEventFields.Add(eventIndexCounter++, [.. new QualifiedName[] { BrowseNames.LimitState, BrowseNames.CurrentState }]);
            m_desiredEventFields.Add(eventIndexCounter++, [.. new QualifiedName[] { BrowseNames.LimitState, BrowseNames.LastTransition }]);
        }

        /// <summary>
        /// Read a list of nodes from Server
        /// </summary>
        public void ReadNodes(ISession session)
        {
            if (session == null || !session.Connected)
            {
                m_output.WriteLine("Session not connected!");
                return;
            }

            try
            {
                // build a list of nodes to be read
                var nodesToRead = new ReadValueIdCollection()
                {
                    // Value of ServerStatus
                    new ReadValueId() { NodeId = Variables.Server_ServerStatus, AttributeId = Attributes.Value },
                    // BrowseName of ServerStatus_StartTime
                    new ReadValueId() { NodeId = Variables.Server_ServerStatus_StartTime, AttributeId = Attributes.BrowseName },
                    // Value of ServerStatus_StartTime
                    new ReadValueId() { NodeId = Variables.Server_ServerStatus_StartTime, AttributeId = Attributes.Value }
                };

                // Read the node attributes
                m_output.WriteLine("Reading nodes...");

                // Call Read Service
                session.Read(
                    null,
                    0,
                    TimestampsToReturn.Both,
                    nodesToRead,
                    out DataValueCollection resultsValues,
                    out DiagnosticInfoCollection diagnosticInfos);

                // Validate the results
                m_validateResponse(resultsValues, nodesToRead);

                // Display the results.
                foreach (DataValue result in resultsValues)
                {
                    m_output.WriteLine("Read Value = {0} , StatusCode = {1}", result.Value, result.StatusCode);
                }

                // Read Server NamespaceArray
                m_output.WriteLine("Reading Value of NamespaceArray node...");
                DataValue namespaceArray = session.ReadValue(Variables.Server_NamespaceArray);
                // Display the result
                m_output.WriteLine($"NamespaceArray Value = {namespaceArray}");
            }
            catch (Exception ex)
            {
                // Log Error
                m_output.WriteLine($"Read Nodes Error : {ex.Message}.");
            }
        }

        /// <summary>
        /// Write a list of nodes to the Server.
        /// </summary>
        public void WriteNodes(ISession session)
        {
            if (session == null || !session.Connected)
            {
                m_output.WriteLine("Session not connected!");
                return;
            }

            try
            {
                // Write the configured nodes
                var nodesToWrite = new WriteValueCollection();

                // Int32 Node - Objects\CTT\Scalar\Scalar_Static\Int32
                var intWriteVal = new WriteValue();
                intWriteVal.NodeId = new NodeId("ns=2;s=Scalar_Static_Int32");
                intWriteVal.AttributeId = Attributes.Value;
                intWriteVal.Value = new DataValue();
                intWriteVal.Value.Value = 100;
                nodesToWrite.Add(intWriteVal);

                // Float Node - Objects\CTT\Scalar\Scalar_Static\Float
                var floatWriteVal = new WriteValue();
                floatWriteVal.NodeId = new NodeId("ns=2;s=Scalar_Static_Float");
                floatWriteVal.AttributeId = Attributes.Value;
                floatWriteVal.Value = new DataValue();
                floatWriteVal.Value.Value = (float)100.5;
                nodesToWrite.Add(floatWriteVal);

                // String Node - Objects\CTT\Scalar\Scalar_Static\String
                var stringWriteVal = new WriteValue();
                stringWriteVal.NodeId = new NodeId("ns=2;s=Scalar_Static_String");
                stringWriteVal.AttributeId = Attributes.Value;
                stringWriteVal.Value = new DataValue();
                stringWriteVal.Value.Value = "String Test";
                nodesToWrite.Add(stringWriteVal);

                // Write the node attributes
                StatusCodeCollection results = null;
                DiagnosticInfoCollection diagnosticInfos;
                m_output.WriteLine("Writing nodes...");

                // Call Write Service
                session.Write(null,
                                nodesToWrite,
                                out results,
                                out diagnosticInfos);

                // Validate the response
                m_validateResponse(results, nodesToWrite);

                // Display the results.
                m_output.WriteLine("Write Results :");

                foreach (StatusCode writeResult in results)
                {
                    m_output.WriteLine("     {0}", writeResult);
                }
            }
            catch (Exception ex)
            {
                // Log Error
                m_output.WriteLine($"Write Nodes Error : {ex.Message}.");
            }
        }

        /// <summary>
        /// Browse Server nodes
        /// </summary>
        public void Browse(ISession session)
        {
            if (session == null || !session.Connected)
            {
                m_output.WriteLine("Session not connected!");
                return;
            }

            try
            {
                // Create a Browser object
                var browser = new Browser(session);

                // Set browse parameters
                browser.BrowseDirection = BrowseDirection.Forward;
                browser.NodeClassMask = (int)NodeClass.Object | (int)NodeClass.Variable;
                browser.ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences;
                browser.IncludeSubtypes = true;

                NodeId nodeToBrowse = ObjectIds.Server;

                // Call Browse service
                m_output.WriteLine("Browsing {0} node...", nodeToBrowse);
                ReferenceDescriptionCollection browseResults = browser.Browse(nodeToBrowse);

                // Display the results
                m_output.WriteLine("Browse returned {0} results:", browseResults.Count);

                foreach (ReferenceDescription result in browseResults)
                {
                    m_output.WriteLine("     DisplayName = {0}, NodeClass = {1}", result.DisplayName.Text, result.NodeClass);
                }
            }
            catch (Exception ex)
            {
                // Log Error
                m_output.WriteLine($"Browse Error : {ex.Message}.");
            }
        }

        /// <summary>
        /// Call UA method
        /// </summary>
        public void CallMethod(ISession session)
        {
            if (session == null || !session.Connected)
            {
                m_output.WriteLine("Session not connected!");
                return;
            }

            try
            {
                // Define the UA Method to call
                // Parent node - Objects\CTT\Methods
                // Method node - Objects\CTT\Methods\Add
                var objectId = new NodeId("ns=2;s=Methods");
                var methodId = new NodeId("ns=2;s=Methods_Add");

                // Define the method parameters
                // Input argument requires a Float and an UInt32 value
                object[] inputArguments = [(float)10.5, (uint)10];
                IList<object> outputArguments = null;

                // Invoke Call service
                m_output.WriteLine("Calling UAMethod for node {0} ...", methodId);
                outputArguments = session.Call(objectId, methodId, inputArguments);

                // Display results
                m_output.WriteLine("Method call returned {0} output argument(s):", outputArguments.Count);

                foreach (object outputArgument in outputArguments)
                {
                    m_output.WriteLine("     OutputValue = {0}", outputArgument.ToString());
                }
            }
            catch (Exception ex)
            {
                m_output.WriteLine("Method call error: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Call the Start method for Alarming to enable events
        /// </summary>
        public void EnableEvents(ISession session, uint timeToRun)
        {
            if (session == null || !session.Connected)
            {
                m_output.WriteLine("Session not connected!");
                return;
            }

            try
            {
                // Define the UA Method to call
                // Parent node - Objects\CTT\Alarms
                // Method node - Objects\CTT\Alarms\Start
                var objectId = new NodeId("ns=7;s=Alarms");
                var methodId = new NodeId("ns=7;s=Alarms.Start");

                // Define the method parameters
                // Input argument requires a Float and an UInt32 value
                object[] inputArguments = [timeToRun];
                IList<object> outputArguments = null;

                // Invoke Call service
                m_output.WriteLine("Calling UAMethod for node {0} ...", methodId);
                outputArguments = session.Call(objectId, methodId, inputArguments);

                // Display results
                m_output.WriteLine("Method call returned {0} output argument(s):", outputArguments.Count);

                foreach (object outputArgument in outputArguments)
                {
                    m_output.WriteLine("     OutputValue = {0}", outputArgument.ToString());
                }
            }
            catch (Exception ex)
            {
                m_output.WriteLine("Method call error: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Create Subscription and MonitoredItems for DataChanges
        /// </summary>
        public bool SubscribeToDataChanges(ISession session, uint minLifeTime, bool enableDurableSubscriptions)
        {
            bool isDurable = false;

            if (session == null || !session.Connected)
            {
                m_output.WriteLine("Session not connected!");
                return isDurable;
            }

            try
            {
                // Create a subscription for receiving data change notifications
                const int subscriptionPublishingInterval = 1000;
                const int itemSamplingInterval = 1000;
                uint queueSize = 10;
                uint lifetime = minLifeTime;

                if (enableDurableSubscriptions)
                {
                    queueSize = 100;
                    lifetime = 20;
                }

                // Define Subscription parameters
                var subscription = new Subscription(session.DefaultSubscription) {
                    DisplayName = "Console ReferenceClient Subscription",
                    PublishingEnabled = true,
                    PublishingInterval = subscriptionPublishingInterval,
                    LifetimeCount = 0,
                    MinLifetimeInterval = lifetime,
                    KeepAliveCount = 5,
                };

                session.AddSubscription(subscription);

                // Create the subscription on Server side
                subscription.Create();
                m_output.WriteLine("New Subscription created with SubscriptionId = {0}, Sampling Interval {1}, Publishing Interval {2}.",
                    subscription.Id, itemSamplingInterval, subscriptionPublishingInterval);

                if (enableDurableSubscriptions)
                {
                    uint revisedLifetimeInHours = 0;

                    if (subscription.SetSubscriptionDurable(1, out revisedLifetimeInHours))
                    {
                        isDurable = true;

                        m_output.WriteLine("Subscription {0} is now durable, Revised Lifetime {1} in hours.",
                            subscription.Id, revisedLifetimeInHours);
                    }
                    else
                    {
                        m_output.WriteLine("Subscription {0} failed durable call", subscription.Id);
                    }
                }

                // Create MonitoredItems for data changes (Reference Server)

                var intMonitoredItem = new MonitoredItem(subscription.DefaultItem);
                // Int32 Node - Objects\CTT\Scalar\Simulation\Int32
                intMonitoredItem.StartNodeId = new NodeId("ns=2;s=Scalar_Simulation_Int32");
                intMonitoredItem.AttributeId = Attributes.Value;
                intMonitoredItem.DisplayName = "Int32 Variable";
                intMonitoredItem.SamplingInterval = itemSamplingInterval;
                intMonitoredItem.QueueSize = queueSize;
                intMonitoredItem.DiscardOldest = true;
                intMonitoredItem.Notification += OnMonitoredItemNotification;

                subscription.AddItem(intMonitoredItem);

                var floatMonitoredItem = new MonitoredItem(subscription.DefaultItem);
                // Float Node - Objects\CTT\Scalar\Simulation\Float
                floatMonitoredItem.StartNodeId = new NodeId("ns=2;s=Scalar_Simulation_Float");
                floatMonitoredItem.AttributeId = Attributes.Value;
                floatMonitoredItem.DisplayName = "Float Variable";
                floatMonitoredItem.SamplingInterval = itemSamplingInterval;
                floatMonitoredItem.QueueSize = queueSize;
                floatMonitoredItem.Notification += OnMonitoredItemNotification;

                subscription.AddItem(floatMonitoredItem);

                var stringMonitoredItem = new MonitoredItem(subscription.DefaultItem);
                // String Node - Objects\CTT\Scalar\Simulation\String
                stringMonitoredItem.StartNodeId = new NodeId("ns=2;s=Scalar_Simulation_String");
                stringMonitoredItem.AttributeId = Attributes.Value;
                stringMonitoredItem.DisplayName = "String Variable";
                stringMonitoredItem.SamplingInterval = itemSamplingInterval;
                stringMonitoredItem.QueueSize = queueSize;
                stringMonitoredItem.Notification += OnMonitoredItemNotification;

                subscription.AddItem(stringMonitoredItem);

                var eventMonitoredItem = new MonitoredItem(subscription.DefaultItem);
                eventMonitoredItem.StartNodeId = new NodeId(ObjectIds.Server);
                eventMonitoredItem.AttributeId = Attributes.EventNotifier;
                eventMonitoredItem.DisplayName = "Event Variable";
                eventMonitoredItem.SamplingInterval = itemSamplingInterval;
                eventMonitoredItem.QueueSize = queueSize;
                eventMonitoredItem.Notification += OnMonitoredItemEventNotification;

                var filter = new EventFilter();

                var simpleAttributeOperands = new SimpleAttributeOperandCollection();

                foreach (QualifiedNameCollection desiredEventField in m_desiredEventFields.Values)
                {
                    simpleAttributeOperands.Add(new SimpleAttributeOperand() {
                        AttributeId = Attributes.Value,
                        TypeDefinitionId = ObjectTypeIds.BaseEventType,
                        BrowsePath = desiredEventField
                    });
                }
                filter.SelectClauses = simpleAttributeOperands;

                var whereClause = new ContentFilter();
                var existingEventType = new SimpleAttributeOperand() {
                    AttributeId = Attributes.Value,
                    TypeDefinitionId = ObjectTypeIds.ExclusiveLevelAlarmType,
                    BrowsePath = new QualifiedNameCollection(["EventType"])
                };
                var desiredEventType = new LiteralOperand {
                    Value = new Variant(new NodeId(ObjectTypeIds.ExclusiveLevelAlarmType))
                };

                whereClause.Push(FilterOperator.Equals, [existingEventType, desiredEventType]);

                filter.WhereClause = whereClause;

                eventMonitoredItem.Filter = filter;
                eventMonitoredItem.NodeClass = NodeClass.Object;

                subscription.AddItem(eventMonitoredItem);

                // Create the monitored items on Server side
                subscription.ApplyChanges();
                m_output.WriteLine("MonitoredItems created for SubscriptionId = {0}.", subscription.Id);
            }
            catch (Exception ex)
            {
                m_output.WriteLine("Subscribe error: {0}", ex.Message);
            }

            return isDurable;
        }

        /// <summary>
        /// Fetch all references and nodes with attributes except values from the server.
        /// </summary>
        /// <param name="uaClient">The UAClient with a session to use.</param>
        /// <param name="startingNode">The node from which the hierarchical nodes are fetched.</param>
        /// <param name="fetchTree">Iterate to fetch all nodes in the tree.</param>
        /// <param name="addRootNode">Adds the root node to the result.</param>
        /// <param name="filterUATypes">Filters nodes from namespace 0 from the result.</param>
        /// <returns>The list of nodes on the server.</returns>
        public async Task<IList<INode>> FetchAllNodesNodeCacheAsync(
            IUAClient uaClient,
            NodeId startingNode,
            bool fetchTree = false,
            bool addRootNode = false,
            bool filterUATypes = true,
            bool clearNodeCache = true)
        {
            var stopwatch = new Stopwatch();
            var nodeDictionary = new Dictionary<ExpandedNodeId, INode>();
            var references = new NodeIdCollection { ReferenceTypeIds.HierarchicalReferences };
            var nodesToBrowse = new ExpandedNodeIdCollection {
                    startingNode
                };

            // start
            stopwatch.Start();

            if (clearNodeCache)
            {
                // clear NodeCache to fetch all nodes from server
                uaClient.Session.NodeCache.Clear();
                await FetchReferenceIdTypesAsync(uaClient.Session).ConfigureAwait(false);
            }

            // add root node
            if (addRootNode)
            {
                INode rootNode = await uaClient.Session.NodeCache.FindAsync(startingNode).ConfigureAwait(false);
                nodeDictionary[rootNode.NodeId] = rootNode;
            }

            int searchDepth = 0;
            while (nodesToBrowse.Count > 0 && searchDepth < kMaxSearchDepth)
            {
                if (m_quitEvent?.WaitOne(0) == true)
                {
                    m_output.WriteLine("Browse aborted.");
                    break;
                }

                searchDepth++;
                Utils.LogInfo("{0}: Find {1} references after {2}ms", searchDepth, nodesToBrowse.Count, stopwatch.ElapsedMilliseconds);
                IList<INode> response = await uaClient.Session.NodeCache.FindReferencesAsync(
                    nodesToBrowse,
                    references,
                    false,
                    true).ConfigureAwait(false);

                var nextNodesToBrowse = new ExpandedNodeIdCollection();
                int duplicates = 0;
                int leafNodes = 0;
                foreach (INode node in response)
                {
                    if (!nodeDictionary.ContainsKey(node.NodeId))
                    {
                        if (fetchTree)
                        {
                            bool leafNode = false;

                            // no need to browse property types
                            if (node is VariableNode variableNode)
                            {
                                IReference hasTypeDefinition = variableNode.ReferenceTable.FirstOrDefault(r => r.ReferenceTypeId.Equals(ReferenceTypeIds.HasTypeDefinition));
                                if (hasTypeDefinition != null)
                                {
                                    leafNode = hasTypeDefinition.TargetId == VariableTypeIds.PropertyType;
                                }
                            }

                            if (!leafNode)
                            {
                                nextNodesToBrowse.Add(node.NodeId);
                            }
                            else
                            {
                                leafNodes++;
                            }
                        }

                        if (filterUATypes)
                        {
                            if (node.NodeId.NamespaceIndex != 0)
                            {
                                // filter out default namespace
                                nodeDictionary[node.NodeId] = node;
                            }
                        }
                        else
                        {
                            nodeDictionary[node.NodeId] = node;
                        }
                    }
                    else
                    {
                        duplicates++;
                    }
                }
                if (duplicates > 0)
                {
                    Utils.LogInfo("Find References {0} duplicate nodes were ignored", duplicates);
                }
                if (leafNodes > 0)
                {
                    Utils.LogInfo("Find References {0} leaf nodes were ignored", leafNodes);
                }
                nodesToBrowse = nextNodesToBrowse;
            }

            stopwatch.Stop();

            m_output.WriteLine("FetchAllNodesNodeCache found {0} nodes in {1}ms", nodeDictionary.Count, stopwatch.ElapsedMilliseconds);

            var result = nodeDictionary.Values.ToList();
            result.Sort((x, y) => x.NodeId.CompareTo(y.NodeId));

            if (m_verbose)
            {
                foreach (INode node in result)
                {
                    m_output.WriteLine("NodeId {0} {1} {2}", node.NodeId, node.NodeClass, node.BrowseName);
                }
            }

            return result;
        }

        /// <summary>
        /// Browse full address space using the ManagedBrowseMethod, which
        /// will take care of not sending to many nodes to the server,
        /// calling BrowseNext and dealing with the status codes
        /// BadNoContinuationPoint and BadInvalidContinuationPoint.
        /// </summary>
        /// <param name="uaClient">The UAClient with a session to use.</param>
        /// <param name="startingNode">The node where the browse operation starts.</param>
        /// <param name="browseDescription">An optional BrowseDescription to use.</param>
        public async Task<ReferenceDescriptionCollection> ManagedBrowseFullAddressSpaceAsync(
            IUAClient uaClient,
            NodeId startingNode = null,
            BrowseDescription browseDescription = null,
            CancellationToken ct = default)
        {
            ContinuationPointPolicy policyBackup = uaClient.Session.ContinuationPointPolicy;
            uaClient.Session.ContinuationPointPolicy = ContinuationPointPolicy.Default;

            var stopWatch = new Stopwatch();
            stopWatch.Start();
            BrowseDirection browseDirection = BrowseDirection.Forward;
            NodeId referenceTypeId = ReferenceTypeIds.HierarchicalReferences;
            bool includeSubtypes = true;
            uint nodeClassMask = 0;

            if (browseDescription != null)
            {
                startingNode = browseDescription.NodeId;
                browseDirection = browseDescription.BrowseDirection;
                referenceTypeId = browseDescription.ReferenceTypeId;
                includeSubtypes = browseDescription.IncludeSubtypes;
                nodeClassMask = browseDescription.NodeClassMask;

                if (browseDescription.ResultMask != (uint)BrowseResultMask.All)
                {
                    Utils.LogWarning("Setting the BrowseResultMask is not supported by the " +
                        $"ManagedBrowse method. Using '{BrowseResultMask.All}' instead of " +
                        $"the mask {browseDescription.ResultMask} for the result mask");
                }
            }

            var nodesToBrowse = new List<NodeId> {
                startingNode ?? ObjectIds.RootFolder
            };

            const int kMaxReferencesPerNode = 1000;

            // Browse
            var referenceDescriptions = new Dictionary<ExpandedNodeId, ReferenceDescription>();

            int searchDepth = 0;
            uint maxNodesPerBrowse = uaClient.Session.OperationLimits.MaxNodesPerBrowse;

            var allReferenceDescriptions = new List<ReferenceDescriptionCollection>();
            var newReferenceDescriptions = new List<ReferenceDescriptionCollection>();
            var allServiceResults = new List<ServiceResult>();

            while (nodesToBrowse.Count != 0 && searchDepth < kMaxSearchDepth)
            {
                searchDepth++;
                Utils.LogInfo("{0}: Browse {1} nodes after {2}ms",
                    searchDepth, nodesToBrowse.Count, stopWatch.ElapsedMilliseconds);

                const bool repeatBrowse = false;

                do
                {
                    if (m_quitEvent?.WaitOne(0) == true)
                    {
                        m_output.WriteLine("Browse aborted.");
                        break;
                    }

                    try
                    {
                        // the resultMask defaults to "all"
                        // maybe the API should be extended to
                        // support it. But that will then also be
                        // necessary for BrowseAsync
                        (
                            IList<ReferenceDescriptionCollection> descriptions,
                            IList<ServiceResult> errors
                        ) = await uaClient.Session.ManagedBrowseAsync(
                            null,
                            null,
                            nodesToBrowse,
                            kMaxReferencesPerNode,
                            browseDirection,
                            referenceTypeId,
                            true,
                            nodeClassMask,
                            ct
                            ).ConfigureAwait(false);

                        allReferenceDescriptions.AddRange(descriptions);
                        newReferenceDescriptions.AddRange(descriptions);
                        allServiceResults.AddRange(errors);
                    }
                    catch (ServiceResultException sre)
                    {
                        // the maximum number of nodes per browse is
                        // set in the ManagedBrowse from the configuration
                        // and cannot be influenced from the outside.
                        // if that's desired it would be necessary to provide
                        // an additional parameter to the method.
                        m_output.WriteLine("Browse error: {0}", sre.Message);
                        throw;
                    }
                } while (repeatBrowse);

                // Build browse request for next level
                var nodesForNextManagedBrowse = new List<NodeId>();
                int duplicates = 0;
                foreach (ReferenceDescriptionCollection referenceCollection in newReferenceDescriptions)
                {
                    foreach (ReferenceDescription reference in referenceCollection)
                    {
                        if (!referenceDescriptions.ContainsKey(reference.NodeId))
                        {
                            referenceDescriptions[reference.NodeId] = reference;

                            if (!reference.ReferenceTypeId.Equals(ReferenceTypeIds.HasProperty))
                            {
                                nodesForNextManagedBrowse.Add(ExpandedNodeId.ToNodeId(reference.NodeId, uaClient.Session.NamespaceUris));
                            }
                        }
                        else
                        {
                            duplicates++;
                        }
                    }
                }

                newReferenceDescriptions.Clear();

                nodesToBrowse = nodesForNextManagedBrowse;

                if (duplicates > 0)
                {
                    Utils.LogInfo("Managed Browse Result {0} duplicate nodes were ignored.", duplicates);
                }
            }

            stopWatch.Stop();

            var result = new ReferenceDescriptionCollection(referenceDescriptions.Values);

            result.Sort((x, y) => x.NodeId.CompareTo(y.NodeId));

            m_output.WriteLine("ManagedBrowseFullAddressSpace found {0} references on server in {1}ms.",
                result.Count, stopWatch.ElapsedMilliseconds);

            if (m_verbose)
            {
                foreach (ReferenceDescription reference in result)
                {
                    m_output.WriteLine("NodeId {0} {1} {2}", reference.NodeId, reference.NodeClass, reference.BrowseName);
                }
            }

            uaClient.Session.ContinuationPointPolicy = policyBackup;

            return result;
        }

        /// <summary>
        /// Browse full address space.
        /// </summary>
        /// <param name="uaClient">The UAClient with a session to use.</param>
        /// <param name="startingNode">The node where the browse operation starts.</param>
        /// <param name="browseDescription">An optional BrowseDescription to use.</param>
        public async Task<ReferenceDescriptionCollection> BrowseFullAddressSpaceAsync(
            IUAClient uaClient,
            NodeId startingNode = null,
            BrowseDescription browseDescription = null,
            CancellationToken ct = default)
        {
            var stopWatch = new Stopwatch();
            stopWatch.Start();

            // Browse template
            const int kMaxReferencesPerNode = 1000;
            BrowseDescription browseTemplate = browseDescription ?? new BrowseDescription {
                NodeId = startingNode ?? ObjectIds.RootFolder,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                IncludeSubtypes = true,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All
            };
            BrowseDescriptionCollection browseDescriptionCollection = CreateBrowseDescriptionCollectionFromNodeId(
                [.. new NodeId[] { startingNode ?? ObjectIds.RootFolder }],
                browseTemplate);

            // Browse
            var referenceDescriptions = new Dictionary<ExpandedNodeId, ReferenceDescription>();

            int searchDepth = 0;
            uint maxNodesPerBrowse = uaClient.Session.OperationLimits.MaxNodesPerBrowse;
            while (browseDescriptionCollection.Any() && searchDepth < kMaxSearchDepth)
            {
                searchDepth++;
                Utils.LogInfo("{0}: Browse {1} nodes after {2}ms",
                    searchDepth, browseDescriptionCollection.Count, stopWatch.ElapsedMilliseconds);

                var allBrowseResults = new BrowseResultCollection();
                bool repeatBrowse;
                var browseResultCollection = new BrowseResultCollection();
                var unprocessedOperations = new BrowseDescriptionCollection();
                DiagnosticInfoCollection diagnosticsInfoCollection;
                do
                {
                    if (m_quitEvent?.WaitOne(0) == true)
                    {
                        m_output.WriteLine("Browse aborted.");
                        break;
                    }

                    BrowseDescriptionCollection browseCollection = (maxNodesPerBrowse == 0) ?
                        browseDescriptionCollection :
                        browseDescriptionCollection.Take((int)maxNodesPerBrowse).ToArray();
                    repeatBrowse = false;
                    try
                    {
                        BrowseResponse browseResponse = await uaClient.Session.BrowseAsync(null, null,
                            kMaxReferencesPerNode, browseCollection, ct).ConfigureAwait(false);
                        browseResultCollection = browseResponse.Results;
                        diagnosticsInfoCollection = browseResponse.DiagnosticInfos;
                        ClientBase.ValidateResponse(browseResultCollection, browseCollection);
                        ClientBase.ValidateDiagnosticInfos(diagnosticsInfoCollection, browseCollection);

                        // separate unprocessed nodes for later
                        int ii = 0;
                        foreach (BrowseResult browseResult in browseResultCollection)
                        {
                            // check for error.
                            StatusCode statusCode = browseResult.StatusCode;
                            if (StatusCode.IsBad(statusCode))
                            {
                                // this error indicates that the server does not have enough simultaneously active
                                // continuation points. This request will need to be resent after the other operations
                                // have been completed and their continuation points released.
                                if (statusCode == StatusCodes.BadNoContinuationPoints)
                                {
                                    unprocessedOperations.Add(browseCollection[ii++]);
                                    continue;
                                }
                            }

                            // save results.
                            allBrowseResults.Add(browseResult);
                            ii++;
                        }
                    }
                    catch (ServiceResultException sre)
                    {
                        if (sre.StatusCode == StatusCodes.BadEncodingLimitsExceeded ||
                            sre.StatusCode == StatusCodes.BadResponseTooLarge)
                        {
                            // try to address by overriding operation limit
                            maxNodesPerBrowse = maxNodesPerBrowse == 0 ?
                                (uint)browseCollection.Count / 2 : maxNodesPerBrowse / 2;
                            repeatBrowse = true;
                        }
                        else
                        {
                            m_output.WriteLine("Browse error: {0}", sre.Message);
                            throw;
                        }
                    }
                } while (repeatBrowse);

                if (maxNodesPerBrowse == 0)
                {
                    browseDescriptionCollection.Clear();
                }
                else
                {
                    browseDescriptionCollection = browseDescriptionCollection.Skip(browseResultCollection.Count).ToArray();
                }

                // Browse next
                ByteStringCollection continuationPoints = PrepareBrowseNext(browseResultCollection);
                while (continuationPoints.Any())
                {
                    if (m_quitEvent?.WaitOne(0) == true)
                    {
                        m_output.WriteLine("Browse aborted.");
                    }

                    Utils.LogInfo("BrowseNext {0} continuation points.", continuationPoints.Count);
                    BrowseNextResponse browseNextResult = await uaClient.Session.BrowseNextAsync(null, false, continuationPoints, ct).ConfigureAwait(false);
                    BrowseResultCollection browseNextResultCollection = browseNextResult.Results;
                    diagnosticsInfoCollection = browseNextResult.DiagnosticInfos;
                    ClientBase.ValidateResponse(browseNextResultCollection, continuationPoints);
                    ClientBase.ValidateDiagnosticInfos(diagnosticsInfoCollection, continuationPoints);
                    allBrowseResults.AddRange(browseNextResultCollection);
                    continuationPoints = PrepareBrowseNext(browseNextResultCollection);
                }

                // Build browse request for next level
                var browseTable = new NodeIdCollection();
                int duplicates = 0;
                foreach (BrowseResult browseResult in allBrowseResults)
                {
                    foreach (ReferenceDescription reference in browseResult.References)
                    {
                        if (!referenceDescriptions.ContainsKey(reference.NodeId))
                        {
                            referenceDescriptions[reference.NodeId] = reference;
                            if (reference.ReferenceTypeId != ReferenceTypeIds.HasProperty)
                            {
                                browseTable.Add(ExpandedNodeId.ToNodeId(reference.NodeId, uaClient.Session.NamespaceUris));
                            }
                        }
                        else
                        {
                            duplicates++;
                        }
                    }
                }
                if (duplicates > 0)
                {
                    Utils.LogInfo("Browse Result {0} duplicate nodes were ignored.", duplicates);
                }
                browseDescriptionCollection.AddRange(CreateBrowseDescriptionCollectionFromNodeId(browseTable, browseTemplate));

                // add unprocessed nodes if any
                browseDescriptionCollection.AddRange(unprocessedOperations);
            }

            stopWatch.Stop();

            var result = new ReferenceDescriptionCollection(referenceDescriptions.Values);
            result.Sort((x, y) => x.NodeId.CompareTo(y.NodeId));

            m_output.WriteLine("BrowseFullAddressSpace found {0} references on server in {1}ms.",
                referenceDescriptions.Count, stopWatch.ElapsedMilliseconds);

            if (m_verbose)
            {
                foreach (ReferenceDescription reference in result)
                {
                    m_output.WriteLine("NodeId {0} {1} {2}", reference.NodeId, reference.NodeClass, reference.BrowseName);
                }
            }

            return result;
        }

        /// <summary>
        /// Loads the custom type system of the server in the session.
        /// </summary>
        /// <remarks>
        /// Outputs elapsed time information for perf testing and lists all
        /// types that were successfully added to the session encodeable type factory.
        /// </remarks>
        public async Task<ComplexTypeSystem> LoadTypeSystemAsync(ISession session)
        {
            m_output.WriteLine("Load the server type system.");

            var stopWatch = new Stopwatch();
            stopWatch.Start();

            var complexTypeSystem = new ComplexTypeSystem(session);
            await complexTypeSystem.Load().ConfigureAwait(false);

            stopWatch.Stop();

            m_output.WriteLine("Loaded {0} types took {1}ms.",
                complexTypeSystem.GetDefinedTypes().Length, stopWatch.ElapsedMilliseconds);

            if (m_verbose)
            {
                m_output.WriteLine("Custom types defined for this session:");
                foreach (Type type in complexTypeSystem.GetDefinedTypes())
                {
                    m_output.WriteLine($"{type.Namespace}.{type.Name}");
                }

                m_output.WriteLine($"Loaded {complexTypeSystem.DataTypeSystem.Count} dictionaries:");
                foreach (KeyValuePair<NodeId, DataDictionary> dictionary in complexTypeSystem.DataTypeSystem)
                {
                    m_output.WriteLine($" + {dictionary.Value.Name}");
                    foreach (KeyValuePair<NodeId, QualifiedName> type in dictionary.Value.DataTypes)
                    {
                        m_output.WriteLine($" -- {type.Key}:{type.Value}");
                    }
                }
            }

            return complexTypeSystem;
        }

        /// <summary>
        /// Read all ReferenceTypeIds from the server that are not known by the client.
        /// To reduce the number of calls due to traversal call pyramid, start with all
        /// known reference types to reduce the number of FetchReferences/FetchNodes calls.
        /// </summary>
        /// <remarks>
        /// The NodeCache needs this information to function properly with subtypes of hierarchical calls.
        /// </remarks>
        /// <param name="session">The session to use</param>
        private static Task FetchReferenceIdTypesAsync(ISession session)
        {
            // fetch the reference types first, otherwise browse for e.g. hierarchical references with subtypes won't work
            const BindingFlags bindingFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public;
            NamespaceTable namespaceUris = session.NamespaceUris;
            IEnumerable<ExpandedNodeId> referenceTypes = typeof(ReferenceTypeIds)
                     .GetFields(bindingFlags)
                     .Select(field => NodeId.ToExpandedNodeId((NodeId)field.GetValue(null), namespaceUris));
            return session.FetchTypeTreeAsync([.. referenceTypes]);
        }

        /// <summary>
        /// Output all values as JSON.
        /// </summary>
        public async Task<(DataValueCollection, IList<ServiceResult>)> ReadAllValuesAsync(
            IUAClient uaClient,
            NodeIdCollection variableIds)
        {
            bool retrySingleRead = false;
            DataValueCollection values = null;
            IList<ServiceResult> errors = null;

            do
            {
                try
                {
                    if (retrySingleRead)
                    {
                        values = [];
                        errors = [];

                        foreach (NodeId variableId in variableIds)
                        {
                            try
                            {
                                m_output.WriteLine("Read {0}", variableId);
                                DataValue value = await uaClient.Session.ReadValueAsync(variableId).ConfigureAwait(false);
                                values.Add(value);
                                errors.Add(value.StatusCode);

                                if (ServiceResult.IsNotBad(value.StatusCode))
                                {
                                    string valueString = FormatValueAsJson(uaClient.Session.MessageContext, variableId.ToString(), value, JsonEncodingType.Compact);
                                    m_output.WriteLine(valueString);
                                }
                                else
                                {
                                    m_output.WriteLine("Error: {0}", value.StatusCode);
                                }
                            }
                            catch (ServiceResultException sre)
                            {
                                m_output.WriteLine("Error: {0}", sre.Message);
                                values.Add(new DataValue(sre.StatusCode));
                                errors.Add(sre.Result);
                            }
                        }
                    }
                    else
                    {
                        (values, errors) = await uaClient.Session.ReadValuesAsync(variableIds).ConfigureAwait(false);

                        int ii = 0;
                        foreach (DataValue value in values)
                        {
                            if (ServiceResult.IsNotBad(errors[ii]))
                            {
                                string valueString = FormatValueAsJson(uaClient.Session.MessageContext, variableIds[ii].ToString(), value, JsonEncodingType.Compact);
                                m_output.WriteLine(valueString);
                            }
                            else
                            {
                                m_output.WriteLine("Error: {0}", value.StatusCode);
                            }
                            ii++;
                        }
                    }

                    retrySingleRead = false;
                }
                catch (ServiceResultException sre) when (sre.StatusCode == StatusCodes.BadEncodingLimitsExceeded)
                {
                    m_output.WriteLine("Retry to read the values due to error: {0}", sre.Message);
                    retrySingleRead = !retrySingleRead;
                }
            } while (retrySingleRead);

            return (values, errors);
        }

        /// <summary>
        /// Subscribe to all variables in the list.
        /// </summary>
        /// <param name="uaClient">The UAClient with a session to use.</param>
        /// <param name="variableIds">The variables to subscribe.</param>
        public async Task SubscribeAllValuesAsync(
            IUAClient uaClient,
            NodeCollection variableIds,
            int samplingInterval,
            int publishingInterval,
            uint queueSize,
            uint lifetimeCount,
            uint keepAliveCount)
        {
            if (uaClient.Session == null || !uaClient.Session.Connected)
            {
                m_output.WriteLine("Session not connected!");
                return;
            }

            try
            {
                // Create a subscription for receiving data change notifications
                ISession session = uaClient.Session;

                // test for deferred ack of sequence numbers
                session.PublishSequenceNumbersToAcknowledge += DeferSubscriptionAcknowledge;

                // set a minimum amount of three publish requests per session
                session.MinPublishRequestCount = 3;

                // Define Subscription parameters
                var subscription = new Subscription(session.DefaultSubscription) {
                    DisplayName = "Console ReferenceClient Subscription",
                    PublishingEnabled = true,
                    PublishingInterval = publishingInterval,
                    LifetimeCount = lifetimeCount,
                    KeepAliveCount = keepAliveCount,
                    SequentialPublishing = true,
                    RepublishAfterTransfer = true,
                    DisableMonitoredItemCache = true,
                    MaxNotificationsPerPublish = 1000,
                    MinLifetimeInterval = (uint)session.SessionTimeout,
                    FastDataChangeCallback = FastDataChangeNotification,
                    FastKeepAliveCallback = FastKeepAliveNotification,
                };
                session.AddSubscription(subscription);

                // Create the subscription on Server side
                await subscription.CreateAsync().ConfigureAwait(false);
                m_output.WriteLine("New Subscription created with SubscriptionId = {0}.", subscription.Id);

                // Create MonitoredItems for data changes
                foreach (Node item in variableIds)
                {
                    var monitoredItem = new MonitoredItem(subscription.DefaultItem) {
                        StartNodeId = item.NodeId,
                        AttributeId = Attributes.Value,
                        SamplingInterval = samplingInterval,
                        DisplayName = item.DisplayName?.Text ?? item.BrowseName?.Name ?? "unknown",
                        QueueSize = queueSize,
                        DiscardOldest = true,
                        MonitoringMode = MonitoringMode.Reporting,
                    };
                    subscription.AddItem(monitoredItem);
                    if (subscription.CurrentKeepAliveCount > 1000)
                    {
                        break;
                    }
                }

                // Create the monitored items on Server side
                await subscription.ApplyChangesAsync().ConfigureAwait(false);
                m_output.WriteLine("MonitoredItems {0} created for SubscriptionId = {1}.", subscription.MonitoredItemCount, subscription.Id);
            }
            catch (Exception ex)
            {
                m_output.WriteLine("Subscribe error: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Create a prettified JSON string of a DataValue.
        /// </summary>
        /// <param name="name">The key of the Json value.</param>
        /// <param name="value">The DataValue.</param>
        /// <param name="jsonEncodingType">Use reversible encoding.</param>
        public static string FormatValueAsJson(
            IServiceMessageContext messageContext,
            string name,
            DataValue value,
            JsonEncodingType jsonEncodingType)
        {
            string textbuffer;
            using (var jsonEncoder = new JsonEncoder(messageContext, jsonEncodingType))
            {
                jsonEncoder.WriteDataValue(name, value);
                textbuffer = jsonEncoder.CloseAndReturnText();
            }

            // prettify
            using var stringWriter = new StringWriter();
            try
            {
                using var stringReader = new StringReader(textbuffer);
                var jsonReader = new JsonTextReader(stringReader);
                var jsonWriter = new JsonTextWriter(stringWriter) {
                    Formatting = Formatting.Indented,
                    Culture = CultureInfo.InvariantCulture
                };
                jsonWriter.WriteToken(jsonReader);
            }
            catch (Exception ex)
            {
                stringWriter.WriteLine("Failed to format the JSON output: {0}", ex.Message);
                stringWriter.WriteLine(textbuffer);
                throw;
            }
            return stringWriter.ToString();
        }

        /// <summary>
        /// The fast keep alive notification callback.
        /// </summary>
        private void FastKeepAliveNotification(Subscription subscription, NotificationData notification)
        {
            try
            {
                m_output.WriteLine("Keep Alive  : Id={0} PublishTime={1} SequenceNumber={2}.",
                    subscription.Id, notification.PublishTime, notification.SequenceNumber);
            }
            catch (Exception ex)
            {
                m_output.WriteLine("FastKeepAliveNotification error: {0}", ex.Message);
            }
        }

        /// <summary>
        /// The fast data change notification callback.
        /// </summary>
        private void FastDataChangeNotification(Subscription subscription, DataChangeNotification notification, IList<string> stringTable)
        {
            try
            {
                m_output.WriteLine("Notification: Id={0} PublishTime={1} SequenceNumber={2} Items={3}.",
                    subscription.Id, notification.PublishTime,
                    notification.SequenceNumber, notification.MonitoredItems.Count);
            }
            catch (Exception ex)
            {
                m_output.WriteLine("FastDataChangeNotification error: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Handle DataChange notifications from Server
        /// </summary>
        private void OnMonitoredItemNotification(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e)
        {
            try
            {
                // Log MonitoredItem Notification event
                var notification = e.NotificationValue as MonitoredItemNotification;
                DateTime localTime = notification.Value.SourceTimestamp.ToLocalTime();
                m_output.WriteLine("Notification: {0} \"{1}\" and Value = {2} at [{3}].",
                    notification.Message.SequenceNumber,
                    monitoredItem.ResolvedNodeId,
                    notification.Value,
                    localTime.ToLongTimeString());
            }
            catch (Exception ex)
            {
                m_output.WriteLine("OnMonitoredItemNotification error: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Handle Requested Event notifications from Server
        /// </summary>
        private void OnMonitoredItemEventNotification(MonitoredItem monitoredItem, MonitoredItemNotificationEventArgs e)
        {
            try
            {
                // Log MonitoredItem Notification event
                var notification = e.NotificationValue as EventFieldList;

                foreach (KeyValuePair<int, QualifiedNameCollection> entry in m_desiredEventFields)
                {
                    Variant field = notification.EventFields[entry.Key];
                    if (field.TypeInfo.BuiltInType != BuiltInType.Null)
                    {
                        var fieldPath = new StringBuilder();

                        int lastIndex = entry.Value.Count - 1;
                        for (int index = 0; index < entry.Value.Count; index++)
                        {
                            fieldPath.Append(entry.Value[index].Name);
                            if (index < lastIndex)
                            {
                                fieldPath.Append('.');
                            }
                        }

                        string fieldName = fieldPath.ToString();
                        if (fieldName.Equals("Time", StringComparison.Ordinal))
                        {
                            try
                            {
                                var currentTime = (DateTime)field.Value;
                                TimeSpan timeSpan = currentTime - m_lastEventTime;
                                m_lastEventTime = currentTime;
                                m_processedEvents++;
                                string timeBetweenEvents = "";
                                if (m_processedEvents > 1)
                                {
                                    timeBetweenEvents = ", time since last event = " + timeSpan.Seconds.ToString(CultureInfo.InvariantCulture) + " seconds";
                                }

                                m_output.WriteLine("Event Received - total count = {0}{1}",
                                    m_processedEvents.ToString(CultureInfo.InvariantCulture),
                                    timeBetweenEvents);
                            }
                            catch (Exception ex)
                            {
                                m_output.WriteLine("Unexpected error retrieving Event Time Field Value: {0}", ex.Message);
                            }
                        }

                        m_output.WriteLine("\tField [{0}] \"{1}\" = [{2}]",
                            entry.Key.ToString(CultureInfo.InvariantCulture), fieldName, field.Value);
                    }
                }
            }
            catch (Exception ex)
            {
                m_output.WriteLine("OnMonitoredItemEventNotification error: {0}", ex.Message);
            }
        }

        /// <summary>
        /// Event handler to defer publish response sequence number acknowledge.
        /// </summary>
        private void DeferSubscriptionAcknowledge(ISession session, PublishSequenceNumbersToAcknowledgeEventArgs e)
        {
            // for testing keep the latest sequence numbers for a while
            const int AckDelay = 5;
            if (e.AcknowledgementsToSend.Count > 0)
            {
                // defer latest sequence numbers
                var deferredItems = e.AcknowledgementsToSend.OrderByDescending(s => s.SequenceNumber).Take(AckDelay).ToList();
                e.DeferredAcknowledgementsToSend.AddRange(deferredItems);
                foreach (SubscriptionAcknowledgement deferredItem in deferredItems)
                {
                    e.AcknowledgementsToSend.Remove(deferredItem);
                }
            }
        }

        /// <summary>
        /// Create a browse description from a node id collection.
        /// </summary>
        /// <param name="nodeIdCollection">The node id collection.</param>
        /// <param name="template">The template for the browse description for each node id.</param>
        private static BrowseDescriptionCollection CreateBrowseDescriptionCollectionFromNodeId(
            NodeIdCollection nodeIdCollection,
            BrowseDescription template)
        {
            var browseDescriptionCollection = new BrowseDescriptionCollection();
            foreach (NodeId nodeId in nodeIdCollection)
            {
                var browseDescription = (BrowseDescription)template.MemberwiseClone();
                browseDescription.NodeId = nodeId;
                browseDescriptionCollection.Add(browseDescription);
            }
            return browseDescriptionCollection;
        }

        /// <summary>
        /// Create the continuation point collection from the browse result
        /// collection for the BrowseNext service.
        /// </summary>
        /// <param name="browseResultCollection">The browse result collection to use.</param>
        /// <returns>The collection of continuation points for the BrowseNext service.</returns>
        private static ByteStringCollection PrepareBrowseNext(BrowseResultCollection browseResultCollection)
        {
            var continuationPoints = new ByteStringCollection();
            foreach (BrowseResult browseResult in browseResultCollection)
            {
                if (browseResult.ContinuationPoint != null)
                {
                    continuationPoints.Add(browseResult.ContinuationPoint);
                }
            }
            return continuationPoints;
        }

        private readonly Action<IList, IList> m_validateResponse;
        private readonly TextWriter m_output;
        private readonly ManualResetEvent m_quitEvent;
        private readonly bool m_verbose;
        private readonly Dictionary<int, QualifiedNameCollection> m_desiredEventFields;
        private int m_processedEvents;
        private DateTime m_lastEventTime = DateTime.Now;
    }
}
