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
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Opc.Ua
{
    /// <summary>
    /// Stores the authority key identifier extension.
    /// </summary>
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
            this(new Oid(oid, s_FriendlyName), rawData, critical)
        {
        }

        /// <summary>
        /// Creates an extension from ASN.1 encoded data.
        /// </summary>
        public X509AuthorityKeyIdentifierExtension(Oid oid, byte[] rawData, bool critical)
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

            if (m_keyId != null && m_keyId.Length >  0)
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

                buffer.Append(s_KeyIdentifier);
                buffer.Append("=");
                buffer.Append(m_keyId);
            }

            if (m_authorityNames != null)
            {
                for (int ii = 0; ii < m_authorityNames.Count; ii++)
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

                    buffer.Append(m_authorityNames[ii]);
                }
            }
            
            if (m_serialNumber != null && m_serialNumber.Length >  0)
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

                buffer.Append(s_SerialNumber);
                buffer.Append("=");
                buffer.Append(m_serialNumber);
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
        /// The OID for a Authority Key Identifier extension.
        /// </summary>
        public const string AuthorityKeyIdentifierOid = "2.5.29.1";

        /// <summary>
        /// The alternate OID for a Authority Key Identifier extension.
        /// </summary>
        public const string AuthorityKeyIdentifier2Oid = "2.5.29.35";

        /// <summary>
        /// The identifier for the key.
        /// </summary>
        public string KeyId
        {
            get { return m_keyId; }
            private set { m_keyId = value; }
        }

        /// <summary>
        /// A list of names for the issuer.
        /// </summary>
        public ReadOnlyList<string> AuthorityNames
        {
            get { return m_authorityNames; }
            private set { m_authorityNames = value; }
        }

        /// <summary>
        /// The serial number for the key.
        /// </summary>
        public string SerialNumber
        {
            get { return m_serialNumber; }
            private set { m_serialNumber = value; }
        }
        #endregion

        #region Private Methods
        private void Parse(byte[] data)
        {
            if (base.Oid.Value == AuthorityKeyIdentifierOid ||
                base.Oid.Value == AuthorityKeyIdentifier2Oid)
            {
                Org.BouncyCastle.X509.Extension.AuthorityKeyIdentifierStructure authorityKey =
                    new Org.BouncyCastle.X509.Extension.AuthorityKeyIdentifierStructure(
                        new Org.BouncyCastle.Asn1.DerOctetString(data));
                if (authorityKey != null)
                {
                    if (authorityKey.AuthorityCertSerialNumber != null)
                    {
                        m_serialNumber = Utils.ToHexString(authorityKey.AuthorityCertSerialNumber.ToByteArray());
                    }
                    if (authorityKey.AuthorityCertIssuer != null)
                    {
                        List<string> authorityNames = new List<string>();
                        foreach (var name in authorityKey.AuthorityCertIssuer.GetNames())
                        {
                            if (name.TagNo == Org.BouncyCastle.Asn1.X509.GeneralName.DirectoryName)
                            {
                                authorityNames.Add(name.Name.ToString());
                            }
                        }
                        m_authorityNames = new ReadOnlyList<string>(authorityNames);
                    }
                    m_keyId = Utils.ToHexString(authorityKey.GetKeyIdentifier());
                    return;
                }
            }
            throw new ServiceResultException(
                StatusCodes.BadCertificateInvalid,
                "Certificate uses unknown or bad AuthorityKeyIdentifierOid.");
        }
        #endregion

        #region Private Fields
        /// <summary>
        /// Authority Key Identifier extension string
        /// definitions see RFC 3281 4.3.3
        /// </summary>
        private const string s_KeyIdentifier = "keyid";
        private const string s_SerialNumber = "serialnumber";
        private const string s_FriendlyName = "Authority Key Identifier";
        private string m_keyId;
        private ReadOnlyList<string> m_authorityNames;
        private string m_serialNumber;
#endregion
    }
}
