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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Client.FileSystem;
using UaLens.Telemetry;

namespace UaLens.Tests.Administration;

/// <summary>
/// Finite protocol script for the real SDK file client. Unscripted methods fail;
/// all mutations, including Close, are checked in their expected wire order.
/// </summary>
internal sealed class FileSystemProtocolTestDriver
{
    public FileSystemProtocolTestDriver(int chunkSize = 4)
    {
        ServiceMessageContext messages = ServiceMessageContext.Create(new AppTelemetryContext(new LogRingBuffer(32)));
        Session.SetupGet(s => s.MessageContext).Returns(messages);
        Session.SetupGet(s => s.NamespaceUris).Returns(messages.NamespaceUris);
        var types = new Mock<ITypeTable>(MockBehavior.Strict);
        types.Setup(t => t.IsTypeOf(It.IsAny<NodeId>(), It.IsAny<NodeId>()))
            .Returns((NodeId candidate, NodeId parent) => candidate == parent);
        Session.SetupGet(s => s.TypeTree).Returns(types.Object);
        Session.Setup(s => s.FetchTypeTreeAsync(It.IsAny<ExpandedNodeId>(), It.IsAny<CancellationToken>()))
            .Returns((ExpandedNodeId _, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            });
        Session.Setup(s => s.BrowseAsync(
            It.IsAny<RequestHeader>(), It.IsAny<ViewDescription>(), It.IsAny<uint>(),
            It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
            .Returns((RequestHeader _, ViewDescription _, uint _, ArrayOf<BrowseDescription> descriptions,
                CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                if (BrowseFailure is { } failure)
                {
                    throw failure;
                }
                var results = new BrowseResult[descriptions.Count];
                for (int i = 0; i < descriptions.Count; i++)
                {
                    BrowseDescription request = descriptions[i];
                    Browses.Add(request);
                    Assert.That(request.BrowseDirection, Is.EqualTo(BrowseDirection.Forward));
                    Assert.That(request.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HierarchicalReferences));
                    Assert.That(request.IncludeSubtypes, Is.True);
                    Assert.That(request.NodeClassMask, Is.EqualTo((uint)NodeClass.Object));
                    results[i] = new BrowseResult
                    {
                        StatusCode = StatusCodes.Good,
                        References = Children[request.NodeId].ToArrayOf()
                    };
                }
                return new ValueTask<BrowseResponse>(new BrowseResponse
                {
                    ResponseHeader = new ResponseHeader(), Results = results
                });
            });
        Session.Setup(s => s.ReadAsync(
            It.IsAny<RequestHeader>(), 0, TimestampsToReturn.Neither,
            It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
            .Returns((RequestHeader _, double _, TimestampsToReturn _, ArrayOf<ReadValueId> requests,
                CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                if (ReadFailure is { } failure)
                {
                    throw failure;
                }
                var values = new DataValue[requests.Count];
                for (int i = 0; i < requests.Count; i++)
                {
                    ReadValueId request = requests[i];
                    Reads.Add(request);
                    values[i] = request.AttributeId == Attributes.BrowseName
                        ? new DataValue(new Variant(Names[request.NodeId]))
                        : new DataValue(Values[request.NodeId]);
                }
                return new ValueTask<ReadResponse>(new ReadResponse
                {
                    ResponseHeader = new ResponseHeader(), Results = values
                });
            });
        Session.Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
            It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
            .Returns((RequestHeader _, ArrayOf<BrowsePath> paths, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                var results = new BrowsePathResult[paths.Count];
                for (int i = 0; i < paths.Count; i++)
                {
                    BrowsePath path = paths[i];
                    Translations.Add(path);
                    Assert.That(path.RelativePath.Elements.Count, Is.EqualTo(1));
                    string name = path.RelativePath.Elements[0].TargetName.Name!;
                    if (Properties.TryGetValue((path.StartingNode, name), out NodeId id))
                    {
                        results[i] = new BrowsePathResult
                        {
                            StatusCode = StatusCodes.Good,
                            Targets = [new BrowsePathTarget { TargetId = id, RemainingPathIndex = uint.MaxValue }]
                        };
                    }
                    else
                    {
                        results[i] = new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch };
                    }
                }
                return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                    new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(), Results = results
                    });
            });
        Session.Setup(s => s.CallAsync(
            It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
            .Returns((RequestHeader _, ArrayOf<CallMethodRequest> requests, CancellationToken token) =>
            {
                token.ThrowIfCancellationRequested();
                Assert.That(requests.Count, Is.EqualTo(1));
                CallMethodRequest request = requests[0];
                var arguments = new Variant[request.InputArguments.Count];
                for (int i = 0; i < arguments.Length; i++)
                {
                    Variant value = request.InputArguments[i];
                    arguments[i] = value.TryGetValue(out ByteString bytes)
                        ? new Variant(bytes.Memory.ToArray().ToByteString()) : value;
                }
                Calls.Add(new CallMethodRequest
                {
                    ObjectId = request.ObjectId, MethodId = request.MethodId, InputArguments = arguments
                });
                CallTokens.Add(token);
                Assert.That(m_calls, Is.Not.Empty, $"Unexpected call: {request.MethodId}");
                CallMethodResult result = m_calls.Dequeue()(request);
                return new ValueTask<CallResponse>(new CallResponse
                {
                    ResponseHeader = new ResponseHeader(), Results = [result]
                });
            });
        Children[RootId] = [];
        Client = new FileSystemClient(Session.Object, RootId, new FileSystemClientOptions { ChunkSize = chunkSize });
    }

    public static NodeId RootId { get; } = new(6100);
    public Mock<ISession> Session { get; } = new(MockBehavior.Strict);
    public FileSystemClient Client { get; }
    public Dictionary<NodeId, List<ReferenceDescription>> Children { get; } = [];
    public Dictionary<NodeId, QualifiedName> Names { get; } = [];
    public Dictionary<NodeId, Variant> Values { get; } = [];
    public Dictionary<(NodeId Node, string Name), NodeId> Properties { get; } = [];
    public List<CallMethodRequest> Calls { get; } = [];
    public List<CancellationToken> CallTokens { get; } = [];
    public List<BrowseDescription> Browses { get; } = [];
    public List<ReadValueId> Reads { get; } = [];
    public List<BrowsePath> Translations { get; } = [];
    public Exception? BrowseFailure { get; set; }
    public Exception? ReadFailure { get; set; }
    public Action? OnClose { get; set; }

    public void AddChild(NodeId parent, NodeId id, string name, bool directory = false)
    {
        Names[id] = new QualifiedName(name);
        Children[parent].Add(new ReferenceDescription
        {
            NodeId = id,
            BrowseName = new QualifiedName(name),
            DisplayName = new LocalizedText(name),
            NodeClass = NodeClass.Object,
            IsForward = true,
            ReferenceTypeId = ReferenceTypeIds.Organizes,
            TypeDefinition = directory ? ObjectTypeIds.FileDirectoryType : ObjectTypeIds.FileType
        });
        if (directory)
        {
            Children[id] = [];
        }
    }

    public void Metadata(NodeId file, string name, Variant value)
    {
        var id = new NodeId(file + "/" + name, 0);
        Properties[(file, name)] = id;
        Values[id] = value;
    }

    public void Expect(
        NodeId objectId, NodeId methodId, Variant[] arguments, Variant[] outputs,
        Action? completed = null, Exception? failure = null)
    {
        m_calls.Enqueue(request =>
        {
            Assert.That(request.ObjectId, Is.EqualTo(objectId));
            Assert.That(request.MethodId, Is.EqualTo(methodId));
            Assert.That(request.InputArguments.ToArray(), Is.EqualTo(arguments));
            if (failure is not null)
            {
                throw failure;
            }
            completed?.Invoke();
            return new CallMethodResult { StatusCode = StatusCodes.Good, OutputArguments = outputs };
        });
    }

    public void ExpectClose(NodeId file, uint handle)
    {
        Expect(file, new NodeId(Methods.FileType_Close), [new Variant(handle)], [], () => OnClose?.Invoke());
    }

    public void VerifyComplete()
    {
        Assert.That(m_calls, Is.Empty, "Every scripted protocol operation, including Close, must be consumed.");
        Assert.That(CallTokens.All(token => token == CancellationToken.None), Is.True);
    }

    private readonly Queue<Func<CallMethodRequest, CallMethodResult>> m_calls = new();
}
