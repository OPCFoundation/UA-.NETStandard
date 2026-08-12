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
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
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
    [NonParallelizable]
    public class SecurityPoliciesTests
    {
        [Test]
        public void LookupHelpersHandleKnownShortFullAndUnknownPolicies()
        {
            SecurityPolicyInfo none = SecurityPolicyRegistry.Default.GetInfo(null);
            Assert.That(none, Is.SameAs(SecurityPolicyInfo.None));
            Assert.That(SecurityPolicyRegistry.Default.GetInfo(string.Empty), Is.SameAs(SecurityPolicyInfo.None));

            SecurityPolicyInfo basicByUri = SecurityPolicyRegistry.Default.GetInfo(SecurityPolicies.Basic256Sha256);
            SecurityPolicyInfo basicByName = SecurityPolicyRegistry.Default.GetInfo(nameof(SecurityPolicies.Basic256Sha256));
            Assert.That(basicByUri, Is.Not.Null);
            Assert.That(basicByName, Is.SameAs(basicByUri));
            Assert.That(SecurityPolicyRegistry.Default.GetUri(nameof(SecurityPolicies.Basic256Sha256)), Is.EqualTo(SecurityPolicies.Basic256Sha256));
            Assert.That(SecurityPolicyRegistry.Default.GetDisplayName(SecurityPolicies.Basic256Sha256), Is.EqualTo(nameof(SecurityPolicies.Basic256Sha256)));
            Assert.That(SecurityPolicyRegistry.Default.IsValidSecurityPolicyUri(SecurityPolicies.Basic256Sha256), Is.True);

            Assert.That(SecurityPolicyRegistry.Default.GetInfo("UnknownPolicy"), Is.Null);
            Assert.That(SecurityPolicyRegistry.Default.GetUri("UnknownPolicy"), Is.Null);
            Assert.That(SecurityPolicyRegistry.Default.GetDisplayName("urn:unknown"), Is.Null);
            Assert.That(SecurityPolicyRegistry.Default.IsValidSecurityPolicyUri("urn:unknown"), Is.False);
            Assert.That(SecurityPolicyRegistry.Default.GetDisplayNames(), Does.Contain(nameof(SecurityPolicies.Basic256Sha256)));
            Assert.That(SecurityPolicyRegistry.Default.GetDefaultDeprecatedUris(), Does.Contain(SecurityPolicies.Basic256));
            Assert.That(SecurityPolicyRegistry.Default.GetDefaultUris(), Does.Contain(SecurityPolicies.Basic256Sha256));
        }

        [Test]
        public void RegisterRejectsNullAndDuplicatePolicies()
        {
            Assert.That(
                () => SecurityPolicyRegistry.Default.Register(null!),
                Throws.TypeOf<ArgumentNullException>().With.Property("ParamName").EqualTo("securityPolicy"));

            var policy = new SecurityPolicyInfo(SecurityPolicies.BaseUri + "TestDuplicatePolicy")
            {
                PlatformSupport = () => true
            };

            using IDisposable registration = SecurityPolicyRegistry.Default.Register(policy);

            Assert.That(
                () => SecurityPolicyRegistry.Default.Register(new SecurityPolicyInfo(policy.Uri, "OtherName")),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => SecurityPolicyRegistry.Default.Register(new SecurityPolicyInfo(SecurityPolicies.BaseUri + "OtherPolicy", policy.Name)),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => SecurityPolicyRegistry.Default.Register(new SecurityPolicyInfo(SecurityPolicyInfo.Basic256Sha256)),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void RegisterLightsUpCurvePoliciesFromOutsideCore()
        {
            using IDisposable curve25519 = SecurityPolicyRegistry.Default.Register(
                WithPlatformSupport(SecurityPolicyInfo.ECC_curve25519, isDefaultEcc: true),
                replaceExisting: true);
            using IDisposable curve25519AesGcm = SecurityPolicyRegistry.Default.Register(
                WithPlatformSupport(SecurityPolicyInfo.ECC_curve25519_AesGcm, isDefaultEcc: false),
                replaceExisting: true);
            using IDisposable curve25519ChaChaPoly = SecurityPolicyRegistry.Default.Register(
                WithPlatformSupport(SecurityPolicyInfo.ECC_curve25519_ChaChaPoly, isDefaultEcc: false),
                replaceExisting: true);
            using IDisposable curve448 = SecurityPolicyRegistry.Default.Register(
                WithPlatformSupport(SecurityPolicyInfo.ECC_curve448, isDefaultEcc: true),
                replaceExisting: true);
            using IDisposable curve448AesGcm = SecurityPolicyRegistry.Default.Register(
                WithPlatformSupport(SecurityPolicyInfo.ECC_curve448_AesGcm, isDefaultEcc: false),
                replaceExisting: true);
            using IDisposable curve448ChaChaPoly = SecurityPolicyRegistry.Default.Register(
                WithPlatformSupport(SecurityPolicyInfo.ECC_curve448_ChaChaPoly, isDefaultEcc: false),
                replaceExisting: true);

            string[] policyUris =
            [
                SecurityPolicies.ECC_curve25519,
                SecurityPolicies.ECC_curve25519_AesGcm,
                SecurityPolicies.ECC_curve25519_ChaChaPoly,
                SecurityPolicies.ECC_curve448,
                SecurityPolicies.ECC_curve448_AesGcm,
                SecurityPolicies.ECC_curve448_ChaChaPoly
            ];

            Assert.Multiple(() =>
            {
                foreach (string policyUri in policyUris)
                {
                    SecurityPolicyInfo info = SecurityPolicyRegistry.Default.GetInfo(policyUri);
                    Assert.That(info, Is.Not.Null, policyUri);
                    Assert.That(SecurityPolicyRegistry.Default.GetUri(info.Name), Is.EqualTo(policyUri), policyUri);
                    Assert.That(SecurityPolicyRegistry.Default.GetDisplayName(policyUri), Is.EqualTo(info.Name), policyUri);
                    Assert.That(SecurityPolicyRegistry.Default.GetDisplayNames(), Does.Contain(info.Name), policyUri);
                }

                Assert.That(SecurityPolicyRegistry.Default.GetDefaultEccUris(), Does.Contain(SecurityPolicies.ECC_curve25519));
                Assert.That(SecurityPolicyRegistry.Default.GetDefaultEccUris(), Does.Contain(SecurityPolicies.ECC_curve448));
                Assert.That(
                    CertificateIdentifier.MapSecurityPolicyToCertificateTypes(SecurityPolicies.ECC_curve25519),
                    Does.Contain(ObjectTypeIds.EccCurve25519ApplicationCertificateType));
                Assert.That(
                    CertificateIdentifier.MapSecurityPolicyToCertificateTypes(SecurityPolicies.ECC_curve448),
                    Does.Contain(ObjectTypeIds.EccCurve448ApplicationCertificateType));
                Assert.That(
                    CryptoUtils.GetCurveFromCertificateTypeId(ObjectTypeIds.EccCurve25519ApplicationCertificateType),
                    Is.Not.Null);
                Assert.That(
                    CryptoUtils.GetCurveFromCertificateTypeId(ObjectTypeIds.EccCurve448ApplicationCertificateType),
                    Is.Not.Null);
            });
        }

        /// <summary>
        /// A policy contributed through the container reaches the registry that
        /// container owns, and deliberately does not reach anyone else's.
        /// </summary>
        /// <remarks>
        /// Before the policy set became an object this registered into
        /// process-wide state, so one application's policy was visible to every
        /// other application in the process.
        /// </remarks>
        [Test]
        public void AddSecurityPolicyRegistersPoliciesThroughDependencyInjection()
        {
            var policy = new SecurityPolicyInfo(SecurityPolicies.BaseUri + "DependencyInjectionPolicy")
            {
                PlatformSupport = () => true,
                IsDefault = true,
                SupportedCertificateTypes = [ObjectTypeIds.RsaSha256ApplicationCertificateType]
            };

            var services = new ServiceCollection();
            services.AddOpcUa().AddSecurityPolicy(policy);

            using (ServiceProvider provider = services.BuildServiceProvider())
            {
                var registry = provider.GetRequiredService<ISecurityPolicyRegistry>();

                Assert.That(registry, Is.Not.Null);
                Assert.That(registry.GetInfo(policy.Uri), Is.SameAs(policy));
                Assert.That(registry.GetDefaultUris(), Does.Contain(policy.Uri));

                // The container's registry owns the policy; the fallback does not.
                Assert.That(SecurityPolicyRegistry.Default.GetInfo(policy.Uri), Is.Null);
            }

            Assert.That(SecurityPolicyRegistry.Default.GetInfo(policy.Uri), Is.Null);
        }

        [TestCaseSource(nameof(SecurityConfigurationSupportedPolicyCases))]
        public void SecurityConfigurationBuildsExpectedSupportedPolicySet(
            bool hasCertificateType,
            NodeId certificateType,
            string[] expectedPolicyUris)
        {
            SecurityConfiguration securityConfiguration = hasCertificateType
                ? new SecurityConfiguration
                {
                    ApplicationCertificates =
                    [
                        CreateCertificateIdentifier(certificateType)
                    ]
                }
                : new SecurityConfiguration
                {
                    ApplicationCertificate = new CertificateIdentifier()
                };

            Assert.That(securityConfiguration.SupportedSecurityPolicies, Is.EqualTo(SupportedOnly(expectedPolicyUris)));
        }

        [Test]
        public void EmptyPolicyEncryptDecryptAndSignAreNoOps()
        {
            ILogger logger = NUnitTelemetryContext.Create().CreateLogger<SecurityPoliciesTests>();
            byte[] plainText = [1, 2, 3];

            EncryptedData encrypted = SecurityPolicyRegistry.Default.Encrypt(null, string.Empty, plainText);
            Assert.That(encrypted.Algorithm, Is.Null);
            Assert.That(encrypted.Data, Is.EqualTo(plainText));
            Assert.That(SecurityPolicyRegistry.Default.Decrypt(null, string.Empty, encrypted), Is.EqualTo(plainText));
            Assert.That(SecurityPolicyRegistry.Default.Decrypt(null, string.Empty, null), Is.Null);

            using Certificate certificate = CertificateBuilder
                .Create("CN=SecurityPolicies NoOp")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            SignatureData emptyPolicySignature = SecurityPolicyRegistry.Default.CreateSignatureData(
                string.Empty,
                certificate,
                null,
                null,
                null,
                null,
                null,
                null);
            Assert.That(emptyPolicySignature.Algorithm, Is.Null);
            Assert.That(SecurityPolicyRegistry.Default.VerifySignatureData(null, string.Empty, certificate, null, null, null, null, null, null), Is.True);

            SignatureData noneSignature = SecurityPolicyRegistry.Default.CreateSignatureData(
                SecurityPolicyInfo.None,
                certificate,
                plainText);
            Assert.That(noneSignature.Algorithm, Is.Null);
            Assert.That(noneSignature.Signature.IsNull, Is.True);
            Assert.That(SecurityPolicyRegistry.Default.VerifySignatureData(noneSignature, SecurityPolicyInfo.None, certificate, plainText), Is.True);
        }

        [Test]
        public void UnsupportedPoliciesThrowExpectedServiceResultExceptions()
        {
            ILogger logger = NUnitTelemetryContext.Create().CreateLogger<SecurityPoliciesTests>();
            var encrypted = new EncryptedData { Algorithm = "unknown", Data = [1] };

            ServiceResultException encryptException = Assert.Throws<ServiceResultException>(
                () => SecurityPolicyRegistry.Default.Encrypt(null, "UnknownPolicy", [1]));
            Assert.That(encryptException.StatusCode, Is.EqualTo(StatusCodes.BadSecurityPolicyRejected));

            ServiceResultException decryptException = Assert.Throws<ServiceResultException>(
                () => SecurityPolicyRegistry.Default.Decrypt(null, "UnknownPolicy", encrypted));
            Assert.That(decryptException.StatusCode, Is.EqualTo(StatusCodes.BadSecurityPolicyRejected));

            using Certificate certificate = CertificateBuilder
                .Create("CN=SecurityPolicies Unsupported")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            ServiceResultException createException = Assert.Throws<ServiceResultException>(
                () => SecurityPolicyRegistry.Default.CreateSignatureData("UnknownPolicy", certificate, [1]));
            Assert.That(createException.StatusCode, Is.EqualTo(StatusCodes.BadSecurityPolicyRejected));

            ServiceResultException verifyException = Assert.Throws<ServiceResultException>(
                () => SecurityPolicyRegistry.Default.VerifySignatureData(new SignatureData(), "UnknownPolicy", certificate, [1]));
            Assert.That(verifyException.StatusCode, Is.EqualTo(StatusCodes.BadSecurityPolicyRejected));
        }

        [TestCase(SecurityPolicies.Basic128Rsa15, SecurityAlgorithms.Rsa15)]
        [TestCase(SecurityPolicies.Basic256, SecurityAlgorithms.RsaOaep)]
        [TestCase(SecurityPolicies.Aes128_Sha256_RsaOaep, SecurityAlgorithms.RsaOaep)]
        [TestCase(SecurityPolicies.Aes256_Sha256_RsaPss, SecurityAlgorithms.RsaOaepSha256)]
        public void RsaEncryptDecryptRoundTripsForSupportedPolicies(string policyUri, string expectedAlgorithm)
        {
            if (SecurityPolicyRegistry.Default.GetInfo(policyUri) == null)
            {
                Assert.Ignore("Policy is not supported by this platform.");
            }

            ILogger logger = NUnitTelemetryContext.Create().CreateLogger<SecurityPoliciesTests>();
            using Certificate certificate = CertificateBuilder
                .Create("CN=SecurityPolicies Encrypt")
                .SetRSAKeySize(2048)
                .CreateForRSA();
            byte[] plainText = [10, 20, 30, 40];

            EncryptedData encrypted = SecurityPolicyRegistry.Default.Encrypt(certificate, policyUri, plainText);
            Assert.That(encrypted.Algorithm, Is.EqualTo(expectedAlgorithm));
            Assert.That(encrypted.Data, Is.Not.EqualTo(plainText));

            byte[] decrypted = SecurityPolicyRegistry.Default.Decrypt(certificate, policyUri, encrypted);
            Assert.That(decrypted, Is.EqualTo(plainText));
        }

        [TestCase(SecurityPolicies.Basic128Rsa15)]
        [TestCase(SecurityPolicies.Basic256Sha256)]
        [TestCase(SecurityPolicies.Aes256_Sha256_RsaPss)]
        public void SignatureDataRoundTripsForSupportedRsaPolicies(string policyUri)
        {
            if (SecurityPolicyRegistry.Default.GetInfo(policyUri) == null)
            {
                Assert.Ignore("Policy is not supported by this platform.");
            }

            using Certificate certificate = CertificateBuilder
                .Create("CN=SecurityPolicies Signature")
                .SetRSAKeySize(2048)
                .CreateForRSA();
            byte[] data = [1, 3, 5, 7, 9];

            SignatureData signature = SecurityPolicyRegistry.Default.CreateSignatureData(policyUri, certificate, data);
            Assert.That(signature.Algorithm, Is.Not.Null);
            Assert.That(signature.Signature.IsNull, Is.False);
            Assert.That(SecurityPolicyRegistry.Default.VerifySignatureData(signature, policyUri, certificate, data), Is.True);

            ServiceResultException sre = Assert.Throws<ServiceResultException>(
                () => SecurityPolicyRegistry.Default.VerifySignatureData(
                    new SignatureData { Algorithm = "unexpected", Signature = signature.Signature },
                    policyUri,
                    certificate,
                    data));
            Assert.That(sre.StatusCode, Is.EqualTo(StatusCodes.BadSecurityChecksFailed));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        [TestCase(3)]
        [TestCase(4)]
        [TestCase(5)]
        public void EnhancedSignatureDataSignsEveryChannelInput(int inputIndex)
        {
            if (SecurityPolicyRegistry.Default.GetInfo(SecurityPolicies.RSA_DH_AesGcm) == null)
            {
                Assert.Ignore("Policy is not supported by this platform.");
            }

            using Certificate certificate = CertificateBuilder
                .Create("CN=SecurityPolicies Enhanced")
                .SetRSAKeySize(2048)
                .CreateForRSA();

            byte[][] inputs =
            [
                [1, 11],
                [2, 12],
                [3, 13],
                [4, 14],
                [5, 15],
                [6, 16]
            ];
            SignatureData signature = SecurityPolicyRegistry.Default.CreateSignatureData(
                SecurityPolicies.RSA_DH_AesGcm,
                certificate,
                inputs[0],
                inputs[1],
                inputs[2],
                inputs[3],
                inputs[4],
                inputs[5]);

            Assert.That(signature.Signature.IsNull, Is.False);
            Assert.That(
                VerifyEnhancedSignature(signature, certificate, inputs),
                Is.True);

            byte[][] mutatedInputs = new byte[inputs.Length][];
            for (int ii = 0; ii < inputs.Length; ii++)
            {
                mutatedInputs[ii] = (byte[])inputs[ii].Clone();
            }
            mutatedInputs[inputIndex][0]++;

            Assert.That(
                VerifyEnhancedSignature(signature, certificate, mutatedInputs),
                Is.False);
        }

        private static bool VerifyEnhancedSignature(
            SignatureData signature,
            Certificate certificate,
            byte[][] inputs)
        {
            return SecurityPolicyRegistry.Default.VerifySignatureData(
                signature,
                SecurityPolicies.RSA_DH_AesGcm,
                certificate,
                inputs[0],
                inputs[1],
                inputs[2],
                inputs[3],
                inputs[4],
                inputs[5]);
        }

        private static SecurityPolicyInfo WithPlatformSupport(SecurityPolicyInfo policy, bool isDefaultEcc)
        {
            return new SecurityPolicyInfo(policy)
            {
                PlatformSupport = () => true,
                IsDefaultEcc = isDefaultEcc
            };
        }

        private static IEnumerable<TestCaseData> SecurityConfigurationSupportedPolicyCases()
        {
            yield return new TestCaseData(
                false,
                NodeId.Null,
                new[]
                {
                    SecurityPolicies.None,
                    SecurityPolicies.Basic256Sha256,
                    SecurityPolicies.Aes128_Sha256_RsaOaep,
                    SecurityPolicies.Aes256_Sha256_RsaPss,
                    SecurityPolicies.RSA_DH_AesGcm,
                    SecurityPolicies.RSA_DH_ChaChaPoly
                }).SetName("SecurityConfigurationBuildsExpectedSupportedPolicySetForNullCertificateType");

            yield return new TestCaseData(
                true,
                ObjectTypeIds.ApplicationCertificateType,
                new[]
                {
                    SecurityPolicies.None,
                    SecurityPolicies.Basic256Sha256,
                    SecurityPolicies.Aes128_Sha256_RsaOaep,
                    SecurityPolicies.Aes256_Sha256_RsaPss,
                    SecurityPolicies.RSA_DH_AesGcm,
                    SecurityPolicies.RSA_DH_ChaChaPoly,
                    SecurityPolicies.Basic128Rsa15,
                    SecurityPolicies.Basic256
                }).SetName("SecurityConfigurationBuildsExpectedSupportedPolicySetForApplicationCertificateType");

            yield return new TestCaseData(
                true,
                ObjectTypeIds.RsaSha256ApplicationCertificateType,
                new[]
                {
                    SecurityPolicies.None,
                    SecurityPolicies.Basic256Sha256,
                    SecurityPolicies.Aes128_Sha256_RsaOaep,
                    SecurityPolicies.Aes256_Sha256_RsaPss,
                    SecurityPolicies.RSA_DH_AesGcm,
                    SecurityPolicies.RSA_DH_ChaChaPoly,
                    SecurityPolicies.Basic128Rsa15,
                    SecurityPolicies.Basic256
                }).SetName("SecurityConfigurationBuildsExpectedSupportedPolicySetForRsaSha256CertificateType");

            yield return new TestCaseData(
                true,
                ObjectTypeIds.RsaMinApplicationCertificateType,
                new[]
                {
                    SecurityPolicies.None,
                    SecurityPolicies.Basic128Rsa15,
                    SecurityPolicies.Basic256
                }).SetName("SecurityConfigurationBuildsExpectedSupportedPolicySetForRsaMinCertificateType");

            yield return new TestCaseData(
                true,
                ObjectTypeIds.EccNistP256ApplicationCertificateType,
                new[]
                {
                    SecurityPolicies.None,
                    SecurityPolicies.ECC_nistP256,
                    SecurityPolicies.ECC_nistP256_AesGcm,
                    SecurityPolicies.ECC_nistP256_ChaChaPoly
                }).SetName("SecurityConfigurationBuildsExpectedSupportedPolicySetForEccNistP256CertificateType");

            yield return new TestCaseData(
                true,
                ObjectTypeIds.EccNistP384ApplicationCertificateType,
                new[]
                {
                    SecurityPolicies.None,
                    SecurityPolicies.ECC_nistP256,
                    SecurityPolicies.ECC_nistP256_AesGcm,
                    SecurityPolicies.ECC_nistP256_ChaChaPoly,
                    SecurityPolicies.ECC_nistP384,
                    SecurityPolicies.ECC_nistP384_AesGcm,
                    SecurityPolicies.ECC_nistP384_ChaChaPoly
                }).SetName("SecurityConfigurationBuildsExpectedSupportedPolicySetForEccNistP384CertificateType");

            yield return new TestCaseData(
                true,
                ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType,
                new[]
                {
                    SecurityPolicies.None,
                    SecurityPolicies.ECC_brainpoolP256r1,
                    SecurityPolicies.ECC_brainpoolP256r1_AesGcm,
                    SecurityPolicies.ECC_brainpoolP256r1_ChaChaPoly
                }).SetName("SecurityConfigurationBuildsExpectedSupportedPolicySetForEccBrainpoolP256r1CertificateType");

            yield return new TestCaseData(
                true,
                ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType,
                new[]
                {
                    SecurityPolicies.None,
                    SecurityPolicies.ECC_brainpoolP256r1,
                    SecurityPolicies.ECC_brainpoolP256r1_AesGcm,
                    SecurityPolicies.ECC_brainpoolP256r1_ChaChaPoly,
                    SecurityPolicies.ECC_brainpoolP384r1,
                    SecurityPolicies.ECC_brainpoolP384r1_AesGcm,
                    SecurityPolicies.ECC_brainpoolP384r1_ChaChaPoly
                }).SetName("SecurityConfigurationBuildsExpectedSupportedPolicySetForEccBrainpoolP384r1CertificateType");
        }

        private static string[] SupportedOnly(string[] policyUris)
        {
            var supportedPolicyUris = new List<string>();
            foreach (string policyUri in policyUris)
            {
                if (policyUri == SecurityPolicies.None || SecurityPolicyRegistry.Default.GetDisplayName(policyUri) != null)
                {
                    supportedPolicyUris.Add(policyUri);
                }
            }

            return [.. supportedPolicyUris];
        }

        private static CertificateIdentifier CreateCertificateIdentifier(NodeId certificateType)
        {
            return new CertificateIdentifier
            {
                CertificateType = certificateType
            };
        }
    }
}
