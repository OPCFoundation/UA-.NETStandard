// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System.Threading;
using System.Threading.Tasks;
using V1 = Opc.Ua.ISA95.JobControl.V1;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.ISA95.Server.Providers
{
    /// <summary>
    /// Provides version-specific snapshots for JobOrderList variables.
    /// </summary>
    public interface IIsa95JobOrderCatalog
    {
        /// <summary>
        /// Gets the current Job Control V1 job-order list.
        /// </summary>
        ValueTask<ArrayOf<V1.ISA95JobOrderDataType>> GetJobOrdersV1Async(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the current Job Control V2 job-order and state list.
        /// </summary>
        ValueTask<ArrayOf<V2.ISA95JobOrderAndStateDataType>> GetJobOrdersV2Async(
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the maximum number of downloadable job orders.
        /// </summary>
        ushort MaxDownloadableJobOrders { get; }
    }
}
