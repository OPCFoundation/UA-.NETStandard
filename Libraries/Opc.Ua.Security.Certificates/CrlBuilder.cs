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
using System.Numerics;
using System.Linq;
using System.Collections.Generic;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Builds a CRL.
    /// </summary>
    public sealed class CrlBuilder : IX509CRL
    {
        #region Constructors
        /// <summary>
        /// Initialize a CRL builder with a decoded CRL.
        /// </summary>
        /// <param name="crl">The decoded CRL</param>
        public CrlBuilder(IX509CRL crl)
        {
            IssuerName = crl.IssuerName;
            HashAlgorithmName = crl.HashAlgorithmName;
            ThisUpdate = crl.ThisUpdate;
            NextUpdate = crl.NextUpdate;
            RawData = crl.RawData;
            m_revokedCertificates = new List<RevokedCertificate>(crl.RevokedCertificates);
            m_crlExtensions = new List<X509Extension>(crl.CrlExtensions);
        }

        /// <summary>
        /// Initialize the CRL builder with Issuer and hash algorithm.
        /// </summary>
        /// <param name="issuerSubjectName">Issuer distinguished name</param>
        /// <param name="hashAlgorithmName">The signing algorithm to use.</param>
        public CrlBuilder(X500DistinguishedName issuerSubjectName, HashAlgorithmName hashAlgorithmName) : this()
        {
            IssuerName = issuerSubjectName;
            HashAlgorithmName = hashAlgorithmName;
            ThisUpdate = DateTime.UtcNow;
        }

        /// <summary>
        /// Initialize the CRL builder with Issuer, hash algorithm and .
        /// </summary>
        /// <param name="issuerSubjectName">Issuer distinguished name</param>
        /// <param name="hashAlgorithmName">The signing algorithm to use.</param>
        /// <param name="serialNumbers">The array of serial numbers to revoke.</param>
        public CrlBuilder(X500DistinguishedName issuerSubjectName, HashAlgorithmName hashAlgorithmName, string[] serialNumbers)
            : this(issuerSubjectName, hashAlgorithmName)
        {
            m_revokedCertificates.AddRange(serialNumbers.Select(s => new RevokedCertificate(s)).ToList());
        }

        /// <summary>
        /// Default constructor.
        /// </summary>
        private CrlBuilder()
        {
            ThisUpdate = DateTime.MinValue;
            NextUpdate = DateTime.MinValue;
            m_revokedCertificates = new List<RevokedCertificate>();
            m_crlExtensions = new List<X509Extension>();
        }
        #endregion

        #region IX509CRL Interface
        /// <inheritdoc/>
        public X500DistinguishedName IssuerName { get; }

        /// <inheritdoc/>
        public string Issuer => IssuerName.Name;

        /// <inheritdoc/>
        public DateTime ThisUpdate { get; set; }

        /// <inheritdoc/>
        public DateTime NextUpdate { get; set; }

        /// <inheritdoc/>
        public HashAlgorithmName HashAlgorithmName { get; private set; }

        /// <inheritdoc/>
        public IList<RevokedCertificate> RevokedCertificates => m_revokedCertificates;

        /// <inheritdoc/>
        public IList<X509Extension> CrlExtensions => m_crlExtensions;

        /// <inheritdoc/>
        public byte[] RawData { get; private set; }
        #endregion

        #region Public Methods
        /// <summary>
        /// Create the CRL with signature.
        /// </summary>
        /// <param name="generator">The RSA or ECDSA signature generator to use.</param>
        /// <returns>The signed certificate.</returns>
        public byte [] Create(X509SignatureGenerator generator)
        {
            var tbsRawData = Encode();
            var signatureAlgorithm = generator.GetSignatureAlgorithmIdentifier(HashAlgorithmName);
            byte[] signature = generator.SignData(tbsRawData, HashAlgorithmName);
            var crlSigner = new X509Signature(tbsRawData, signature, HashAlgorithmName);
            RawData = crlSigner.Encode();
            return RawData;
        }
        #endregion

        #region Internal Methods
        /// <summary>
        /// Constructs Certificate Revocation List raw data in X509 ASN format.
        /// </summary>
        /// <remarks>
        /// CRL fields -- https://tools.ietf.org/html/rfc5280#section-5.1
        /// 
        /// CertificateList  ::=  SEQUENCE  {
        ///    tbsCertList          TBSCertList,
        ///    signatureAlgorithm   AlgorithmIdentifier,
        ///    signatureValue       BIT STRING
        ///    }
        ///
        /// TBSCertList  ::=  SEQUENCE  {
        ///    version                 Version OPTIONAL,
        ///                            -- if present, MUST be v2
        ///    signature               AlgorithmIdentifier,
        ///    issuer                  Name,
        ///    thisUpdate              Time,
        ///    nextUpdate              Time OPTIONAL,
        ///    revokedCertificates     SEQUENCE OF SEQUENCE  {
        ///        userCertificate         CertificateSerialNumber,
        ///        revocationDate          Time,
        ///        crlEntryExtensions      Extensions OPTIONAL
        ///                              -- if present, version MUST be v2
        ///                            }  OPTIONAL,
        ///    crlExtensions           [0]  EXPLICIT Extensions OPTIONAL
        ///                              -- if present, version MUST be v2
        ///                            }
        /// </remarks>
        internal byte[] Encode()
        {
            AsnWriter crlWriter = new AsnWriter(AsnEncodingRules.DER);
            {
                // tbsCertList
                crlWriter.PushSequence();

                // version
                crlWriter.WriteInteger(1);

                // Signature Algorithm Identifier
                crlWriter.PushSequence();
                string signatureAlgorithm = Oids.GetRSAOid(HashAlgorithmName);
                crlWriter.WriteObjectIdentifier(signatureAlgorithm);
                crlWriter.WriteNull();

                // pop
                crlWriter.PopSequence();

                // Issuer
                crlWriter.WriteEncodedValue((ReadOnlySpan<byte>)IssuerName.RawData);

                // this update
                crlWriter.WriteUtcTime(this.ThisUpdate);

                if (NextUpdate != DateTime.MinValue &&
                    NextUpdate > ThisUpdate)
                {
                    // next update
                    crlWriter.WriteUtcTime(NextUpdate);
                }

                // sequence to start the revoked certificates.
                crlWriter.PushSequence();

                foreach (var revokedCert in RevokedCertificates)
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
        #endregion

        #region Private Fields
        private List<RevokedCertificate> m_revokedCertificates;
        private List<X509Extension> m_crlExtensions;
        #endregion
    }
}
