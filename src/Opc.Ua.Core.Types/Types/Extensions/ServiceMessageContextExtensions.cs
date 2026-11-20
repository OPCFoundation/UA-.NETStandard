/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua
{
    /// <summary>
    /// Service message context extensions
    /// </summary>
    public static class ServiceMessageContextExtensions
    {
        extension(ServiceMessageContext)
        {
            /// <summary>
            /// Create a new service message context initialized with all known types.
            /// </summary>
            /// <returns></returns>
            public static ServiceMessageContext Create(ITelemetryContext? telemetry)
            {
                return new ServiceMessageContext(telemetry!, EncodeableFactory.Create()); // ctor accepts non-null but legacy callers may pass null
            }
        }
    }
}
