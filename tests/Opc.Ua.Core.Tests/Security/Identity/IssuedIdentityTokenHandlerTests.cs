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

using System;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security.Identity
{
    [TestFixture]
    [Category("Identity")]
    [Parallelizable]
    public sealed class IssuedIdentityTokenHandlerTests
    {
        [TestCase(Namespaces.OpcUa + "UserToken#GenericWSS", IssuedTokenType.GenericWSS, "Generic WSS Token")]
        [TestCase(Namespaces.OpcUa + "UserToken#SAML", IssuedTokenType.SAML, "SAML Token")]
        [TestCase(Profiles.JwtUserToken, IssuedTokenType.JWT, "JWT")]
        [TestCase(Namespaces.OpcUa + "UserToken#KerberosBinary", IssuedTokenType.KerberosBinary, "Kerberos Token")]
        [TestCase("urn:custom", IssuedTokenType.Unknown, "Issued Token")]
        public void IssuedTokenTypeAndDisplayNameFollowProfileUri(
            string profileUri,
            IssuedTokenType expectedType,
            string expectedDisplayName)
        {
            var handler = new IssuedIdentityTokenHandler(profileUri, [1, 2, 3]);

            Assert.That(handler.TokenType, Is.EqualTo(UserTokenType.IssuedToken));
            Assert.That(handler.IssuedTokenType, Is.EqualTo(expectedType));
            Assert.That(handler.DisplayName, Is.EqualTo(expectedDisplayName));
            Assert.That(handler.IssuedTokenTypeProfileUri, Is.EqualTo(profileUri));
        }

        [Test]
        public void ConstructorDefaultsPolicyIdToJwtProfile()
        {
            var token = new IssuedIdentityToken();

            var handler = new IssuedIdentityTokenHandler(token);

            Assert.That(handler.IssuedTokenType, Is.EqualTo(IssuedTokenType.JWT));
            Assert.That(token.PolicyId, Is.EqualTo(Profiles.JwtUserToken));
        }

        [Test]
        public void DecryptedTokenDataUsesDefensiveCopiesAndClearsPreviousValue()
        {
            byte[] original = [1, 2, 3];
            var handler = new IssuedIdentityTokenHandler(Profiles.JwtUserToken, original);

            byte[] firstCopy = handler.DecryptedTokenData;
            firstCopy[0] = 99;
            handler.DecryptedTokenData = [4, 5];

            Assert.That(handler.DecryptedTokenData, Is.EqualTo(new byte[] { 4, 5 }));
            Assert.That(firstCopy, Is.EqualTo(new byte[] { 99, 2, 3 }));
            handler.DecryptedTokenData = null;
            Assert.That(handler.DecryptedTokenData, Is.Null);
        }

        [Test]
        public async Task EncryptAndDecryptWithSecurityNoneRoundTripsTokenDataAsync()
        {
            var handler = new IssuedIdentityTokenHandler(Profiles.JwtUserToken, [0x10, 0x20, 0x30]);
            IServiceMessageContext context = ServiceMessageContext.Create(NUnitTelemetryContext.Create());

            await handler.EncryptAsync(null, [], SecurityPolicies.None, context).ConfigureAwait(false);
            Assert.That(((IssuedIdentityToken)handler.Token).EncryptionAlgorithm, Is.EqualTo(string.Empty));
            Assert.That(((IssuedIdentityToken)handler.Token).TokenData.ToArray(), Is.EqualTo(new byte[] { 0x10, 0x20, 0x30 }));

            var encryptedToken = new IssuedIdentityToken
            {
                PolicyId = Profiles.JwtUserToken,
                TokenData = new byte[] { 0x10, 0x20, 0x30 }.ToByteString()
            };
            handler = new IssuedIdentityTokenHandler(encryptedToken);
            await handler.DecryptAsync(null, null, SecurityPolicies.None, context).ConfigureAwait(false);

            Assert.That(handler.DecryptedTokenData, Is.EqualTo(new byte[] { 0x10, 0x20, 0x30 }));
        }

        [Test]
        public void UpdatePolicyChangesPolicyAndProfile()
        {
            var handler = new IssuedIdentityTokenHandler(Profiles.JwtUserToken, [1]);
            var policy = new UserTokenPolicy
            {
                PolicyId = "issued",
                IssuedTokenType = Namespaces.OpcUa + "UserToken#SAML"
            };

            handler.UpdatePolicy(policy);

            Assert.That(((IssuedIdentityToken)handler.Token).PolicyId, Is.EqualTo("issued"));
            Assert.That(handler.IssuedTokenTypeProfileUri, Is.EqualTo(policy.IssuedTokenType));
            Assert.That(handler.IssuedTokenType, Is.EqualTo(IssuedTokenType.SAML));
        }

        [Test]
        public async Task SignAndVerifyAreNoOpsForIssuedTokensAsync()
        {
            var handler = new IssuedIdentityTokenHandler(Profiles.JwtUserToken, [1]);

            SignatureData signature = await handler
                .SignAsync([1, 2, 3], SecurityPolicies.None)
                .ConfigureAwait(false);
            bool verified = await handler
                .VerifyAsync([1, 2, 3], signature, SecurityPolicies.None)
                .ConfigureAwait(false);

            Assert.That(signature, Is.Null);
            Assert.That(verified, Is.True);
        }

        [Test]
        public void CloneCopiesTokenAndEqualsComparesTokenData()
        {
            var handler = new IssuedIdentityTokenHandler(Profiles.JwtUserToken, [7, 8, 9]);
            handler.DecryptedTokenData = [7, 8, 9];

            var clone = (IssuedIdentityTokenHandler)handler.Clone();

            Assert.That(clone, Is.Not.SameAs(handler));
            Assert.That(clone.IssuedTokenTypeProfileUri, Is.EqualTo(handler.IssuedTokenTypeProfileUri));
            Assert.That(clone.DecryptedTokenData, Is.EqualTo(handler.DecryptedTokenData));
            bool equalsClone = handler.Equals(clone);
            bool equalsOtherHandler = handler.Equals(new UserNameIdentityTokenHandler("user", [1]));
            Assert.That(equalsClone, Is.True);
            Assert.That(equalsOtherHandler, Is.False);
        }
    }
}
