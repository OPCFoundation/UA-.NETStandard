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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client.Tests.Stack.Client.Fakes;

namespace Opc.Ua.Client.Tests.SessionContexts
{
    /// <summary>
    /// Proves that the borrowed client's actual service dispatch cannot adopt a replacement session.
    /// </summary>
    [TestFixture]
    [Parallelizable(ParallelScope.All)]
    public sealed class SessionRequestContextTests
    {
        /// <summary>
        /// Identity changes notify old contexts before another capture can subscribe for the new generation.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SessionIdentityNotificationExcludesNewCapturesAsync(bool reusedIdentity)
        {
            await using var fixture = new SessionRequestContextTestContext();
            var identity = (Lock)typeof(Session)
                .GetField("m_requestContextIdentity", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(fixture.InnerSession)!;
            bool notified = false;
            bool captureExcluded = false;
            void ObserveChange()
            {
                notified = true;
                captureExcluded = identity.IsHeldByCurrentThread;
            }
            fixture.InnerSession.RequestContextChanged += ObserveChange;
            try
            {
                await using SessionRequestContext original =
                    await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
                if (reusedIdentity)
                {
                    fixture.InnerSession.SessionCreated(
                        NodeId.Parse("s=original-session"), NodeId.Parse("s=original-token"));
                }
                else
                {
                    fixture.Recreate();
                }

                Assert.That(notified, Is.True);
                Assert.That(captureExcluded, Is.True,
                    "A new capture must not enter between the identity change and its invalidation notification.");
                Assert.That(original.Invalidated.IsCompleted, Is.True);
                await using SessionRequestContext current =
                    await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
                Assert.That(current.Invalidated.IsCompleted, Is.False);
                Assert.That(current.SessionId, Is.EqualTo(fixture.InnerSession.SessionId));
                fixture.Handler = (_, _) => new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = [new DataValue(Variant.From(19))]
                });

                ReadResponse response = await current.Session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                    [new ReadValueId { NodeId = new NodeId(10u), AttributeId = Attributes.Value }],
                    CancellationToken.None)
                    .ConfigureAwait(false);

