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

using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.LADS.Tests.Server
{
    /// <summary>
    /// Assertion helpers of the LADS server tests.
    /// </summary>
    internal static class LadsAssert
    {
        /// <summary>
        /// Asserts that an asynchronous command fails with a status code.
        /// </summary>
        public static async Task ThrowsStatusAsync(StatusCode expected, Func<ValueTask> command)
        {
            ServiceResultException? exception = null;
            try
            {
                await command().ConfigureAwait(false);
            }
            catch (ServiceResultException ex)
            {
                exception = ex;
            }
            Assert.That(exception, Is.Not.Null, $"Expected {expected}.");
            Assert.That(exception!.StatusCode, Is.EqualTo(expected), exception.Message);
        }

        /// <summary>
        /// Asserts that an asynchronous command returning a value fails with a status code.
        /// </summary>
        public static Task ThrowsStatusAsync<T>(StatusCode expected, Func<ValueTask<T>> command)
        {
            return ThrowsStatusAsync(expected, () => new ValueTask(command().AsTask()));
        }

        /// <summary>
        /// Asserts that a synchronous call fails with a status code.
        /// </summary>
        public static void ThrowsStatus(StatusCode expected, Action command)
        {
            ServiceResultException? exception = null;
            try
            {
                command();
            }
            catch (ServiceResultException ex)
            {
                exception = ex;
            }
            Assert.That(exception, Is.Not.Null, $"Expected {expected}.");
            Assert.That(exception!.StatusCode, Is.EqualTo(expected), exception.Message);
        }

        /// <summary>
        /// Waits until a condition holds.
        /// </summary>
        public static async Task EventuallyAsync(Func<bool> condition, string message)
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (!condition())
            {
                if (DateTime.UtcNow > deadline)
                {
                    Assert.Fail(message);
                }
                await Task.Delay(10).ConfigureAwait(false);
            }
        }
    }
}
