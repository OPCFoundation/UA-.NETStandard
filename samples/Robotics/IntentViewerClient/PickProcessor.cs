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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua;
using Opc.Ua.Client;
using Opc.Ua.Robotics.Client.Intent;
using Opc.Ua.RobotIntent;

namespace IntentViewerClient
{
    internal sealed partial class PickProcessor : IDisposable
    {
        public PickProcessor(
            RobotIntentControllerClient controller,
            ISession session,
            IReadOnlyList<TargetLocation> targets,
            ILogger logger,
            bool canCommand)
        {
            m_controller = controller;
            m_session = session;
            m_targets = targets;
            m_logger = logger;
            m_canCommand = canCommand;
        }

        public async Task ProcessPickAsync(string primPath, CancellationToken cancellationToken)
        {
            await m_pickGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ProcessPickCoreAsync(primPath, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_pickGate.Release();
            }
        }

        public void Dispose()
        {
            m_pickGate.Dispose();
        }

        private async Task ProcessPickCoreAsync(string primPath, CancellationToken cancellationToken)
        {
            TargetLocation? target = m_targets.FirstOrDefault(
                t => string.Equals(t.PrimPath, primPath, StringComparison.Ordinal));
            if (target is null)
            {
                Console.Error.WriteLine($"Picked {primPath}, but the server did not publish a LocationType mapping for it.");
                return;
            }

            Console.Error.WriteLine($"Picked {target.Name} at {target.PrimPath}; reading {target.LocationNodeId} Pose.");
            Pose3DDataType pose;
            try
            {
                pose = await Program.ReadLocationPoseForProcessorAsync(
                    m_session, target.LocationNodeId, cancellationToken).ConfigureAwait(false);
            }
            catch (ServiceResultException exception) when (exception.StatusCode == StatusCodes.BadUnexpectedError)
            {
                Console.Error.WriteLine(
                    $"Location {target.Name} ({target.LocationNodeId}) published a Pose value this " +
                    "client could not decode; skipping that target.");
                return;
            }
            if (!m_canCommand)
            {
                Console.Error.WriteLine(
                    "Read-only mode: the client discovered and decoded the target, but did not submit an " +
                    "intent because command authority was not granted.");
                return;
            }
            LinearMoveIntentDataType intent = RobotIntentBuilder
                .LinearMove(pose, 0.35).CartesianSpeed(0.20).Exact().Build();
            IntentSubmissionResult submission = await m_controller.TrySubmitIntentAsync(intent, cancellationToken)
                .ConfigureAwait(false);
            if (!submission.Accepted)
            {
                PrintRefusal(submission.Failure, submission.Message);
                LogPickRefused(m_logger, target.PrimPath, submission.Failure, submission.Message.Text ?? string.Empty);
                return;
            }

            Console.Error.WriteLine($"Intent admitted: {submission.IntentId}; operation {submission.Operation}.");
            LogPickSubmitted(m_logger, target.PrimPath, submission.IntentId);
            IntentOperationHandle handle = await m_controller.TrackOperationAsync(
                submission.IntentId, submission.Operation, cancellationToken).ConfigureAwait(false);
            await using (handle.ConfigureAwait(false))
            {
                handle.Changed += PrintSnapshot;
                using var cancelWatchCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                Task cancelTask = WatchForCancelKeyAsync(handle, cancelWatchCts.Token);
                IntentResultDataType result = await handle.Completion
                    .WaitAsync(cancellationToken).ConfigureAwait(false);
                await cancelWatchCts.CancelAsync().ConfigureAwait(false);
                await ObserveCancelWatchAsync(cancelTask).ConfigureAwait(false);
                Console.Error.WriteLine("Operation terminal result:");
                PrintResult(result);
                LogPickCompleted(m_logger, submission.IntentId, handle.Current.ExecutionState);
            }
        }

