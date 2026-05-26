/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person
 * obtaining a copy of this software and associated documentation
 * files (the "Software"), to deal in the Software without
 * restriction, including without limitation the rights to use,
 * copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following
 * conditions:
 *
 * The above copyright notice and this permission notice shall be
 * included in all copies or substantial portions of the Software.
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
 * EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
 * OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
 * NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
 * HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
 * WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
 * FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 *
 * The complete license agreement can be found here:
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

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
