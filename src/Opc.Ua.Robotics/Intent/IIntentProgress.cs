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

        /// <summary>
        /// Reports observed trajectory deviation so the host can enforce clause 6.8 tolerances.
        /// </summary>
        /// <param name="pathPositionDeviation">The current path position deviation in metres.</param>
        /// <param name="goalPositionDeviation">The current goal position deviation in metres.</param>
        /// <param name="elapsedMilliseconds">Elapsed trajectory execution time in milliseconds.</param>
        /// <param name="final">Whether this is the final trajectory report.</param>
        void ReportTrajectoryDeviation(
            double pathPositionDeviation,
            double goalPositionDeviation,
            double elapsedMilliseconds,
            bool final);
    }

    /// <summary>
    /// Optional progress surface for executors that can report blend entry.
    /// </summary>
    public interface IIntentBlendProgress
    {
        /// <summary>
        /// Reports that execution reached the blend point for the current intent.
        /// </summary>
        /// <param name="pose">The tool centre point pose at the blend point.</param>
        void ReportBlendBegin(Pose3DDataType pose);
    }

    /// <summary>
    /// Optional progress helpers.
    /// </summary>
    public static class IntentProgressExtensions
    {
        /// <summary>
        /// Reports blend entry when the host supports blend-progress callbacks.
        /// </summary>
        /// <param name="progress">The progress sink.</param>
        /// <param name="pose">The tool centre point pose at the blend point.</param>
        public static void ReportBlendBegin(this IIntentProgress progress, Pose3DDataType pose)
        {
            if (progress is IIntentBlendProgress blendProgress)
            {
                blendProgress.ReportBlendBegin(pose);
            }
        }
    }
}
