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

namespace Opc.Ua.XRegistry
{
    /// <summary>
    /// Provides the abstract xRegistry base companion NodeSet2 document, embedded in the
    /// <c>Opc.Ua.XRegistry</c> assembly. This type is intentionally free of any dependency on the
    /// OPC UA server SDK; the server-side runtime NodeSet wrapping is done in
    /// <c>Opc.Ua.XRegistry.Server</c>.
    /// </summary>
    [Experimental("UA_NETStandard_Encoders")]
    public static class XRegistryNodeSets
    {
        /// <summary>
        /// The embedded-resource name of the xRegistry base companion NodeSet2 document.
        /// </summary>
        public const string BaseNodeSetResourceName =
            "Opc.Ua.XRegistry.Opc.Ua.XRegistry.NodeSet2.xml";

        /// <summary>
        /// Opens a fresh read stream over the embedded xRegistry base companion NodeSet2 document.
        /// </summary>
        /// <returns>A readable stream positioned at the start of the NodeSet2 XML.</returns>
        /// <exception cref="InvalidOperationException">The embedded NodeSet was not found.</exception>
        public static Stream OpenBaseNodeSet()
        {
            Stream? stream = typeof(XRegistryNodeSets).Assembly
                .GetManifestResourceStream(BaseNodeSetResourceName);

            if (stream is null)
            {
                throw new InvalidOperationException(
                    $"Embedded xRegistry base NodeSet '{BaseNodeSetResourceName}' was not found.");
            }

            return stream;
        }
    }
}
