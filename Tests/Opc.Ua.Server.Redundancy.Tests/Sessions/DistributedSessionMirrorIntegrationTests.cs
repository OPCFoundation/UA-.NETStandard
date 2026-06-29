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
using NUnit.Framework;
using Opc.Ua.Server.Redundancy;
using Opc.Ua.Server.TestFramework;
using Quickstarts.ReferenceServer;
using Opc.Ua.Redundancy;

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

            m_fixture = new ServerFixture<StandardServer>(t =>
            {
                var server = new ReferenceServer(t);
                server.SessionManagerFactory = factory;
                return server;
            });
            m_server = await m_fixture.StartAsync();
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_fixture != null)
            {
                await m_fixture.StopAsync();
            }
            m_protector?.Dispose();
            m_kv?.Dispose();
        }

        [Test]
        public async Task SessionIsMirroredEncryptedOnActivateAndRemovedOnCloseAsync()
        {
            const string sessionName = nameof(SessionIsMirroredEncryptedOnActivateAndRemovedOnCloseAsync);

            (RequestHeader header, SecureChannelContext context) =
                await m_server.CreateAndActivateSessionAsync(sessionName);

            IServiceMessageContext messageContext = m_server.CurrentInstance.MessageContext;
            var store = new SharedKeyValueSessionStore(m_kv, messageContext, m_protector);

            SharedSessionEntry? entry = await store.TryGetAsync(header.AuthenticationToken);

            Assert.That(entry, Is.Not.Null, "the session must be mirrored to the shared store on activate");
            Assert.That(entry!.AuthenticationToken, Is.EqualTo(header.AuthenticationToken));

            // Encrypted at rest: a protector with a different key fails closed.
            using var wrongKey = new AesCbcHmacRecordProtector(MakeKey(2));
            var wrongStore = new SharedKeyValueSessionStore(m_kv, messageContext, wrongKey);
            SharedSessionEntry? wrong = await wrongStore.TryGetAsync(header.AuthenticationToken);
            Assert.That(wrong, Is.Null, "the mirrored entry is encrypted; the wrong key cannot read it");

            await m_server.CloseSessionAsync(context, header, true, RequestLifetime.None);

            SharedSessionEntry? afterClose = await store.TryGetAsync(header.AuthenticationToken);
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
