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
