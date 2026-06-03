/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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

using System;

namespace Opc.Ua
{
    /// <summary>
    /// Helpers for the legacy <c>Utils.Trace</c> trampoline that
    /// <see cref="TraceConfiguration"/> drives.
    /// </summary>
    public partial class TraceConfiguration
    {
        /// <summary>
        /// Apply the trace configuration to the legacy
        /// <see cref="Utils"/>-based trace pipeline. Centralizes the
        /// <c>SetTraceLog</c> / <c>SetTraceMask</c> / <c>SetTraceOutput</c>
        /// trampoline so callers don't have to suppress
        /// <c>CS0618</c> at every site.
        /// </summary>
        /// <remarks>
        /// The underlying <see cref="Utils"/> trace API is itself
        /// <c>[Obsolete]</c> in favour of <see cref="ITelemetryContext"/>-derived
        /// <c>ILogger</c> instances. This method exists so legacy consumers can
        /// keep wiring <see cref="TraceConfiguration"/> through their startup
        /// path verbatim; new code should configure logging via
        /// <c>ITelemetryContext</c> directly.
        /// </remarks>
        public void ApplySettings()
        {
#pragma warning disable CS0618 // Type or member is obsolete
            if (OutputFilePath != null)
            {
                Utils.SetTraceLog(OutputFilePath, DeleteOnLoad);
            }
            Utils.SetTraceMask(TraceMasks);
            Utils.SetTraceOutput(TraceMasks == 0
                ? Utils.TraceOutput.Off
                : Utils.TraceOutput.DebugAndFile);
#pragma warning restore CS0618 // Type or member is obsolete
        }
    }
}
