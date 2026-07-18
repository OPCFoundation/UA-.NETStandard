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
using System.IO;
using System.Threading.Tasks;
using Opc.Ua.Server.RuntimeNodeSet;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests.SchemaRegistry
{
    /// <summary>
    /// A <see cref="ReferenceServer"/> that loads the experimental abstract xRegistry base
    /// companion NodeSet and the Schema Registry companion NodeSet through the
    /// <see cref="RuntimeNodeSetNodeManagerFactory"/> import path. This proves the in-server
    /// Schema Registry AddressSpace model (the <c>SchemaRegistryType</c> and its
    /// well-known <c>SchemaRegistry</c> object attached to the Server object) materializes
    /// in a real server exactly as the generated companion NodeSets describe.
    /// </summary>
    internal sealed class SchemaRegistryTestServer : ReferenceServer
    {
        /// <summary>
        /// The abstract xRegistry base companion namespace URI.
        /// </summary>
        public const string XRegistryNamespaceUri = "http://opcfoundation.org/UA/xRegistry/";

        /// <summary>
        /// The Schema Registry companion namespace URI.
        /// </summary>
        public const string SchemaRegistryNamespaceUri = "http://opcfoundation.org/UA/SchemaRegistry/";

        /// <summary>
        /// Provisional NodeId of the <c>SchemaRegistryType</c> ObjectType.
        /// </summary>
        public const uint SchemaRegistryType = 62000;

        /// <summary>
        /// Provisional NodeId of the well-known <c>SchemaRegistry</c> object.
        /// </summary>
        public const uint SchemaRegistryObject = 62100;

        /// <summary>
        /// Provisional NodeId of the <c>GetSchema</c> method materialized on the well-known
        /// <c>SchemaRegistry</c> object.
        /// </summary>
        public const uint SchemaRegistryGetSchemaMethod = 62516;

        private const string kXRegistryResource =
            "Opc.Ua.Server.Tests.SchemaRegistry.Opc.Ua.XRegistry.NodeSet2.xml";
        private const string kSchemaRegistryResource =
            "Opc.Ua.Server.Tests.SchemaRegistry.Opc.Ua.SchemaRegistry.NodeSet2.xml";

        /// <summary>
        /// Initializes the server and registers the runtime NodeSet factory that loads the
        /// xRegistry base and Schema Registry companion NodeSets in dependency order.
        /// </summary>
        /// <param name="telemetry">Telemetry context forwarded to the base server.</param>
        public SchemaRegistryTestServer(ITelemetryContext telemetry)
            : base(telemetry)
        {
            var options = new RuntimeNodeSetOptions
            {
                Sources =
                [
                    RuntimeNodeSetSource.FromStream(
                        "xRegistry",
                        _ => new ValueTask<Stream>(OpenResource(kXRegistryResource)),
                        [XRegistryNamespaceUri]),
                    RuntimeNodeSetSource.FromStream(
                        "SchemaRegistry",
                        _ => new ValueTask<Stream>(OpenResource(kSchemaRegistryResource)),
                        [SchemaRegistryNamespaceUri]),
                ]
            };

            AddNodeManager(new RuntimeNodeSetNodeManagerFactory(options));
            AddNodeManager(new SchemaRegistryFastPathNodeManagerFactory());
        }

        private static Stream OpenResource(string name)
        {
            Stream stream = typeof(SchemaRegistryTestServer).Assembly
                .GetManifestResourceStream(name);

            if (stream is null)
            {
                throw new InvalidOperationException(
                    $"Embedded resource '{name}' was not found.");
            }

            return stream;
        }
    }
}
