/* Copyright (c) 1996-2022 The OPC Foundation. All rights reserved.
   The source code in this file is covered under a dual-license scenario:
     - RCL: for OPC Foundation Corporate Members in good-standing
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
            ProductUri = string.Empty;
            ManufacturerName = string.Empty;
            ProductName = string.Empty;
            SoftwareVersion = string.Empty;
            BuildNumber = string.Empty;
            BuildDate = DateTime.MinValue;
            DatatypeAssemblies = new StringCollection();
            SoftwareCertificates = new SignedSoftwareCertificateCollection();
        }
        #endregion

        #region Public Properties
        /// <summary>
        /// The unique identifier for the product.
        /// </summary>
        public string ProductUri { get; set; }

        /// <summary>
        /// The name of the product
        /// </summary>
        public string ProductName { get; set; }

        /// <summary>
        /// The name of the manufacturer
        /// </summary>
        public string ManufacturerName { get; set; }

        /// <summary>
        /// The software version for the application
        /// </summary>
        public string SoftwareVersion { get; set; }

        /// <summary>
        /// The build number for the application
        /// </summary>
        public string BuildNumber { get; set; }

        /// <summary>
        /// When the application was built.
        /// </summary>
        public DateTime BuildDate { get; set; }

        /// <summary>
        /// The assemblies that contain encodeable types that could be uses a variable values.
        /// </summary>
        public StringCollection DatatypeAssemblies { get; }

        /// <summary>
        /// The software certificates granted to the server.
        /// </summary>
        public SignedSoftwareCertificateCollection SoftwareCertificates { get; }

#endregion
    }
}
