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

namespace Opc.Ua.Server.Tests.CoverageNodeSet
{
    /// <summary>
    /// A <see cref="ReferenceServer"/> that hosts the identical kitchen-sink
    /// coverage model through the <b>runtime import</b> pipeline. The same XML
    /// asset that the source-generation server consumes at compile time is
    /// embedded as a resource and imported at startup through
    /// <see cref="RuntimeNodeSetNodeManagerFactory"/>. This asserts that the
    /// runtime importer materialises the same address space from the same
    /// bytes as the source-generated manager.
    /// </summary>
    public sealed class CoverageTestRuntimeServer : ReferenceServer
    {
        /// <summary>
        /// The model namespace URI shared by both coverage pipelines.
        /// </summary>
        public const string NamespaceUri = CoverageTestCatalogue.NamespaceUri;

        /// <summary>
        /// The secondary (dependent) model namespace URI.
        /// </summary>
        public const string SecondaryNamespaceUri = CoverageTestCatalogue.SecondaryNamespaceUri;

        private const string kResourceName =
            "Opc.Ua.Server.Tests.CoverageNodeSet.Assets.Opc.Ua.CoverageTest.NodeSet2.xml";

        private const string kSecondaryResourceName =
            "Opc.Ua.Server.Tests.CoverageNodeSet.Assets.Opc.Ua.CoverageTestSecondary.NodeSet2.xml";

        /// <summary>
        /// Initializes the server and registers a
        /// <see cref="RuntimeNodeSetNodeManagerFactory"/> that imports the
        /// embedded coverage NodeSets (primary + secondary) from stream
        /// sources. The factory dependency-sorts the two sources so the primary
        /// model is imported before the dependent secondary model.
        /// </summary>
        /// <param name="telemetry">
        /// Telemetry context forwarded to the base server.
        /// </param>
        public CoverageTestRuntimeServer(ITelemetryContext telemetry)
            : base(telemetry)
        {
            var options = new RuntimeNodeSetOptions
            {
                Sources =
                [
                    RuntimeNodeSetSource.FromStream(
                        "CoverageTest",
                        _ => new ValueTask<Stream>(OpenStream(kResourceName)),
                        [NamespaceUri]),
                    RuntimeNodeSetSource.FromStream(
                        "CoverageTestSecondary",
                        _ => new ValueTask<Stream>(OpenStream(kSecondaryResourceName)),
                        [SecondaryNamespaceUri]),
                ],
                DefaultNamespaceUri = NamespaceUri
            };

            AddNodeManager(new RuntimeNodeSetNodeManagerFactory(options));
        }

        /// <summary>
        /// Opens an embedded coverage NodeSet2 resource stream.
        /// </summary>
        internal static Stream OpenStream(string resourceName)
        {
            Stream stream = typeof(CoverageTestRuntimeServer).Assembly
                .GetManifestResourceStream(resourceName);

            if (stream is null)
            {
                throw new InvalidOperationException(
                    $"Embedded resource '{resourceName}' was not found.");
            }

            return stream;
        }
    }
}
