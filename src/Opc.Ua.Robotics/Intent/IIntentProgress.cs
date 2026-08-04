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
}
