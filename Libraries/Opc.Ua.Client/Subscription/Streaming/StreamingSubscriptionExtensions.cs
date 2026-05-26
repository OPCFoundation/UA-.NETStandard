/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client.Subscriptions.Streaming
{
    /// <summary>
    /// Extensions on <see cref="IAsyncEnumerable{T}"/> useful for
    /// short-lived subscriptions tracking state-machine transitions
    /// or bounded observation windows.
    /// </summary>
    public static class StreamingSubscriptionExtensions
    {
        /// <summary>
        /// Yields items until the predicate returns <c>true</c>. The
        /// matching item is yielded last, then enumeration stops.
        /// Useful for waiting on a specific state transition.
        /// </summary>
        public static IAsyncEnumerable<T> TakeUntilAsync<T>(
            this IAsyncEnumerable<T> source,
            Func<T, bool> predicate,
            CancellationToken ct = default)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (predicate == null)
            {
                throw new ArgumentNullException(nameof(predicate));
            }
            return TakeUntilImpl(source, predicate, ct);
        }

        private static async IAsyncEnumerable<T> TakeUntilImpl<T>(
            IAsyncEnumerable<T> source,
            Func<T, bool> predicate,
            [EnumeratorCancellation] CancellationToken ct)
        {
            await foreach (T item in source.ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();
                yield return item;
                if (predicate(item))
                {
                    yield break;
                }
            }
        }

        /// <summary>
        /// Yields items until the timeout elapses. The enumeration
        /// completes silently when the timeout expires.
        /// </summary>
        public static IAsyncEnumerable<T> WithTimeoutAsync<T>(
            this IAsyncEnumerable<T> source,
            TimeSpan timeout,
            CancellationToken ct = default)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            return WithTimeoutImpl(source, timeout, ct);
        }

        private static async IAsyncEnumerable<T> WithTimeoutImpl<T>(
            IAsyncEnumerable<T> source,
            TimeSpan timeout,
            [EnumeratorCancellation] CancellationToken ct)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(timeout);

            IAsyncEnumerator<T> enumerator =
                source.GetAsyncEnumerator(cts.Token);

            try
            {
                while (true)
                {
                    bool hasNext;
                    try
                    {
                        hasNext = await enumerator.MoveNextAsync().ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (cts.IsCancellationRequested && !ct.IsCancellationRequested)
                    {
                        yield break;
                    }

                    if (!hasNext)
                    {
                        yield break;
                    }

                    yield return enumerator.Current;
                }
            }
            finally
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Yields exactly <paramref name="count"/> items then completes.
        /// </summary>
        public static IAsyncEnumerable<T> TakeAsync<T>(
            this IAsyncEnumerable<T> source,
            int count,
            CancellationToken ct = default)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            return TakeImpl(source, count, ct);
        }

        private static async IAsyncEnumerable<T> TakeImpl<T>(
            IAsyncEnumerable<T> source,
            int count,
            [EnumeratorCancellation] CancellationToken ct)
        {
            int taken = 0;
            await foreach (T item in source.ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();
                yield return item;
                if (++taken >= count)
                {
                    yield break;
                }
            }
        }

        /// <summary>
        /// Buffers the first <paramref name="count"/> items into a
        /// list and returns it.
        /// </summary>
        public static async System.Threading.Tasks.ValueTask<IReadOnlyList<T>> BufferedAsync<T>(
            this IAsyncEnumerable<T> source,
            int count,
            CancellationToken ct = default)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (count <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            var buffer = new List<T>(count);
            await foreach (T item in source.ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();
                buffer.Add(item);
                if (buffer.Count >= count)
                {
                    break;
                }
            }
            return buffer;
        }
    }
}
