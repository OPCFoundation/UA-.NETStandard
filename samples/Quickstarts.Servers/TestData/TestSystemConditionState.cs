// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using Opc.Ua;

namespace TestData
{
    public partial class TestSystemConditionState
    {
        /// <summary>
        /// Initializes the object as a collection of counters which change value on read.
        /// </summary>
        protected override void OnAfterCreate(ISystemContext context, NodeState node, CancellationToken ct = default)
        {
            base.OnAfterCreate(context, node, ct);
            MonitoredNodeCount!.OnSimpleReadValue = OnReadMonitoredNodeCount;
        }

        /// <summary>
        /// Reads the value for the MonitoredNodeCount.
        /// </summary>
        protected virtual ServiceResult OnReadMonitoredNodeCount(
            ISystemContext context,
            NodeState node,
            ref Variant value)
        {
            if (context?.SystemHandle is not TestDataSystem system)
            {
                return StatusCodes.BadOutOfService;
            }

            value = system.MonitoredNodeCount;
            return ServiceResult.Good;
        }
    }
}
