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
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Opc.Ua.Storage
{
    /// <summary>
    /// Shared stable-storage barriers for immutable files and atomic generation publication.
    /// </summary>
    internal static class DurableFileOperations
    {
        /// <summary>
        /// Creates and durably writes a new file, without replacing an existing file.
        /// The caller owns publication and any cleanup after an interrupted write.
        /// Null and empty content both create an empty file.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="path"/> is <c>null</c>.</exception>
        internal static async ValueTask WriteNewAsync(
            string path,
            ByteString content,
            Action<string>? fileCreated = null,
            CancellationToken cancellationToken = default)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }
            cancellationToken.ThrowIfCancellationRequested();
            using var stream = new FileStream(
                path,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough | FileOptions.Asynchronous);
            fileCreated?.Invoke(path);
#if NET
            await stream.WriteAsync(content.Memory, cancellationToken).ConfigureAwait(false);
#else
            if (MemoryMarshal.TryGetArray(content.Memory, out ArraySegment<byte> segment))
            {
                await stream.WriteAsync(segment.Array!, segment.Offset, segment.Count, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                byte[] bytes = content.ToArray() ?? [];
                await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
            }
#endif
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            // FlushAsync has no disk-barrier option. TODO: use an asynchronous disk flush when available.
#pragma warning disable CA1849
            stream.Flush(flushToDisk: true);
#pragma warning restore CA1849
        }

        /// <summary>
        /// Makes directory-entry changes durable. This is the native storage barrier,
        /// not a substitute for flushing a file's contents before publication.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="path"/> is <c>null</c>.</exception>
        /// <exception cref="IOException"></exception>
        internal static void SynchronizeDirectory(string path, string storageDescription)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }
            if (storageDescription == null)
            {
                throw new ArgumentNullException(nameof(storageDescription));
            }
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                using SafeFileHandle handle = CreateFileW(
                    path,
                    kGenericWrite,
                    kFileShareRead | kFileShareWrite | kFileShareDelete,
                    IntPtr.Zero,
                    kOpenExisting,
                    kFileFlagBackupSemantics,
                    IntPtr.Zero);
                if (handle.IsInvalid)
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new IOException(
                        $"Unable to open {storageDescription} directory '{path}' for a durability flush.",
                        new Win32Exception(error));
                }
                if (!FlushFileBuffers(handle))
                {
                    int error = Marshal.GetLastWin32Error();
                    throw new IOException(
                        $"Unable to durably synchronize {storageDescription} directory '{path}'.",
                        new Win32Exception(error));
                }
                return;
            }

            byte[] utf8Path = System.Text.Encoding.UTF8.GetBytes(path + "\0");
            using SafeUnixDirectoryHandle directory = OpenUnixDirectory(utf8Path, kOpenReadOnly);
            if (directory.IsInvalid)
            {
                int error = Marshal.GetLastWin32Error();
                throw new IOException(
                    $"Unable to open {storageDescription} directory '{path}' for a durability flush.",
                    new Win32Exception(error));
            }
            if (Fsync(directory) != 0)
            {
                int error = Marshal.GetLastWin32Error();
                throw new IOException(
                    $"Unable to durably synchronize {storageDescription} directory '{path}'.",
                    new Win32Exception(error));
            }
        }

        // Preserve the shared net48/native ABI.
        // TODO: validate per-target LibraryImport replacements on Windows and Unix.
#pragma warning disable SYSLIB1054
        /// <summary>
        /// Opens a Windows directory handle for a stable-storage barrier.
        /// </summary>
        [DllImport("kernel32.dll", EntryPoint = "CreateFileW", ExactSpelling = true,
            CharSet = CharSet.Unicode, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern SafeFileHandle CreateFileW(
            string fileName,
            uint desiredAccess,
            uint shareMode,
            IntPtr securityAttributes,
            uint creationDisposition,
            uint flagsAndAttributes,
            IntPtr templateFile);

        /// <summary>
        /// Flushes pending Windows filesystem metadata for an open directory.
        /// </summary>
        [DllImport("kernel32.dll", EntryPoint = "FlushFileBuffers", ExactSpelling = true, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FlushFileBuffers(SafeFileHandle file);

        /// <summary>
        /// Opens a Unix directory descriptor.
        /// </summary>
        [DllImport("libc", EntryPoint = "open", ExactSpelling = true,
            CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static extern SafeUnixDirectoryHandle OpenUnixDirectory([In] byte[] path, int flags);

        /// <summary>
        /// Flushes pending Unix filesystem metadata for an open directory.
        /// </summary>
        [DllImport("libc", EntryPoint = "fsync", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static extern int Fsync(SafeUnixDirectoryHandle file);

        /// <summary>
        /// Releases a Unix directory descriptor.
        /// </summary>
        [DllImport("libc", EntryPoint = "close", CallingConvention = CallingConvention.Cdecl, SetLastError = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]
        private static extern int CloseUnix(IntPtr file);
#pragma warning restore SYSLIB1054

        /// <summary>The Unix read-only open flag.</summary>
        private const int kOpenReadOnly = 0;

        /// <summary>The Windows generic-write access mask.</summary>
        private const uint kGenericWrite = 0x40000000;

        /// <summary>The Windows read-sharing flag.</summary>
        private const uint kFileShareRead = 0x00000001;

        /// <summary>The Windows write-sharing flag.</summary>
        private const uint kFileShareWrite = 0x00000002;

        /// <summary>The Windows deletion-sharing flag.</summary>
        private const uint kFileShareDelete = 0x00000004;

        /// <summary>The Windows existing-path disposition.</summary>
        private const uint kOpenExisting = 3;

        /// <summary>The Windows directory-handle flag.</summary>
        private const uint kFileFlagBackupSemantics = 0x02000000;

        /// <summary>
        /// Closes a native Unix directory descriptor once, including on a failed flush.
        /// </summary>
        private sealed class SafeUnixDirectoryHandle : SafeHandleMinusOneIsInvalid
        {
            /// <summary>
            /// Initializes an owning handle for native descriptor marshalling.
            /// </summary>
            private SafeUnixDirectoryHandle()
                : base(ownsHandle: true)
            {
            }

            /// <inheritdoc/>
            protected override bool ReleaseHandle()
            {
                return CloseUnix(handle) == 0;
            }
        }
    }
}
