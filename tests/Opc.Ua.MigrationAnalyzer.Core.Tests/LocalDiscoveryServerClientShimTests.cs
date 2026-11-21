// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Gds.Client;

namespace Opc.Ua.MigrationAnalyzer.Core.Tests
{
    /// <summary>
    /// Runtime tests for <see cref="LocalDiscoveryServerClientShim"/>.
    /// </summary>
    /// <remarks>
    /// <see cref="LocalDiscoveryServerClient.FindServersAsync"/> is not
    /// virtual, so the APM <c>BeginFindServers</c>/<c>EndFindServers</c>
    /// adapter cannot be exercised against a Moq stand-in. End-to-end
    /// validation requires a live LDS endpoint and lives in the discovery
    /// integration suite.
    /// </remarks>
    [TestFixture]
    [Category("Shim")]
    public class LocalDiscoveryServerClientShimTests
    {
        /// <summary>
        /// Placeholder for the APM adapter test. Requires a live LDS
        /// endpoint to drive <c>FindServersAsync</c>.
        /// </summary>
        [Test]
        [Ignore("Requires Local Discovery Server endpoint: " +
            "LocalDiscoveryServerClient.FindServersAsync is non-virtual, so " +
            "the APM Begin/End shim cannot be intercepted via Moq. " +
            "Integration coverage lives in Opc.Ua.Lds.Tests.")]
        public Task BeginEndFindServersDeliverAsyncResultAsync()
        {
            return Task.CompletedTask;
        }
    }
}
