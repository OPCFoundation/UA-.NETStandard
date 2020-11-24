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
    /// Provides access to an X509 CRL object.
    /// </summary>
    public sealed class X509CRL : IDisposable
    {
        #region Constructors
        /// <summary>
        /// Loads a CRL from a file.
        /// </summary>
        public X509CRL(string filePath) : this()
        {
            RawData = File.ReadAllBytes(filePath);
            Decode(RawData);
        }

        /// <summary>
        /// Loads a CRL from a memory buffer.
        /// </summary>
        public X509CRL(byte[] crl) : this()
        {
            RawData = crl;
            Decode(RawData);
        }

        internal X509CRL()
        {
            ThisUpdate = DateTime.MinValue;
            NextUpdate = DateTime.MinValue;
            RevokedCertificates = new List<RevokedCertificate>();
            CrlExtensions = new List<X509Extension>();
        }
        #endregion

        #region IDisposable Members
        /// <summary>
        /// The finalizer implementation.
        /// </summary>
        ~X509CRL()
        {
            Dispose(false);
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        private void Dispose(bool disposing)
        {
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The name of the issuer for the CRL.
        /// </summary>
        public X500DistinguishedName IssuerName { get; private set; }

        /// <summary>
        /// The name of the issuer for the CRL.
        /// </summary>
        public string Issuer => IssuerName.Name;

        /// <summary>
        /// When the CRL was last updated.
        /// </summary>
        public DateTime ThisUpdate { get; private set; }

        /// <summary>
        /// When the CRL is due for its next update.
        /// </summary>
        public DateTime NextUpdate { get; private set; }

        /// <summary>
        /// The hash algorithm used to sign the CRL.
        /// </summary>
        public HashAlgorithmName HashAlgorithmName { get; private set; }

        /// <summary>
        /// The revoked user certificates
        /// </summary>
        public IReadOnlyList<RevokedCertificate> RevokedCertificates { get; private set; }

        /// <summary>
        /// The X509Extensions of the CRL.
        /// </summary>
        public IReadOnlyList<X509Extension> CrlExtensions { get; private set;  }

        /// <summary>
        /// The raw data for the CRL.
        /// </summary>
        public byte[] RawData { get; private set; }
        #endregion

        #region Public Methods
        /// <summary>
        /// Verifies the signature on the CRL.
        /// </summary>
        public bool VerifySignature(X509Certificate2 issuer, bool throwOnError)
        {
            try
            {
#if TODO
                Org.BouncyCastle.X509.X509Certificate bccert = new X509CertificateParser().ReadCertificate(issuer.RawData);
                m_crl.Verify(bccert.GetPublicKey());
#endif
            }
            catch (Exception)
            {
                if (throwOnError)
                {
                    throw new CryptographicException("Could not verify signature on CRL.");
                }
                return false;
            }
            return true;
        }

        /// <summary>
        /// Returns true the certificate is in the CRL.
        /// </summary>
        public bool IsRevoked(X509Certificate2 certificate)
        {
            if (certificate.IssuerName.Equals(IssuerName))
            {
                throw new CryptographicException("Certificate was not created by the CRL Issuer.");
            }
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
        /// <param name="crl">The raw CRL.</param>
        private void Decode(byte[] crl)
        {
            m_signature = new X509Signature(crl);
            DecodeCrl(m_signature.Tbs);
            // TODO validate signature here
        }

        /// <summary>
        /// Decode the Tbs of the CRL.
        /// </summary>
        /// <param name="tbs">The raw Tbs data of the CRL.</param>
        internal void DecodeCrl(byte[] tbs)
        {
            try
            {
                AsnReader crlReader = new AsnReader(tbs, AsnEncodingRules.DER);
                var tag = Asn1Tag.Sequence;
                var seqReader = crlReader?.ReadSequence(tag);
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
                    HashAlgorithmName = OidConstants.GetHashAlgorithmName(oid);

                    // Issuer
                    IssuerName = new X500DistinguishedName(seqReader.ReadEncodedValue().ToArray());

                    // thisUpdate
                    ThisUpdate = seqReader.ReadUtcTime().UtcDateTime;

                    // nextUpdate is OPTIONAL
                    var utcTag = new Asn1Tag(UniversalTagNumber.UtcTime);
                    peekTag = seqReader.PeekTag();
                    if (peekTag == utcTag)
                    {
                        NextUpdate = seqReader.ReadUtcTime().UtcDateTime;
                    }

                    var seqTag = new Asn1Tag(UniversalTagNumber.Sequence, true);
                    peekTag = seqReader.PeekTag();
                    if (peekTag == seqTag)
                    {
                        // revoked certificates
                        var boolTag = new Asn1Tag(UniversalTagNumber.Boolean);
                        var revReader = seqReader.ReadSequence(tag);
                        var revokedCertificates = new List<RevokedCertificate>();
                        while (revReader.HasData)
                        {
                            var crlEntry = revReader.ReadSequence();
                            var serial = crlEntry.ReadInteger();
                            var revokedCertificate = new RevokedCertificate(serial.ToByteArray());
                            revokedCertificate.RevocationDate = crlEntry.ReadUtcTime().UtcDateTime;
                            if (crlEntry.HasData)
                            {
                                // CRL entry extensions
                                var crlEntryExtensions = crlEntry.ReadSequence();
                                while (crlEntryExtensions.HasData)
                                {
                                    var extension = crlEntryExtensions.ReadExtension();
                                    revokedCertificate.CrlEntryExtensions.Add(extension);
                                }
                            }
                            revokedCertificates.Add(revokedCertificate);
                        }
                        this.RevokedCertificates = revokedCertificates.AsReadOnly();
                    }

                    // CRL extensions
                    var extTag = new Asn1Tag(TagClass.ContextSpecific, 0);
                    var optReader = seqReader.ReadSequence(extTag);
                    if (optReader.HasData)
                    {
                        var crlExtensionList = new List<X509Extension>();
                        var crlExtensions = optReader.ReadSequence();
                        while (crlExtensions.HasData)
                        {
                            var extension = crlExtensions.ReadExtension();
                            crlExtensionList.Add(extension);
                        }
                        this.CrlExtensions = crlExtensionList.AsReadOnly();
                    }
                }
            }
            catch (Exception ex)
            {
                throw new CryptographicException("Failed to decode the CRL.", ex);
            }
        }
        #endregion

        #region Private Fields
        private X509Signature m_signature;
        #endregion
    }
}
