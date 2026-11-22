// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
        /// The WS Security Extensions Namespace.
        /// </summary>
        public const string WSSecurityExtensions =
            "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-secext-1.0.xsd";

        /// <summary>
        /// The WS Security Utilities Namespace.
        /// </summary>
        public const string WSSecurityUtilities =
            "http://docs.oasis-open.org/wss/2004/01/oasis-200401-wss-wssecurity-utility-1.0.xsd";

        /// <summary>
        /// The URI for the UA WSDL.
        /// </summary>
        public const string OpcUaWsdl = "http://opcfoundation.org/UA/2008/02/Services.wsdl";

        /// <summary>
        /// The URI for the UA SecuredApplication schema.
        /// </summary>
        public const string OpcUaSecurity
            = "http://opcfoundation.org/UA/2011/03/SecuredApplication.xsd";

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
