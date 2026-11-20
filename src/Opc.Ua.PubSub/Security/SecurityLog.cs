// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Microsoft.Extensions.Logging;

namespace Opc.Ua.PubSub.Security
{
    /// <summary>
    /// Shared source-generated log messages for PubSub security components.
    /// </summary>
    internal static partial class SecurityLog
    {
        [LoggerMessage(EventId = PubSubEventIds.Security + 0, Level = LogLevel.Debug,
            Message = "PubSub security event sink raised an exception.")]
        public static partial void PubSubSecurityEventSinkRaisedException(
            this ILogger logger,
            Exception exception);
    }
}
