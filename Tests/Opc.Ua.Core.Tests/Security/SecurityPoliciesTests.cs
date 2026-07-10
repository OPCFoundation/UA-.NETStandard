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

// CA2000: certificates are disposed by using declarations in each test.
#pragma warning disable CA2000
using System;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Security
{
    [TestFixture]
    [Category("Security")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class SecurityPoliciesTests
    {
        [Test]
        public void LookupHelpersHandleKnownShortFullAndUnknownPolicies()
        {
            SecurityPolicyInfo none = SecurityPolicies.GetInfo(null);
            Assert.That(none, Is.SameAs(SecurityPolicyInfo.None));
            Assert.That(SecurityPolicies.GetInfo(string.Empty), Is.SameAs(SecurityPolicyInfo.None));

            SecurityPolicyInfo basicByUri = SecurityPolicies.GetInfo(SecurityPolicies.Basic256Sha256);
            SecurityPolicyInfo basicByName = SecurityPolicies.GetInfo(nameof(SecurityPolicies.Basic256Sha256));
            Assert.That(basicByUri, Is.Not.Null);
            Assert.That(basicByName, Is.SameAs(basicByUri));
            Assert.That(SecurityPolicies.GetUri(nameof(SecurityPolicies.Basic256Sha256)), Is.EqualTo(SecurityPolicies.Basic256Sha256));
            Assert.That(SecurityPolicies.GetDisplayName(SecurityPolicies.Basic256Sha256), Is.EqualTo(nameof(SecurityPolicies.Basic256Sha256)));
            Assert.That(SecurityPolicies.IsValidSecurityPolicyUri(SecurityPolicies.Basic256Sha256), Is.True);

            Assert.That(SecurityPolicies.GetInfo("UnknownPolicy"), Is.Null);
            Assert.That(SecurityPolicies.GetUri("UnknownPolicy"), Is.Null);
            Assert.That(SecurityPolicies.GetDisplayName("urn:unknown"), Is.Null);
            Assert.That(SecurityPolicies.IsValidSecurityPolicyUri("urn:unknown"), Is.False);
            Assert.That(SecurityPolicies.GetDisplayNames(), Does.Contain(nameof(SecurityPolicies.Basic256Sha256)));
            Assert.That(SecurityPolicies.GetDefaultDeprecatedUris(), Does.Contain(SecurityPolicies.Basic256));
            Assert.That(SecurityPolicies.GetDefaultUris(), Does.Contain(SecurityPolicies.Basic256Sha256));
        }

        [Test]
        public void EmptyPolicyEncryptDecryptAndSignAreNoOps()
        {
            ILogger logger = NUnitTelemetryContext.Create().CreateLogger<SecurityPoliciesTests>();
            byte[] plainText = [1, 2, 3];

            EncryptedData encrypted = SecurityPolicies.Encrypt(null, string.Empty, plainText, logger);
            Assert.That(encrypted.Algorithm, Is.Null);
            Assert.That(encrypted.Data, Is.EqualTo(plainText));
            Assert.That(SecurityPolicies.Decrypt(null, string.Empty, encrypted, logger), Is.EqualTo(plainText));
            Assert.That(SecurityPolicies.Decrypt(null, string.Empty, null, logger), Is.Null);

            using Certificate certificate = CertificateBuilder
                .Create("CN=SecurityPolicies NoOp")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            SignatureData emptyPolicySignature = SecurityPolicies.CreateSignatureData(
                string.Empty,
                certificate,
                null,
                null,
                null,
                null,
                null,
                null);
            Assert.That(emptyPolicySignature.Algorithm, Is.Null);
            Assert.That(SecurityPolicies.VerifySignatureData(null, string.Empty, certificate, null, null, null, null, null, null), Is.True);

            SignatureData noneSignature = SecurityPolicies.CreateSignatureData(
                SecurityPolicyInfo.None,
                certificate,
                plainText);
            Assert.That(noneSignature.Algorithm, Is.Null);
            Assert.That(noneSignature.Signature.IsNull, Is.True);
            Assert.That(SecurityPolicies.VerifySignatureData(noneSignature, SecurityPolicyInfo.None, certificate, plainText), Is.True);
        }

        [Test]
        public void UnsupportedPoliciesThrowExpectedServiceResultExceptions()
        {
            ILogger logger = NUnitTelemetryContext.Create().CreateLogger<SecurityPoliciesTests>();
            var encrypted = new EncryptedData { Algorithm = "unknown", Data = [1] };

            ServiceResultException encryptException = Assert.Throws<ServiceResultException>(
                () => SecurityPolicies.Encrypt(null, "UnknownPolicy", [1], logger));
            Assert.That(encryptException.StatusCode, Is.EqualTo(StatusCodes.BadSecurityPolicyRejected));

            ServiceResultException decryptException = Assert.Throws<ServiceResultException>(
                () => SecurityPolicies.Decrypt(null, "UnknownPolicy", encrypted, logger));
            Assert.That(decryptException.StatusCode, Is.EqualTo(StatusCodes.BadSecurityPolicyRejected));

            using Certificate certificate = CertificateBuilder
                .Create("CN=SecurityPolicies Unsupported")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            ServiceResultException createException = Assert.Throws<ServiceResultException>(
                () => SecurityPolicies.CreateSignatureData("UnknownPolicy", certificate, [1]));
            Assert.That(createException.StatusCode, Is.EqualTo(StatusCodes.BadSecurityPolicyRejected));

            ServiceResultException verifyException = Assert.Throws<ServiceResultException>(
                () => SecurityPolicies.VerifySignatureData(new SignatureData(), "UnknownPolicy", certificate, [1]));
            Assert.That(verifyException.StatusCode, Is.EqualTo(StatusCodes.BadSecurityPolicyRejected));
        }

        [TestCase(SecurityPolicies.Basic128Rsa15, SecurityAlgorithms.Rsa15)]
        [TestCase(SecurityPolicies.Basic256, SecurityAlgorithms.RsaOaep)]
        [TestCase(SecurityPolicies.Aes128_Sha256_RsaOaep, SecurityAlgorithms.RsaOaep)]
        [TestCase(SecurityPolicies.Aes256_Sha256_RsaPss, SecurityAlgorithms.RsaOaepSha256)]
        public void RsaEncryptDecryptRoundTripsForSupportedPolicies(string policyUri, string expectedAlgorithm)
        {
            if (SecurityPolicies.GetInfo(policyUri) == null)
            {
                Assert.Ignore("Policy is not supported by this platform.");
            }

            ILogger logger = NUnitTelemetryContext.Create().CreateLogger<SecurityPoliciesTests>();
            using Certificate certificate = CertificateBuilder
                .Create("CN=SecurityPolicies Encrypt")
                .SetRSAKeySize(2048)
                .CreateForRSA();
            byte[] plainText = [10, 20, 30, 40];

            EncryptedData encrypted = SecurityPolicies.Encrypt(certificate, policyUri, plainText, logger);
            Assert.That(encrypted.Algorithm, Is.EqualTo(expectedAlgorithm));
            Assert.That(encrypted.Data, Is.Not.EqualTo(plainText));

            byte[] decrypted = SecurityPolicies.Decrypt(certificate, policyUri, encrypted, logger);
            Assert.That(decrypted, Is.EqualTo(plainText));
        }

        [TestCase(SecurityPolicies.Basic128Rsa15)]
        [TestCase(SecurityPolicies.Basic256Sha256)]
        [TestCase(SecurityPolicies.Aes256_Sha256_RsaPss)]
        public void SignatureDataRoundTripsForSupportedRsaPolicies(string policyUri)
        {
            if (SecurityPolicies.GetInfo(policyUri) == null)
            {
                Assert.Ignore("Policy is not supported by this platform.");
            }

            using Certificate certificate = CertificateBuilder
                .Create("CN=SecurityPolicies Signature")
                .SetRSAKeySize(2048)
                .CreateForRSA();
            byte[] data = [1, 3, 5, 7, 9];

            SignatureData signature = SecurityPolicies.CreateSignatureData(policyUri, certificate, data);
            Assert.That(signature.Algorithm, Is.Not.Null);
            Assert.That(signature.Signature.IsNull, Is.False);
            Assert.That(SecurityPolicies.VerifySignatureData(signature, policyUri, certificate, data), Is.True);

            ServiceResultException sre = Assert.Throws<ServiceResultException>(
                () => SecurityPolicies.VerifySignatureData(
                    new SignatureData { Algorithm = "unexpected", Signature = signature.Signature },
                    policyUri,
                    certificate,
                    data));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [Test]
        public void EnhancedSignatureDataUsesAllChannelInputsWhenPolicySupportsIt()
        {
            if (SecurityPolicies.GetInfo(SecurityPolicies.RSA_DH_AesGcm) == null)
            {
                Assert.Ignore("Policy is not supported by this platform.");
            }

            using Certificate certificate = CertificateBuilder
                .Create("CN=SecurityPolicies Enhanced")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            SignatureData signature = SecurityPolicies.CreateSignatureData(
                SecurityPolicies.RSA_DH_AesGcm,
                certificate,
                [1],
                [2],
                [3],
                [4],
                [5],
                [6]);

            Assert.That(signature.Signature.IsNull, Is.False);
            Assert.That(
                SecurityPolicies.VerifySignatureData(
                    signature,
                    SecurityPolicies.RSA_DH_AesGcm,
                    certificate,
                    [1],
                    [2],
                    [3],
                    [4],
                    [5],
                    [6]),
                Is.True);
        }
    }
}
