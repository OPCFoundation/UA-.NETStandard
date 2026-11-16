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

#pragma warning disable CA2000
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.Types
{
    [TestFixture]
    [Category("IssuedIdentityTokenHandler")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class IssuedIdentityTokenHandlerTests
    {
        private const string kSecurityPolicyUri = SecurityPolicies.Basic256Sha256;

        /// <summary>
        /// Part 4 §7.40.6: an encrypted IssuedIdentityToken uses the EncryptedSecret
        /// format with an empty encryptionAlgorithm.
        /// </summary>
        [Test]
        public async Task DecryptSupportsRsaEncryptedSecretFormatAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = ServiceMessageContext.CreateEmpty(telemetry);
            SecurityPolicyInfo securityPolicy = SecurityPolicies.Default.GetInfo(kSecurityPolicyUri)!;
            byte[] receiverNonce = Nonce.CreateNonce(securityPolicy.SecureChannelNonceLength).Data!;
            byte[] expectedToken = Encoding.UTF8.GetBytes("eyJhbGciOiJub25lIn0.eyJzdWIiOiJ0ZXN0In0.");

            using Certificate certificate = CertificateBuilder
                .Create("CN=Issued Identity Token Test Subject, O=OPC Foundation")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            byte[] encryptedSecret = UserNameIdentityTokenHandlerTests.CreateRsaEncryptedSecret(
                context,
                certificate,
                kSecurityPolicyUri,
                expectedToken,
                receiverNonce);

            var token = new IssuedIdentityToken
            {
                TokenData = encryptedSecret.ToByteString(),
                EncryptionAlgorithm = null
            };

            var tokenHandler = new IssuedIdentityTokenHandler(token);
            await tokenHandler.DecryptAsync(
                certificate,
                Nonce.CreateNonce(securityPolicy, receiverNonce),
                kSecurityPolicyUri,
                context).ConfigureAwait(false);

            Assert.That(tokenHandler.DecryptedTokenData, Is.EqualTo(expectedToken));
        }

        [Test]
        public async Task DecryptKeepsLegacyRsaEncryptedTokenPathAsync()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var context = ServiceMessageContext.CreateEmpty(telemetry);
            SecurityPolicyInfo securityPolicy = SecurityPolicies.Default.GetInfo(kSecurityPolicyUri)!;
            byte[] receiverNonce = Nonce.CreateNonce(securityPolicy.SecureChannelNonceLength).Data!;
            byte[] expectedToken = Encoding.UTF8.GetBytes("legacy-issued-token");

            using Certificate certificate = CertificateBuilder
                .Create("CN=Issued Identity Token Legacy Test Subject, O=OPC Foundation")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            EncryptedData encryptedData = SecurityPolicies.Default.Encrypt(
                certificate,
                kSecurityPolicyUri,
                Utils.Append(expectedToken, receiverNonce));

            var token = new IssuedIdentityToken
            {
                TokenData = encryptedData.Data.ToByteString(),
                EncryptionAlgorithm = encryptedData.Algorithm
            };

            var tokenHandler = new IssuedIdentityTokenHandler(token);
            await tokenHandler.DecryptAsync(
                certificate,
                Nonce.CreateNonce(securityPolicy, receiverNonce),
                kSecurityPolicyUri,
                context).ConfigureAwait(false);

            Assert.That(tokenHandler.DecryptedTokenData, Is.EqualTo(expectedToken));
        }
    }
}
