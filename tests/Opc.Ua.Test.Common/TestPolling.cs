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

#nullable enable

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
