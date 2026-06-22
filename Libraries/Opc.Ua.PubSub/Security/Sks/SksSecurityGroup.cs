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
        /// <param name="authorizedCallerIdentities">
        /// Caller identities authorized to retrieve keys for this group.
        /// An empty list fails closed unless <paramref name="rolePermissions"/> grants Call.
        /// </param>
        /// <param name="rolePermissions">
        /// RolePermissions that control GetSecurityKeys Call access for this group.
        /// </param>
        public SksSecurityGroup(
            string securityGroupId,
            string securityPolicyUri,
            TimeSpan keyLifetime,
            int maxFutureKeyCount,
            int maxPastKeyCount,
            ArrayOf<PubSubSecurityKey> keys,
            ArrayOf<string> authorizedCallerIdentities = default,
            ArrayOf<RolePermissionType> rolePermissions = default)
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
            List<string> callers = [];
            if (!authorizedCallerIdentities.IsNull)
            {
                for (int i = 0; i < authorizedCallerIdentities.Count; i++)
                {
                    string caller = authorizedCallerIdentities[i];
                    if (string.IsNullOrEmpty(caller))
                    {
                        throw new ArgumentException(
                            "Authorized caller identities must be non-empty.",
                            nameof(authorizedCallerIdentities));
                    }
                    if (!ContainsCaller(callers, caller))
                    {
                        callers.Add(caller);
                    }
                }
            }

            SecurityGroupId = securityGroupId;
            SecurityPolicyUri = securityPolicyUri;
            KeyLifetime = keyLifetime;
            MaxFutureKeyCount = maxFutureKeyCount;
            MaxPastKeyCount = maxPastKeyCount;
            Keys = keys;
            AuthorizedCallerIdentities = callers;
            RolePermissions = rolePermissions.IsNull ? [] : [.. rolePermissions];
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
        public ArrayOf<PubSubSecurityKey> Keys { get; }

        /// <summary>
        /// Caller identities authorized to retrieve keys for this group.
        /// </summary>
        public ArrayOf<string> AuthorizedCallerIdentities { get; private init; }

        /// <summary>
        /// RolePermissions controlling GetSecurityKeys Call access.
        /// </summary>
        public ArrayOf<RolePermissionType> RolePermissions { get; private init; }

        /// <summary>
        /// Returns a copy of this group with the supplied caller authorized.
        /// </summary>
        /// <param name="callerIdentity">Authenticated caller identity.</param>
        /// <returns>Updated group configuration.</returns>
        public SksSecurityGroup WithAuthorizedCaller(string callerIdentity)
        {
            if (string.IsNullOrEmpty(callerIdentity))
            {
                throw new ArgumentException(
                    "Caller identity must be non-empty.",
                    nameof(callerIdentity));
            }

            if (IsCallerAuthorized(callerIdentity))
            {
                return this;
            }

            var callers = new List<string>(AuthorizedCallerIdentities.Count + 1);
            for (int i = 0; i < AuthorizedCallerIdentities.Count; i++)
            {
                callers.Add(AuthorizedCallerIdentities[i]);
            }
            callers.Add(callerIdentity);

            return this with
            {
                AuthorizedCallerIdentities = callers
            };
        }

        /// <summary>
        /// Determines whether a caller may retrieve keys for this group.
        /// </summary>
        /// <param name="callerIdentity">Authenticated caller identity.</param>
        /// <returns>
        /// <see langword="true"/> when RolePermissions grant Call or the caller is explicitly authorized.
        /// </returns>
        public bool IsCallerAuthorized(string callerIdentity)
        {
            if (string.IsNullOrEmpty(callerIdentity))
            {
                return false;
            }

            if (RolePermissionsGrantCall())
            {
                return true;
            }

            for (int i = 0; i < AuthorizedCallerIdentities.Count; i++)
            {
                if (string.Equals(
                    AuthorizedCallerIdentities[i],
                    callerIdentity,
                    StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns a copy of this group with RolePermissions assigned.
        /// </summary>
        /// <param name="rolePermissions">RolePermissions to apply.</param>
        /// <returns>Updated group configuration.</returns>
        public SksSecurityGroup WithRolePermissions(ArrayOf<RolePermissionType> rolePermissions)
        {
            return this with
            {
                RolePermissions = rolePermissions.IsNull ? [] : [.. rolePermissions]
            };
        }

        private bool RolePermissionsGrantCall()
        {
            for (int i = 0; i < RolePermissions.Count; i++)
            {
                RolePermissionType permission = RolePermissions[i];
                if ((permission.Permissions & (uint)PermissionType.Call) == 0)
                {
                    continue;
                }
                if (permission.RoleId == ObjectIds.WellKnownRole_AuthenticatedUser ||
                    permission.RoleId == ObjectIds.WellKnownRole_Anonymous)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ContainsCaller(List<string> callers, string callerIdentity)
        {
            for (int i = 0; i < callers.Count; i++)
            {
                if (string.Equals(callers[i], callerIdentity, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
