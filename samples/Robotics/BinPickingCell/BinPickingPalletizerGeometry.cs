// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Vision.BinPickingCell
{
    /// <summary>
    /// Dimensions of the branch-stable palletizer used by the bin-picking cell.
    /// </summary>
    internal static class BinPickingPalletizerGeometry
    {
        public const string RobotBaseFrameId = "robot_base";
        public const int AxisCount = 4;
        public const double ShoulderHeightMetres = 0.280;
        public const double UpperArmLengthMetres = 0.480;
        public const double ForearmLengthMetres = 0.480;
        public const double FlangeToTcpMetres = 0.185;

        public const double MaximumReachMetres =
            UpperArmLengthMetres + ForearmLengthMetres;

        public const double BaseYawLimitRadians = 3.1415926535897931;
        public const double ShoulderMinimumRadians = -1.3962634015954636;
        public const double ShoulderMaximumRadians = 2.2689280275926285;
        public const double ElbowMinimumRadians = -2.6179938779914944;
        public const double ElbowMaximumRadians = 2.6179938779914944;
        public const double ToolRollLimitRadians = 3.1415926535897931;
    }
}
