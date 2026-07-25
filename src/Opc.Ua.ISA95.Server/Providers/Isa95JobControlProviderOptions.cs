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

namespace Opc.Ua.ISA95.Server.Providers
{
    /// <summary>
    /// Bounded, validated options for the <see cref="InMemoryIsa95JobControlProvider"/>.
    /// The defaults are non-durable and conservative; they can be tuned to bound
    /// the memory footprint of the engine.
    /// </summary>
    public sealed class Isa95JobControlProviderOptions
    {
        /// <summary>
        /// The maximum number of job orders that can be tracked concurrently.
        /// Storing a new order beyond this bound is rejected. Must be at least 1.
        /// Defaults to 1024.
        /// </summary>
        public int MaxJobOrders { get; set; } = 1024;

        /// <summary>
        /// The maximum number of job responses that can be retained concurrently.
        /// Receiving a new response beyond this bound is rejected. Must be at
        /// least 1. Defaults to 1024.
        /// </summary>
        public int MaxJobResponses { get; set; } = 1024;

        /// <summary>
        /// The maximum age of a retained job response. Responses older than this
        /// value are purged (using the injected <see cref="TimeProvider"/>) before
        /// a new response is received or a query is served.
        /// <see cref="TimeSpan.Zero"/> disables age-based purging. Must not be
        /// negative. Defaults to <see cref="TimeSpan.Zero"/>.
        /// </summary>
        public TimeSpan ResponseRetention { get; set; } = TimeSpan.Zero;

        /// <summary>
        /// Validates the option values and throws if any are out of range.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// Thrown when an option value is out of its permitted range.
        /// </exception>
        public void Validate()
        {
            if (MaxJobOrders < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaxJobOrders),
                    MaxJobOrders,
                    "The maximum number of job orders must be at least 1.");
            }
            if (MaxJobResponses < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(MaxJobResponses),
                    MaxJobResponses,
                    "The maximum number of job responses must be at least 1.");
            }
            if (ResponseRetention < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(ResponseRetention),
                    ResponseRetention,
                    "The response retention must not be negative.");
            }
        }
    }
}
