/* Copyright (c) 1996-2019 The OPC Foundation. All rights reserved.
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
	/// Defines well-known namespaces.
	/// </summary>
    public static partial class Namespaces
	{
		/// <summary>
		/// The XML Schema namespace.
		/// </summary>
        public const string XmlSchema = "http://www.w3.org/2001/XMLSchema";

		/// <summary>
		/// The XML Schema Instance namespace.
		/// </summary>
        public const string XmlSchemaInstance = "http://www.w3.org/2001/XMLSchema-instance";

		/// <summary>
		/// The WS Secuirity Extensions Namespace.
		/// </summary>
        public const string WSSecurityExtensions = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";
        
		/// <summary>
		/// The WS Secuirity Utilities Namespace.
		/// </summary>
        public const string WSSecurityUtilities = "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";
        
		/// <summary>
		/// The URI for the UA WSDL.
		/// </summary>
        public const string OpcUaWsdl = "http://opcfoundation.org/UA/2008/02/Services.wsdl";
        
        /// <summary>
        /// The URI for the UA SecuredApplication schema.
        /// </summary>
        public const string OpcUaSecurity = "http://opcfoundation.org/UA/2011/03/SecuredApplication.xsd";

        /// <summary>
        /// The base URI for the Global Discovery Service.
        /// </summary>
        public const string OpcUaGds = "http://opcfoundation.org/UA/GDS/";
				
		/// <summary>
		/// The base URI for SDK related schemas.
		/// </summary>
        public const string OpcUaSdk = "http://opcfoundation.org/UA/SDK/";

		/// <summary>
		/// The URI for the UA SDK Configuration Schema.
		/// </summary>
        public const string OpcUaConfig = OpcUaSdk + "Configuration.xsd";
        
        /// <summary>
        /// The URI for the built-in types namespace.
        /// </summary>
        public const string OpcUaBuiltInTypes = OpcUa + "BuiltInTypes/";

        /// <summary>
        /// The URI for the OPC Binary Schema.
        /// </summary>
        public const string OpcBinarySchema = "http://opcfoundation.org/BinarySchema/";

        /// <summary>
        /// The URI representing all resources in at a site managed by an OAuthe Authorization Service.
        /// </summary>
        public const string OAuth2SiteResourceUri = "urn:opcfoundation.org:ua:oauth2:resource:site";
    }
}
