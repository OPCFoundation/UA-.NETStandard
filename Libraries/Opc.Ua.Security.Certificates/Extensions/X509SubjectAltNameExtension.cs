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
using System.Collections.Generic;
using System.Formats.Asn1;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Opc.Ua.Security.Certificates
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
            : this(encodedExtension.Oid, encodedExtension.RawData, critical)
        {
        }

        /// <summary>
        /// Creates an extension from an Oid and ASN.1 encoded raw data.
        /// </summary>
        public X509SubjectAltNameExtension(string oid, byte[] rawData, bool critical)
            : this(new Oid(oid, kFriendlyName), rawData, critical)
        {
        }

        /// <summary>
        /// Creates an extension from ASN.1 encoded data.
        /// </summary>
        public X509SubjectAltNameExtension(Oid oid, byte[] rawData, bool critical)
        :
            base(oid, rawData, critical)
        {
            m_decoded = false;
        }

        /// <summary>
        /// Build the Subject Alternative name extension (for OPC UA application certs).
        /// </summary>
        /// <param name="applicationUri">The application Uri</param>
        /// <param name="domainNames">The domain names. DNS Hostnames, IPv4 or IPv6 addresses</param>
        public X509SubjectAltNameExtension(
            string applicationUri,
            IEnumerable<string> domainNames)
        {
            Oid = new Oid(SubjectAltName2Oid, kFriendlyName);
            Critical = false;
            Initialize(applicationUri, domainNames);
            RawData = Encode();
            m_decoded = true;
        }
        #endregion

        #region Overridden Methods
        /// <summary>
        /// Returns a formatted version of the Abstract Syntax Notation One (ASN.1)-encoded data as a string.
        /// </summary>
        public override string Format(bool multiLine)
        {
            EnsureDecoded();
            StringBuilder buffer = new StringBuilder();
            for (int ii = 0; ii < m_uris.Count; ii++)
            {
                if (buffer.Length > 0)
                {
                    if (multiLine)
                    {
                        buffer.AppendLine();
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
                        buffer.AppendLine();
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
                        buffer.AppendLine();
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
            Oid = asnEncodedData.Oid;
            RawData = asnEncodedData.RawData;
            m_decoded = false;
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
        public IReadOnlyList<string> Uris
        {
            get
            {
                EnsureDecoded();
                return m_uris.AsReadOnly();
            }
        }

        /// <summary>
        /// Gets the domain names.
        /// </summary>
        /// <value>The domain names.</value>
        public IReadOnlyList<string> DomainNames
        {
            get
            {
                EnsureDecoded();
                return m_domainNames.AsReadOnly();
            }
        }

        /// <summary>
        /// Gets the IP addresses.
        /// </summary>
        /// <value>The IP addresses.</value>
        public IReadOnlyList<string> IPAddresses
        {
            get
            {
                EnsureDecoded();
                return m_ipAddresses.AsReadOnly();
            }
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
                throw new CryptographicException("Certificate contains invalid IP address.");
            }
        }

#if NETSTANDARD2_1 || NET472
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
        private void EncodeGeneralNames(SubjectAlternativeNameBuilder sanBuilder, IList<string> generalNames)
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
#else  
        /// <summary>
        /// Encode the Subject Alternative name extension.
        /// </summary>
        private byte[] Encode()
        {
            return BouncyCastle.X509Extensions.BuildSubjectAltNameExtension(m_uris, m_domainNames, m_ipAddresses).RawData;
        }
#endif

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
                    AsnReader dataReader = new AsnReader(data, AsnEncodingRules.DER);
                    var akiReader = dataReader.ReadSequence();
                    dataReader.ThrowIfNotEmpty();
                    if (akiReader != null)
                    {
                        Asn1Tag uriTag = new Asn1Tag(TagClass.ContextSpecific, 6);
                        Asn1Tag dnsTag = new Asn1Tag(TagClass.ContextSpecific, 2);
                        Asn1Tag ipTag = new Asn1Tag(TagClass.ContextSpecific, 7);

                        while (akiReader.HasData)
                        {
                            Asn1Tag peekTag = akiReader.PeekTag();
                            if (peekTag == uriTag)
                            {
                                var uri = akiReader.ReadCharacterString(UniversalTagNumber.IA5String, uriTag);
                                uris.Add(uri);
                            }
                            else if (peekTag == dnsTag)
                            {
                                var dnsName = akiReader.ReadCharacterString(UniversalTagNumber.IA5String, dnsTag);
                                domainNames.Add(dnsName);
                            }
                            else if (peekTag == ipTag)
                            {
                                var ip = akiReader.ReadOctetString(ipTag);
                                ipAddresses.Add(IPAddressToString(ip));
                            }
                            else  // skip over
                            {
                                akiReader.ReadEncodedValue();
                            }
                        }
                        akiReader.ThrowIfNotEmpty();
                        m_uris = uris;
                        m_domainNames = domainNames;
                        m_ipAddresses = ipAddresses;
                        m_decoded = true;
                        return;
                    }
                    throw new CryptographicException("No valid data in the X509 signature.");
                }
                catch (AsnContentException ace)
                {
                    throw new CryptographicException("Failed to decode the SubjectAltName extension.", ace);
                }
            }
            throw new CryptographicException("Invalid SubjectAltNameOid.");
        }

        /// <summary>
        /// Initialize the Subject Alternative name extension.
        /// </summary>
        /// <param name="applicationUri">The application Uri</param>
        /// <param name="generalNames">The general names. DNS Hostnames, IPv4 or IPv6 addresses</param>
        private void Initialize(string applicationUri, IEnumerable<string> generalNames)
        {
            List<string> uris = new List<string>();
            List<string> domainNames = new List<string>();
            List<string> ipAddresses = new List<string>();
            uris.Add(applicationUri);
            foreach (string generalName in generalNames)
            {
                switch (Uri.CheckHostName(generalName))
                {
                    case UriHostNameType.Dns:
                        domainNames.Add(generalName); break;
                    case UriHostNameType.IPv4:
                    case UriHostNameType.IPv6:
                        ipAddresses.Add(generalName); break;
                    default: continue;
                }
            }
            m_uris = uris;
            m_domainNames = domainNames;
            m_ipAddresses = ipAddresses;
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
        private List<string> m_uris;
        private List<string> m_domainNames;
        private List<string> m_ipAddresses;
        private bool m_decoded;
        #endregion
    }
}
