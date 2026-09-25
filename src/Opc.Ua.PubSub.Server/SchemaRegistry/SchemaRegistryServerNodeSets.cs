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
using Opc.Ua.PubSub.SchemaRegistry;
using Opc.Ua.Server.RuntimeNodeSet;

namespace Opc.Ua.PubSub.Server.SchemaRegistry
{
    /// <summary>
    /// Composes the ordered runtime NodeSet sources that back the in-server Schema Registry: the
    /// abstract xRegistry base companion NodeSet followed by the Schema Registry companion NodeSet,
    /// whose <c>RequiredModel</c> depends on the base. Both documents are embedded in
    /// <c>Opc.Ua.PubSub</c>.
    /// </summary>
    /// <remarks>
    /// <c>Opc.Ua.XRegistry</c> compiles its companion model into the assembly and loads it as
    /// predefined nodes through the xRegistry node managers, so it no longer publishes a runtime
    /// NodeSet source of its own. The base document is still imported here because the runtime
    /// importer must resolve the supertypes the Schema Registry model derives from.
    /// </remarks>
    public static class SchemaRegistryServerNodeSets
    {
        /// <summary>
        /// Creates the ordered runtime NodeSet sources (xRegistry base first, then Schema Registry).
        /// </summary>
        /// <param name="options">The Schema Registry feature options (namespace URIs).</param>
        /// <returns>The ordered NodeSet sources.</returns>
        public static ArrayOf<RuntimeNodeSetSource> CreateSources(SchemaRegistryOptions? options = null)
        {
            options ??= new SchemaRegistryOptions();
            return
            [
                RuntimeNodeSetSource.FromStream(
                    "xRegistry",
                    _ => new ValueTask<Stream>(SchemaRegistryNodeSets.OpenBaseNodeSet()),
                    [options.XRegistryNamespaceUri]),
                RuntimeNodeSetSource.FromStream(
                    "SchemaRegistry",
                    _ => new ValueTask<Stream>(SchemaRegistryNodeSets.OpenNodeSet()),
                    [options.SchemaRegistryNamespaceUri]),
            ];
        }
    }
}
