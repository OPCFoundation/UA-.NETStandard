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

            if (!fullRoot.EndsWith(Path.DirectorySeparatorChar))
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
