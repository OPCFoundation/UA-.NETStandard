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
using Opc.Ua.Client;
using Opc.Ua.Machinery.Client;
using Opc.Ua.Machinery.Result;
using Opc.Ua.Tests;

namespace Opc.Ua.Machinery.Tests
{
    public sealed partial class MachineryResultTransferTests
    {
        private sealed class TransferHarness
        {
            public TransferHarness()
            {
                Context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
                Context.NamespaceUris.Append("urn:result-transfer:test");
                Context.NamespaceUris.Append(Opc.Ua.Machinery.Result.Namespaces.MachineryResult);
                Session = new Mock<ISession>(MockBehavior.Strict);
                Session.SetupGet(session => session.MessageContext).Returns(Context);
                Session.SetupGet(session => session.NamespaceUris).Returns(Context.NamespaceUris);
                Session.SetupGet(session => session.SessionId).Returns(() => SessionId);
                Session.Setup(session => session.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                    .Returns((RequestHeader? _, ArrayOf<BrowsePath> paths, CancellationToken _) =>
                    {
                        Assert.That(paths, Has.Count.EqualTo(1));
                        Paths.Add(paths[0]);
                        return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                            Translate is not null ? Translate(paths[0]) : PathResponse(paths[0]));
                    });
                Session.Setup(session => session.CallAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                    .Returns((RequestHeader? _, ArrayOf<CallMethodRequest> calls, CancellationToken ct) =>
                    {
                        Assert.That(calls, Has.Count.EqualTo(1));
                        return DispatchAsync(calls[0], ct);
                    });
                Session.Setup(session => session.ReadAsync(
                    It.IsAny<RequestHeader?>(),
                    It.IsAny<double>(),
                    It.IsAny<TimestampsToReturn>(),
                    It.IsAny<ArrayOf<ReadValueId>>(),
                    It.IsAny<CancellationToken>()))
                    .Returns((
                        RequestHeader? _,
                        double _,
                        TimestampsToReturn _,
                        ArrayOf<ReadValueId> reads,
                        CancellationToken ct) =>
                    {
                        Assert.That(reads, Has.Count.EqualTo(1));
                        Assert.That(reads[0].NodeId, Is.EqualTo(StateId), "Never read a declared File Size or URI.");
                        AttributeReads++;
                        return ReadState?.Invoke(ct) ?? new ValueTask<ReadResponse>(new ReadResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results =
                            [
                                new DataValue(Variant.From(Opc.Ua.ObjectIds.FileTransferStateMachineType_ReadTransfer))
                            ]
                        });
                    });
                Client = new MachineryClient(Session.Object, Context.Telemetry);
            }

            public Mock<ISession> Session { get; }

            public ServiceMessageContext Context { get; }

            public MachineryClient Client { get; }

            public NodeId SessionId { get; set; } = new("owned-session", 1);

            public uint Handle { get; set; } = 37;

            public ByteString Payload { get; set; } = ByteString.Empty;

            public int MaxReturnedBytes { get; set; } = int.MaxValue;

            public int Position { get; private set; }

            public bool OwnsHandle { get; private set; }

            public int GenerateCount { get; private set; }

            public int CloseCount { get; private set; }

            public int AttributeReads { get; private set; }

            public string? SelectedResultId { get; private set; }

            public CancellationToken CloseToken { get; private set; }

            public bool CloseWasCancelledOnEntry { get; private set; }

            public List<int> ReadLengths { get; } = [];

            public List<BrowsePath> Paths { get; } = [];

            public List<CallMethodRequest> Calls { get; } = [];

            public CallResponse? GenerateResponse { get; set; }

            public Action? Generated { get; set; }

            public Func<int, CancellationToken, ValueTask<CallResponse>>? Read { get; set; }

            public Func<CancellationToken, ValueTask<CallResponse>>? Close { get; set; }

            public Func<BrowsePath, TranslateBrowsePathsToNodeIdsResponse>? Translate { get; set; }

            public Func<CancellationToken, ValueTask<ReadResponse>>? ReadState { get; set; }

            public static CallResponse Response(ArrayOf<Variant> outputs)
            {
                return new CallResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new CallMethodResult { OutputArguments = outputs }]
                };
            }

