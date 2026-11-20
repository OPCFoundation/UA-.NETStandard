// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using NUnit.Framework;
using Opc.Ua.Security.Certificates;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Security.Tests
{
    /// <summary>
    /// Assembly-level setup/teardown that verifies no Certificate
    /// instances are leaked during the test run.
    /// </summary>
    [SetUpFixture]
    public class LeakDetectionSetup
    {
        [OneTimeSetUp]
        public void GlobalSetup()
        {
            Certificate.ResetLeakCounters();
        }

        [OneTimeTearDown]
        public void GlobalTeardown()
        {
            // Release the shared X509 user-identity signing provider/store so its
            // private-key cache disposes the certificates it materialised.
            X509UserIdentityHelper.DisposeSharedResources();
            LeakDetectionHelpers.AssertNoCertificateLeaks();
        }
    }
}
