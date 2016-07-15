/* Copyright (c) 1996-2016, OPC Foundation. All rights reserved.
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
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Net;
using System.Security.Cryptography.X509Certificates;

namespace Opc.Ua.Configuration
{
    /// <summary>
    /// An access rule for an HTTP URL used by a WCF service.
    /// </summary>
    [DataContract(Namespace=Namespaces.OpcUaConfig)]
    public class HttpAccessRule
    {
        const Int16 AF_INET = 2;
        const Int16 AF_INET6 = 23;
    }

    #region AccessControlEntity Class
    /// <summary>
    /// A class that stores the components of ACE within a DACL.
    /// </summary>
    public class AccessControlEntity
    {
        /// <summary>
        /// The access type granted by the ACE.
        /// </summary>
        public string AccessType
        {
            get { return m_accessType;  }
            set { m_accessType = value; }
        }
        
        /// <summary>
        /// Any flags associated with the ACE.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1726:UsePreferredTerms", MessageId = "Flags")]
        public string Flags
        {
            get { return m_flags;  }
            set { m_flags = value; }
        }
        
        /// <summary>
        /// The rights allowed/restricted by the ACE.
        /// </summary>
        public string Rights
        {
            get { return m_rights;  }
            set { m_rights = value; }
        }
        
        /// <summary>
        /// The object associated with the ACE.
        /// </summary>
        public string ObjectGuid
        {
            get { return m_objectGuid;  }
            set { m_objectGuid = value; }
        }
        
        /// <summary>
        /// The inherited object associated with the ACE.
        /// </summary>
        public string InheritObjectGuid
        {
            get { return m_inheritObjectGuid;  }
            set { m_inheritObjectGuid = value; }
        }
        
        /// <summary>
        /// The SID for the account which is affected by the ACE.
        /// </summary>
        public string AccountSid
        {
            get { return m_accountSid;  }
            set { m_accountSid = value; }
        }        
        
        /// <summary>
        /// Extracts a list of ACEs from a SDDL string.
        /// </summary>
        public static IList<AccessControlEntity> Parse(string sddl)
        {
            List<AccessControlEntity> entities = new List<AccessControlEntity>();

            if (!sddl.StartsWith("D:", StringComparison.Ordinal))
            {
                throw new ArgumentException(Utils.Format("Could not parse SDDL string: {0}", sddl));
            }

            sddl = sddl.Substring(2, sddl.Length-2);

            string[] aces = sddl.Split('(', ')');

            for (int ii = 0; ii < aces.Length; ii++)
            {
                if (String.IsNullOrEmpty(aces[ii]))
                {
                    continue;
                }
                
                AccessControlEntity entity = new AccessControlEntity();
                entity.Initialize(aces[ii]);
                entities.Add(entity);
            }

            return entities;
        }

        /// <summary>
        /// Extracts a single ACE from a SDDL string fragment.
        /// </summary>
        public void Initialize(string sddl)
        {
            string[] fields = sddl.Split(';');

            if (fields.Length != 6)
            {
                throw new ArgumentException(Utils.Format("Could not parse SDDL ACE string: {0}", sddl));
            }
            
            m_accessType = fields[0];
            m_flags = fields[1];
            m_rights = fields[2];
            m_objectGuid = fields[3];
            m_inheritObjectGuid = fields[4];
            m_accountSid = fields[5];
        }

        private string m_accessType;
        private string m_flags;
        private string m_rights;
        private string m_objectGuid;
        private string m_inheritObjectGuid;
        private string m_accountSid;
    }
    #endregion

    #region SslCertificateBinding Class
    /// <summary>
    /// Stores the details of an SSL certification configuration binding.
    /// </summary>
    public class SslCertificateBinding
    {
        /// <summary>
        /// The IP Address.
        /// </summary>
        public IPAddress IPAddress { get; set; }

        /// <summary>
        /// The port number.
        /// </summary>
        public ushort Port { get; set; }

        /// <summary>
        /// The certificate thumbprint.
        /// </summary>
        public string Thumbprint { get; set; }

        /// <summary>
        /// The application id.
        /// </summary>
        public Guid ApplicationId { get; set; }

        /// <summary>
        /// The names of the store to use.
        /// </summary>
        public string StoreName { get; set; }

        /// <summary>
        /// The default revocation check mode.
        /// </summary>
        public uint DefaultCertCheckMode { get; set; }

        /// <summary>
        /// The default revocation freshness time.
        /// </summary>
        public int DefaultRevocationFreshnessTime { get; set; }

        /// <summary>
        /// The default revocation URL timeout.
        /// </summary>
        public int DefaultRevocationUrlRetrievalTimeout { get; set; }

        /// <summary>
        /// The default certificate trust list identifier.
        /// </summary>
        public string DefaultSslCtlIdentifier { get; set; }

        /// <summary>
        /// The default certificate trust list store.
        /// </summary>
        public string DefaultSslCtlStoreName { get; set; }

        /// <summary>
        /// The default flags.
        /// </summary>
        public uint DefaultFlags { get; set; }
    }
    #endregion
}
