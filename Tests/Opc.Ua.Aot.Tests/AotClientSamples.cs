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

using System.Text;
using Opc.Ua;
using Opc.Ua.Client;

namespace Opc.Ua.Aot.Tests;

/// <summary>
/// Simplified OPC UA client samples for AOT integration testing.
/// Each method performs an OPC UA operation and asserts the results
/// using TUnit assertions.
/// </summary>
public static class AotClientSamples
{
    private const int kMaxSearchDepth = 128;

    /// <summary>
    /// Read a list of nodes from the server and assert values are returned.
    /// </summary>
    public static async Task ReadNodesAsync(ISession session)
    {
        ArrayOf<ReadValueId> nodesToRead =
        [
            new ReadValueId
            {
                NodeId = VariableIds.Server_ServerStatus,
                AttributeId = Attributes.Value
            },
            new ReadValueId
            {
                NodeId = VariableIds.Server_ServerStatus_StartTime,
                AttributeId = Attributes.BrowseName
            },
            new ReadValueId
            {
                NodeId = VariableIds.Server_ServerStatus_StartTime,
                AttributeId = Attributes.Value
            }
        ];

        ReadResponse response = await session.ReadAsync(
            null, 0, TimestampsToReturn.Both,
            nodesToRead, CancellationToken.None).ConfigureAwait(false);

        await Assert.That(response).IsNotNull();
        await Assert.That(response.Results.Count).IsEqualTo(nodesToRead.Count);

        foreach (DataValue result in response.Results.ToList())
        {
            await Assert.That(StatusCode.IsGood(result.StatusCode)).IsTrue();
        }

        // Also read the NamespaceArray
        DataValue namespaceArray = await session.ReadValueAsync(
            VariableIds.Server_NamespaceArray, CancellationToken.None).ConfigureAwait(false);

        await Assert.That(namespaceArray).IsNotNull();
        await Assert.That(StatusCode.IsGood(namespaceArray.StatusCode)).IsTrue();
    }

    /// <summary>
    /// Write a list of nodes to the server and assert success.
    /// </summary>
    public static async Task WriteNodesAsync(ISession session)
    {
        ArrayOf<WriteValue> nodesToWrite =
        [
            new WriteValue
            {
                NodeId = NodeId.Parse("ns=2;s=Scalar_Static_Int32"),
                AttributeId = Attributes.Value,
                Value = new DataValue(Variant.From(100))
            },
            new WriteValue
            {
                NodeId = NodeId.Parse("ns=2;s=Scalar_Static_Float"),
                AttributeId = Attributes.Value,
                Value = new DataValue(Variant.From(100.5f))
            },
            new WriteValue
            {
                NodeId = NodeId.Parse("ns=2;s=Scalar_Static_String"),
                AttributeId = Attributes.Value,
                Value = new DataValue(Variant.From("String Test"))
            }
        ];

        WriteResponse response = await session.WriteAsync(
            null, nodesToWrite, CancellationToken.None).ConfigureAwait(false);

        await Assert.That(response).IsNotNull();
        await Assert.That(response.Results.Count).IsEqualTo(nodesToWrite.Count);

        foreach (StatusCode writeResult in response.Results.ToList())
        {
            await Assert.That(StatusCode.IsGood(writeResult)).IsTrue();
        }
    }

    /// <summary>
    /// Browse the Server node and assert references are found.
    /// </summary>
    public static async Task BrowseAsync(ISession session)
    {
        var browser = new Browser(session)
        {
            BrowseDirection = BrowseDirection.Forward,
            NodeClassMask = (int)NodeClass.Object | (int)NodeClass.Variable,
            ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
            IncludeSubtypes = true
        };

        ArrayOf<ReferenceDescription> browseResults =
            await browser.BrowseAsync(ObjectIds.Server, CancellationToken.None).ConfigureAwait(false);

        await Assert.That(browseResults.Count).IsGreaterThan(0);

        foreach (ReferenceDescription result in browseResults.ToList())
        {
            await Assert.That(result.DisplayName.Text).IsNotNull();
        }
    }

    /// <summary>
    /// Call the Add method on the server and assert result.
    /// </summary>
    public static async Task CallMethodAsync(ISession session)
    {
        var objectId = NodeId.Parse("ns=2;s=Methods");
        var methodId = NodeId.Parse("ns=2;s=Methods_Add");

        ArrayOf<Variant> outputArguments = await session.CallAsync(
            objectId, methodId, CancellationToken.None,
            (float)10.5, (uint)10).ConfigureAwait(false);

        await Assert.That(outputArguments.Count).IsGreaterThan(0);
    }

