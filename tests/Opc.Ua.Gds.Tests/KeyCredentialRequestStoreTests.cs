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

using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Gds.Server;

#nullable enable

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// Unit tests for the in-memory KeyCredential request store and
    /// the round-trip StartRequestAsync / FinishRequestAsync / RevokeAsync lifecycle.
    /// </summary>
    [TestFixture]
    [Category("KeyCredential")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class KeyCredentialRequestStoreTests
    {
        private InMemoryKeyCredentialRequestStore m_store = null!;

        [SetUp]
        public void SetUp()
        {
            m_store = new InMemoryKeyCredentialRequestStore();
        }

        [Test]
        public async Task StartRequestReturnsNonNullIdAsync()
        {
            NodeId id = await m_store.StartRequestAsync(
                "urn:test:app",
                ByteString.From(new byte[] { 1, 2, 3 }),
                "http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256",
                default).ConfigureAwait(false);

            Assert.That(id.IsNull, Is.False);
        }

        [Test]
        public async Task FinishRequestReturnsCredentialAsync()
        {
            NodeId id = await m_store.StartRequestAsync(
                "urn:test:app",
                ByteString.From(new byte[] { 1, 2 }),
                null,
                default).ConfigureAwait(false);

            FinishKeyCredentialRequestResult result = await m_store.FinishRequestAsync(
                id,
                cancelRequest: false).ConfigureAwait(false);

            Assert.That(result.State, Is.EqualTo(KeyCredentialRequestState.Completed));
            Assert.That(result.CredentialId, Is.Not.Null.And.Not.Empty);
            Assert.That(result.CredentialSecret.IsEmpty, Is.False);
        }

        [Test]
        public async Task BoundRequestFinishesWithInitiatingCertificateAsync()
        {
            ByteString fingerprint = CreateFingerprint(1);
            NodeId id = await m_store.StartBoundRequestAsync(
                "urn:test:app",
                ByteString.From(new byte[] { 1, 2 }),
                null,
                default,
                fingerprint).ConfigureAwait(false);

            FinishKeyCredentialRequestResult result = await m_store.FinishBoundRequestAsync(
                id,
                cancelRequest: false,
                fingerprint).ConfigureAwait(false);

            Assert.That(result.State, Is.EqualTo(KeyCredentialRequestState.Completed));
            Assert.That(result.CredentialSecret.IsEmpty, Is.False);
        }

        [Test]
        public async Task OwnedRequestPreservesApplicationOwnershipAsync()
        {
            var applicationId = new NodeId(42);
            ByteString fingerprint = CreateFingerprint(1);
            NodeId requestId = await m_store.StartOwnedBoundRequestAsync(
                "urn:test:app",
                applicationId,
                default,
                null,
                default,
                fingerprint).ConfigureAwait(false);

            Assert.That(
                await m_store.GetRequestApplicationIdAsync(requestId).ConfigureAwait(false),
                Is.EqualTo(applicationId));

            FinishKeyCredentialRequestResult result = await m_store.FinishOwnedBoundRequestAsync(
                requestId,
                cancelRequest: false,
                applicationId,
                fingerprint).ConfigureAwait(false);

            Assert.That(
                await m_store
                    .GetCredentialApplicationIdAsync(result.CredentialId!)
                    .ConfigureAwait(false),
                Is.EqualTo(applicationId));
            Assert.That(
                async () => await m_store
                    .RevokeOwnedAsync(result.CredentialId!, new NodeId(43))
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSecurityChecksFailed));
            Assert.That(
                async () => await m_store
                    .RevokeOwnedAsync(result.CredentialId!, applicationId)
                    .ConfigureAwait(false),
                Throws.Nothing);
        }

        [Test]
        public async Task OwnedFinishRejectsDifferentApplicationAsync()
        {
            var applicationId = new NodeId(42);
            ByteString fingerprint = CreateFingerprint(1);
            NodeId requestId = await m_store.StartOwnedBoundRequestAsync(
                "urn:test:app",
                applicationId,
                default,
                null,
                default,
                fingerprint).ConfigureAwait(false);

            Assert.That(
                async () => await m_store.FinishOwnedBoundRequestAsync(
                    requestId,
                    cancelRequest: false,
                    new NodeId(43),
                    fingerprint).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        public async Task OwnershipLookupRejectsLegacyUnownedRecordAsync()
        {
            NodeId requestId = await m_store.StartBoundRequestAsync(
                "urn:test:app",
                default,
                null,
                default,
                CreateFingerprint(1)).ConfigureAwait(false);

            Assert.That(
                async () => await m_store
                    .GetRequestApplicationIdAsync(requestId)
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        public void OwnershipLookupUsesConformantInvalidArgumentStatus()
        {
            Assert.That(
                async () => await m_store
                    .GetRequestApplicationIdAsync(new NodeId(999))
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadInvalidArgument));
            Assert.That(
                async () => await m_store
                    .GetCredentialApplicationIdAsync("unknown-credential")
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public async Task BoundRequestRejectsDifferentCertificateAsync()
        {
            NodeId id = await m_store.StartBoundRequestAsync(
                "urn:test:app",
                default,
                null,
                default,
                CreateFingerprint(1)).ConfigureAwait(false);

            Assert.That(
                async () => await m_store.FinishBoundRequestAsync(
                    id,
                    cancelRequest: false,
                    CreateFingerprint(2)).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        public async Task LegacyFinishRejectsBoundRequestAsync()
        {
            NodeId id = await m_store.StartBoundRequestAsync(
                "urn:test:app",
                default,
                null,
                default,
                CreateFingerprint(1)).ConfigureAwait(false);

            Assert.That(
                async () => await m_store.FinishRequestAsync(
                    id,
                    cancelRequest: false).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        public async Task BoundFinishRejectsLegacyUnboundRequestAsync()
        {
            NodeId id = await m_store.StartRequestAsync(
                "urn:test:app",
                default,
                null,
                default).ConfigureAwait(false);

            Assert.That(
                async () => await m_store.FinishBoundRequestAsync(
                    id,
                    cancelRequest: false,
                    CreateFingerprint(1)).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        public void BindingCompatibilityRejectsLegacyStore()
        {
            IKeyCredentialRequestStore store = new LegacyKeyCredentialRequestStore();

            Assert.That(
                async () => await store.StartBoundRequestCompatAsync(
                    "urn:test:app",
                    default,
                    null,
                    default,
                    CreateFingerprint(1),
                    CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        public async Task FinishRequestWithCancelRejectsRequestAsync()
        {
            NodeId id = await m_store.StartRequestAsync(
                "urn:test:app",
                ByteString.From(new byte[] { 1 }),
                null,
                default).ConfigureAwait(false);

            FinishKeyCredentialRequestResult result = await m_store.FinishRequestAsync(
                id,
                cancelRequest: true).ConfigureAwait(false);

            Assert.That(result.State, Is.EqualTo(KeyCredentialRequestState.Rejected));
            Assert.That(result.CredentialId, Is.Null);
        }

        [Test]
        public void FinishRequestWithUnknownIdThrows()
        {
            Assert.That(
                async () => await m_store.FinishRequestAsync(
                    new NodeId(999),
                    cancelRequest: false).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public async Task RevokeMarksCredentialAsRejectedAsync()
        {
            NodeId id = await m_store.StartRequestAsync(
                "urn:test:app",
                ByteString.From(new byte[] { 1 }),
                null,
                default).ConfigureAwait(false);

            FinishKeyCredentialRequestResult result = await m_store.FinishRequestAsync(
                id, cancelRequest: false).ConfigureAwait(false);

            Assert.That(
                async () => await m_store.RevokeAsync(result.CredentialId!).ConfigureAwait(false),
                Throws.Nothing);
        }

        [Test]
        public void RevokeUnknownCredentialThrows()
        {
            Assert.That(
                async () => await m_store.RevokeAsync("unknown-credential").ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public async Task MultipleRequestsGetUniqueIdsAsync()
        {
            NodeId id1 = await m_store.StartRequestAsync("urn:app1", default, null, default).ConfigureAwait(false);
            NodeId id2 = await m_store.StartRequestAsync("urn:app2", default, null, default).ConfigureAwait(false);

            Assert.That(id1, Is.Not.EqualTo(id2));
        }

        private static ByteString CreateFingerprint(byte value)
        {
            byte[] fingerprint = new byte[32];
            fingerprint[0] = value;
            return ByteString.From(fingerprint);
        }

        private sealed class LegacyKeyCredentialRequestStore : IKeyCredentialRequestStore
        {
            public ValueTask<NodeId> StartRequestAsync(
                string applicationUri,
                ByteString publicKey,
                string? securityPolicyUri,
                ArrayOf<NodeId> requestedRoles,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<NodeId>(new NodeId(1));
            }

            public ValueTask<FinishKeyCredentialRequestResult> FinishRequestAsync(
                NodeId requestId,
                bool cancelRequest,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<FinishKeyCredentialRequestResult>(
                    new FinishKeyCredentialRequestResult
                    {
                        State = KeyCredentialRequestState.Completed
                    });
            }

            public ValueTask RevokeAsync(
                string credentialId,
                CancellationToken cancellationToken = default)
            {
                return default;
            }
        }
    }
}
