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

namespace Opc.Ua.PubSub.SchemaRegistry
{
    /// <summary>
    /// Well-known identifiers of the PubSub Schema Registry — the concrete xRegistry specialization
    /// that registers the schemas describing PubSub DataSet encodings. It reuses the generic
    /// resource/method NodeIds from the xRegistry base and adds the Schema Registry companion
    /// namespace and its type/instance NodeIds. Final NodeIds are assigned by the OPC Foundation.
    /// </summary>
    public static class SchemaRegistryWellKnown
    {
        /// <summary>The Schema Registry companion namespace URI.</summary>
        public const string SchemaRegistryNamespaceUri = "http://opcfoundation.org/UA/SchemaRegistry/";

        /// <summary>Provisional NodeId of the <c>SchemaRegistryType</c> ObjectType.</summary>
        public const uint SchemaRegistryType = 62000;

        /// <summary>Provisional NodeId of the well-known <c>SchemaRegistry</c> object.</summary>
        public const uint SchemaRegistryObject = 62100;

        /// <summary>Provisional NodeId of the <c>GetSchema</c> method on the well-known object.</summary>
        public const uint SchemaRegistryGetSchemaMethod = 62516;
    }
}
