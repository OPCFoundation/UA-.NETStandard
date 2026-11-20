/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
