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
 *
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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua
{
    /// <summary>
    /// Helpers for working with <see cref="TimeProvider"/> in monotonic duration math
    /// and for bridging legacy public APIs that expose <see cref="int"/> tick counts.
    /// </summary>
    /// <remarks>
    /// These helpers exist primarily to support the migration off
    /// <see cref="HiResClock"/>; new public APIs should consume
    /// <see cref="TimeProvider"/> directly via constructor injection.
    /// </remarks>
    public static class TimeProviderExtensions
    {
        /// <summary>
        /// Returns the current monotonic timestamp normalised to milliseconds.
        /// </summary>
        /// <remarks>
        /// Equivalent to <see cref="HiResClock.TickCount64"/>, computed from
        /// <see cref="TimeProvider.GetTimestamp"/> and the provider's
        /// <see cref="TimeProvider.TimestampFrequency"/>.
        /// </remarks>
        public static long GetTimestampMilliseconds(this TimeProvider timeProvider)
        {
            if (timeProvider == null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }
            long ticks = timeProvider.GetTimestamp();
            long frequency = timeProvider.TimestampFrequency;
            return ticks / (frequency / 1000L);
        }

        /// <summary>
        /// Returns a monotonic 32-bit tick count derived from the provider's
        /// millisecond timestamp.
        /// </summary>
        /// <remarks>
        /// Provided strictly for back-compatibility with public APIs that still
        /// expose <see cref="int"/> tick values (e.g.
        /// <c>Session.LastKeepAliveTickCount</c>). The value wraps every
        /// ~49.7 days, matching the semantics of <see cref="Environment.TickCount"/>.
        /// New code should store the underlying <see cref="long"/> timestamp instead.
        /// </remarks>
        public static int GetTickCount(this TimeProvider timeProvider)
        {
            return unchecked((int)timeProvider.GetTimestampMilliseconds());
        }

        /// <summary>
        /// Returns the elapsed time since the supplied start timestamp obtained
        /// from <see cref="TimeProvider.GetTimestamp"/>.
        /// </summary>
        public static TimeSpan GetElapsedTimeSince(
            this TimeProvider timeProvider,
            long startTimestamp)
        {
            if (timeProvider == null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }
            return timeProvider.GetElapsedTime(startTimestamp);
        }

        /// <summary>
        /// Creates a <see cref="Task"/> that completes after the supplied
        /// <paramref name="delay"/> measured by the supplied
        /// <paramref name="timeProvider"/>, or when the
        /// <paramref name="cancellationToken"/> is cancelled.
        /// </summary>
        /// <remarks>
        /// Polyfill for the .NET 8+ <c>Task.Delay(TimeSpan, TimeProvider,
        /// CancellationToken)</c> static method that is not available on
        /// netstandard2.0 / .NET Framework. On .NET 8+ this delegates to the
        /// built-in BCL overload; on older targets it is implemented manually
        /// using <see cref="TimeProvider.CreateTimer"/>.
        /// </remarks>
        public static Task Delay(
            this TimeProvider timeProvider,
            TimeSpan delay,
            CancellationToken cancellationToken = default)
        {
            if (timeProvider == null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }
#if NET8_0_OR_GREATER
            return Task.Delay(delay, timeProvider, cancellationToken);
#else
            if (timeProvider == TimeProvider.System)
            {
                return Task.Delay(delay, cancellationToken);
            }
            if (delay != Timeout.InfiniteTimeSpan && delay < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(delay));
            }
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }
            if (delay == TimeSpan.Zero)
            {
                return Task.CompletedTask;
            }
            var state = new DelayState(cancellationToken);
            state.Timer = timeProvider.CreateTimer(
                static obj =>
                {
                    var s = (DelayState)obj!;
                    s.TrySetResult(true);
                    s.Registration.Dispose();
                    s.Timer?.Dispose();
                },
                state,
                delay,
                Timeout.InfiniteTimeSpan);
            if (cancellationToken.CanBeCanceled)
            {
                state.Registration = cancellationToken.Register(
                    static obj =>
                    {
                        var s = (DelayState)obj!;
                        s.TrySetCanceled(s.Token);
                        s.Timer?.Dispose();
                    },
                    state);
            }
            return state.Task;
#endif
        }

#if !NET8_0_OR_GREATER
        private sealed class DelayState : TaskCompletionSource<bool>
        {
            public DelayState(CancellationToken cancellationToken)
                : base(TaskCreationOptions.RunContinuationsAsynchronously)
            {
                Token = cancellationToken;
            }

            public CancellationToken Token { get; }
            public ITimer? Timer { get; set; }
            public CancellationTokenRegistration Registration { get; set; }
        }
#endif

        /// <summary>
        /// Creates a <see cref="CancellationTokenSource"/> that cancels
        /// automatically after the supplied <paramref name="delay"/> measured
        /// by the supplied <paramref name="timeProvider"/>.
        /// </summary>
        /// <remarks>
        /// Polyfill for the .NET 8+
        /// <c>CancellationTokenSource(TimeSpan, TimeProvider)</c> constructor
        /// that is not available on netstandard2.0 / .NET Framework. On
        /// .NET 8+ this delegates to the built-in BCL constructor; on older
        /// targets it is implemented manually using
        /// <see cref="TimeProvider.CreateTimer"/>.
        /// </remarks>
        public static CancellationTokenSource CreateCancellationTokenSource(
            this TimeProvider timeProvider,
            TimeSpan delay)
        {
            if (timeProvider == null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }
#if NET8_0_OR_GREATER
            return new CancellationTokenSource(delay, timeProvider);
#else
            if (timeProvider == TimeProvider.System)
            {
                return new CancellationTokenSource(delay);
            }
            if (delay != Timeout.InfiniteTimeSpan && delay < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(delay));
            }
            var cts = new CancellationTokenSource();
            if (delay == Timeout.InfiniteTimeSpan)
            {
                return cts;
            }
            ITimer? timer = null;
            timer = timeProvider.CreateTimer(
                static state =>
                {
                    var inner = (CancellationTokenSource)state!;
                    try
                    {
                        inner.Cancel();
                    }
                    catch (ObjectDisposedException)
                    {
                    }
                },
                cts,
                delay,
                Timeout.InfiniteTimeSpan);
            cts.Token.Register(static t => ((ITimer)t!).Dispose(), timer);
            return cts;
#endif
        }
    }
}
