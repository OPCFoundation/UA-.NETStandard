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
using System.Threading;

namespace Opc.Ua
{
    /// <summary>
    /// Controls the retry behavior of an
    /// <see cref="IClientChannelManager"/> when a transport channel
    /// reconnect attempt fails.
    /// </summary>
    /// <remarks>
    /// The default policy
    /// (<see cref="ExponentialBackoffChannelReconnectPolicy"/>) mirrors
    /// the historical <c>SessionReconnectHandler</c> behavior:
    /// exponential backoff starting at <c>500&#160;ms</c>, doubling each
    /// attempt up to <c>30&#160;s</c>, with no overall attempt limit.
    /// </remarks>
    public interface IChannelReconnectPolicy
    {
        /// <summary>
        /// Compute the delay before the next reconnect attempt.
        /// </summary>
        /// <param name="attempt">Attempt counter, 0-based. The very
        /// first reconnect attempt is invoked with <paramref name="attempt"/>
        /// equal to 0 and the returned delay is applied before the
        /// attempt is executed.</param>
        /// <returns>The wait period; <see cref="TimeSpan.Zero"/> means
        /// "retry immediately". Return a negative value (or
        /// <see cref="Timeout.InfiniteTimeSpan"/>) to indicate the
        /// manager should stop retrying and transition to
        /// <see cref="ChannelState.Faulted"/>.</returns>
        TimeSpan GetDelay(int attempt);

#if NETSTANDARD2_1 || NET8_0_OR_GREATER
        /// <summary>
        /// Compute the delay before the next reconnect attempt while
        /// consulting a shared retry budget.
        /// </summary>
        /// <param name="attempt">Attempt counter, 0-based.</param>
        /// <param name="budget">Optional shared retry budget.</param>
        /// <returns>The wait period, capped to the remaining budget
        /// when possible.</returns>
        TimeSpan GetDelay(int attempt, IRetryBudget? budget)
        {
            return ChannelReconnectPolicyBudget.GetDelay(this, attempt, budget);
        }

        /// <summary>
        /// Maximum time a single participant's <see cref="IReconnectParticipant.OnReconnectAsync"/>
        /// invocation may run during one reconnect cycle.
        /// </summary>
        /// <remarks>
        /// The default preserves the historical unbounded behavior. Policies that opt in to bounded
        /// participant work should return a non-negative timeout value.
        /// </remarks>
        TimeSpan ParticipantTimeout => Timeout.InfiniteTimeSpan;
#endif
    }

    /// <summary>
    /// Optional interface for channel reconnect policies that provide
    /// a budget-aware delay on TFMs without default interface method
    /// support.
    /// </summary>
    public interface IBudgetAwareChannelReconnectPolicy : IChannelReconnectPolicy
    {
        /// <summary>
        /// Compute the delay before the next reconnect attempt while
        /// consulting a shared retry budget.
        /// </summary>
        /// <param name="attempt">Attempt counter, 0-based.</param>
        /// <param name="budget">Optional shared retry budget.</param>
        /// <returns>The wait period, capped to the remaining budget
        /// when possible.</returns>
#if NETSTANDARD2_1 || NET8_0_OR_GREATER
        new TimeSpan GetDelay(int attempt, IRetryBudget? budget);
#else
        TimeSpan GetDelay(int attempt, IRetryBudget? budget);
#endif
    }

    /// <summary>
    /// Optional interface for channel reconnect policies that bound participant reactivation
    /// callbacks on TFMs without default interface method support.
    /// </summary>
    public interface IParticipantTimeoutPolicy : IChannelReconnectPolicy
    {
        /// <summary>
        /// Maximum time a single participant's <see cref="IReconnectParticipant.OnReconnectAsync"/>
        /// invocation may run during one reconnect cycle.
        /// </summary>
#if NETSTANDARD2_1 || NET8_0_OR_GREATER
        new TimeSpan ParticipantTimeout { get; }
#else
        TimeSpan ParticipantTimeout { get; }
#endif
    }

