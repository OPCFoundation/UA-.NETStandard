/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
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
