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
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Server.Distributed;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Distributed
{
    /// <summary>
    /// Unit tests for the security decision logic of
    /// <see cref="DistributedSessionManager"/>: the REQ-UA-7 SecurityPolicy/Mode
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

        private static readonly InMemorySharedKeyValueStore s_sessionKv = new();
        private static readonly SharedKeyValueSessionStore s_sessionStore =
            new(s_sessionKv, ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()));

        private static DistributedSessionManager CreateManager(ISingleUseNonceRegistry nonceRegistry)
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var serverMock = new Mock<IServerInternal>();
            serverMock.Setup(s => s.Telemetry).Returns(telemetry);

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
                serverMock.Object,
                configuration,
                s_sessionStore,
                nonceRegistry,
                _ => (Certificate?)null,
                new DistributedSessionOptions { EnableFastReconnect = true });
        }

        private static SharedSessionEntry EntryWithNonce(byte[] nonce)
        {
            return new SharedSessionEntry
            {
                SessionId = new NodeId(1, 1),
                AuthenticationToken = new NodeId(2, 1),
                SecurityPolicyUri = PolicyA,
                SecurityMode = (int)MessageSecurityMode.SignAndEncrypt,
                ServerNonce = ByteString.From(nonce)
            };
        }

        [Test]
        public async Task AuthorizeSucceedsForMatchingPolicyAndFreshNonceAsync()
        {
            using var registryKv = new InMemorySharedKeyValueStore();
            var registry = new SharedSingleUseNonceRegistry(registryKv);
            using DistributedSessionManager manager = CreateManager(registry);
            SharedSessionEntry entry = EntryWithNonce(new byte[] { 1, 2, 3, 4 });

            DistributedSessionManager.RestoreDecision decision = await manager.AuthorizeAndConsumeAsync(
                entry, PolicyA, MessageSecurityMode.SignAndEncrypt);

            Assert.That(decision, Is.EqualTo(DistributedSessionManager.RestoreDecision.Authorized));
        }

        [Test]
        public async Task AuthorizeRejectsMismatchedPolicyAsync()
        {
            using var registryKv = new InMemorySharedKeyValueStore();
            var registry = new SharedSingleUseNonceRegistry(registryKv);
            using DistributedSessionManager manager = CreateManager(registry);
            SharedSessionEntry entry = EntryWithNonce(new byte[] { 1, 2, 3, 4 });

            DistributedSessionManager.RestoreDecision decision = await manager.AuthorizeAndConsumeAsync(
                entry, PolicyB, MessageSecurityMode.SignAndEncrypt);

            Assert.That(decision, Is.EqualTo(DistributedSessionManager.RestoreDecision.PolicyMismatch));
        }

        [Test]
        public async Task AuthorizeRejectsMismatchedSecurityModeAsync()
        {
            using var registryKv = new InMemorySharedKeyValueStore();
            var registry = new SharedSingleUseNonceRegistry(registryKv);
            using DistributedSessionManager manager = CreateManager(registry);
            SharedSessionEntry entry = EntryWithNonce(new byte[] { 1, 2, 3, 4 });

            DistributedSessionManager.RestoreDecision decision = await manager.AuthorizeAndConsumeAsync(
                entry, PolicyA, MessageSecurityMode.Sign);

            Assert.That(decision, Is.EqualTo(DistributedSessionManager.RestoreDecision.PolicyMismatch));
        }

        [Test]
        public async Task AuthorizeRejectsReplayedNonceAsync()
        {
            using var registryKv = new InMemorySharedKeyValueStore();
            var registry = new SharedSingleUseNonceRegistry(registryKv);
            using DistributedSessionManager manager = CreateManager(registry);
            SharedSessionEntry entry = EntryWithNonce(new byte[] { 9, 9, 9, 9 });

            DistributedSessionManager.RestoreDecision first = await manager.AuthorizeAndConsumeAsync(
                entry, PolicyA, MessageSecurityMode.SignAndEncrypt);
            DistributedSessionManager.RestoreDecision second = await manager.AuthorizeAndConsumeAsync(
                entry, PolicyA, MessageSecurityMode.SignAndEncrypt);

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
            SharedSessionEntry entry = EntryWithNonce(Array.Empty<byte>());

            DistributedSessionManager.RestoreDecision decision = await manager.AuthorizeAndConsumeAsync(
                entry, PolicyA, MessageSecurityMode.SignAndEncrypt);

            Assert.That(decision, Is.EqualTo(DistributedSessionManager.RestoreDecision.NonceReplayed));
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
    }
}
