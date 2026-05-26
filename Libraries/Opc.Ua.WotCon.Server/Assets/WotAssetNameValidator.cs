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
using System.Diagnostics.CodeAnalysis;
using System.IO;

namespace Opc.Ua.WotCon.Server.Assets
{
    /// <summary>
    /// Validates user-supplied asset names before they are used as
    /// file-name components in the Thing Description persistence folder.
    /// Centralising the rules in one place keeps every callsite honest
    /// and gives us a single audit point for path-traversal hardening.
    /// </summary>
    /// <remarks>
    /// Asset names arrive over the wire through OPC UA's
    /// <c>WoTAssetConnectionManagement.CreateAsset</c> /
    /// <c>CreateAssetForEndpoint</c> methods and are appended verbatim
    /// to the configured persistence folder; without these checks a
    /// caller could supply <c>"../../etc/passwd"</c>,
    /// <c>"C:\\Windows\\System32\\..."</c>, a NUL byte, an absolute
    /// path, or a Windows reserved device name and either escape the
    /// configured folder or trip the underlying filesystem.
    /// </remarks>
    internal static class WotAssetNameValidator
    {
        /// <summary>
        /// Maximum allowed asset-name length. Picked to leave headroom
        /// under the NTFS 255-byte component limit once the
        /// <c>.jsonld</c> extension and any namespace prefix added by
        /// future revisions is appended.
        /// </summary>
        public const int MaxNameLength = 200;

        /// <summary>
        /// Validates <paramref name="name"/> against the asset-name
        /// character / shape rules.
        /// </summary>
        /// <param name="name">The user-supplied asset name.</param>
        /// <returns>
        /// <see cref="ServiceResult.Good"/> when the name is acceptable;
        /// <c>Bad_InvalidArgument</c> with an explanatory message
        /// otherwise.
        /// </returns>
        public static ServiceResult Validate(string? name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "Asset name is required.");
            }
            if (name!.Length > MaxNameLength)
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "Asset name exceeds the maximum length of {0} characters.",
                    MaxNameLength);
            }
            // Reject control / NUL bytes early — Path.GetInvalidFileNameChars
            // already lists them but we want a clear message for NUL.
            if (name.Contains('\0', StringComparison.Ordinal))
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "Asset name must not contain NUL bytes.");
            }
            // Reject path separators outright — both styles, on every
            // platform, to avoid surprises when files persisted on Linux
            // are later read on Windows or vice versa.
            if (name.Contains('/', StringComparison.Ordinal) ||
                name.Contains('\\', StringComparison.Ordinal))
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "Asset name must not contain path separators.");
            }
            // Reject any ".." substring — covers ".." on its own,
            // "..\\foo", "foo/.." and any other traversal token.
            if (name.Contains("..", StringComparison.Ordinal))
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "Asset name must not contain '..'.");
            }
            // Reject leading dot / space / tilde — these tokens have
            // special meaning on at least one supported platform (hidden
            // files on Linux, stripped trailing dots/spaces on Windows,
            // home-folder expansion when passed to a shell).
            char first = name[0];
            if (first is '.' or ' ' or '~')
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "Asset name must not start with '.', ' ' or '~'.");
            }
            // Reject trailing dot / space — Windows silently strips
            // these when resolving file paths, which would let a caller
            // smuggle a name that compares unequal to its on-disk form.
            char last = name[^1];
            if (last is '.' or ' ')
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "Asset name must not end with '.' or ' '.");
            }
            if (name.Contains(':', StringComparison.Ordinal))
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "Asset name must not contain ':'.");
            }
            // Catch-all for any other invalid file-name char on the
            // current platform — keeps us in lock-step with the runtime
            // when new chars are added.
            char[] invalid = Path.GetInvalidFileNameChars();
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                for (int j = 0; j < invalid.Length; j++)
                {
                    if (c == invalid[j])
                    {
                        return ServiceResult.Create(
                            StatusCodes.BadInvalidArgument,
                            "Asset name contains invalid character '{0}'.",
                            c);
                    }
                }
            }
            if (IsWindowsReservedName(name))
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "Asset name '{0}' is reserved on Windows.",
                    name);
            }
            return ServiceResult.Good;
        }

        /// <summary>
        /// Computes the absolute on-disk path for the
        /// <paramref name="assetName"/>'s persisted Thing Description
        /// and confirms it lies strictly under
        /// <paramref name="baseFolder"/>. Returns <c>false</c> if the
        /// name fails <see cref="Validate"/> or if the canonical
        /// resolution escapes <paramref name="baseFolder"/> (the
        /// defence-in-depth check after character filtering).
        /// </summary>
        public static bool TryGetSafeFileName(
            string assetName,
            string baseFolder,
            [NotNullWhen(true)] out string? fullPath)
        {
            fullPath = null;
            if (string.IsNullOrEmpty(baseFolder))
            {
                return false;
            }
            if (!ServiceResult.IsGood(Validate(assetName)))
            {
                return false;
            }
            string baseFull = Path.GetFullPath(baseFolder);
            string candidate = Path.GetFullPath(Path.Combine(baseFull, assetName + ".jsonld"));

            // Normalise trailing separator on the base before comparing
            // so the StartsWith check is exact ("/foo" must not match
            // "/foobar"). Use OrdinalIgnoreCase because Windows file
            // systems are case-insensitive; on Linux this is still safe
            // because matching is anchored at the absolute prefix.
            string baseWithSeparator = baseFull;
            if (baseWithSeparator.Length == 0 ||
                (baseWithSeparator[^1] != Path.DirectorySeparatorChar &&
                    baseWithSeparator[^1] != Path.AltDirectorySeparatorChar))
            {
                baseWithSeparator += Path.DirectorySeparatorChar;
            }
            if (!candidate.StartsWith(baseWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            fullPath = candidate;
            return true;
        }

        private static bool IsWindowsReservedName(string name)
        {
            // Compare against CON, PRN, AUX, NUL, COM1-COM9, LPT1-LPT9
            // case-insensitively; reject regardless of extension since
            // "con.jsonld" is still reserved on Windows.
            // Use ordinal comparison so the check is the same on every
            // platform and doesn't depend on the current culture.
            if (Equals(name, "CON") ||
                Equals(name, "PRN") ||
                Equals(name, "AUX") ||
                Equals(name, "NUL"))
            {
                return true;
            }
            if (name.Length == 4 &&
                (name[0] == 'C' || name[0] == 'c') &&
                (name[1] == 'O' || name[1] == 'o') &&
                (name[2] == 'M' || name[2] == 'm') &&
                name[3] >= '1' &&
                name[3] <= '9')
            {
                return true;
            }
            if (name.Length == 4 &&
                (name[0] == 'L' || name[0] == 'l') &&
                (name[1] == 'P' || name[1] == 'p') &&
                (name[2] == 'T' || name[2] == 't') &&
                name[3] >= '1' &&
                name[3] <= '9')
            {
                return true;
            }
            return false;

            static bool Equals(string value, string reserved)
                => string.Equals(value, reserved, StringComparison.OrdinalIgnoreCase);
        }
    }
}
