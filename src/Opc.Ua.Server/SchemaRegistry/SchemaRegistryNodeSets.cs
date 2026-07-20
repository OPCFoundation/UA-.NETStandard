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
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Server.RuntimeNodeSet;

namespace Opc.Ua.Server.SchemaRegistry
{
    /// <summary>
    /// Provides the abstract xRegistry base and Schema Registry companion NodeSet2 documents that
    /// back the in-server Schema Registry feature. The documents ship as embedded resources of the
    /// <c>Opc.Ua.Server</c> assembly and are imported, in dependency order, through the
    /// <see cref="RuntimeNodeSetNodeManagerFactory"/> path.
    /// </summary>
    [Experimental("UA_NETStandard_Encoders")]
    public static class SchemaRegistryNodeSets
    {
        private const string XRegistryResource =
            "Opc.Ua.Server.SchemaRegistry.Opc.Ua.XRegistry.NodeSet2.xml";

        private const string SchemaRegistryResource =
            "Opc.Ua.Server.SchemaRegistry.Opc.Ua.SchemaRegistry.NodeSet2.xml";

        /// <summary>
        /// Creates the ordered runtime NodeSet sources (xRegistry base first, then Schema Registry)
        /// for the supplied <paramref name="options"/>.
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
                    _ => new ValueTask<Stream>(OpenResource(XRegistryResource)),
                    [options.XRegistryNamespaceUri]),
                RuntimeNodeSetSource.FromStream(
                    "SchemaRegistry",
                    _ => new ValueTask<Stream>(OpenResource(SchemaRegistryResource)),
                    [options.SchemaRegistryNamespaceUri]),
            ];
        }

        private static Stream OpenResource(string name)
        {
            Stream? stream = typeof(SchemaRegistryNodeSets).Assembly
                .GetManifestResourceStream(name);

            if (stream is null)
            {
                throw new InvalidOperationException(
                    $"Embedded Schema Registry NodeSet '{name}' was not found.");
            }

            return stream;
        }
    }
}
