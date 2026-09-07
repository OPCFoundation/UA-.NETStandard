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
