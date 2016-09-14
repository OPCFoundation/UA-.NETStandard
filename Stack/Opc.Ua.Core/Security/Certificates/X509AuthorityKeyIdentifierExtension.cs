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
                for (int ii = 0; ii < m_authorityNames.Length; ii++)
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
            if (asnEncodedData == null) throw new ArgumentNullException("asnEncodedData");
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
        public string[] AuthorityNames
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
        /// <summary>
        /// Convert string to upper case and remove white space.
        /// </summary>
        private string TrimHexString(string hex)
        {
            int i = 0;
            string result = "";
            while (i < hex.Length)
            {
                if (!Char.IsWhiteSpace(hex[i]))
                {
                    result += Char.ToUpper(hex[i]);
                }
                i++;
            }
            return result;
        }

        /// <summary>
        /// Extract KeyID and SerialNumber from formatted Authority Key Identifier.
        /// This is not a ASN.1 parser. Not parsing authority names.
        /// </summary>
        private void ParseAuthorityKeyIdentifierExtension(string formattedData)
        {
            m_keyId = null;
            m_serialNumber = null;

            string[] pairedData = formattedData.Split(',');

            // find desired keys in formatted data
            int position = 1;
            foreach (string pair in pairedData)
            {
                string[] splitPair = pair.Trim().Split('=');
                if (splitPair.Length == 2)
                {
                    if (splitPair[0] == s_KeyIdentifier && position == 1)
                    {
                        m_keyId = TrimHexString(splitPair[1]);
                    }
                    else if (splitPair[0].EndsWith(s_SerialNumber) && position == pairedData.Length)
                    {
                        m_serialNumber = TrimHexString(splitPair[1]);
                    }
                }
                position++;
            }
        }

        private void Parse(byte[] data)
        {
            if (base.Oid.Value == AuthorityKeyIdentifierOid ||
                base.Oid.Value == AuthorityKeyIdentifier2Oid)
            {
                AsnEncodedData asnData = new AsnEncodedData(base.Oid.Value, data);
                string formattedData = asnData.Format(false);
                ParseAuthorityKeyIdentifierExtension(formattedData);
            }
            else
            {
                throw new ServiceResultException(
                    StatusCodes.BadCertificateInvalid,
                    "Certificate uses unknown AuthorityKeyIdentifierOid.");
            }
        }
        #endregion

        #region Private Fields
        /// <summary>
        /// Authority Key Identifier extension string
        /// definitions see RFC 3281 4.3.3
        /// </summary>
        private const string s_KeyIdentifier = "KeyID";
        private const string s_SerialNumber = "SerialNumber";
        private const string s_FriendlyName = "Authority Key Identifier";
        private string m_keyId;
        private string[] m_authorityNames;
        private string m_serialNumber;
#endregion
    }
}
