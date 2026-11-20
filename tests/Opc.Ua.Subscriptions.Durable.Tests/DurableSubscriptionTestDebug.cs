// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Subscriptions.Durable.Tests
{
    [TestFixture]
    [Explicit]
    public class DurableSubscriptionTestDebug
    {
        internal const int LoopCount = 100;

        [Test]
        [TestCase(false, false, TestName = "Validate Session Close")]
        [TestCase(true, false, TestName = "Validate Transfer")]
        [TestCase(true, true, TestName = "Restart of Server")]
        public async Task TransferLoopTestAsync(bool setSubscriptionDurable, bool restartServer)
        {
            var test = new DurableSubscriptionTest();
            await test.OneTimeSetUpAsync().ConfigureAwait(false);
            for (int i = 0; i < LoopCount; i++)
            {
                await test.SetUpAsync().ConfigureAwait(false);
                await test.TestSessionTransferAsync(setSubscriptionDurable, restartServer).ConfigureAwait(false);
                await test.TearDownAsync().ConfigureAwait(false);
                TestContext.Out.WriteLine("===========================================");
                TestContext.Out.WriteLine("===========================================");
                TestContext.Out.WriteLine($"Completed {i}th iteration.");
                TestContext.Out.WriteLine("===========================================");
                TestContext.Out.WriteLine("===========================================");
            }
            await test.OneTimeTearDownAsync().ConfigureAwait(false);
        }
    }
}
