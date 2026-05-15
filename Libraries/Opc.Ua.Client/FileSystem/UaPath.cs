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
using System.Collections.Generic;
using System.Text;

namespace Opc.Ua.Client.FileSystem
{
    /// <summary>
    /// <see cref="System.IO.Path"/>-equivalent helpers for the OPC UA
    /// FileSystem client. Paths use the forward slash <c>'/'</c> as
    /// separator and each segment is parsed via
    /// <see cref="QualifiedName.Parse(string)"/>, so the
    /// <c>"&lt;ns&gt;:&lt;name&gt;"</c> form is supported (with the
    /// <c>&lt;ns&gt;:</c> prefix omitted when the namespace index is
    /// zero).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The empty string and <c>"/"</c> both denote the root directory.
    /// Leading and trailing separators are tolerated and trimmed during
    /// parsing. Internal duplicate separators (<c>"a//b"</c>) collapse
    /// into a single one and an empty middle segment is rejected.
    /// </para>
    /// <para>
    /// Path comparisons are <em>namespace aware</em>: siblings
    /// <c>"1:foo"</c> and <c>"2:foo"</c> are different paths and produce
    /// different cache keys, even though their <see cref="QualifiedName.Name"/>
    /// is identical.
    /// </para>
    /// </remarks>
    public static class UaPath
    {
        /// <summary>
        /// The path separator character used by <see cref="UaPath"/>.
        /// </summary>
        public const char Separator = '/';

        /// <summary>
        /// Returns the canonical string form of the root path
        /// (a single <c>'/'</c>).
        /// </summary>
        public const string Root = "/";

        /// <summary>
        /// Splits <paramref name="path"/> into its <see cref="QualifiedName"/>
        /// segments. The empty string and <c>"/"</c> both yield an empty
        /// array. Leading and trailing slashes are tolerated; embedded
        /// empty segments throw <see cref="ArgumentException"/>.
        /// </summary>
        /// <param name="path">A forward-slash separated path.</param>
        /// <returns>The parsed path segments.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="path"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="path"/> contains an empty middle segment
        /// (e.g. <c>"a//b"</c>) or a segment whose namespace prefix is
        /// not a valid <see cref="ushort"/>.
        /// </exception>
        public static QualifiedName[] Parse(string path)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            ReadOnlySpan<char> remaining = path.AsSpan();

            // Trim a single leading / trailing slash; multiple are illegal.
            if (remaining.Length > 0 && remaining[0] == Separator)
            {
                remaining = remaining[1..];
            }
            if (remaining.Length > 0 && remaining[^1] == Separator)
            {
                remaining = remaining[..^1];
            }

            if (remaining.Length == 0)
            {
                return [];
            }

            var segments = new List<QualifiedName>();
            int start = 0;
            for (int i = 0; i <= remaining.Length; i++)
            {
                if (i == remaining.Length || remaining[i] == Separator)
                {
                    if (i == start)
                    {
                        throw new ArgumentException(
                            $"Invalid path '{path}': empty segment.",
                            nameof(path));
                    }
                    string segment = remaining[start..i].ToString();
                    segments.Add(ParseSegment(segment, path));
                    start = i + 1;
                }
            }

            return [.. segments];
        }

