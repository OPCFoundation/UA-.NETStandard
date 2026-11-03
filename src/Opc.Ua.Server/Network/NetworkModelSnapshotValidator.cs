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

namespace Opc.Ua.Server.Network
{
    /// <summary>
    /// Contains snapshot validation shared by provider implementations and the server publication boundary.
    /// </summary>
    internal static class NetworkModelSnapshotValidator
    {
        /// <summary>
        /// Determines whether a TSN StreamId contains the eight bytes required by Part 22.
        /// </summary>
        /// <param name="streamId">The stream identity to validate.</param>
        /// <returns>True when the identity contains exactly eight bytes.</returns>
        internal static bool IsValidStreamId(ByteString streamId)
        {
            return streamId.Length == 8;
        }

        /// <summary>
        /// Determines whether an optional TSN stream reservation class identifier is in the defined range.
        /// </summary>
        /// <param name="srClassId">The optional stream reservation class identifier.</param>
        /// <returns>True when the identifier is absent or between zero and seven.</returns>
        internal static bool IsValidSrClassId(byte? srClassId)
        {
            return !srClassId.HasValue || srClassId.Value <= 7;
        }

        /// <summary>
        /// Determines whether the optional stream MAC addresses have valid lengths and dependencies.
        /// </summary>
        /// <param name="sourceAddress">The optional stream source address.</param>
        /// <param name="destinationAddress">The optional stream destination address.</param>
        /// <returns>True when both values are absent or valid and a source has a destination.</returns>
        internal static bool AreValidStreamMacAddresses(ByteString sourceAddress, ByteString destinationAddress)
        {
            return (sourceAddress.Length is 0 or 6) &&
                (destinationAddress.Length is 0 or 6) &&
                (sourceAddress.Length == 0 || destinationAddress.Length == 6);
        }

        /// <summary>
        /// Determines whether each failure-system identifier contains the eight bytes required by Part 22.
        /// </summary>
        /// <param name="identifiers">The failure-system identifiers to validate.</param>
        /// <returns>True when every identifier contains exactly eight bytes.</returns>
        internal static bool AreValidFailureSystemIdentifiers(ArrayOf<ByteString> identifiers)
        {
            foreach (ByteString identifier in identifiers)
            {
                if (identifier.Length != 8)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Determines whether an optional traffic specification has a non-zero interval denominator.
        /// </summary>
        /// <param name="traffic">The traffic specification to validate.</param>
        /// <returns>True when the interval denominator is non-zero.</returns>
        internal static bool IsValidTraffic(NetworkTrafficSnapshot traffic)
        {
            return traffic.Denominator != 0;
        }
    }
}
