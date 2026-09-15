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
/// Real client helpers run against these explicit OPC UA read/browse responses, never a live connection.
/// </summary>
internal sealed class CellProviderTestSession
{
    public CellProviderTestSession(string modelUri, int maxTargets = 128, int maxFields = 256)
    {
        ITelemetryContext telemetry = DefaultTelemetry.Create(static _ => { });
        ServiceMessageContext messageContext = ServiceMessageContext.Create(telemetry);
        NamespaceUris = messageContext.NamespaceUris;
        NamespaceIndex = (ushort)NamespaceUris.GetIndexOrAppend(modelUri);
        NamespaceUris.GetIndexOrAppend(Opc.Ua.Di.Namespaces.OpcUaDi);
        Session.SetupGet(session => session.NamespaceUris).Returns(NamespaceUris);
        Session.SetupGet(session => session.MessageContext).Returns(messageContext);
        Session.SetupGet(session => session.Factory).Returns(messageContext.Factory);
        Session.SetupGet(session => session.OperationLimits).Returns(new OperationLimits());
        Session.SetupGet(session => session.ServerCapabilities).Returns(new ServerCapabilities());
        Session.SetupGet(session => session.ContinuationPointPolicy).Returns(ContinuationPointPolicy.Default);
        Session.SetupGet(session => session.NodeCache).Returns(NodeCache.Object);
        Session.SetupGet(session => session.TypeTree).Returns(new TypeTable(NamespaceUris));
        Session.SetupGet(session => session.Endpoint)
            .Returns(new EndpointDescription { EndpointUrl = "opc.tcp://localhost:4840/CellTests" });
        NodeCache.Setup(cache => cache.IsTypeOfAsync(
                It.IsAny<NodeId>(), It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
            .Returns((NodeId subtype, NodeId supertype, CancellationToken _) =>
                ValueTask.FromResult(subtype == supertype));
        Session.Setup(session => session.BrowseAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
                It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
            .Returns((RequestHeader _, ViewDescription _, uint _, ArrayOf<BrowseDescription> requests,
                CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                var results = new List<BrowseResult>();
                for (int index = 0; index < requests.Count; index++)
                {
                    results.Add(new BrowseResult
                    {
                        StatusCode = StatusCodes.Good,
                        References = Browses.TryGetValue(requests[index].NodeId, out ArrayOf<ReferenceDescription> refs)
                            ? refs
                            : []
                    });
                }
                return ValueTask.FromResult(new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [.. results],
                    DiagnosticInfos = []
                });
            });
        Session.Setup(session => session.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
            .Returns((RequestHeader _, ArrayOf<BrowsePath> paths, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                var results = new List<BrowsePathResult>();
                for (int index = 0; index < paths.Count; index++)
                {
                    NodeId current = paths[index].StartingNode;
                    ArrayOf<RelativePathElement> elements = paths[index].RelativePath.Elements;
                    for (int part = 0; part < elements.Count; part++)
                    {
                        if (!Children.TryGetValue(
                            (current, elements[part].TargetName.Name ?? string.Empty), out NodeId child))
                        {
                            current = NodeId.Null;
                            break;
                        }
                        current = child;
                    }
                    results.Add(current.IsNull
                        ? new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch }
                        : new BrowsePathResult
                        {
                            StatusCode = StatusCodes.Good,
                            Targets = [new BrowsePathTarget { TargetId = current, RemainingPathIndex = uint.MaxValue }]
                        });
                }
                return ValueTask.FromResult(new TranslateBrowsePathsToNodeIdsResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [.. results],
                    DiagnosticInfos = []
                });
            });
        Session.Setup(session => session.ReadAsync(
                It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
            .Returns((RequestHeader _, double _, TimestampsToReturn _, ArrayOf<ReadValueId> requests,
                CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                var values = new List<DataValue>();
                for (int index = 0; index < requests.Count; index++)
                {
                    ReadValueId request = requests[index];
                    values.Add(Values.TryGetValue((request.NodeId, request.AttributeId), out DataValue value)
                        ? value
                        : DataValue.FromStatusCode(StatusCodes.BadNodeIdUnknown));
                }
                return ValueTask.FromResult(new ReadResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [.. values],
                    DiagnosticInfos = []
                });
            });
        Context = new CompanionContext(Session.Object, telemetry, maxTargets, maxFields);
    }

    public Mock<ISession> Session { get; } = new(MockBehavior.Strict);

    public Mock<INodeCache> NodeCache { get; } = new(MockBehavior.Strict);

    public NamespaceTable NamespaceUris { get; }

    public ushort NamespaceIndex { get; }

    public CompanionContext Context { get; }

    public Dictionary<NodeId, ArrayOf<ReferenceDescription>> Browses { get; } = [];

    public Dictionary<(NodeId Node, string Name), NodeId> Children { get; } = [];

    public Dictionary<(NodeId Node, uint Attribute), DataValue> Values { get; } = [];

    public ReferenceDescription Reference(NodeId nodeId, string name, NodeId typeDefinition)
    {
        return new ReferenceDescription
        {
            NodeId = nodeId,
            BrowseName = new QualifiedName(name, NamespaceIndex),
            DisplayName = new LocalizedText(name),
            NodeClass = NodeClass.Object,
            ReferenceTypeId = Opc.Ua.ReferenceTypeIds.Organizes,
            IsForward = true,
            TypeDefinition = typeDefinition
        };
    }

    public void AddValue(NodeId parent, string name, Variant value)
    {
        var nodeId = new NodeId($"property-{Children.Count + 1}", NamespaceIndex);
        Children.Add((parent, name), nodeId);
        Values.Add((nodeId, Attributes.Value), new DataValue(value));
        ArrayOf<ReferenceDescription> existing = Browses.TryGetValue(parent, out ArrayOf<ReferenceDescription> refs)
            ? refs
            : [];
        Browses[parent] =
        [
            .. existing,
            new ReferenceDescription
            {
                NodeId = nodeId,
                BrowseName = new QualifiedName(name, NamespaceIndex),
                DisplayName = new LocalizedText(name),
                NodeClass = NodeClass.Variable,
                ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasProperty,
                IsForward = true
            }
        ];
    }

    public void VerifyNoMutationOrSessionOwnership()
    {
        Session.Verify(session => session.CallAsync(
            It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Session.Verify(session => session.WriteAsync(
            It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<WriteValue>>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Session.Verify(session => session.CloseAsync(
            It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()), Times.Never);
        Session.Verify(session => session.Dispose(), Times.Never);
    }

    public static Variant Field(ArrayOf<CompanionValue> fields, string name)
    {
        for (int index = 0; index < fields.Count; index++)
        {
            if (fields[index].Name == name)
            {
                return fields[index].Value;
            }
        }
        Assert.Fail($"Expected field '{name}' was not returned.");
        return Variant.Null;
    }
}

/// <summary>
/// A deterministic enumerator that exposes whether the provider actually stops and disposes on failure.
/// </summary>
internal sealed class CellProviderTestEntries<T> : IAsyncEnumerable<T>, IAsyncEnumerator<T>
{
    public CellProviderTestEntries(ArrayOf<T> values)
    {
        m_values = values;
    }

    public T Current => m_values[m_index];

    public int Visited { get; private set; }

    public int Disposals { get; private set; }

    public Action? OnMove { get; set; }

    public IAsyncEnumerator<T> GetAsyncEnumerator(CancellationToken cancellationToken = default)
    {
        m_token = cancellationToken;
        m_index = -1;
        return this;
    }

    public ValueTask<bool> MoveNextAsync()
    {
        OnMove?.Invoke();
        m_token.ThrowIfCancellationRequested();
        bool available = ++m_index < m_values.Count;
        if (available)
        {
            Visited++;
        }
        return ValueTask.FromResult(available);
    }

    public ValueTask DisposeAsync()
    {
        Disposals++;
        return ValueTask.CompletedTask;
    }

    private readonly ArrayOf<T> m_values;
    private CancellationToken m_token;
    private int m_index = -1;
}
