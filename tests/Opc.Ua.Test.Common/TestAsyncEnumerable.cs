// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Opc.Ua.PubSub.Tests
{
    /// <summary>
    /// Test helper that yields an empty <see cref="IAsyncEnumerable{T}"/>.
    /// Replaces <c>System.Linq.AsyncEnumerable.Empty</c>, which is only
    /// available on the modern .NET target frameworks and not on the
    /// <c>net48</c> test matrix.
    /// </summary>
    public static class TestAsyncEnumerable
    {
        /// <summary>
        /// Returns an empty asynchronous sequence of <typeparamref name="T"/>.
        /// </summary>
        /// <typeparam name="T">Element type.</typeparam>
        /// <returns>An empty <see cref="IAsyncEnumerable{T}"/>.</returns>
        public static async IAsyncEnumerable<T> Empty<T>()
        {
            await Task.CompletedTask.ConfigureAwait(false);
            yield break;
        }
    }
}
