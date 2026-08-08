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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Client.FileSystem;
using Opc.Ua.Client.Subscriptions.Streaming;
using Opc.Ua.RobotIntent;

namespace Opc.Ua.Robotics.Client
{
    public sealed partial class RoboticsClient
    {
        /// <summary>
        /// Opens the standard SystemOperation client for a controller.
        /// </summary>
        public SystemOperationClient SystemOperation(NodeId controller)
        {
            return new SystemOperationClient(Session, controller, Telemetry, resolveFromController: true);
        }

        /// <summary>
        /// Opens the standard TaskControl client for a task control.
        /// </summary>
        public TaskControlClient TaskControl(NodeId taskControl)
        {
            return new TaskControlClient(Session, taskControl, Telemetry);
        }

        /// <summary>
        /// Opens the controller Programs directory as a FileSystemClient.
        /// </summary>
        public async Task<FileSystemClient> ProgramsAsync(
            NodeId controller,
            CancellationToken cancellationToken = default)
        {
            ControllerTypeClient proxy = new(Session, controller, Telemetry);
            FileDirectoryTypeClient? programs = await proxy.GetProgramsAsync(Telemetry, cancellationToken)
                .ConfigureAwait(false);
            if (programs == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadNotFound,
                    $"Controller '{controller}' does not expose Programs.");
            }
            return new FileSystemClient(Session, programs.ObjectId);
        }

        /// <summary>
        /// Opens the OPC UA - Robot Intent surface for a robot.
        /// </summary>
        /// <remarks>
        /// This is where task-level commanding lives. OPC 40010-1 describes the robot
        /// and defines no motion verbs; the intent controller supplies them, and the
        /// two are joined by a HasIntentController reference rather than by either
        /// model depending on the other.
        /// </remarks>
        /// <param name="intentController">The IntentController object.</param>
        public IntentControllerTypeClient IntentController(NodeId intentController)
        {
            return new IntentControllerTypeClient(Session, intentController, Telemetry);
        }

        internal static IStreamingSubscription GetDefaultStreaming(ISession session)
        {
            if (session is ManagedSession managedSession)
            {
                return managedSession.DefaultStreaming;
            }
            throw new ServiceResultException(
                StatusCodes.BadInvalidState,
                "Observation without an explicit IStreamingSubscription requires ManagedSession.");
        }
    }
}
