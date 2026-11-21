// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using NUnit.Framework;

namespace Opc.Ua.Server.Tests.CoverageNodeSet
{
    /// <summary>
    /// Runs the shared coverage assertion battery against the
    /// <b>source-generation</b> server.
    /// </summary>
    [TestFixture]
    [Category("CoverageNodeSet")]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class CoverageTestSourceGenAssertionsTests
        : CoverageNodeSetAssertionsBase<CoverageTestSourceGenServer>
    {
        /// <inheritdoc/>
        protected override CoverageTestSourceGenServer CreateServer(ITelemetryContext telemetry)
        {
            return new CoverageTestSourceGenServer(telemetry);
        }
    }

    /// <summary>
    /// Runs the shared coverage assertion battery against the
    /// <b>runtime-import</b> server.
    /// </summary>
    [TestFixture]
    [Category("CoverageNodeSet")]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class CoverageTestRuntimeAssertionsTests
        : CoverageNodeSetAssertionsBase<CoverageTestRuntimeServer>
    {
        /// <inheritdoc/>
        protected override CoverageTestRuntimeServer CreateServer(ITelemetryContext telemetry)
        {
            return new CoverageTestRuntimeServer(telemetry);
        }

        /// <summary>
        /// The runtime importer materialises the address space from raw bytes
        /// and carries no <c>ModelDependency</c> assembly attribute, so the
        /// published NamespaceMetadata Object does not surface the model
        /// version. Only the object and its NamespaceUri are asserted.
        /// </summary>
        protected override bool PublishesModelVersion => false;
    }

    /// <summary>
    /// Runs the shared coverage assertion battery against the fully
    /// <b>source-generated fluent</b> node manager server.
    /// </summary>
    [TestFixture]
    [Category("CoverageNodeSet")]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public sealed class CoverageTestFluentGenAssertionsTests
        : CoverageNodeSetAssertionsBase<CoverageTestFluentGenServer>
    {
        /// <inheritdoc/>
        protected override CoverageTestFluentGenServer CreateServer(ITelemetryContext telemetry)
        {
            return new CoverageTestFluentGenServer(telemetry);
        }
    }
}