            public static TranslateBrowsePathsToNodeIdsResponse PathResponse(BrowsePath path)
            {
                return new TranslateBrowsePathsToNodeIdsResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results =
                    [
                        new BrowsePathResult
                        {
                            Targets =
                            [
                                new BrowsePathTarget
                                {
                                    TargetId = TargetOf(path),
                                    RemainingPathIndex = uint.MaxValue
                                }
                            ]
                        }
                    ]
                };
            }

            private async ValueTask<CallResponse> DispatchAsync(CallMethodRequest request, CancellationToken ct)
            {
                Calls.Add(request);
                if (request.MethodId == GenerateMethodId)
                {
                    Assert.That(request.ObjectId, Is.EqualTo(TransferId));
                    Assert.That(request.InputArguments, Has.Count.EqualTo(1));
                    Assert.That(request.InputArguments[0].TryGetValue<ResultTransferOptionsDataType>(
                        out ResultTransferOptionsDataType? options,
                        Context), Is.True);
                    Assert.That(options, Is.Not.Null);
                    SelectedResultId = options!.ResultId;
                    GenerateCount++;
                    CallResponse response = GenerateResponse ?? Response(
                        [Variant.From(FileId), Variant.From(Handle), Variant.From(NodeId.Null)]);
                    if (response.ResponseHeader is not null &&
                        StatusCode.IsGood(response.ResponseHeader.ServiceResult) &&
                        response.Results.Count == 1 &&
                        StatusCode.IsGood(response.Results[0].StatusCode) &&
                        response.Results[0].OutputArguments.Count >= 2 &&
                        response.Results[0].OutputArguments[0].TryGetValue(out NodeId node) && node == FileId &&
                        response.Results[0].OutputArguments[1].TryGetValue(out uint returnedHandle))
                    {
                        Handle = returnedHandle;
                        OwnsHandle = true;
                    }
                    Generated?.Invoke();
                    return response;
                }

                Assert.That(request.ObjectId, Is.EqualTo(FileId));
                Assert.That(request.InputArguments[0].TryGetValue(out uint handle), Is.True);
                Assert.That(handle, Is.EqualTo(Handle));
                Assert.That(OwnsHandle, Is.True, "Read and Close must use the exact acquired handle.");
                if (request.MethodId == ReadMethodId)
                {
                    Assert.That(request.InputArguments, Has.Count.EqualTo(2));
                    Assert.That(request.InputArguments[1].TryGetValue(out int length), Is.True);
                    Assert.That(length, Is.GreaterThan(0).And.LessThanOrEqualTo(65536));
                    ReadLengths.Add(length);
                    if (Read is not null)
                    {
                        return await Read(length, ct);
                    }
                    int count = Math.Min(Math.Min(length, MaxReturnedBytes), Payload.Length - Position);
                    var data = new ByteString(Payload.Memory.Slice(Position, count));
                    Position += count;
                    return Response([Variant.From(data)]);
                }

                Assert.That(request.MethodId, Is.EqualTo(CloseMethodId).Or.EqualTo(MethodIds.FileType_Close));
                Assert.That(request.InputArguments, Has.Count.EqualTo(1));
                CloseCount++;
                CloseToken = ct;
                CloseWasCancelledOnEntry = ct.IsCancellationRequested;
                CallResponse closed = Close is null ? Response([]) : await Close(ct);
                if (closed.ResponseHeader is not null &&
                    StatusCode.IsGood(closed.ResponseHeader.ServiceResult) &&
                    closed.Results.Count == 1 &&
                    StatusCode.IsGood(closed.Results[0].StatusCode))
                {
                    OwnsHandle = false;
                }
                return closed;
            }

            private static NodeId TargetOf(BrowsePath path)
            {
                if (path.StartingNode == MachineId)
                {
                    return TransferId;
                }
                if (path.StartingNode == TransferId)
                {
                    Assert.That(path.RelativePath.Elements, Has.Count.EqualTo(1));
                    Assert.That(
                        path.RelativePath.Elements[0].TargetName,
                        Is.EqualTo(new QualifiedName("GenerateFileForRead")));
                    return GenerateMethodId;
                }
                if (path.StartingNode == FileId)
                {
                    Assert.That(path.RelativePath.Elements, Has.Count.EqualTo(1));
                    string? name = path.RelativePath.Elements[0].TargetName.Name;
                    Assert.That(name, Is.EqualTo("Read").Or.EqualTo("Close"));
                    return name == "Read" ? ReadMethodId : CloseMethodId;
                }
                Assert.That(path.StartingNode, Is.EqualTo(StateMachineId));
                return StateId;
            }

            public static readonly NodeId MachineId = new("machine", 1);
            public static readonly NodeId TransferId = new("result-transfer", 1);
            public static readonly NodeId FileId = new("owned-result-file", 1);
            public static readonly NodeId GenerateMethodId = new("instance-generate-method", 1);
            public static readonly NodeId ReadMethodId = new("instance-read-method", 1);
            public static readonly NodeId CloseMethodId = new("instance-close-method", 1);
            public static readonly NodeId StateMachineId = new("generation-state-machine", 1);
            public static readonly NodeId StateId = new("generation-state", 1);
        }
    }
}
