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

#if NETSTANDARD2_1
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Opc.Ua
{
    /// <summary>
    /// Sets Unix permission bits for the netstandard2.1 build, whose reference assemblies
    /// lack File.SetUnixFileMode, through libc chmod so private key material is never
    /// left world-readable.
    /// </summary>
    internal static class UnixFileModeShim
    {
        /// <summary>
        /// Owner read and write (0600).
        /// </summary>
        public const int UserReadWrite = 0x180;

        /// <summary>
        /// Owner read, write and execute (0700).
        /// </summary>
        public const int UserReadWriteExecute = 0x1C0;

        /// <summary>
        /// Sets the Unix permission bits of a file or directory.
        /// </summary>
        /// <exception cref="UnauthorizedAccessException">The caller may not change the mode.</exception>
        /// <exception cref="IOException">The mode could not be changed.</exception>
        public static void SetMode(string path, int mode)
        {
            if (NativeMethods.chmod(path, mode) != 0)
            {
                int errno = Marshal.GetLastWin32Error();
                string message = $"chmod failed with errno {errno}.";
                if (errno is kEPERM or kEACCES)
                {
                    throw new UnauthorizedAccessException(message);
                }
                throw new IOException(message);
            }
        }

        private const int kEPERM = 1;
        private const int kEACCES = 13;

        // SYSLIB1054 (source-generated LibraryImport) is unavailable on netstandard2.1;
        // a classic DllImport of this blittable libc call remains NativeAOT compatible.
#pragma warning disable SYSLIB1054
        private static class NativeMethods
        {
            [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
            [DllImport("libc", SetLastError = true, CharSet = CharSet.Ansi,
                BestFitMapping = false, ThrowOnUnmappableChar = true)]
            internal static extern int chmod(string path, int mode);
        }
#pragma warning restore SYSLIB1054
    }
}
#endif
