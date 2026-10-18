/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
