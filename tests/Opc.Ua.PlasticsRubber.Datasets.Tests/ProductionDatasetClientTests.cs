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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.PlasticsRubber.Client;

namespace Opc.Ua.PlasticsRubber.Datasets.Tests
{
    /// <summary>
    /// Tests client behavior for asynchronous remote peers not produced by the synchronous-commit server binding.
    /// </summary>
    [TestFixture]
    public sealed class ProductionDatasetClientTests
    {
        /// <summary>
        /// A commit with a completion node is not acknowledged as complete until the peer reaches Idle.
        /// </summary>
        [Test]
        public async Task UploadWaitsForAsynchronousRemoteCommitAsync()
        {
            var peer = new ProductionDatasetClientPeer();
            var client = new ProductionDatasetClient(peer.Session.Object, peer.ManagementId);
            using var source = new MemoryStream([1, 2, 3]);
            await client.UploadAsync(Options(), source).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(peer.CommitCalls, Is.EqualTo(1));
                Assert.That(peer.StateReads, Is.EqualTo(2));
            });
        }

        /// <summary>
        /// A peer's Error state remains a failure even when CloseAndCommit returned a successful service result.
        /// </summary>
        [Test]
        public void UploadRejectsAsynchronousRemoteCommitError()
        {
            var peer = new ProductionDatasetClientPeer { FailsCommit = true };
            var client = new ProductionDatasetClient(peer.Session.Object, peer.ManagementId);
            using var source = new MemoryStream([1]);
            ServiceResultException? exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.UploadAsync(Options(), source).ConfigureAwait(false));
            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(peer.StateReads, Is.EqualTo(2));
        }

        /// <summary>
        /// A bad completion-state read cannot be interpreted as successful commit completion.
        /// </summary>
        [Test]
        public void UploadPropagatesBadCompletionStateQuality()
        {
            var peer = new ProductionDatasetClientPeer { BadStateQuality = true };
            var client = new ProductionDatasetClient(peer.Session.Object, peer.ManagementId);
            using var source = new MemoryStream([1]);
            ServiceResultException? exception = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await client.UploadAsync(Options(), source).ConfigureAwait(false));
            Assert.That(exception!.StatusCode, Is.EqualTo(StatusCodes.BadNoCommunication));
        }

        /// <summary>
        /// An asynchronous peer that never finishes cannot keep the high-level operation alive indefinitely.
        /// </summary>
        [Test]
        public void UploadCompletionWaitHonorsOverallDeadline()
        {
            var peer = new ProductionDatasetClientPeer { NeverCompletes = true };
            var client = new ProductionDatasetClient(peer.Session.Object, peer.ManagementId,
                new ProductionDatasetClientOptions { OperationTimeout = TimeSpan.FromMilliseconds(150) });
            using var source = new MemoryStream([1]);
            Assert.That(async () => await client.UploadAsync(Options(), source).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
            Assert.That(peer.CommitCalls, Is.EqualTo(1));
        }

        /// <summary>
        /// Invalid limits and missing dependencies fail at public construction boundaries.
        /// </summary>
        [Test]
        public void ClientConstructionRejectsUnboundedConfiguration()
        {
            Assert.Throws<ArgumentNullException>(() => new ProductionDatasetClient(null!, new NodeId(1u)));
            var peer = new ProductionDatasetClientPeer();
            Assert.Throws<ArgumentException>(() => new ProductionDatasetClient(peer.Session.Object, NodeId.Null));
            foreach (ProductionDatasetClientOptions options in new[]
            {
                new ProductionDatasetClientOptions { MaximumFileBytes = 0 },
                new ProductionDatasetClientOptions { MaximumDatasetCount = 0 },
                new ProductionDatasetClientOptions { ChunkSize = 0 },
                new ProductionDatasetClientOptions { OperationTimeout = TimeSpan.Zero },
                new ProductionDatasetClientOptions { OperationTimeout = TimeSpan.FromDays(2) }
            })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => new ProductionDatasetClientFactory(options));
            }
        }

        /// <summary>
        /// Creates generated options without introducing a second transfer format.
        /// </summary>
        private static ProductionDatasetWriteOptionsType Options()
        {
            return new ProductionDatasetWriteOptionsType { Storage = StorageEnumeration.FILE_SYSTEM, Name = "remote" };
        }
    }

    /// <summary>
    /// Models only an external session boundary and standard asynchronous transfer completion.
    /// </summary>
    internal sealed class ProductionDatasetClientPeer
    {
        /// <summary>
        /// Creates a minimal service peer for the generated standard file and state-machine clients.
        /// </summary>
        internal ProductionDatasetClientPeer()
        {
            var telemetry = new Mock<ITelemetryContext>();
            ServiceMessageContext context = ServiceMessageContext.Create(telemetry.Object);
            context.NamespaceUris.GetIndexOrAppend("urn:datasets:external-peer");
            context.NamespaceUris.GetIndexOrAppend(Namespaces.PlasticsRubber);
            Session.SetupGet(session => session.MessageContext).Returns(context);
            Session.SetupGet(session => session.Factory).Returns(context.Factory);
            Session.SetupGet(session => session.NamespaceUris).Returns(context.NamespaceUris);
            Session.Setup(session => session.TranslateBrowsePathsToNodeIdsAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<BrowsePath>>(), It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<BrowsePath>, CancellationToken>((_, paths, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    var results = new BrowsePathResult[paths.Count];
                    for (int i = 0; i < paths.Count; i++)
                    {
                        ArrayOf<RelativePathElement> elements = paths[i].RelativePath.Elements;
                        NodeId target = elements[0].TargetName.Name switch
                        {
                            BrowseNames.ProductionDatasetTransfer => m_transferId,
                            Opc.Ua.BrowseNames.CurrentState => elements.Count == 1 ? m_stateNameId : m_stateId,
                            _ => NodeId.Null
                        };
                        results[i] = target.IsNull
                            ? new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch }
                            : new BrowsePathResult
                            {
                                Targets =
                                [
                                    new BrowsePathTarget
                                    {
                                        TargetId = new ExpandedNodeId(target),
                                        RemainingPathIndex = uint.MaxValue
                                    }
                                ]
                            };
                    }
                    return new ValueTask<TranslateBrowsePathsToNodeIdsResponse>(new
                        TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = results.ToArrayOf()
                    });
                });
            Session.Setup(session => session.CallAsync(
                It.IsAny<RequestHeader>(), It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, ArrayOf<CallMethodRequest>, CancellationToken>((_, requests, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    var results = new CallMethodResult[requests.Count];
                    for (int i = 0; i < requests.Count; i++)
                    {
                        ArrayOf<Variant> output = [];
                        NodeId method = requests[i].MethodId;
                        if (method == Opc.Ua.MethodIds.TemporaryFileTransferType_GenerateFileForWrite)
                        {
                            output = [Variant.From(m_fileId), Variant.From(42u)];
                        }
                        else if (method == Opc.Ua.MethodIds.TemporaryFileTransferType_CloseAndCommit)
                        {
                            CommitCalls++;
                            output = [Variant.From(m_completionId)];
                        }
                        else if (method != Opc.Ua.MethodIds.FileType_Write && method != Opc.Ua.MethodIds.FileType_Close)
                        {
                            throw new InvalidOperationException("Unexpected external-peer method: " + method);
                        }
                        results[i] = new CallMethodResult { StatusCode = StatusCodes.Good, OutputArguments = output };
                    }
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = results.ToArrayOf()
                    });
                });
            Session.Setup(session => session.ReadAsync(
                It.IsAny<RequestHeader>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
                It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
                .Returns<RequestHeader, double, TimestampsToReturn, ArrayOf<ReadValueId>, CancellationToken>(
                    (_, _, _, requests, ct) =>
                    {
                        ct.ThrowIfCancellationRequested();
                        StateReads++;
                        NodeId state = StateReads < 2 || NeverCompletes
                            ? Opc.Ua.ObjectIds.FileTransferStateMachineType_ApplyWrite
                            : FailsCommit ? Opc.Ua.ObjectIds.FileTransferStateMachineType_Error
                            : Opc.Ua.ObjectIds.FileTransferStateMachineType_Idle;
                        var values = new DataValue[requests.Count];
                        for (int i = 0; i < requests.Count; i++)
                        {
                            values[i] = new DataValue(
                                requests[i].NodeId == m_stateId
                                    ? Variant.From(state) : Variant.From(new LocalizedText("Transfer state")),
                                BadStateQuality ? StatusCodes.BadNoCommunication : StatusCodes.Good);
                        }
                        return new ValueTask<ReadResponse>(new ReadResponse
                        {
                            ResponseHeader = new ResponseHeader(),
                            Results = values.ToArrayOf()
                        });
                    });
        }

        /// <summary>
        /// Gets the mocked external service boundary.
        /// </summary>
        internal Mock<ISession> Session { get; } = new();

        /// <summary>
        /// Gets the external management instance.
        /// </summary>
        internal NodeId ManagementId { get; } = new(100u, 1);

        /// <summary>
        /// Gets or sets whether processing ends in Error instead of Idle.
        /// </summary>
        internal bool FailsCommit { get; set; }

        /// <summary>
        /// Gets or sets whether processing remains active until the operation is canceled.
        /// </summary>
        internal bool NeverCompletes { get; set; }

        /// <summary>
        /// Gets or sets whether completion state has bad data quality.
        /// </summary>
        internal bool BadStateQuality { get; set; }

        /// <summary>
        /// Gets the observed commit calls.
        /// </summary>
        internal int CommitCalls { get; private set; }

        /// <summary>
        /// Gets the observed completion-state reads.
        /// </summary>
        internal int StateReads { get; private set; }

        /// <summary>The external standard transfer object.</summary>
        private readonly NodeId m_transferId = new(101u, 1);

        /// <summary>The external temporary file.</summary>
        private readonly NodeId m_fileId = new(102u, 1);

        /// <summary>The external completion state machine.</summary>
        private readonly NodeId m_completionId = new(103u, 1);

        /// <summary>The external localized state-name variable.</summary>
        private readonly NodeId m_stateNameId = new(104u, 1);

        /// <summary>The external state-id variable.</summary>
        private readonly NodeId m_stateId = new(105u, 1);
    }
}
