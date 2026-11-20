// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.RobotIntent
{
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
}
