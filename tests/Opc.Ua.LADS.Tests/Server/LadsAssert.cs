/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
