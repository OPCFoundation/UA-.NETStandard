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
