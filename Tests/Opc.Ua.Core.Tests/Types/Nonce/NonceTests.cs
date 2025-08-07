/* ========================================================================
 * Copyright (c) 2005-2024 The OPC Foundation, Inc. All rights reserved.
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
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Core.Tests.Types.Nonce
{
    /// <summary>
    /// Tests for the Binary Schema Validator class.
    /// </summary>
    [TestFixture, Category("NonceTests")]
    [Parallelizable]
    public class NonceTests
    {
        #region Test Methods
        /// <summary>
        /// Test the CreateNonce - securitypolicy and valid nonceLength
        /// </summary>
        [Theory]
        [TestCase(SecurityPolicies.ECC_nistP256)]
        [TestCase(SecurityPolicies.ECC_nistP384)]
        [TestCase(SecurityPolicies.ECC_brainpoolP256r1)]
        [TestCase(SecurityPolicies.ECC_brainpoolP384r1)]
        [TestCase(SecurityPolicies.Basic256)]
        [TestCase(SecurityPolicies.Basic256Sha256)]
        [TestCase(SecurityPolicies.Aes128_Sha256_RsaOaep)]
        [TestCase(SecurityPolicies.Aes256_Sha256_RsaPss)]
        public void ValidateCreateNoncePolicyLength(string securityPolicyUri)
        {
            if (IsSupportedByPlatform(securityPolicyUri))
            {
                uint nonceLength = Ua.Nonce.GetNonceLength(securityPolicyUri);

                var nonce = Ua.Nonce.CreateNonce(securityPolicyUri);

                Assert.IsNotNull(nonce);
                Assert.IsNotNull(nonce.Data);
                Assert.AreEqual(nonceLength, nonce.Data.Length);
            }
        }

        /// <summary>
        /// Test the CreateEccNonce - securitypolicy and nonceData
        /// </summary>
        [Theory]
        [TestCase(SecurityPolicies.ECC_nistP256)]
        [TestCase(SecurityPolicies.ECC_nistP384)]
        [TestCase(SecurityPolicies.ECC_brainpoolP256r1)]
        [TestCase(SecurityPolicies.ECC_brainpoolP384r1)]
        [TestCase(SecurityPolicies.Basic256)]
        [TestCase(SecurityPolicies.Basic256Sha256)]
        [TestCase(SecurityPolicies.Aes128_Sha256_RsaOaep)]
        [TestCase(SecurityPolicies.Aes256_Sha256_RsaPss)]
        public void ValidateCreateNoncePolicyNonceData(string securityPolicyUri)
        {
            if (IsSupportedByPlatform(securityPolicyUri))
            {
                uint nonceLength = Ua.Nonce.GetNonceLength(securityPolicyUri);
                var nonceByLen = Ua.Nonce.CreateNonce(securityPolicyUri);

                var nonceByData = Ua.Nonce.CreateNonce(securityPolicyUri, nonceByLen.Data);

                Assert.IsNotNull(nonceByData);
                Assert.IsNotNull(nonceByData.Data);
                Assert.AreEqual(nonceLength, nonceByData.Data.Length);
                Assert.AreEqual(nonceByData.Data, nonceByLen.Data);
            }
        }

        /// <summary>
        /// Test the CreateEccNonce - securitypolicy and invalid nonceData
        /// </summary>
        [Theory]
        [TestCase(SecurityPolicies.ECC_nistP256)]
        [TestCase(SecurityPolicies.ECC_nistP384)]
        [TestCase(SecurityPolicies.ECC_brainpoolP256r1)]
        [TestCase(SecurityPolicies.ECC_brainpoolP384r1)]
        [TestCase(SecurityPolicies.Basic256)]
        [TestCase(SecurityPolicies.Basic256Sha256)]
        [TestCase(SecurityPolicies.Aes128_Sha256_RsaOaep)]
        [TestCase(SecurityPolicies.Aes256_Sha256_RsaPss)]
        public void ValidateCreateEccNoncePolicyInvalidNonceDataCorrectLength(string securityPolicyUri)
        {
            if (IsSupportedByPlatform(securityPolicyUri))
            {
                uint nonceLength = Ua.Nonce.GetNonceLength(securityPolicyUri);

                byte[] randomValue = Ua.Nonce.CreateRandomNonceData(nonceLength);

                if (securityPolicyUri.Contains("ECC_", StringComparison.Ordinal))
                {
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) &&
                        (securityPolicyUri == SecurityPolicies.ECC_nistP256 || securityPolicyUri == SecurityPolicies.ECC_nistP384))
                    {
                        Assert.Ignore("No exception is thrown on OSX with NIST curves");
                    }
                    Assert.Throws(typeof(ArgumentException), () => Ua.Nonce.CreateNonce(securityPolicyUri, randomValue));
                }
                else
                {
                    var rsaNonce = Ua.Nonce.CreateNonce(securityPolicyUri, randomValue);
                    Assert.AreEqual(rsaNonce.Data, randomValue);
                }
            }
        }
        #endregion

        #region Helper

        /// <summary>
        /// Determines if security policy is supported by platform
        /// </summary>
        /// <param name="securityPolicyUri"></param>
        /// <returns></returns>
        private bool IsSupportedByPlatform(string securityPolicyUri)
        {
            if (securityPolicyUri.Equals(SecurityPolicies.ECC_nistP256, StringComparison.Ordinal))
            {
                return Utils.IsSupportedCertificateType(ObjectTypeIds.EccNistP256ApplicationCertificateType);
            }
            else if (securityPolicyUri.Equals(SecurityPolicies.ECC_nistP384, StringComparison.Ordinal))
            {
                return Utils.IsSupportedCertificateType(ObjectTypeIds.EccNistP384ApplicationCertificateType);
            }
            else if (securityPolicyUri.Equals(SecurityPolicies.ECC_brainpoolP256r1, StringComparison.Ordinal))
            {
                return Utils.IsSupportedCertificateType(ObjectTypeIds.EccBrainpoolP256r1ApplicationCertificateType);
            }
            else if (securityPolicyUri.Equals(SecurityPolicies.ECC_brainpoolP384r1, StringComparison.Ordinal))
            {
                return Utils.IsSupportedCertificateType(ObjectTypeIds.EccBrainpoolP384r1ApplicationCertificateType);
            }

            return true;
        }

        #endregion
    }

}
