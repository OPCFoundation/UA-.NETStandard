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

using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Core.Tests.Stack.Configuration
{
    /// <summary>
    /// Tests for the EndpointDescription class.
    /// </summary>
    [TestFixture]
    [Category("EndpointDescription")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class EndpointDescriptionTests
    {
        [Test]
        public void FindUserTokenPolicy_EccEndpoint_RsaUserToken_ShouldSucceed()
        {
            // Arrange - Create an endpoint with ECC security
            var endpoint = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.ECC_nistP256
            };

            // Add both RSA and ECC user token policies
            endpoint.UserIdentityTokens.Add(new UserTokenPolicy
            {
                PolicyId = "RSAUserToken",
                TokenType = UserTokenType.UserName,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            });

            endpoint.UserIdentityTokens.Add(new UserTokenPolicy
            {
                PolicyId = "ECCUserToken",
                TokenType = UserTokenType.UserName,
                SecurityPolicyUri = SecurityPolicies.ECC_nistP256
            });

            // Act - Try to find RSA user token policy on ECC endpoint
            UserTokenPolicy foundPolicy = endpoint.FindUserTokenPolicy(
                "RSAUserToken",
                SecurityPolicies.Basic256Sha256);

            // Assert - Should find the RSA policy despite endpoint being ECC
            Assert.NotNull(foundPolicy, "Should find RSA user token policy on ECC endpoint");
            Assert.AreEqual("RSAUserToken", foundPolicy.PolicyId);
            Assert.AreEqual(SecurityPolicies.Basic256Sha256, foundPolicy.SecurityPolicyUri);
        }

        [Test]
        public void FindUserTokenPolicy_RsaEndpoint_EccUserToken_ShouldSucceed()
        {
            // Arrange - Create an endpoint with RSA security
            var endpoint = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            };

            // Add both RSA and ECC user token policies
            endpoint.UserIdentityTokens.Add(new UserTokenPolicy
            {
                PolicyId = "RSAUserToken",
                TokenType = UserTokenType.UserName,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            });

            endpoint.UserIdentityTokens.Add(new UserTokenPolicy
            {
                PolicyId = "ECCUserToken",
                TokenType = UserTokenType.UserName,
                SecurityPolicyUri = SecurityPolicies.ECC_nistP256
            });

            // Act - Try to find ECC user token policy on RSA endpoint
            UserTokenPolicy foundPolicy = endpoint.FindUserTokenPolicy(
                "ECCUserToken",
                SecurityPolicies.ECC_nistP256);

            // Assert - Should find the ECC policy despite endpoint being RSA
            Assert.NotNull(foundPolicy, "Should find ECC user token policy on RSA endpoint");
            Assert.AreEqual("ECCUserToken", foundPolicy.PolicyId);
            Assert.AreEqual(SecurityPolicies.ECC_nistP256, foundPolicy.SecurityPolicyUri);
        }

        [Test]
        public void FindUserTokenPolicy_ByTokenType_EccEndpoint_RsaUserToken_ShouldSucceed()
        {
            // Arrange - Create an endpoint with ECC security
            var endpoint = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.ECC_nistP256
            };

            // Add RSA user token policy
            endpoint.UserIdentityTokens.Add(new UserTokenPolicy
            {
                PolicyId = "UserToken1",
                TokenType = UserTokenType.UserName,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            });

            // Act - Try to find user token policy by type with RSA security
            UserTokenPolicy foundPolicy = endpoint.FindUserTokenPolicy(
                UserTokenType.UserName,
                (string)null,
                SecurityPolicies.Basic256Sha256);

            // Assert - Should find the RSA policy despite endpoint being ECC
            Assert.NotNull(foundPolicy, "Should find RSA user token policy by type on ECC endpoint");
            Assert.AreEqual(UserTokenType.UserName, foundPolicy.TokenType);
            Assert.AreEqual(SecurityPolicies.Basic256Sha256, foundPolicy.SecurityPolicyUri);
        }

        [Test]
        public void FindUserTokenPolicy_PreferExactMatch()
        {
            // Arrange - Create an endpoint
            var endpoint = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            };

            // Add multiple policies with same PolicyId but different security
            endpoint.UserIdentityTokens.Add(new UserTokenPolicy
            {
                PolicyId = "UserToken1",
                TokenType = UserTokenType.UserName,
                SecurityPolicyUri = SecurityPolicies.Aes128_Sha256_RsaOaep
            });

            endpoint.UserIdentityTokens.Add(new UserTokenPolicy
            {
                PolicyId = "UserToken1",
                TokenType = UserTokenType.UserName,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            });

            // Act - Request exact match
            UserTokenPolicy foundPolicy = endpoint.FindUserTokenPolicy(
                "UserToken1",
                SecurityPolicies.Basic256Sha256);

            // Assert - Should prefer exact match
            Assert.NotNull(foundPolicy);
            Assert.AreEqual(SecurityPolicies.Basic256Sha256, foundPolicy.SecurityPolicyUri);
        }

        [Test]
        public void FindUserTokenPolicy_FallbackToAnyMatch()
        {
            // Arrange - Create an endpoint
            var endpoint = new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.ECC_nistP256
            };

            // Add only RSA policy
            endpoint.UserIdentityTokens.Add(new UserTokenPolicy
            {
                PolicyId = "UserToken1",
                TokenType = UserTokenType.UserName,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256
            });

            // Act - Request with ECC but only RSA available
            UserTokenPolicy foundPolicy = endpoint.FindUserTokenPolicy(
                "UserToken1",
                SecurityPolicies.ECC_nistP256);

            // Assert - Should fallback to any matching PolicyId
            Assert.NotNull(foundPolicy, "Should fallback to RSA policy when ECC not available");
            Assert.AreEqual("UserToken1", foundPolicy.PolicyId);
            Assert.AreEqual(SecurityPolicies.Basic256Sha256, foundPolicy.SecurityPolicyUri);
        }
    }
}
