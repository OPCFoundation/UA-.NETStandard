/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Describes whether a redundant client should fail over.
    /// </summary>
    public sealed class ServerFailoverDecision
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ServerFailoverDecision"/> class.
        /// </summary>
        public ServerFailoverDecision(
            bool isFailoverWarranted,
            DateTime retryAfter,
            string reason)
        {
            IsFailoverWarranted = isFailoverWarranted;
            RetryAfter = retryAfter;
            Reason = reason;
        }

        /// <summary>
        /// Gets a value indicating whether a failover is warranted.
        /// </summary>
        public bool IsFailoverWarranted { get; }

        /// <summary>
        /// Gets the time after which a deferred failover may be retried.
        /// </summary>
        public DateTime RetryAfter { get; }

        /// <summary>
        /// Gets the decision reason.
        /// </summary>
        public string Reason { get; }
    }
}
