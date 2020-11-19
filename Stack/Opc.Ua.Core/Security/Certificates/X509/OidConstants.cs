/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation members in good-standing
     - GPL V2: everybody else
   RCL license terms accompanied with this source code. See http://opcfoundation.org/License/RCL/1.00/
   GNU General Public License as published by the Free Software Foundation;
   version 2 of the License are accompanied with this source code. See http://opcfoundation.org/License/GPLv2
   This source code is distributed in the hope that it will be useful,
   but WITHOUT ANY WARRANTY; without even the implied warranty of
   MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.
*/

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
using System;
using System.Security.Cryptography;

namespace Opc.Ua.Security.Certificates.X509
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
