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

namespace Opc.Ua
{
    /// <summary>
	/// The properties of the current server instance.
	/// </summary>
	public class ServerProperties
	{
#region Constructors
        /// <summary>
        /// The default constructor.
        /// </summary>
        public ServerProperties()
		{
			m_productUri = String.Empty;
		    m_manufacturerName = String.Empty;
		    m_productName = String.Empty;
		    m_softwareVersion = String.Empty;
		    m_buildNumber = String.Empty;
		    m_buildDate = DateTime.MinValue;
            m_datatypeAssemblies = new StringCollection();
            m_softwareCertificates = new SignedSoftwareCertificateCollection();
		}
#endregion

#region Public Properties
		/// <summary>
		/// The unique identifier for the product.
		/// </summary>
		public string ProductUri
		{
			get { return m_productUri;  }
			set { m_productUri = value; }
		}
     
		/// <summary>
		/// The name of the product
		/// </summary>
		public string ProductName
		{
			get { return m_productName;  }
			set { m_productName = value; }
		}     
     
		/// <summary>
		/// The name of the manufacturer
		/// </summary>
		public string ManufacturerName
		{
			get { return m_manufacturerName;  }
			set { m_manufacturerName = value; }
		}  
     
		/// <summary>
		/// The software version for the application
		/// </summary>
		public string SoftwareVersion
		{
			get { return m_softwareVersion;  }
			set { m_softwareVersion = value; }
		}       
     
		/// <summary>
		/// The build number for the application
		/// </summary>
		public string BuildNumber
		{
			get { return m_buildNumber;  }
			set { m_buildNumber = value; }
		}       
     
		/// <summary>
		/// When the application was built.
        /// </summary>
		public DateTime BuildDate
		{
			get { return m_buildDate;  }
			set { m_buildDate = value; }
		}       
                
		/// <summary>
		/// The assemblies that contain encodeable types that could be uses a variable values.
		/// </summary>
		public StringCollection DatatypeAssemblies
		{
			get { return m_datatypeAssemblies; }
		}

        /// <summary>
        /// The software certificates granted to the server.
        /// </summary>
        public SignedSoftwareCertificateCollection SoftwareCertificates
        {
            get { return m_softwareCertificates; }
        }
#endregion

#region Private Members
		private string m_productUri;
		private string m_productName;
		private string m_manufacturerName;
		private string m_softwareVersion;
		private string m_buildNumber;
		private DateTime m_buildDate;
        private StringCollection m_datatypeAssemblies;
        private SignedSoftwareCertificateCollection m_softwareCertificates;
#endregion
	}
}
