/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using Opc.Ua.Client;

namespace Opc.Ua.Scales.Client
{
    /// <summary>
    /// Registers the OPC 40200 structures (<c>WeightType</c>,
    /// <c>PrintableWeightType</c>, the recipe structures) with a session's
    /// encodeable factory, so values read from a scale decode into the
    /// generated types.
    /// </summary>
    internal static class ScalesEncodeableRegistration
    {
        public static void Register(ISession session)
        {
            IServiceMessageContext messageContext = session.MessageContext ??
                throw new ArgumentException(
                    "The session must provide a message context.",
                    nameof(session));
            IEncodeableFactory factory = messageContext.Factory;
            if (factory.ContainsEncodeableType(DataTypeIds.WeightType))
            {
                return;
            }
            factory.Builder.AddOpcUaScales().Commit();
        }
    }

    /// <summary>
    /// Session extensions for OPC 40200 Scales.
    /// </summary>
    public static class SessionScalesExtensions
    {
        /// <summary>
        /// Creates a <see cref="ScalesClient"/> over the session.
        /// </summary>
        /// <param name="session">The connected session.</param>
        /// <param name="telemetry">The telemetry context.</param>
        public static ScalesClient Scales(this ISession session, ITelemetryContext telemetry)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            return new ScalesClient(session, telemetry);
        }
    }
}