    /// <summary>
    /// Provides a one-shot server retry-after hint for the next reconnect attempt.
    /// </summary>
    internal interface IServerRetryAfterHintProvider
    {
        /// <summary>
        /// Consumes the pending server retry-after hint, if one is available.
        /// </summary>
        /// <returns>The retry-after hint, or <c>null</c> when none is pending.</returns>
        TimeSpan? ConsumeServerRetryAfterHint();
    }

    internal static class ChannelReconnectPolicyBudget
    {
        internal static TimeSpan GetDelay(
            IChannelReconnectPolicy policy,
            int attempt,
            IRetryBudget? budget)
        {
            if (policy == null)
            {
                throw new ArgumentNullException(nameof(policy));
            }

            if (policy is IBudgetAwareChannelReconnectPolicy budgetAwarePolicy)
            {
                return budgetAwarePolicy.GetDelay(attempt, budget);
            }

            TimeSpan delay = policy.GetDelay(attempt);
            return ClampDelayToBudget(delay, budget);
        }

        internal static TimeSpan ClampDelayToBudget(
            TimeSpan delay,
            IRetryBudget? budget)
        {
            if (delay < TimeSpan.Zero || budget == null)
            {
                return delay;
            }

            return budget.TryConsume(out TimeSpan remaining)
                ? TimeSpan.FromTicks(Math.Min(delay.Ticks, remaining.Ticks))
                : Timeout.InfiniteTimeSpan;
        }
    }

    /// <summary>
    /// Default <see cref="IChannelReconnectPolicy"/>: exponential
    /// backoff with configurable minimum, maximum and maximum-attempt
    /// limit. Defaults match the historical <c>SessionReconnectHandler</c>
    /// behavior — <c>500&#160;ms → 30&#160;s</c>, unlimited attempts.
    /// </summary>
    public sealed class ExponentialBackoffChannelReconnectPolicy :
        IBudgetAwareChannelReconnectPolicy,
        IParticipantTimeoutPolicy
    {
        /// <summary>
        /// Initial delay applied before the first reconnect attempt.
        /// </summary>
        public TimeSpan MinDelay { get; init; } = TimeSpan.FromMilliseconds(500);

        /// <summary>
        /// Maximum delay between reconnect attempts.
        /// </summary>
        public TimeSpan MaxDelay { get; init; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Maximum number of attempts before giving up. Use
        /// <c>int.MaxValue</c> (default) for unlimited retries.
        /// </summary>
        public int MaxAttempts { get; init; } = int.MaxValue;

        /// <summary>
        /// Maximum time a single participant's <see cref="IReconnectParticipant.OnReconnectAsync"/>
        /// invocation may run during one reconnect cycle.
        /// </summary>
        public TimeSpan ParticipantTimeout { get; init; } = TimeSpan.FromSeconds(30);

        /// <inheritdoc/>
        public TimeSpan GetDelay(int attempt)
        {
            if (attempt < 0 || attempt >= MaxAttempts)
            {
                return Timeout.InfiniteTimeSpan;
            }

            double ms = MinDelay.TotalMilliseconds;
            double max = MaxDelay.TotalMilliseconds;
            for (int i = 0; i < attempt; i++)
            {
                ms *= 2;
                if (ms >= max)
                {
                    ms = max;
                    break;
                }
            }

            return TimeSpan.FromMilliseconds(ms);
        }

        /// <inheritdoc/>
        public TimeSpan GetDelay(int attempt, IRetryBudget? budget)
        {
            TimeSpan delay = GetDelay(attempt);
            if (delay < TimeSpan.Zero || budget == null)
            {
                return delay;
            }

            if (!budget.TryConsume(out TimeSpan remaining))
            {
                return Timeout.InfiniteTimeSpan;
            }

            return TimeSpan.FromTicks(Math.Min(delay.Ticks, remaining.Ticks));
        }
    }
}