    /// <summary>
    /// Create a subscription with monitored items and assert creation.
    /// </summary>
    public static async Task SubscribeToDataChangesAsync(ISession session)
    {
        const int publishingInterval = 1000;
        const int samplingInterval = 1000;
        const uint queueSize = 10;

#pragma warning disable CA2000 // Dispose objects before losing scope
        var subscription = new Subscription(session.DefaultSubscription)
        {
            DisplayName = "AotTest Subscription",
            PublishingEnabled = true,
            PublishingInterval = publishingInterval,
            LifetimeCount = 0,
            MinLifetimeInterval = 3,
            KeepAliveCount = 5
        };
#pragma warning restore CA2000 // Dispose objects before losing scope

        session.AddSubscription(subscription);

        await subscription.CreateAsync(CancellationToken.None).ConfigureAwait(false);

        await Assert.That(subscription.Id).IsGreaterThan((uint)0);
        await Assert.That(subscription.Created).IsTrue();

        // Add monitored items
        var intMonitoredItem = new MonitoredItem(subscription.DefaultItem)
        {
            StartNodeId = NodeId.Parse("ns=2;s=Scalar_Simulation_Int32"),
            AttributeId = Attributes.Value,
            DisplayName = "Int32 Variable",
            SamplingInterval = samplingInterval,
            QueueSize = queueSize,
            DiscardOldest = true
        };
        subscription.AddItem(intMonitoredItem);

        var floatMonitoredItem = new MonitoredItem(subscription.DefaultItem)
        {
            StartNodeId = NodeId.Parse("ns=2;s=Scalar_Simulation_Float"),
            AttributeId = Attributes.Value,
            DisplayName = "Float Variable",
            SamplingInterval = samplingInterval,
            QueueSize = queueSize
        };
        subscription.AddItem(floatMonitoredItem);

        var stringMonitoredItem = new MonitoredItem(subscription.DefaultItem)
        {
            StartNodeId = NodeId.Parse("ns=2;s=Scalar_Simulation_String"),
            AttributeId = Attributes.Value,
            DisplayName = "String Variable",
            SamplingInterval = samplingInterval,
            QueueSize = queueSize
        };
        subscription.AddItem(stringMonitoredItem);

        await subscription.ApplyChangesAsync(CancellationToken.None).ConfigureAwait(false);

        await Assert.That((int)subscription.MonitoredItemCount).IsEqualTo(3);
    }

    /// <summary>
    /// Fetch all nodes via NodeCache and assert nodes are found.
    /// </summary>
    public static async Task<IList<INode>> FetchAllNodesNodeCacheAsync(
        ISession session,
        NodeId startingNode)
    {
        var nodeDictionary = new Dictionary<ExpandedNodeId, INode>();
        ArrayOf<NodeId> references = [ReferenceTypeIds.HierarchicalReferences];
        ArrayOf<ExpandedNodeId> nodesToBrowse = [startingNode];

        // Prime NodeCache with reference types
        session.NodeCache.Clear();
        await FetchReferenceIdTypesAsync(session).ConfigureAwait(false);

        int searchDepth = 0;
        while (nodesToBrowse.Count > 0 && searchDepth < kMaxSearchDepth)
        {
            searchDepth++;
            ArrayOf<INode> response = await session.NodeCache
                .FindReferencesAsync(nodesToBrowse, references, false, true, CancellationToken.None)
                .ConfigureAwait(false);

            var nextNodesToBrowse = new List<ExpandedNodeId>();
            foreach (INode node in response)
            {
                if (!nodeDictionary.ContainsKey(node.NodeId))
                {
                    nodeDictionary[node.NodeId] = node;
                }
            }
            nodesToBrowse = nextNodesToBrowse;
        }

        var result = nodeDictionary.Values.ToList();
        result.Sort((x, y) => x.NodeId.CompareTo(y.NodeId));

        await Assert.That(result.Count).IsGreaterThan(0);

        return result;
    }

