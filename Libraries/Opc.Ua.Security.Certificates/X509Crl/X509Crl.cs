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
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.IO;
using System.Security.Cryptography;
using System.Formats.Asn1;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Decodes a X509 CRL and provides access to information.
    /// </summary>
    public class X509CRL : IX509CRL
    {
        #region Constructors
        /// <summary>
        /// Loads a CRL from a file.
        /// </summary>
        public X509CRL(string filePath) : this()
        {
            RawData = File.ReadAllBytes(filePath);
        }

        /// <summary>
        /// Loads a CRL from a memory buffer.
        /// </summary>
        public X509CRL(byte[] crl) : this()
        {
            RawData = crl;
        }

        /// <summary>
        /// Create CRL from IX509CRL interface.
        /// </summary>
        /// <param name="crl"></param>
        public X509CRL(IX509CRL crl)
        {
            m_decoded = true;
            m_issuerName = crl.IssuerName;
            m_hashAlgorithmName = crl.HashAlgorithmName;
            m_thisUpdate = crl.ThisUpdate;
            m_nextUpdate = crl.NextUpdate;
            m_revokedCertificates = new List<RevokedCertificate>(crl.RevokedCertificates);
            m_crlExtensions = new X509ExtensionCollection();
            foreach (var extension in crl.CrlExtensions)
            {
                m_crlExtensions.Add(extension);
            }
            RawData = crl.RawData;
        }

        /// <summary>
        /// Default constructor, also internal test hook.
        /// </summary>
        internal X509CRL()
        {
            m_decoded = false;
            m_thisUpdate = DateTime.MinValue;
            m_nextUpdate = DateTime.MinValue;
            m_revokedCertificates = new List<RevokedCertificate>();
            m_crlExtensions = new X509ExtensionCollection();
        }
        #endregion

        #region IX509CRL Interface
        /// <inheritdoc/>
        public X500DistinguishedName IssuerName
        {
            get
            {
                EnsureDecoded();
                return m_issuerName;
            }
        }

        /// <inheritdoc/>
        public string Issuer => IssuerName.Name;

        /// <inheritdoc/>
        public DateTime ThisUpdate
        {
            get
            {
                EnsureDecoded();
                return m_thisUpdate;
            }
        }

        /// <inheritdoc/>
        public DateTime NextUpdate
        {
            get
            {
                EnsureDecoded();
                return m_nextUpdate;
            }
        }

        /// <inheritdoc/>
        public HashAlgorithmName HashAlgorithmName
        {
            get
            {
                EnsureDecoded();
                return m_hashAlgorithmName;
            }
        }

        /// <inheritdoc/>
        public IList<RevokedCertificate> RevokedCertificates
        {
            get
            {
                EnsureDecoded();
                return m_revokedCertificates.AsReadOnly();
            }
        }

        /// <inheritdoc/>
        public X509ExtensionCollection CrlExtensions
        {
            get
            {
                EnsureDecoded();
                return m_crlExtensions;
            }
        }

        /// <inheritdoc/>
        public byte[] RawData { get; private set; }
        #endregion

        #region Public Methods
        /// <summary>
        /// Verifies the signature on the CRL.
        /// </summary>
        public bool VerifySignature(X509Certificate2 issuer, bool throwOnError)
        {
            bool result;
            try
            {
                var signature = new X509Signature(RawData);
                result = signature.Verify(issuer);
            }
            catch (Exception)
            {
                result = false;
            }
            if (!result && throwOnError)
            {
                throw new CryptographicException("Could not verify signature on CRL.");
            }
            return result;
        }

        /// <summary>
        /// Returns true if the certificate is revoked in the CRL.
        /// </summary>
        public bool IsRevoked(X509Certificate2 certificate)
        {
            if (certificate.IssuerName.Equals(IssuerName))
            {
                throw new CryptographicException("Certificate was not created by the CRL Issuer.");
            }
            EnsureDecoded();
            var serialnumber = certificate.GetSerialNumber();
            foreach (var revokedCert in RevokedCertificates)
            {
                if (serialnumber.SequenceEqual<byte>(revokedCert.UserCertificate))
                {
                    return true;
                }
            }
            return false;
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Decode the complete CRL.
        /// </summary>
        /// <param name="crl">The raw signed CRL</param>
        internal void Decode(byte[] crl)
        {
            // Decode the Tbs and signature
            m_signature = new X509Signature(crl);
            // Decode the TbsCertList
            DecodeCrl(m_signature.Tbs);
        }

        /// <summary>
        /// Decode the Tbs of the CRL.
        /// </summary>
        /// <param name="tbs">The raw TbsCertList of the CRL.</param>
        internal void DecodeCrl(byte[] tbs)
        {
            try
            {
                AsnReader crlReader = new AsnReader(tbs, AsnEncodingRules.DER);
                var tag = Asn1Tag.Sequence;
                var seqReader = crlReader.ReadSequence(tag);
                crlReader.ThrowIfNotEmpty();
                if (seqReader != null)
                {
                    // Version is OPTIONAL
                    uint version = 0;
                    var intTag = new Asn1Tag(UniversalTagNumber.Integer);
                    var peekTag = seqReader.PeekTag();
                    if (peekTag == intTag)
                    {
                        if (seqReader.TryReadUInt32(out version))
                        {
                            if (version != 1)
                            {
                                throw new AsnContentException($"The CRL contains an incorrect version {version}");
                            }
                        }
                    }

                    // Signature Algorithm Identifier
                    var sigReader = seqReader.ReadSequence();
                    var oid = sigReader.ReadObjectIdentifier();
                    m_hashAlgorithmName = Oids.GetHashAlgorithmName(oid);
                    if (sigReader.HasData)
                    {
                        sigReader.ReadNull();
                    }
                    sigReader.ThrowIfNotEmpty();

                    // Issuer
                    m_issuerName = new X500DistinguishedName(seqReader.ReadEncodedValue().ToArray());

                    // thisUpdate
                    m_thisUpdate = seqReader.ReadUtcTime().UtcDateTime;

                    // nextUpdate is OPTIONAL
                    var utcTag = new Asn1Tag(UniversalTagNumber.UtcTime);
                    peekTag = seqReader.PeekTag();
                    if (peekTag == utcTag)
                    {
                        m_nextUpdate = seqReader.ReadUtcTime().UtcDateTime;
                    }

                    var seqTag = new Asn1Tag(UniversalTagNumber.Sequence, true);
                    peekTag = seqReader.PeekTag();
                    if (peekTag == seqTag)
                    {
                        // revoked certificates
                        var revReader = seqReader.ReadSequence(tag);
                        var revokedCertificates = new List<RevokedCertificate>();
                        while (revReader.HasData)
                        {
                            var crlEntry = revReader.ReadSequence();
                            var serial = crlEntry.ReadInteger();
                            var revokedCertificate = new RevokedCertificate(serial.ToByteArray());
                            revokedCertificate.RevocationDate = crlEntry.ReadUtcTime().UtcDateTime;
                            if (version == 1 &&
                                crlEntry.HasData)
                            {
                                // CRL entry extensions
                                var crlEntryExtensions = crlEntry.ReadSequence();
                                while (crlEntryExtensions.HasData)
                                {
                                    var extension = crlEntryExtensions.ReadExtension();
                                    revokedCertificate.CrlEntryExtensions.Add(extension);
                                }
                                crlEntryExtensions.ThrowIfNotEmpty();
                            }
                            crlEntry.ThrowIfNotEmpty();
                            revokedCertificates.Add(revokedCertificate);
                        }
                        revReader.ThrowIfNotEmpty();
                        m_revokedCertificates = revokedCertificates;
                    }

                    // CRL extensions OPTIONAL
                    if (version == 1 &&
                        seqReader.HasData)
                    {
                        var extTag = new Asn1Tag(TagClass.ContextSpecific, 0);
                        var optReader = seqReader.ReadSequence(extTag);
                        var crlExtensionList = new X509ExtensionCollection();
                        var crlExtensions = optReader.ReadSequence();
                        while (crlExtensions.HasData)
                        {
                            var extension = crlExtensions.ReadExtension();
                            crlExtensionList.Add(extension);
                        }
                        m_crlExtensions = crlExtensionList;
                    }
                    seqReader.ThrowIfNotEmpty();
                    m_decoded = true;
                    return;
                }
                throw new CryptographicException("The CRL contains ivalid data.");
            }
            catch (AsnContentException ace)
            {
                throw new CryptographicException("Failed to decode the CRL.", ace);
            }
        }

        /// <summary>
        /// Decode if RawData is yet undecoded.
        /// </summary>
        private void EnsureDecoded()
        {
            if (!m_decoded)
            {
                Decode(RawData);
            }
        }
        #endregion

        #region Private Fields
        private bool m_decoded = false;
        private X509Signature m_signature;
        private X500DistinguishedName m_issuerName;
        private DateTime m_thisUpdate;
        private DateTime m_nextUpdate;
        private HashAlgorithmName m_hashAlgorithmName;
        private List<RevokedCertificate> m_revokedCertificates;
        private X509ExtensionCollection m_crlExtensions;
        #endregion
    }
}
