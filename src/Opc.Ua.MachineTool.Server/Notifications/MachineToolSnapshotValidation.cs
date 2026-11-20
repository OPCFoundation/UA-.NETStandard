/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace Opc.Ua.MachineTool.Server
{
    /// <summary>
    /// Validates mandatory observations independently of the generated machine subtype.
    /// </summary>
    internal static class MachineToolSnapshotValidation
    {
        /// <summary>
        /// Validates the complete mandatory observation before allocating or changing any machine.
        /// </summary>
        /// <exception cref="ServiceResultException">The observation is absent or invalid.</exception>
        internal static void Validate(MachineToolSnapshot snapshot)
        {
            if (snapshot == null)
            {
                throw new ServiceResultException(StatusCodes.BadNoData, "The equipment provider returned no snapshot.");
            }
            if (snapshot.Operation == null ||
                snapshot.ActiveProgram == null ||
                (int)snapshot.Operation.Mode is < 0 or > 5 ||
                (int)snapshot.ActiveProgram.Phase is < 0 or > 4 ||
                snapshot.ActiveProgram.Name == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument, "Invalid mandatory MachineTool observation.");
            }
            ValidateSample(snapshot.Operation.Sample);
            ValidateSample(snapshot.ActiveProgram.Sample);
        }

        /// <summary>
        /// Rejects usable measurements without an equipment acquisition time.
        /// </summary>
        /// <exception cref="ServiceResultException">A usable sample has no acquisition time.</exception>
        internal static void ValidateSample(MachineToolSampleInfo sample)
        {
            if (!StatusCode.IsBad(sample.StatusCode) && sample.SourceTimestamp == default)
            {
                throw new ServiceResultException(
                    StatusCodes.BadInvalidArgument, "A usable sample needs an acquisition time.");
            }
        }
    }
}
