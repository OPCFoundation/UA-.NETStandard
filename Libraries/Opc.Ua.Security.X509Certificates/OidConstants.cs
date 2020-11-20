/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
using System;
using System.Security.Cryptography;

namespace Opc.Ua.Security.X509Certificates
{
    /// <summary>
    /// Oid constants defined for ASN encoding/decoding.
    /// </summary>
    public static class OidConstants
    {
        public const string ECDsaWithSha1 = "1.2.840.10045.4.1";
        public const string ECDSASHA256 = "1.2.840.10045.4.3.2";
        public const string ECDSASHA384 = "1.2.840.10045.4.3.3";
        public const string ECDSASHA512 = "1.2.840.10045.4.3.4";

        public const string RsaPkcs1Sha1 = "1.2.840.113549.1.1.5";
        public const string RsaPkcs1Sha256 = "1.2.840.113549.1.1.11";
        public const string RsaPkcs1Sha384 = "1.2.840.113549.1.1.12";
        public const string RsaPkcs1Sha512 = "1.2.840.113549.1.1.13";

        public const string CrlNumber = "2.5.29.20";
        public const string CertificateRevocationReasonCode = "2.5.29.21";

        public const string ServerAuthentication = "1.3.6.1.5.5.7.3.1";
        public const string ClientAuthentication = "1.3.6.1.5.5.7.3.2";

        public static string GetRSAOid(HashAlgorithmName hashAlgorithm)
        {
            if (hashAlgorithm == HashAlgorithmName.SHA1)
            {
                return OidConstants.RsaPkcs1Sha1;
            }
            else if (hashAlgorithm == HashAlgorithmName.SHA256)
            {
                return OidConstants.RsaPkcs1Sha256;
            }
            else if (hashAlgorithm == HashAlgorithmName.SHA384)
            {
                return OidConstants.RsaPkcs1Sha384;
            }
            else if (hashAlgorithm == HashAlgorithmName.SHA512)
            {
                return OidConstants.RsaPkcs1Sha512;
            }
            else
            {
                throw new NotSupportedException($"Signing RSA with hash {hashAlgorithm.Name} is not supported. ");
            }
        }

        public static string GetECDSAOid(HashAlgorithmName hashAlgorithm)
        {
            if (hashAlgorithm == HashAlgorithmName.SHA256)
            {
                return OidConstants.ECDSASHA256;
            }
            else if (hashAlgorithm == HashAlgorithmName.SHA384)
            {
                return OidConstants.ECDSASHA384;
            }
            else if (hashAlgorithm == HashAlgorithmName.SHA512)
            {
                return OidConstants.ECDSASHA512;
            }
            else
            {
                throw new NotSupportedException($"Signing ECDSA with hash {hashAlgorithm.Name} is not supported. ");
            }
        }

        public static HashAlgorithmName GetHashAlgorithmName(string oid)
        {
            switch (oid)
            {
                case OidConstants.RsaPkcs1Sha1: return HashAlgorithmName.SHA1;
                case OidConstants.ECDSASHA256:
                case OidConstants.RsaPkcs1Sha256: return HashAlgorithmName.SHA256;
                case OidConstants.ECDSASHA384:
                case OidConstants.RsaPkcs1Sha384: return HashAlgorithmName.SHA384;
                case OidConstants.ECDSASHA512:
                case OidConstants.RsaPkcs1Sha512: return HashAlgorithmName.SHA512;
            }
            throw new NotSupportedException($"Unknown hash algorithm {oid} is not supported. ");
        }

    }
}
