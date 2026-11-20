/* ========================================================================
 * SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
 * SPDX-License-Identifier: MIT
 * ======================================================================*/

using System;
using System.IO;
using System.Security;

namespace Opc.Ua.Mcp.Tools
{
    /// <summary>
    /// Creates exclusive NodeSet export files below a private, link-free directory tree.
    /// </summary>
    internal static class NodeSetExportFileWriter
    {
        /// <summary>
        /// Opens a new output file after validating the root and each existing path component.
        /// </summary>
        /// <param name="root">The configured export root.</param>
        /// <param name="filePath">The fully resolved output file.</param>
        /// <returns>An exclusive stream for a newly created file.</returns>
        internal static FileStream CreateNew(string root, string filePath)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(root);
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

            string fullRoot = Path.GetFullPath(root);
            string fullFilePath = Path.GetFullPath(filePath);
            string relativePath = Path.GetRelativePath(fullRoot, fullFilePath);
            if (EscapesRoot(relativePath))
            {
                throw new ArgumentException("The output file is outside the configured export root.", nameof(filePath));
            }

            EnsureDirectoryTree(fullRoot, privateRoot: true);
            string? parent = Path.GetDirectoryName(fullFilePath);
            if (string.IsNullOrEmpty(parent))
            {
                throw new IOException("The export file has no parent directory.");
            }
            EnsureDirectoryTree(parent, privateRoot: false, fullRoot);

            var options = new FileStreamOptions
            {
                Mode = FileMode.CreateNew,
                Access = FileAccess.Write,
                Share = FileShare.None,
                Options = FileOptions.SequentialScan
            };
            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            }
            return new FileStream(fullFilePath, options);
        }

        private static void EnsureDirectoryTree(
            string directoryPath,
            bool privateRoot,
            string? requiredAncestor = null)
        {
            string fullPath = Path.GetFullPath(directoryPath);
            string volumeRoot = Path.GetPathRoot(fullPath) ??
                throw new IOException("The export path has no volume root.");
            if (OperatingSystem.IsWindows() &&
                (volumeRoot.StartsWith(@"\\", StringComparison.Ordinal) ||
                 volumeRoot.StartsWith("//", StringComparison.Ordinal)))
            {
                throw new IOException("Network export roots are not supported.");
            }

            string[] components = fullPath[volumeRoot.Length..]
                .Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                    StringSplitOptions.RemoveEmptyEntries);
            if (components.Length == 0)
            {
                throw new IOException("A filesystem volume root cannot be used as the export directory.");
            }
            string current = volumeRoot;
            for (int index = 0; index < components.Length; index++)
            {
                current = Path.Combine(current, components[index]);
                bool isRoot = privateRoot && index == components.Length - 1;
                if (!Directory.Exists(current))
                {
                    if (File.Exists(current))
                    {
                        throw new IOException("An export directory component is not a directory.");
                    }
                    if (OperatingSystem.IsWindows())
                    {
                        Directory.CreateDirectory(current);
                    }
                    else
                    {
                        Directory.CreateDirectory(current, UnixFileMode.UserRead |
                            UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                    }
                }

                FileAttributes attributes = File.GetAttributes(current);
                if ((attributes & FileAttributes.Directory) == 0 ||
                    (attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new IOException("Export paths cannot contain files, links or reparse points.");
                }
                if (!OperatingSystem.IsWindows())
                {
                    if (isRoot)
                    {
                        File.SetUnixFileMode(current, UnixFileMode.UserRead |
                            UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                    }
                    UnixFileMode mode = File.GetUnixFileMode(current);
                    if (isRoot &&
                        (mode & (UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                            UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute)) != 0)
                    {
                        throw new SecurityException(
                            "The configured export root must be private to the current account.");
                    }
                    if (!isRoot &&
                        (mode & (UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) != 0 &&
                        (mode & UnixFileMode.StickyBit) == 0)
                    {
                        throw new SecurityException("An export path ancestor is writable by other accounts.");
                    }
                }
            }

            if (requiredAncestor is not null)
            {
                string relative = Path.GetRelativePath(requiredAncestor, fullPath);
                if (EscapesRoot(relative))
                {
                    throw new ArgumentException("The export directory is outside its configured root.");
                }
            }
        }

        private static bool EscapesRoot(string relativePath)
        {
            return relativePath == ".." ||
                relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                relativePath.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal) ||
                Path.IsPathRooted(relativePath);
        }
    }
}
