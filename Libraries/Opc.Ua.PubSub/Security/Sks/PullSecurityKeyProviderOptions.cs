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

namespace Opc.Ua.PubSub.Security.Sks
{
    /// <summary>
    /// Bindable options that tune the behaviour of a single
    /// <see cref="PullSecurityKeyProvider"/>. Defaults are chosen to
    /// match the operational guidance in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.3">
    /// Part 14 §8.3 Security Key Service</see>: pre-fetch a small
    /// future window so that publish never blocks on the SKS, and
    /// schedule the next pull a few minutes before the active key
    /// expires.
    /// </summary>
    public sealed class PullSecurityKeyProviderOptions
    {
        /// <summary>
        /// Number of future keys to pre-fetch from the SKS in
        /// addition to the currently active key. The total number
        /// of keys requested per pull is
        /// <c>RequestedFutureKeyCount + 1</c>.
        /// </summary>
        public int RequestedFutureKeyCount { get; set; } = 4;

        /// <summary>
        /// Delta subtracted from the current key's expiration to
        /// schedule the next refresh. Should be large enough that
        /// the SKS round-trip cannot push the refresh past the
        /// expiration boundary even under adverse network conditions.
        /// </summary>
        public TimeSpan RefreshLeadTime { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Delay applied between consecutive failed refresh attempts.
        /// </summary>
        public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Maximum number of consecutive failed refresh attempts
        /// tolerated before the provider stops scheduling retries.
        /// The provider keeps serving the last-known keys until a
        /// caller-driven action restarts it.
        /// </summary>
        public int MaxConsecutiveFailures { get; set; } = 5;
    }
}
