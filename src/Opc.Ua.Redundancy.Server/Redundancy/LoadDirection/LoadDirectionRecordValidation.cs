// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Fail-closed checks for records exchanged by load-directed discovery.
    /// </summary>
    internal static class LoadDirectionRecordValidation
    {
        /// <summary>
        /// Rejects the no-op protector unless the store is process-local.
        /// </summary>
        public static IRecordProtector RequireProtection(ISharedKeyValueStore store, IRecordProtector protector)
        {
            if (protector == null)
            {
                throw new ArgumentNullException(nameof(protector));
            }

            if (store is not InMemorySharedKeyValueStore && protector is NullRecordProtector)
            {
                throw new InvalidOperationException(
                    "Load direction requires an authenticated IRecordProtector for a nonlocal shared store. " +
                    "NullRecordProtector is only supported with InMemorySharedKeyValueStore.");
            }

            return protector;
        }

        /// <summary>
        /// Checks that a record was issued within the configured age window and not in the future.
        /// </summary>
        public static bool IsCurrent(long recordTicks, long nowTicks, TimeSpan maximumAge)
        {
            return recordTicks >= 0 && recordTicks <= nowTicks && nowTicks - recordTicks <= maximumAge.Ticks;
        }

        /// <summary>
        /// Takes an ordinal, deduplicated snapshot of the configured members.
        /// </summary>
        public static HashSet<string> SnapshotMembers(ArrayOf<string> peerApplicationUris)
        {
            var members = new HashSet<string>(StringComparer.Ordinal);
            foreach (string uri in peerApplicationUris)
            {
                if (!string.IsNullOrWhiteSpace(uri))
                {
                    members.Add(uri);
                }
            }

            return members;
        }

        /// <summary>
        /// Checks that all endpoint descriptions belong to the same expected member.
        /// </summary>
        public static bool MatchesMember(ArrayOf<EndpointDescription> endpoints, string serverUri)
        {
            foreach (EndpointDescription endpoint in endpoints)
            {
                if (!string.Equals(endpoint?.Server?.ApplicationUri, serverUri, StringComparison.Ordinal))
                {
                    return false;
                }
            }

            return endpoints.Count != 0;
        }
    }
}
