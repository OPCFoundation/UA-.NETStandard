/* Copyright (c) 1996-2015, OPC Foundation. All rights reserved.

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
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Opc.Ua.Security.Certificates.X509
{
    /// <summary>
    /// The subject alternate name extension.
    /// </summary>
    /// <remarks>
    /// 
    /// id-ce-subjectAltName OBJECT IDENTIFIER::=  { id-ce 17 }
    /// 
    /// SubjectAltName::= GeneralNames
    /// 
    ///    GeneralNames::= SEQUENCE SIZE(1..MAX) OF GeneralName
    /// 
    ///    GeneralName ::= CHOICE {
    ///        otherName                       [0] OtherName,
    ///        rfc822Name[1]                   IA5String,
    ///        dNSName[2]                      IA5String,
    ///        x400Address[3]                  ORAddress,
    ///        directoryName[4]                Name,
    ///        ediPartyName[5]                 EDIPartyName,
    ///        uniformResourceIdentifier[6]    IA5String,
    ///        iPAddress[7]                    OCTET STRING,
    ///        registeredID[8]                 OBJECT IDENTIFIER
    ///        }
    /// 
    ///    OtherName::= SEQUENCE {
    ///        type-id                         OBJECT IDENTIFIER,
    ///        value[0] EXPLICIT ANY DEFINED BY type - id
    ///        }
    /// 
    ///    EDIPartyName::= SEQUENCE {
    ///        nameAssigner[0]                 DirectoryString OPTIONAL,
    ///        partyName[1]                    DirectoryString
    ///        }
    /// 
    /// </remarks>
    public class X509SubjectAltNameExtension : X509Extension
    {
        #region Constructors
        /// <summary>
        /// Creates an empty extension.
        /// </summary>
        protected X509SubjectAltNameExtension()
        {
        }

        /// <summary>
        /// Creates an extension from ASN.1 encoded data.
        /// </summary>
        public X509SubjectAltNameExtension(AsnEncodedData encodedExtension, bool critical)
        :
            this(encodedExtension.Oid, encodedExtension.RawData, critical)
        {
        }

        /// <summary>
        /// Creates an extension from an Oid and ASN.1 encoded raw data.
        /// </summary>
        public X509SubjectAltNameExtension(string oid, byte[] rawData, bool critical)
        :
            this(new Oid(oid, kFriendlyName), rawData, critical)
        {
        }

#if NETSTANDARD2_1
        /// <summary>
        /// Build the Subject Alternative name extension (for OPC UA application certs).
        /// </summary>
        /// <param name="applicationUri">The application Uri</param>
        /// <param name="domainNames">The domain names. DNS Hostnames, IPv4 or IPv6 addresses</param>
        public X509SubjectAltNameExtension(
            string applicationUri,
            IList<string> domainNames)
        {
            this.Oid = new Oid(SubjectAltName2Oid, kFriendlyName);
            this.Critical = false;
            this.Initialize(applicationUri, domainNames);
            this.RawData = Encode();
        }
#endif

        /// <summary>
        /// Creates an extension from ASN.1 encoded data.
        /// </summary>
        public X509SubjectAltNameExtension(Oid oid, byte[] rawData, bool critical)
        :
            base(oid, rawData, critical)
        {
            Decode(rawData);
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Returns a formatted version of the Abstract Syntax Notation One (ASN.1)-encoded data as a string.
        /// </summary>
        public override string Format(bool multiLine)
        {
            StringBuilder buffer = new StringBuilder();

            for (int ii = 0; ii < m_uris.Count; ii++)
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

                buffer.Append(kUniformResourceIdentifier);
                buffer.Append("=");
                buffer.Append(m_uris[ii]);
            }

            for (int ii = 0; ii < m_domainNames.Count; ii++)
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

                buffer.Append(kDnsName);
                buffer.Append("=");
                buffer.Append(m_domainNames[ii]);
            }

            for (int ii = 0; ii < m_ipAddresses.Count; ii++)
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

                buffer.Append(kIpAddress);
                buffer.Append("=");
                buffer.Append(m_ipAddresses[ii]);
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
            Decode(asnEncodedData.RawData);
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The OID for a Subject Alternate Name extension.
        /// </summary>
        public const string SubjectAltNameOid = "2.5.29.7";

        /// <summary>
        /// The OID for a Subject Alternate Name 2 extension.
        /// </summary>
        public static string SubjectAltName2Oid = "2.5.29.17";

        /// <summary>
        /// Gets the uris.
        /// </summary>
        /// <value>The uris.</value>
        public ReadOnlyList<string> Uris
        {
            get { return m_uris; }
        }

        /// <summary>
        /// Gets the domain names.
        /// </summary>
        /// <value>The domain names.</value>
        public ReadOnlyList<string> DomainNames
        {
            get { return m_domainNames; }
        }

        /// <summary>
        /// Gets the IP addresses.
        /// </summary>
        /// <value>The IP addresses.</value>
        public ReadOnlyList<string> IPAddresses
        {
            get { return m_ipAddresses; }
        }
        #endregion

        #region Private Methods
        /// <summary>
        /// Create a normalized IPv4 or IPv6 address from a 4 byte or 16 byte array.
        /// </summary>
        private string IPAddressToString(byte[] encodedIPAddress)
        {
            try
            {
                IPAddress address = new IPAddress(encodedIPAddress);
                return address.ToString();
            }
            catch
            {
                throw new ServiceResultException(
                    StatusCodes.BadCertificateInvalid,
                    "Certificate contains invalid IP address.");
            }
        }

#if NETSTANDARD2_1
        /// <summary>
        /// Encode the Subject Alternative name extension.
        /// </summary>
        private byte[] Encode()
        {
            var sanBuilder = new SubjectAlternativeNameBuilder();
            foreach (var uri in m_uris)
            {
                sanBuilder.AddUri(new Uri(uri));
            }
            EncodeGeneralNames(sanBuilder, m_domainNames);
            EncodeGeneralNames(sanBuilder, m_ipAddresses);
            var extension = sanBuilder.Build();
            return extension.RawData;
        }

        /// <summary>
        /// Encode a list of general Names in a SAN builder.
        /// </summary>
        /// <param name="sanBuilder">The subject alternative name builder</param>
        /// <param name="generalNames">The general Names to add</param>
        private static void EncodeGeneralNames(SubjectAlternativeNameBuilder sanBuilder, IList<string> generalNames)
        {
            foreach (string generalName in generalNames)
            {
                IPAddress ipAddr;
                if (String.IsNullOrWhiteSpace(generalName))
                {
                    continue;
                }
                if (IPAddress.TryParse(generalName, out ipAddr))
                {
                    sanBuilder.AddIpAddress(ipAddr);
                }
                else
                {
                    sanBuilder.AddDnsName(generalName);
                }
            }
        }
#endif

        /// <summary>
        /// Decode URI, DNS and IP from Subject Alternative Name.
        /// </summary>
        /// <remarks>
        /// Only general names relevant for Opc.Ua are decoded.
        /// </remarks>
        private void Decode(byte[] data)
        {
            if (base.Oid.Value == SubjectAltNameOid ||
                base.Oid.Value == SubjectAltName2Oid)
            {
                try
                {
                    List<string> uris = new List<string>();
                    List<string> domainNames = new List<string>();
                    List<string> ipAddresses = new List<string>();
                    Asn1Tag uriTag = new Asn1Tag(TagClass.ContextSpecific, 6);
                    Asn1Tag dnsTag = new Asn1Tag(TagClass.ContextSpecific, 2);
                    Asn1Tag ipTag = new Asn1Tag(TagClass.ContextSpecific, 7);
                    AsnReader dataReader = new AsnReader(data, AsnEncodingRules.DER);
                    var akiReader = dataReader?.ReadSequence();
                    if (akiReader != null)
                    {
                        Asn1Tag peekTag;
                        while (akiReader.HasData)
                        {
                            peekTag = akiReader.PeekTag();
                            if (peekTag == uriTag)
                            {
                                var uri = akiReader.ReadCharacterString(UniversalTagNumber.IA5String,
                                    new Asn1Tag(TagClass.ContextSpecific, 6));
                                uris.Add(uri);
                            }
                            else if (peekTag == dnsTag)
                            {
                                var dnsName = akiReader.ReadCharacterString(UniversalTagNumber.IA5String,
                                    new Asn1Tag(TagClass.ContextSpecific, 2));
                                domainNames.Add(dnsName);
                            }
                            else if (peekTag == ipTag)
                            {
                                var ip = akiReader.ReadOctetString(new Asn1Tag(TagClass.ContextSpecific, 7));
                                ipAddresses.Add(IPAddressToString(ip));
                            }
                            else  // skip over
                            {
                                akiReader.ReadEncodedValue();
                            }
                        }
                    }
                    m_uris = new ReadOnlyList<string>(uris);
                    m_domainNames = new ReadOnlyList<string>(domainNames);
                    m_ipAddresses = new ReadOnlyList<string>(ipAddresses);
                }
                catch (AsnContentException)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadCertificateInvalid,
                        "Certificate has invalid ASN content in the SubjectAltName extension.");
                }
            }
            else
            {
                throw new ServiceResultException(
                    StatusCodes.BadCertificateInvalid,
                    "Certificate uses unknown SubjectAltNameOid.");
            }
        }

        /// <summary>
        /// Initialize the Subject Alternative name extension.
        /// </summary>
        /// <param name="applicationUri">The application Uri</param>
        /// <param name="generalNames">The general names. DNS Hostnames, IPv4 or IPv6 addresses</param>
        private void Initialize(string applicationUri, IList<string> generalNames)
        {
            List<string> uris = new List<string>();
            List<string> domainNames = new List<string>();
            List<string> ipAddresses = new List<string>();
            uris.Add(applicationUri);
            foreach (string generalName in generalNames)
            {
                IPAddress ipAddr;
                if (String.IsNullOrWhiteSpace(generalName))
                {
                    continue;
                }
                if (IPAddress.TryParse(generalName, out ipAddr))
                {
                    ipAddresses.Add(generalName);
                }
                else
                {
                    domainNames.Add(generalName);
                }
            }
            m_uris = new ReadOnlyList<string>(uris);
            m_domainNames = new ReadOnlyList<string>(domainNames);
            m_ipAddresses = new ReadOnlyList<string>(ipAddresses);
        }
        #endregion

        #region Private Fields
        /// <summary>
        /// Subject Alternate Name extension string
        /// definitions see RFC 5280 4.2.1.7
        /// </summary>
        private const string kUniformResourceIdentifier = "URL";
        private const string kDnsName = "DNS Name";
        private const string kIpAddress = "IP Address";
        private const string kFriendlyName = "Subject Alternative Name";
        private ReadOnlyList<string> m_uris;
        private ReadOnlyList<string> m_domainNames;
        private ReadOnlyList<string> m_ipAddresses;
        #endregion
    }
}
