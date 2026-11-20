// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.IO;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Confines the file paths an MCP client supplies to capture tools.
    /// </summary>
    /// <remarks>
    /// The capture tool packages each accept a file path from the caller, so
    /// the containment check that stops path traversal turning them into an
    /// arbitrary-file-read primitive lives here rather than being restated per
    /// package.
    /// </remarks>
    public static class McpCapturePath
    {
        /// <summary>
        /// Resolves <paramref name="filePath"/> against
        /// <paramref name="allowedRoot"/> and rejects anything that escapes
        /// it.
        /// </summary>
        /// <param name="filePath">
        /// The caller-supplied path, absolute or relative to
        /// <paramref name="allowedRoot"/>.
        /// </param>
        /// <param name="allowedRoot">
        /// The directory the path must resolve underneath, typically the
        /// per-user packet-capture base folder.
        /// </param>
        /// <returns>The resolved absolute path.</returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="filePath"/> or <paramref name="allowedRoot"/> is
        /// empty or whitespace, or <paramref name="filePath"/> resolves
        /// outside <paramref name="allowedRoot"/>.
        /// </exception>
        public static string ResolveAndValidate(string filePath, string allowedRoot)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
            ArgumentException.ThrowIfNullOrWhiteSpace(allowedRoot);

            string fullPath = Path.GetFullPath(filePath, allowedRoot);
            string fullRoot = Path.GetFullPath(allowedRoot);

            if (!fullRoot.EndsWith(Path.DirectorySeparatorChar, StringComparison.Ordinal))
            {
                fullRoot += Path.DirectorySeparatorChar;
            }

            if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Decode path '{filePath}' resolves to '{fullPath}' which is " +
                    $"outside the allowed root '{allowedRoot}'.",
                    nameof(filePath));
            }

            return fullPath;
        }
    }
}
