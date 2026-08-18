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
    /// A <see cref="ReferenceServer"/> that hosts the coverage model through the
    /// fully <b>source-generated fluent node manager</b> pipeline. Unlike
    /// <see cref="CoverageTestSourceGenServer"/> (which composes the generated
    /// nodes inside a hand-written <see cref="CustomNodeManager2"/>), this host
    /// registers the generated <see cref="CoverageTestFluentNodeManagerFactory"/>
    /// produced by the <c>[NodeManager]</c> attribute — the generator emits the
    /// entire manager, factory and fluent builder surface.
    /// </summary>
    public sealed class CoverageTestFluentGenServer : ReferenceServer
    {
        /// <summary>
        /// The model namespace URI shared by all coverage pipelines.
        /// </summary>
        public const string NamespaceUri = CoverageTestCatalogue.NamespaceUri;

        /// <summary>
        /// Initializes the server and registers the source-generated fluent
        /// node manager factory for the coverage model.
        /// </summary>
        /// <param name="telemetry">
        /// Telemetry context forwarded to the base server.
        /// </param>
        public CoverageTestFluentGenServer(ITelemetryContext telemetry)
            : base(telemetry)
        {
            AddNodeManager(new CoverageTestFluentNodeManagerFactory());
            AddNodeManager(new CoverageTestSecondaryFluentNodeManagerFactory());
        }
    }
}
