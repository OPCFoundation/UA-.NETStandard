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

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Opc.Ua.PubSub.SchemaRegistry
{
    /// <summary>
    /// Provides the NodeSet2 documents the in-server Schema Registry imports at runtime: the Schema
    /// Registry companion NodeSet and the abstract xRegistry base model it declares a
    /// <c>RequiredModel</c> on. Both are embedded in the <c>Opc.Ua.PubSub</c> assembly. This type
    /// has no dependency on the OPC UA server SDK; the server-side runtime NodeSet wrapping is done
    /// in <c>Opc.Ua.PubSub.Server</c>.
    /// </summary>
    /// <remarks>
    /// <c>Opc.Ua.XRegistry</c> compiles its companion model into the assembly with the OPC UA model
    /// source generator and therefore no longer exposes it as a runtime NodeSet. The base document
    /// is embedded here so the Schema Registry import path stays self-contained and can resolve the
    /// supertypes the Schema Registry model derives from.
    /// </remarks>
    public static class SchemaRegistryNodeSets
    {
        /// <summary>
        /// The embedded-resource name of the Schema Registry companion NodeSet2 document.
        /// </summary>
        public const string NodeSetResourceName =
            "Opc.Ua.PubSub.SchemaRegistry.Opc.Ua.SchemaRegistry.NodeSet2.xml";

        /// <summary>
        /// The embedded-resource name of the abstract xRegistry base companion NodeSet2 document.
        /// </summary>
        public const string BaseNodeSetResourceName =
            "Opc.Ua.PubSub.SchemaRegistry.Opc.Ua.XRegistry.NodeSet2.xml";

        /// <summary>
        /// Opens a fresh read stream over the embedded Schema Registry companion NodeSet2 document.
        /// </summary>
        /// <returns>A readable stream positioned at the start of the NodeSet2 XML.</returns>
        /// <exception cref="InvalidOperationException">The embedded NodeSet was not found.</exception>
        public static Stream OpenNodeSet()
        {
            return OpenEmbeddedNodeSet(NodeSetResourceName);
        }

        /// <summary>
        /// Opens a fresh read stream over the embedded abstract xRegistry base companion NodeSet2
        /// document, which the Schema Registry model requires.
        /// </summary>
        /// <returns>A readable stream positioned at the start of the NodeSet2 XML.</returns>
        /// <exception cref="InvalidOperationException">The embedded NodeSet was not found.</exception>
        public static Stream OpenBaseNodeSet()
        {
            return OpenEmbeddedNodeSet(BaseNodeSetResourceName);
        }

        /// <summary>
        /// Opens an embedded NodeSet2 document by its manifest-resource name.
        /// </summary>
        /// <param name="resourceName">The manifest-resource name.</param>
        /// <returns>A readable stream positioned at the start of the NodeSet2 XML.</returns>
        /// <exception cref="InvalidOperationException">The embedded NodeSet was not found.</exception>
        private static Stream OpenEmbeddedNodeSet(string resourceName)
        {
            return typeof(SchemaRegistryNodeSets).Assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException(
                    $"Embedded Schema Registry NodeSet '{resourceName}' was not found.");
        }
    }
}
