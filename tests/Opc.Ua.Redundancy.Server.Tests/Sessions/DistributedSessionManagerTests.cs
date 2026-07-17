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

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

#nullable enable

using System;
using System.Globalization;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Unit tests for the security decision logic of
    /// <see cref="DistributedSessionManager"/>: the SecurityPolicy/Mode
    /// check and the single-use server-nonce consumption (replay defence) that
    /// gate a mirrored fast-reconnect. The full reconstruct + signature path is
    /// exercised by the two-server end-to-end test.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class DistributedSessionManagerTests
    {
        private const string PolicyA = "http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256";
        private const string PolicyB = "http://opcfoundation.org/UA/SecurityPolicy#Aes256_Sha256_RsaPss";
        private static readonly ByteString s_clientChannelCertificate =
            ByteString.From(CreateBytes(32, 0x41));

        private static readonly InMemorySharedKeyValueStore s_sessionKv = new();

        private static readonly SharedKeyValueSessionStore s_sessionStore =
            new(s_sessionKv, ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()));

        private static DistributedSessionManager CreateManager(
            ISingleUseNonceRegistry nonceRegistry,
            ISharedSessionStore? sessionStore = null,
            IServerInternal? server = null,
            Func<string, Certificate?>? serverCertificateProvider = null,
            TimeProvider? timeProvider = null)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Telemetry).Returns(telemetry);
            serverMock.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());

            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MinSessionTimeout = 1000,
                    MaxSessionTimeout = 3_600_000,
                    MaxSessionCount = 100,
                    MaxRequestAge = 60_000,
                    MaxBrowseContinuationPoints = 10,
                    MaxHistoryContinuationPoints = 10
                }
            };

            return new DistributedSessionManager(
                server ?? serverMock.Object,
                configuration,
                sessionStore ?? s_sessionStore,
                nonceRegistry,
                serverCertificateProvider ?? (_ => null),
                new DistributedSessionOptions { EnableFastReconnect = true },
                timeProvider);
        }

        private static SharedSessionEntry EntryWithNonce(byte[] nonce)
        {
            return new SharedSessionEntry
            {
                SessionId = new NodeId(1, 1),
                AuthenticationToken = new NodeId(2, 1),
                SecurityPolicyUri = PolicyA,
                SecurityMode = (int)MessageSecurityMode.SignAndEncrypt,
                ServerNonce = ByteString.From(nonce),
                SecurityStateVersion = SharedSessionEntry.CurrentSecurityStateVersion,
                OriginalClientChannelCertificate = s_clientChannelCertificate,
                ClientUserId = "Anonymous"
            };
        }

        [Test]
        public async Task AuthorizeSucceedsForMatchingPolicyAndFreshNonceAsync()
        {
            using var registryKv = new InMemorySharedKeyValueStore();
            var registry = new SharedSingleUseNonceRegistry(registryKv);
            using DistributedSessionManager manager = CreateManager(registry);
            SharedSessionEntry entry = EntryWithNonce(CreateBytes(32, 1));

            DistributedSessionManager.RestoreDecision decision = await manager.AuthorizeAndConsumeAsync(
                entry,
                PolicyA,
                MessageSecurityMode.SignAndEncrypt,
                s_clientChannelCertificate).ConfigureAwait(false);

            Assert.That(decision, Is.EqualTo(DistributedSessionManager.RestoreDecision.Authorized));
        }

        [Test]
        public async Task AuthorizeRejectsMismatchedPolicyAsync()
        {
            using var registryKv = new InMemorySharedKeyValueStore();
            var registry = new SharedSingleUseNonceRegistry(registryKv);
            using DistributedSessionManager manager = CreateManager(registry);
            SharedSessionEntry entry = EntryWithNonce(CreateBytes(32, 2));

            DistributedSessionManager.RestoreDecision decision = await manager.AuthorizeAndConsumeAsync(
                entry,
                PolicyB,
                MessageSecurityMode.SignAndEncrypt,
                s_clientChannelCertificate).ConfigureAwait(false);

            Assert.That(decision, Is.EqualTo(DistributedSessionManager.RestoreDecision.PolicyMismatch));
        }

        [Test]
        public async Task AuthorizeRejectsMismatchedSecurityModeAsync()
        {
            using var registryKv = new InMemorySharedKeyValueStore();
            var registry = new SharedSingleUseNonceRegistry(registryKv);
            using DistributedSessionManager manager = CreateManager(registry);
            SharedSessionEntry entry = EntryWithNonce(CreateBytes(32, 3));

            DistributedSessionManager.RestoreDecision decision = await manager.AuthorizeAndConsumeAsync(
                entry,
                PolicyA,
                MessageSecurityMode.Sign,
                s_clientChannelCertificate).ConfigureAwait(false);

            Assert.That(decision, Is.EqualTo(DistributedSessionManager.RestoreDecision.PolicyMismatch));
        }

        [Test]
        public async Task AuthorizeRejectsReplayedNonceAsync()
        {
            using var registryKv = new InMemorySharedKeyValueStore();
            var registry = new SharedSingleUseNonceRegistry(registryKv);
            using DistributedSessionManager manager = CreateManager(registry);
            SharedSessionEntry entry = EntryWithNonce(CreateBytes(32, 9));

            DistributedSessionManager.RestoreDecision first = await manager.AuthorizeAndConsumeAsync(
                entry,
                PolicyA,
                MessageSecurityMode.SignAndEncrypt,
                s_clientChannelCertificate).ConfigureAwait(false);
            DistributedSessionManager.RestoreDecision second = await manager.AuthorizeAndConsumeAsync(
                entry,
                PolicyA,
                MessageSecurityMode.SignAndEncrypt,
                s_clientChannelCertificate).ConfigureAwait(false);

            Assert.That(first, Is.EqualTo(DistributedSessionManager.RestoreDecision.Authorized));
            Assert.That(second, Is.EqualTo(DistributedSessionManager.RestoreDecision.NonceReplayed),
                "a captured activation cannot be replayed once the nonce is consumed");
        }

        [Test]
        public async Task AuthorizeRejectsEmptyNonceAsync()
        {
            using var registryKv = new InMemorySharedKeyValueStore();
            var registry = new SharedSingleUseNonceRegistry(registryKv);
            using DistributedSessionManager manager = CreateManager(registry);
            SharedSessionEntry entry = EntryWithNonce([]);

            DistributedSessionManager.RestoreDecision decision = await manager.AuthorizeAndConsumeAsync(
                entry,
                PolicyA,
                MessageSecurityMode.SignAndEncrypt,
                s_clientChannelCertificate).ConfigureAwait(false);

            Assert.That(decision, Is.EqualTo(DistributedSessionManager.RestoreDecision.NonceInvalid));
        }

        [Test]
        public async Task AuthorizeRejectsMissingVersionedSecurityStateAsync()
        {
            using var registryKv = new InMemorySharedKeyValueStore();
            var registry = new SharedSingleUseNonceRegistry(registryKv);
            using DistributedSessionManager manager = CreateManager(registry);
            SharedSessionEntry entry = EntryWithNonce(CreateBytes(32, 10)) with
            {
                SecurityStateVersion = 0
            };

            DistributedSessionManager.RestoreDecision decision =
                await manager.AuthorizeAndConsumeAsync(
                    entry,
                    PolicyA,
                    MessageSecurityMode.SignAndEncrypt,
                    s_clientChannelCertificate).ConfigureAwait(false);

            Assert.That(
                decision,
                Is.EqualTo(DistributedSessionManager.RestoreDecision.SecurityStateMissing));
        }

        [Test]
        public async Task AuthorizeRejectsMissingClientUserIdAsync()
        {
            using var registryKv = new InMemorySharedKeyValueStore();
            var registry = new SharedSingleUseNonceRegistry(registryKv);
            using DistributedSessionManager manager = CreateManager(registry);
            SharedSessionEntry entry = EntryWithNonce(CreateBytes(32, 11)) with
            {
                ClientUserId = string.Empty
            };

            DistributedSessionManager.RestoreDecision decision =
                await manager.AuthorizeAndConsumeAsync(
                    entry,
                    PolicyA,
                    MessageSecurityMode.SignAndEncrypt,
                    s_clientChannelCertificate).ConfigureAwait(false);

            Assert.That(
                decision,
                Is.EqualTo(DistributedSessionManager.RestoreDecision.SecurityStateMissing));
        }

        [Test]
        public async Task AuthorizeRejectsDifferentOriginalChannelCertificateAsync()
        {
            using var registryKv = new InMemorySharedKeyValueStore();
            var registry = new SharedSingleUseNonceRegistry(registryKv);
            using DistributedSessionManager manager = CreateManager(registry);
            SharedSessionEntry entry = EntryWithNonce(CreateBytes(32, 12));

            DistributedSessionManager.RestoreDecision decision =
                await manager.AuthorizeAndConsumeAsync(
                    entry,
                    PolicyA,
                    MessageSecurityMode.SignAndEncrypt,
                    ByteString.From(CreateBytes(32, 0x61))).ConfigureAwait(false);

            Assert.That(
                decision,
                Is.EqualTo(
                    DistributedSessionManager.RestoreDecision.ClientCertificateMismatch));
        }

        [Test]
        public async Task AuthorizeRejectsAllZeroNonceAsync()
        {
            using var registryKv = new InMemorySharedKeyValueStore();
            var registry = new SharedSingleUseNonceRegistry(registryKv);
            using DistributedSessionManager manager = CreateManager(registry);
            SharedSessionEntry entry = EntryWithNonce(new byte[32]);

            DistributedSessionManager.RestoreDecision decision =
                await manager.AuthorizeAndConsumeAsync(
                    entry,
                    PolicyA,
                    MessageSecurityMode.SignAndEncrypt,
                    s_clientChannelCertificate).ConfigureAwait(false);

            Assert.That(decision, Is.EqualTo(DistributedSessionManager.RestoreDecision.NonceInvalid));
        }

        [Test]
        public async Task AuthorizeRejectsExpiredSessionAsync()
        {
            var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-07-08T12:00:00Z", CultureInfo.InvariantCulture));
            using var registryKv = new InMemorySharedKeyValueStore();
            var registry = new SharedSingleUseNonceRegistry(registryKv);
            using DistributedSessionManager manager = CreateManager(registry, timeProvider: timeProvider);
            SharedSessionEntry entry = EntryWithNonce(CreateBytes(32, 4)) with
            {
                LastActivatedAt = DateTimeUtc.From(timeProvider.GetUtcNow().UtcDateTime.AddMilliseconds(-1001)),
                SessionTimeout = 1000
            };

            DistributedSessionManager.RestoreDecision decision = await manager.AuthorizeAndConsumeAsync(
                entry,
                PolicyA,
                MessageSecurityMode.SignAndEncrypt,
                s_clientChannelCertificate).ConfigureAwait(false);

            Assert.That(decision, Is.EqualTo(DistributedSessionManager.RestoreDecision.Expired));
        }

        [Test]
        public async Task RestoreRejectsExpiredMirroredSessionAsync()
        {
            var timeProvider = new FakeTimeProvider(DateTimeOffset.Parse("2026-07-08T12:00:00Z", CultureInfo.InvariantCulture));
            using var registryKv = new InMemorySharedKeyValueStore();
            using var sessionKv = new InMemorySharedKeyValueStore();
            var registry = new SharedSingleUseNonceRegistry(registryKv);
            var sessionStore = new SharedKeyValueSessionStore(
                sessionKv,
                ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()));
            using DistributedSessionManager manager = CreateManager(
                registry,
                sessionStore: sessionStore,
                timeProvider: timeProvider);
            var authenticationToken = new NodeId("expired-token", 2);
            SharedSessionEntry entry = EntryWithNonce(CreateBytes(32, 5)) with
            {
                AuthenticationToken = authenticationToken,
                LastActivatedAt = DateTimeUtc.From(timeProvider.GetUtcNow().UtcDateTime.AddMilliseconds(-1001)),
                SessionTimeout = 1000
            };
            await sessionStore.PutAsync(entry).ConfigureAwait(false);

            ISession? session = await InvokeRestoreSessionAsync(
                manager,
                authenticationToken,
                CreateContext()).ConfigureAwait(false);

            Assert.That(session, Is.Null);
        }

        [Test]
        public void SecureRestoreWithinTimeoutReconstructsClientCertificate()
        {
            using var registryKv = new InMemorySharedKeyValueStore();
            var registry = new SharedSingleUseNonceRegistry(registryKv);
            using Certificate serverCertificate = CreateCertificate("CN=DistributedServer");
            using Certificate clientCertificate = CreateCertificate("CN=DistributedClient");
            Mock<IServerInternal> server = CreateRestoreServerMock(true);
            using DistributedSessionManager manager = CreateManager(
                registry,
                server: server.Object,
                serverCertificateProvider: _ => serverCertificate.AddRef());
            SharedSessionEntry entry = CreateSecureEntry(clientCertificate) with
            {
                LastActivatedAt = DateTimeUtc.Now,
                SessionTimeout = 60_000
            };

            ISession? session = InvokeReconstructSession(
                manager,
                entry,
                entry.AuthenticationToken,
                CreateContext(clientCertificate.RawData));

            Assert.That(session, Is.Not.Null);
            Assert.That(session!.ClientCertificate, Is.Not.Null);
            Assert.That(session.ClientCertificate!.Thumbprint, Is.EqualTo(clientCertificate.Thumbprint));
            Assert.That(session.EndpointDescription.SecurityMode, Is.EqualTo(MessageSecurityMode.Sign));
            Assert.That(session.EndpointDescription.SecurityPolicyUri, Is.EqualTo(PolicyA));
            session.Dispose();
        }

        [Test]
        public void SecureRestoreWithoutClientCertificateChainIsRejected()
        {
            using var registryKv = new InMemorySharedKeyValueStore();
            var registry = new SharedSingleUseNonceRegistry(registryKv);
            using Certificate serverCertificate = CreateCertificate("CN=DistributedServer");
            Mock<IServerInternal> server = CreateRestoreServerMock(true);
            using DistributedSessionManager manager = CreateManager(
                registry,
                server: server.Object,
                serverCertificateProvider: _ => serverCertificate.AddRef());
            SharedSessionEntry entry = EntryWithNonce(CreateBytes(32, 6)) with
            {
                SecurityMode = (int)MessageSecurityMode.Sign,
                ClientCertificateChain = ByteString.Empty,
                SessionTimeout = 60_000
            };

            ISession? session = InvokeReconstructSession(
                manager,
                entry,
                entry.AuthenticationToken,
                CreateContext(s_clientChannelCertificate.ToArray()));

            Assert.That(session, Is.Null);
        }

        [Test]
        public async Task FailedSecureRestoreDisposesTemporaryCertificateHandlesAsync()
        {
            using var registryKv = new InMemorySharedKeyValueStore();
            using var sessionKv = new InMemorySharedKeyValueStore();
            var registry = new SharedSingleUseNonceRegistry(registryKv);
            var sessionStore = new SharedKeyValueSessionStore(
                sessionKv,
                ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()));
            Certificate serverCertificate = CreateCertificate("CN=DistributedServer");
            Certificate clientCertificate = CreateCertificate("CN=DistributedClient");
            Mock<IServerInternal> server = CreateRestoreServerMock(false);
            string serverThumbprint = serverCertificate.Thumbprint;
            string clientThumbprint = clientCertificate.Thumbprint;

            try
            {
                using DistributedSessionManager manager = CreateManager(
                    registry,
                    sessionStore: sessionStore,
                    server: server.Object,
                    serverCertificateProvider: _ => serverCertificate.AddRef());
                SharedSessionEntry entry = CreateSecureEntry(clientCertificate) with
                {
                    SessionTimeout = 60_000
                };
                await sessionStore.PutAsync(entry).ConfigureAwait(false);
                OperationContext context = CreateContext(clientCertificate.RawData);
                InvalidOperationException? exception = null;
                try
                {
                    _ = await InvokeRestoreSessionAsync(
                        manager,
                        entry.AuthenticationToken,
                        context).ConfigureAwait(false);
                }
                catch (InvalidOperationException ex)
                {
                    exception = ex;
                }

                Assert.That(exception, Is.Not.Null);
            }
            finally
            {
                serverCertificate.Dispose();
                clientCertificate.Dispose();
            }

            Assert.That(GetCertificateRefCount(serverCertificate), Is.Zero, serverThumbprint);
            Assert.That(GetCertificateRefCount(clientCertificate), Is.Zero, clientThumbprint);
        }

        [Test]
        public void ConstructorValidatesArguments()
        {
            using var registryKv = new InMemorySharedKeyValueStore();
            var registry = new SharedSingleUseNonceRegistry(registryKv);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            IServiceMessageContext context = ServiceMessageContext.CreateEmpty(telemetry);
            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Telemetry).Returns(telemetry);
            var configuration = new ApplicationConfiguration
            {
                ServerConfiguration = new ServerConfiguration
                {
                    MinSessionTimeout = 1000,
                    MaxSessionTimeout = 3_600_000,
                    MaxSessionCount = 100
                }
            };
            using var kv = new InMemorySharedKeyValueStore();
            var store = new SharedKeyValueSessionStore(kv, context);

            Assert.That(
                () => new DistributedSessionManager(
                    serverMock.Object, configuration, store, registry, null!),
                Throws.ArgumentNullException);
        }

        private static SharedSessionEntry CreateSecureEntry(Certificate clientCertificate)
        {
            using var clientChain = new CertificateCollection { clientCertificate };
            return new SharedSessionEntry
            {
                SessionId = new NodeId(1, 1),
                AuthenticationToken = new NodeId("secure-token", 2),
                SessionName = "secure-session",
                CreatedAt = DateTimeUtc.Now,
                LastActivatedAt = DateTimeUtc.Now,
                ServerNonce = ByteString.From(CreateBytes(32, 7)),
                ClientNonce = ByteString.From(new byte[] { 5, 6, 7, 8 }),
                ClientCertificateChain = ByteString.From(Utils.CreateCertificateChainBlob(clientChain)),
                SecurityStateVersion = SharedSessionEntry.CurrentSecurityStateVersion,
                OriginalClientChannelCertificate = clientCertificate.RawData.ToByteString(),
                ClientUserId = "Anonymous",
                SecurityPolicyUri = PolicyA,
                SecurityMode = (int)MessageSecurityMode.Sign,
                EndpointUrl = "opc.tcp://localhost:4840",
                SessionTimeout = 60_000,
                ClientDescription = new ApplicationDescription
                {
                    ApplicationUri = "urn:test:client",
                    ApplicationName = new LocalizedText("DistributedSessionManagerTests"),
                    ApplicationType = ApplicationType.Client
                }
            };
        }

        private static Certificate CreateCertificate(string subject)
        {
            return CertificateBuilder
                .Create(subject)
                .SetRSAKeySize(CertificateFactory.DefaultKeySize)
                .CreateForRSA();
        }

        private static OperationContext CreateContext(byte[]? clientChannelCertificate = null)
        {
            var endpoint = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityPolicyUri = PolicyA,
                SecurityMode = MessageSecurityMode.Sign
            };
            var channelContext = new SecureChannelContext(
                "restore-channel",
                endpoint,
                RequestEncoding.Binary,
                clientChannelCertificate,
                null,
                null);
            return new OperationContext(
                new RequestHeader(),
                channelContext,
                RequestType.ActivateSession,
                RequestLifetime.None);
        }

        private static Mock<IServerInternal> CreateRestoreServerMock(bool initializeSucceeds)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Telemetry).Returns(telemetry);
            serverMock.Setup(s => s.NamespaceUris).Returns(new NamespaceTable());
            serverMock.Setup(s => s.SubscriptionStore).Returns((ISubscriptionStore)null!);

            var diagnosticsNodeManager = new Mock<IDiagnosticsNodeManager>();
            if (initializeSucceeds)
            {
                diagnosticsNodeManager
                    .Setup(m => m.CreateSessionDiagnosticsAsync(
                        It.IsAny<ServerSystemContext>(),
                        It.IsAny<SessionDiagnosticsDataType>(),
                        It.IsAny<NodeValueSimpleEventHandler>(),
                        It.IsAny<SessionSecurityDiagnosticsDataType>(),
                        It.IsAny<NodeValueSimpleEventHandler>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(new ValueTask<NodeId>(new NodeId(5001, 1)));
            }
            else
            {
                diagnosticsNodeManager
                    .Setup(m => m.CreateSessionDiagnosticsAsync(
                        It.IsAny<ServerSystemContext>(),
                        It.IsAny<SessionDiagnosticsDataType>(),
                        It.IsAny<NodeValueSimpleEventHandler>(),
                        It.IsAny<SessionSecurityDiagnosticsDataType>(),
                        It.IsAny<NodeValueSimpleEventHandler>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(
                        new ValueTask<NodeId>(
                            Task.FromException<NodeId>(
                                new InvalidOperationException("diagnostics unavailable"))));
            }

            serverMock.Setup(s => s.DiagnosticsNodeManager).Returns(diagnosticsNodeManager.Object);
            serverMock.Setup(s => s.DefaultSystemContext).Returns(new ServerSystemContext(serverMock.Object));
            return serverMock;
        }

        private static ISession? InvokeReconstructSession(
            DistributedSessionManager manager,
            SharedSessionEntry entry,
            NodeId authenticationToken,
            OperationContext context)
        {
            MethodInfo method = typeof(DistributedSessionManager).GetMethod(
                "ReconstructSession",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            return (ISession?)method.Invoke(manager, [entry, authenticationToken, context]);
        }

        private static async Task<ISession?> InvokeRestoreSessionAsync(
            DistributedSessionManager manager,
            NodeId authenticationToken,
            OperationContext context)
        {
            MethodInfo method = typeof(DistributedSessionManager).GetMethod(
                "RestoreSessionAsync",
                BindingFlags.Instance | BindingFlags.NonPublic)!;
            var task = (ValueTask<ISession?>)method.Invoke(manager, [authenticationToken, context, default(CancellationToken)])!;
            return await task.ConfigureAwait(false);
        }

        private static int GetCertificateRefCount(Certificate certificate)
        {
            object core = typeof(Certificate)
                .GetField("m_core", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(certificate)!;
            return (int)core.GetType().GetProperty("RefCount", BindingFlags.Instance | BindingFlags.Public)!
                .GetValue(core)!;
        }

        private static byte[] CreateBytes(int length, byte seed)
        {
            byte[] bytes = new byte[length];
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i] = (byte)(seed + i);
            }
            return bytes;
        }
    }
}
