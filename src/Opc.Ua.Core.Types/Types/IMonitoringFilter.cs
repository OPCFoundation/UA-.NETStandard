// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua
{
    /// <summary>
    /// Exposes the monitoring-filter definition supplied directly or owned by a filter handler.
    /// </summary>
    public interface IMonitoringFilter
    {
        /// <summary>
        /// Gets the active filter definition, or null when no monitoring filter is applied.
        /// </summary>
        MonitoringFilter? Filter { get; }
    }
}
