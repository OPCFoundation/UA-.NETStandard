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
namespace Opc.Ua.Security.Certificates.X509
{
    /// <summary>
    /// Oid constants defined for ASN encoding/decoding.
    /// </summary>
    public class OidConstants
    {
        public const string ECDsaWithSha1 = "1.2.840.10045.4.1";
        public const string ECDSASHA256SignatureAlgorithm = "1.2.840.10045.4.3.2";
        public const string ECDSASHA384SignatureAlgorithm = "1.2.840.10045.4.3.3";
        public const string ECDSASHA512SignatureAlgorithm = "1.2.840.10045.4.3.4";

        public const string RsaPkcs1Sha1 = "1.2.840.113549.1.1.5";
        public const string RsaPkcs1Sha256 = "1.2.840.113549.1.1.11";
        public const string RsaPkcs1Sha384 = "1.2.840.113549.1.1.12";
        public const string RsaPkcs1Sha512 = "1.2.840.113549.1.1.13";

        public const string CertificateRevocationReasonCode = "2.5.29.21";
    }
}
