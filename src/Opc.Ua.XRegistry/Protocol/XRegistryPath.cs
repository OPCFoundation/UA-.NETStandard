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
using System.Collections.Generic;
using System.Text;

namespace Opc.Ua.XRegistry.Protocol
{
    /// <summary>
    /// Lossless registry-relative addressing. Identifiers are decoded exactly once,
    /// never normalized with the native source-identity construction helper.
    /// </summary>
    public static class XRegistryPath
    {
        /// <summary>
        /// Returns a canonical escaped path, preserving the exact Unicode identifiers.
        /// Rejects traversal, malformed escapes and ambiguous path separators.
        /// </summary>
        public static string Normalize(string path)
        {
            return FromSegments(GetSegments(path));
        }

        /// <summary>
        /// Splits an escaped relative path into decoded collection and entity identities.
        /// The root has no segments. One trailing slash is permitted.
        /// </summary>
        /// <exception cref="ArgumentException">
        /// The path is not registry-relative or contains invalid escapes, Unicode or path segments.
        /// </exception>
        public static ArrayOf<string> GetSegments(string path)
        {
            path.ThrowIfNull(nameof(path));
            if (path.Length == 0 || path[0] != '/' || path.IndexOfAny(['?', '#']) >= 0)
            {
                throw new ArgumentException("A registry path must start with '/' and contain no query or fragment.",
                    nameof(path));
            }
            if (path == "/")
            {
                return [];
            }

            string remaining = path[1..];
            if (remaining.EndsWith('/'))
            {
                remaining = remaining[..^1];
            }
            string[] segments = remaining.Split('/');
            for (int index = 0; index < segments.Length; index++)
            {
                ValidateEscapes(segments[index]);
                segments[index] = Uri.UnescapeDataString(segments[index]);
                ValidateSegment(segments[index]);
            }
            return segments;
        }

        /// <summary>
        /// Constructs a path from decoded identities without changing their content.
        /// </summary>
        public static string FromSegments(ArrayOf<string> segments)
        {
            if (segments.Count == 0)
            {
                return "/";
            }

            var result = new StringBuilder();
            for (int index = 0; index < segments.Count; index++)
            {
                ValidateSegment(segments[index]);
                result.Append('/')
                    .Append(Uri.EscapeDataString(segments[index]));
            }
            return result.ToString();
        }

        private static void ValidateSegment(string segment)
        {
            if (string.IsNullOrEmpty(segment) ||
                segment is "." or ".." ||
                segment.IndexOfAny(['/', '\\']) >= 0)
            {
                throw new ArgumentException("A path segment cannot be empty, a traversal or contain a separator.",
                    nameof(segment));
            }
            for (int index = 0; index < segment.Length; index++)
            {
                if (char.IsControl(segment[index]) ||
                    (char.IsSurrogate(segment[index]) &&
                        (!char.IsHighSurrogate(segment[index]) ||
                            index + 1 == segment.Length ||
                            !char.IsLowSurrogate(segment[++index]))))
                {
                    throw new ArgumentException(
                        "A path segment must contain valid Unicode without control characters.",
                        nameof(segment));
                }
            }
        }

        private static void ValidateEscapes(string segment)
        {
            for (int index = 0; index < segment.Length; index++)
            {
                if (segment[index] != '%')
                {
                    continue;
                }

                var bytes = new List<byte>();
                do
                {
                    if (index + 2 >= segment.Length ||
                        !TryHex(segment[index + 1], out int high) ||
                        !TryHex(segment[index + 2], out int low))
                    {
                        throw new ArgumentException("A path contains an invalid percent escape.", nameof(segment));
                    }
                    bytes.Add((byte)((high << 4) | low));
                    index += 3;
                }
                while (index < segment.Length && segment[index] == '%');

                try
                {
                    _ = s_utf8.GetCharCount(bytes.ToArray());
                }
                catch (DecoderFallbackException exception)
                {
                    throw new ArgumentException(
                        "Percent escapes must encode valid UTF-8.", nameof(segment), exception);
                }
                index--;
            }
        }

        private static bool TryHex(char value, out int digit)
        {
            digit = value switch
            {
                >= '0' and <= '9' => value - '0',
                >= 'a' and <= 'f' => value - 'a' + 10,
                >= 'A' and <= 'F' => value - 'A' + 10,
                _ => -1
            };
            return digit >= 0;
        }

        private static readonly UTF8Encoding s_utf8 = new(false, true);
    }
}
