// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using NUnit.Framework;

namespace Opc.Ua.Redundancy.Samples.Tests
{
    /// <summary>
    /// Confirms the legacy placeholder target can execute under the CI test host.
    /// </summary>
    [TestFixture]
    internal sealed class PlaceholderTests
    {
        /// <summary>
        /// Records one successful test result for legacy target-framework jobs.
        /// </summary>
        [Test]
        public void LegacyTargetBuildsPlaceholder()
        {
            Assert.Pass("The legacy placeholder target executed successfully.");
        }
    }
}
