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
using System.Collections.Generic;

namespace Opc.Ua.PubSub.Security.Sks
{
    /// <summary>
    /// Server-side state of a single SecurityGroup as held inside an
    /// <see cref="IPubSubKeyServiceServer"/>. Carries the configured
    /// algorithm, lifetime and history bounds together with the
    /// currently issued <see cref="PubSubSecurityKey"/> material.
    /// </summary>
    /// <remarks>
    /// Mirrors the SecurityGroup configuration described in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/8.3.1">
    /// Part 14 §8.3.1 PubSubKeyServiceType</see>. A single
    /// <see cref="SecurityGroupId"/> value uniquely identifies the
    /// group within an SKS.
    /// </remarks>
    public sealed record SksSecurityGroup
    {
        /// <summary>
        /// Initializes a new <see cref="SksSecurityGroup"/>.
        /// </summary>
        /// <param name="securityGroupId">SecurityGroup identifier.</param>
        /// <param name="securityPolicyUri">
        /// URI of the security policy applied to this group.
        /// </param>
        /// <param name="keyLifetime">Per-key validity duration.</param>
        /// <param name="maxFutureKeyCount">
        /// Maximum number of pre-issued future keys that the SKS may
        /// hand out in a single <c>GetSecurityKeys</c> call.
        /// </param>
        /// <param name="maxPastKeyCount">
        /// Maximum number of expired keys retained for late-arrival
        /// decryption.
        /// </param>
        /// <param name="keys">
        /// Ordered key history (oldest first).
        /// </param>
        public SksSecurityGroup(
            string securityGroupId,
            string securityPolicyUri,
            TimeSpan keyLifetime,
            int maxFutureKeyCount,
            int maxPastKeyCount,
            IReadOnlyList<PubSubSecurityKey> keys)
        {
            if (string.IsNullOrEmpty(securityGroupId))
            {
                throw new ArgumentException(
                    "SecurityGroupId must be non-empty.",
                    nameof(securityGroupId));
            }
            if (string.IsNullOrEmpty(securityPolicyUri))
            {
                throw new ArgumentException(
                    "SecurityPolicyUri must be non-empty.",
                    nameof(securityPolicyUri));
            }
            if (keyLifetime <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(keyLifetime),
                    "Key lifetime must be positive.");
            }
            if (maxFutureKeyCount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxFutureKeyCount),
                    "Max future key count must be non-negative.");
            }
            if (maxPastKeyCount < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxPastKeyCount),
                    "Max past key count must be non-negative.");
            }
            if (keys is null)
            {
                throw new ArgumentNullException(nameof(keys));
            }

            SecurityGroupId = securityGroupId;
            SecurityPolicyUri = securityPolicyUri;
            KeyLifetime = keyLifetime;
            MaxFutureKeyCount = maxFutureKeyCount;
            MaxPastKeyCount = maxPastKeyCount;
            Keys = keys;
        }

        /// <summary>
        /// Identifier of the SecurityGroup.
        /// </summary>
        public string SecurityGroupId { get; }

        /// <summary>
        /// URI of the security policy applied to this group.
        /// </summary>
        public string SecurityPolicyUri { get; }

        /// <summary>
        /// Per-key validity duration.
        /// </summary>
        public TimeSpan KeyLifetime { get; }

        /// <summary>
        /// Maximum number of pre-issued future keys served in one call.
        /// </summary>
        public int MaxFutureKeyCount { get; }

        /// <summary>
        /// Maximum number of expired keys retained for late-arrival
        /// decryption.
        /// </summary>
        public int MaxPastKeyCount { get; }

        /// <summary>
        /// Ordered key history (oldest first). The current key is the
        /// first non-expired entry.
        /// </summary>
        public IReadOnlyList<PubSubSecurityKey> Keys { get; }
    }
}
