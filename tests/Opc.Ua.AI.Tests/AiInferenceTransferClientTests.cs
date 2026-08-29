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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.AI.Tests
{
    [TestFixture]
    [Category("AI")]
    [Category("Client")]
    public sealed class AIInferenceTransferClientTests
    {
        private static readonly NodeId s_transferId = new(5000u, 3);
        private static readonly NodeId s_requestFileId = new(5001u, 3);
        private static readonly NodeId s_responseFileId = new(5002u, 3);

        [Test]
        public async Task WriteRequestAsyncWithByteStringWritesChunksAndClosesAsync()
        {
            var harness = new AISessionHarness();
            harness.AddChild(s_transferId, BrowseNames.Request, s_requestFileId);
            NodeId open = StandardMethod(harness, Ua.MethodIds.FileType_Open);
            NodeId write = StandardMethod(harness, Ua.MethodIds.FileType_Write);
            NodeId close = StandardMethod(harness, Ua.MethodIds.FileType_Close);
            var chunks = new List<byte[]>();
            int closeCalls = 0;
            SetupCalls(harness, request =>
            {
                if (request.MethodId == open)
                {
                    return CallResult(StatusCodes.Good, [Variant.From((uint)17)]);
                }
                if (request.MethodId == write)
                {
                    Assert.That(request.InputArguments[0].GetUInt32(), Is.EqualTo(17));
                    Assert.That(
                        request.InputArguments[1].TryGetValue(out ByteString chunk),
                        Is.True);
                    chunks.Add(chunk.Span.ToArray());
                    return CallResult(StatusCodes.Good);
                }
                if (request.MethodId == close)
                {
                    closeCalls++;
                    return CallResult(StatusCodes.Good);
                }
                throw new InvalidOperationException($"Unexpected method {request.MethodId}.");
            });

            await harness.Client.Transfer(s_transferId)
                .WriteRequestAsync(ByteString.From([1, 2, 3, 4, 5]), 2)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(chunks, Has.Count.EqualTo(3));
                Assert.That(chunks[0], Is.EqualTo(new byte[] { 1, 2 }));
                Assert.That(chunks[1], Is.EqualTo(new byte[] { 3, 4 }));
                Assert.That(chunks[2], Is.EqualTo(new byte[] { 5 }));
                Assert.That(closeCalls, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task WriteRequestAsyncWithStreamWritesChunksAndClosesAsync()
        {
            var harness = new AISessionHarness();
            harness.AddChild(s_transferId, BrowseNames.Request, s_requestFileId);
            NodeId open = StandardMethod(harness, Ua.MethodIds.FileType_Open);
            NodeId write = StandardMethod(harness, Ua.MethodIds.FileType_Write);
            NodeId close = StandardMethod(harness, Ua.MethodIds.FileType_Close);
            var chunks = new List<byte[]>();
            int closeCalls = 0;
            SetupCalls(harness, request =>
            {
                if (request.MethodId == open)
                {
                    return CallResult(StatusCodes.Good, [Variant.From((uint)23)]);
                }
                if (request.MethodId == write)
                {
                    Assert.That(
                        request.InputArguments[1].TryGetValue(out ByteString chunk),
                        Is.True);
                    chunks.Add(chunk.Span.ToArray());
                    return CallResult(StatusCodes.Good);
                }
                if (request.MethodId == close)
                {
                    closeCalls++;
                    return CallResult(StatusCodes.Good);
                }
                throw new InvalidOperationException($"Unexpected method {request.MethodId}.");
            });
            using var content = new MemoryStream([6, 7, 8, 9, 10]);

            await harness.Client.Transfer(s_transferId)
                .WriteRequestAsync(content, 3)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(chunks, Has.Count.EqualTo(2));
                Assert.That(chunks[0], Is.EqualTo(new byte[] { 6, 7, 8 }));
                Assert.That(chunks[1], Is.EqualTo("\t\n"u8.ToArray()));
                Assert.That(closeCalls, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task ReadResponseAsyncReturnsAllChunksAndClosesAsync()
        {
            var harness = new AISessionHarness();
            harness.AddChild(s_transferId, BrowseNames.Response, s_responseFileId);
            NodeId open = StandardMethod(harness, Ua.MethodIds.FileType_Open);
            NodeId read = StandardMethod(harness, Ua.MethodIds.FileType_Read);
            NodeId close = StandardMethod(harness, Ua.MethodIds.FileType_Close);
            var chunks = new Queue<ByteString>(
            [
                ByteString.From([1, 2]),
                ByteString.From([3])
            ]);
            int closeCalls = 0;
            SetupCalls(harness, request =>
            {
                if (request.MethodId == open)
                {
                    return CallResult(StatusCodes.Good, [Variant.From((uint)31)]);
                }
                if (request.MethodId == read)
                {
                    return CallResult(StatusCodes.Good, [Variant.From(chunks.Dequeue())]);
                }
                if (request.MethodId == close)
                {
                    closeCalls++;
                    return CallResult(StatusCodes.Good);
                }
                throw new InvalidOperationException($"Unexpected method {request.MethodId}.");
            });

            ByteString result = await harness.Client.Transfer(s_transferId)
                .ReadResponseAsync(2)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Span.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
                Assert.That(chunks, Is.Empty);
                Assert.That(closeCalls, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task ReadResponseAsyncWithDestinationWritesContentAsync()
        {
            var harness = new AISessionHarness();
            harness.AddChild(s_transferId, BrowseNames.Response, s_responseFileId);
            NodeId open = StandardMethod(harness, Ua.MethodIds.FileType_Open);
            NodeId read = StandardMethod(harness, Ua.MethodIds.FileType_Read);
            NodeId close = StandardMethod(harness, Ua.MethodIds.FileType_Close);
            SetupCalls(harness, request =>
            {
                if (request.MethodId == open)
                {
                    return CallResult(StatusCodes.Good, [Variant.From((uint)37)]);
                }
                if (request.MethodId == read)
                {
                    return CallResult(StatusCodes.Good, [Variant.From(ByteString.From([4, 5]))]);
                }
                if (request.MethodId == close)
                {
                    return CallResult(StatusCodes.Good);
                }
                throw new InvalidOperationException($"Unexpected method {request.MethodId}.");
            });
            using var destination = new MemoryStream();

            await harness.Client.Transfer(s_transferId)
                .ReadResponseAsync(destination, 4)
                .ConfigureAwait(false);

            Assert.That(destination.ToArray(), Is.EqualTo(new byte[] { 4, 5 }));
        }

        [Test]
        public async Task WriteRequestAsyncWhenTypedOpenFailsUsesBrowseNameMethodsAsync()
        {
            var harness = new AISessionHarness();
            harness.AddChild(s_transferId, BrowseNames.Request, s_requestFileId);
            NodeId instanceOpen = new(5100u, 3);
            NodeId instanceWrite = new(5101u, 3);
            NodeId instanceClose = new(5102u, 3);
            harness.AddChild(s_requestFileId, Ua.BrowseNames.Open, 0, instanceOpen);
            harness.AddChild(s_requestFileId, Ua.BrowseNames.Write, 0, instanceWrite);
            harness.AddChild(s_requestFileId, Ua.BrowseNames.Close, 0, instanceClose);
            NodeId typeOpen = StandardMethod(harness, Ua.MethodIds.FileType_Open);
            var chunks = new List<byte[]>();
            int closeCalls = 0;
            SetupCalls(harness, request =>
            {
                if (request.MethodId == typeOpen)
                {
                    return CallResult(StatusCodes.BadNodeIdUnknown);
                }
                if (request.MethodId == instanceOpen)
                {
                    return CallResult(StatusCodes.Good, [Variant.From((uint)41)]);
                }
                if (request.MethodId == instanceWrite)
                {
                    Assert.That(
                        request.InputArguments[1].TryGetValue(out ByteString chunk),
                        Is.True);
                    chunks.Add(chunk.Span.ToArray());
                    return CallResult(StatusCodes.Good);
                }
                if (request.MethodId == instanceClose)
                {
                    closeCalls++;
                    return CallResult(StatusCodes.Good);
                }
                throw new InvalidOperationException($"Unexpected method {request.MethodId}.");
            });

            await harness.Client.Transfer(s_transferId)
                .WriteRequestAsync(ByteString.From([11, 12, 13]), 2)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(chunks, Has.Count.EqualTo(2));
                Assert.That(chunks[0], Is.EqualTo(new byte[] { 11, 12 }));
                Assert.That(chunks[1], Is.EqualTo("\r"u8.ToArray()));
                Assert.That(closeCalls, Is.EqualTo(1));
            });
        }

        [Test]
        public async Task ReadResponseAsyncWhenTypedOpenFailsUsesBrowseNameMethodsAsync()
        {
            var harness = new AISessionHarness();
            harness.AddChild(s_transferId, BrowseNames.Response, s_responseFileId);
            NodeId instanceOpen = new(5200u, 3);
            NodeId instanceRead = new(5201u, 3);
            NodeId instanceClose = new(5202u, 3);
            harness.AddChild(s_responseFileId, Ua.BrowseNames.Open, 0, instanceOpen);
            harness.AddChild(s_responseFileId, Ua.BrowseNames.Read, 0, instanceRead);
            harness.AddChild(s_responseFileId, Ua.BrowseNames.Close, 0, instanceClose);
            NodeId typeOpen = StandardMethod(harness, Ua.MethodIds.FileType_Open);
            int closeCalls = 0;
            SetupCalls(harness, request =>
            {
                if (request.MethodId == typeOpen)
                {
                    return CallResult(StatusCodes.BadNodeIdUnknown);
                }
                if (request.MethodId == instanceOpen)
                {
                    return CallResult(StatusCodes.Good, [Variant.From((uint)43)]);
                }
                if (request.MethodId == instanceRead)
                {
                    return CallResult(StatusCodes.Good, [Variant.From(ByteString.From([14, 15]))]);
                }
                if (request.MethodId == instanceClose)
                {
                    closeCalls++;
                    return CallResult(StatusCodes.Good);
                }
                throw new InvalidOperationException($"Unexpected method {request.MethodId}.");
            });

            ByteString result = await harness.Client.Transfer(s_transferId)
                .ReadResponseAsync(4)
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result.Span.ToArray(), Is.EqualTo(new byte[] { 14, 15 }));
                Assert.That(closeCalls, Is.EqualTo(1));
            });
        }

        [Test]
        public void WriteRequestAsyncWithInvalidChunkSizeThrowsArgumentOutOfRangeException()
        {
            var harness = new AISessionHarness();
            harness.AddChild(s_transferId, BrowseNames.Request, s_requestFileId);

            Assert.ThrowsAsync<ArgumentOutOfRangeException>(async () =>
                await harness.Client.Transfer(s_transferId)
                    .WriteRequestAsync(ByteString.Empty, 0)
                    .ConfigureAwait(false));
        }

        [Test]
        public void WriteRequestAsyncWithNullStreamThrowsArgumentNullException()
        {
            var harness = new AISessionHarness();
            harness.AddChild(s_transferId, BrowseNames.Request, s_requestFileId);

            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await harness.Client.Transfer(s_transferId)
                    .WriteRequestAsync(null!, 1)
                    .ConfigureAwait(false));
        }

        [Test]
        public async Task ExecuteAsyncWhenTypeMethodIsRejectedUsesResolvedInstanceMethodAsync()
        {
            var harness = new AISessionHarness();
            NodeId instanceMethodId = new(5300u, 3);
            SetupFirstResolutionMissingThenPresent(
                harness,
                instanceMethodId,
                s_transferId,
                BrowseNames.Execute,
                harness.AINamespaceIndex);
            int callCount = 0;
            List<CallMethodRequest> calls = SetupCalls(harness, _ =>
                callCount++ == 0
                    ? CallResult(StatusCodes.BadMethodInvalid)
                    : CallResult(StatusCodes.Good, [Variant.From(true)]));

            bool result = await harness.Client.Transfer(s_transferId)
                .ExecuteAsync()
                .ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(result, Is.True);
                Assert.That(calls, Has.Count.EqualTo(2));
                Assert.That(calls[1].MethodId, Is.EqualTo(instanceMethodId));
            });
        }

        [Test]
        public async Task AbortAsyncWhenTypeMethodSucceedsCompletesAsync()
        {
            var harness = new AISessionHarness();
            List<CallMethodRequest> calls = SetupCalls(
                harness,
                _ => CallResult(StatusCodes.Good));

            await harness.Client.Transfer(s_transferId).AbortAsync().ConfigureAwait(false);

            Assert.That(calls, Has.Count.EqualTo(1));
        }

        private static NodeId StandardMethod(AISessionHarness harness, ExpandedNodeId methodId)
        {
            return ExpandedNodeId.ToNodeId(methodId, harness.NamespaceUris);
        }

        private static List<CallMethodRequest> SetupCalls(
            AISessionHarness harness,
            Func<CallMethodRequest, CallMethodResult> handler)
        {
            var calls = new List<CallMethodRequest>();
            harness.Session
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>(
                    (_, requests, _) =>
                    {
                        CallMethodRequest request = requests[0];
                        calls.Add(request);
                        return new ValueTask<CallResponse>(new CallResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = [handler(request)],
                            DiagnosticInfos = []
                        });
                    });
            return calls;
        }

        private static void SetupFirstResolutionMissingThenPresent(
            AISessionHarness harness,
            NodeId instanceMethodId,
            NodeId startingNode,
            string browseName,
            ushort namespaceIndex)
        {
            int callCount = 0;
            harness.Session
                .Setup(s => s.TranslateBrowsePathsToNodeIdsAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<BrowsePath>>(),
                    It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<BrowsePath>, CancellationToken>(
                    (_, paths, _) =>
                    {
                        Assert.That(paths, Has.Count.EqualTo(1));
                        BrowsePath path = paths[0];
                        Assert.That(path.StartingNode, Is.EqualTo(startingNode));
                        Assert.That(path.RelativePath.Elements, Has.Count.EqualTo(1));
                        RelativePathElement element = path.RelativePath.Elements[0];
                        Assert.That(element.TargetName, Is.EqualTo(
                            new QualifiedName(browseName, namespaceIndex)));
                        Assert.That(
                            element.ReferenceTypeId,
                            Is.EqualTo(callCount == 0
                                ? global::Opc.Ua.ReferenceTypeIds.HasComponent
                                : global::Opc.Ua.ReferenceTypeIds.HierarchicalReferences));
                        bool found = callCount++ > 0;
                        return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(
                            new TranslateBrowsePathsToNodeIdsResponse
                            {
                                ResponseHeader = new ResponseHeader(),
                                Results =
                                [
                                    new BrowsePathResult
                                    {
                                        StatusCode = found
                                            ? StatusCodes.Good
                                            : StatusCodes.BadNoMatch,
                                        Targets = found
                                            ? [new BrowsePathTarget
                                            {
                                                TargetId = new ExpandedNodeId(instanceMethodId)
                                            }]
                                            : []
                                    }
                                ],
                                DiagnosticInfos = []
                            });
                    });
        }

        private static CallMethodResult CallResult(
            StatusCode statusCode,
            ArrayOf<Variant> outputs = default)
        {
            return new CallMethodResult
            {
                StatusCode = statusCode,
                OutputArguments = outputs
            };
        }
    }
}
