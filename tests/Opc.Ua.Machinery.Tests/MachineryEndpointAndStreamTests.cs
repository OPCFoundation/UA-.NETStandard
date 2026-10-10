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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.ISA95.Client;
using Opc.Ua.Machinery.Client;
using Opc.Ua.Machinery.Result;
using Opc.Ua.Tests;

namespace Opc.Ua.Machinery.Tests
{
    /// <summary>
    /// Pins the additive endpoint and streaming APIs without changing legacy accessor behavior.
    /// </summary>
    [TestFixture]
    [Category("Machinery")]
    public sealed class MachineryEndpointAndStreamTests
    {
        [Test]
        public async Task MissingJobNamespaceReportsNullEndpointsWithoutBrowsingAsync()
        {
            Mock<ISession> session = CreateSession(includeJobs: false);
            var client = new MachineryClient(session.Object, NUnitTelemetryContext.Create());

            MachineryJobManagementEndpoints endpoints = await client.GetJobManagementEndpointsAsync(
                new NodeId("machine", 2));

            Assert.That(endpoints.JobManagementId.IsNull, Is.True);
            Assert.That(endpoints.JobOrderReceiverId.IsNull, Is.True);
            Assert.That(endpoints.JobResponseProviderId.IsNull, Is.True);
            session.Verify(value => value.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        [TestCase(false, true)]
        public async Task ActualRolesRemainIndependentAndLegacyFallbackRemainsUnchangedAsync(
            bool hasReceiver,
            bool hasProvider)
        {
            Mock<ISession> session = CreateSession();
            var machine = new NodeId("machine", 2);
            var management = new NodeId("jobs", 2);
            var receiver = new NodeId("receiver", 2);
            var provider = new NodeId("provider", 2);
            var children = new Dictionary<(NodeId, string), NodeId>
            {
                [(machine, "JobManagement")] = management
            };
            if (hasReceiver)
            {
                children[(management, "JobOrderControl")] = receiver;
            }
            if (hasProvider)
            {
                children[(management, "JobOrderResults")] = provider;
            }
            SetupPaths(session, children);
            var client = new MachineryClient(session.Object, NUnitTelemetryContext.Create());

            MachineryJobManagementEndpoints endpoints = await client.GetJobManagementEndpointsAsync(machine);
            Isa95JobControlV2Client? legacy = await client.JobManagementAsync(machine);

            Assert.That(endpoints.JobManagementId, Is.EqualTo(management));
            Assert.That(endpoints.JobOrderReceiverId, Is.EqualTo(hasReceiver ? receiver : NodeId.Null));
            Assert.That(endpoints.JobResponseProviderId, Is.EqualTo(hasProvider ? provider : NodeId.Null));
            if (hasReceiver)
            {
                Assert.That(legacy, Is.Not.Null);
                Assert.That(legacy!.JobOrderReceiverId, Is.EqualTo(receiver));
                Assert.That(legacy.JobResponseProviderId, Is.EqualTo(hasProvider ? provider : receiver));
                Assert.That(legacy.JobResponseReceiverId, Is.EqualTo(receiver));
            }
            else
            {
                Assert.That(legacy, Is.Null);
            }
        }

        [Test]
        public async Task AbsentManagementReturnsOnlyNullEndpointsAsync()
        {
            Mock<ISession> session = CreateSession();
            SetupPaths(session, []);
            var client = new MachineryClient(session.Object, NUnitTelemetryContext.Create());

            MachineryJobManagementEndpoints endpoints = await client.GetJobManagementEndpointsAsync(
                new NodeId("machine", 2));

            Assert.That(endpoints.JobManagementId.IsNull, Is.True);
            Assert.That(endpoints.JobOrderReceiverId.IsNull, Is.True);
            Assert.That(endpoints.JobResponseProviderId.IsNull, Is.True);
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(131079)]
        public async Task StreamingAndLegacyDownloadsMatchAndLeaveDestinationOpenAsync(int length)
        {
            byte[] payload = Enumerable.Range(0, length).Select(index => (byte)(index % 251)).ToArray();
            var calls = new List<CallMethodRequest>();
            Mock<ISession> session = CreateTransferSession(payload, calls);
            var client = new MachineryClient(session.Object, NUnitTelemetryContext.Create());
            using var destination = new MemoryStream();

            await client.DownloadResultAsync(new NodeId("machine", 2), "result-1", destination);
            ByteString legacy = await client.DownloadResultAsync(new NodeId("machine", 2), "result-1");

            Assert.That(destination.CanWrite, Is.True, "The destination belongs to the caller.");
            Assert.That(destination.ToArray(), Is.EqualTo(payload));
            Assert.That(legacy.Span.ToArray(), Is.EqualTo(payload));
            Assert.That(calls.Count(call => call.MethodId == new NodeId(Ua.Methods.FileType_Close)), Is.EqualTo(2));
            Assert.That(calls.Count(call =>
                call.MethodId == new NodeId(Ua.Methods.TemporaryFileTransferType_GenerateFileForRead)), Is.EqualTo(2));
            foreach (CallMethodRequest generate in calls.Where(call =>
                call.MethodId == new NodeId(Ua.Methods.TemporaryFileTransferType_GenerateFileForRead)))
            {
                Assert.That(generate.ObjectId, Is.EqualTo(new NodeId("transfer", 2)));
                Assert.That(generate.InputArguments[0].TryGetStructure<ResultTransferOptionsDataType>(
                    out ResultTransferOptionsDataType? options), Is.True);
                Assert.That(options!.ResultId, Is.EqualTo("result-1"));
            }
            if (length > 65_536)
            {
                Assert.That(calls.Count(call => call.MethodId == new NodeId(Ua.Methods.FileType_Read)),
                    Is.GreaterThan(4), "The result must cross several incremental reads.");
            }
        }

        [Test]
        public void DestinationFailureClosesRemoteHandleWithoutDisposingCallerStream()
        {
            var calls = new List<CallMethodRequest>();
            Mock<ISession> session = CreateTransferSession([1, 2, 3], calls);
            var client = new MachineryClient(session.Object, NUnitTelemetryContext.Create());
            using var destination = new FailingDestination();

            Assert.ThrowsAsync<IOException>(async () =>
                await client.DownloadResultAsync(new NodeId("machine", 2), "result-1", destination));

            Assert.That(destination.WasDisposed, Is.False);
            Assert.That(calls.Count(call => call.MethodId == new NodeId(Ua.Methods.FileType_Close)), Is.EqualTo(1));
        }

        [Test]
        public void CancellationClosesRemoteHandleWithAnUncancelledCleanupToken()
        {
            var calls = new List<CallMethodRequest>();
            Mock<ISession> session = CreateTransferSession([1, 2, 3], calls);
            var client = new MachineryClient(session.Object, NUnitTelemetryContext.Create());
            using var cancellation = new CancellationTokenSource();
            using var destination = new FailingDestination(cancellation);

            Assert.CatchAsync<OperationCanceledException>(async () =>
                await client.DownloadResultAsync(
                    new NodeId("machine", 2), "result-1", destination, cancellation.Token));

            Assert.That(cancellation.IsCancellationRequested, Is.True);
            Assert.That(destination.WasDisposed, Is.False);
            Assert.That(calls.Count(call => call.MethodId == new NodeId(Ua.Methods.FileType_Close)), Is.EqualTo(1));
        }

        [Test]
        public void InvalidDestinationIsRejectedBeforeOpeningRemoteTransfer()
        {
            Mock<ISession> session = CreateSession();
            var client = new MachineryClient(session.Object, NUnitTelemetryContext.Create());
            using var readOnly = new MemoryStream([1, 2, 3], writable: false);

            Assert.ThrowsAsync<ArgumentNullException>(async () =>
                await client.DownloadResultAsync(new NodeId("machine", 2), "result-1", null!));
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await client.DownloadResultAsync(new NodeId("machine", 2), "result-1", readOnly));

            session.Verify(value => value.CallAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()),
                Times.Never);
        }

