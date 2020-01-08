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
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Opc.Ua
{
    /// <summary>
    /// Stores the subject alternate name extension.
    /// </summary>
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
        /// Creates an extension from ASN.1 encoded data.
        /// </summary>
        public X509SubjectAltNameExtension(string oid, byte[] rawData, bool critical)
        :
            this(new Oid(oid, s_FriendlyName), rawData, critical)
        {
        }

        /// <summary>
        /// Creates an extension from ASN.1 encoded data.
        /// </summary>
        public X509SubjectAltNameExtension(Oid oid, byte[] rawData, bool critical)
        :
            base(oid, rawData, critical)
        {
            Parse(rawData);
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

                buffer.Append(s_UniformResourceIdentifier);
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

                buffer.Append(s_DnsName);
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

                buffer.Append(s_IpAddress);
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
            Parse(asnEncodedData.RawData);
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The OID for a Subject Alternate Name extension.
        /// </summary>
        public static string SubjectAltNameOid
        {
            get { return s_SubjectAltNameOid; }
        }

        /// <summary>
        /// The OID for a Subject Alternate Name 2 extension.
        /// </summary>
        public static string SubjectAltName2Oid
        {
            get { return s_SubjectAltName2Oid; }
        }

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
        /// Extract URI, DNS and IP from Subject Alternative Name.
        /// </summary>
        private void ParseSubjectAltNameUsageExtension(Org.BouncyCastle.Asn1.X509.GeneralNames generalNames)
        {
            List<string> uris = new List<string>();
            List<string> domainNames = new List<string>();
            List<string> ipAddresses = new List<string>();
            foreach (var generalName in generalNames.GetNames())
            {
                switch (generalName.TagNo)
                {
                    case Org.BouncyCastle.Asn1.X509.GeneralName.UniformResourceIdentifier:
                        uris.Add(generalName.Name.ToString());
                        break;
                    case Org.BouncyCastle.Asn1.X509.GeneralName.DnsName:
                        domainNames.Add(generalName.Name.ToString());
                        break;
                    case Org.BouncyCastle.Asn1.X509.GeneralName.IPAddress:
                        ipAddresses.Add(IPAddressToString(Org.BouncyCastle.Asn1.DerOctetString.GetInstance(generalName.Name).GetOctets()));
                        break;
                    default:
                        break;
                }
            }
            m_uris = new ReadOnlyList<string>(uris);
            m_domainNames = new ReadOnlyList<string>(domainNames);
            m_ipAddresses = new ReadOnlyList<string>(ipAddresses);
        }

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

        /// <summary>
        /// Parse certificate for alternate name extension.
        /// </summary>
        private void Parse(byte[] data)
        {
            if (base.Oid.Value == SubjectAltNameOid ||
                base.Oid.Value == SubjectAltName2Oid)
            {
                Org.BouncyCastle.Asn1.Asn1OctetString altNames = new Org.BouncyCastle.Asn1.DerOctetString(data);
                var altNamesObjects = Org.BouncyCastle.X509.Extension.X509ExtensionUtilities.FromExtensionValue(altNames);
                ParseSubjectAltNameUsageExtension(Org.BouncyCastle.Asn1.X509.GeneralNames.GetInstance(altNamesObjects));
            }
            else
            {
                throw new ServiceResultException(
                    StatusCodes.BadCertificateInvalid,
                    "Certificate uses unknown SubjectAltNameOid.");
            }
        }
        #endregion

        #region Private Fields
        /// <summary>
        /// Subject Alternate Name extension string
        /// definitions see RFC 3280 4.2.1.7
        /// </summary>
        private const string s_UniformResourceIdentifier = "URL";
        private const string s_DnsName = "DNS Name";
        private const string s_IpAddress = "IP Address";

        private const string s_SubjectAltNameOid = "2.5.29.7";
        private const string s_SubjectAltName2Oid = "2.5.29.17";
        private const string s_FriendlyName = "Subject Alternative Name";
        private ReadOnlyList<string> m_uris;
        private ReadOnlyList<string> m_domainNames;
        private ReadOnlyList<string> m_ipAddresses;
        #endregion
    }
}
