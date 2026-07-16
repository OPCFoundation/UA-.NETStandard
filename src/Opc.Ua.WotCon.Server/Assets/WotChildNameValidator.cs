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

namespace Opc.Ua.WotCon.Server.Assets
{
    /// <summary>
    /// Validates Thing-Description child names (the keys of
    /// <c>td.properties</c> / <c>td.actions</c>) before they flow into
    /// OPC UA <see cref="NodeId"/> path segments, <see cref="QualifiedName"/>
    /// browse names, or a typed node's <c>SymbolicName</c>.
    /// Names that would corrupt the address space, hide nodes, break
    /// browse-path resolution, or enable visual-spoofing attacks
    /// (e.g. RTL-override Unicode) are rejected with
    /// <c>Bad_InvalidArgument</c>.
    /// </summary>
    /// <remarks>
    /// This validator is a sibling of <see cref="WotAssetNameValidator"/>:
    /// the asset-name rules focus on file-system safety (path
    /// traversal, Windows reserved names, the <c>.jsonld</c>
    /// extension); the child-name rules focus on OPC UA semantics
    /// (NodeId / browse-path separators, browse-name collisions, and
    /// Unicode characters that distort how the name renders to a
    /// human operator).
    /// </remarks>
    internal static class WotChildNameValidator
    {
        /// <summary>
        /// Maximum allowed child-name length. Picked to leave generous
        /// headroom under the OPC UA spec's <see cref="QualifiedName"/>
        /// 512-character cap and to keep generated NodeId path segments
        /// readable.
        /// </summary>
        public const int MaxLength = 128;

        /// <summary>
        /// Validates a TD property / action key.
        /// </summary>
        /// <param name="name">The raw key from the Thing Description.</param>
        /// <returns>
        /// <see cref="ServiceResult.Good"/> when the key is acceptable;
        /// <c>Bad_InvalidArgument</c> with an explanatory message
        /// otherwise.
        /// </returns>
        public static ServiceResult Validate(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "TD child name is required.");
            }
            if (name!.Length > MaxLength)
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "TD child name exceeds the maximum length of {0} characters.",
                    MaxLength);
            }
            // Reject leading / trailing whitespace — Unicode-aware so
            // U+00A0 NBSP, U+2003 EM SPACE, etc. are also caught.
            if (char.IsWhiteSpace(name[0]) || char.IsWhiteSpace(name[^1]))
            {
                return ServiceResult.Create(
                    StatusCodes.BadInvalidArgument,
                    "TD child name must not start or end with whitespace.");
            }
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (char.IsControl(c))
                {
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidArgument,
                        "TD child name must not contain control characters.");
                }
                if (IsBidiOrFormatChar(c))
                {
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidArgument,
                        "TD child name must not contain BIDI / format " +
                        "characters (visual-spoofing risk).");
                }
                if (IsForbiddenPunctuation(c))
                {
                    return ServiceResult.Create(
                        StatusCodes.BadInvalidArgument,
                        "TD child name must not contain '{0}' (reserved " +
                        "for NodeId / browse-path syntax).",
                        c);
                }
            }
            return ServiceResult.Good;
        }

        /// <summary>
        /// Returns a string safe to surface in a log message: control
        /// characters and BIDI / format characters are replaced with
        /// <c>U+XXXX</c> placeholders so a hostile name cannot reshape
        /// the rendered log line.
        /// </summary>
        public static string SanitiseForLog(string? name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }
            int max = System.Math.Min(name!.Length, MaxLength);
            var builder = new System.Text.StringBuilder(max + 8);
            for (int i = 0; i < max; i++)
            {
                char c = name[i];
                if (char.IsControl(c) || IsBidiOrFormatChar(c))
                {
                    builder.Append('U').Append('+').Append(
                        ((int)c).ToString("X4", System.Globalization.CultureInfo.InvariantCulture));
                }
                else
                {
                    builder.Append(c);
                }
            }
            if (name.Length > MaxLength)
            {
                builder.Append("...");
            }
            return builder.ToString();
        }

        /// <summary>
        /// Reserved punctuation that would either land inside a
        /// NodeId / browse-path syntax token (<c>.</c>, <c>!</c>,
        /// <c>#</c>, <c>:</c>) or be re-interpreted as a path separator
        /// at the file-system layer (<c>/</c>, <c>\</c>).
        /// </summary>
        private static bool IsForbiddenPunctuation(char c)
        {
            return c switch
            {
                '/' or '\\' or '.' or '#' or ':' or '!' => true,
                _ => false
            };
        }

        /// <summary>
        /// Returns true for Unicode BIDI / format characters that can
        /// distort how a name renders in a text terminal or browse
        /// viewer (the classic 'rtlo' visual-spoofing vector).
        /// </summary>
        private static bool IsBidiOrFormatChar(char c)
        {
            return c switch
            {
                '\u200E' or '\u200F' => true,           // LRM / RLM
                >= '\u202A' and <= '\u202E' => true,    // LRE / RLE / PDF / LRO / RLO
                >= '\u2066' and <= '\u2069' => true,    // LRI / RLI / FSI / PDI
                _ => false
            };
        }
    }
}