                Assert.That(response.Results[0].WrappedValue.TryGetValue(out int value), Is.True);
                Assert.That(value, Is.EqualTo(19));
                Assert.That(fixture.Requests, Has.Count.EqualTo(1));
                Assert.That(fixture.Requests[0].RequestHeader.AuthenticationToken, Is.EqualTo(NodeId.Parse(
                    reusedIdentity ? "s=original-token" : "s=replacement-token")));
                Assert.That(current.Invalidated.IsCompleted, Is.False);
            }
            finally
            {
                fixture.InnerSession.RequestContextChanged -= ObserveChange;
            }
        }

        /// <summary>
        /// Queues a request behind recovery after the caller observed the original identity.
        /// </summary>
        [Test]
        public async Task DispatchRejectsGenerationChangedWhileWaitingForGate()
        {
            await using var fixture = new SessionRequestContextTestContext();
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            ISession borrowed = context.Session;
            Session original = fixture.InnerSession;
            Assert.That(borrowed.SessionId, Is.EqualTo(original.SessionId));

            Task<HistoryReadResponse> request;
            using (await fixture.ServiceGate.WriterLockAsync().ConfigureAwait(false))
            {
                request = ReadHistoryAsync(borrowed).AsTask();
                Assert.That(request.IsCompleted, Is.False);
                fixture.Recreate();
            }

            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await request.ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(fixture.InnerSession, Is.SameAs(original));
            Assert.That(fixture.Requests, Is.Empty);
            Assert.That(context.Invalidated.IsCompleted, Is.True);
            Assert.That(context.SessionId, Is.EqualTo(NodeId.Parse("s=original-session")));
        }

        /// <summary>
        /// A reader whose drain is bypassed cannot cause the next call to use the new generation.
        /// </summary>
        [Test]
        public async Task RecoveryDrainBypassDoesNotRetargetLaterDispatch()
        {
            await using var fixture = new SessionRequestContextTestContext();
            var reply = new TaskCompletionSource<IServiceResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.Handler = (_, _) => new ValueTask<IServiceResponse>(reply.Task);
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);

            Task<HistoryReadResponse> first = ReadHistoryAsync(context.Session).AsTask();
            Task<AsyncReaderWriterLock.Releaser> recovery = fixture.ServiceGate.WriterLockAsync(
                TimeSpan.FromSeconds(1), fixture.TimeProvider, static () => true, CancellationToken.None).AsTask();
            try
            {
                Assert.That(first.IsCompleted, Is.False);
                Assert.That(recovery.IsCompleted, Is.False);
                fixture.TimeProvider.Advance(TimeSpan.FromSeconds(1));
                using (await recovery.ConfigureAwait(false))
                {
                    fixture.Recreate();
                }
            }
            finally
            {
                reply.TrySetResult(new HistoryReadResponse { Results = [] });
            }
            _ = await first.ConfigureAwait(false);
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await ReadHistoryAsync(context.Session).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(fixture.Requests, Has.Count.EqualTo(1));
            Assert.That(fixture.Requests[0].RequestHeader.AuthenticationToken,
                Is.EqualTo(NodeId.Parse("s=original-token")));
        }

        /// <summary>
        /// Namespace metadata stays captured and remapping is rejected before sending the request.
        /// </summary>
        [Test]
        public async Task BorrowedSessionKeepsOriginalNamespaceMapping()
        {
            await using var fixture = new SessionRequestContextTestContext();
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            Assert.That(context.Session.NamespaceUris.GetString(1), Is.EqualTo("urn:request-context:original"));

            Task<HistoryReadResponse> request;
            using (await fixture.ServiceGate.WriterLockAsync().ConfigureAwait(false))
            {
                request = ReadHistoryAsync(context.Session).AsTask();
                fixture.InnerSession.NamespaceUris.Update(
                    ["http://opcfoundation.org/UA/", "urn:request-context:replacement"]);
            }

            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await request.ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(context.Session.NamespaceUris.GetString(1), Is.EqualTo("urn:request-context:original"));
            Assert.That(fixture.Requests, Is.Empty);
        }

        /// <summary>
        /// Public identifiers alone cannot make a captured context valid on another physical connection.
        /// </summary>
        [Test]
        public async Task PhysicalChannelReplacementRejectsReusedSessionIdentity()
        {
            await using var fixture = new SessionRequestContextTestContext();
            var replacement = new Mock<ITransportChannel>();
            replacement.SetupGet(channel => channel.MessageContext).Returns(fixture.MessageContext);
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            Task<HistoryReadResponse> request;
            using (await fixture.ServiceGate.WriterLockAsync().ConfigureAwait(false))
            {
                request = ReadHistoryAsync(context.Session).AsTask();
                fixture.ReplaceTransport(replacement.Object);
            }
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await request.ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(fixture.InnerSession.SessionId, Is.EqualTo(context.SessionId));
            Assert.That(fixture.Requests, Is.Empty);
            replacement.Verify(channel => channel.SendRequestAsync(
                It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// A recreation is still a new generation if a server reuses both externally visible identifiers.
        /// </summary>
        [Test]
        public async Task GenerationCounterRejectsReusedIdentifiers()
        {
            await using var fixture = new SessionRequestContextTestContext();
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            fixture.InnerSession.SessionCreated(NodeId.Parse("s=original-session"), NodeId.Parse("s=original-token"));
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await ReadHistoryAsync(context.Session).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(context.Invalidated.IsCompleted, Is.True);
            Assert.That(fixture.Requests, Is.Empty);
        }

        /// <summary>
        /// A caller-supplied header cannot replace the captured authentication context.
        /// </summary>
        [Test]
        public async Task ForeignAuthenticationHeaderIsRejectedBeforeDispatch()
        {
            await using var fixture = new SessionRequestContextTestContext();
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await context.Session.ReadAsync(
                    new RequestHeader { AuthenticationToken = NodeId.Parse("s=foreign-token") },
                    0, TimestampsToReturn.Neither,
                    [new ReadValueId { NodeId = new NodeId(10u), AttributeId = Attributes.Value }],
                    CancellationToken.None).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(fixture.Requests, Is.Empty);
        }

        /// <summary>
        /// Borrowed generated services keep the owner's negotiated batching limits without sharing mutable options.
        /// </summary>
        [Test]
        public async Task BorrowedSessionPreservesNegotiatedBatchingLimits()
        {
            await using var fixture = new SessionRequestContextTestContext();
            fixture.InnerSession.OperationLimits.MaxNodesPerRead = 1;
            fixture.InnerSession.ServerCapabilities.MaxBrowseContinuationPoints = 7;
            fixture.Handler = (request, _) =>
            {
                var read = (ReadRequest)request;
                return new ValueTask<IServiceResponse>(new ReadResponse
                {
                    Results = read.NodesToRead.ConvertAll(static _ => new DataValue(Variant.From(42)))
                });
            };
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            fixture.InnerSession.OperationLimits.MaxNodesPerRead = 10;
            ReadResponse response = await context.Session.ReadAsync(
                null, 0, TimestampsToReturn.Neither,
                [
                    new ReadValueId { NodeId = new NodeId(10u), AttributeId = Attributes.Value },
                    new ReadValueId { NodeId = new NodeId(11u), AttributeId = Attributes.Value }
                ], CancellationToken.None).ConfigureAwait(false);
            Assert.That(fixture.Requests, Has.Count.EqualTo(2));
            Assert.That(response.Results.Count, Is.EqualTo(2));
            Assert.That(context.Session.OperationLimits.MaxNodesPerRead, Is.EqualTo(1));
            Assert.That(context.Session.ServerCapabilities.MaxBrowseContinuationPoints, Is.EqualTo(7));
            Assert.That(context.Session.SystemContext.NamespaceUris.GetString(1),
                Is.EqualTo("urn:request-context:original"));
        }

        /// <summary>
        /// Disposal and a direct Close on the borrowed session affect only its local resources.
        /// </summary>
        [Test]
        public async Task ScopedDisposalDoesNotCloseOwner()
        {
            await using var fixture = new SessionRequestContextTestContext();
            SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            await using (context.ConfigureAwait(false))
            {
                StatusCode closed = await context.Session.CloseAsync().ConfigureAwait(false);
                Assert.That(closed, Is.EqualTo(StatusCodes.Good));
            }
            Assert.That(fixture.InnerSession.Connected, Is.True);
            Assert.That(fixture.ManagedSession.SessionId, Is.EqualTo(NodeId.Parse("s=original-session")));
            Assert.That(fixture.Requests, Is.Empty);
            fixture.Channel.Verify(channel => channel.CloseAsync(It.IsAny<CancellationToken>()), Times.Never);
            fixture.Channel.Verify(channel => channel.Dispose(), Times.Never);
        }

        /// <summary>
        /// A completed method result is not replaced by a later caller cancellation or session change.
        /// </summary>
        [Test]
        public async Task ScopedDispatchPreservesCommittedResponseAfterCancellation()
        {
            await using var fixture = new SessionRequestContextTestContext();
            using var cancellation = new CancellationTokenSource();
            var expected = new CallMethodResult { StatusCode = StatusCodes.GoodCompletesAsynchronously };
            fixture.Handler = async (_, _) =>
            {
                fixture.Recreate();
                await cancellation.CancelAsync().ConfigureAwait(false);
                return new CallResponse { Results = [expected] };
            };
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);

            CallResponse response = await context.ExecuteAsync(
                (session, ct) => session.CallAsync(null,
                    [new CallMethodRequest { ObjectId = new NodeId(1u), MethodId = new NodeId(2u) }], ct),
                cancellation.Token).ConfigureAwait(false);

            Assert.That(response.Results[0], Is.SameAs(expected));
            Assert.That(response.Results[0].StatusCode,
                Is.EqualTo(StatusCodes.GoodCompletesAsynchronously));
            Assert.That(cancellation.IsCancellationRequested, Is.True);
            Assert.That(fixture.Requests, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// The real managed transport path preserves a known result and does not follow channel recovery.
        /// </summary>
        [Test]
        public async Task ManagedTransportContextPreservesCommittedResultAndExpiresOnRecovery()
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
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(owner).ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            int calls = 0;
            var expected = new CallMethodResult { StatusCode = StatusCodes.GoodCompletesAsynchronously };
            ScriptedChannel transport = channels.CreatedChannels[0];
            transport.RequestHandler = async (request, _) =>
            {
                if (request is not CallRequest)
                {
                    return transport.CreateResponse(request);
                }
                calls++;
                await cancellation.CancelAsync().ConfigureAwait(false);
                return new CallResponse
                {
                    ResponseHeader = transport.CreateGoodHeader(),
                    Results = [expected]
                };
            };
            CallResponse response = await context.Session.CallAsync(
                null, [new CallMethodRequest { ObjectId = new NodeId(1u), MethodId = new NodeId(2u) }],
                cancellation.Token).ConfigureAwait(false);
            Assert.That(response.Results[0], Is.SameAs(expected));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(cancellation.IsCancellationRequested, Is.True);

            await channels.Manager.ReconnectAsync((IManagedTransportChannel)owner.TransportChannel)
                .AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await ReadHistoryAsync(context.Session).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadSessionIdInvalid));
            Assert.That(context.Invalidated.IsCompleted, Is.True);
            Assert.That(calls, Is.EqualTo(1));
        }

        /// <summary>
        /// Dispatch returns the original service failure without replaying the request.
        /// </summary>
        [Test]
        public async Task ScopedDispatchPreservesServiceFailureWithoutRetry()
        {
            await using var fixture = new SessionRequestContextTestContext();
            fixture.Handler = static (_, _) => throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await ReadHistoryAsync(context.Session).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(fixture.Requests, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Local disposal callbacks still run after invalidation even when the supplied token is cancelled.
        /// </summary>
        [Test]
        public async Task StaleExecuteStillRunsLocalCleanup()
        {
            await using var fixture = new SessionRequestContextTestContext();
            await using SessionRequestContext context =
                await SessionRequestContext.CaptureAsync(fixture.ManagedSession).ConfigureAwait(false);
            fixture.Recreate();
            bool cleaned = false;
            using var cancellation = new CancellationTokenSource();
            await cancellation.CancelAsync().ConfigureAwait(false);
            int result = await context.ExecuteAsync((session, _) =>
            {
                Assert.That(session, Is.SameAs(context.Session));
                cleaned = true;
                return new ValueTask<int>(12);
            }, cancellation.Token).ConfigureAwait(false);
            Assert.That(cleaned, Is.True);
            Assert.That(result, Is.EqualTo(12));
            Assert.That(fixture.Requests, Is.Empty);
        }

        /// <summary>
        /// No fixed-generation guarantee is fabricated for an arbitrary ISession implementation.
        /// </summary>
        [Test]
        public void CapturingCustomSessionFailsClosed()
        {
            var custom = new Mock<ISession>();
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(
                async () => await SessionRequestContext.CaptureAsync(custom.Object).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo(StatusCodes.BadNotSupported));
            custom.VerifyNoOtherCalls();
        }

        /// <summary>
        /// Sends an actual history request carrying a representative retained continuation point.
        /// </summary>
        private static ValueTask<HistoryReadResponse> ReadHistoryAsync(ISession session)
        {
            return session.HistoryReadAsync(
                null, new ExtensionObject(new ReadRawModifiedDetails()),
                TimestampsToReturn.Both, false,
                [new HistoryReadValueId { NodeId = new NodeId(3u, 1), ContinuationPoint = ByteString.From("point"u8) }],
                CancellationToken.None);
        }
    }
}
