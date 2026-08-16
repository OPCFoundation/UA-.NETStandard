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

using Opc.Ua.CoverageTest;
using Opc.Ua.CoverageTestSecondary;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests.CoverageNodeSet
{
    /// <summary>
    /// A <see cref="ReferenceServer"/> that hosts the kitchen-sink coverage
    /// model through the <b>source-generation</b> pipeline. The model XML is
    /// consumed at compile time via
    /// <c>&lt;AdditionalFiles ModelSourceGeneratorPrefix="Opc.Ua.CoverageTest"&gt;</c>;
    /// the generated <c>AddOpcUaCoverageTest</c> extension composes the
    /// predefined nodes — each authored node materialising as its strongly
    /// typed, generated <see cref="NodeState"/> subclass — inside the
    /// hand-written <see cref="CoverageTestNodeManager"/>.
    /// </summary>
    public sealed class CoverageTestSourceGenServer : ReferenceServer
    {
        /// <summary>
        /// The model namespace URI shared by both coverage pipelines.
        /// </summary>
        public const string NamespaceUri = CoverageTestCatalogue.NamespaceUri;

        /// <summary>
        /// Initializes the server and registers the source-generation node
        /// manager factory for the coverage model.
        /// </summary>
        /// <param name="telemetry">
        /// Telemetry context forwarded to the base server.
        /// </param>
        public CoverageTestSourceGenServer(ITelemetryContext telemetry)
            : base(telemetry)
        {
            AddNodeManager(new CoverageTestNodeManagerFactory());
        }
    }

    /// <summary>
    /// The <see cref="INodeManagerFactory"/> for the source-generation coverage
    /// node manager.
    /// </summary>
    public sealed class CoverageTestNodeManagerFactory : INodeManagerFactory
    {
        /// <inheritdoc/>
        public ArrayOf<string> NamespacesUris =>
            [CoverageTestCatalogue.NamespaceUri, CoverageTestCatalogue.SecondaryNamespaceUri];

        /// <inheritdoc/>
        public INodeManager Create(IServerInternal server, ApplicationConfiguration configuration)
        {
            return new CoverageTestNodeManager(server, configuration);
        }
    }

    /// <summary>
    /// A <see cref="CustomNodeManager2"/> that composes the coverage model from
    /// the source-generated <c>AddOpcUaCoverageTest</c> extension. It owns both
    /// the primary and the secondary (dependent) namespaces and composes both
    /// generated node sets.
    /// </summary>
    public sealed class CoverageTestNodeManager : CustomNodeManager2
    {
        /// <summary>
        /// Initializes the node manager for the coverage model namespaces.
        /// </summary>
        /// <param name="server">
        /// The server that owns the node manager.
        /// </param>
        /// <param name="configuration">
        /// The application configuration.
        /// </param>
        public CoverageTestNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration)
            : base(
                server,
                configuration,
                CoverageTestCatalogue.NamespaceUri,
                CoverageTestCatalogue.SecondaryNamespaceUri)
        {
        }

        /// <inheritdoc/>
        protected override NodeStateCollection LoadPredefinedNodes(ISystemContext context)
        {
            var predefinedNodes = new NodeStateCollection();
            predefinedNodes.AddOpcUaCoverageTest(context);
            predefinedNodes.AddOpcUaCoverageTestSecondary(context);
            return predefinedNodes;
        }
    }
}
