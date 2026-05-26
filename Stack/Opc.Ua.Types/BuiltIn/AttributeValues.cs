/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System.Collections.Generic;

namespace Opc.Ua
{
    /// <summary>
    /// Flags that can be set for the EventNotifier attribute.
    /// </summary>
    /// <remarks>
    /// Flags that can be set for the EventNotifier attribute.
    /// </remarks>
    public static class EventNotifiers
    {
        /// <summary>
        /// The Object or View produces no event and has no event history.
        /// </summary>
        public const byte None = 0x0;

        /// <summary>
        /// The Object or View produces event notifications.
        /// </summary>
        public const byte SubscribeToEvents = 0x1;

        /// <summary>
        /// The Object has an event history which may be read.
        /// </summary>
        public const byte HistoryRead = 0x4;

        /// <summary>
        /// The Object has an event history which may be updated.
        /// </summary>
        public const byte HistoryWrite = 0x8;
    }

    /// <summary>
    /// Flags that can be set for the AccessLevel attribute.
    /// </summary>
    /// <remarks>
    /// Flags that can be set for the AccessLevel attribute.
    /// </remarks>
    public static class AccessLevels
    {
        /// <summary>
        /// The Variable value cannot be accessed and has no event history.
        /// </summary>
        public const byte None = 0x0;

        /// <summary>
        /// The current value of the Variable may be read.
        /// </summary>
        public const byte CurrentRead = 0x1;

        /// <summary>
        /// The current value of the Variable may be written.
        /// </summary>
        public const byte CurrentWrite = 0x2;

        /// <summary>
        /// The current value of the Variable may be read or written.
        /// </summary>
        public const byte CurrentReadOrWrite = 0x3;

        /// <summary>
        /// The history for the Variable may be read.
        /// </summary>
        public const byte HistoryRead = 0x4;

        /// <summary>
        /// The history for the Variable may be updated.
        /// </summary>
        public const byte HistoryWrite = 0x8;

        /// <summary>
        /// The history value of the Variable may be read or updated.
        /// </summary>
        public const byte HistoryReadOrWrite = 0xC;

        /// <summary>
        /// Indicates if the Variable generates SemanticChangeEvents when its value changes.
        /// </summary>
        public const byte SemanticChange = 0x10;

        /// <summary>
        /// Indicates if the current StatusCode of the value is writable.
        /// </summary>
        public const byte StatusWrite = 0x20;

        /// <summary>
        /// Indicates if the current SourceTimestamp is writable.
        /// </summary>
        public const byte TimestampWrite = 0x40;
    }

    /// <summary>
    /// Constants defined for the ValueRank attribute.
    /// </summary>
    /// <remarks>
    /// Constants defined for the ValueRank attribute.
    /// </remarks>
    public static class ValueRanks
    {
        /// <summary>
        /// The variable may be a scalar or a one dimensional array.
        /// </summary>
        public const int ScalarOrOneDimension = -3;

        /// <summary>
        /// The variable may be a scalar or an array of any dimension.
        /// </summary>
        public const int Any = -2;

        /// <summary>
        /// The variable is always a scalar.
        /// </summary>
        public const int Scalar = -1;

        /// <summary>
        /// The variable is always an array with one or more dimensions.
        /// </summary>
        public const int OneOrMoreDimensions = 0;

        /// <summary>
        /// The variable is always one dimensional array.
        /// </summary>
        public const int OneDimension = 1;

        /// <summary>
        /// The variable is always an array with two or more dimensions.
        /// </summary>
        public const int TwoDimensions = 2;

        /// <summary>
        /// Checks if the actual value rank is compatible with the expected value rank.
        /// </summary>
        public static bool IsValid(int actualValueRank, int expectedValueRank)
        {
            if (actualValueRank == expectedValueRank)
            {
                return true;
            }

            switch (expectedValueRank)
            {
                case Any:
                    return true;
                case OneOrMoreDimensions:
                    if (actualValueRank < 0)
                    {
                        return false;
                    }

                    break;
                case ScalarOrOneDimension:
                    if (actualValueRank is not Scalar and not OneDimension)
                    {
                        return false;
                    }

                    break;
                default:
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Checks if the actual array dimensions is compatible with the expected value rank and array dimensions.
        /// </summary>
        public static bool IsValid(
            IList<uint> actualArrayDimensions,
            int valueRank,
            IList<uint> expectedArrayDimensions)
        {
            // check if parameter omitted.
            if (actualArrayDimensions == null || actualArrayDimensions.Count == 0)
            {
                return expectedArrayDimensions == null || expectedArrayDimensions.Count == 0;
            }

            // no array dimensions allowed for scalars.
            if (valueRank == Scalar)
            {
                return false;
            }

            // check if one dimension required.
            if (valueRank is OneDimension or ScalarOrOneDimension &&
                actualArrayDimensions.Count != 1)
            {
                return false;
            }

            // check number of dimensions.
            if (valueRank != OneOrMoreDimensions && actualArrayDimensions.Count != valueRank)
            {
                return false;
            }

            // nothing more to do if expected dimensions omitted.
            if (expectedArrayDimensions == null || expectedArrayDimensions.Count == 0)
            {
                return true;
            }

            // check dimensions.
            if (expectedArrayDimensions.Count != actualArrayDimensions.Count)
            {
                return false;
            }

            // check length of each dimension.
            for (int ii = 0; ii < expectedArrayDimensions.Count; ii++)
            {
                if (expectedArrayDimensions[ii] != actualArrayDimensions[ii] &&
                    expectedArrayDimensions[ii] != 0)
                {
                    return false;
                }
            }

            // everything ok.
            return true;
        }
    }

    /// <summary>
    /// Constants defined for the MinimumSamplingInterval attribute.
    /// </summary>
    /// <remarks>
    /// Constants defined for the MinimumSamplingInterval attribute.
    /// </remarks>
    public static class MinimumSamplingIntervals
    {
        /// <summary>
        /// The server does not know how fast the value can be sampled.
        /// </summary>
        public const double Indeterminate = -1;

        /// <summary>
        /// The server can sample the variable continuously.
        /// </summary>
        public const double Continuous = 0;
    }
}
