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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using ServerSession = Opc.Ua.Server.Session;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Regression tests for request admission while a Session is being closed and for
    /// the rejection accounting of requests refused during validation.
    /// </summary>
    [TestFixture]
    [Category("Session")]
    public class SessionRequestAdmissionRegressionTests
    {
        /// <summary>
        /// A request whose cancellation callback fails while its Session is closed must not
        /// leave the Session marked closing but still registered (OPC 10000-4 5.7.2.1).
        /// </summary>
        [Test]
        public async Task CloseSessionCompletesWhenAbortingARequestFailsAsync()
        {
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));
            StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
            try
            {
                (RequestHeader requestHeader, SecureChannelContext _) =
                    await server.CreateAndActivateSessionAsync("AbortFails").ConfigureAwait(false);
                var serverInternal = (ServerInternalData)server.CurrentInstance;
                ISession? session = serverInternal.SessionManager.GetSession(requestHeader.AuthenticationToken);
                Assert.That(session, Is.Not.Null);

                using var lifetime = new RequestLifetime();
                using CancellationTokenRegistration registration = lifetime.CancellationToken.Register(
                    () => throw new InvalidOperationException("cancellation callback"));
                var outstanding = new OperationContext(
                    new RequestHeader { RequestHandle = 1 },
                    null!,
                    RequestType.Call,
                    lifetime,
                    session!);
                using IDisposable requestScope = serverInternal.RequestManager.EnterRequestScope(outstanding);

                await Assert.DoesNotThrowAsync(async () => await serverInternal
                    .CloseSessionAsync(null!, session!.Id, true)
                    .ConfigureAwait(false));

                Assert.That(
                    serverInternal.SessionManager.GetSession(requestHeader.AuthenticationToken),
                    Is.Null,
                    "The Session must be removed although aborting its request failed.");
                Assert.That(outstanding.OperationStatus.Code, Is.EqualTo(StatusCodes.BadSessionClosed));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A request the Session admitted just before a close marked it closing, and that
        /// registered only after the close aborted the outstanding requests, must not run
        /// against the closing Session (OPC 10000-4 5.7.2.1).
        /// </summary>
        [Test]
        public async Task RequestRegisteredAfterTheCloseSweepIsRejectedAsync()
        {
            var fixture = new ServerFixture<AdmissionServer>(t => new AdmissionServer(t));
            AdmissionServer server = await fixture.StartAsync().ConfigureAwait(false);
            try
            {
                (RequestHeader requestHeader, SecureChannelContext secureChannelContext) =
                    await server.CreateAndActivateSessionAsync("ClosingWindow").ConfigureAwait(false);
                var serverInternal = (ServerInternalData)server.CurrentInstance;
                var sessionManager = (ClosingWindowSessionManager)serverInternal.SessionManager;
                uint rejectedBefore = serverInternal.ServerDiagnostics.RejectedRequestsCount;

                // The close starts after Session.ValidateRequest admitted the Read.
                sessionManager.MarkClosingAfterAdmission = true;
                ServiceResultException ex = (await Assert.ThrowsAsync<ServiceResultException>(
                    async () => await server.ReadAsync(
                        secureChannelContext,
                        new RequestHeader { AuthenticationToken = requestHeader.AuthenticationToken },
                        0,
                        TimestampsToReturn.Neither,
                        [new ReadValueId { NodeId = VariableIds.Server_ServerStatus_State, AttributeId = Attributes.Value }],
                        RequestLifetime.None).ConfigureAwait(false)))!;

                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
                Assert.That(
                    serverInternal.ServerDiagnostics.RejectedRequestsCount,
                    Is.EqualTo(rejectedBefore + 1));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A queued request cancelled by an earlier Cancel call is rejected with
        /// Bad_RequestCancelledByClient and completed even when one of its cancellation
        /// callbacks fails, so it does not stay registered and block lifecycle drains
        /// (OPC 10000-4 5.7.5.2).
        /// </summary>
        [Test]
        public async Task PendingCancelAdmissionCompletesWhenACancellationCallbackFailsAsync()
        {
            var fixture = new ServerFixture<StandardServer>(t => new StandardServer(t));
            StandardServer server = await fixture.StartAsync().ConfigureAwait(false);
            try
            {
                (RequestHeader requestHeader, SecureChannelContext secureChannelContext) =
                    await server.CreateAndActivateSessionAsync("PendingCancelFails").ConfigureAwait(false);
                var serverInternal = (ServerInternalData)server.CurrentInstance;
                DateTime sentAt = DateTime.UtcNow;

                await server.CancelAsync(
                    secureChannelContext,
                    new RequestHeader
                    {
                        AuthenticationToken = requestHeader.AuthenticationToken,
                        Timestamp = sentAt
                    },
                    9,
                    RequestLifetime.None).ConfigureAwait(false);

                using var lifetime = new RequestLifetime();
                using CancellationTokenRegistration registration = lifetime.CancellationToken.Register(
                    () => throw new InvalidOperationException("cancellation callback"));

                // the Read was sent before the Cancel and was still queued when it ran.
                ServiceResultException ex = (await Assert.ThrowsAsync<ServiceResultException>(
                    async () => await server.ReadAsync(
                        secureChannelContext,
                        new RequestHeader
                        {
                            AuthenticationToken = requestHeader.AuthenticationToken,
                            RequestHandle = 9,
                            Timestamp = sentAt.AddSeconds(-1)
                        },
                        0,
                        TimestampsToReturn.Neither,
                        [new ReadValueId { NodeId = VariableIds.Server_ServerStatus_State, AttributeId = Attributes.Value }],
                        lifetime).ConfigureAwait(false)))!;

                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
                Assert.That(lifetime.StatusCode, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));

                Assert.That(serverInternal.RequestManager.TrackedRequestCount, Is.Zero,
                    "The rejected request must not stay registered.");
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Closing a Session aborts an in-flight ActivateSession of that Session with
        /// Bad_SessionClosed, so the close does not wait on an authenticator that waits for
        /// the request to be cancelled (OPC 10000-4 5.7.2.1).
        /// </summary>
        [Test]
        public async Task CloseSessionAbortsAnInFlightActivateSessionAsync()
        {
            var fixture = new ServerFixture<AdmissionServer>(t => new AdmissionServer(t));
            AdmissionServer server = await fixture.StartAsync().ConfigureAwait(false);
            using var lifetime = new RequestLifetime();
            try
            {
                (RequestHeader requestHeader, SecureChannelContext secureChannelContext) =
                    await server.CreateAndActivateSessionAsync("ActivationClose").ConfigureAwait(false);
                var serverInternal = (ServerInternalData)server.CurrentInstance;
                var sessionManager = (ClosingWindowSessionManager)serverInternal.SessionManager;
                ISession? session = sessionManager.GetSession(requestHeader.AuthenticationToken);
                Assert.That(session, Is.Not.Null);

                sessionManager.BlockAuthentication = true;
                Task<ActivateSessionResponse> activation = server.ActivateSessionAsync(
                    secureChannelContext,
                    new RequestHeader { AuthenticationToken = requestHeader.AuthenticationToken },
                    null,
                    [],
                    [],
                    default,
                    null,
                    lifetime).AsTask();
                Task entered = sessionManager.AuthenticationEntered.Task;
                Assert.That(
                    await Task.WhenAny(entered, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false),
                    Is.SameAs(entered),
                    "The activation must reach the authenticator.");

                Task close = serverInternal.CloseSessionAsync(null!, session!.Id, true).AsTask();
                Assert.That(
                    await Task.WhenAny(close, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false),
                    Is.SameAs(close),
                    "The close must not wait for the in-flight activation.");
                await close.ConfigureAwait(false);

                Assert.That(lifetime.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
                Assert.That(() => activation, Throws.Exception);
                Assert.That(sessionManager.GetSession(requestHeader.AuthenticationToken), Is.Null);
            }
            finally
            {
                // releases a blocked authenticator when the close did not abort it.
                lifetime.TryCancel(StatusCodes.BadShutdown);
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A CreateSession rejected by request validation is one rejected session
        /// establishment request, counted once in each counter (OPC 10000-5 12.9).
        /// </summary>
        [Test]
        public async Task CreateSessionRejectedDuringValidationIsCountedOnceAsync()
        {
            var fixture = new ServerFixture<AdmissionServer>(t => new AdmissionServer(t));
            AdmissionServer server = await fixture.StartAsync().ConfigureAwait(false);
            try
            {
                var serverInternal = (ServerInternalData)server.CurrentInstance;
                ServerDiagnosticsSummaryDataType diagnostics = serverInternal.ServerDiagnostics;
                uint rejectedRequestsBefore = diagnostics.RejectedRequestsCount;
                uint rejectedSessionsBefore = diagnostics.RejectedSessionCount;
                uint securityRejectedRequestsBefore = diagnostics.SecurityRejectedRequestsCount;
                uint securityRejectedSessionsBefore = diagnostics.SecurityRejectedSessionCount;

                server.RejectCreateSession = true;
                ServiceResultException ex = (await Assert.ThrowsAsync<ServiceResultException>(
                    async () => await server.CreateAndActivateSessionAsync("RejectedCreate").ConfigureAwait(false)))!;

                Assert.That(ex.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(diagnostics.RejectedSessionCount, Is.EqualTo(rejectedSessionsBefore + 1));
                Assert.That(diagnostics.RejectedRequestsCount, Is.EqualTo(rejectedRequestsBefore + 1));
                Assert.That(
                    diagnostics.SecurityRejectedRequestsCount - securityRejectedRequestsBefore,
                    Is.EqualTo(diagnostics.SecurityRejectedSessionCount - securityRejectedSessionsBefore));
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// The routing view is pinned before application admission and released when the request completes.
        /// </summary>
        [Test]
        public async Task RequestRoutingIsPinnedBeforeApplicationAdmissionAndReleasedAfterCompletionAsync()
        {
            var fixture = new ServerFixture<AdmissionServer>(t => new AdmissionServer(t));
            try
            {
                AdmissionServer server = await fixture.StartAsync().ConfigureAwait(false);
                (RequestHeader requestHeader, SecureChannelContext channel) =
                    await server.CreateAndActivateSessionAsync("PinnedRouting").ConfigureAwait(false);
                await server.ReadAsync(
                    channel,
                    new RequestHeader { AuthenticationToken = requestHeader.AuthenticationToken },
                    0,
                    TimestampsToReturn.Neither,
                    [
                        new ReadValueId
                        {
                            NodeId = VariableIds.Server_ServerStatus_State,
                            AttributeId = Attributes.Value
                        }
                    ],
                    RequestLifetime.None).ConfigureAwait(false);

                using (Assert.EnterMultipleScope())
                {
                    Assert.That(server.AdmittedReadView, Is.Not.Null,
                        "Application admission must see the request's explicit routing view.");
                    Assert.That(server.AdmittedReadView!.VisibleNodeManagers, Has.Length.GreaterThanOrEqualTo(2));
                    Assert.That(server.AdmittedReadContext, Is.Not.Null);
                    Assert.That(server.AdmittedReadContext!.RoutingSnapshot, Is.Null,
                        "Completion must release the request's view.");
                }
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A parked Publish retains its request admission without retaining a NodeManager routing epoch.
        /// </summary>
        [Test]
        public async Task ParkedPublishDoesNotDelayNodeManagerRetirementAsync()
        {
            const string namespaceUri = "urn:opcfoundation:tests:parked-publish";
            var fixture = new ServerFixture<AdmissionServer>(telemetry => new AdmissionServer(telemetry));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            var parked = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var parkSink = new Mock<IRequestParkSink>();
            parkSink.Setup(value => value.NotifyParked()).Callback(() => parked.TrySetResult(true));
            using var lifetime = new RequestLifetime { ParkSink = parkSink.Object };
            Task<PublishResponse>? publish = null;
            NodeManagerBatchResult? removal = null;
            try
            {
                AdmissionServer server = await fixture.StartAsync().ConfigureAwait(false);
                var manager = new Mock<IAsyncNodeManager>();
                manager.SetupGet(value => value.NamespaceUris).Returns([namespaceUri]);
                var factory = new Mock<IAsyncNodeManagerFactory>();
                factory.SetupGet(value => value.NamespacesUris).Returns([namespaceUri]);
                factory.Setup(value => value.CreateAsync(
                    It.IsAny<IServerInternal>(), It.IsAny<ApplicationConfiguration>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(manager.Object);
                NodeManagerRegistration registration;
                await using (IPreparedNodeManagerBatch addition = await server.NodeManagerLifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Add(factory.Object)], timeout.Token).ConfigureAwait(false))
                {
                    NodeManagerBatchResult added = await addition.CommitAsync(static _ => default, timeout.Token)
                        .ConfigureAwait(false);
                    Assert.That(added.CleanupFailure, Is.Null);
                    registration = added.Registrations[0];
                }
                (RequestHeader header, SecureChannelContext channel) =
                    await server.CreateAndActivateSessionAsync("ParkedPublishRouting").ConfigureAwait(false);
                var services = new ServerTestServices(server, channel);
                CreateSubscriptionResponse subscription = await services.CreateSubscriptionAsync(
                    header, 60_000, 1_000, 100, 0, false, 0).ConfigureAwait(false);
                Assert.That(subscription.RevisedPublishingInterval, Is.GreaterThanOrEqualTo(30_000));
                publish = server.PublishAsync(channel, header, [], lifetime).AsTask();
                await parked.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                Assert.That(publish.IsCompleted, Is.False);
                Assert.That(server.CurrentInstance.RequestManager.TrackedRequestCount, Is.EqualTo(1));

                await using (IPreparedNodeManagerBatch prepared = await server.NodeManagerLifecycle.PrepareAsync(
                    [NodeManagerBatchChange.Remove(registration)], timeout.Token).ConfigureAwait(false))
                {
                    removal = await prepared.CommitAsync(static _ => default, timeout.Token).ConfigureAwait(false);
                }
                Assert.That(removal.CleanupFailure, Is.Null);
                await removal.Retirement.WaitAsync(TimeSpan.FromSeconds(10), timeout.Token).ConfigureAwait(false);

                Assert.That(publish.IsCompleted, Is.False,
                    "Physical retirement must complete while the Publish request is still parked.");
                Assert.That(server.AdmittedPublishContext, Is.Not.Null);
                Assert.That(server.AdmittedPublishContext!.RoutingSnapshot, Is.Null);
                Assert.That(server.CurrentInstance.RequestManager.TrackedRequestCount, Is.EqualTo(1));
                manager.Verify(value => value.DeleteAddressSpaceAsync(It.IsAny<CancellationToken>()), Times.Once);
            }
            finally
            {
                lifetime.TryCancel(StatusCodes.BadRequestCancelledByClient);
                try
                {
                    if (publish is not null)
                    {
                        await Assert.ThatAsync(() => publish.WaitAsync(TimeSpan.FromSeconds(10)),
                            Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
                        Assert.That(lifetime.StatusCode.Code, Is.EqualTo(StatusCodes.BadRequestCancelledByClient));
                    }
                    if (removal is not null)
                    {
                        await removal.Retirement.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                    }
                }
                finally
                {
                    await fixture.StopAsync().ConfigureAwait(false);
                }
            }
        }

        /// <summary>
        /// A server whose session manager can start a close inside the admission window.
        /// </summary>
        public sealed class AdmissionServer : StandardServer
        {
            public AdmissionServer(ITelemetryContext telemetry)
                : base(telemetry)
            {
            }

            public bool RejectCreateSession { get; set; }

            /// <summary>
            /// Gets the Read operation observed at application admission.
            /// </summary>
            internal OperationContext? AdmittedReadContext { get; private set; }

            /// <summary>
            /// Gets the routing view observed before application admission runs.
            /// </summary>
            internal NodeManagerRoutingTable.RoutingSnapshot? AdmittedReadView { get; private set; }

            /// <summary>
            /// Gets the Publish operation observed at application admission.
            /// </summary>
            internal OperationContext? AdmittedPublishContext { get; private set; }

            protected override ISessionManager CreateSessionManager(
                IServerInternal server,
                ApplicationConfiguration configuration)
            {
                return new ClosingWindowSessionManager(server, configuration);
            }

            protected override ValueTask OnRequestValidatedAsync(OperationContext context)
            {
                if (context.RequestType == RequestType.Read)
                {
                    AdmittedReadContext = context;
                    AdmittedReadView = context.RoutingSnapshot;
                }
                if (context.RequestType == RequestType.Publish)
                {
                    AdmittedPublishContext = context;
                }
                if (RejectCreateSession && context.RequestType == RequestType.CreateSession)
                {
                    throw new ServiceResultException(StatusCodes.BadUserAccessDenied);
                }

                return base.OnRequestValidatedAsync(context);
            }
        }

        /// <summary>
        /// Marks the Session closing after it admitted a request, which is where a
        /// concurrent close lands in the race.
        /// </summary>
        private sealed class ClosingWindowSessionManager : SessionManager
        {
            public ClosingWindowSessionManager(IServerInternal server, ApplicationConfiguration configuration)
                : base(server, configuration)
            {
            }

            public bool MarkClosingAfterAdmission { get; set; }

            public bool BlockAuthentication { get; set; }

            public TaskCompletionSource<bool> AuthenticationEntered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            protected override async ValueTask<(
                IUserIdentity? Identity,
                IUserIdentity? EffectiveIdentity,
                ServiceResult? Error)> AuthenticateUserIdentityAsync(
                    ISession session,
                    IUserIdentityTokenHandler newIdentity,
                    UserTokenPolicy? userTokenPolicy,
                    EndpointDescription endpointDescription,
                    CancellationToken cancellationToken)
            {
                if (BlockAuthentication)
                {
                    // an authenticator that only finishes when the request is cancelled.
                    AuthenticationEntered.TrySetResult(true);
                    await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                }

                return await base.AuthenticateUserIdentityAsync(
                    session,
                    newIdentity,
                    userTokenPolicy,
                    endpointDescription,
                    cancellationToken).ConfigureAwait(false);
            }

            protected override void ReevaluateIdentityIfStale(
                ISession session,
                SecureChannelContext secureChannelContext)
            {
                base.ReevaluateIdentityIfStale(session, secureChannelContext);
                if (MarkClosingAfterAdmission)
                {
                    MarkClosingAfterAdmission = false;
                    ((ServerSession)session).MarkClosing();
                }
            }
        }
    }
}
