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
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.RobotIntent
{
    /// <summary>
    /// Reports the progress of an executing intent back to the address space.
    /// </summary>
    /// <remarks>
    /// Everything here is a status report published at whatever rate a client's
    /// Subscription asks for. OPC UA is not a real-time control channel, and the
    /// specification excludes servo-level use as a normative limit rather than a
    /// caution; see OPC UA - Robot Intent clause 4.3.
    /// </remarks>
    public interface IIntentProgress
    {
        /// <summary>
        /// Reports the fraction of the intent completed, in the range 0 to 1.
        /// A negative value states that the Server cannot estimate it.
        /// </summary>
        void ReportProgress(double fraction);

        /// <summary>
        /// Reports where the driven tool centre point is now.
        /// </summary>
        void ReportPose(Pose3DDataType pose);
    }

    /// <summary>
    /// What an executor is given when it is asked to carry out one intent.
    /// </summary>
    public sealed class IntentExecution
    {
        /// <summary>
        /// Creates an execution context.
        /// </summary>
        public IntentExecution(string intentId, IntentDataType intent, IIntentProgress progress)
        {
            IntentId = intentId ?? throw new ArgumentNullException(nameof(intentId));
            Intent = intent ?? throw new ArgumentNullException(nameof(intent));
            Progress = progress ?? throw new ArgumentNullException(nameof(progress));
        }

        /// <summary>
        /// The identifier the intent was admitted under.
        /// </summary>
        public string IntentId { get; }

        /// <summary>
        /// The intent as admitted, after the Server applied its defaults.
        /// </summary>
        public IntentDataType Intent { get; }

        /// <summary>
        /// Progress and pose reporting for this execution.
        /// </summary>
        public IIntentProgress Progress { get; }

        /// <summary>
        /// The mission this intent belongs to, or an empty string when it was
        /// submitted on its own.
        /// </summary>
        public string MissionId { get; init; } = string.Empty;
    }

    /// <summary>
    /// How an intent ended.
    /// </summary>
    /// <remarks>
    /// The failure is deliberately a small, diagnosable set: a client decides whether
    /// to retry, re-plan or escalate from that value alone, and reads the message only
    /// to show a human.
    /// </remarks>
    public sealed record IntentOutcome
    {
        /// <summary>
        /// The terminal state reached. Only Succeeded, Failed, Cancelled and Retriable
        /// are terminal.
        /// </summary>
        public ExecutionStateEnum State { get; init; } = ExecutionStateEnum.Succeeded;

        /// <summary>
        /// Why it did not succeed, or None.
        /// </summary>
        public IntentFailureEnum Failure { get; init; } = IntentFailureEnum.None;

        /// <summary>
        /// Human-readable detail. Never parsed.
        /// </summary>
        public string? Message { get; init; }

        /// <summary>
        /// Where the driven tool centre point came to rest, or was when blending
        /// began. Null when the intent moved nothing.
        /// </summary>
        public Pose3DDataType? AchievedPose { get; init; }

        /// <summary>
        /// Named results the intent produced, for example the identity of a picked
        /// object.
        /// </summary>
        public ArrayOf<KeyValuePair> Outputs { get; init; }

        /// <summary>
        /// A successful outcome that moved nothing.
        /// </summary>
        public static IntentOutcome Success { get; } = new();

        /// <summary>
        /// A successful outcome that came to rest at the given pose.
        /// </summary>
        public static IntentOutcome SucceededAt(Pose3DDataType pose)
        {
            return new IntentOutcome { AchievedPose = pose };
        }

        /// <summary>
        /// A failed outcome.
        /// </summary>
        public static IntentOutcome Fail(IntentFailureEnum failure, string? message = null)
        {
            return new IntentOutcome
            {
                State = ExecutionStateEnum.Failed,
                Failure = failure,
                Message = message
            };
        }

        /// <summary>
        /// A failed outcome the Server is willing to re-attempt on Retry.
        /// </summary>
        public static IntentOutcome Retriable(IntentFailureEnum failure, string? message = null)
        {
            return new IntentOutcome
            {
                State = ExecutionStateEnum.Retriable,
                Failure = failure,
                Message = message
            };
        }

        /// <summary>
        /// Gets a value indicating whether a state is terminal.
        /// </summary>
        public static bool IsTerminal(ExecutionStateEnum state)
        {
            return state is ExecutionStateEnum.Succeeded
                or ExecutionStateEnum.Failed
                or ExecutionStateEnum.Cancelled
                or ExecutionStateEnum.Retriable;
        }
    }

    /// <summary>
    /// Carries out intents on the robot.
    /// </summary>
    /// <remarks>
    /// The host owns admission, queueing, the state machine, cancellation and the
    /// result; an implementation of this interface owns only the doing. Translating an
    /// intent into whatever the controller actually executes - URScript, RAPID, KRL, a
    /// TP program - is the whole of its job.
    /// </remarks>
    public interface IIntentExecutor
    {
        /// <summary>
        /// Executes one intent.
        /// </summary>
        /// <remarks>
        /// The cancellation token is signalled when a cancel has been ACCEPTED, which
        /// is the point at which the operation enters Cancelling. An implementation
        /// brings motion to a controlled end and then returns; it need not return
        /// Cancelled, because the host records the cancellation itself.
        /// </remarks>
        ValueTask<IntentOutcome> ExecuteAsync(
            IntentExecution execution,
            CancellationToken cancellationToken);

        /// <summary>
        /// Decides whether a cancel may be accepted for an intent that is executing.
        /// </summary>
        /// <remarks>
        /// Some motions cannot be abandoned part-way without leaving the cell in a
        /// worse state than completing them - a tool change mid-exchange, a placement
        /// mid-release. Returning false refuses this one occasion; declaring
        /// CancelSupported false in the capability refuses the whole intent type in
        /// advance.
        /// <para>
        /// This has no default implementation because the library targets .NET
        /// Framework, which has none, and because whether a motion can be safely
        /// abandoned is a decision worth making deliberately. An executor that has no
        /// such motions returns true.
        /// </para>
        /// </remarks>
        bool CanCancel(IntentExecution execution);
    }
}