    /// <summary>
    /// Browse the full address space and assert references are found.
    /// </summary>
    public static async Task<ArrayOf<ReferenceDescription>> BrowseFullAddressSpaceAsync(
        ISession session,
        NodeId startingNode)
    {
        const int kMaxReferencesPerNode = 1000;
        var browseTemplate = new BrowseDescription
        {
            NodeId = startingNode.IsNull ? ObjectIds.RootFolder : startingNode,
            BrowseDirection = BrowseDirection.Forward,
            ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
            IncludeSubtypes = true,
            NodeClassMask = 0,
            ResultMask = (uint)BrowseResultMask.All
        };
        ArrayOf<BrowseDescription> browseDescriptionCollection =
            CreateBrowseDescriptionCollectionFromNodeId(
                [startingNode.IsNull ? ObjectIds.RootFolder : startingNode],
                browseTemplate);

        var referenceDescriptions = new Dictionary<ExpandedNodeId, ReferenceDescription>();

        int searchDepth = 0;
        uint maxNodesPerBrowse = session.OperationLimits.MaxNodesPerBrowse;
        while (browseDescriptionCollection.Count > 0 && searchDepth < kMaxSearchDepth)
        {
            searchDepth++;

            var allBrowseResults = new List<BrowseResult>();
            bool repeatBrowse;
            ArrayOf<BrowseResult> browseResultCollection = default;
            var unprocessedOperations = new List<BrowseDescription>();
            do
            {
                if (maxNodesPerBrowse >= browseDescriptionCollection.Count)
                {
                    maxNodesPerBrowse = 0;
                }

                ArrayOf<BrowseDescription> browseCollection =
                    maxNodesPerBrowse == 0
                        ? browseDescriptionCollection
                        : browseDescriptionCollection[..(int)maxNodesPerBrowse];
                repeatBrowse = false;

                BrowseResponse browseResponse = await session.BrowseAsync(
                    null, null, kMaxReferencesPerNode,
                    browseCollection, CancellationToken.None).ConfigureAwait(false);
                browseResultCollection = browseResponse.Results;
                ClientBase.ValidateResponse(browseResultCollection, browseCollection);

                int ii = 0;
                foreach (BrowseResult browseResult in browseResultCollection)
                {
                    if (browseResult.StatusCode == StatusCodes.BadNoContinuationPoints)
                    {
                        unprocessedOperations.Add(browseCollection[ii++]);
                        continue;
                    }
                    allBrowseResults.Add(browseResult);
                    ii++;
                }
            } while (repeatBrowse);

            browseDescriptionCollection = maxNodesPerBrowse == 0
                ? default
                : browseDescriptionCollection[(int)maxNodesPerBrowse..];

            // Browse next
            ArrayOf<ByteString> continuationPoints = PrepareBrowseNext(browseResultCollection);
            while (continuationPoints.Count > 0)
            {
                BrowseNextResponse browseNextResult = await session.BrowseNextAsync(
                    null, false, continuationPoints, CancellationToken.None).ConfigureAwait(false);
                ArrayOf<BrowseResult> browseNextResultCollection = browseNextResult.Results;
                ClientBase.ValidateResponse(browseNextResultCollection, continuationPoints);
                allBrowseResults.AddRange(browseNextResultCollection);
                continuationPoints = PrepareBrowseNext(browseNextResultCollection);
            }

            // Build browse request for next level
            var browseTable = new List<NodeId>();
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
                                    reference.NodeId, session.NamespaceUris));
                        }
                    }
                }
            }
            browseDescriptionCollection = ArrayOf.Combine(
                browseDescriptionCollection,
                CreateBrowseDescriptionCollectionFromNodeId(browseTable, browseTemplate),
                unprocessedOperations);
        }

        var result = new List<ReferenceDescription>(referenceDescriptions.Values);
        result.Sort((x, y) => x.NodeId.CompareTo(y.NodeId));

        await Assert.That(result.Count).IsGreaterThan(0);

        return result;
    }

    /// <summary>
    /// Create a prettified JSON string of a DataValue.
    /// </summary>
    public static string FormatValueAsJson(
        IServiceMessageContext messageContext,
        string name,
        DataValue value,
        JsonEncoderOptions jsonEncodingType = null)
    {
        string textbuffer;
        using (var jsonEncoder = new JsonEncoder(messageContext, jsonEncodingType))
        {
            jsonEncoder.WriteDataValue(name, value);
            textbuffer = jsonEncoder.CloseAndReturnText();
        }

        using var doc = System.Text.Json.JsonDocument.Parse(textbuffer);
        using var stream = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(
            stream,
            new System.Text.Json.JsonWriterOptions { Indented = true }))
        {
            doc.WriteTo(writer);
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// Read all ReferenceTypeIds from the server that are not known by the client.
    /// </summary>
    private static Task FetchReferenceIdTypesAsync(ISession session)
    {
        NamespaceTable namespaceUris = session.NamespaceUris;
        var referenceTypes = ReferenceTypeIds.Identifiers
            .Select(nodeId => NodeId.ToExpandedNodeId(nodeId, namespaceUris))
            .ToArrayOf();
        return session.FetchTypeTreeAsync(referenceTypes, CancellationToken.None);
    }

    private static ArrayOf<BrowseDescription> CreateBrowseDescriptionCollectionFromNodeId(
        ArrayOf<NodeId> nodeIdCollection,
        BrowseDescription template)
    {
        var browseDescriptionCollection = new List<BrowseDescription>();
        foreach (NodeId nodeId in nodeIdCollection)
        {
            BrowseDescription browseDescription = CoreUtils.Clone(template);
            browseDescription.NodeId = nodeId;
            browseDescriptionCollection.Add(browseDescription);
        }
        return browseDescriptionCollection;
    }

    private static ArrayOf<ByteString> PrepareBrowseNext(
        ArrayOf<BrowseResult> browseResultCollection)
    {
        var continuationPoints = new List<ByteString>();
        foreach (BrowseResult browseResult in browseResultCollection)
        {
            if (!browseResult.ContinuationPoint.IsEmpty)
            {
                continuationPoints.Add(browseResult.ContinuationPoint);
            }
        }
        return continuationPoints;
    }
}
