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

        public bool CanCancel(IntentExecution execution) => true;

        public ValueTask<IntentOutcome> ExecuteAsync(
            IntentExecution execution, CancellationToken cancellationToken)
        {
            if (execution == null)
            {
                throw new ArgumentNullException(nameof(execution));
            }
            string intentId = execution.Intent.IntentId ?? string.Empty;
            execution.Progress.ReportProgress(1.0);

            if (execution.Intent is GraspIntentDataType grasp &&
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
            int end = intentId.IndexOf('-', start);
            if (end < 0)
            {
                end = intentId.Length;
            }
            classLabel = intentId.Substring(start, end - start);
            return classLabel.Length > 0;
        }

        private readonly TestBinWorld m_world;
    }
}
