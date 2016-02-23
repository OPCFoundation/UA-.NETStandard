/* ========================================================================
 * Copyright (c) 2005-2013 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Reciprocal Community License ("RCL") Version 1.00
 * 
 * Unless explicitly acquired and licensed from Licensor under another 
 * license, the contents of this file are subject to the Reciprocal 
 * Community License ("RCL") Version 1.00, or subsequent versions 
 * as allowed by the RCL, and You may not copy or use this file in either 
 * source code or executable form, except in compliance with the terms and 
 * conditions of the RCL.
 * 
 * All software distributed under the RCL is provided strictly on an 
 * "AS IS" basis, WITHOUT WARRANTY OF ANY KIND, EITHER EXPRESS OR IMPLIED, 
 * AND LICENSOR HEREBY DISCLAIMS ALL SUCH WARRANTIES, INCLUDING WITHOUT 
 * LIMITATION, ANY WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR 
 * PURPOSE, QUIET ENJOYMENT, OR NON-INFRINGEMENT. See the RCL for specific 
 * language governing rights and limitations under the RCL.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/RCL/1.00/
 * ======================================================================*/

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

                buffer.Append("KeyID=");
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

                buffer.Append("SerialNumber=");
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
        }

        /// <summary>
        /// A list of names for the issuer.
        /// </summary>
        public string[] AuthorityNames
        {
            get { return m_authorityNames; }
        }

        /// <summary>
        /// The serial number for the key.
        /// </summary>
        public string SerialNumber
        {
            get { return m_serialNumber; }
        }
        #endregion

        #region Private Methods
        private void Parse(byte[] data)
        {
            byte[] keyId;
            byte[] serialNumber;

            if (base.Oid.Value == AuthorityKeyIdentifierOid)
            {
                CertificateFactory.ParseAuthorityKeyIdentifierExtension(
                    data,
                    out keyId,
                    out m_authorityNames,
                    out serialNumber);
            }
            else
            {
                CertificateFactory.ParseAuthorityKeyIdentifierExtension2(
                    data,
                    out keyId,
                    out m_authorityNames,
                    out serialNumber);
            }
            
            m_keyId = Utils.ToHexString(keyId);
            m_serialNumber = null;

            // the serial number is a little endian integer so must convert to string in reverse order. 
            if (serialNumber != null)
            {
                StringBuilder builder = new StringBuilder(serialNumber.Length*2);

                for (int ii = serialNumber.Length-1; ii >=  0; ii--)
                {
                    builder.AppendFormat("{0:X2}", serialNumber[ii]);
                }

                m_serialNumber = builder.ToString();
            }
        }
        #endregion

        #region Private Fields
        private const string s_FriendlyName = "Authority Key Identifier";
        private string m_keyId;
        private string[] m_authorityNames;
        private string m_serialNumber;
        #endregion
    }
}
