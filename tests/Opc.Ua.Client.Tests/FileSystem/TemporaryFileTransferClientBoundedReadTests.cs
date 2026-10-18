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
using Opc.Ua.Client.FileSystem;
using Opc.Ua.Client.Tests.SessionContexts;
using Opc.Ua.Client.Tests.Stack.Client.Fakes;

namespace Opc.Ua.Client.Tests.FileSystem
{
    /// <summary>
    /// Verifies bounded generated-file copying through real session dispatch and managed transport leases.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class TemporaryFileTransferClientBoundedReadTests
    {
        /// <summary>
        /// A recreated session cannot receive an old handle even when it reuses the public session identifier.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task CopyRejectsRecreatedManagedGenerationAsync(bool reuseToken, bool duringReadiness)
        {
            await using var fixture = new SessionRequestContextTestContext();
            var script = new TransferScript();
            fixture.Handler = script.DispatchAsync;
            NodeId originalSessionId = fixture.InnerSession.SessionId;
            NodeId originalToken = fixture.InnerSession.SaveSessionConfiguration().AuthenticationToken;
            ValueTask RecreateAsync(CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                fixture.InnerSession.SessionCreated(originalSessionId,
                    reuseToken ? originalToken : NodeId.Parse("s=replacement-token"));
                return default;
            }
            if (duringReadiness)
            {
                script.GenerationResponse.Results[0].OutputArguments =
                    [Variant.From(s_file), Variant.From(kHandle), Variant.From(s_stateMachine)];
                script.OnStateRead = RecreateAsync;
            }
            else
            {
                script.OnGenerate = RecreateAsync;
            }
            script.Chunks.Enqueue(ByteString.Empty);
            var client = new TemporaryFileTransferClient(fixture.ManagedSession, s_transfer);
            using var destination = new MemoryStream();

            AggregateException failure = Assert.ThrowsAsync<AggregateException>(
                async () => await client.CopyToAsync(default, destination, 3).ConfigureAwait(false))!;

            Assert.That(failure.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(failure.InnerExceptions, Has.All.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(script.Methods, Is.EqualTo(new[] { s_generateMethod }));
            Assert.That(fixture.Requests, Has.Count.EqualTo(duringReadiness ? 4 : 2));
            Assert.That(fixture.Requests.Select(request => request.RequestHeader.AuthenticationToken),
                Is.All.EqualTo(originalToken));
            Assert.That(destination.Length, Is.Zero);
            Assert.That(fixture.InnerSession.Connected, Is.True);
            fixture.Channel.Verify(channel => channel.CloseAsync(It.IsAny<CancellationToken>()), Times.Never);
            fixture.Channel.Verify(channel => channel.Dispose(), Times.Never);
        }

        /// <summary>
        /// Recovery queued behind generation is rechecked at the next dispatch, not just before entering the gate.
        /// </summary>
        [Test]
        public async Task CopyRejectsRecoveryBetweenGenerationAndReadDispatchAsync()
        {
            await using var fixture = new SessionRequestContextTestContext();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            async Task RecreateBehindDispatchAsync()
            {
                using (await fixture.ServiceGate.WriterLockAsync().ConfigureAwait(false))
                {
                    entered.TrySetResult(true);
                    await release.Task.ConfigureAwait(false);
                    fixture.Recreate();
                }
            }
            Task? recovery = null;
            var script = new TransferScript
            {
                OnGenerate = _ =>
                {
                    recovery = RecreateBehindDispatchAsync();
                    return default;
                }
            };
            script.Chunks.Enqueue(ByteString.Empty);
            fixture.Handler = script.DispatchAsync;
            var client = new TemporaryFileTransferClient(fixture.ManagedSession, s_transfer);
            using var destination = new MemoryStream();
            Task<long> copying = client.CopyToAsync(default, destination, 3).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(copying.IsCompleted, Is.False);
                Assert.That(fixture.Requests, Has.Count.EqualTo(2));
                release.TrySetResult(true);

                AggregateException failure = Assert.ThrowsAsync<AggregateException>(
                    async () => await copying.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false))!;

                Assert.That(failure.InnerExceptions, Has.Count.EqualTo(2));
                Assert.That(failure.InnerExceptions, Has.All.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadSessionIdInvalid));
                Assert.That(script.Methods, Is.EqualTo(new[] { s_generateMethod }));
                Assert.That(fixture.Requests, Has.Count.EqualTo(2));
                Assert.That(destination.Length, Is.Zero);
            }
            finally
            {
                release.TrySetResult(true);
                if (recovery != null)
                {
                    await recovery.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// Cancellation cannot discard a generated handle returned through a managed transport lease.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ManagedCopyClosesHandleReturnedAfterCancellationAsync(bool delayedResponse)
        {
            await using var channels = new SessionChannelHarness();
            await using Client.ManagedSession owner = await Client.ManagedSession.CreateAsync(
                new ManagedSessionOptions { Endpoint = SessionChannelHarness.CreateEndpoint() },
                channels.Configuration,
                new DefaultSessionFactory(channels.Telemetry),
                telemetry: channels.Telemetry,
                channelManager: channels.Manager,
                updateBeforeConnect: false).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            Assert.That(owner.TransportChannel, Is.InstanceOf<IManagedTransportChannel>());
            using var cancellation = new CancellationTokenSource();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var script = new TransferScript
            {
                OnGenerate = async ct =>
                {
                    Assert.That(ct, Is.EqualTo(cancellation.Token));
                    entered.TrySetResult(true);
                    if (delayedResponse)
                    {
                        await release.Task.ConfigureAwait(false);
                    }
                    else
                    {
                        await cancellation.CancelAsync().ConfigureAwait(false);
                    }
                }
            };
            ScriptedChannel transport = channels.CreatedChannels[0];
            transport.RequestHandler = (request, ct) =>
                request is CallRequest or TranslateBrowsePathsToNodeIdsRequest
                    ? script.DispatchAsync(request, ct)
                    : new ValueTask<IServiceResponse>(transport.CreateResponse(request));
            var client = new TemporaryFileTransferClient(owner, s_transfer);
            using var destination = new MemoryStream();
            Task<long> copying = client.CopyToAsync(default, destination, 3, cancellation.Token).AsTask();
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                if (delayedResponse)
                {
                    await cancellation.CancelAsync().ConfigureAwait(false);
                    release.TrySetResult(true);
                }
                await Assert.ThatAsync(
                    () => copying.WaitAsync(TimeSpan.FromSeconds(10)),
                    Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

                Assert.That(script.Methods, Is.EqualTo(new[] { s_generateMethod, MethodIds.FileType_Close }));
                Assert.That(script.CloseToken.CanBeCanceled, Is.True);
                Assert.That(script.CloseToken.IsCancellationRequested, Is.False);
                Assert.That(script.CloseToken, Is.Not.EqualTo(cancellation.Token));
                Assert.That(script.CloseDiscoveries, Is.Zero);
                Assert.That(destination.Length, Is.Zero);
                Assert.That(destination.CanWrite, Is.True);
                Assert.That(owner.Connected, Is.True);
                Assert.That(transport.CloseCount, Is.Zero);
                Assert.That(transport.DisposeCount, Is.Zero);
            }
            finally
            {
                release.TrySetResult(true);
            }
        }

        /// <summary>
        /// Cleanup spends its independent deadline on the declaration Close call, not optional metadata discovery.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CopyUsesDeclarationCloseWithoutDiscoveryAsync(bool managed)
        {
            await using var fixture = new SessionRequestContextTestContext();
            var script = new TransferScript { RejectCloseDiscovery = true };
            script.Chunks.Enqueue(ByteString.From([1, 2, 3]));
            script.Chunks.Enqueue(ByteString.Empty);
            fixture.Handler = script.DispatchAsync;
            var client = new TemporaryFileTransferClient(
                managed ? fixture.ManagedSession : fixture.InnerSession, s_transfer);
            using var destination = new MemoryStream();

            long copied = await client.CopyToAsync(default, destination, 3).ConfigureAwait(false);

            Assert.That(copied, Is.EqualTo(3));
            Assert.That(destination.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(script.Methods, Is.EqualTo(
                new[] { s_generateMethod, s_readMethod, s_readMethod, MethodIds.FileType_Close }));
            Assert.That(script.CloseDiscoveries, Is.Zero);
            Assert.That(script.CloseToken.CanBeCanceled, Is.True);
            Assert.That(script.CloseToken.IsCancellationRequested, Is.False);
            Assert.That(destination.CanWrite, Is.True);
            Assert.That(fixture.InnerSession.Connected, Is.True);
            fixture.Channel.Verify(channel => channel.CloseAsync(It.IsAny<CancellationToken>()), Times.Never);
            fixture.Channel.Verify(channel => channel.Dispose(), Times.Never);
        }

        /// <summary>
        /// A non-Good generation result cannot hide a valid allocated handle from cleanup.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        public async Task CopyClosesHandleBeforeRejectingGenerationStatusAsync(bool serviceStatus, bool badStatus)
        {
            await using var fixture = new SessionRequestContextTestContext();
            var script = new TransferScript();
            StatusCode status = badStatus ? StatusCodes.BadUserAccessDenied : StatusCodes.Uncertain;
            SetStatus(script.GenerationResponse, serviceStatus, status);
            fixture.Handler = script.DispatchAsync;
            var client = new TemporaryFileTransferClient(fixture.InnerSession, s_transfer);
            using var destination = new MemoryStream();

            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.CopyToAsync(default, destination, 3).ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo(status));
            Assert.That(script.Methods, Is.EqualTo(new[] { s_generateMethod, MethodIds.FileType_Close }));
            Assert.That(destination.Length, Is.Zero);
        }

        /// <summary>
        /// Response-count and diagnostic errors are rejected only after retaining the returned file handle.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CopyClosesHandleFromMalformedGenerationResponseAsync(bool extraResult)
        {
            await using var fixture = new SessionRequestContextTestContext();
            var script = new TransferScript();
            if (extraResult)
            {
                script.GenerationResponse.Results = [script.GenerationResponse.Results[0], new CallMethodResult()];
            }
            else
            {
                script.GenerationResponse.DiagnosticInfos = [new DiagnosticInfo(), new DiagnosticInfo()];
            }
            fixture.Handler = script.DispatchAsync;
            var client = new TemporaryFileTransferClient(fixture.InnerSession, s_transfer);
            using var destination = new MemoryStream();

            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.CopyToAsync(default, destination, 3).ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadUnexpectedError));
            Assert.That(script.Methods, Is.EqualTo(new[] { s_generateMethod, MethodIds.FileType_Close }));
            Assert.That(destination.Length, Is.Zero);
        }

        /// <summary>
        /// A rejected generation without a complete handle cannot trigger an invented Close.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task CopyRejectsGenerationWithoutInventingHandleAsync(bool uncertain)
        {
            await using var fixture = new SessionRequestContextTestContext();
            var script = new TransferScript();
            script.GenerationResponse.Results[0].StatusCode = uncertain ? StatusCodes.Uncertain : StatusCodes.Good;
            script.GenerationResponse.Results[0].OutputArguments = [Variant.From(s_file)];
            fixture.Handler = script.DispatchAsync;
            var client = new TemporaryFileTransferClient(fixture.InnerSession, s_transfer);
            using var destination = new MemoryStream();

            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.CopyToAsync(default, destination, 3).ConfigureAwait(false))!;

            Assert.That(failure.StatusCode,
                Is.EqualTo(uncertain ? StatusCodes.Uncertain : StatusCodes.BadUnexpectedError));
            Assert.That(script.Methods, Is.EqualTo(new[] { s_generateMethod }));
            Assert.That(destination.Length, Is.Zero);
        }