        private static Mock<ISession> CreateSession(bool includeJobs = true)
        {
            var context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());
            context.NamespaceUris.GetIndexOrAppend(Ua.Machinery.Namespaces.Machinery);
            context.NamespaceUris.GetIndexOrAppend(Ua.Machinery.Result.Namespaces.MachineryResult);
            if (includeJobs)
            {
                context.NamespaceUris.GetIndexOrAppend(Ua.Machinery.Jobs.Namespaces.MachineryJobs);
            }
            var session = new Mock<ISession>(MockBehavior.Strict);
            session.SetupGet(value => value.MessageContext).Returns(context);
            session.SetupGet(value => value.NamespaceUris).Returns(context.NamespaceUris);
            return session;
        }

        private static void SetupPaths(
            Mock<ISession> session,
            Dictionary<(NodeId, string), NodeId> children)
        {
            session.Setup(value => value.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader? _, ArrayOf<BrowsePath> paths, CancellationToken _) =>
                    new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = paths.ConvertAll(path =>
                        {
                            NodeId node = path.StartingNode;
                            foreach (RelativePathElement element in path.RelativePath.Elements)
                            {
                                if (!children.TryGetValue((node, element.TargetName.Name!), out node))
                                {
                                    return new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch };
                                }
                            }
                            return new BrowsePathResult
                            {
                                StatusCode = StatusCodes.Good,
                                Targets = [new BrowsePathTarget { TargetId = node, RemainingPathIndex = uint.MaxValue }]
                            };
                        })
                    });
        }

        private static Mock<ISession> CreateTransferSession(byte[] payload, List<CallMethodRequest> calls)
        {
            Mock<ISession> session = CreateSession();
            SetupPaths(session, new Dictionary<(NodeId, string), NodeId>
            {
                [(new NodeId("machine", 2), "ResultManagement")] = new NodeId("results", 2),
                [(new NodeId("results", 2), "ResultTransfer")] = new NodeId("transfer", 2)
            });
            int position = 0;
            session.Setup(value => value.CallAsync(
                It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((RequestHeader? _, ArrayOf<CallMethodRequest> requests, CancellationToken token) =>
                {
                    token.ThrowIfCancellationRequested();
                    Assert.That(requests.Count, Is.EqualTo(1));
                    CallMethodRequest call = requests[0];
                    calls.Add(call);
                    Assert.That(call.MethodId.TryGetValue(out uint method), Is.True);
                    ArrayOf<Variant> output;
                    switch (method)
                    {
                        case Ua.Methods.TemporaryFileTransferType_GenerateFileForRead:
                            position = 0;
                            output = [Variant.From(new NodeId("file", 2)), Variant.From(7u), Variant.From(NodeId.Null)];
                            break;
                        case Ua.Methods.FileType_Read:
                            Assert.That(call.ObjectId, Is.EqualTo(new NodeId("file", 2)));
                            Assert.That(call.InputArguments[1].TryGetValue(out int requested), Is.True);
                            int count = Math.Min(requested, payload.Length - position);
                            output = [Variant.From(new ByteString(payload.AsSpan(position, count).ToArray()))];
                            position += count;
                            break;
                        case Ua.Methods.FileType_Close:
                            Assert.That(call.ObjectId, Is.EqualTo(new NodeId("file", 2)));
                            Assert.That(call.InputArguments[0].TryGetValue(out uint handle), Is.True);
                            Assert.That(handle, Is.EqualTo(7u));
                            Assert.That(token.IsCancellationRequested, Is.False);
                            output = [];
                            break;
                        default:
                            throw new InvalidOperationException($"Unexpected method {method}.");
                    }
                    return new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [new CallMethodResult { StatusCode = StatusCodes.Good, OutputArguments = output }]
                    };
                });
            return session;
        }

        private sealed class FailingDestination : Stream
        {
            public FailingDestination(CancellationTokenSource? cancellation = null)
            {
                m_cancellation = cancellation;
            }

            public bool WasDisposed { get; private set; }

            public override bool CanRead => false;

            public override bool CanSeek => false;

            public override bool CanWrite => true;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                throw new NotSupportedException();
            }

            public override long Seek(long offset, SeekOrigin origin)
            {
                throw new NotSupportedException();
            }

            public override void SetLength(long value)
            {
                throw new NotSupportedException();
            }

            public override void Write(byte[] buffer, int offset, int count)
            {
                throw new IOException("Destination quota reached.");
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                return FailWriteAsync();
            }

#if NET6_0_OR_GREATER
            public override ValueTask WriteAsync(
                ReadOnlyMemory<byte> buffer,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask(FailWriteAsync());
            }
#endif

            protected override void Dispose(bool disposing)
            {
                WasDisposed = true;
                base.Dispose(disposing);
            }

            private Task FailWriteAsync()
            {
                if (m_cancellation is not null)
                {
                    m_cancellation.Cancel();
                    return Task.FromCanceled(m_cancellation.Token);
                }
                return Task.FromException(new IOException("Destination quota reached."));
            }

            private readonly CancellationTokenSource? m_cancellation;
        }
    }
}
