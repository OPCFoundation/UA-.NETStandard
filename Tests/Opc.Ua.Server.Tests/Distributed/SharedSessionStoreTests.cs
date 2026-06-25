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

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.Distributed;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Distributed
{
    /// <summary>
    /// Unit tests for <see cref="SharedKeyValueSessionStore"/>.
    /// </summary>
    [TestFixture]
    [Category("Distributed")]
    [Parallelizable(ParallelScope.All)]
    public class SharedSessionStoreTests
    {
        private const ushort NamespaceIndex = 1;
        private IServiceMessageContext m_context = null!;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            ServiceMessageContext messageContext = ServiceMessageContext.CreateEmpty(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend("urn:test:session");
            m_context = messageContext;
        }

        [Test]
        public async Task PutAndTryGetRoundTripsAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var store = new SharedKeyValueSessionStore(kv, m_context);
            SharedSessionEntry entry = NewEntry("tok-1");

            await store.PutAsync(entry);
            SharedSessionEntry? loaded = await store.TryGetAsync(entry.AuthenticationToken);

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.SessionId, Is.EqualTo(entry.SessionId));
            Assert.That(loaded.AuthenticationToken, Is.EqualTo(entry.AuthenticationToken));
            Assert.That(loaded.SessionName, Is.EqualTo(entry.SessionName));
            Assert.That(loaded.CreatedAt, Is.EqualTo(entry.CreatedAt));
            Assert.That(loaded.LastActivatedAt, Is.EqualTo(entry.LastActivatedAt));
            Assert.That(loaded.SecretMaterial.ToArray(), Is.EqualTo(entry.SecretMaterial.ToArray()));
        }

        [Test]
        public async Task FullEntryRoundTripsAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var store = new SharedKeyValueSessionStore(kv, m_context);
            SharedSessionEntry entry = NewEntry("tok-full");

            await store.PutAsync(entry);
            SharedSessionEntry? loaded = await store.TryGetAsync(entry.AuthenticationToken);

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.ServerNonce.ToArray(), Is.EqualTo(entry.ServerNonce.ToArray()));
            Assert.That(loaded.ClientNonce.ToArray(), Is.EqualTo(entry.ClientNonce.ToArray()));
            Assert.That(
                loaded.ClientCertificateChain.ToArray(),
                Is.EqualTo(entry.ClientCertificateChain.ToArray()));
            Assert.That(loaded.SecurityPolicyUri, Is.EqualTo(entry.SecurityPolicyUri));
            Assert.That(loaded.SecurityMode, Is.EqualTo(entry.SecurityMode));
            Assert.That(loaded.EndpointUrl, Is.EqualTo(entry.EndpointUrl));
            Assert.That(loaded.SessionTimeout, Is.EqualTo(entry.SessionTimeout));
            Assert.That(
                loaded.ClientDescription.ApplicationUri,
                Is.EqualTo(entry.ClientDescription.ApplicationUri));
        }

        [Test]
        public async Task TryGetMissingReturnsNullAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var store = new SharedKeyValueSessionStore(kv, m_context);

            SharedSessionEntry? loaded = await store.TryGetAsync(new NodeId("nope", NamespaceIndex));

            Assert.That(loaded, Is.Null);
        }

        [Test]
        public async Task RemoveDeletesEntryAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var store = new SharedKeyValueSessionStore(kv, m_context);
            SharedSessionEntry entry = NewEntry("tok-2");
            await store.PutAsync(entry);

            bool removed = await store.RemoveAsync(entry.AuthenticationToken);
            SharedSessionEntry? loaded = await store.TryGetAsync(entry.AuthenticationToken);

            Assert.That(removed, Is.True);
            Assert.That(loaded, Is.Null);
        }

        [Test]
        public async Task SessionVisibleToOtherReplicaSharingStoreAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var active = new SharedKeyValueSessionStore(kv, m_context);
            var standby = new SharedKeyValueSessionStore(kv, m_context);
            SharedSessionEntry entry = NewEntry("tok-3");

            await active.PutAsync(entry);
            SharedSessionEntry? onStandby = await standby.TryGetAsync(entry.AuthenticationToken);

            Assert.That(onStandby, Is.Not.Null, "standby can reconnect the session using just the token");
            Assert.That(onStandby!.SessionId, Is.EqualTo(entry.SessionId));
        }

        [Test]
        public async Task ProtectedSessionRoundTripsAndIsEncryptedAtRestAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var protector = new AesCbcHmacRecordProtector(MakeKey(21));
            var store = new SharedKeyValueSessionStore(kv, m_context, protector);
            SharedSessionEntry entry = NewEntry("tok-prot");

            await store.PutAsync(entry);
            SharedSessionEntry? loaded = await store.TryGetAsync(entry.AuthenticationToken);

            // Secret material and the server nonce must not be persisted in cleartext.
            (bool rawFound, ByteString raw) = await kv.TryGetAsync("session/" + entry.AuthenticationToken);
            Assert.That(rawFound, Is.True);
            Assert.That(Contains(raw.ToArray(), entry.SecretMaterial.ToArray()), Is.False);
            Assert.That(Contains(raw.ToArray(), entry.ServerNonce.ToArray()), Is.False);

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.SecretMaterial.ToArray(), Is.EqualTo(entry.SecretMaterial.ToArray()));
            Assert.That(loaded.ServerNonce.ToArray(), Is.EqualTo(entry.ServerNonce.ToArray()));
        }

        [Test]
        public async Task TamperedProtectedSessionIsRejectedFailClosedAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            using var protector = new AesCbcHmacRecordProtector(MakeKey(22));
            var store = new SharedKeyValueSessionStore(kv, m_context, protector);
            SharedSessionEntry entry = NewEntry("tok-tamper");
            await store.PutAsync(entry);

            await kv.SetAsync(
                "session/" + entry.AuthenticationToken,
                ByteString.From(new byte[] { 1, 2, 3, 4, 5, 6 }));

            SharedSessionEntry? loaded = await store.TryGetAsync(entry.AuthenticationToken);

            Assert.That(loaded, Is.Null);
        }

        private static bool Contains(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= haystack.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                {
                    return true;
                }
            }
            return false;
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

        private SharedSessionEntry NewEntry(string token)
        {
            return new SharedSessionEntry
            {
                SessionId = new NodeId(42, NamespaceIndex),
                AuthenticationToken = new NodeId(token, NamespaceIndex),
                SessionName = "Session " + token,
                CreatedAt = DateTimeUtc.Now,
                LastActivatedAt = DateTimeUtc.Now,
                ServerNonce = ByteString.From(new byte[] { 40, 50, 60, 70 }),
                ClientNonce = ByteString.From(new byte[] { 1, 2, 3, 4 }),
                ClientCertificateChain = ByteString.From(new byte[] { 5, 6, 7, 8, 9 }),
                SecurityPolicyUri = "http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256",
                SecurityMode = (int)MessageSecurityMode.SignAndEncrypt,
                EndpointUrl = "opc.tcp://localhost:4840",
                SessionTimeout = 60000,
                ClientDescription = new ApplicationDescription
                {
                    ApplicationName = new LocalizedText("Test Client"),
                    ApplicationUri = "urn:test:client:" + token,
                    ApplicationType = ApplicationType.Client
                },
                SecretMaterial = ByteString.From(new byte[] { 10, 20, 30 })
            };
        }
    }
}
