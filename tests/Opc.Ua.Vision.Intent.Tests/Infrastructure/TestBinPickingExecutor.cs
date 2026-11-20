// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.RobotIntent;

namespace Opc.Ua.Vision.Intent.Tests.Infrastructure
{
    /// <summary>
    /// Deterministic executor used by the pick-and-place loop test.
    /// Interprets a small subset of the intent grammar:
    /// <list type="bullet">
    ///   <item>
    ///     <c>GraspIntent</c> with an IntentId starting with
    ///     <c>grasp-&lt;classLabel&gt;-</c> marks that part as Held on
    ///     the shared <see cref="TestBinWorld"/>.
    ///   </item>
    ///   <item>
    ///     <c>LinearMoveIntent</c> with an IntentId starting with
    ///     <c>place-&lt;classLabel&gt;-</c> marks that part as Placed
    ///     at the intent's <c>Target</c> position.
    ///   </item>
    ///   <item>
    ///     Every other intent succeeds without side effects.
    ///   </item>
    /// </list>
    /// The executor does not sleep or spin — it returns immediately
    /// so the loop test can drive the state machine at the speed of a
    /// unit test.
    /// </summary>
    internal sealed class TestBinPickingExecutor : IIntentExecutor
    {
        public TestBinPickingExecutor(TestBinWorld world)
        {
            m_world = world ?? throw new ArgumentNullException(nameof(world));
        }

        public bool CanCancel(IntentExecution execution)
        {
            return true;
        }

        public ValueTask<IntentOutcome> ExecuteAsync(
            IntentExecution execution, CancellationToken cancellationToken)
        {
            if (execution == null)
            {
                throw new ArgumentNullException(nameof(execution));
            }
            string intentId = execution.Intent.IntentId ?? string.Empty;
            execution.Progress.ReportProgress(1.0);

            if (execution.Intent is GraspIntentDataType &&
                TryExtractClassLabel(intentId, "grasp-", out string graspLabel))
            {
                m_world.MarkHeld(graspLabel);
                return new ValueTask<IntentOutcome>(IntentOutcome.Success);
            }

            if (execution.Intent is LinearMoveIntentDataType linear &&
                TryExtractClassLabel(intentId, "place-", out string placeLabel))
            {
                Pose3DDataType target = linear.Target;
                (double x, double y, double z) = ReadPosition(target);
                m_world.MarkPlaced(placeLabel, x, y, z);
                return new ValueTask<IntentOutcome>(IntentOutcome.SucceededAt(target));
            }

            return new ValueTask<IntentOutcome>(IntentOutcome.Success);
        }

        private static (double X, double Y, double Z) ReadPosition(Pose3DDataType pose)
        {
            if (pose == null || pose.Position.Count < 3)
            {
                return (0.0, 0.0, 0.0);
            }
            return (pose.Position[0], pose.Position[1], pose.Position[2]);
        }

        private static bool TryExtractClassLabel(string intentId, string prefix, out string classLabel)
        {
            classLabel = string.Empty;
            if (string.IsNullOrEmpty(intentId) || !intentId.StartsWith(prefix, StringComparison.Ordinal))
            {
                return false;
            }
            int start = prefix.Length;
            int end = intentId.IndexOf('-', start, StringComparison.Ordinal);
            if (end < 0)
            {
                end = intentId.Length;
            }
            classLabel = intentId[start..end];
            return classLabel.Length > 0;
        }

        private readonly TestBinWorld m_world;
    }
}
