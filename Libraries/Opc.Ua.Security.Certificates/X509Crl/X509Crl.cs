/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.Formats.Asn1;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Security.Certificates
{
    /// <summary>
    /// Decodes a X509 CRL and provides access to information.
    /// </summary>
    public class X509CRL : IX509CRL
    {
        /// <summary>
        /// Loads a CRL from a file.
        /// </summary>
        public X509CRL(string filePath)
            : this()
        {
            RawData = File.ReadAllBytes(filePath);
            EnsureDecoded();
        }

        /// <summary>
        /// Loads a CRL from a memory buffer.
        /// </summary>
        public X509CRL(byte[] crl)
            : this()
        {
            RawData = crl;
            EnsureDecoded();
        }

        /// <summary>
        /// Create CRL from IX509CRL interface.
        /// </summary>
        public X509CRL(IX509CRL crl)
        {
            m_decoded = true;
            IssuerName = crl.IssuerName;
            HashAlgorithmName = crl.HashAlgorithmName;
            ThisUpdate = crl.ThisUpdate;
            NextUpdate = crl.NextUpdate;
            m_revokedCertificates = [.. crl.RevokedCertificates];
            CrlExtensions = [.. crl.CrlExtensions];
            RawData = crl.RawData;
            EnsureDecoded();
        }

        /// <summary>
        /// Default constructor, also internal test hook.
        /// </summary>
        internal X509CRL()
        {
            m_decoded = false;
            ThisUpdate = DateTime.MinValue;
            NextUpdate = DateTime.MinValue;
            m_revokedCertificates = [];
            CrlExtensions = [];
        }

        /// <inheritdoc/>
        public X500DistinguishedName IssuerName { get; private set; }

        /// <inheritdoc/>
        public string Issuer => IssuerName.Name;

        /// <inheritdoc/>
        public DateTime ThisUpdate { get; private set; }

        /// <inheritdoc/>
        public DateTime NextUpdate { get; private set; }

        /// <inheritdoc/>
        public HashAlgorithmName HashAlgorithmName { get; private set; }

        /// <inheritdoc/>
        public IList<RevokedCertificate> RevokedCertificates => m_revokedCertificates.AsReadOnly();

        /// <inheritdoc/>
        public X509ExtensionCollection CrlExtensions { get; private set; }

        /// <inheritdoc/>
        public byte[] RawData { get; }

        /// <summary>
        /// Verifies the signature on the CRL.
        /// </summary>
        /// <exception cref="CryptographicException"></exception>
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
        /// <exception cref="CryptographicException"></exception>
        public bool IsRevoked(X509Certificate2 certificate)
        {
            if (certificate.IssuerName.Equals(IssuerName))
            {
                throw new CryptographicException("Certificate was not created by the CRL Issuer.");
            }
            EnsureDecoded();
            byte[] serialnumber = certificate.GetSerialNumber();
            foreach (RevokedCertificate revokedCert in RevokedCertificates)
            {
                if (serialnumber.SequenceEqual(revokedCert.UserCertificate))
                {
                    return true;
                }
            }
            return false;
        }

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
        /// <exception cref="CryptographicException"></exception>
        /// <exception cref="AsnContentException"></exception>
        internal void DecodeCrl(byte[] tbs)
        {
            try
            {
                var crlReader = new AsnReader(tbs, AsnEncodingRules.DER);
                AsnReader seqReader = crlReader.ReadSequence(Asn1Tag.Sequence);
                crlReader.ThrowIfNotEmpty();
                if (seqReader != null)
                {
                    // Version is OPTIONAL
                    uint version = 0;
                    var intTag = new Asn1Tag(UniversalTagNumber.Integer);
                    Asn1Tag peekTag = seqReader.PeekTag();
                    if (peekTag == intTag && seqReader.TryReadUInt32(out version) && version != 1)
                    {
                        throw new AsnContentException(
                            $"The CRL contains an incorrect version {version}");
                    }

                    // Signature Algorithm Identifier
                    AsnReader sigReader = seqReader.ReadSequence();
                    string oid = sigReader.ReadObjectIdentifier();
                    HashAlgorithmName = Oids.GetHashAlgorithmName(oid);
                    if (sigReader.HasData)
                    {
                        sigReader.ReadNull();
                    }
                    sigReader.ThrowIfNotEmpty();

                    // Issuer
                    IssuerName = new X500DistinguishedName(seqReader.ReadEncodedValue().ToArray());

                    // thisUpdate
                    ThisUpdate = ReadTime(seqReader, optional: false);

                    // nextUpdate is OPTIONAL
                    NextUpdate = ReadTime(seqReader, optional: true);

                    // revokedCertificates is OPTIONAL
                    if (seqReader.HasData)
                    {
                        var seqTag = new Asn1Tag(UniversalTagNumber.Sequence, true);
                        peekTag = seqReader.PeekTag();
                        if (peekTag == seqTag)
                        {
                            // revoked certificates
                            AsnReader revReader = seqReader.ReadSequence(Asn1Tag.Sequence);
                            var revokedCertificates = new List<RevokedCertificate>();
                            while (revReader.HasData)
                            {
                                AsnReader crlEntry = revReader.ReadSequence();
                                System.Numerics.BigInteger serial = crlEntry.ReadInteger();
                                var revokedCertificate = new RevokedCertificate(
                                    serial.ToByteArray())
                                {
                                    RevocationDate = ReadTime(crlEntry, optional: false)
                                };
                                if (version == 1 && crlEntry.HasData)
                                {
                                    // CRL entry extensions
                                    AsnReader crlEntryExtensions = crlEntry.ReadSequence();
                                    while (crlEntryExtensions.HasData)
                                    {
                                        X509Extension extension = crlEntryExtensions
                                            .ReadExtension();
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
                        if (version == 1 && seqReader.HasData)
                        {
                            var extTag = new Asn1Tag(TagClass.ContextSpecific, 0);
                            AsnReader optReader = seqReader.ReadSequence(extTag);
                            var crlExtensionList = new X509ExtensionCollection();
                            AsnReader crlExtensions = optReader.ReadSequence();
                            while (crlExtensions.HasData)
                            {
                                X509Extension extension = crlExtensions.ReadExtension();
                                crlExtensionList.Add(extension);
                            }
                            CrlExtensions = crlExtensionList;
                        }
                    }
                    seqReader.ThrowIfNotEmpty();
                    m_decoded = true;
                    return;
                }
                throw new CryptographicException("The CRL contains invalid data.");
            }
            catch (AsnContentException ace)
            {
                throw new CryptographicException("Failed to decode the CRL.", ace);
            }
        }

        /// <summary>
        /// Read the time, UTC or local time
        /// </summary>
        /// <returns>The DateTime representing the tag</returns>
        /// <exception cref="AsnContentException"></exception>
        private static DateTime ReadTime(AsnReader asnReader, bool optional)
        {
            // determine if the time is UTC or GeneralizedTime time
            Asn1Tag timeTag = asnReader.PeekTag();
            if (timeTag.TagValue == Asn1Tag.UtcTime.TagValue)
            {
                return asnReader.ReadUtcTime().UtcDateTime;
            }
            else if (timeTag.TagValue == Asn1Tag.GeneralizedTime.TagValue)
            {
                return asnReader.ReadGeneralizedTime().UtcDateTime;
            }
            else if (optional)
            {
                return DateTime.MinValue;
            }
            else
            {
                throw new AsnContentException("The CRL contains an invalid time tag.");
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

        private bool m_decoded;
        private X509Signature m_signature;
        private List<RevokedCertificate> m_revokedCertificates;
    }

    /// <summary>
    /// A collection of X509CRL.
    /// </summary>
    [CollectionDataContract(Name = "ListOfX509CRL", ItemName = "X509CRL")]
    public class X509CRLCollection : List<X509CRL>
    {
        /// <summary>
        /// Gets or sets the element at the specified index.
        /// </summary>
        /// <param name="index">The zero-based index of the element to get or set.</param>
        /// <exception cref="ArgumentNullException"></exception>
        public new X509CRL this[int index]
        {
            get => base[index];
            set => base[index] = value ?? throw new ArgumentNullException(nameof(value));
        }

        /// <summary>
        /// Create an empty X509CRL collection.
        /// </summary>
        public X509CRLCollection()
        {
        }

        /// <summary>
        /// Create a crl collection from a single CRL.
        /// </summary>
        public X509CRLCollection(X509CRL crl)
        {
            Add(crl);
        }

        /// <summary>
        /// Create a crl collection from a CRL collection.
        /// </summary>
        public X509CRLCollection(X509CRLCollection crls)
        {
            AddRange(crls);
        }

        /// <summary>
        /// Create a collection from an array.
        /// </summary>
        public X509CRLCollection(X509CRL[] crls)
        {
            AddRange(crls);
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        public static X509CRLCollection ToX509CRLCollection(X509CRL[] crls)
        {
            if (crls != null)
            {
                return [.. crls];
            }
            return [];
        }

        /// <summary>
        /// Converts an array to a collection.
        /// </summary>
        public static implicit operator X509CRLCollection(X509CRL[] crls)
        {
            return ToX509CRLCollection(crls);
        }
    }
}
