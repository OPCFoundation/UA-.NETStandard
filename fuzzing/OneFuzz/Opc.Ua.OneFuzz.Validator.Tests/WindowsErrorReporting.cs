/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00. See LICENSE.txt in the repository root.
 * ======================================================================*/

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Opc.Ua.OneFuzz.Validator.Tests
{
    /// <summary>
    /// Expected CLI rejections can be unhandled exceptions. Do not show OS fault
    /// dialogs or run Windows Error Reporting for these intentional child failures.
    /// The error mode is inherited by children and restored after this nonparallel
    /// fixture; it does not alter the validator's exit code or exception behavior.
    /// </summary>
    internal sealed partial class WindowsErrorReporting : IDisposable
    {
        private WindowsErrorReporting(uint previous)
        {
            m_previous = previous;
        }

        internal static WindowsErrorReporting? Suppress()
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            const uint kSemNoGpFaultErrorBox = 0x0002;
            uint previous = SetErrorMode(kSemNoGpFaultErrorBox);
            _ = SetErrorMode(previous | kSemNoGpFaultErrorBox);
            return new WindowsErrorReporting(previous);
        }

        public void Dispose()
        {
            if (OperatingSystem.IsWindows())
            {
                _ = SetErrorMode(m_previous);
            }
        }

        [LibraryImport("kernel32.dll")]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [SupportedOSPlatform("windows")]
        private static partial uint SetErrorMode(uint mode);

        private readonly uint m_previous;
    }
}
