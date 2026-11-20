// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua
{
    /// <summary>
    /// Reads and writes a server "retry-after" backpressure hint carried in a
    /// <see cref="ResponseHeader.AdditionalHeader"/> as an
    /// <see cref="AdditionalParametersType"/> parameter
    /// (<see cref="AdditionalParameterNames.RetryAfterMs"/>).
    /// </summary>
    /// <remarks>
    /// The additional header is delivered independently of
    /// <c>RequestHeader.ReturnDiagnostics</c>, so a cooperating client can honor
    /// the hint without requesting diagnostics. The value is a whole number of
    /// milliseconds encoded as an <c>Int64</c>. A <see cref="ServiceFault"/>
    /// carries a <see cref="ResponseHeader"/>, so this carrier also covers
    /// transient overload rejections such as <c>BadServerTooBusy</c>.
    /// </remarks>
    public static class RetryAfterHeader
    {
        /// <summary>
        /// Attaches (or merges into an existing set of additional parameters) a
        /// retry-after hint on a response header. A non-positive delay is ignored.
        /// </summary>
        /// <param name="header">
        /// The response header to annotate.
        /// </param>
        /// <param name="retryAfter">
        /// The suggested minimum delay before the client should retry.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="header"/> is <c>null</c>.</exception>
        public static void AttachTo(ResponseHeader header, TimeSpan retryAfter)
        {
            if (header == null)
            {
                throw new ArgumentNullException(nameof(header));
            }

            if (retryAfter <= TimeSpan.Zero)
            {
                return;
            }

            long milliseconds = (long)Math.Ceiling(retryAfter.TotalMilliseconds);
            var parameter = new KeyValuePair
            {
                Key = QualifiedName.From(AdditionalParameterNames.RetryAfterMs),
                Value = Variant.From(milliseconds)
            };

            if (header.AdditionalHeader.TryGetValue(out AdditionalParametersType? existing) &&
                existing != null)
            {
                existing.Parameters = existing.Parameters.AddItem(parameter);
                header.AdditionalHeader = new ExtensionObject(existing);
            }
            else
            {
                header.AdditionalHeader = new ExtensionObject(
                    new AdditionalParametersType { Parameters = [parameter] });
            }
        }

        /// <summary>
        /// Reads a retry-after hint from a response header's additional parameters.
        /// </summary>
        /// <param name="header">
        /// The response header to read, may be <c>null</c>.
        /// </param>
        /// <returns>
        /// The retry-after delay, or <c>null</c> when absent or malformed.
        /// </returns>
        public static TimeSpan? Read(ResponseHeader? header)
        {
            if (header == null ||
                !header.AdditionalHeader.TryGetValue(out AdditionalParametersType? parameters) ||
                parameters == null)
            {
                return null;
            }

            foreach (KeyValuePair parameter in parameters.Parameters)
            {
                if (parameter.Key != AdditionalParameterNames.RetryAfterMs)
                {
                    continue;
                }

                if (parameter.Value.TypeInfo != TypeInfo.Scalars.Int64)
                {
                    return null;
                }

                long milliseconds = parameter.Value.GetInt64(0);
                if (milliseconds <= 0)
                {
                    return null;
                }

                // the value comes from the peer: clamp it like the text hints so
                // an oversized value cannot overflow TimeSpan.
                long maxMilliseconds = (long)RetryAfterHint.MaxRetryAfter.TotalMilliseconds;
                return TimeSpan.FromMilliseconds(Math.Min(milliseconds, maxMilliseconds));
            }

            return null;
        }
    }
}
