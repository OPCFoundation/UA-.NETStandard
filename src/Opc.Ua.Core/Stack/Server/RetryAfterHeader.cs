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
                return milliseconds > 0
                    ? TimeSpan.FromMilliseconds(milliseconds)
                    : null;
            }

            return null;
        }
    }
}
