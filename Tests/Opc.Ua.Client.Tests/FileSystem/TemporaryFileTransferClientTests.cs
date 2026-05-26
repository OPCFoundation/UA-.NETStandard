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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.FileSystem;
using Opc.Ua.Tests;

using Opc.Ua.Client.TestFramework;

namespace Opc.Ua.Client.Tests.FileSystem
{
    /// <summary>
    /// Unit tests for <see cref="TemporaryFileTransferClient"/> that
    /// validate the temp-write commit lifecycle (CloseAndCommit vs
    /// Close, single terminal call).
    /// </summary>
    [TestFixture]
    [Category("FileSystem")]
    [Parallelizable]
    public class TemporaryFileTransferClientTests
    {
        [Test]
        public async Task CommitAsyncSendsCloseAndCommitOnceAsync()
        {
            using var harness = TempTransferHarness.Create();
            UaTemporaryWriteFile temp = await harness
                .GenerateForWriteAsync().ConfigureAwait(false);
            await harness.WriteSomeBytesAsync(temp).ConfigureAwait(false);

            NodeId completion = await temp.CommitAsync().ConfigureAwait(false);
            await temp.DisposeAsync().ConfigureAwait(false);
            await temp.CommitAsync().ConfigureAwait(false);

            Assert.That(harness.CloseAndCommitCount, Is.EqualTo(1));
            Assert.That(harness.CloseCount, Is.Zero);
            Assert.That(completion, Is.EqualTo(new NodeId(99)));
        }

        [Test]
        public async Task DisposeWithoutCommitSendsCloseAsync()
        {
            using var harness = TempTransferHarness.Create();
            UaTemporaryWriteFile temp = await harness
                .GenerateForWriteAsync().ConfigureAwait(false);

            await temp.DisposeAsync().ConfigureAwait(false);
            await temp.DisposeAsync().ConfigureAwait(false);

            Assert.That(harness.CloseCount, Is.EqualTo(1));
            Assert.That(harness.CloseAndCommitCount, Is.Zero);
        }

        [Test]
        public async Task GenerateForReadReturnsStreamThatClosesHandleOnDisposeAsync()
        {
            using var harness = TempTransferHarness.Create();
            UaFileStream stream = await harness.Client
                .GenerateFileForReadAsync(default, CancellationToken.None)
                .ConfigureAwait(false);

            await stream.DisposeAsync().ConfigureAwait(false);

            Assert.That(harness.CloseCount, Is.EqualTo(1));
        }

        [Test]
        public async Task TempStreamWrapperDisposeDoesNotCloseHandleAsync()
        {
            using var harness = TempTransferHarness.Create();
            UaTemporaryWriteFile temp = await harness
                .GenerateForWriteAsync().ConfigureAwait(false);

            // Disposing the wrapped Stream must NOT close the server handle.
            temp.Stream.Dispose();
            Assert.That(harness.CloseCount, Is.Zero);
            Assert.That(harness.CloseAndCommitCount, Is.Zero);

            await temp.CommitAsync().ConfigureAwait(false);
            Assert.That(harness.CloseAndCommitCount, Is.EqualTo(1));
        }

        private sealed class TempTransferHarness : IDisposable
        {
            private readonly Dictionary<uint, Func<CallMethodRequest, Variant[]>> m_handlers = [];

            private TempTransferHarness(TemporaryFileTransferClient client)
            {
                Client = client;
            }

            public TemporaryFileTransferClient Client { get; }

            public int CloseAndCommitCount { get; private set; }
            public int CloseCount { get; private set; }

            public static TempTransferHarness Create()
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                IServiceMessageContext messageContext = ServiceMessageContext.Create(telemetry);
                var sessionMock = new Mock<ISession>(MockBehavior.Loose);
                sessionMock.SetupGet(s => s.MessageContext).Returns(messageContext);

                TempTransferHarness harness = null;
                sessionMock
                    .Setup(s => s.CallAsync(
                        It.IsAny<RequestHeader>(),
                        It.IsAny<ArrayOf<CallMethodRequest>>(),
                        It.IsAny<CancellationToken>()))
                    .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>(
                        (_, requests, _) =>
                        {
                            CallMethodRequest req = requests[0];
                            req.MethodId.TryGetValue(out uint methodId);
                            Variant[] outputs = harness!.m_handlers[methodId](req);
                            var result = new CallMethodResult
                            {
                                StatusCode = StatusCodes.Good,
                                OutputArguments = outputs.ToArrayOf()
                            };
                            var response = new CallResponse
                            {
                                ResponseHeader = new ResponseHeader(),
                                Results = new[] { result }.ToArrayOf(),
                                DiagnosticInfos = default
                            };
                            return new ValueTask<CallResponse>(response);
                        });

                var client = new TemporaryFileTransferClient(
                    sessionMock.Object,
                    new NodeId(1000));

                harness = new TempTransferHarness(client);
                harness.RegisterHandlers();
                return harness;
            }

            public async Task<UaTemporaryWriteFile> GenerateForWriteAsync()
            {
                return await Client
                    .GenerateFileForWriteAsync(default, CancellationToken.None)
                    .ConfigureAwait(false);
            }

            public async Task WriteSomeBytesAsync(UaTemporaryWriteFile temp)
            {
                byte[] payload = [1, 2, 3];
#if NETSTANDARD2_1_OR_GREATER || NET
                await temp.Stream
                    .WriteAsync(payload.AsMemory(), CancellationToken.None)
                    .ConfigureAwait(false);
#else
                await temp.Stream
                    .WriteAsync(payload, 0, payload.Length, CancellationToken.None)
                    .ConfigureAwait(false);
#endif
            }

            public void Dispose()
            {
                // No-op (mock-only).
            }

            private void RegisterHandlers()
            {
                m_handlers[Methods.FileType_Close] = _ =>
                {
                    CloseCount++;
                    return [];
                };
                m_handlers[Methods.FileType_Write] = _ => [];
                m_handlers[Methods.FileType_SetPosition] = _ => [];
                m_handlers[Methods.FileType_Read] = _ =>
                    [new Variant(Array.Empty<byte>().ToByteString())];

                // GenerateFileForRead → (NodeId, uint handle, NodeId completionStateMachine).
                m_handlers[Methods.TemporaryFileTransferType_GenerateFileForRead] = _ =>
                [
                    new Variant(new NodeId(123)),
                    new Variant(7u),
                    new Variant(NodeId.Null)
                ];

                // GenerateFileForWrite → (NodeId, uint handle).
                m_handlers[Methods.TemporaryFileTransferType_GenerateFileForWrite] = _ =>
                [
                    new Variant(new NodeId(123)),
                    new Variant(7u)
                ];

                // CloseAndCommit → NodeId.
                m_handlers[Methods.TemporaryFileTransferType_CloseAndCommit] = _ =>
                {
                    CloseAndCommitCount++;
                    return [new Variant(new NodeId(99))];
                };
            }
        }
    }
}
