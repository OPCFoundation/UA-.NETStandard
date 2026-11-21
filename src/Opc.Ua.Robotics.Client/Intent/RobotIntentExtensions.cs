// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.Robotics.Client.Intent;

namespace Opc.Ua.Robotics.Client
{
    /// <summary>
    /// Robot Intent entry points on RoboticsClient.
    /// </summary>
    public sealed partial class RoboticsClient
    {
        /// <summary>
        /// Opens Robot Intent discovery.
        /// </summary>
        public RobotIntentClient RobotIntent(IStreamingSubscription? streaming = null)
        {
            return new RobotIntentClient(Session, Telemetry, streaming);
        }

        /// <summary>
        /// Opens a high-level Robot Intent controller client.
        /// </summary>
        public RobotIntentControllerClient RobotIntentController(
            NodeId controllerId,
            IStreamingSubscription? streaming = null)
        {
            return RobotIntent(streaming).Controller(controllerId);
        }
    }

    /// <summary>
    /// Robot Intent entry points on ISession.
    /// </summary>
    public static class SessionRobotIntentExtensions
    {
        /// <summary>
        /// Creates a Robot Intent discovery client over the supplied session.
        /// </summary>
        public static RobotIntentClient RobotIntent(
            this ISession session,
            ITelemetryContext telemetry,
            IStreamingSubscription? streaming = null)
        {
            if (session is null)
            {
                throw new ArgumentNullException(nameof(session));
            }
            if (telemetry is null)
            {
                throw new ArgumentNullException(nameof(telemetry));
            }
            return new RobotIntentClient(session, telemetry, streaming);
        }
    }
}
