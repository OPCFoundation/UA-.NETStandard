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
