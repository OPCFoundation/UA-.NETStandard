// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua.Client;

namespace Opc.Ua.AI.Client
{
    public static class SessionAIExtensions
    {
        public static AIClient AI(this ISession session, ITelemetryContext telemetry)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            return new AIClient(session, telemetry);
        }
    }
}
