// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

namespace Opc.Ua
{
    /// <summary>
    /// The description of a monitored item to create.
    /// </summary>
    public partial class MonitoredItemCreateResult
    {
        /// <summary>
        /// Initializes the object with an error code.
        /// </summary>
        public MonitoredItemCreateResult(uint statusCode)
        {
            Initialize();
            m_statusCode = statusCode;
        }
    }
}
