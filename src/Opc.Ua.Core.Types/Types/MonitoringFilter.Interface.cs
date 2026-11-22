// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

namespace Opc.Ua
{
    /// <summary>
    /// Supplies a protocol monitoring-filter definition without an additional handler wrapper.
    /// </summary>
    public partial class MonitoringFilter : IMonitoringFilter
    {
        /// <inheritdoc/>
        MonitoringFilter IMonitoringFilter.Filter => this;
    }
}
