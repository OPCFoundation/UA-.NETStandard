// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua
{
    public partial class TimeZoneDataType
    {
        /// <summary>
        /// Returns the TimeZone information for the current local time.
        /// </summary>
        /// <returns>The TimeZone information for the current local time.</returns>
        public static TimeZoneDataType Local => new()
        {
            Offset = (short)TimeZoneInfo.Local.GetUtcOffset(DateTime.Now).TotalMinutes,
            DaylightSavingInOffset = true
        };
    }
}
