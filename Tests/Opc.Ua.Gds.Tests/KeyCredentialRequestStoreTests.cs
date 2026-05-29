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
    }
}