        /// <summary>
        /// Returns the canonical string form of <paramref name="segments"/>.
        /// Always begins with <c>'/'</c>. The root produces
        /// <c>"/"</c>. Each segment is rendered via
        /// <see cref="FormatSegment(QualifiedName)"/> so the namespace
        /// index is always preserved.
        /// </summary>
        /// <param name="segments">The path segments.</param>
        /// <returns>A canonical, namespace-aware string form.</returns>
        public static string Format(IReadOnlyList<QualifiedName> segments)
        {
            if (segments == null || segments.Count == 0)
            {
                return Root;
            }
            var sb = new StringBuilder();
            for (int i = 0; i < segments.Count; i++)
            {
                sb.Append(Separator).Append(FormatSegment(segments[i]));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Returns the canonical string form of a single segment as it
        /// appears in a path (e.g. <c>"1:foo"</c> for a non-zero
        /// namespace index, <c>"foo"</c> for namespace zero).
        /// </summary>
        /// <param name="segment">The segment.</param>
        /// <returns>The formatted segment.</returns>
        /// <exception cref="ArgumentException">
        /// <paramref name="segment"/> has a null or empty
        /// <see cref="QualifiedName.Name"/>.
        /// </exception>
        public static string FormatSegment(QualifiedName segment)
        {
            if (string.IsNullOrEmpty(segment.Name))
            {
                throw new ArgumentException(
                    "Path segment must have a non-empty Name.",
                    nameof(segment));
            }
            if (segment.NamespaceIndex == 0)
            {
                return segment.Name;
            }
            return $"{segment.NamespaceIndex}:{segment.Name}";
        }

        /// <summary>
        /// Combines two paths in the manner of
        /// <see cref="System.IO.Path.Combine(string, string)"/>: if
        /// <paramref name="right"/> begins with <c>'/'</c> it is
        /// returned unchanged; otherwise the two are joined by exactly
        /// one separator.
        /// </summary>
        /// <param name="left">The base path; may be <c>null</c> or empty
        /// to mean the root.</param>
        /// <param name="right">The relative or absolute path.</param>
        /// <returns>The combined path.</returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="right"/> is <c>null</c>.
        /// </exception>
        public static string Combine(string left, string right)
        {
            if (right == null)
            {
                throw new ArgumentNullException(nameof(right));
            }

            if (right.Length > 0 && right[0] == Separator)
            {
                return Normalize(right);
            }

            QualifiedName[] leftSegments = Parse(left ?? string.Empty);
            QualifiedName[] rightSegments = Parse(right);

            if (rightSegments.Length == 0)
            {
                return Format(leftSegments);
            }

            var combined = new List<QualifiedName>(leftSegments.Length + rightSegments.Length);
            combined.AddRange(leftSegments);
            combined.AddRange(rightSegments);
            return Format(combined);
        }

        /// <summary>
        /// Returns the parent directory of <paramref name="path"/>, or
        /// <c>null</c> if <paramref name="path"/> is the root or empty.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>The directory portion in canonical form, or
        /// <c>null</c>.</returns>
        public static string? GetDirectoryName(string path)
        {
            QualifiedName[] segments = Parse(path);
            if (segments.Length == 0)
            {
                return null;
            }
            if (segments.Length == 1)
            {
                return Root;
            }
            var parent = new QualifiedName[segments.Length - 1];
            Array.Copy(segments, parent, parent.Length);
            return Format(parent);
        }

        /// <summary>
        /// Returns the leaf segment of <paramref name="path"/> as a
        /// <see cref="QualifiedName"/>, or
        /// <see cref="QualifiedName.Null"/> when the path is the root.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>The leaf segment.</returns>
        public static QualifiedName GetFileName(string path)
        {
            QualifiedName[] segments = Parse(path);
            if (segments.Length == 0)
            {
                return QualifiedName.Null;
            }
            return segments[^1];
        }

        /// <summary>
        /// Returns the canonical string form of <paramref name="path"/>.
        /// </summary>
        /// <param name="path">The path.</param>
        /// <returns>The canonicalised path string.</returns>
        public static string Normalize(string path)
        {
            return Format(Parse(path));
        }

        private static QualifiedName ParseSegment(string segment, string fullPath)
        {
            int colon = segment.IndexOf(':', StringComparison.Ordinal);
            if (colon < 0)
            {
                return new QualifiedName(segment);
            }
            if (colon == 0 || colon == segment.Length - 1)
            {
                throw new ArgumentException(
                    $"Invalid path '{fullPath}': segment '{segment}' has an empty namespace prefix or empty name.",
                    nameof(fullPath));
            }
            string nsToken = segment[..colon];
            if (!ushort.TryParse(nsToken, out ushort ns))
            {
                throw new ArgumentException(
                    $"Invalid path '{fullPath}': segment '{segment}' has a non-numeric namespace prefix.",
                    nameof(fullPath));
            }
            return new QualifiedName(segment[(colon + 1)..], ns);
        }
    }
}
