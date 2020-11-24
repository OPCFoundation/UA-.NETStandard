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

using System;
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Collections.Generic;
using System.Numerics;
using System.Linq;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Builds the tbsCertList of a CRL.
    /// </summary>
    public sealed class CrlBuilder 
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

        public X500DistinguishedName Issuer { get; private set; }
        public HashAlgorithmName HashAlgorithmName { get; private set; }
        public DateTime ThisUpdate { get; set; }
        public DateTime NextUpdate { get; set; }
        public IList<RevokedCertificate> RevokedCertificates { get; }
        public IList<X509Extension> CrlExtensions { get; }

        public CrlBuilder(X500DistinguishedName issuerSubjectName, HashAlgorithmName hashAlgorithmName)
        {
            this.Issuer = issuerSubjectName;
            this.HashAlgorithmName = hashAlgorithmName;
            this.ThisUpdate = DateTime.UtcNow;
            this.NextUpdate = DateTime.MinValue;
            this.RevokedCertificates = new List<RevokedCertificate>();
            this.CrlExtensions = new List<X509Extension>();
        }

        public CrlBuilder(X500DistinguishedName issuerSubjectName, string[] serialNumbers, HashAlgorithmName hashAlgorithmName)
        {
            this.Issuer = issuerSubjectName;
            this.HashAlgorithmName = hashAlgorithmName;
            this.ThisUpdate = DateTime.UtcNow;
            this.NextUpdate = DateTime.MinValue;
            this.RevokedCertificates = serialNumbers.Select(s => new RevokedCertificate(s)).ToList();
            this.CrlExtensions = new List<X509Extension>();
        }

#if NETSTANDARD2_1
        // TODO
        public X509CRL Create(X509SignatureGenerator generator)
        {
            return null;
        }
#endif

        /// <summary>
        /// Constructs Certificate Revocation List raw data in X509 ASN format.
        /// </summary>
        public byte[] GetEncoded()
        {
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
                crlWriter.WriteEncodedValue((ReadOnlySpan<byte>)this.Issuer.RawData);

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

                    BigInteger srlNumberValue = new BigInteger(revokedCert.UserCertificate);
                    crlWriter.WriteInteger(srlNumberValue);
                    crlWriter.WriteUtcTime(revokedCert.RevocationDate);

                    if (revokedCert.CrlEntryExtensions.Count > 0)
                    {
                        crlWriter.PushSequence();
                        foreach (var crlEntryExt in revokedCert.CrlEntryExtensions)
                        {
                            crlWriter.WriteExtension(crlEntryExt);
                        }
                        crlWriter.PopSequence();
                    }
                    crlWriter.PopSequence();
                }

                crlWriter.PopSequence();

                // CRL extensions
                if (CrlExtensions.Count > 0)
                {
                    // [0]  EXPLICIT Extensions OPTIONAL
                    var tag = new Asn1Tag(TagClass.ContextSpecific, 0);
                    crlWriter.PushSequence(tag);

                    // CRL extensions
                    crlWriter.PushSequence();
                    foreach (var extension in CrlExtensions)
                    {
                        crlWriter.WriteExtension(extension);
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
