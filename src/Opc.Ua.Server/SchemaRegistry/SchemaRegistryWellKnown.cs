/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

using System.Diagnostics.CodeAnalysis;

namespace Opc.Ua.Server.SchemaRegistry
{
    /// <summary>
    /// Well-known identifiers of the experimental in-server Schema Registry feature: the abstract
    /// xRegistry base companion namespace, the Schema Registry companion namespace, and the
    /// provisional NodeIds the runtime node managers materialize. These mirror the generated
    /// companion NodeSets and the OPC UA — Schema Registry specification (Annex B).
    /// </summary>
    [Experimental("UA_NETStandard_Encoders")]
    public static class SchemaRegistryWellKnown
    {
        /// <summary>The abstract xRegistry base companion namespace URI.</summary>
        public const string XRegistryNamespaceUri = "http://opcfoundation.org/UA/xRegistry/";

        /// <summary>The Schema Registry companion namespace URI.</summary>
        public const string SchemaRegistryNamespaceUri = "http://opcfoundation.org/UA/SchemaRegistry/";

        /// <summary>Provisional NodeId of the <c>SchemaRegistryType</c> ObjectType.</summary>
        public const uint SchemaRegistryType = 62000;

        /// <summary>Provisional NodeId of the well-known <c>SchemaRegistry</c> object.</summary>
        public const uint SchemaRegistryObject = 62100;

        /// <summary>Provisional NodeId of the <c>GetSchema</c> method on the well-known object.</summary>
        public const uint SchemaRegistryGetSchemaMethod = 62516;

        /// <summary>Provisional NodeId of the registration <c>SchemaGroup</c> object.</summary>
        public const uint SchemaGroupObject = 63001;

        /// <summary>Provisional NodeId of the <c>CreateResource</c> method.</summary>
        public const uint CreateResourceMethod = 63002;

        /// <summary>Provisional NodeId of the <c>Write</c> method.</summary>
        public const uint WriteMethod = 63003;

        /// <summary>Provisional NodeId of the <c>Close</c> method.</summary>
        public const uint CloseMethod = 63004;

        /// <summary>Provisional NodeId of the <c>Delete</c> method.</summary>
        public const uint DeleteMethod = 63005;

        /// <summary>Provisional NodeId of the federated schema proxy object.</summary>
        public const uint FederationProxyObject = 64001;

        /// <summary>Provisional NodeId of the proxy's <c>ExternalReference</c> Property.</summary>
        public const uint FederationExternalReferenceProperty = 64002;

        /// <summary>Provisional NodeId of the proxy's <c>ResourceUrl</c> Property.</summary>
        public const uint FederationResourceUrlProperty = 64003;

        /// <summary>Provisional NodeId of the proxy's <c>SchemaId</c> Property.</summary>
        public const uint FederationSchemaIdProperty = 64004;
    }
}
