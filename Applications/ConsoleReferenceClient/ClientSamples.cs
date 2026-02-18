/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
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

        public ClientSamples(
            ITelemetryContext telemetry,
            Action<IList, IList> validateResponse,
            ManualResetEvent quitEvent = null,
            bool verbose = false)
        {
            m_telemetry = telemetry;
            m_logger = telemetry.CreateLogger<ClientSamples>();
            m_validate = validateResponse;

            m_quitEvent = quitEvent;
            m_verbose = verbose;
            m_desiredEventFields = [];
            int eventIndexCounter = 0;

            m_desiredEventFields.Add(
                eventIndexCounter++,
                [.. new QualifiedName[] { QualifiedName.From(BrowseNames.Time) }]);
            m_desiredEventFields.Add(
                eventIndexCounter++,
                [.. new QualifiedName[] { QualifiedName.From(BrowseNames.ActiveState) }]);
            m_desiredEventFields.Add(
                eventIndexCounter++,
                [.. new QualifiedName[] { QualifiedName.From(BrowseNames.Message) }]);
            m_desiredEventFields.Add(
                eventIndexCounter++,
                [.. new QualifiedName[] { QualifiedName.From(BrowseNames.LimitState), QualifiedName.From(BrowseNames.CurrentState) }]);
            m_desiredEventFields.Add(
                eventIndexCounter++,
                [.. new QualifiedName[] { QualifiedName.From(BrowseNames.LimitState), QualifiedName.From(BrowseNames.LastTransition) }]);
        }

        /// <summary>
        /// Read a list of nodes from Server
        /// </summary>
        public async Task ReadNodesAsync(ISession session, CancellationToken ct = default)
        {
            if (session == null || !session.Connected)
            {
                Console.WriteLine("Session not connected!");
                return;
            }

            try
            {
                // build a list of nodes to be read
                var nodesToRead = new ReadValueIdCollection
                {
                    // Value of ServerStatus
                    new ReadValueId {
                        NodeId = Variables.Server_ServerStatus,
                        AttributeId = Attributes.Value },
                    // BrowseName of ServerStatus_StartTime
                    new ReadValueId
                    {
                        NodeId = Variables.Server_ServerStatus_StartTime,
                        AttributeId = Attributes.BrowseName
                    },
                    // Value of ServerStatus_StartTime
                    new ReadValueId
                    {
                        NodeId = Variables.Server_ServerStatus_StartTime,
                        AttributeId = Attributes.Value
                    }
                };

                // Read the node attributes
                Console.WriteLine("Reading nodes...");

                // Call Read Service
                ReadResponse response = await session.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Both,
                    nodesToRead,
                    ct).ConfigureAwait(false);

                DataValueCollection resultsValues = response.Results;
                DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

                // Validate the results
                ValidateResponse(resultsValues, nodesToRead);

                // Display the results.
                foreach (DataValue result in resultsValues)
                {
                    Console.WriteLine($"Read Value = {result.Value} , StatusCode = {result.StatusCode}");
                }

                // Read Server NamespaceArray
                Console.WriteLine("Reading Value of NamespaceArray node...");
                DataValue namespaceArray = await session.ReadValueAsync(Variables.Server_NamespaceArray, ct)
                    .ConfigureAwait(false);
                // Display the result
                Console.WriteLine($"NamespaceArray Value = {namespaceArray}");
            }
            catch (Exception ex)
            {
                // Log Error
                m_logger.LogError(ex, "Read Nodes Error.");
            }
        }

        /// <summary>
        /// Write a list of nodes to the Server.
        /// </summary>
        public async Task WriteNodesAsync(ISession session, CancellationToken ct = default)
        {
            if (session == null || !session.Connected)
            {
                Console.WriteLine("Session not connected!");
                return;
            }

            try
            {
                // Write the configured nodes
                var nodesToWrite = new WriteValueCollection();

                // Int32 Node - Objects\CTT\Scalar\Scalar_Static\Int32
                var intWriteVal = new WriteValue
                {
                    NodeId = NodeId.Parse("ns=2;s=Scalar_Static_Int32"),
                    AttributeId = Attributes.Value,
                    Value = new DataValue { Value = 100 }
                };
                nodesToWrite.Add(intWriteVal);

                // Float Node - Objects\CTT\Scalar\Scalar_Static\Float
                var floatWriteVal = new WriteValue
                {
                    NodeId = NodeId.Parse("ns=2;s=Scalar_Static_Float"),
                    AttributeId = Attributes.Value,
                    Value = new DataValue { Value = (float)100.5 }
                };
                nodesToWrite.Add(floatWriteVal);

                // String Node - Objects\CTT\Scalar\Scalar_Static\String
                var stringWriteVal = new WriteValue
                {
                    NodeId = NodeId.Parse("ns=2;s=Scalar_Static_String"),
                    AttributeId = Attributes.Value,
                    Value = new DataValue { Value = "String Test" }
                };
                nodesToWrite.Add(stringWriteVal);

                // Write the node attributes
                Console.WriteLine("Writing nodes...");

                // Call Write Service
                WriteResponse response = await session.WriteAsync(
                    null,
                    nodesToWrite,
                    ct).ConfigureAwait(false);

                StatusCodeCollection results = response.Results;
                DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

                // Validate the response
                ValidateResponse(results, nodesToWrite);

                // Display the results.
                Console.WriteLine("Write Results :");

                foreach (StatusCode writeResult in results)
                {
                    Console.WriteLine($"     {writeResult}");
                }
            }
            catch (Exception ex)
            {
                // Log Error
                m_logger.LogInformation(ex, "Write Nodes Error.");
            }
        }

        /// <summary>
        /// Browse Server nodes
        /// </summary>
        public async Task BrowseAsync(ISession session, CancellationToken ct = default)
        {
            if (session == null || !session.Connected)
            {
                Console.WriteLine("Session not connected!");
                return;
            }

            try
            {
                // Create a Browser object
                var browser = new Browser(session)
                {
                    // Set browse parameters
                    BrowseDirection = BrowseDirection.Forward,
                    NodeClassMask = (int)NodeClass.Object | (int)NodeClass.Variable,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true
                };

                NodeId nodeToBrowse = ObjectIds.Server;

                // Call Browse service
                Console.WriteLine($"Browsing {nodeToBrowse} node...");
                ReferenceDescriptionCollection browseResults =
                    await browser.BrowseAsync(nodeToBrowse, ct).ConfigureAwait(false);

                // Display the results
                Console.WriteLine($"Browse returned {browseResults.Count} results:");

                foreach (ReferenceDescription result in browseResults)
                {
                    Console.WriteLine($"     DisplayName = {result.DisplayName.Text}, NodeClass = {result.NodeClass}");
                }
            }
            catch (Exception ex)
            {
                // Log Error
                m_logger.LogError(ex, "Browse Error.");
            }
        }

        /// <summary>
        /// Call UA method
        /// </summary>
        public async Task CallMethodAsync(ISession session, CancellationToken ct = default)
        {
            if (session == null || !session.Connected)
            {
                Console.WriteLine("Session not connected!");
                return;
            }

            try
            {
                // Define the UA Method to call
                // Parent node - Objects\CTT\Methods
                // Method node - Objects\CTT\Methods\Add
                var objectId = NodeId.Parse("ns=2;s=Methods");
                var methodId = NodeId.Parse("ns=2;s=Methods_Add");

                // Define the method parameters
                // Input argument requires a Float and an UInt32 value
                // Invoke Call service
                Console.WriteLine($"Calling UAMethod for node {methodId} ...");
                VariantCollection outputArguments = await session.CallAsync(
                    objectId,
                    methodId,
                    ct,
                    (float)10.5,
                    (uint)10).ConfigureAwait(false);

                // Display results
                Console.WriteLine($"Method call returned {outputArguments.Count} output argument(s):");

                foreach (Variant outputArgument in outputArguments)
                {
                    Console.WriteLine($"     OutputValue = {outputArgument.Value}");
                }
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Method call error");
            }
        }

        /// <summary>
        /// Call the Start method for Alarming to enable events
        /// </summary>
        public async Task EnableEventsAsync(
            ISession session,
            uint timeToRun,
            CancellationToken ct = default)
        {
            if (session == null || !session.Connected)
            {
                Console.WriteLine("Session not connected!");
                return;
            }

            try
            {
                // Define the UA Method to call
                // Parent node - Objects\CTT\Alarms
                // Method node - Objects\CTT\Alarms\Start
                var objectId = NodeId.Parse("ns=7;s=Alarms");
                var methodId = NodeId.Parse("ns=7;s=Alarms.Start");

                // Define the method parameters
                // Input argument requires a Float and an UInt32 value
                // Invoke Call service
                Console.WriteLine($"Calling UAMethod for node {methodId} ...");
                VariantCollection outputArguments = await session.CallAsync(
                    objectId,
                    methodId,
                    ct,
                    timeToRun).ConfigureAwait(false);

                // Display results
                Console.WriteLine($"Method call returned {outputArguments.Count} output argument(s):");

                foreach (Variant outputArgument in outputArguments)
                {
                    Console.WriteLine($"     OutputValue = {outputArgument.Value}");
                }
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Method call error");
            }
        }

        /// <summary>
        /// Create Subscription and MonitoredItems for DataChanges
        /// </summary>
        public async Task<bool> SubscribeToDataChangesAsync(
            ISession session,
            uint minLifeTime,
            bool enableDurableSubscriptions,
            CancellationToken ct = default)
        {
            bool isDurable = false;

            if (session == null || !session.Connected)
            {
                Console.WriteLine("Session not connected!");
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
                var subscription = new Subscription(session.DefaultSubscription)
                {
                    DisplayName = "Console ReferenceClient Subscription",
                    PublishingEnabled = true,
                    PublishingInterval = subscriptionPublishingInterval,
                    LifetimeCount = 0,
                    MinLifetimeInterval = lifetime,
                    KeepAliveCount = 5
                };

                session.AddSubscription(subscription);

                // Create the subscription on Server side
                await subscription.CreateAsync(ct).ConfigureAwait(false);
                m_logger.LogInformation(
                    "New Subscription created with SubscriptionId = {Id}, Sampling Interval {SamplingInterval}, Publishing Interval {PublishingInterval}.",
                    subscription.Id,
                    itemSamplingInterval,
                    subscriptionPublishingInterval);

                if (enableDurableSubscriptions)
                {
                    (bool success, uint revisedLifetimeInHours) =
                        await subscription.SetSubscriptionDurableAsync(1, ct).ConfigureAwait(false);
                    if (success)
                    {
                        isDurable = true;

                        m_logger.LogInformation(
                            "Subscription {SubscriptionId} is now durable, Revised Lifetime {Lifetime} in hours.",
                            subscription.Id,
                            revisedLifetimeInHours);
                    }
                    else
                    {
                        m_logger.LogInformation("Subscription {SubscriptionId} failed durable call", subscription.Id);
                    }
                }

                // Create MonitoredItems for data changes (Reference Server)

                var intMonitoredItem = new MonitoredItem(subscription.DefaultItem)
                {
                    // Int32 Node - Objects\CTT\Scalar\Simulation\Int32
                    StartNodeId = NodeId.Parse("ns=2;s=Scalar_Simulation_Int32"),
                    AttributeId = Attributes.Value,
                    DisplayName = "Int32 Variable",
                    SamplingInterval = itemSamplingInterval,
                    QueueSize = queueSize,
                    DiscardOldest = true
                };
                intMonitoredItem.Notification += OnMonitoredItemNotification;

                subscription.AddItem(intMonitoredItem);

                var floatMonitoredItem = new MonitoredItem(subscription.DefaultItem)
                {
                    // Float Node - Objects\CTT\Scalar\Simulation\Float
                    StartNodeId = NodeId.Parse("ns=2;s=Scalar_Simulation_Float"),
                    AttributeId = Attributes.Value,
                    DisplayName = "Float Variable",
                    SamplingInterval = itemSamplingInterval,
                    QueueSize = queueSize
                };
                floatMonitoredItem.Notification += OnMonitoredItemNotification;

                subscription.AddItem(floatMonitoredItem);

                var stringMonitoredItem = new MonitoredItem(subscription.DefaultItem)
                {
                    // String Node - Objects\CTT\Scalar\Simulation\String
                    StartNodeId = NodeId.Parse("ns=2;s=Scalar_Simulation_String"),
                    AttributeId = Attributes.Value,
                    DisplayName = "String Variable",
                    SamplingInterval = itemSamplingInterval,
                    QueueSize = queueSize
                };
                stringMonitoredItem.Notification += OnMonitoredItemNotification;

                subscription.AddItem(stringMonitoredItem);

                var eventMonitoredItem = new MonitoredItem(subscription.DefaultItem)
                {
                    StartNodeId = ObjectIds.Server,
                    AttributeId = Attributes.EventNotifier,
                    DisplayName = "Event Variable",
                    SamplingInterval = itemSamplingInterval,
                    QueueSize = queueSize
                };
                eventMonitoredItem.Notification += OnMonitoredItemEventNotification;

                var filter = new EventFilter();

                var simpleAttributeOperands = new SimpleAttributeOperandCollection();

                foreach (QualifiedNameCollection desiredEventField in m_desiredEventFields.Values)
                {
                    simpleAttributeOperands.Add(
                        new SimpleAttributeOperand
                        {
                            AttributeId = Attributes.Value,
                            TypeDefinitionId = ObjectTypeIds.BaseEventType,
                            BrowsePath = desiredEventField
                        });
                }
                filter.SelectClauses = simpleAttributeOperands;

                var whereClause = new ContentFilter();
                var existingEventType = new SimpleAttributeOperand
                {
                    AttributeId = Attributes.Value,
                    TypeDefinitionId = ObjectTypeIds.ExclusiveLevelAlarmType,
                    BrowsePath = [QualifiedName.From("EventType")]
                };
                var desiredEventType = new LiteralOperand
                {
                    Value = new Variant(ObjectTypeIds.ExclusiveLevelAlarmType)
                };

                whereClause.Push(FilterOperator.Equals, [existingEventType, desiredEventType]);

                filter.WhereClause = whereClause;

                eventMonitoredItem.Filter = filter;
                eventMonitoredItem.NodeClass = NodeClass.Object;

                subscription.AddItem(eventMonitoredItem);

                // Create the monitored items on Server side
                await subscription.ApplyChangesAsync(ct).ConfigureAwait(false);
                m_logger.LogInformation(
                    "MonitoredItems created for SubscriptionId = {SubscriptionId}.",
                    subscription.Id);
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Subscribe error");
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
            bool clearNodeCache = true,
            CancellationToken ct = default)
        {
            var stopwatch = new Stopwatch();
            var nodeDictionary = new Dictionary<ExpandedNodeId, INode>();
            var references = new NodeIdCollection { ReferenceTypeIds.HierarchicalReferences };
            var nodesToBrowse = new ExpandedNodeIdCollection { startingNode };

            // start
            stopwatch.Start();

            if (clearNodeCache)
            {
                // clear NodeCache to fetch all nodes from server
                uaClient.Session.NodeCache.Clear();
                await FetchReferenceIdTypesAsync(uaClient.Session, ct).ConfigureAwait(false);
            }

            // add root node
            if (addRootNode)
            {
                INode rootNode = await uaClient.Session.NodeCache.FindAsync(startingNode, ct)
                    .ConfigureAwait(false);
                nodeDictionary[rootNode.NodeId] = rootNode;
            }

            int searchDepth = 0;
            while (nodesToBrowse.Count > 0 && searchDepth < kMaxSearchDepth)
            {
                if (m_quitEvent?.WaitOne(0) == true)
                {
                    m_logger.LogInformation("Browse aborted.");
                    break;
                }

                searchDepth++;
                m_logger.LogInformation(
                    "{Depth}: Find {Count} references after {Duration}ms",
                    searchDepth,
                    nodesToBrowse.Count,
                    stopwatch.ElapsedMilliseconds);
                IList<INode> response = await uaClient
                    .Session.NodeCache.FindReferencesAsync(nodesToBrowse, references, false, true, ct)
                    .ConfigureAwait(false);

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
                                IReference hasTypeDefinition = variableNode.ReferenceTable
                                    .FirstOrDefault(r =>
                                        r.ReferenceTypeId
                                            .Equals(ReferenceTypeIds.HasTypeDefinition));
                                if (hasTypeDefinition != null)
                                {
                                    leafNode = hasTypeDefinition.TargetId == VariableTypeIds
                                        .PropertyType;
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
                    m_logger.LogInformation("Find References {Count} duplicate nodes were ignored", duplicates);
                }
                if (leafNodes > 0)
                {
                    m_logger.LogInformation("Find References {Count} leaf nodes were ignored", leafNodes);
                }
                nodesToBrowse = nextNodesToBrowse;
            }

            stopwatch.Stop();

            m_logger.LogInformation(
                "FetchAllNodesNodeCache found {Count} nodes in {Duration}ms",
                nodeDictionary.Count,
                stopwatch.ElapsedMilliseconds);

            var result = nodeDictionary.Values.ToList();
            result.Sort((x, y) => x.NodeId.CompareTo(y.NodeId));

            if (m_verbose)
            {
                foreach (INode node in result)
                {
                    m_logger.LogInformation(
                        "NodeId {NodeId} {NodeClass} {BrowseName}",
                        node.NodeId,
                        node.NodeClass,
                        node.BrowseName);
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
            NodeId startingNode = default,
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
                    m_logger.LogWarning(
                        "Setting the BrowseResultMask is not supported by the " +
                        "ManagedBrowse method. Using '{BrowseResultMask}' instead of " +
                        "the mask {BrowseDescriptionResultMask} for the result mask",
                        BrowseResultMask.All,
                        browseDescription.ResultMask);
                }
            }

            var nodesToBrowse = new List<NodeId> { startingNode.IsNull ? ObjectIds.RootFolder : startingNode };

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
                m_logger.LogInformation(
                    "{Depth}: Browse {Count} nodes after {Duration}ms",
                    searchDepth,
                    nodesToBrowse.Count,
                    stopWatch.ElapsedMilliseconds);

                const bool repeatBrowse = false;

                do
                {
                    if (m_quitEvent?.WaitOne(0) == true)
                    {
                        m_logger.LogInformation("Browse aborted.");
                        break;
                    }

                    try
                    {
                        // the resultMask defaults to "all"
                        // maybe the API should be extended to
                        // support it. But that will then also be
                        // necessary for BrowseAsync
                        (IList<ReferenceDescriptionCollection> descriptions, IList<ServiceResult> errors) =
                            await uaClient
                                .Session.ManagedBrowseAsync(
                                    null,
                                    null,
                                    nodesToBrowse,
                                    kMaxReferencesPerNode,
                                    browseDirection,
                                    referenceTypeId,
                                    true,
                                    nodeClassMask,
                                    ct)
                                .ConfigureAwait(false);

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
                        m_logger.LogError(sre, "Browse error");
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
                                nodesForNextManagedBrowse.Add(
                                    ExpandedNodeId.ToNodeId(
                                        reference.NodeId,
                                        uaClient.Session.NamespaceUris));
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
                    m_logger.LogInformation(
                        "Managed Browse Result {Count} duplicate nodes were ignored.",
                        duplicates);
                }
            }

            stopWatch.Stop();

            var result = new ReferenceDescriptionCollection(referenceDescriptions.Values);

            result.Sort((x, y) => x.NodeId.CompareTo(y.NodeId));

            m_logger.LogInformation(
                "ManagedBrowseFullAddressSpace found {Count} references on server in {Duration}ms.",
                result.Count,
                stopWatch.ElapsedMilliseconds);

            if (m_verbose)
            {
                foreach (ReferenceDescription reference in result)
                {
                    m_logger.LogInformation(
                        "NodeId {NodeId} {NodeClass} {BrowseName}",
                        reference.NodeId,
                        reference.NodeClass,
                        reference.BrowseName);
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
            NodeId startingNode = default,
            BrowseDescription browseDescription = null,
            CancellationToken ct = default)
        {
            var stopWatch = new Stopwatch();
            stopWatch.Start();

            // Browse template
            const int kMaxReferencesPerNode = 1000;
            BrowseDescription browseTemplate =
                browseDescription
                ?? new BrowseDescription
                {
                    NodeId = startingNode.IsNull ? ObjectIds.RootFolder : startingNode,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true,
                    NodeClassMask = 0,
                    ResultMask = (uint)BrowseResultMask.All
                };
            var browseDescriptionCollection
                = CreateBrowseDescriptionCollectionFromNodeId(
                [.. new NodeId[] { startingNode.IsNull ? ObjectIds.RootFolder : startingNode }],
                browseTemplate);

            // Browse
            var referenceDescriptions = new Dictionary<ExpandedNodeId, ReferenceDescription>();

            int searchDepth = 0;
            uint maxNodesPerBrowse = uaClient.Session.OperationLimits.MaxNodesPerBrowse;
            while (browseDescriptionCollection.Count > 0 && searchDepth < kMaxSearchDepth)
            {
                searchDepth++;
                m_logger.LogInformation(
                    "{Depth}: Browse {Count} nodes after {Duration}ms",
                    searchDepth,
                    browseDescriptionCollection.Count,
                    stopWatch.ElapsedMilliseconds);

                var allBrowseResults = new BrowseResultCollection();
                bool repeatBrowse;
                ArrayOf<BrowseResult> browseResultCollection = default;
                var unprocessedOperations = new BrowseDescriptionCollection();
                ArrayOf<DiagnosticInfo> diagnosticsInfoCollection;
                do
                {
                    if (m_quitEvent?.WaitOne(0) == true)
                    {
                        m_logger.LogInformation("Browse aborted.");
                        break;
                    }

                    var browseCollection =
                        maxNodesPerBrowse == 0
                            ? browseDescriptionCollection
                            : browseDescriptionCollection.Slice(0, (int)maxNodesPerBrowse);
                    repeatBrowse = false;
                    try
                    {
                        BrowseResponse browseResponse = await uaClient
                            .Session.BrowseAsync(
                                null,
                                null,
                                kMaxReferencesPerNode,
                                browseCollection,
                                ct)
                            .ConfigureAwait(false);
                        browseResultCollection = browseResponse.Results;
                        diagnosticsInfoCollection = browseResponse.DiagnosticInfos;
                        ClientBase.ValidateResponse(browseResultCollection, browseCollection);
                        ClientBase.ValidateDiagnosticInfos(
                            diagnosticsInfoCollection,
                            browseCollection);

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
                            maxNodesPerBrowse =
                                maxNodesPerBrowse == 0
                                    ? (uint)browseCollection.Count / 2
                                    : maxNodesPerBrowse / 2;
                            repeatBrowse = true;
                        }
                        else
                        {
                            m_logger.LogError(sre, "Browse error.");
                            throw;
                        }
                    }
                } while (repeatBrowse);

                if (maxNodesPerBrowse == 0)
                {
                    browseDescriptionCollection = ArrayOf<BrowseDescription>.Empty;
                }
                else
                {
                    browseDescriptionCollection = browseDescriptionCollection
                        .Slice(browseResultCollection.Count);
                }

                // Browse next
                var continuationPoints = PrepareBrowseNext(browseResultCollection);
                while (continuationPoints.Count > 0)
                {
                    if (m_quitEvent?.WaitOne(0) == true)
                    {
                        m_logger.LogInformation("Browse aborted.");
                    }

                    m_logger.LogInformation("BrowseNext {Count} continuation points.", continuationPoints.Count);
                    BrowseNextResponse browseNextResult = await uaClient
                        .Session.BrowseNextAsync(null, false, continuationPoints, ct)
                        .ConfigureAwait(false);
                    var browseNextResultCollection = browseNextResult.Results;
                    diagnosticsInfoCollection = browseNextResult.DiagnosticInfos;
                    ClientBase.ValidateResponse(browseNextResultCollection, continuationPoints);
                    ClientBase.ValidateDiagnosticInfos(
                        diagnosticsInfoCollection,
                        continuationPoints);
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
                                browseTable.Add(
                                    ExpandedNodeId.ToNodeId(
                                        reference.NodeId,
                                        uaClient.Session.NamespaceUris));
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
                    m_logger.LogInformation("Browse Result {Count} duplicate nodes were ignored.", duplicates);
                }
                browseDescriptionCollection = ArrayOf.Combine(
                    browseDescriptionCollection,
                    CreateBrowseDescriptionCollectionFromNodeId(browseTable, browseTemplate),
                    unprocessedOperations); // add unprocessed nodes if any
            }

            stopWatch.Stop();

            var result = new ReferenceDescriptionCollection(referenceDescriptions.Values);
            result.Sort((x, y) => x.NodeId.CompareTo(y.NodeId));

            m_logger.LogInformation(
                "BrowseFullAddressSpace found {Count} references on server in {Duration}ms.",
                referenceDescriptions.Count,
                stopWatch.ElapsedMilliseconds);

            if (m_verbose)
            {
                foreach (ReferenceDescription reference in result)
                {
                    m_logger.LogInformation(
                        "NodeId {NodeId} {NodeClass} {BrowseName}",
                        reference.NodeId,
                        reference.NodeClass,
                        reference.BrowseName);
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
        public async Task<ComplexTypeSystem> LoadTypeSystemAsync(
            ISession session,
            CancellationToken ct = default)
        {
            m_logger.LogInformation("Load the server type system.");

            var stopWatch = new Stopwatch();
            stopWatch.Start();

            var complexTypeSystem = new ComplexTypeSystem(session, m_telemetry);
            await complexTypeSystem.LoadAsync(throwOnError: true, ct: ct).ConfigureAwait(false);

            stopWatch.Stop();

            m_logger.LogInformation(
                "Loaded {Count} types took {Duration}ms.",
                complexTypeSystem.GetDefinedTypes().Length,
                stopWatch.ElapsedMilliseconds);

            if (m_verbose)
            {
                m_logger.LogInformation("Custom types defined for this session:");
                foreach (Type type in complexTypeSystem.GetDefinedTypes())
                {
                    m_logger.LogInformation("{Namespace}.{TypeName}", type.Namespace, type.Name);
                }

                m_logger.LogInformation(
                    "Loaded {Count} dictionaries:", complexTypeSystem.DataTypeSystem.Count);
                foreach (KeyValuePair<NodeId, DataDictionary> dictionary in complexTypeSystem
                    .DataTypeSystem)
                {
                    m_logger.LogInformation(" + {DictionaryName}", dictionary.Value.Name);
                    foreach (KeyValuePair<NodeId, QualifiedName> type in dictionary.Value.DataTypes)
                    {
                        m_logger.LogInformation(" -- {NodeId}:{BrowseName}", type.Key, type.Value);
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
        /// The NodeCache needs this information to function properly with subtypes
        /// of hierarchical calls.
        /// </remarks>
        /// <param name="session">The session to use</param>
        private static Task FetchReferenceIdTypesAsync(
            ISession session,
            CancellationToken ct = default)
        {
            NamespaceTable namespaceUris = session.NamespaceUris;
            IEnumerable<ExpandedNodeId> referenceTypes = ReferenceTypeIds.Identifiers
                .Select(nodeId => NodeId.ToExpandedNodeId(nodeId, namespaceUris));
            return session.FetchTypeTreeAsync([.. referenceTypes], ct);
        }

        /// <summary>
        /// Output all values as JSON.
        /// </summary>
        public async Task<ResultSet<DataValue>> ReadAllValuesAsync(
            IUAClient uaClient,
            NodeIdCollection variableIds,
            CancellationToken ct = default)
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
                                m_logger.LogInformation("Read {NodeId}", variableId);
                                DataValue value = await uaClient
                                    .Session.ReadValueAsync(variableId, ct)
                                    .ConfigureAwait(false);
                                values.Add(value);
                                errors.Add(value.StatusCode);

                                if (ServiceResult.IsNotBad(value.StatusCode))
                                {
                                    string valueString = FormatValueAsJson(
                                        uaClient.Session.MessageContext,
                                        variableId.ToString(),
                                        value,
                                        JsonEncodingType.Compact);
                                    m_logger.LogInformation("{Value}", valueString);
                                }
                                else
                                {
                                    m_logger.LogInformation("Error: {StatusCode}", value.StatusCode);
                                }
                            }
                            catch (ServiceResultException sre)
                            {
                                m_logger.LogError(sre, "Error");
                                values.Add(new DataValue(sre.StatusCode));
                                errors.Add(sre.Result);
                            }
                        }
                    }
                    else
                    {
                        (values, errors) = await uaClient.Session.ReadValuesAsync(variableIds, ct)
                            .ConfigureAwait(false);

                        int ii = 0;
                        foreach (DataValue value in values)
                        {
                            if (ServiceResult.IsNotBad(errors[ii]))
                            {
                                string valueString = FormatValueAsJson(
                                    uaClient.Session.MessageContext,
                                    variableIds[ii].ToString(),
                                    value,
                                    JsonEncodingType.Compact);
                                m_logger.LogInformation("{Value}", valueString);
                            }
                            else
                            {
                                m_logger.LogInformation("Error: {StatusCode}", value.StatusCode);
                            }
                            ii++;
                        }
                    }

                    retrySingleRead = false;
                }
                catch (ServiceResultException sre) when (sre.StatusCode == StatusCodes
                    .BadEncodingLimitsExceeded)
                {
                    m_logger.LogInformation("Retry to read the values due to error: {Error}", sre.Message);
                    retrySingleRead = !retrySingleRead;
                }
            } while (retrySingleRead);

            return ResultSet.From(values, errors);
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
            uint keepAliveCount,
            CancellationToken ct = default)
        {
            if (uaClient.Session == null || !uaClient.Session.Connected)
            {
                m_logger.LogInformation("Session not connected!");
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
                var subscription = new Subscription(session.DefaultSubscription)
                {
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
                    FastKeepAliveCallback = FastKeepAliveNotification
                };
                session.AddSubscription(subscription);

                // Create the subscription on Server side
                await subscription.CreateAsync(ct).ConfigureAwait(false);
                m_logger.LogInformation(
                    "New Subscription created with SubscriptionId = {SubscriptionId}.",
                    subscription.Id);

                // Create MonitoredItems for data changes
                foreach (Node item in variableIds)
                {
                    var monitoredItem = new MonitoredItem(subscription.DefaultItem)
                    {
                        StartNodeId = item.NodeId,
                        AttributeId = Attributes.Value,
                        SamplingInterval = samplingInterval,
                        DisplayName = item.DisplayName.Text ?? item.BrowseName.Name ?? "unknown",
                        QueueSize = queueSize,
                        DiscardOldest = true,
                        MonitoringMode = MonitoringMode.Reporting
                    };
                    subscription.AddItem(monitoredItem);
                    if (subscription.CurrentKeepAliveCount > 1000)
                    {
                        break;
                    }
                }

                // Create the monitored items on Server side
                await subscription.ApplyChangesAsync(ct).ConfigureAwait(false);
                m_logger.LogInformation(
                    "MonitoredItems {Count} created for SubscriptionId = {SubscriptionId}.",
                    subscription.MonitoredItemCount,
                    subscription.Id);
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "Subscribe error");
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
                var jsonWriter = new JsonTextWriter(stringWriter)
                {
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
        private void FastKeepAliveNotification(
            Subscription subscription,
            NotificationData notification)
        {
            try
            {
                m_logger.LogInformation(
                    "Keep Alive  : Id={SubscriptionId} PublishTime={PublishTime} SequenceNumber={SequenceNumber}.",
                    subscription.Id,
                    notification.PublishTime,
                    notification.SequenceNumber);
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "FastKeepAliveNotification error");
            }
        }

        /// <summary>
        /// The fast data change notification callback.
        /// </summary>
        private void FastDataChangeNotification(
            Subscription subscription,
            DataChangeNotification notification,
            IList<string> stringTable)
        {
            try
            {
                m_logger.LogInformation(
                    "Notification: Id={SubscriptionId} PublishTime={PublishTime} SequenceNumber={SequenceNumber} Items={Count}.",
                    subscription.Id,
                    notification.PublishTime,
                    notification.SequenceNumber,
                    notification.MonitoredItems.Count);
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "FastDataChangeNotification error");
            }
        }

        /// <summary>
        /// Handle DataChange notifications from Server
        /// </summary>
        private void OnMonitoredItemNotification(
            MonitoredItem monitoredItem,
            MonitoredItemNotificationEventArgs e)
        {
            try
            {
                // Log MonitoredItem Notification event
                var notification = e.NotificationValue as MonitoredItemNotification;
                DateTime localTime = notification.Value.SourceTimestamp.ToLocalTime();
                m_logger.LogInformation(
                    "Notification: {SequenceNumber} \"{NodeId}\" and Value = {Value} at [{CurrentTime}].",
                    notification.Message.SequenceNumber,
                    monitoredItem.ResolvedNodeId,
                    notification.Value,
                    localTime.ToLongTimeString());
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "OnMonitoredItemNotification error");
            }
        }

        /// <summary>
        /// Handle Requested Event notifications from Server
        /// </summary>
        private void OnMonitoredItemEventNotification(
            MonitoredItem monitoredItem,
            MonitoredItemNotificationEventArgs e)
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
                                DateTime currentTime = field.GetDateTime();
                                TimeSpan timeSpan = currentTime - m_lastEventTime;
                                m_lastEventTime = currentTime;
                                m_processedEvents++;
                                if (m_processedEvents > 1)
                                {
                                    m_logger.LogInformation(
                                        "Event Received - total count = {Count}, time since last event = {TimeBetweenEvents} seconds",
                                        m_processedEvents,
                                        timeSpan.Seconds);
                                }
                                else
                                {
                                    m_logger.LogInformation(
                                        "Event Received - total count = {Count}",
                                        m_processedEvents);
                                }
                            }
                            catch (Exception ex)
                            {
                                m_logger.LogError(ex,
                                    "Unexpected error retrieving Event Time Field Value");
                            }
                        }

                        m_logger.LogInformation(
                            "\tField [{Index}] \"{Name}\" = [{Value}]",
                            entry.Key,
                            fieldName,
                            field);
                    }
                }
            }
            catch (Exception ex)
            {
                m_logger.LogError(ex, "OnMonitoredItemEventNotification error");
            }
        }

        /// <summary>
        /// Event handler to defer publish response sequence number acknowledge.
        /// </summary>
        private void DeferSubscriptionAcknowledge(
            ISession session,
            PublishSequenceNumbersToAcknowledgeEventArgs e)
        {
            // for testing keep the latest sequence numbers for a while
            const int ackDelay = 5;
            if (e.AcknowledgementsToSend.Count > 0)
            {
                // defer latest sequence numbers
                var deferredItems = e
                    .AcknowledgementsToSend.OrderByDescending(s => s.SequenceNumber)
                    .Take(ackDelay)
                    .ToList();
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
        private static ArrayOf<BrowseDescription> CreateBrowseDescriptionCollectionFromNodeId(
            ArrayOf<NodeId> nodeIdCollection,
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
        /// Exports nodes to a NodeSet2 XML file.
        /// </summary>
        /// <param name="session">The session to use for exporting.</param>
        /// <param name="nodes">The list of nodes to export.</param>
        /// <param name="filePath">The path where the NodeSet2 XML file will be saved.</param>
        public void ExportNodesToNodeSet2(ISession session, IList<INode> nodes, string filePath)
        {
            m_logger.LogInformation("Exporting {Count} nodes to {FilePath}...", nodes.Count, filePath);

            var stopwatch = new Stopwatch();
            stopwatch.Start();

            using var outputStream = new FileStream(filePath, FileMode.Create);
            var systemContext = new SystemContext(m_telemetry)
            {
                NamespaceUris = session.NamespaceUris,
                ServerUris = session.ServerUris
            };

            CoreClientUtils.ExportNodesToNodeSet2(systemContext, nodes, outputStream);

            stopwatch.Stop();

            m_logger.LogInformation(
                "Exported {Count} nodes to {FilePath} in {Duration}ms",
                nodes.Count,
                filePath,
                stopwatch.ElapsedMilliseconds);
        }

        /// <summary>
        /// Exports nodes to separate NodeSet2 XML files, one per namespace.
        /// Excludes OPC Foundation companion specifications (namespaces starting with http://opcfoundation.org/UA/).
        /// </summary>
        /// <param name="session">The session to use for exporting.</param>
        /// <param name="nodes">The list of nodes to export.</param>
        /// <param name="outputDirectory">The directory where NodeSet2 XML files will be saved.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>A dictionary mapping namespace URI to the file path of the exported NodeSet2 file.</returns>
        /// <exception cref="ArgumentNullException">Thrown when session, nodes, or outputDirectory is null.</exception>
        /// <exception cref="ArgumentException">Thrown when outputDirectory is empty or whitespace.</exception>
        public async Task<IReadOnlyDictionary<string, string>> ExportNodesToNodeSet2PerNamespaceAsync(
            ISession session,
            IList<INode> nodes,
            string outputDirectory,
            CancellationToken cancellationToken = default)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }
            if (string.IsNullOrWhiteSpace(outputDirectory))
            {
                throw new ArgumentException("Value cannot be null or whitespace.", nameof(outputDirectory));
            }

            m_logger.LogInformation(
                "Exporting {Count} nodes to separate NodeSet2 files per namespace in {Directory}...",
                nodes.Count,
                outputDirectory);

            var stopwatch = Stopwatch.StartNew();

            // Ensure output directory exists
            Directory.CreateDirectory(outputDirectory);

            // Group nodes by namespace, excluding OPC Foundation companion specs
            var nodesByNamespace = nodes
                .Where(node => node.NodeId.NamespaceIndex > 0) // Skip namespace 0 (OPC UA base)
                .GroupBy(node => node.NodeId.NamespaceIndex)
                .Where(group =>
                {
                    string namespaceUri = session.NamespaceUris.GetString(group.Key);
                    // Exclude OPC Foundation companion specifications
                    return
                        !string.IsNullOrEmpty(namespaceUri) &&
                        !namespaceUri.StartsWith(Namespaces.OpcUa, StringComparison.OrdinalIgnoreCase);
                })
                .ToDictionary(
                    group => group.Key,
                    group => group.ToList());

            var exportedFiles = new Dictionary<string, string>();

            // Export each namespace to its own file
            foreach (KeyValuePair<ushort, List<INode>> kvp in nodesByNamespace)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string namespaceUri = session.NamespaceUris.GetString(kvp.Key);

                // Create a safe filename from the namespace URI

                string fileName = CreateSafeFileName(namespaceUri, kvp.Key);
                string filePath = Path.Combine(outputDirectory, fileName);

                m_logger.LogInformation(
                    "Exporting namespace {NamespaceIndex} ({NamespaceUri}): {Count} nodes to {FilePath}",
                    kvp.Key,
                    namespaceUri,
                    kvp.Value.Count,
                    filePath);

                await Task.Run(() =>
                {
                    using var outputStream = new FileStream(filePath, FileMode.Create);
                    var systemContext = new SystemContext(m_telemetry)
                    {
                        NamespaceUris = session.NamespaceUris,
                        ServerUris = session.ServerUris
                    };

                    CoreClientUtils.ExportNodesToNodeSet2(systemContext, kvp.Value, outputStream, NodeSetExportOptions.Complete);
                }, cancellationToken).ConfigureAwait(false);

                exportedFiles[namespaceUri] = filePath;
            }

            stopwatch.Stop();

            m_logger.LogInformation(
                "Exported {NamespaceCount} namespaces ({NodeCount} total nodes) in {Duration}ms",
                exportedFiles.Count,
                nodes.Count,
                stopwatch.ElapsedMilliseconds);

            return exportedFiles;
        }

        /// <summary>
        /// Creates a safe filename from a namespace URI.
        /// </summary>
        /// <param name="namespaceUri">The namespace URI.</param>
        /// <param name="namespaceIndex">The namespace index (used as fallback).</param>
        /// <returns>A safe filename for the NodeSet2 export.</returns>
        private static string CreateSafeFileName(string namespaceUri, ushort namespaceIndex)
        {
            // Extract meaningful part from URI
            string fileName = namespaceUri
                .Replace("http://", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("https://", string.Empty, StringComparison.OrdinalIgnoreCase)
                .Replace("urn:", string.Empty, StringComparison.OrdinalIgnoreCase);

            // Replace invalid filename characters
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                fileName = fileName.Replace(c, '_');
            }

            // Additional cleanup for common URI characters
            fileName = fileName
                .Replace('/', '_')
                .Replace('\\', '_')
                .Replace(':', '_')
                .TrimEnd('_');

            // Limit length and ensure uniqueness with namespace index
            if (fileName.Length > 200)
            {
                fileName = fileName[..200];
            }

            return $"{fileName}_ns{namespaceIndex}.xml";
        }

        /// <summary>
        /// Create the continuation point collection from the browse result
        /// collection for the BrowseNext service.
        /// </summary>
        /// <param name="browseResultCollection">The browse result collection to use.</param>
        /// <returns>The collection of continuation points for the BrowseNext service.</returns>
        private static ArrayOf<ByteString> PrepareBrowseNext(
            ArrayOf<BrowseResult> browseResultCollection)
        {
            var continuationPoints = new ByteStringCollection();
            foreach (BrowseResult browseResult in browseResultCollection)
            {
                if (!browseResult.ContinuationPoint.IsEmpty)
                {
                    continuationPoints.Add(browseResult.ContinuationPoint);
                }
            }
            return continuationPoints;
        }

        private void ValidateResponse<TRequest, TResponse>(
            ArrayOf<TRequest> requests,
            ArrayOf<TResponse> responses)
        {
            if (m_validate != null)
            {
                m_validate(requests.ToArray(), responses.ToArray());
            }
            else
            {
                ClientBase.ValidateResponse(responses, requests);
            }
        }

        private readonly Action<IList, IList> m_validate;
        private readonly ITelemetryContext m_telemetry;
        private readonly ILogger m_logger;
        private readonly ManualResetEvent m_quitEvent;
        private readonly bool m_verbose;
        private readonly Dictionary<int, QualifiedNameCollection> m_desiredEventFields;
        private int m_processedEvents;
        private DateTime m_lastEventTime = DateTime.Now;
    }
}
