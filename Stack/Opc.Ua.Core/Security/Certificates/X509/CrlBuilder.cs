/* Copyright (c) 1996-2020 The OPC Foundation. All rights reserved.
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

using System;
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Globalization;
using System.Collections.Generic;
using Opc.Ua.Security.Certificates.X509;

namespace Opc.Ua.Security.Certificates
{



    /// <summary>
    /// CRL Reason codes.
    /// </summary>
    /// <remarks>
    /// id-ce-cRLReasons OBJECT IDENTIFIER ::= { id-ce 21 }
    ///   -- reasonCode::= { CRLReason }
    /// CRLReason::= ENUMERATED {
    ///      unspecified(0),
    ///      keyCompromise(1),
    ///      cACompromise(2),
    ///      affiliationChanged(3),
    ///      superseded(4),
    ///      cessationOfOperation(5),
    ///      certificateHold(6),
    ///           --value 7 is not used
    ///      removeFromCRL(8),
    ///      privilegeWithdrawn(9),
    ///      aACompromise(10) }
    /// </remarks>
    public enum CRLReason
    {
        Unspecified = 0,
        KeyCompromise = 1,
        CACompromise = 2,
        AffiliationChanged = 3,
        Superseded = 4,
        CessationOfOperation = 5,
        CertificateHold = 6,
        RemoveFromCRL = 8,
        PrivilegeWithdrawn = 9,
        AACompromise = 10
    };

    public class CrlBuilder
    {
        public X500DistinguishedName IssuerSubjectName { get; private set; }

        public string[] SerialNumbers { get; private set; }

        public HashAlgorithmName HashAlgorithmName { get; private set; }

        public DateTime ThisUpdate { get; set; }
        public DateTime NextUpdate { get; set; }

        public IList<X509Extension> CrlExtensions { get; private set; }

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
                return OidConstants.ECDSASHA256SignatureAlgorithm;
            }
            else if (hashAlgorithm == HashAlgorithmName.SHA384)
            {
                return OidConstants.ECDSASHA384SignatureAlgorithm;
            }
            else if (hashAlgorithm == HashAlgorithmName.SHA512)
            {
                return OidConstants.ECDSASHA512SignatureAlgorithm;
            }
            else
            {
                throw new NotSupportedException($"Signing ECDSA with hash {hashAlgorithm.Name} is not supported. ");
            }
        }

        public CrlBuilder(X500DistinguishedName issuerSubjectName, string[] serialNumbers, HashAlgorithmName hashAlgorithmName)
        {
            this.IssuerSubjectName = issuerSubjectName;
            this.SerialNumbers = serialNumbers;
            this.HashAlgorithmName = hashAlgorithmName;
            this.ThisUpdate = DateTime.UtcNow;
            this.NextUpdate = DateTime.MinValue;
            this.CrlExtensions = new List<X509Extension>();
        }

        /// <summary>
        /// Constructs Certificate Revocation List raw data in X509 ASN format.
        /// </summary>
        public byte[] GetEncoded()
        {
            // CRL fields -- https://tools.ietf.org/html/rfc5280#section-5.1
            // 
            // CertificateList  ::=  SEQUENCE  {
            //    tbsCertList          TBSCertList,
            //    signatureAlgorithm   AlgorithmIdentifier,
            //    signatureValue       BIT STRING
            //    }
            //
            // TBSCertList  ::=  SEQUENCE  {
            //    version                 Version OPTIONAL,
            //                            -- if present, MUST be v2
            //    signature               AlgorithmIdentifier,
            //    issuer                  Name,
            //    thisUpdate              Time,
            //    nextUpdate              Time OPTIONAL,
            //    revokedCertificates     SEQUENCE OF SEQUENCE  {
            //        userCertificate         CertificateSerialNumber,
            //        revocationDate          Time,
            //        crlEntryExtensions      Extensions OPTIONAL
            //                              -- if present, version MUST be v2
            //                            }  OPTIONAL,
            //    crlExtensions           [0]  EXPLICIT Extensions OPTIONAL
            //                              -- if present, version MUST be v2
            //                            }

            AsnWriter crlWriter = new AsnWriter(AsnEncodingRules.DER);
            {
                // tbsCertList
                crlWriter.PushSequence();

                // version
                crlWriter.WriteInteger(1);

                // Signature Algorithm Identifier
                crlWriter.PushSequence();
                string signatureAlgorithm = GetRSAOid(this.HashAlgorithmName);
                crlWriter.WriteObjectIdentifier(signatureAlgorithm);
                crlWriter.WriteNull();

                // pop
                crlWriter.PopSequence();

                // Issuer
                crlWriter.WriteEncodedValue((ReadOnlySpan<byte>)this.IssuerSubjectName.RawData);

                // this update
                crlWriter.WriteUtcTime(this.ThisUpdate);

                if (this.NextUpdate != DateTime.MinValue &&
                    this.NextUpdate > this.ThisUpdate)
                {
                    // next update
                    crlWriter.WriteUtcTime(this.NextUpdate);
                }

                // sequence to start the revoked certificates.
                crlWriter.PushSequence();

                foreach (string serialNumber in this.SerialNumbers)
                {
                    crlWriter.PushSequence();

                    System.Numerics.BigInteger srlNumberValue = System.Numerics.BigInteger.Parse(serialNumber, NumberStyles.AllowHexSpecifier);
                    crlWriter.WriteInteger(srlNumberValue);
                    crlWriter.WriteUtcTime(DateTime.UtcNow);

                    crlWriter.PushSequence();
                    crlWriter.PushSequence();
                    crlWriter.WriteObjectIdentifier(OidConstants.CertificateRevocationReasonCode);

                    // TODO: is there a better way to encode CRLReason?
                    crlWriter.WriteOctetString(new byte[] { (byte)UniversalTagNumber.Enumerated, 0x1, (byte)CRLReason.Unspecified });

                    crlWriter.PopSequence();
                    crlWriter.PopSequence();
                    crlWriter.PopSequence();
                }

                crlWriter.PopSequence();

                // CRL extensions
                if (CrlExtensions.Count > 0)
                {
                    // [0]  EXPLICIT Extensions
                    var tag = new Asn1Tag(TagClass.ContextSpecific, 0);
                    crlWriter.PushSequence(tag);

                    // CRL extensions
                    crlWriter.PushSequence();
                    foreach (var extension in CrlExtensions)
                    {
                        var etag = Asn1Tag.Sequence;
                        crlWriter.PushSequence(etag);
                        crlWriter.WriteObjectIdentifier(extension.Oid.Value);
                        crlWriter.WriteBoolean(extension.Critical);
                        crlWriter.WriteOctetString(extension.RawData);
                        crlWriter.PopSequence(etag);
                    }
                    crlWriter.PopSequence();

                    crlWriter.PopSequence(tag);
                }

                crlWriter.PopSequence();

                return crlWriter.Encode();
            }
        }
    }
}
