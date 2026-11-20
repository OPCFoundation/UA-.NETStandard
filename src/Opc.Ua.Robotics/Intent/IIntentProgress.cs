// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
