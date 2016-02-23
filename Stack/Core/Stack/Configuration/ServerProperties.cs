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
