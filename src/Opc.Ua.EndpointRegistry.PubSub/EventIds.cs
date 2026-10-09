/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * SPDX-License-Identifier: MIT
 * http://opcfoundation.org/License/MIT/1.00/
 * ======================================================================*/

using System;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.EndpointRegistry.PubSub
{
    internal static class EventIds
    {
        public const int SourceNotSurfaced = 0;
        public const int BackgroundRefreshFailed = 1;
    }

    internal static partial class PubSubBindingLog
    {
        [LoggerMessage(EventId = EventIds.SourceNotSurfaced, Level = LogLevel.Warning,
            Message = "PubSub source {Source} cannot be surfaced from its native configuration.")]
        public static partial void SourceNotSurfaced(this ILogger logger, string source, Exception error);

        [LoggerMessage(EventId = EventIds.BackgroundRefreshFailed, Level = LogLevel.Error,
            Message = "A PubSub binding metadata revalidation or expiry check failed; existing associations remain invalidated.")]
        public static partial void BackgroundRefreshFailed(this ILogger logger, Exception error);
    }
}
