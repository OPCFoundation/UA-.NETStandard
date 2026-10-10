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

using System;
using System.ComponentModel;
using System.IO;
#if NET7_0_OR_GREATER
using System.Runtime.CompilerServices;
#endif
using System.Runtime.InteropServices;
#if !NET7_0_OR_GREATER
using System.Text;
#endif
using Microsoft.Win32.SafeHandles;

namespace Opc.Ua
{
    /// <summary>
    /// Physical-file-system durability barriers shared by transactional file stores.
    /// An atomic rename alone does not persist its directory entry.
    /// </summary>
    public static partial class DirectoryDurability
    {
        /// <summary>
        /// Flushes directory entries to the operating system's stable-storage boundary.
        /// Fails explicitly when the platform, permissions or filesystem cannot provide it.
        /// Flush staged file contents before calling this around an atomic publication.
        /// </summary>
        /// <exception cref="IOException">
        /// The directory cannot be opened or its entries cannot be flushed to stable storage.
        /// </exception>
        public static void Flush(string directoryPath)
        {
            directoryPath.ThrowIfNull(nameof(directoryPath));
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using SafeFileHandle handle = CreateFileW(
                    directoryPath, GenericWrite, FileShareRead | FileShareWrite | FileShareDelete,
                    IntPtr.Zero, OpenExisting, FileFlagBackupSemantics, IntPtr.Zero);
                if (handle.IsInvalid || !FlushFileBuffers(handle))
                {
                    throw Failure(directoryPath);
                }
                return;
            }

#if NET7_0_OR_GREATER
            using SafeUnixDirectoryHandle directory = OpenUnixDirectory(directoryPath, 0);
#else
            byte[] utf8Path = Encoding.UTF8.GetBytes(directoryPath + "\0");
            using SafeUnixDirectoryHandle directory = OpenUnixDirectory(utf8Path, 0);
#endif
            if (directory.IsInvalid || Fsync(directory) != 0)
            {
                throw Failure(directoryPath);
            }
        }

        private static IOException Failure(string path)
        {
            int error = Marshal.GetLastWin32Error();
            return new IOException($"Unable to durably synchronize directory '{path}'.", new Win32Exception(error));
        }

#if NET7_0_OR_GREATER
        [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW",
            StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static partial SafeFileHandle CreateFileW(
            string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
            uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [LibraryImport("kernel32.dll", EntryPoint = "FlushFileBuffers", SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static partial bool FlushFileBuffers(SafeFileHandle file);

        [LibraryImport("libc", EntryPoint = "open", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static partial SafeUnixDirectoryHandle OpenUnixDirectory(string path, int flags);

        [LibraryImport("libc", EntryPoint = "fsync", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static partial int Fsync(SafeUnixDirectoryHandle file);

        [LibraryImport("libc", EntryPoint = "close", SetLastError = true)]
        [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static partial int CloseUnix(IntPtr file);
#else
        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", ExactSpelling = true,
            CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern SafeFileHandle CreateFileW(
            string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes,
            uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", EntryPoint = "FlushFileBuffers", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FlushFileBuffers(SafeFileHandle file);

        [DllImport("libc", EntryPoint = "open", ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static extern SafeUnixDirectoryHandle OpenUnixDirectory([In] byte[] path, int flags);

        [DllImport("libc", EntryPoint = "fsync", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static extern int Fsync(SafeUnixDirectoryHandle file);

        [DllImport("libc", EntryPoint = "close", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static extern int CloseUnix(IntPtr file);
#endif

        private sealed class SafeUnixDirectoryHandle : SafeHandleMinusOneIsInvalid
        {
            public SafeUnixDirectoryHandle()
                : base(ownsHandle: true)
            {
            }

            protected override bool ReleaseHandle()
            {
                return CloseUnix(handle) == 0;
            }
        }

        private const uint GenericWrite = 0x40000000;
        private const uint FileShareRead = 0x00000001;
        private const uint FileShareWrite = 0x00000002;
        private const uint FileShareDelete = 0x00000004;
        private const uint OpenExisting = 3;
        private const uint FileFlagBackupSemantics = 0x02000000;
    }
}
