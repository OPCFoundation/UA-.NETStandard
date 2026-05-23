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

using NUnit.Framework;
using Opc.Ua.Gds.Server;

#nullable enable

namespace Opc.Ua.Gds.Tests
{
    /// <summary>
    /// Unit tests for the in-memory KeyCredential request store and
    /// the round-trip StartRequest / FinishRequest / Revoke lifecycle.
    /// </summary>
    [TestFixture]
    [Category("KeyCredential")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class KeyCredentialRequestStoreTests
    {
        private InMemoryKeyCredentialRequestStore m_store;

        [SetUp]
        public void SetUp()
        {
            m_store = new InMemoryKeyCredentialRequestStore();
        }

        [Test]
        public void StartRequestReturnsNonNullId()
        {
            NodeId id = m_store.StartRequest(
                "urn:test:app",
                ByteString.From(new byte[] { 1, 2, 3 }),
                "http://opcfoundation.org/UA/SecurityPolicy#Basic256Sha256",
                default);

            Assert.That(id.IsNull, Is.False);
        }

        [Test]
        public void FinishRequestReturnsCredential()
        {
            NodeId id = m_store.StartRequest(
                "urn:test:app",
                ByteString.From(new byte[] { 1, 2 }),
                null,
                default);

            KeyCredentialRequestState state = m_store.FinishRequest(
                id,
                cancelRequest: false,
                out string? credentialId,
                out ByteString credentialSecret,
                out string? _,
                out string? _,
                out ArrayOf<NodeId> _);

            Assert.That(state, Is.EqualTo(KeyCredentialRequestState.Completed));
            Assert.That(credentialId, Is.Not.Null.And.Not.Empty);
            Assert.That(credentialSecret.IsEmpty, Is.False);
        }

        [Test]
        public void FinishRequestWithCancelRejectsRequest()
        {
            NodeId id = m_store.StartRequest(
                "urn:test:app",
                ByteString.From(new byte[] { 1 }),
                null,
                default);

            KeyCredentialRequestState state = m_store.FinishRequest(
                id,
                cancelRequest: true,
                out string? credentialId,
                out ByteString _,
                out string? _,
                out string? _,
                out ArrayOf<NodeId> _);

            Assert.That(state, Is.EqualTo(KeyCredentialRequestState.Rejected));
            Assert.That(credentialId, Is.Null);
        }

        [Test]
        public void FinishRequestWithUnknownIdThrows()
        {
            Assert.That(
                () => m_store.FinishRequest(
                    new NodeId(999),
                    cancelRequest: false,
                    out _, out _, out _, out _, out _),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public void RevokeMarksCredentialAsRejected()
        {
            NodeId id = m_store.StartRequest(
                "urn:test:app",
                ByteString.From(new byte[] { 1 }),
                null,
                default);

            m_store.FinishRequest(
                id, false,
                out string? credentialId,
                out _, out _, out _, out _);

            Assert.DoesNotThrow(() => m_store.Revoke(credentialId!));
        }

        [Test]
        public void RevokeUnknownCredentialThrows()
        {
            Assert.That(
                () => m_store.Revoke("unknown-credential"),
                Throws.TypeOf<ServiceResultException>()
                    .With.Property(nameof(ServiceResultException.StatusCode))
                    .EqualTo(StatusCodes.BadNotFound));
        }

        [Test]
        public void MultipleRequestsGetUniqueIds()
        {
            NodeId id1 = m_store.StartRequest("urn:app1", default, null, default);
            NodeId id2 = m_store.StartRequest("urn:app2", default, null, default);

            Assert.That(id1, Is.Not.EqualTo(id2));
        }
    }
}
