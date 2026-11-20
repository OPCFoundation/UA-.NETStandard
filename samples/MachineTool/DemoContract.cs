/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

namespace MachineToolSample
{
    /// <summary>
    /// Contains explicit application identities shared by these examples, not a new wire protocol.
    /// </summary>
    internal static class DemoContract
    {
        /// <summary>The sole declared order within each independent demo machine.</summary>
        internal const string JobOrderId = "DemoOrder";

        /// <summary>The explicit work-master identity carried by the generated ISA-95 order.</summary>
        internal const string WorkMasterId = "DemoWorkMaster";

        /// <summary>The descriptor/file name, containing non-executable simulation data.</summary>
        internal const string ProgramName = "DemoProgram.nc";

        /// <summary>The bounded, non-executable content exposed through the existing file provider.</summary>
        internal const string ProgramContents = "SIMULATION DATA ONLY. No CNC instructions or hardware execution.\r\n";
    }
}
