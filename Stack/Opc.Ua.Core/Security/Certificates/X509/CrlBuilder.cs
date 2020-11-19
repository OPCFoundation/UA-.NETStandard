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
using System.Numerics;
using System.Linq;

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
        public class RevokedCertificate
        {
            public RevokedCertificate(string serialNumber)
            {
                UserCertificate = serialNumber;
                RevocationDate = DateTime.UtcNow;
                CrlEntryExtensions = new List<X509Extension>();
                CrlEntryExtensions.Add(X509Extensions.BuildX509CRLReason(CRLReason.KeyCompromise));
            }

            public string UserCertificate { get; set; }
            public DateTime RevocationDate { get; set; }
            public IList<X509Extension> CrlEntryExtensions { get; }
        }

        public X500DistinguishedName IssuerSubjectName { get; private set; }
        public HashAlgorithmName HashAlgorithmName { get; private set; }
        public DateTime ThisUpdate { get; set; }
        public DateTime NextUpdate { get; set; }
        public IList<RevokedCertificate> RevokedCertificates { get; }
        public IList<X509Extension> CrlExtensions { get; }

        public CrlBuilder(byte[] crl)
        {
            this.ThisUpdate = DateTime.MinValue;
            this.NextUpdate = DateTime.MinValue;
            this.RevokedCertificates = new List<RevokedCertificate>();
            this.CrlExtensions = new List<X509Extension>();
            Decode(crl);
        }

        public CrlBuilder(X500DistinguishedName issuerSubjectName, HashAlgorithmName hashAlgorithmName)
        {
            this.IssuerSubjectName = issuerSubjectName;
            this.HashAlgorithmName = hashAlgorithmName;
            this.ThisUpdate = DateTime.UtcNow;
            this.NextUpdate = DateTime.MinValue;
            this.RevokedCertificates = new List<RevokedCertificate>();
            this.CrlExtensions = new List<X509Extension>();
        }

        public CrlBuilder(X500DistinguishedName issuerSubjectName, string[] serialNumbers, HashAlgorithmName hashAlgorithmName)
        {
            this.IssuerSubjectName = issuerSubjectName;
            this.HashAlgorithmName = hashAlgorithmName;
            this.ThisUpdate = DateTime.UtcNow;
            this.NextUpdate = DateTime.MinValue;
            this.RevokedCertificates = serialNumbers.Select(s => new RevokedCertificate(s)).ToList();
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
                string signatureAlgorithm = OidConstants.GetRSAOid(this.HashAlgorithmName);
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

                foreach (var revokedCert in this.RevokedCertificates)
                {
                    crlWriter.PushSequence();

                    BigInteger srlNumberValue = BigInteger.Parse(revokedCert.UserCertificate, NumberStyles.AllowHexSpecifier);
                    crlWriter.WriteInteger(srlNumberValue);
                    crlWriter.WriteUtcTime(revokedCert.RevocationDate);

                    if (revokedCert.CrlEntryExtensions.Count > 0)
                    {
                        crlWriter.PushSequence();
                        foreach (var crlEntryExt in revokedCert.CrlEntryExtensions)
                        {
                            crlWriter.WriteEncodedValue(crlEntryExt.RawData);
                        }
                        crlWriter.PopSequence();
                    }
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
                        //crlWriter.WriteEncodedValue(extension.RawData);
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


        public void Decode(byte[] crl)
        {
            AsnReader crlReader = new AsnReader(crl, AsnEncodingRules.DER);
            {
                var tag = Asn1Tag.Sequence;
                var seqReader = crlReader?.ReadSequence(tag);
                if (seqReader != null)
                {
                    uint version = 0;
                    if (seqReader.TryReadUInt32(out version))
                    {
                        if (version != 1)
                        {
                            throw new AsnContentException($"The CRL contains an incorrect version {version}");
                        }
                    }

                    // Signature Algorithm Identifier
                    var sigReader = seqReader.ReadSequence();
                    var oid = sigReader.ReadObjectIdentifier();
                    HashAlgorithmName = OidConstants.GetHashAlgorithmName(oid);

                    // Issuer
                    IssuerSubjectName = new X500DistinguishedName(seqReader.ReadEncodedValue().ToArray());

                    // thisUpdate
                    ThisUpdate = seqReader.ReadUtcTime().UtcDateTime;

                    // nextUpdate is OPTIONAL
                    var utcTag = new Asn1Tag(UniversalTagNumber.UtcTime);
                    var peekTag = seqReader.PeekTag();
                    if (peekTag == utcTag)
                    {
                        NextUpdate = seqReader.ReadUtcTime().UtcDateTime;
                    }

                    // revoked certificates
                    var boolTag = new Asn1Tag(UniversalTagNumber.Boolean);
                    var revReader = seqReader.ReadSequence(tag);
                    while (revReader.HasData)
                    {
                        var crlEntry = revReader.ReadSequence();
                        var serial = crlEntry.ReadInteger();
                        var revokedCertificate = new RevokedCertificate(Utils.ToHexString(serial.ToByteArray()));
                        revokedCertificate.RevocationDate = crlEntry.ReadUtcTime().UtcDateTime;
                        if (crlEntry.HasData)
                        {
                            var crlEntryExtensions = crlEntry.ReadSequence();
                            while (crlEntryExtensions.HasData)
                            {
                                var crlEntryExt = crlEntryExtensions.ReadSequence();
                                var extOid = crlEntryExt.ReadObjectIdentifier();
                                bool critical = false;
                                peekTag = crlEntryExt.PeekTag();
                                if (peekTag == boolTag)
                                {
                                    critical = crlEntryExt.ReadBoolean();
                                }
                                var data = crlEntryExt.ReadOctetString();
                                var x509Ext = new X509Extension(new Oid(extOid), data, critical);
                                revokedCertificate.CrlEntryExtensions.Add(x509Ext);
                            }
                        }
                        this.RevokedCertificates.Add(revokedCertificate);
                    }

                    // CRL extensions
                    var ext = new Asn1Tag(TagClass.ContextSpecific, 0);
                    var optReader = seqReader.ReadSequence(ext);
                    if (optReader.HasData)
                    {
                        var crlExtensions = optReader.ReadSequence();
                        while (crlExtensions.HasData)
                        {
                            var etag = Asn1Tag.Sequence;
                            var extension = crlExtensions.ReadSequence(etag);
                            var extOid = extension.ReadObjectIdentifier();
                            bool critical = false;
                            peekTag = extension.PeekTag();
                            if (peekTag == boolTag)
                            {
                                critical = extension.ReadBoolean();
                            }
                            var data = extension.ReadOctetString();
                            var x509Ext = new X509Extension(new Oid(extOid), data, critical);
                            this.CrlExtensions.Add(x509Ext);
                        }
                    }
                }
            }
        }
    }
}
