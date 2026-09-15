/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Plugins.Companions;

namespace UaLens.Tests.Companions;

/// <summary>
/// In-memory service responses, not a server. Every mutation must have an
/// explicit handler and the session stays borrowed throughout each test.
/// </summary>
internal sealed class IndustrialCompanionTestSession
{
    public IndustrialCompanionTestSession(bool includeModels = true)
    {
        Telemetry = DefaultTelemetry.Create(static _ => { });
        MessageContext = ServiceMessageContext.Create(Telemetry);
        NamespaceUris = MessageContext.NamespaceUris;
        NamespaceUris.GetIndexOrAppend("urn:ualens:test:namespace-padding");
        if (includeModels)
        {
            foreach (string uri in s_modelUris)
            {
                NamespaceUris.GetIndexOrAppend(uri);
            }
        }
        InstanceNamespaceIndex = NamespaceUris.GetIndexOrAppend("urn:ualens:test:instances");
        Session = new Mock<ISession>(MockBehavior.Strict);
        Session.SetupGet(session => session.NamespaceUris).Returns(NamespaceUris);
        Session.SetupGet(session => session.MessageContext).Returns(MessageContext);
        Session.Setup(session => session.BrowseAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(), It.IsAny<uint>(),
                It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
            .Returns((RequestHeader? _, ViewDescription? _, uint maximum,
                ArrayOf<BrowseDescription> descriptions, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                Assert.That(maximum, Is.InRange(1u, 64u));
                Assert.That(descriptions, Has.Count.EqualTo(1));
                m_browseCalls.Add(descriptions[0]);
                BrowseResult result = BrowseHandler?.Invoke(descriptions[0], token) ?? Browse(descriptions[0]);
                return new ValueTask<BrowseResponse>(new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [result]
                });
            });
        Session.Setup(session => session.BrowseNextAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<bool>(),
                It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()))
            .Returns((RequestHeader? _, bool release, ArrayOf<ByteString> points, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                if (release)
                {
                    foreach (ByteString point in points)
                    {
                        m_released.Add(point);
                    }
                }
                BrowseNextResponse response = NextHandler?.Invoke(release, points, token) ?? new BrowseNextResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new BrowseResult { StatusCode = StatusCodes.Good }]
                };
                return new ValueTask<BrowseNextResponse>(response);
            });
        Session.Setup(session => session.ReadAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
            .Returns((RequestHeader? _, double _, TimestampsToReturn _,
                ArrayOf<ReadValueId> reads, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                var results = new List<DataValue>();
                foreach (ReadValueId read in reads)
                {
                    results.Add(ReadHandler?.Invoke(read, token) ?? Read(read));
                }
                return new ValueTask<ReadResponse>(new ReadResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [.. results]
                });
            });
        Session.Setup(session => session.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
            .Returns((RequestHeader? _, ArrayOf<BrowsePath> paths, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                var results = new List<BrowsePathResult>();
                foreach (BrowsePath path in paths)
                {
                    results.Add(TranslateHandler?.Invoke(path, token) ?? Translate(path));
                }
                return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(new TranslateBrowsePathsToNodeIdsResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [.. results]
                });
            });
        Session.Setup(session => session.CallAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
            .Returns((RequestHeader? _, ArrayOf<CallMethodRequest> requests, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                Assert.That(requests, Has.Count.EqualTo(1));
                m_calls.Add(requests[0]);
                CallMethodResult result = CallHandler?.Invoke(requests[0], token)
                    ?? throw new InvalidOperationException("An unexpected Method was called.");
                return new ValueTask<CallResponse>(new CallResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [result]
                });
            });
    }

    public ITelemetryContext Telemetry { get; }

    public IServiceMessageContext MessageContext { get; }

    public NamespaceTable NamespaceUris { get; }

    public ushort InstanceNamespaceIndex { get; }

    public Mock<ISession> Session { get; }

    public ArrayOf<CallMethodRequest> Calls => [.. m_calls];

    public ArrayOf<BrowseDescription> BrowseCalls => [.. m_browseCalls];

    public ArrayOf<ByteString> Released => [.. m_released];

    public Func<BrowseDescription, CancellationToken, BrowseResult>? BrowseHandler { get; set; }

    public Func<bool, ArrayOf<ByteString>, CancellationToken, BrowseNextResponse>? NextHandler { get; set; }

    public Func<ReadValueId, CancellationToken, DataValue>? ReadHandler { get; set; }

    public Func<BrowsePath, CancellationToken, BrowsePathResult>? TranslateHandler { get; set; }

    public Func<CallMethodRequest, CancellationToken, CallMethodResult>? CallHandler { get; set; }

    public CompanionContext Context(int maxTargets = 128, int maxFields = 256)
    {
        return new CompanionContext(Session.Object, Telemetry, maxTargets, maxFields);
    }

    public NodeId Id(string identifier)
    {
        return new NodeId(identifier, InstanceNamespaceIndex);
    }

    public NodeId Resolve(ExpandedNodeId identifier)
    {
        return ExpandedNodeId.ToNodeId(identifier, NamespaceUris);
    }

    public void AddObject(NodeId nodeId, ExpandedNodeId typeId, string? name = null)
    {
        var reference = new ReferenceDescription
        {
            NodeId = new ExpandedNodeId(nodeId),
            NodeClass = NodeClass.Object,
            TypeDefinition = new ExpandedNodeId(Resolve(typeId)),
            BrowseName = new QualifiedName(name ?? nodeId.ToString(), nodeId.NamespaceIndex),
            DisplayName = new LocalizedText(name ?? nodeId.ToString()),
            IsForward = true,
            ReferenceTypeId = ReferenceTypeIds.Organizes
        };
        m_objects[nodeId] = reference;
        SetValue(nodeId, Variant.From((int)NodeClass.Object), Attributes.NodeClass);
        SetValue(nodeId, Variant.From(reference.DisplayName), Attributes.DisplayName);
        SetReferences(nodeId, BrowseDirection.Forward, ReferenceTypeIds.HasTypeDefinition,
        [
            new ReferenceDescription
            {
                NodeId = new ExpandedNodeId(Resolve(typeId)),
                NodeClass = NodeClass.ObjectType,
                IsForward = true,
                ReferenceTypeId = ReferenceTypeIds.HasTypeDefinition
            }
        ]);
    }

    public void AddChild(NodeId parent, NodeId child, string? namespaceUri = null, string? name = null)
    {
        ReferenceDescription reference = m_objects[child];
        if (namespaceUri is not null && name is not null)
        {
            m_children[(parent, new QualifiedName(name, (ushort)NamespaceUris.GetIndex(namespaceUri)))] = child;
        }
        else
        {
            m_children[(parent, reference.BrowseName)] = child;
        }
        AppendReference(parent, BrowseDirection.Forward, ReferenceTypeIds.HierarchicalReferences, reference);
        AppendReference(child, BrowseDirection.Inverse, ReferenceTypeIds.HierarchicalReferences,
            new ReferenceDescription
            {
                NodeId = new ExpandedNodeId(parent),
                NodeClass = NodeClass.Object,
                IsForward = false,
                ReferenceTypeId = ReferenceTypeIds.Organizes
            });
    }

    public NodeId AddProperty(NodeId parent, string namespaceUri, string name, Variant value)
    {
        NodeId nodeId = Id("property-" + ++m_nextProperty);
        m_children[(parent, new QualifiedName(name, (ushort)NamespaceUris.GetIndex(namespaceUri)))] = nodeId;
        SetValue(nodeId, value);
        return nodeId;
    }

    public void SetValue(NodeId nodeId, Variant value, uint attribute = Attributes.Value, StatusCode status = default)
    {
        m_values[(nodeId, attribute)] = new DataValue(value, status);
    }

    public void SetReferences(
        NodeId parent,
        BrowseDirection direction,
        NodeId referenceType,
        ArrayOf<ReferenceDescription> references)
    {
        m_references[(parent, direction, referenceType)] = references;
    }

    public BrowseResult Browse(BrowseDescription description)
    {
        m_references.TryGetValue(
            (description.NodeId, description.BrowseDirection, description.ReferenceTypeId),
            out ArrayOf<ReferenceDescription> references);
        return new BrowseResult { StatusCode = StatusCodes.Good, References = references };
    }

    public DataValue Read(ReadValueId read)
    {
        return m_values.TryGetValue((read.NodeId, read.AttributeId), out DataValue value)
            ? value
            : DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown);
    }

    public BrowsePathResult Translate(BrowsePath path)
    {
        NodeId current = path.StartingNode;
        foreach (RelativePathElement element in path.RelativePath.Elements)
        {
            if (!m_children.TryGetValue((current, element.TargetName), out current))
            {
                return new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch };
            }
        }
        return new BrowsePathResult
        {
            StatusCode = StatusCodes.Good,
            Targets =
                [new BrowsePathTarget { TargetId = new ExpandedNodeId(current), RemainingPathIndex = uint.MaxValue }]
        };
    }

    public static CallMethodResult Good(ArrayOf<Variant> outputs = default)
    {
        return new CallMethodResult { StatusCode = StatusCodes.Good, OutputArguments = outputs };
    }

    public static Variant Field(ArrayOf<CompanionValue> fields, string name)
    {
        foreach (CompanionValue field in fields)
        {
            if (field.Name == name)
            {
                return field.Value;
            }
        }
        throw new AssertionException("The expected field was not returned: " + name);
    }

    private void AppendReference(
        NodeId parent,
        BrowseDirection direction,
        NodeId referenceType,
        ReferenceDescription reference)
    {
        var key = (parent, direction, referenceType);
        m_references.TryGetValue(key, out ArrayOf<ReferenceDescription> previous);
        m_references[key] = [.. previous, reference];
    }

    private readonly Dictionary<NodeId, ReferenceDescription> m_objects = [];
    private readonly Dictionary<(NodeId Parent, QualifiedName Name), NodeId> m_children = [];
    private readonly Dictionary<(NodeId Node, uint Attribute), DataValue> m_values = [];
    private readonly Dictionary<(NodeId Parent, BrowseDirection Direction, NodeId Type),
        ArrayOf<ReferenceDescription>> m_references = [];
    private readonly List<CallMethodRequest> m_calls = [];
    private readonly List<BrowseDescription> m_browseCalls = [];
    private readonly List<ByteString> m_released = [];
    private int m_nextProperty;
    private static readonly ArrayOf<string> s_modelUris =
    [
        Opc.Ua.Di.Namespaces.OpcUaDi,
        Opc.Ua.ISA95.Namespaces.ISA95,
        Opc.Ua.ISA95.JobControl.V1.Namespaces.ISA95JobControlV1,
        Opc.Ua.ISA95.JobControl.V2.Namespaces.ISA95JobControlV2,
        Opc.Ua.XRegistry.XRegistryWellKnown.XRegistryNamespaceUri,
        Opc.Ua.WotCon.Namespaces.WotCon
    ];
}
