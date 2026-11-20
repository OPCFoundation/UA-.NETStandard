/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Server.TestFramework;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests.Redundancy
{
    /// <summary>
    /// Integration test that drives a real, fully-started server whose
    /// <see cref="ISessionManagerFactory"/> builds a
    /// <see cref="DistributedSessionManager"/>, and verifies that a session
    /// created and activated through the real service handlers is mirrored
    /// (encrypted) to the shared store and removed again on close. This closes
    /// the runtime-wiring gap (factory -> <c>StandardServer.CreateSessionManager</c>
    /// -> mirror on real <c>CreateSession</c> / <c>ActivateSession</c> / close)
    /// that the manager unit tests cannot exercise. The secured token-reuse
    /// restore path is covered by the manager's unit tests (policy match +
    /// single-use nonce) and the base activation integration tests.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Category("Session")]
    [NonParallelizable]
    public class DistributedSessionMirrorIntegrationTests
    {
        private InMemorySharedKeyValueStore m_kv = null!;
        private AesCbcHmacRecordProtector m_protector = null!;
        private ServerFixture<StandardServer> m_fixture = null!;
        private StandardServer m_server = null!;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_kv = new InMemorySharedKeyValueStore();
            m_protector = new AesCbcHmacRecordProtector(MakeKey(1));
            var factory = new DistributedSessionManagerFactory(
                m_kv, m_protector, new DistributedSessionOptions { EnableFastReconnect = true });

            m_fixture = new ServerFixture<StandardServer>(t => new ReferenceServer(t)
            {
                SessionManagerFactory = factory
            });
            m_server = await m_fixture.StartAsync().ConfigureAwait(false);
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_fixture != null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
            m_protector?.Dispose();
            m_kv?.Dispose();
        }

        [Test]
        public async Task SessionIsMirroredEncryptedOnActivateAndRemovedOnCloseAsync()
        {
            const string sessionName = nameof(SessionIsMirroredEncryptedOnActivateAndRemovedOnCloseAsync);

            (RequestHeader header, SecureChannelContext context) =
                await m_server.CreateAndActivateSessionAsync(sessionName).ConfigureAwait(false);

            IServiceMessageContext messageContext = m_server.CurrentInstance.MessageContext;
            var store = new SharedKeyValueSessionStore(m_kv, messageContext, m_protector);

            SharedSessionEntry? entry = await store.TryGetAsync(header.AuthenticationToken).ConfigureAwait(false);

            Assert.That(entry, Is.Not.Null, "the session must be mirrored to the shared store on activate");
            Assert.That(entry!.AuthenticationToken, Is.EqualTo(header.AuthenticationToken));
            Assert.That(
                entry.SecurityStateVersion,
                Is.EqualTo(SharedSessionEntry.CurrentSecurityStateVersion));
            Assert.That(entry.ClientUserId, Is.Null);
            Assert.That(entry.ClientUserTokenType, Is.EqualTo(UserTokenType.Anonymous));
            Assert.That(entry.HasActivatedUserIdentity, Is.True);
            Assert.That(
                entry.OriginalClientChannelCertificate,
                Is.EqualTo(context.ClientChannelCertificate));

            // Encrypted at rest: a protector with a different key fails closed.
            using var wrongKey = new AesCbcHmacRecordProtector(MakeKey(2));
            var wrongStore = new SharedKeyValueSessionStore(m_kv, messageContext, wrongKey);
            SharedSessionEntry? wrong = await wrongStore.TryGetAsync(header.AuthenticationToken).ConfigureAwait(false);
            Assert.That(wrong, Is.Null, "the mirrored entry is encrypted; the wrong key cannot read it");

            await m_server.CloseSessionAsync(context, header, true, RequestLifetime.None).ConfigureAwait(false);

            SharedSessionEntry? afterClose = await store.TryGetAsync(header.AuthenticationToken).ConfigureAwait(false);
            Assert.That(afterClose, Is.Null, "the mirror must be removed when the session closes");
        }

        private static byte[] MakeKey(byte seed)
        {
            byte[] key = new byte[32];
            for (int i = 0; i < key.Length; i++)
            {
                key[i] = (byte)(seed + i);
            }
            return key;
        }
    }
}
