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
using System.IO;

namespace Opc.Ua.Client.FileSystem
{
    /// <summary>
    /// Centralised mapping of OPC UA <see cref="StatusCode"/> values to
    /// the <see cref="System.IO"/>-style exceptions surfaced from the
    /// public <c>FileSystemClient</c> API.
    /// </summary>
    internal static class FileSystemErrors
    {
        /// <summary>
        /// Maps a <see cref="ServiceResultException"/> reported during a
        /// file-system operation into a more idiomatic
        /// <see cref="IOException"/> family exception when the status
        /// code matches a well-known OPC UA file-system failure mode.
        /// Returns <paramref name="ex"/> unchanged for unrecognised
        /// codes so callers may rethrow.
        /// </summary>
        /// <param name="ex">The original exception.</param>
        /// <param name="path">The path the caller used; included in the
        /// message and as the <see cref="FileNotFoundException.FileName"/>
        /// when applicable.</param>
        /// <param name="targetIsDirectory">When the caller was operating
        /// on a directory <c>true</c> selects
        /// <see cref="DirectoryNotFoundException"/> over
        /// <see cref="FileNotFoundException"/>.</param>
        /// <returns>A more specific exception, or
        /// <paramref name="ex"/> when no mapping applies.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="ex"/> is <c>null</c>.</exception>
        public static Exception Translate(
            ServiceResultException ex,
            string? path,
            bool targetIsDirectory)
        {
            if (ex == null)
            {
                throw new ArgumentNullException(nameof(ex));
            }

            uint code = ex.StatusCode.Code;
            if (code == StatusCodes.BadNoMatch ||
                code == StatusCodes.BadNodeIdUnknown ||
                code == StatusCodes.BadNotFound)
            {
                return targetIsDirectory
                    ? new DirectoryNotFoundException(
                        FormatNotFound(path, "directory"), ex)
                    : new FileNotFoundException(
                        FormatNotFound(path, "file"),
                        path,
                        ex);
            }

            if (code == StatusCodes.BadBrowseNameDuplicated)
            {
                return new IOException(
                    FormatPath(path, "already exists"), ex);
            }

            if (code == StatusCodes.BadUserAccessDenied ||
                code == StatusCodes.BadNotWritable ||
                code == StatusCodes.BadWriteNotSupported ||
                code == StatusCodes.BadSecurityChecksFailed)
            {
                return new UnauthorizedAccessException(
                    FormatPath(path, "access denied"), ex);
            }

            if (code == StatusCodes.BadOutOfRange ||
                code == StatusCodes.BadInvalidState ||
                code == StatusCodes.BadResourceUnavailable ||
                code == StatusCodes.BadOutOfMemory ||
                code == StatusCodes.BadInvalidArgument)
            {
                return new IOException(
                    FormatPath(path, ex.Message), ex);
            }

            return ex;
        }

        /// <summary>
        /// Wraps the supplied exception as a <see cref="FileNotFoundException"/>
        /// or <see cref="DirectoryNotFoundException"/> depending on the
        /// caller's expected target kind. Used by path-resolution code
        /// when the server returns no matches at all (no
        /// <see cref="ServiceResultException"/>).
        /// </summary>
        public static Exception NotFound(string? path, bool targetIsDirectory)
        {
            return targetIsDirectory
                ? new DirectoryNotFoundException(FormatNotFound(path, "directory"))
                : new FileNotFoundException(
                    FormatNotFound(path, "file"),
                    path);
        }

        /// <summary>
        /// Wraps the supplied path/message as an
        /// <see cref="IOException"/> for the ambiguous-resolution case.
        /// </summary>
        public static IOException Ambiguous(string? path, int targetCount)
        {
            return new IOException(
                $"OPC UA path '{path}' resolved to {targetCount} different nodes; ambiguous.");
        }

        private static string FormatNotFound(string? path, string kind)
        {
            return path == null
                ? $"OPC UA {kind} not found."
                : $"OPC UA {kind} not found: '{path}'.";
        }

        private static string FormatPath(string? path, string suffix)
        {
            return path == null ? suffix : $"'{path}': {suffix}";
        }
    }
}
