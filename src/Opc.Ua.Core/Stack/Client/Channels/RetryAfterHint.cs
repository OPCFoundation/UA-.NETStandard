// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;

namespace Opc.Ua
{
    /// <summary>
    /// Parses and applies server retry-after hints carried as additional parameters or text tokens.
    /// </summary>
    public static class RetryAfterHint
    {
        /// <summary>
        /// Maximum retry-after duration accepted from a remote peer.
        /// </summary>
        public static TimeSpan MaxRetryAfter { get; } = TimeSpan.FromDays(1);

        /// <summary>
        /// Parses a <c>RetryAfterMs=N</c> token from the supplied text.
        /// </summary>
        /// <param name="text">Text that may contain a retry-after token.</param>
        /// <returns>The parsed duration, or <c>null</c> when the token is absent or invalid.</returns>
        public static TimeSpan? ParseServerRetryAfter(string? text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            int index = text!.IndexOf(kRetryAfterHintPrefix, StringComparison.Ordinal);
            if (index < 0)
            {
                return null;
            }

            int start = index + kRetryAfterHintPrefix.Length;
            int end = start;
            long milliseconds = 0;
            long maxMilliseconds = (long)MaxRetryAfter.TotalMilliseconds;
            while (end < text.Length && text[end] is >= '0' and <= '9')
            {
                milliseconds = (milliseconds * 10) + (text[end] - '0');
                end++;

                if (milliseconds >= maxMilliseconds)
                {
                    milliseconds = maxMilliseconds;
                    break;
                }
            }

            if (end > start && milliseconds > 0)
            {
                return TimeSpan.FromMilliseconds(milliseconds);
            }

            return null;
        }

        /// <summary>
        /// Parses a retry-after hint from a transient server-busy result.
        /// </summary>
        /// <param name="result">The service result to inspect.</param>
        /// <returns>The parsed duration, or <c>null</c> when no server-busy hint is present.</returns>
        public static TimeSpan? ParseServerBusyRetryAfter(ServiceResult? result)
        {
            ServiceResult? current = result;
            while (current != null)
            {
                if (IsServerBusyStatus(current.StatusCode))
                {
                    TimeSpan? retryAfter = ParseServerRetryAfter(current.AdditionalInfo)
                        ?? ParseServerRetryAfter(current.LocalizedText.Text);
                    if (retryAfter.HasValue)
                    {
                        return retryAfter;
                    }
                }

                current = current.InnerResult;
            }

            return null;
        }

        /// <summary>
        /// Applies a server retry-after hint as a lower bound on a reconnect delay.
        /// </summary>
        /// <param name="policy">The reconnect policy that produced <paramref name="delay"/>.</param>
        /// <param name="delay">The reconnect delay produced by the policy.</param>
        /// <param name="serverRetryAfter">The server-provided retry-after hint.</param>
        /// <returns>The effective delay with the hint honored as a lower bound.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="policy"/> is <c>null</c>.</exception>
        public static TimeSpan ApplyReconnectDelayLowerBound(
            IChannelReconnectPolicy policy,
            TimeSpan delay,
            TimeSpan? serverRetryAfter)
        {
            if (policy == null)
            {
                throw new ArgumentNullException(nameof(policy));
            }

            if (!serverRetryAfter.HasValue ||
                serverRetryAfter.Value <= TimeSpan.Zero ||
                delay < TimeSpan.Zero)
            {
                return delay;
            }

            TimeSpan hint = ClampToPolicyMax(policy, serverRetryAfter.Value);
            return hint > delay ? hint : delay;
        }

        /// <summary>
        /// Checks whether a status code represents transient server overload.
        /// </summary>
        /// <param name="status">The status code to inspect.</param>
        /// <returns><c>true</c> when <paramref name="status"/> is a server-busy signal.</returns>
        public static bool IsServerBusyStatus(StatusCode status)
        {
            return status == StatusCodes.BadTcpServerTooBusy ||
                status == StatusCodes.BadServerTooBusy;
        }

        private static TimeSpan ClampToPolicyMax(IChannelReconnectPolicy policy, TimeSpan hint)
        {
            if (policy is ExponentialBackoffChannelReconnectPolicy exponential &&
                exponential.MaxDelay >= TimeSpan.Zero &&
                hint > exponential.MaxDelay)
            {
                return exponential.MaxDelay;
            }

            return hint;
        }

        private const string kRetryAfterHintPrefix = AdditionalParameterNames.RetryAfterMs + "=";
    }
}
