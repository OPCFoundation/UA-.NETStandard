// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

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
