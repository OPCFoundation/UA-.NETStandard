// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Tests
{
    /// <summary>
    /// Shared deadline-based polling helpers for tests that await a
    /// condition produced by background work. Use this instead of adding
    /// another private <c>WaitUntilAsync</c> copy so CI timeout tuning
    /// happens in one place.
    /// </summary>
    public static class TestPolling
    {
        /// <summary>
        /// Polls <paramref name="condition"/> until it returns
        /// <see langword="true"/> or the timeout elapses, failing the test
        /// with <paramref name="timeoutMessage"/> on expiry.
        /// </summary>
        /// <param name="condition">The condition to await.</param>
        /// <param name="timeout">
        /// Maximum time to wait; defaults to 10 seconds.
        /// </param>
        /// <param name="timeoutMessage">
        /// The assertion message used when the deadline expires.
        /// </param>
        public static async Task WaitUntilAsync(
            Func<bool> condition,
            TimeSpan? timeout = null,
            string timeoutMessage = "Timed out waiting for the condition.")
        {
            DateTime deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    Assert.Fail(timeoutMessage);
                }

                await Task.Delay(10).ConfigureAwait(false);
            }
        }
    }
}
