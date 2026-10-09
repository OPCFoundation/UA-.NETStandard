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

// CA2000: test code; many disposables are ownership-transferred to test fixtures or short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using NUnit.Framework;
#if NETFRAMEWORK
using Opc.Ua.Security.Certificates.BouncyCastle;
#endif

namespace Opc.Ua.Core.Tests.Types.Nonce
{
    /// <summary>
    /// Tests for the Binary Schema Validator class.
    /// </summary>
    [TestFixture]
    [Category("NonceTests")]
    [Parallelizable]
    public class NonceTests
    {
        public static readonly string[] SupportedNoncePolicies =
        [
            .. SecurityPolicies.Default.GetDisplayNames()
                .Where(name => !name.Equals(nameof(SecurityPolicies.None), StringComparison.Ordinal))
                .Select(SecurityPolicies.Default.GetUri)!
        ];

        private static readonly HashSet<string> s_supportedPolicyUris =
        [
            .. SecurityPolicies.Default.GetDisplayNames().Select(name => SecurityPolicies.Default.GetUri(name)!)
        ];

        [Test]
        public void RawEccSecretIsUnhashed([Values] bool p384)
        {
            SecurityPolicyInfo policy = p384 ? SecurityPolicyInfo.ECC_nistP384 : SecurityPolicyInfo.ECC_nistP256;
            Assert.That(SecurityPolicies.Default.GetInfo(policy.Uri), Is.Not.Null);
            Assert.That(SecurityPolicies.Default.GetDefaultEccUris(), Does.Contain(policy.Uri));
            using var local = Ua.Nonce.CreateNonce(policy);
            // A peer using scalar 1 publishes the curve generator. Its shared Z is the local public X coordinate.
            byte[] generator = Utils.FromHexString(p384
                ? "AA87CA22BE8B05378EB1C71EF320AD746E1D3B628BA79B9859F741E082542A385502F25DBF55296C3A545E3872760AB7" +
                    "3617DE4A96262C6F5D9E98BF9292DC29F8F41DBD289A147CE9DA3113B5F0B8C00A60B1CE1D7E819D7A431D7C90EA0E5F"
                : "6B17D1F2E12C4247F8BCE6E563A440F277037D812DEB33A0F4A13945D898C296" +
                    "4FE342E2FE1A7F9B8EE7EB4A7C0F9E162BCE33576B315ECECBB6406837BF51F5");
            using var remote = Ua.Nonce.CreateNonce(policy, generator);

            byte[] secret = local.GenerateSecret(remote, null)!;
            try
            {
                Assert.That(secret, Is.EqualTo(local.Data.AsSpan(0, p384 ? 48 : 32).ToArray()));
            }
            finally
            {
                CryptoUtils.ZeroMemory(secret);
            }
        }

        /// <summary>
        /// Both peers derive the same raw secret, of the curve's field size,
        /// on every ECC curve the platform supports.
        /// </summary>
        [TestCase(SecurityPolicies.ECC_nistP256, 32)]
        [TestCase(SecurityPolicies.ECC_nistP384, 48)]
        [TestCase(SecurityPolicies.ECC_brainpoolP256r1, 32)]
        [TestCase(SecurityPolicies.ECC_brainpoolP384r1, 48)]
        public void EccSecretAgreementIsSymmetric(string policyUri, int fieldSize)
        {
            SecurityPolicyInfo? info = SecurityPolicies.Default.GetInfo(policyUri);
            if (info == null)
            {
                Assert.Ignore($"{policyUri} is not supported on this platform.");
            }

            using var client = Ua.Nonce.CreateNonce(info);
            using var server = Ua.Nonce.CreateNonce(info);
            using var clientSeenByServer = Ua.Nonce.CreateNonce(info, client.Data!);
            using var serverSeenByClient = Ua.Nonce.CreateNonce(info, server.Data!);

            byte[] clientSecret = client.GenerateSecret(serverSeenByClient, null)!;
            byte[] serverSecret = server.GenerateSecret(clientSeenByServer, null)!;
            try
            {
                Assert.That(clientSecret, Has.Length.EqualTo(fieldSize));
                Assert.That(serverSecret, Is.EqualTo(clientSecret));
            }
            finally
            {
                CryptoUtils.ZeroMemory(clientSecret);
                CryptoUtils.ZeroMemory(serverSecret);
            }
        }

#if NETFRAMEWORK
        /// <summary>
        /// The BouncyCastle polyfill returns the unhashed secret CNG agrees on:
        /// hashing it gives exactly what CNG's own hashed derivation returns.
        /// </summary>
        [TestCase("nistP256")]
        [TestCase("nistP384")]
        [TestCase("brainpoolP256r1")]
        [TestCase("brainpoolP384r1")]
        public void PolyfillRawSecretMatchesThePlatformAgreement(string curveName)
        {
            ECCurve curve = ECCurve.CreateFromFriendlyName(curveName);
            ECDiffieHellman local;
            try
            {
                local = ECDiffieHellman.Create(curve);
            }
            catch (PlatformNotSupportedException)
            {
                Assert.Ignore($"{curveName} is not supported on this platform.");
                return;
            }

            using (local)
            using (var remote = ECDiffieHellman.Create(curve))
            {
                byte[] raw = EcdhRawAgreement.DeriveRawSecretAgreement(local, remote.PublicKey);
                byte[] expected = local.DeriveKeyFromHash(remote.PublicKey, HashAlgorithmName.SHA256);
                using var sha256 = SHA256.Create();

                Assert.That(sha256.ComputeHash(raw), Is.EqualTo(expected));
                Assert.That(
                    EcdhRawAgreement.DeriveRawSecretAgreement(remote, local.PublicKey),
                    Is.EqualTo(raw));
            }
        }

