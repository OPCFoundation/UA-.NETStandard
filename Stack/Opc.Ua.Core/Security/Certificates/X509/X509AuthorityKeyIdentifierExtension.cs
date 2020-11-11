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
using System.Text;

namespace Opc.Ua.Security.Certificates.X509
{
    /// <summary>
    /// Stores the authority key identifier extension.
    /// </summary>
    /// <remarks>
    ///     id-ce-authorityKeyIdentifier OBJECT IDENTIFIER ::=  { id-ce 35 }
    ///     AuthorityKeyIdentifier ::= SEQUENCE {
    ///         keyIdentifier[0] KeyIdentifier           OPTIONAL,
    ///         authorityCertIssuer[1] GeneralNames            OPTIONAL,
    ///         authorityCertSerialNumber[2] CertificateSerialNumber OPTIONAL
    ///         }
    ///     KeyIdentifier::= OCTET STRING
    /// </remarks>
    public class X509AuthorityKeyIdentifierExtension : X509Extension
    {
        #region Constructors
        /// <summary>
        /// Creates an empty extension.
        /// </summary>
        protected X509AuthorityKeyIdentifierExtension()
        {
        }

        /// <summary>
        /// Creates an extension from ASN.1 encoded data.
        /// </summary>
        public X509AuthorityKeyIdentifierExtension(AsnEncodedData encodedExtension, bool critical)
        :
            this(encodedExtension.Oid, encodedExtension.RawData, critical)
        {
        }

        /// <summary>
        /// Creates an extension from ASN.1 encoded data.
        /// </summary>
        public X509AuthorityKeyIdentifierExtension(string oid, byte[] rawData, bool critical)
        :
            this(new Oid(oid, kFriendlyName), rawData, critical)
        {
        }

        /// <summary>
        /// Build the X509 Authority Key extension.
        /// </summary>
        /// <param name="authorityName">The distinguished name of the issuer</param>
        /// <param name="serialNumber">The serial number of the issuer</param>
        /// <param name="subjectKeyIdentifier">The subject key identifier</param>
        public X509AuthorityKeyIdentifierExtension(
            X500DistinguishedName authorityName,
            byte[] serialNumber,
            byte[] subjectKeyIdentifier)
        {
            this.Oid = new Oid(AuthorityKeyIdentifier2Oid, kFriendlyName);
            this.Critical = false;
            m_Issuer = authorityName;
            m_keyIdentifier = subjectKeyIdentifier;
            m_serialNumber = serialNumber;
            this.RawData = Encode();
        }

