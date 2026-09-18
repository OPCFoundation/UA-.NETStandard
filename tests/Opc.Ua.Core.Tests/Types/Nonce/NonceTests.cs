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
using NUnit.Framework;

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
                .Select(SecurityPolicies.Default.GetUri)
        ];

        private static readonly HashSet<string> s_supportedPolicyUris =
        [
            .. SecurityPolicies.Default.GetDisplayNames().Select(SecurityPolicies.Default.GetUri)
        ];

        [Test]
        public void RawEccSecretIsUnhashedOrExplicitlyUnsupported([Values(false, true)] bool p384)
        {
            SecurityPolicyInfo policy = p384 ? SecurityPolicyInfo.ECC_nistP384 : SecurityPolicyInfo.ECC_nistP256;
            bool supported = SecurityPolicies.SupportsRawEccSecretAgreement();
            Assert.That(SecurityPolicies.Default.GetInfo(policy.Uri) != null, Is.EqualTo(supported));
            using Ua.Nonce local = Ua.Nonce.CreateNonce(policy);
            // A peer using scalar 1 publishes the curve generator. Its shared Z is the local public X coordinate.
            byte[] generator = Utils.FromHexString(p384
                ? "AA87CA22BE8B05378EB1C71EF320AD746E1D3B628BA79B9859F741E082542A385502F25DBF55296C3A545E3872760AB7"
                    + "3617DE4A96262C6F5D9E98BF9292DC29F8F41DBD289A147CE9DA3113B5F0B8C00A60B1CE1D7E819D7A431D7C90EA0E5F"
                : "6B17D1F2E12C4247F8BCE6E563A440F277037D812DEB33A0F4A13945D898C296"
                    + "4FE342E2FE1A7F9B8EE7EB4A7C0F9E162BCE33576B315ECECBB6406837BF51F5");
            using Ua.Nonce remote = Ua.Nonce.CreateNonce(policy, generator);
            if (!supported)
            {
                Assert.That(SecurityPolicies.Default.GetDefaultEccUris(), Does.Not.Contain(policy.Uri));
                Assert.That(() => local.GenerateSecret(remote, null),
                    Throws.TypeOf<NotSupportedException>().With.Message.Contains(".NET 8"));
                return;
            }

            byte[] secret = local.GenerateSecret(remote, null);
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
        /// Test the CreateNonce - securitypolicy and valid nonceLength
        /// </summary>
        [Theory]
        [TestCaseSource(nameof(SupportedNoncePolicies))]
        public void ValidateCreateNoncePolicyLength(string securityPolicyUri)
        {
            if (IsSupportedByPlatform(securityPolicyUri))
            {
                SecurityPolicyInfo info = SecurityPolicies.Default.GetInfo(securityPolicyUri);
                int nonceLength = info.SecureChannelNonceLength;

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
                SecurityPolicyInfo info = SecurityPolicies.Default.GetInfo(securityPolicyUri);
                int nonceLength = info.SecureChannelNonceLength;
                var nonceByLen = Ua.Nonce.CreateNonce(securityPolicyUri);

                var nonceByData = Ua.Nonce.CreateNonce(info, nonceByLen.Data);

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
                SecurityPolicyInfo info = SecurityPolicies.Default.GetInfo(securityPolicyUri);
                int nonceLength = info.SecureChannelNonceLength;

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
