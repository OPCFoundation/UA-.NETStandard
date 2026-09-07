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

// IDE0230: byte-array literals below are opaque binary test vectors, not text; a
// UTF-8 "..."u8 literal would misrepresent their intent, so keep the explicit byte arrays.
#pragma warning disable IDE0230 // Use UTF-8 string literal

// CA2007: tests run without a SynchronizationContext; ConfigureAwait(false)
// adds noise without a behavioural benefit. Disabled file-level for the suite.
#pragma warning disable CA2007

#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Redundancy;
using Opc.Ua.Redundancy.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Redundancy
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
            var messageContext = ServiceMessageContext.CreateEmpty(telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend("urn:test:session");
            m_context = messageContext;
        }

        [Test]
        public async Task PutAndTryGetRoundTripsAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var store = new SharedKeyValueSessionStore(kv, m_context);
            SharedSessionEntry entry = NewEntry("tok-1");

            await store.PutAsync(entry).ConfigureAwait(false);
            SharedSessionEntry? loaded = await store.TryGetAsync(entry.AuthenticationToken).ConfigureAwait(false);

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

            await store.PutAsync(entry).ConfigureAwait(false);
            SharedSessionEntry? loaded = await store.TryGetAsync(entry.AuthenticationToken).ConfigureAwait(false);

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
            Assert.That(loaded.SecurityStateVersion, Is.EqualTo(entry.SecurityStateVersion));
            Assert.That(
                loaded.OriginalClientChannelCertificate,
                Is.EqualTo(entry.OriginalClientChannelCertificate));
            Assert.That(loaded.ClientUserId, Is.EqualTo(entry.ClientUserId));
            Assert.That(loaded.ClientUserTokenType, Is.EqualTo(entry.ClientUserTokenType));
            Assert.That(
                loaded.HasActivatedUserIdentity,
                Is.EqualTo(entry.HasActivatedUserIdentity));
            Assert.That(
                loaded.ClientDescription.ApplicationUri,
                Is.EqualTo(entry.ClientDescription.ApplicationUri));
        }

        [Test]
        public async Task TryGetMissingReturnsNullAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var store = new SharedKeyValueSessionStore(kv, m_context);

            SharedSessionEntry? loaded = await store.TryGetAsync(new NodeId("nope", NamespaceIndex)).ConfigureAwait(false);

            Assert.That(loaded, Is.Null);
        }

        [Test]
        public async Task RemoveDeletesEntryAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var store = new SharedKeyValueSessionStore(kv, m_context);
            SharedSessionEntry entry = NewEntry("tok-2");
            await store.PutAsync(entry).ConfigureAwait(false);

            bool removed = await store.RemoveAsync(entry.AuthenticationToken).ConfigureAwait(false);
            SharedSessionEntry? loaded = await store.TryGetAsync(entry.AuthenticationToken).ConfigureAwait(false);

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

            await active.PutAsync(entry).ConfigureAwait(false);
            SharedSessionEntry? onStandby = await standby.TryGetAsync(entry.AuthenticationToken).ConfigureAwait(false);

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

            await store.PutAsync(entry).ConfigureAwait(false);
            SharedSessionEntry? loaded = await store.TryGetAsync(entry.AuthenticationToken).ConfigureAwait(false);

            // Secret material and the server nonce must not be persisted in cleartext.
            (bool rawFound, ByteString raw) = await kv.TryGetAsync(
                SharedKeyValueSessionStore.KeyFor(entry.AuthenticationToken)).ConfigureAwait(false);
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
            await store.PutAsync(entry).ConfigureAwait(false);

            await kv.SetAsync(
                SharedKeyValueSessionStore.KeyFor(entry.AuthenticationToken),
                ByteString.From(new byte[] { 1, 2, 3, 4, 5, 6 })).ConfigureAwait(false);

            SharedSessionEntry? loaded = await store.TryGetAsync(entry.AuthenticationToken).ConfigureAwait(false);

            Assert.That(loaded, Is.Null);
        }

        [Test]
        public async Task CorruptSessionEntryIsRejectedAndLoggedAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var logger = new RecordingLogger<SharedKeyValueSessionStore>();
            var store = new SharedKeyValueSessionStore(kv, m_context, null, logger);
            var authenticationToken = new NodeId("tok-corrupt", NamespaceIndex);
            string key = SharedKeyValueSessionStore.KeyFor(authenticationToken);
            await kv.SetAsync(key, ByteString.From(new byte[] { 1, 2, 3, 4 })).ConfigureAwait(false);

            SharedSessionEntry? loaded = await store
                .TryGetAsync(authenticationToken)
                .ConfigureAwait(false);

            Assert.That(loaded, Is.Null);
            Assert.That(logger.Level, Is.EqualTo(LogLevel.Warning));
            Assert.That(logger.Message, Does.Contain(key));
            Assert.That(logger.Exception, Is.Not.Null);
        }

        [Test]
        public async Task LegacyEntryDecodesWithoutSecurityStateForFailClosedRestoreAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var store = new SharedKeyValueSessionStore(kv, m_context);
            SharedSessionEntry entry = NewEntry("tok-legacy");
            await kv.SetAsync(
                SharedKeyValueSessionStore.KeyFor(entry.AuthenticationToken),
                EncodeLegacyEntry(entry)).ConfigureAwait(false);

            SharedSessionEntry? loaded = await store
                .TryGetAsync(entry.AuthenticationToken)
                .ConfigureAwait(false);

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.SecurityStateVersion, Is.Zero);
            Assert.That(loaded.OriginalClientChannelCertificate.IsNull, Is.True);
            Assert.That(loaded.ClientUserId, Is.Null);
        }

        [Test]
        public async Task VersionOneEntryDecodesForVersionTolerantFailClosedRestoreAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var store = new SharedKeyValueSessionStore(kv, m_context);
            SharedSessionEntry entry = NewEntry("tok-version-one");
            await kv.SetAsync(
                SharedKeyValueSessionStore.KeyFor(entry.AuthenticationToken),
                EncodeVersionOneEntry(entry)).ConfigureAwait(false);

            SharedSessionEntry? loaded = await store
                .TryGetAsync(entry.AuthenticationToken)
                .ConfigureAwait(false);

            Assert.That(loaded, Is.Not.Null);
            Assert.That(loaded!.SecurityStateVersion, Is.EqualTo(1));
            Assert.That(
                loaded.OriginalClientChannelCertificate,
                Is.EqualTo(entry.OriginalClientChannelCertificate));
            Assert.That(loaded.ClientUserId, Is.EqualTo(entry.ClientUserId));
            Assert.That(loaded.HasActivatedUserIdentity, Is.False);
        }

        [Test]
        public async Task KeyspaceDoesNotExposeRawTokenAsync()
        {
            using var kv = new InMemorySharedKeyValueStore();
            var store = new SharedKeyValueSessionStore(kv, m_context);
            SharedSessionEntry entry = NewEntry("tok-keyspace");
            await store.PutAsync(entry).ConfigureAwait(false);

            string tokenText = entry.AuthenticationToken.ToString();
            await foreach (KeyValuePair<string, ByteString> pair in kv.ScanAsync("session/"))
            {
                Assert.That(
                    pair.Key.IndexOf(tokenText, StringComparison.Ordinal),
                    Is.LessThan(0),
                    "the raw authentication token must not appear in the keyspace");
            }

            // The legacy raw-token key does not exist; the hashed key resolves the entry.
            (bool legacyFound, _) = await kv.TryGetAsync("session/" + tokenText).ConfigureAwait(false);
            Assert.That(legacyFound, Is.False);
            Assert.That(await store.TryGetAsync(entry.AuthenticationToken).ConfigureAwait(false), Is.Not.Null);
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

        private static SharedSessionEntry NewEntry(string token)
        {
            return new SharedSessionEntry
            {
                SessionId = new NodeId(42, NamespaceIndex),
                AuthenticationToken = new NodeId(token, NamespaceIndex),
                SessionName = "Session " + token,
                CreatedAt = DateTimeUtc.Now,
                LastActivatedAt = DateTimeUtc.Now,
                ServerNonce = ByteString.From(CreateBytes(32, 40)),
                ClientNonce = ByteString.From(new byte[] { 1, 2, 3, 4 }),
                ClientCertificateChain = ByteString.From(new byte[] { 5, 6, 7, 8, 9 }),
                SecurityStateVersion = SharedSessionEntry.CurrentSecurityStateVersion,
                OriginalClientChannelCertificate = ByteString.From(CreateBytes(64, 5)),
                ClientUserId = "ExactUser",
                ClientUserTokenType = UserTokenType.UserName,
                HasActivatedUserIdentity = true,
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

        private ByteString EncodeLegacyEntry(SharedSessionEntry entry)
        {
            using var encoder = new BinaryEncoder(m_context);
            WriteLegacyFields(encoder, entry);
            return ByteString.From(encoder.CloseAndReturnBuffer());
        }

        private ByteString EncodeVersionOneEntry(SharedSessionEntry entry)
        {
            using var encoder = new BinaryEncoder(m_context);
            WriteLegacyFields(encoder, entry);
            encoder.WriteUInt32(null, 1);
            encoder.WriteByteString(null, entry.OriginalClientChannelCertificate);
            encoder.WriteString(null, entry.ClientUserId);
            return ByteString.From(encoder.CloseAndReturnBuffer());
        }

        private static void WriteLegacyFields(
            BinaryEncoder encoder,
            SharedSessionEntry entry)
        {
            encoder.WriteNodeId(null, entry.SessionId);
            encoder.WriteNodeId(null, entry.AuthenticationToken);
            encoder.WriteString(null, entry.SessionName);
            encoder.WriteInt64(null, entry.CreatedAt);
            encoder.WriteInt64(null, entry.LastActivatedAt);
            encoder.WriteByteString(null, entry.ServerNonce);
            encoder.WriteByteString(null, entry.ClientNonce);
            encoder.WriteByteString(null, entry.ClientCertificateChain);
            encoder.WriteString(null, entry.SecurityPolicyUri);
            encoder.WriteInt32(null, entry.SecurityMode);
            encoder.WriteString(null, entry.EndpointUrl);
            encoder.WriteDouble(null, entry.SessionTimeout);
            encoder.WriteEncodeable(null, entry.ClientDescription);
            encoder.WriteByteString(null, entry.SecretMaterial);
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

        private sealed class RecordingLogger<T> : ILogger<T>
        {
            public LogLevel? Level { get; private set; }

            public string? Message { get; private set; }

            public Exception? Exception { get; private set; }

            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull
            {
                return NullScope.Instance;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                Level = logLevel;
                Message = formatter(state, exception);
                Exception = exception;
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}
