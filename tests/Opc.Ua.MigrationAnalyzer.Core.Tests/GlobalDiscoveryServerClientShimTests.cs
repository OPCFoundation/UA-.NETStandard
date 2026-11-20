// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Gds.Client;

namespace Opc.Ua.MigrationAnalyzer.Core.Tests
{
    /// <summary>
    /// Runtime tests for <see cref="GlobalDiscoveryServerClientShim"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="GlobalDiscoveryServerClient"/> is <c>sealed</c> and has
    /// non-virtual <c>RegisterApplicationAsync</c> / <c>UnregisterApplicationAsync</c>
    /// methods, so neither Moq nor a hand-rolled subclass can intercept the
    /// shim's forwarded call. Exercising the shim end-to-end requires a
    /// live GDS endpoint (full server + secure channel bootstrap), which
    /// belongs to the integration test suite (<c>Opc.Ua.Gds.Tests</c>), not
    /// a unit-level runtime check of the shim wiring.
    /// </remarks>
    [TestFixture]
    [Category("Shim")]
    public class GlobalDiscoveryServerClientShimTests
    {
        /// <summary>
        /// Placeholder for the shim invocation test. Requires a full GDS
        /// server bootstrap to exercise.
        /// </summary>
        [Test]
        [Ignore("Requires GDS server bootstrap: GlobalDiscoveryServerClient " +
            "is sealed and RegisterApplicationAsync is non-virtual, so the " +
            "shim cannot be exercised via Moq. Integration coverage lives " +
            "in Opc.Ua.Gds.Tests.")]
        public Task RegisterApplicationCallsRegisterApplicationAsyncAsync()
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// Placeholder for the unregister shim invocation test. Requires a
        /// full GDS server bootstrap to exercise.
        /// </summary>
        [Test]
        [Ignore("Requires GDS server bootstrap: GlobalDiscoveryServerClient " +
            "is sealed and UnregisterApplicationAsync is non-virtual, so the " +
            "shim cannot be exercised via Moq. Integration coverage lives " +
            "in Opc.Ua.Gds.Tests.")]
        public Task UnregisterApplicationCallsUnregisterApplicationAsyncAsync()
        {
            return Task.CompletedTask;
        }
    }
}