        /// <summary>
        /// The polyfill refuses keys on different curves rather than
        /// computing an agreement on the wrong domain.
        /// </summary>
        [Test]
        public void PolyfillRejectsKeysOnDifferentCurves()
        {
            using var local = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            using var remote = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);

            Assert.That(
                () => EcdhRawAgreement.DeriveRawSecretAgreement(local, remote.PublicKey),
                Throws.TypeOf<CryptographicException>());
        }

        /// <summary>
        /// A point that is not on the curve is rejected before any agreement.
        /// </summary>
        [Test]
        public void PolyfillRejectsAPointOffTheCurve()
        {
            using var local = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            ECParameters localKey = local.ExportParameters(true);
            ECParameters offCurve = local.ExportParameters(false);
            offCurve.Q.Y![offCurve.Q.Y.Length - 1] ^= 1;

            Assert.That(
                () => EcdhRawAgreement.DeriveRawSecretAgreement(localKey, offCurve),
                Throws.TypeOf<CryptographicException>());
        }
#endif

        /// <summary>
        /// Test the CreateNonce - securitypolicy and valid nonceLength
        /// </summary>
        [Theory]
        [TestCaseSource(nameof(SupportedNoncePolicies))]
        public void ValidateCreateNoncePolicyLength(string securityPolicyUri)
        {
            if (IsSupportedByPlatform(securityPolicyUri))
            {
                SecurityPolicyInfo info = SecurityPolicies.Default.GetInfo(securityPolicyUri)!;
                int nonceLength = info!.SecureChannelNonceLength;

                var nonce = Ua.Nonce.CreateNonce(securityPolicyUri);

                Assert.That(nonce, Is.Not.Null);
                Assert.That(nonce.Data, Is.Not.Null);
                Assert.That(nonce.Data, Has.Length.EqualTo(nonceLength));
            }
        }

        /// <summary>
        /// Test the CreateEccNonce - securitypolicy and nonceData
        /// </summary>
        [Theory]
        [TestCaseSource(nameof(SupportedNoncePolicies))]
        public void ValidateCreateNoncePolicyNonceData(string securityPolicyUri)
        {
            if (IsSupportedByPlatform(securityPolicyUri))
            {
                SecurityPolicyInfo info = SecurityPolicies.Default.GetInfo(securityPolicyUri)!;
                int nonceLength = info!.SecureChannelNonceLength;
                var nonceByLen = Ua.Nonce.CreateNonce(securityPolicyUri);

                var nonceByData = Ua.Nonce.CreateNonce(info, nonceByLen.Data!);

                Assert.That(nonceByData, Is.Not.Null);
                Assert.That(nonceByData.Data, Is.Not.Null);
                Assert.That(nonceByData.Data, Has.Length.EqualTo(nonceLength));
                Assert.That(nonceByLen.Data, Is.EqualTo(nonceByData.Data));
            }
        }

        /// <summary>
        /// Test the CreateEccNonce - securitypolicy and invalid nonceData
        /// </summary>
        [Theory]
        [TestCaseSource(nameof(SupportedNoncePolicies))]
        public void ValidateCreateEccNoncePolicyInvalidNonceDataCorrectLength(
            string securityPolicyUri)
        {
            if (IsSupportedByPlatform(securityPolicyUri))
            {
                SecurityPolicyInfo info = SecurityPolicies.Default.GetInfo(securityPolicyUri)!;
                int nonceLength = info!.SecureChannelNonceLength;

                byte[] randomValue = Ua.Nonce.CreateRandomNonceData(nonceLength);

                if (info.CertificateKeyFamily == CertificateKeyFamily.ECC)
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) &&
                        (
                            securityPolicyUri.Contains("ECC_nistP256", StringComparison.Ordinal) ||
                            securityPolicyUri.Contains("ECC_nistP384", StringComparison.Ordinal)))
                    {
                        Assert
                            .Ignore("No exception is thrown on OSX with NIST curves");
                    }
                    Assert.Throws<ArgumentException>(() =>
                        Ua.Nonce.CreateNonce(info, randomValue));
                }
                else
                {
                    var rsaNonce = Ua.Nonce.CreateNonce(info, randomValue);
                    Assert.That(randomValue, Is.EqualTo(rsaNonce.Data));
                }
            }
        }

        /// <summary>
        /// Determines if security policy is supported by platform
        /// </summary>
        private static bool IsSupportedByPlatform(string securityPolicyUri)
        {
            if (securityPolicyUri.StartsWith(
                SecurityPolicies.BaseUri,
                StringComparison.Ordinal))
            {
                return s_supportedPolicyUris.Contains(securityPolicyUri);
            }

            return true;
        }
    }
}
