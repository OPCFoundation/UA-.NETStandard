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
	}
}
