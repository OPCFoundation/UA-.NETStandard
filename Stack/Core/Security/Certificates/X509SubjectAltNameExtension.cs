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

                buffer.Append("URL=");
                buffer.Append(m_uris[ii]);
            }

            for (int ii = 0; ii <  m_domainNames.Count; ii++)
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

                buffer.Append("DNS Name=");
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

                buffer.Append("IP Address=");
                buffer.Append(m_ipAddresses[ii]);
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
        private void Parse(byte[] data)
        {
            List<string> uris = new List<string>();
            List<string> domainNames = new List<string>();
            List<string> ipAddresses = new List<string>();

            CertificateFactory.ParseSubjectAltNameUsageExtension(
                data,
                uris,
                domainNames,
                ipAddresses);

            m_uris = new ReadOnlyList<string>(uris);
            m_domainNames = new ReadOnlyList<string>(domainNames);
            m_ipAddresses = new ReadOnlyList<string>(ipAddresses);
        }
        #endregion

        #region Private Fields
        private const string s_SubjectAltNameOid = "2.5.29.7";
        private const string s_SubjectAltName2Oid = "2.5.29.17";
        private const string s_FriendlyName = "Subject Alternative Name";
        private ReadOnlyList<string> m_uris;
        private ReadOnlyList<string> m_domainNames;
        private ReadOnlyList<string> m_ipAddresses;
        #endregion
    }
}
