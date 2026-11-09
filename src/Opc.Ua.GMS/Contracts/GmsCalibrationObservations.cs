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

namespace Opc.Ua.GMS.Contracts
{
    public sealed partial record GmsSystemSnapshot
    {
        /// <summary>
        /// Gets the optional provider-reported machine calibration, without executing a calibration procedure.
        /// </summary>
        public GmsCalibrationObservation? Calibration { get; init; }
    }

    /// <summary>
    /// Native CalibrationPrognosisType observations. Every time and certificate reference comes from the provider.
    /// </summary>
    public sealed record GmsCalibrationObservation
    {
        /// <summary>
        /// Gets the required provider-predicted next calibration time, not a server-computed deadline.
        /// </summary>
        public DataValue PredictedTime { get; init; } = new(Variant.Null, StatusCodes.BadWaitingForInitialData);

        /// <summary>
        /// Gets the mandatory Boolean calibrated-state observation.
        /// </summary>
        public DataValue Calibrated { get; init; } = new(Variant.Null, StatusCodes.BadWaitingForInitialData);

        /// <summary>
        /// Gets the required date of the last actual calibration.
        /// </summary>
        public DataValue DateOfCalibration { get; init; } = new(Variant.Null, StatusCodes.BadWaitingForInitialData);

        /// <summary>
        /// Gets the optional positive calibration interval as native Duration milliseconds.
        /// </summary>
        public DataValue CalibrationInterval { get; init; }

        /// <summary>
        /// Gets the optional nonnegative preparation lead time as native Duration milliseconds.
        /// </summary>
        public DataValue CalibrationPreptime { get; init; }

        /// <summary>
        /// Gets optional references to all available calibration certificates, not certificate bytes or secrets.
        /// </summary>
        public DataValue CalibrationCertificate { get; init; }
    }
}
