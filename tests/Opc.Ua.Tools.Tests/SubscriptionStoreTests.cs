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
 *
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

using System;
using System.IO;
using System.Text;
using NUnit.Framework;
using Opc.Ua.Server;
using Quickstarts.Servers;

namespace Opc.Ua.Tools.Tests
{
    [TestFixture]
    public class SubscriptionStoreTests
    {
        [SetUp]
        public void SetUp()
        {
            m_telemetry = (DefaultTelemetry)DefaultTelemetry.Create(static _ => { });
            m_messageContext = ServiceMessageContext.Create(m_telemetry);
        }

        [TearDown]
        public void TearDown()
        {
            m_telemetry.Dispose();
        }

        [Test]
        public void EncodeSubscriptionRemovesUserNamePassword()
        {
            var identityToken = new UserNameIdentityToken
            {
                PolicyId = "username",
                UserName = "operator",
                Password = new ByteString(Encoding.UTF8.GetBytes("secret")),
                EncryptionAlgorithm = SecurityPolicies.Basic256Sha256
            };
            var subscription = new StoredSubscription
            {
                UserIdentityToken = identityToken
            };

            using var encoder = new BinaryEncoder(m_messageContext);
            SubscriptionStore.EncodeSubscription(encoder, subscription);
            byte[] encoded = encoder.CloseAndReturnBuffer()!;

            using var decoder = new BinaryDecoder(encoded, m_messageContext);
            StoredSubscription decoded = SubscriptionStore.DecodeSubscription(decoder);

            Assert.That(decoded.UserIdentityToken, Is.TypeOf<UserNameIdentityToken>());
            var decodedToken = (UserNameIdentityToken)decoded.UserIdentityToken!;
            Assert.That(decodedToken.PolicyId, Is.EqualTo(identityToken.PolicyId));
            Assert.That(decodedToken.UserName, Is.EqualTo(identityToken.UserName));
            Assert.That(decodedToken.Password.IsNull, Is.True);
            Assert.That(decodedToken.EncryptionAlgorithm, Is.Null);
            Assert.That(identityToken.Password.IsNull, Is.False);
        }

        [Test]
        public void EncodeSubscriptionRejectsIssuedBearerToken()
        {
            var subscription = new StoredSubscription
            {
                UserIdentityToken = new IssuedIdentityToken
                {
                    PolicyId = Profiles.JwtUserToken,
                    TokenData = new ByteString(Encoding.UTF8.GetBytes("bearer-token"))
                }
            };

            using var encoder = new BinaryEncoder(m_messageContext);

            Assert.That(
                () => SubscriptionStore.EncodeSubscription(encoder, subscription),
                Throws.TypeOf<NotSupportedException>());
        }

        [Test]
        public void StoreHeaderAcceptsCurrentVersion()
        {
            using var encoder = new BinaryEncoder(m_messageContext);
            SubscriptionStore.WriteStoreHeader(encoder);
            byte[] encoded = encoder.CloseAndReturnBuffer()!;

            using var decoder = new BinaryDecoder(encoded, m_messageContext);

            Assert.That(
                () => SubscriptionStore.ValidateStoreHeader(decoder),
                Throws.Nothing);
        }

        [Test]
        public void StoreHeaderRejectsLegacyUnsafeFormat()
        {
            using var encoder = new BinaryEncoder(m_messageContext);
            encoder.WriteStringArray(null, ["http://opcfoundation.org/UA/"]);
            byte[] encoded = encoder.CloseAndReturnBuffer()!;

            using var decoder = new BinaryDecoder(encoded, m_messageContext);

            Assert.That(
                () => SubscriptionStore.ValidateStoreHeader(decoder),
                Throws.TypeOf<InvalidDataException>());
        }

        private ServiceMessageContext m_messageContext = null!;
        private DefaultTelemetry m_telemetry = null!;
    }
}