        private static async Task ObserveCancelWatchAsync(Task task)
        {
            try
            {
                await task.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        private static async Task WatchForCancelKeyAsync(
            IntentOperationHandle handle,
            CancellationToken cancellationToken)
        {
            if (Console.IsInputRedirected)
            {
                return;
            }
            Console.Error.WriteLine("Press C while the robot is moving to request CancelIntent.");
            while (!cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                if (!Console.KeyAvailable)
                {
                    continue;
                }
                ConsoleKeyInfo key = Console.ReadKey(intercept: true);
                if (key.Key != ConsoleKey.C)
                {
                    continue;
                }
                IntentCommandOutcome outcome = await handle.CancelAsync(StopModeEnum.ProcessStop, cancellationToken)
                    .ConfigureAwait(false);
                if (outcome.Accepted)
                {
                    Console.Error.WriteLine(
                        "CancelIntent accepted. Waiting for ExecutionState=Cancelled; " +
                        "Cancelling is not terminal motion.");
                }
                else
                {
                    Console.Error.WriteLine("CancelIntent refused by the server; continuing to observe the operation.");
                }
                return;
            }
        }

        private static void PrintSnapshot(IntentOperationSnapshot snapshot)
        {
            string pose = FormatPose(snapshot.CurrentPose);
            Console.Error.WriteLine(string.Format(
                CultureInfo.InvariantCulture,
                "State={0}; progress={1:0.0}%; pose={2}",
                snapshot.ExecutionState,
                snapshot.Progress,
                pose));
            if (snapshot.ExecutionState == ExecutionStateEnum.Cancelling)
            {
                Console.Error.WriteLine("ExecutionState=Cancelling is still in motion; waiting for Cancelled.");
            }
        }

        private static void PrintResult(IntentResultDataType result)
        {
            Console.Error.WriteLine($"  Failure: {result.Failure}");
            Console.Error.WriteLine($"  Message: {result.Message.Text}");
            if (result.HasAchievedPose)
            {
                Console.Error.WriteLine($"  AchievedPose: {FormatPose(result.AchievedPose)}");
            }
        }

        private static void PrintRefusal(IntentFailureEnum failure, LocalizedText message)
        {
            Console.Error.WriteLine($"Intent refused: {failure} - {message.Text}");
            string action = failure switch
            {
                IntentFailureEnum.SafetyStop or IntentFailureEnum.SafetyLimitExceeded =>
                    "Safety system refused the motion; this client observed that decision and will not override it.",
                IntentFailureEnum.ControlNotOwned =>
                    "Retry after obtaining command authority or ask the current operator to release control.",
                IntentFailureEnum.CapabilityNotSupported =>
                    "Re-plan with an intent the controller capability declaration accepts.",
                IntentFailureEnum.ParameterInvalid =>
                    "Re-plan with slower or less restrictive motion constraints.",
                IntentFailureEnum.QueueFull =>
                    "Retry after the controller drains its queue.",
                _ =>
                    "Escalate to the operator with the server's refusal message."
            };
            Console.Error.WriteLine($"Decision: {action}");
        }

        private static string FormatPose(Pose3DDataType pose)
        {
            string frame = string.IsNullOrEmpty(pose.FrameId) ? "<default>" : pose.FrameId;
            if (pose.Position.Count < 3)
            {
                return $"frame={frame}; position=<unavailable>";
            }
            return FormattableString.Invariant(
                $"frame={frame}; x={pose.Position[0]:0.###}, y={pose.Position[1]:0.###}, z={pose.Position[2]:0.###}");
        }

        [LoggerMessage(EventId = IntentViewerClientEventIds.PickSubmitted, Level = LogLevel.Information,
            Message = "Submitted pick {PrimPath} as intent {IntentId}.")]
        private static partial void LogPickSubmitted(ILogger logger, string primPath, string intentId);

        [LoggerMessage(EventId = IntentViewerClientEventIds.PickCompleted, Level = LogLevel.Information,
            Message = "Intent {IntentId} completed in state {ExecutionState}.")]
        private static partial void LogPickCompleted(
            ILogger logger,
            string intentId,
            ExecutionStateEnum executionState);

        [LoggerMessage(EventId = IntentViewerClientEventIds.PickRefused, Level = LogLevel.Warning,
            Message = "Pick {PrimPath} was refused with {Failure}: {Message}.")]
        private static partial void LogPickRefused(
            ILogger logger,
            string primPath,
            IntentFailureEnum failure,
            string message);

        private readonly RobotIntentControllerClient m_controller;
        private readonly ISession m_session;
        private readonly IReadOnlyList<TargetLocation> m_targets;
        private readonly ILogger m_logger;
        private readonly bool m_canCommand;
        private readonly SemaphoreSlim m_pickGate = new(1, 1);
    }
}