        /// <summary>
        /// Creates an extension from ASN.1 encoded data.
        /// </summary>
        public X509AuthorityKeyIdentifierExtension(Oid oid, byte[] rawData, bool critical)
        :
            base(oid, rawData, critical)
        {
            Decode(rawData);
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Returns a formatted version of the Authority Key Identifier as a string.
        /// </summary>
        public override string Format(bool multiLine)
        {
            StringBuilder buffer = new StringBuilder();

            if (m_keyIdentifier != null && m_keyIdentifier.Length > 0)
            {
                if (buffer.Length > 0)
                {
                    if (multiLine)
                    {
                        buffer.Append("\r\n");
                    }
                    else
                    {
                        buffer.Append(", ");
                    }
                }

                buffer.Append(kKeyIdentifier);
                buffer.Append("=");
                buffer.Append(Utils.ToHexString(m_keyIdentifier));
            }

            if (m_Issuer != null)
            {
                if (multiLine)
                {
                    buffer.Append("\r\n");
                }
                else
                {
                    buffer.Append(", ");
                }

                buffer.Append(kIssuer);
                buffer.Append("=");
                buffer.Append(m_Issuer.Format(true));
            }

            if (m_serialNumber != null && m_serialNumber.Length > 0)
            {
                if (buffer.Length > 0)
                {
                    if (!multiLine)
                    {
                        buffer.Append(", ");
                    }
                }

                buffer.Append(kSerialNumber);
                buffer.Append("=");
                buffer.Append(Utils.ToHexString(m_serialNumber, true));
            }

            return buffer.ToString();
        }

        /// <summary>
        /// Initializes the extension from ASN.1 encoded data.
        /// </summary>
        public override void CopyFrom(AsnEncodedData asnEncodedData)
        {
            if (asnEncodedData == null) throw new ArgumentNullException(nameof(asnEncodedData));
            this.Oid = asnEncodedData.Oid;
            this.RawData = asnEncodedData.RawData;
            Decode(asnEncodedData.RawData);
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The OID for a Authority Key Identifier extension.
        /// </summary>
        public const string AuthorityKeyIdentifierOid = "2.5.29.1";

        /// <summary>
        /// The alternate OID for a Authority Key Identifier extension.
        /// </summary>
        public const string AuthorityKeyIdentifier2Oid = "2.5.29.35";

        /// <summary>
        /// The identifier for the key as a little endian hexadecimal string.
        /// </summary>
        public string KeyIdentifier => Utils.ToHexString(m_keyIdentifier);

        /// <summary>
        /// The identifier for the key as a byte array.
        /// </summary>
        public byte[] GetKeyIdentifier() => m_keyIdentifier;

        /// <summary>
        /// A list of distinguished names for the issuer.
        /// </summary>
        public X500DistinguishedName Issuer => m_Issuer;

        /// <summary>
        /// The serial number of the authority key as a big endian hexadecimal string.
        /// </summary>
        public string SerialNumber => Utils.ToHexString(m_serialNumber, true);

        /// <summary>
        /// The serial number of the authority key as a byte array in little endian order.
        /// </summary>
        public byte[] GetSerialNumber() => m_serialNumber;
        #endregion

        #region Private Methods
        private byte[] Encode()
        {
            AsnWriter writer = new AsnWriter(AsnEncodingRules.DER);
            writer.PushSequence();

            if (m_keyIdentifier != null)
            {
                Asn1Tag keyIdTag = new Asn1Tag(TagClass.ContextSpecific, 0);
                writer.WriteOctetString(m_keyIdentifier, keyIdTag);
            }

            if (m_Issuer != null)
            {
                Asn1Tag issuerNameTag = new Asn1Tag(TagClass.ContextSpecific, 1);
                writer.PushSequence(issuerNameTag);

                // Add the issuer to constructed context-specific 4 (GeneralName.directoryName)
                Asn1Tag directoryNameTag = new Asn1Tag(TagClass.ContextSpecific, 4, true);
                writer.PushSetOf(directoryNameTag);
                writer.WriteEncodedValue(m_Issuer.RawData);
                writer.PopSetOf(directoryNameTag);

                writer.PopSequence(issuerNameTag);
            }

            if (m_serialNumber != null)
            {
                Asn1Tag issuerSerialTag = new Asn1Tag(TagClass.ContextSpecific, 2);
                System.Numerics.BigInteger issuerSerial = new System.Numerics.BigInteger(m_serialNumber);
                writer.WriteInteger(issuerSerial, issuerSerialTag);
            }

            writer.PopSequence();
            return writer.Encode();
        }


        private void Decode(byte[] data)
        {
            if (base.Oid.Value == AuthorityKeyIdentifierOid ||
                base.Oid.Value == AuthorityKeyIdentifier2Oid)
            {
                AsnReader dataReader = new AsnReader(data, AsnEncodingRules.DER);
                var akiReader = dataReader?.ReadSequence();
                if (akiReader != null)
                {
                    Asn1Tag keyId = new Asn1Tag(TagClass.ContextSpecific, 0);
                    m_keyIdentifier = akiReader.ReadOctetString(keyId);

                    AsnReader issuerReader = akiReader.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 1));
                    if (issuerReader != null)
                    {
                        Asn1Tag directoryNameTag = new Asn1Tag(TagClass.ContextSpecific, 4, true);
                        m_Issuer = new X500DistinguishedName(issuerReader.ReadSequence(directoryNameTag).ReadEncodedValue().ToArray());
                    }

                    Asn1Tag serialNumber = new Asn1Tag(TagClass.ContextSpecific, 2);
                    m_serialNumber = akiReader.ReadInteger(serialNumber).ToByteArray();
                    return;
                }
            }
            throw new ServiceResultException(
                StatusCodes.BadCertificateInvalid,
                "Certificate uses unknown data or bad AuthorityKeyIdentifierOid.");
        }
        #endregion

        #region Private Fields
        /// <summary>
        /// Authority Key Identifier extension string
        /// definitions see RFC 5280 4.2.1.1
        /// </summary>
        private const string kKeyIdentifier = "Key Identifier";
        private const string kIssuer = "Issuer";
        private const string kSerialNumber = "Serial Number";
        private const string kFriendlyName = "Authority Key Identifier";
        private byte[] m_keyIdentifier;
        private X500DistinguishedName m_Issuer;
        private byte[] m_serialNumber;
        #endregion
    }
}
