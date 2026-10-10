/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using Microsoft.Extensions.Logging;

namespace Opc.Ua
{
    /// <summary>
    /// Centrally managed event id block offsets for the EndpointRegistry.PubSub assembly.
    /// Each block reserves five spare ids and rounds up to a multiple of ten.
    /// </summary>
    internal static class EndpointRegistryPubSubEventIds
    {
        /// <summary>The PubSub binding log block, covering ids 0 through 9.</summary>
        public const int PubSubBinding = 0;
    }
}

namespace Opc.Ua.EndpointRegistry.PubSub
{
    /// <summary>
    /// Source-generated logging for PubSub registry binding lifecycle failures.
    /// </summary>
    internal static partial class PubSubBindingLog
    {
        /// <summary>
        /// Reports a native source that cannot be surfaced.
        /// </summary>
        [LoggerMessage(EventId = EndpointRegistryPubSubEventIds.PubSubBinding + 0, Level = LogLevel.Warning,
            Message = "PubSub source {Source} cannot be surfaced from its native configuration.")]
        public static partial void SourceNotSurfaced(this ILogger logger, string source, Exception error);

        /// <summary>
        /// Reports a failed metadata revalidation or expiry sweep.
        /// </summary>
        [LoggerMessage(EventId = EndpointRegistryPubSubEventIds.PubSubBinding + 1, Level = LogLevel.Error,
            Message = "A PubSub binding metadata revalidation or expiry check failed; existing associations remain invalidated.")]
        public static partial void BackgroundRefreshFailed(this ILogger logger, Exception error);
    }
}
