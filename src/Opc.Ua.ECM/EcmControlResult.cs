// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.ECM
{
    /// <summary>
    /// An OPC 34100 control result, retaining Call status independently of the device return code.
    /// </summary>
    public sealed record EcmControlResult
    {
        /// <summary>
        /// Creates a result. Bad Call statuses must not supply device outputs.
        /// All durations are in milliseconds.
        /// </summary>
        public EcmControlResult(
            StatusCode status,
            byte? returnCode,
            byte modeId = 0,
            double currentTimeToDestination = 0,
            double regularTimeToOperate = 0,
            double timeMinLengthOfStay = 0,
            double currentTimeToOperate = 0)
        {
            if (StatusCode.IsBad(status))
            {
                if (returnCode.HasValue || modeId != 0 || currentTimeToDestination != 0 ||
                    regularTimeToOperate != 0 || timeMinLengthOfStay != 0 || currentTimeToOperate != 0)
                {
                    throw new ArgumentException("Bad Call status has no interpretable device outputs.");
                }
            }
            else if (!returnCode.HasValue ||
                (StatusCode.IsGood(status) && returnCode != 0) ||
                (StatusCode.IsUncertain(status) && returnCode == 0))
            {
                throw new ArgumentException("Call status and device return code disagree.", nameof(returnCode));
            }

            ValidateDuration(currentTimeToDestination, nameof(currentTimeToDestination));
            ValidateDuration(regularTimeToOperate, nameof(regularTimeToOperate));
            ValidateDuration(timeMinLengthOfStay, nameof(timeMinLengthOfStay));
            ValidateDuration(currentTimeToOperate, nameof(currentTimeToOperate));
            if (StatusCode.IsUncertain(status) && (currentTimeToDestination != 0 ||
                regularTimeToOperate != 0 || timeMinLengthOfStay != 0 || currentTimeToOperate != 0))
            {
                throw new ArgumentException("Rejected device commands require zero timing outputs.");
            }
            Status = status;
            ReturnCode = returnCode;
            ModeId = modeId;
            CurrentTimeToDestination = currentTimeToDestination;
            RegularTimeToOperate = regularTimeToOperate;
            TimeMinLengthOfStay = timeMinLengthOfStay;
            CurrentTimeToOperate = currentTimeToOperate;
        }

        /// <summary>
        /// Gets the OPC UA Call status.
        /// </summary>
        public StatusCode Status { get; }

        /// <summary>
        /// Gets the device ReturnCode, or null when the Call status is bad.
        /// </summary>
        public byte? ReturnCode { get; }

        /// <summary>
        /// Gets the selected or effective mode ID.
        /// </summary>
        public byte ModeId { get; }

        /// <summary>
        /// Gets the estimated remaining time to the destination in milliseconds.
        /// </summary>
        public double CurrentTimeToDestination { get; }

        /// <summary>
        /// Gets the regular return-to-operation duration in milliseconds.
        /// </summary>
        public double RegularTimeToOperate { get; }

        /// <summary>
        /// Gets the minimum destination stay in milliseconds.
        /// </summary>
        public double TimeMinLengthOfStay { get; }

        /// <summary>
        /// Gets the EndPause return-to-operation estimate in milliseconds.
        /// </summary>
        public double CurrentTimeToOperate { get; }

        /// <summary>
        /// Rejects negative or nonfinite durations.
        /// </summary>
        internal static void ValidateDuration(double value, string parameter)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < 0)
            {
                throw new ArgumentOutOfRangeException(parameter);
            }
        }
    }
}
