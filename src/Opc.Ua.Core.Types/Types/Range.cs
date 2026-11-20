/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;

namespace Opc.Ua
{
    /// <summary>
    /// Stores a range.
    /// </summary>
    public partial class Range
    {
        /// <summary>
        /// Initializes the object with the high and low limits.
        /// </summary>
        public Range(double high, double low)
        {
            m_low = low;
            m_high = high;

            // swap values if high is not actually higher.
            if (low > high)
            {
                m_high = low;
                m_low = high;
            }
        }

        /// <summary>
        /// Returns the difference between high and low.
        /// </summary>
        public double Magnitude => Math.Abs(m_high - m_low);
    }
}
