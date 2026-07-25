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
using System.IO;
using System.Threading.Tasks;
using Opc.Ua.Server.RuntimeNodeSet;

namespace Opc.Ua.XRegistry.Server
{
    /// <summary>
    /// Bridges the embedded xRegistry base companion NodeSet (from <c>Opc.Ua.XRegistry</c>) into the
    /// server runtime NodeSet import path. A concrete registry composes this base source with its own
    /// companion NodeSet source in dependency order.
    /// </summary>
    public static class XRegistryServerNodeSets
    {
        /// <summary>
        /// Creates a runtime NodeSet source for the abstract xRegistry base companion NodeSet, owning
        /// the xRegistry base namespace.
        /// </summary>
        /// <returns>The base xRegistry NodeSet source.</returns>
        public static RuntimeNodeSetSource CreateBaseSource()
        {
            return RuntimeNodeSetSource.FromStream(
                "xRegistry",
                _ => new ValueTask<Stream>(XRegistryNodeSets.OpenBaseNodeSet()),
                [XRegistryWellKnown.XRegistryNamespaceUri]);
        }
    }
}
