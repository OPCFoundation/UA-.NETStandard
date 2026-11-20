// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;

namespace Opc.Ua.MachineTool
{
    /// <summary>
    /// Selects the independently namespaced released models in the MachineTool package.
    /// Model selection does not advertise equipment capabilities or conformance.
    /// </summary>
    [Flags]
    public enum MachineToolParts
    {
        /// <summary>
        /// No models.
        /// </summary>
        None = 0,

        /// <summary>
        /// OPC 40501-1 MachineTool, including its required legacy Production declarations.
        /// </summary>
        MachineTool = 1,

        /// <summary>
        /// OPC 40505 Wireless Machine Tool Peripherals, independently of OPC 40501-1.
        /// </summary>
        Wmtp = 2,

        /// <summary>
        /// Both released models.
        /// </summary>
        All = MachineTool | Wmtp
    }
}