        /// <summary>
        /// Generation rejection and cleanup failure remain independently visible in their original order.
        /// </summary>
        [Test]
        public async Task CopyPreservesGenerationAndCleanupFailuresAsync()
        {
            await using var fixture = new SessionRequestContextTestContext();
            var script = new TransferScript();
            script.GenerationResponse.Results[0].StatusCode = StatusCodes.Uncertain;
            script.CloseResponse.Results[0].StatusCode = StatusCodes.BadUserAccessDenied;
            fixture.Handler = script.DispatchAsync;
            var client = new TemporaryFileTransferClient(fixture.InnerSession, s_transfer);
            using var destination = new MemoryStream();

            AggregateException failure = Assert.ThrowsAsync<AggregateException>(
                async () => await client.CopyToAsync(default, destination, 3).ConfigureAwait(false))!;

            Assert.That(failure.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(failure.InnerExceptions[0], Is.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.Uncertain));
            Assert.That(failure.InnerExceptions[1], Is.TypeOf<ServiceResultException>()
                .With.Property(nameof(ServiceResultException.StatusCode)).EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(script.Methods, Is.EqualTo(new[] { s_generateMethod, MethodIds.FileType_Close }));
        }

        /// <summary>
        /// Delaying generation validation does not weaken service or method validation for Read and Close.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task CopyKeepsReadAndCloseStatusValidationAsync(bool closeFails, bool serviceStatus)
        {
            await using var fixture = new SessionRequestContextTestContext();
            var script = new TransferScript();
            script.Chunks.Enqueue(closeFails ? ByteString.Empty : ByteString.From([1]));
            SetStatus(closeFails ? script.CloseResponse : script.ReadResponse, serviceStatus, StatusCodes.Uncertain);
            fixture.Handler = script.DispatchAsync;
            var client = new TemporaryFileTransferClient(fixture.InnerSession, s_transfer);
            using var destination = new MemoryStream();

            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await client.CopyToAsync(default, destination, 3).ConfigureAwait(false))!;

            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.Uncertain));
            Assert.That(script.Methods, Is.EqualTo(new[] { s_generateMethod, s_readMethod, MethodIds.FileType_Close }));
            Assert.That(destination.Length, Is.Zero);
        }

        /// <summary>
        /// Assigns the selected service or method status without altering the returned output arguments.
        /// </summary>
        private static void SetStatus(CallResponse response, bool serviceStatus, StatusCode status)
        {
            if (serviceStatus)
            {
                response.ResponseHeader.ServiceResult = status;
            }
            else
            {
                response.Results[0].StatusCode = status;
            }
        }

        /// <summary>
        /// Provides deterministic temporary-file service responses while recording actual method dispatches.
        /// </summary>
        private sealed class TransferScript
        {
            /// <summary>
            /// Gets the generation response, whose metadata tests may corrupt independently of its handle.
            /// </summary>
            public CallResponse GenerationResponse { get; } = CreateResponse(
                [Variant.From(s_file), Variant.From(kHandle), Variant.From(NodeId.Null)]);

            /// <summary>
            /// Gets the Read response, with output populated from the configured chunks.
            /// </summary>
            public CallResponse ReadResponse { get; } = CreateResponse([]);

            /// <summary>
            /// Gets the Close response.
            /// </summary>
            public CallResponse CloseResponse { get; } = CreateResponse([]);

            /// <summary>
            /// Gets the payload chunks; unexpected Read calls fail when none remain.
            /// </summary>
            public Queue<ByteString> Chunks { get; } = new();

            /// <summary>
            /// Gets the methods that reached the transport.
            /// </summary>
            public ArrayOf<NodeId> Methods => m_methods.ToArrayOf();

            /// <summary>
            /// Gets the number of unnecessary Close-metadata lookups.
            /// </summary>
            public int CloseDiscoveries { get; private set; }

            /// <summary>
            /// Gets the cancellation token actually delivered to Close.
            /// </summary>
            public CancellationToken CloseToken { get; private set; }

            /// <summary>
            /// Gets or sets whether optional Close metadata is unavailable.
            /// </summary>
            public bool RejectCloseDiscovery { get; init; }

            /// <summary>
            /// Gets or sets the action between generating a handle and returning its response.
            /// </summary>
            public Func<CancellationToken, ValueTask>? OnGenerate { get; set; }

            /// <summary>
            /// Gets or sets the action before a generation-readiness response completes.
            /// </summary>
            public Func<CancellationToken, ValueTask>? OnStateRead { get; set; }

            /// <summary>
            /// Executes only the expected temporary-file protocol operations.
            /// </summary>
            public async ValueTask<IServiceResponse> DispatchAsync(IServiceRequest request, CancellationToken ct)
            {
                if (request is TranslateBrowsePathsToNodeIdsRequest translate)
                {
                    Assert.That(translate.BrowsePaths.Count, Is.EqualTo(1));
                    string? name = translate.BrowsePaths[0].RelativePath.Elements[0].TargetName.Name;
                    if (name == BrowseNames.Close)
                    {
                        CloseDiscoveries++;
                        if (RejectCloseDiscovery)
                        {
                            throw new ServiceResultException(StatusCodes.BadTimeout, "Close metadata is unavailable.");
                        }
                    }
                    NodeId target = name switch
                    {
                        BrowseNames.GenerateFileForRead => s_generateMethod,
                        BrowseNames.Read => s_readMethod,
                        BrowseNames.Close => MethodIds.FileType_Close,
                        BrowseNames.CurrentState => s_stateId,
                        _ => throw new AssertionException("Unexpected path: " + name)
                    };
                    return new TranslateBrowsePathsToNodeIdsResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results =
                        [
                            new BrowsePathResult
                            {
                                Targets =
                                [
                                    new BrowsePathTarget { TargetId = target, RemainingPathIndex = uint.MaxValue }
                                ]
                            }
                        ]
                    };
                }
                if (request is ReadRequest read)
                {
                    Assert.That(read.NodesToRead.Count, Is.EqualTo(1));
                    Assert.That(read.NodesToRead[0].NodeId, Is.EqualTo(s_stateId));
                    if (OnStateRead != null)
                    {
                        await OnStateRead(ct).ConfigureAwait(false);
                    }
                    return new ReadResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [new DataValue(Variant.From(ObjectIds.FileTransferStateMachineType_ReadTransfer))]
                    };
                }
                Assert.That(request, Is.TypeOf<CallRequest>());
                var call = (CallRequest)request;
                Assert.That(call.MethodsToCall.Count, Is.EqualTo(1));
                CallMethodRequest method = call.MethodsToCall[0];
                m_methods.Add(method.MethodId);
                if (method.MethodId == s_generateMethod)
                {
                    Assert.That(method.ObjectId, Is.EqualTo(s_transfer));
                    if (OnGenerate != null)
                    {
                        await OnGenerate(ct).ConfigureAwait(false);
                    }
                    return GenerationResponse;
                }
                Assert.That(method.ObjectId, Is.EqualTo(s_file));
                Assert.That(method.InputArguments[0].TryGetValue(out uint handle), Is.True);
                Assert.That(handle, Is.EqualTo(kHandle));
                if (method.MethodId == s_readMethod)
                {
                    Assert.That(method.InputArguments.Count, Is.EqualTo(2));
                    ReadResponse.Results[0].OutputArguments = [Variant.From(Chunks.Dequeue())];
                    return ReadResponse;
                }
                Assert.That(method.MethodId, Is.EqualTo(MethodIds.FileType_Close));
                Assert.That(method.InputArguments.Count, Is.EqualTo(1));
                CloseToken = ct;
                return CloseResponse;
            }

            /// <summary>
            /// Creates one successful method response.
            /// </summary>
            private static CallResponse CreateResponse(ArrayOf<Variant> outputs)
            {
                return new CallResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [new CallMethodResult { OutputArguments = outputs }]
                };
            }

            private readonly List<NodeId> m_methods = [];
        }

        private const uint kHandle = 7;
        private static readonly NodeId s_transfer = new(1000u);
        private static readonly NodeId s_file = new(1001u);
        private static readonly NodeId s_generateMethod = new(1002u);
        private static readonly NodeId s_readMethod = new(1003u);
        private static readonly NodeId s_stateMachine = new(1004u);
        private static readonly NodeId s_stateId = new(1005u);
    }
}
