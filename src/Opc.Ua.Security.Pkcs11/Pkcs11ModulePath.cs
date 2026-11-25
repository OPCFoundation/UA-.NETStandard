// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.IO;
#if NETFRAMEWORK
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
#else
using System.Collections.Generic;
#endif

namespace Opc.Ua.Security.Pkcs11
{
    /// <summary>
    /// Resolves the configured file path used for module lifetime coordination.
    /// </summary>
    internal static class Pkcs11ModulePath
    {
        /// <summary>
        /// Compares the canonical paths using the platform's path casing convention.
        /// </summary>
        public static StringComparer Comparer { get; } = Path.DirectorySeparatorChar == '\\'
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

        /// <summary>
        /// Resolves relative paths and existing file and directory links.
        /// </summary>
        /// <remarks>
        /// If a path cannot be resolved, leave reporting the load error to the loader.
        /// </remarks>
        public static string Canonicalize(string path)
        {
            string fullPath = NormalizeWindowsPrefix(Path.GetFullPath(path));
            try
            {
                for (int pass = 0; pass < 40; pass++)
                {
                    string canonical = NormalizeWindowsPrefix(ResolveLinks(fullPath));
                    if (Comparer.Equals(fullPath, canonical))
                    {
                        return canonical;
                    }

                    // A link's target can itself have a linked parent directory.
                    fullPath = canonical;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return fullPath;
            }

            throw new IOException("The PKCS#11 module path did not resolve to a stable location.");
        }

        private static string NormalizeWindowsPrefix(string path)
        {
            if (Path.DirectorySeparatorChar != '\\')
            {
                return path;
            }

            if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase))
            {
#if NETFRAMEWORK
                return @"\\" + path.Substring(8);
#else
                return string.Concat(@"\\", path.AsSpan(8));
#endif
            }

            return path.StartsWith(@"\\?\", StringComparison.Ordinal) && path.Length > 6 && path[5] == ':'
                ? path.Substring(4)
                : path;
        }

#if NETFRAMEWORK
        private static string ResolveLinks(string fullPath)
        {
            using FileStream file = File.Open(fullPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            char[] buffer = new char[512];
            uint length = GetFinalPathNameByHandle(file.SafeFileHandle, buffer, (uint)buffer.Length, 0);
            if (length >= buffer.Length)
            {
                buffer = new char[length];
                length = GetFinalPathNameByHandle(file.SafeFileHandle, buffer, (uint)buffer.Length, 0);
            }

            if (length == 0 || length >= buffer.Length)
            {
                return fullPath;
            }

            return new string(buffer, 0, (int)length);
        }

        [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode,
            ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        private static extern uint GetFinalPathNameByHandle(
            SafeFileHandle file,
            [Out] char[] path,
            uint capacity,
            uint flags);
#else
        private static string ResolveLinks(string fullPath)
        {
            var file = new FileInfo(fullPath);
            if (!file.Exists)
            {
                return fullPath;
            }

            var directories = new Stack<string>();
            DirectoryInfo? directory = file.Directory;
            while (directory?.Parent != null)
            {
                directories.Push(directory.Name);
                directory = directory.Parent;
            }

            string path = directory!.FullName;
            while (directories.Count > 0)
            {
                path = Path.Combine(path, directories.Pop());
                string? target = new DirectoryInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName;
                if (target != null)
                {
                    path = target;
                }
            }

            path = Path.Combine(path, file.Name);
            return new FileInfo(path).ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
        }
#endif
    }
}
