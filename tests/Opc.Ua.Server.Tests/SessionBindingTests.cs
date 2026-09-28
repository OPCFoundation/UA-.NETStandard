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

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    [TestFixture]
    [Category("Session")]
    public sealed class SessionBindingTests
    {
        [SetUp]
        public void SetUp()
        {
            m_clock = new CallbackTimeProvider();
            m_certificate = CertificateBuilder.Create("CN=SessionBindingTests")
                .SetRSAKeySize(2048).CreateForRSA();
            var server = new Mock<IServerInternal>();
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            server.Setup(s => s.Telemetry).Returns(telemetry);
            server.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            server.Setup(s => s.MessageContext).Returns(ServiceMessageContext.CreateEmpty(telemetry));
            server.Setup(s => s.DefaultSystemContext).Returns(new ServerSystemContext(server.Object));
            m_diagnostics = new Mock<IDiagnosticsNodeManager>();
            uint nextSessionId = 0;
            m_diagnostics.Setup(d => d.CreateSessionDiagnosticsAsync(
                    It.IsAny<ServerSystemContext>(),
                    It.IsAny<SessionDiagnosticsDataType>(),
                    It.IsAny<NodeValueSimpleEventHandler>(),
                    It.IsAny<SessionSecurityDiagnosticsDataType>(),
                    It.IsAny<NodeValueSimpleEventHandler>(),
                    It.IsAny<CancellationToken>()))
                .Returns(() => new ValueTask<NodeId>(new NodeId(++nextSessionId, 1)));
            server.Setup(s => s.DiagnosticsNodeManager).Returns(m_diagnostics.Object);
            m_manager = new BindingSessionManager(server.Object, m_certificate, m_clock);
            server.Setup(s => s.SessionManager).Returns(m_manager);
            server.Setup(s => s.CloseSessionAsync(
                    It.IsAny<OperationContext>(), It.IsAny<NodeId>(), It.IsAny<bool>(),
                    It.IsAny<CancellationToken>()))
                .Returns((OperationContext _, NodeId id, bool _, CancellationToken ct) =>
                    m_manager.CloseSessionAsync(id, ct));
        }

        [TearDown]
        public void TearDown()
        {
            foreach (OperationContext context in m_contexts)
            {
                context.Dispose();
            }
            m_contexts.Clear();
            m_manager.Dispose();
            m_certificate.Dispose();
        }

        [Test]
        public async Task RepeatedActivationIsDistinctWithoutActivationEventAsync()
        {
            OperationContext channel = CreateContext("one");
            CreateSessionResult created = await CreateAsync(channel).ConfigureAwait(false);
            Assert.That(m_manager.HasSession("one"), Is.False);
            int events = 0;
            m_manager.SessionActivated += (_, _) => events++;
            await ActivateAsync(created, channel).ConfigureAwait(false);
            SessionBindingContext first = GetBinding(created, channel);
            int firstEvents = events;
            await ActivateAsync(created, channel).ConfigureAwait(false);
            SessionBindingContext second = GetBinding(created, channel);

            Assert.That(events, Is.EqualTo(firstEvents), "Same identity and locales do not raise the event.");
            Assert.That(second.ActivationSequence, Is.EqualTo(first.ActivationSequence + 1));
            Assert.That(first.ActivationSequence, Is.EqualTo(1));
            Assert.That(first.UserTokenType, Is.EqualTo(UserTokenType.Anonymous));
            Assert.That(first.ClientUserId, Is.Null);
            Assert.That(m_manager.HasSession("one"), Is.True);
            await m_manager.CloseSessionAsync(created.SessionId).ConfigureAwait(false);
            Assert.That(m_manager.HasSession("one"), Is.False, "One close must remove repeated activations.");
            AssertNoBinding(created.AuthenticationToken, channel);
        }

        [Test]
        public async Task TransferPreservesOtherSessionsAndInvalidatesOldContextAsync()
        {
            OperationContext oldChannel = CreateContext("old");
            OperationContext newChannel = CreateContext("new");
            CreateSessionResult first = await CreateAsync(oldChannel).ConfigureAwait(false);
            CreateSessionResult second = await CreateAsync(oldChannel).ConfigureAwait(false);
            await ActivateAsync(first, oldChannel).ConfigureAwait(false);
            await ActivateAsync(second, oldChannel).ConfigureAwait(false);
            SessionBindingContext original = GetBinding(first, oldChannel);

            await ActivateAsync(first, newChannel).ConfigureAwait(false);
            SessionBindingContext transferred = GetBinding(first, newChannel);
            AssertNoBinding(first.AuthenticationToken, oldChannel);
            Assert.That(transferred.SessionId, Is.EqualTo(first.SessionId));
            Assert.That(transferred.ActivationSequence, Is.EqualTo(original.ActivationSequence + 1));
            Assert.That(original.SecureChannelId, Is.EqualTo("old"), "Snapshots must not change on transfer.");
            Assert.That(GetBinding(second, oldChannel).SessionId, Is.EqualTo(second.SessionId));
            Assert.That(m_manager.HasSession("old"), Is.True);
            Assert.That(m_manager.HasSession("new"), Is.True);

            await m_manager.CloseSessionAsync(second.SessionId).ConfigureAwait(false);
            Assert.That(m_manager.HasSession("old"), Is.False);
            Assert.That(m_manager.HasSession("new"), Is.True);
            await m_manager.CloseSessionAsync(first.SessionId).ConfigureAwait(false);
            await m_manager.CloseSessionAsync(first.SessionId).ConfigureAwait(false);
            Assert.That(m_manager.HasSession("new"), Is.False);
        }

        [Test]
        public async Task FailedActivationDoesNotPublishOrMoveMembershipAsync()
        {
            OperationContext oldChannel = CreateContext("old");
            OperationContext newChannel = CreateContext("new");
            CreateSessionResult created = await CreateAsync(oldChannel).ConfigureAwait(false);
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                () => ActivateAsync(created, newChannel))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadSecureChannelIdInvalid));
            Assert.That(m_manager.HasSession("old"), Is.False);
            Assert.That(m_manager.HasSession("new"), Is.False);
            AssertNoBinding(created.AuthenticationToken, oldChannel);

            await ActivateAsync(created, oldChannel).ConfigureAwait(false);
            SessionBindingContext original = GetBinding(created, oldChannel);
            m_manager.RejectAuthentication = true;
            error = Assert.ThrowsAsync<ServiceResultException>(() => ActivateAsync(created, newChannel))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(GetBinding(created, oldChannel), Is.SameAs(original));
            Assert.That(m_manager.HasSession("old"), Is.True);
            Assert.That(m_manager.HasSession("new"), Is.False);
        }

        [Test]
        public async Task ReadOnlyLookupValidatesTokenChannelAndSecurityWithoutRefreshingActivityAsync()
        {
            OperationContext channel = CreateContext("one");
            CreateSessionResult created = await CreateAsync(channel).ConfigureAwait(false);
            AssertNoBinding(created.AuthenticationToken, channel);
            AssertNoBinding(NodeId.Null, channel);
            AssertNoBinding(new NodeId("unknown", 1), channel);
            await ActivateAsync(created, channel).ConfigureAwait(false);
            SessionBindingContext binding = GetBinding(created, channel);
            long contact = created.Session.LastContactTickCount;
            uint requestCount = created.Session.ReadDiagnostics(d => d.TotalRequestCount.TotalCount);
            m_clock.Advance(TimeSpan.FromSeconds(1));

            OperationContext otherChannel = CreateContext("other");
            OperationContext otherMode = CreateContext("one", MessageSecurityMode.Sign);
            OperationContext otherPolicy = CreateContext("one", policy: SecurityPolicies.Basic256Sha256);
            OperationContext otherCertificate = CreateContext("one", certificate: [1, 2, 3]);
            AssertNoBinding(created.AuthenticationToken, otherChannel);
            AssertNoBinding(created.AuthenticationToken, otherMode);
            AssertNoBinding(created.AuthenticationToken, otherPolicy);
            Assert.That(GetBinding(created, otherCertificate), Is.SameAs(binding));
            Assert.That(m_manager.TryGetSessionContext(
                created.AuthenticationToken, new SecureChannelContext("one", null, RequestEncoding.Binary),
                out SessionBindingContext? missingEndpoint), Is.False);
            Assert.That(missingEndpoint, Is.Null);
            Assert.That(GetBinding(created, channel), Is.SameAs(binding));
            Assert.That(created.Session.LastContactTickCount, Is.EqualTo(contact));
            Assert.That(created.Session.ReadDiagnostics(d => d.TotalRequestCount.TotalCount), Is.EqualTo(requestCount));
        }

        [TestCase("opc.tcp://localhost:4840/binding")]
        [TestCase("opc.https://localhost:4843/binding")]
        public async Task NonePolicyLookupIgnoresOptionalChannelCertificateAsync(string endpointUrl)
        {
            OperationContext channel = CreateContext("shared", certificate: [1, 2], endpointUrl: endpointUrl);
            CreateSessionResult created = await CreateAsync(channel).ConfigureAwait(false);
            await ActivateAsync(created, channel).ConfigureAwait(false);
            SessionBindingContext original = GetBinding(created, channel);
            byte[]?[] certificates = [null, [], [3, 4]];
            foreach (byte[]? certificate in certificates)
            {
                OperationContext request = CreateContext(
                    "shared", certificate: certificate, endpointUrl: endpointUrl);
                Assert.That(GetBinding(created, request), Is.SameAs(original));
                using OperationContext validated = await m_manager.ValidateRequestAsync(
                    new RequestHeader { AuthenticationToken = created.AuthenticationToken },
                    request.ChannelContext!, RequestType.Read, RequestLifetime.None).ConfigureAwait(false);
                Assert.That(validated.Session, Is.SameAs(created.Session));
            }
            Assert.That(original.UserTokenType, Is.EqualTo(UserTokenType.Anonymous));
            Assert.That(original.ClientUserId, Is.Null);
        }

        [Test]
        public async Task ReadOnlyLookupDoesNotHoldIndexLockWhileProbingSessionAsync()
        {
            OperationContext channel = CreateContext("one");
            CreateSessionResult created = await CreateAsync(channel).ConfigureAwait(false);
            await ActivateAsync(created, channel).ConfigureAwait(false);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var release = new ManualResetEventSlim();
            m_clock.OnNextTimestamp(() =>
            {
                entered.SetResult(true);
                release.Wait();
            });
            Task<bool> lookup = Task.Run(() => m_manager.TryGetSessionContext(
                created.AuthenticationToken, channel.ChannelContext!, out _));
            Task<bool>? membership = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                membership = Task.Run(() => m_manager.HasSession("one"));
                Assert.That(await membership.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false), Is.True);
            }
            finally
            {
                release.Set();
                await lookup.ConfigureAwait(false);
                if (membership != null)
                {
                    await membership.ConfigureAwait(false);
                }
            }
            Assert.That(await lookup.ConfigureAwait(false), Is.True);
        }

        [Test]
        public async Task ReadOnlyLookupRechecksMembershipAfterProbingSessionAsync()
        {
            OperationContext channel = CreateContext("one");
            CreateSessionResult created = await CreateAsync(channel).ConfigureAwait(false);
            await ActivateAsync(created, channel).ConfigureAwait(false);
            Task close = Task.CompletedTask;
            m_clock.OnNextTimestamp(() => close = m_manager.CloseSessionAsync(created.SessionId).AsTask());

            bool classified = m_manager.TryGetSessionContext(
                created.AuthenticationToken, channel.ChannelContext!, out SessionBindingContext? binding);
            await close.ConfigureAwait(false);
            Assert.That(classified, Is.False);
            Assert.That(binding, Is.Null);
            Assert.That(m_manager.HasSession("one"), Is.False);
        }

        [Test]
        public async Task ReadOnlyLookupRejectsVersionChangedDuringSessionProbeAsync()
        {
            OperationContext channel = CreateContext("one");
            CreateSessionResult created = await CreateAsync(channel).ConfigureAwait(false);
            await ActivateAsync(created, channel).ConfigureAwait(false);
            SessionBindingContext original = GetBinding(created, channel);
            Task activation = Task.CompletedTask;
            m_clock.OnNextTimestamp(() => activation = ActivateAsync(created, channel));

            bool classified = m_manager.TryGetSessionContext(
                created.AuthenticationToken, channel.ChannelContext!, out SessionBindingContext? binding);
            await activation.ConfigureAwait(false);
            Assert.That(classified, Is.False);
            Assert.That(binding, Is.Null);
            Assert.That(GetBinding(created, channel).ActivationSequence, Is.EqualTo(original.ActivationSequence + 1));
            Assert.That(m_manager.HasSession("one"), Is.True);
        }

        [Test]
        public async Task ExpiredSessionLosesClassificationAndTimeoutRemovesMembershipAsync()
        {
            OperationContext channel = CreateContext("one");
            CreateSessionResult created = await CreateAsync(channel).ConfigureAwait(false);
            await ActivateAsync(created, channel).ConfigureAwait(false);
            m_clock.Advance(TimeSpan.FromMilliseconds(60_001));
            AssertNoBinding(created.AuthenticationToken, channel);
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                () => ActivateAsync(created, channel))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
            Assert.That(m_manager.HasSession("one"), Is.False);
            Assert.That(m_manager.GetSession(created.AuthenticationToken), Is.Null);
        }

        [Test]
        public async Task AdministrativeCloseRemovesBindingBeforeCallbacksEvenWhenCleanupFailsAsync()
        {
            OperationContext channel = CreateContext("one");
            CreateSessionResult created = await CreateAsync(channel).ConfigureAwait(false);
            await ActivateAsync(created, channel).ConfigureAwait(false);
            bool? membershipAtClose = null;
            m_manager.SessionClosing += (_, _) => membershipAtClose = m_manager.HasSession("one");
            m_diagnostics.Setup(d => d.DeleteSessionDiagnosticsAsync(
                    It.IsAny<ServerSystemContext>(), It.IsAny<NodeId>(), It.IsAny<CancellationToken>()))
                .Throws(new InvalidOperationException("Injected diagnostics cleanup failure."));

            Assert.ThrowsAsync<InvalidOperationException>(
                async () => await m_manager.CloseSessionAsync(created.SessionId).ConfigureAwait(false));
            Assert.That(membershipAtClose, Is.False);
            Assert.That(m_manager.HasSession("one"), Is.False);
            AssertNoBinding(created.AuthenticationToken, channel);
        }

        [Test]
        public async Task DelayedActivationCallbackCannotOverwriteTransferOrResurrectClosedSessionAsync()
        {
            OperationContext oldChannel = CreateContext("old");
            OperationContext newChannel = CreateContext("new");
            CreateSessionResult created = await CreateAsync(oldChannel).ConfigureAwait(false);
            m_manager.DelayNextCallback();
            Task first = ActivateAsync(created, oldChannel);
            await m_manager.CallbackEntered.Task.ConfigureAwait(false);
            try
            {
                Assert.That(GetBinding(created, oldChannel).ActivationSequence, Is.EqualTo(1));
                await ActivateAsync(created, newChannel).ConfigureAwait(false);
                Assert.That(m_manager.HasSession("old"), Is.False);
                Assert.That(GetBinding(created, newChannel).ActivationSequence, Is.EqualTo(2));
                await m_manager.CloseSessionAsync(created.SessionId).ConfigureAwait(false);
                Assert.That(m_manager.HasSession("new"), Is.False);
            }
            finally
            {
                m_manager.ReleaseCallback.TrySetResult(true);
                await first.ConfigureAwait(false);
            }
            Assert.That(m_manager.LastCompletedCallbackSequence, Is.EqualTo(1));
            Assert.That(m_manager.HasSession("old"), Is.False);
            Assert.That(m_manager.HasSession("new"), Is.False);
            AssertNoBinding(created.AuthenticationToken, newChannel);
        }

        [Test]
        public async Task CloseRacingTransferRemovesOnlyCommittedBindingAsync()
        {
            OperationContext oldChannel = CreateContext("old");
            OperationContext newChannel = CreateContext("new");
            CreateSessionResult created = await CreateAsync(oldChannel).ConfigureAwait(false);
            await ActivateAsync(created, oldChannel).ConfigureAwait(false);
            m_manager.DelayNextAuthentication();
            Task transfer = ActivateAsync(created, newChannel);
            await m_manager.AuthenticationEntered.Task.ConfigureAwait(false);
            Task close = m_manager.CloseSessionAsync(created.SessionId).AsTask();
            try
            {
                Assert.That(close.IsCompleted, Is.False);
                Assert.That(GetBinding(created, oldChannel).ActivationSequence, Is.EqualTo(1));
                Assert.That(m_manager.HasSession("new"), Is.False);
            }
            finally
            {
                m_manager.ReleaseAuthentication.TrySetResult(true);
                await Task.WhenAll(transfer, close).ConfigureAwait(false);
            }
            Assert.That(m_manager.HasSession("old"), Is.False);
            Assert.That(m_manager.HasSession("new"), Is.False);
            AssertNoBinding(created.AuthenticationToken, newChannel);
        }

        [Test]
        public async Task ShutdownDuringActivationCannotPublishLateMembershipAsync()
        {
            OperationContext channel = CreateContext("one");
            CreateSessionResult created = await CreateAsync(channel).ConfigureAwait(false);
            await ActivateAsync(created, channel).ConfigureAwait(false);
            m_manager.DelayNextAuthentication();
            Task activation = ActivateAsync(created, channel);
            await m_manager.AuthenticationEntered.Task.ConfigureAwait(false);
            await m_manager.ShutdownAsync().ConfigureAwait(false);
            Assert.That(m_manager.HasSession("one"), Is.False);
            m_manager.ReleaseAuthentication.TrySetResult(true);
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(() => activation)!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadSessionClosed));
            Assert.That(m_manager.HasSession("one"), Is.False);
            AssertNoBinding(created.AuthenticationToken, channel);
        }

        [Test]
        public async Task RestoredSessionGainsMembershipOnlyAfterSuccessfulActivationAsync()
        {
            OperationContext channel = CreateContext("restored");
            var authenticationToken = new NodeId("restored-token", 1);
            AssertNoBinding(authenticationToken, channel);
            m_manager.RestoreEnabled = true;
            m_manager.RejectAuthentication = true;
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(
                () => ActivateTokenAsync(authenticationToken, channel))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(m_manager.HasSession("restored"), Is.False);
            AssertNoBinding(authenticationToken, channel);
            m_manager.RejectAuthentication = false;
            await ActivateTokenAsync(authenticationToken, channel).ConfigureAwait(false);
            Assert.That(m_manager.TryGetSessionContext(authenticationToken, channel.ChannelContext!, out var binding),
                Is.True);
            Assert.That(binding!.ActivationSequence, Is.EqualTo(1));
            Assert.That(m_manager.HasSession("restored"), Is.True);
            await m_manager.CloseSessionAsync(binding.SessionId).ConfigureAwait(false);
            Assert.That(m_manager.HasSession("restored"), Is.False);
            AssertNoBinding(authenticationToken, channel);
        }

        [Test]
        public async Task IdentityChangePublishesNewVersionWithoutMutatingExistingSnapshotAsync()
        {
            OperationContext channel = CreateContext("one");
            CreateSessionResult created = await CreateAsync(channel).ConfigureAwait(false);
            await ActivateUserAsync("alice").ConfigureAwait(false);
            SessionBindingContext alice = GetBinding(created, channel);
            await ActivateUserAsync("bob").ConfigureAwait(false);
            SessionBindingContext bob = GetBinding(created, channel);
            Assert.That(alice.UserTokenType, Is.EqualTo(UserTokenType.UserName));
            Assert.That(alice.ClientUserId, Is.EqualTo("1:-1:alice"));
            Assert.That(bob.UserTokenType, Is.EqualTo(UserTokenType.UserName));
            Assert.That(bob.ClientUserId, Is.EqualTo("1:-1:bob"));
            Assert.That(bob.ActivationSequence, Is.EqualTo(alice.ActivationSequence + 1));
            await m_manager.CloseSessionAsync(created.SessionId).ConfigureAwait(false);
            Assert.That(m_manager.HasSession("one"), Is.False);

            async Task ActivateUserAsync(string userName)
            {
                await m_manager.ActivateSessionAsync(
                    channel, created.AuthenticationToken, null,
                    new ExtensionObject(new UserNameIdentityToken
                    {
                        PolicyId = "username",
                        UserName = userName,
                        Password = ByteString.From([1])
                    }),
                    null, []).ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ClosingSessionCannotBeClassifiedAsync()
        {
            OperationContext channel = CreateContext("one");
            CreateSessionResult created = await CreateAsync(channel).ConfigureAwait(false);
            await ActivateAsync(created, channel).ConfigureAwait(false);
            Assert.That(((Server.Session)created.Session).MarkClosing(), Is.True);
            AssertNoBinding(created.AuthenticationToken, channel);
            await m_manager.CloseSessionAsync(created.SessionId).ConfigureAwait(false);
            Assert.That(m_manager.HasSession("one"), Is.False);
        }

        [Test]
        public async Task RestorationCompletingAfterShutdownCannotAdmitSessionAsync()
        {
            OperationContext channel = CreateContext("restored");
            var token = new NodeId("late-restore", 1);
            m_manager.RestoreEnabled = true;
            m_manager.DelayRestore = true;
            Task activation = ActivateTokenAsync(token, channel);
            await m_manager.RestoreEntered.Task.ConfigureAwait(false);
            try
            {
                await m_manager.ShutdownAsync().ConfigureAwait(false);
            }
            finally
            {
                m_manager.ReleaseRestore.TrySetResult(true);
            }
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(() => activation)!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadServerHalted));
            Assert.That(m_manager.GetSession(token), Is.Null);
            Assert.That(m_manager.HasSession("restored"), Is.False);
            AssertNoBinding(token, channel);
        }

        private async Task<CreateSessionResult> CreateAsync(OperationContext context)
        {
            return await m_manager.CreateSessionAsync(
                context, m_certificate, "binding", default,
                new ApplicationDescription { ApplicationUri = "urn:binding-test" },
                context.ChannelContext!.EndpointDescription!.EndpointUrl,
                null, [], 60_000, 64 * 1024).ConfigureAwait(false);
        }

        private Task ActivateAsync(CreateSessionResult created, OperationContext context)
        {
            return ActivateTokenAsync(created.AuthenticationToken, context);
        }

        private async Task ActivateTokenAsync(NodeId token, OperationContext context)
        {
            await m_manager.ActivateSessionAsync(
                context, token, null, default, null, []).ConfigureAwait(false);
        }

        private SessionBindingContext GetBinding(CreateSessionResult created, OperationContext channel)
        {
            Assert.That(m_manager.TryGetSessionContext(
                created.AuthenticationToken, channel.ChannelContext!, out SessionBindingContext? binding), Is.True);
            Assert.That(binding, Is.Not.Null);
            return binding!;
        }

        private void AssertNoBinding(NodeId token, OperationContext channel)
        {
            Assert.That(m_manager.TryGetSessionContext(
                token, channel.ChannelContext!, out SessionBindingContext? binding), Is.False);
            Assert.That(binding, Is.Null);
        }

        private OperationContext CreateContext(
            string channelId,
            MessageSecurityMode mode = MessageSecurityMode.None,
            string policy = SecurityPolicies.None,
            byte[]? certificate = null,
            string endpointUrl = "opc.tcp://localhost:4840/binding")
        {
            var endpoint = new EndpointDescription
            {
                EndpointUrl = endpointUrl,
                SecurityMode = mode,
                SecurityPolicyUri = policy,
                UserIdentityTokens =
                [
                    new UserTokenPolicy
                    {
                        PolicyId = "anonymous",
                        TokenType = UserTokenType.Anonymous,
                        SecurityPolicyUri = SecurityPolicies.None
                    },
                    new UserTokenPolicy
                    {
                        PolicyId = "username",
                        TokenType = UserTokenType.UserName,
                        SecurityPolicyUri = SecurityPolicies.None
                    }
                ]
            };
            var context = new OperationContext(
                new RequestHeader(),
                new SecureChannelContext(channelId, endpoint, RequestEncoding.Binary, certificate),
                RequestType.ActivateSession, RequestLifetime.None);
            m_contexts.Add(context);
            return context;
        }

        private readonly List<OperationContext> m_contexts = [];
        private Certificate m_certificate = null!;
        private CallbackTimeProvider m_clock = null!;
        private BindingSessionManager m_manager = null!;
        private Mock<IDiagnosticsNodeManager> m_diagnostics = null!;

        private sealed class CallbackTimeProvider : TimeProvider
        {
            public override long TimestampFrequency => m_clock.TimestampFrequency;

            public override DateTimeOffset GetUtcNow()
            {
                return m_clock.GetUtcNow();
            }

            public override long GetTimestamp()
            {
                Interlocked.Exchange(ref m_callback, null)?.Invoke();
                return m_clock.GetTimestamp();
            }

            public void Advance(TimeSpan duration)
            {
                m_clock.Advance(duration);
            }

            public void OnNextTimestamp(Action callback)
            {
                m_callback = callback ?? throw new ArgumentNullException(nameof(callback));
            }

            private readonly FakeTimeProvider m_clock = new();
            private Action? m_callback;
        }

        private sealed class BindingSessionManager : SessionManager
        {
            public BindingSessionManager(IServerInternal server, Certificate certificate, TimeProvider clock)
                : base(server, new ApplicationConfiguration
                {
                    ServerConfiguration = new ServerConfiguration
                    {
                        MinSessionTimeout = 1000,
                        MaxSessionTimeout = 60_000,
                        MaxSessionCount = 10
                    }
                }, clock)
            {
                m_server = server;
                m_certificate = certificate;
            }

            public bool RejectAuthentication { get; set; }

            public bool RestoreEnabled { get; set; }

            public bool DelayRestore { get; set; }

            public long LastCompletedCallbackSequence { get; private set; }

            public TaskCompletionSource<bool> CallbackEntered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> ReleaseCallback { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> AuthenticationEntered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> ReleaseAuthentication { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> RestoreEntered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource<bool> ReleaseRestore { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            protected override bool SupportsSessionRestore => RestoreEnabled;

            public void DelayNextCallback()
            {
                m_delayCallback = true;
            }

            public void DelayNextAuthentication()
            {
                m_delayAuthentication = true;
            }

            protected override async ValueTask<(
                IUserIdentity? Identity, IUserIdentity? EffectiveIdentity, ServiceResult? Error)>
                AuthenticateUserIdentityAsync(
                    ISession session, IUserIdentityTokenHandler newIdentity, UserTokenPolicy? userTokenPolicy,
                    EndpointDescription endpointDescription, CancellationToken cancellationToken)
            {
                if (m_delayAuthentication)
                {
                    m_delayAuthentication = false;
                    AuthenticationEntered.SetResult(true);
                    await ReleaseAuthentication.Task.ConfigureAwait(false);
                }
                if (RejectAuthentication)
                {
                    return (null, null, new ServiceResult(StatusCodes.BadUserAccessDenied));
                }
                var identity = new UserIdentity(newIdentity);
                return (identity, identity, null);
            }

            protected override async ValueTask OnSessionActivatedAsync(
                NodeId authenticationToken, ISession session, ByteString serverNonce,
                UserTokenType clientUserTokenType, string? clientUserId, long activationSequence,
                CancellationToken cancellationToken)
            {
                if (m_delayCallback)
                {
                    m_delayCallback = false;
                    CallbackEntered.SetResult(true);
                    await ReleaseCallback.Task.ConfigureAwait(false);
                }
                LastCompletedCallbackSequence = activationSequence;
            }

            protected override async ValueTask<ISession?> RestoreSessionAsync(
                NodeId authenticationToken, OperationContext context, CancellationToken cancellationToken = default)
            {
                if (DelayRestore)
                {
                    RestoreEntered.SetResult(true);
                    await ReleaseRestore.Task.ConfigureAwait(false);
                }
                ISession session = CreateSession(
                    context, m_server, m_certificate, authenticationToken, default, Nonce.CreateNonce(32),
                    "restored", new ApplicationDescription(), context.ChannelContext!.EndpointDescription!.EndpointUrl!,
                    null!, [], 60_000, 64 * 1024, 0, 0);
                await session.InitializeAsync(context, cancellationToken).ConfigureAwait(false);
                SetRestoredSessionTransferSecurityState(
                    session, default, SecurityPolicies.None, MessageSecurityMode.None, UserTokenType.Anonymous, null);
                return session;
            }

            private readonly IServerInternal m_server;
            private readonly Certificate m_certificate;
            private bool m_delayCallback;
            private bool m_delayAuthentication;
        }
    }
}
