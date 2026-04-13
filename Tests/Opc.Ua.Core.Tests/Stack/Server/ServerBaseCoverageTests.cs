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

using System;
using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Server
{
    [TestFixture]
    [Category("ServerBaseCoverage")]
    [Parallelizable]
    public class ServerBaseCoverageTests
    {
        [Test]
        public void RequireEncryptionNullDescriptionReturnsFalse()
        {
            bool result = ServerBase.RequireEncryption(null);
            Assert.That(result, Is.False);
        }

        [Test]
        public void RequireEncryptionNonePolicyReturnsFalse()
        {
            var desc = new EndpointDescription
            {
                SecurityPolicyUri = SecurityPolicies.None,
                UserIdentityTokens =
                [
                    new UserTokenPolicy
                    {
                        TokenType = UserTokenType.Anonymous,
                        SecurityPolicyUri = SecurityPolicies.None
                    }
                ]
            };
            bool result = ServerBase.RequireEncryption(desc);
            Assert.That(result, Is.False);
        }

        [Test]
        public void RequireEncryptionBasic256Sha256ReturnsTrue()
        {
            var desc = new EndpointDescription
            {
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            };
            bool result = ServerBase.RequireEncryption(desc);
            Assert.That(result, Is.True);
        }

        [Test]
        public void RequireEncryptionAes128ReturnsTrue()
        {
            var desc = new EndpointDescription
            {
                SecurityPolicyUri = SecurityPolicies.Aes128_Sha256_RsaOaep
            };
            bool result = ServerBase.RequireEncryption(desc);
            Assert.That(result, Is.True);
        }

        [Test]
        public void RequireEncryptionAes256ReturnsTrue()
        {
            var desc = new EndpointDescription
            {
                SecurityPolicyUri = SecurityPolicies.Aes256_Sha256_RsaPss
            };
            bool result = ServerBase.RequireEncryption(desc);
            Assert.That(result, Is.True);
        }

        [Test]
        public void RequireEncryptionNonePolicyWithSecuredTokenReturnsTrue()
        {
            var desc = new EndpointDescription
            {
                SecurityPolicyUri = SecurityPolicies.None,
                UserIdentityTokens =
                [
                    new UserTokenPolicy
                    {
                        TokenType = UserTokenType.UserName,
                        SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                    }
                ]
            };
            bool result = ServerBase.RequireEncryption(desc);
            Assert.That(result, Is.True);
        }

        [Test]
        public void RequireEncryptionNonePolicyWithMultipleTokens()
        {
            // One anonymous (None) and one username with encryption
            var desc = new EndpointDescription
            {
                SecurityPolicyUri = SecurityPolicies.None,
                UserIdentityTokens =
                [
                    new UserTokenPolicy
                    {
                        TokenType = UserTokenType.Anonymous,
                        SecurityPolicyUri = SecurityPolicies.None
                    },
                    new UserTokenPolicy
                    {
                        TokenType = UserTokenType.UserName,
                        SecurityPolicyUri = SecurityPolicies.Aes256_Sha256_RsaPss
                    }
                ]
            };
            bool result = ServerBase.RequireEncryption(desc);
            Assert.That(result, Is.True);
        }

        [Test]
        public void RequireEncryptionNonePolicyAllTokensNone()
        {
            var desc = new EndpointDescription
            {
                SecurityPolicyUri = SecurityPolicies.None,
                UserIdentityTokens =
                [
                    new UserTokenPolicy
                    {
                        TokenType = UserTokenType.Anonymous,
                        SecurityPolicyUri = SecurityPolicies.None
                    }
                ]
            };
            bool result = ServerBase.RequireEncryption(desc);
            Assert.That(result, Is.False);
        }

        [Test]
        public void RequireEncryptionEmptyUserTokens()
        {
            var desc = new EndpointDescription
            {
                SecurityPolicyUri = SecurityPolicies.None,
                UserIdentityTokens = []
            };
            bool result = ServerBase.RequireEncryption(desc);
            Assert.That(result, Is.False);
        }

        [Test]
        public void EndpointDescriptionSecurityLevelDefaults()
        {
            var desc = new EndpointDescription();
            Assert.That(desc.SecurityLevel, Is.EqualTo((byte)0));
        }

        [Test]
        public void EndpointDescriptionSecurityModeDefault()
        {
            var desc = new EndpointDescription();
            Assert.That(
                desc.SecurityMode,
                Is.EqualTo(MessageSecurityMode.Invalid));
        }

        [Test]
        public void UserTokenPolicyDefaultValues()
        {
            var policy = new UserTokenPolicy();
            Assert.That(policy.PolicyId, Is.Null);
            Assert.That(
                policy.TokenType,
                Is.EqualTo(UserTokenType.Anonymous));
        }

        [Test]
        public void UserTokenPolicyWithTokenType()
        {
            var policy = new UserTokenPolicy(UserTokenType.Certificate);
            Assert.That(policy.TokenType, Is.EqualTo(UserTokenType.Certificate));
        }

        [Test]
        public void RequireEncryptionEccNistP256ReturnsTrue()
        {
            var desc = new EndpointDescription
            {
                SecurityPolicyUri = SecurityPolicies.ECC_nistP256
            };
            bool result = ServerBase.RequireEncryption(desc);
            Assert.That(result, Is.True);
        }

        [Test]
        public void RequireEncryptionEccBrainpoolReturnsTrue()
        {
            var desc = new EndpointDescription
            {
                SecurityPolicyUri = SecurityPolicies.ECC_brainpoolP256r1
            };
            bool result = ServerBase.RequireEncryption(desc);
            Assert.That(result, Is.True);
        }

        [Test]
        public void RequireEncryptionNonePolicyX509TokenWithPolicy()
        {
            var desc = new EndpointDescription
            {
                SecurityPolicyUri = SecurityPolicies.None,
                UserIdentityTokens =
                [
                    new UserTokenPolicy
                    {
                        TokenType = UserTokenType.Certificate,
                        SecurityPolicyUri = SecurityPolicies.Aes128_Sha256_RsaOaep
                    }
                ]
            };
            bool result = ServerBase.RequireEncryption(desc);
            Assert.That(result, Is.True);
        }

        [Test]
        public void EndpointDescriptionServerCertificateDefault()
        {
            var desc = new EndpointDescription();
            Assert.That(desc.ServerCertificate.IsEmpty, Is.True);
        }

        [Test]
        public void EndpointDescriptionEndpointUrlCanBeSet()
        {
            var desc = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840"
            };
            Assert.That(desc.EndpointUrl, Is.EqualTo("opc.tcp://localhost:4840"));
        }

        [Test]
        public void EndpointDescriptionTransportProfileUriCanBeSet()
        {
            var desc = new EndpointDescription
            {
                TransportProfileUri = Profiles.UaTcpTransport
            };
            Assert.That(desc.TransportProfileUri, Is.EqualTo(Profiles.UaTcpTransport));
        }

        [Test]
        public void RequireEncryptionNonePolicyIssuedTokenWithPolicy()
        {
            var desc = new EndpointDescription
            {
                SecurityPolicyUri = SecurityPolicies.None,
                UserIdentityTokens =
                [
                    new UserTokenPolicy
                    {
                        TokenType = UserTokenType.IssuedToken,
                        SecurityPolicyUri = SecurityPolicies.Basic256Sha256
                    }
                ]
            };
            bool result = ServerBase.RequireEncryption(desc);
            Assert.That(result, Is.True);
        }
    }
}
