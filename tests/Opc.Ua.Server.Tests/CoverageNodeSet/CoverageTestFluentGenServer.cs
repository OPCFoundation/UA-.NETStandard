// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

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
