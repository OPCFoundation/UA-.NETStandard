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
using System.Globalization;

namespace Opc.Ua.Server.Fluent
{
    /// <summary>
    /// Resolves forward-slash separated <see cref="QualifiedName"/> browse
    /// paths against the in-memory node graph. Callers supply a delegate
    /// that locates the first segment (typically a lookup against the
    /// node manager's predefined-nodes dictionary); subsequent segments
    /// are walked using <see cref="NodeState.FindChild(ISystemContext, QualifiedName)"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Path grammar:
    /// <list type="bullet">
    ///   <item><description><c>segment ( '/' segment )*</c></description></item>
    ///   <item><description><c>segment := ( 'ns=' UInt16 ';' )? name</c></description></item>
    /// </list>
    /// Leading and trailing slashes are tolerated. A segment without an
    /// <c>ns=</c> prefix is parsed in <c>defaultNamespaceIndex</c>.
    /// </para>
    /// <para>
    /// Failures throw <see cref="ServiceResultException"/> with
    /// <see cref="StatusCodes.BadBrowseNameInvalid"/> (malformed input) or
    /// <see cref="StatusCodes.BadNodeIdUnknown"/> (path does not resolve).
    /// Resolution is deliberately strict so wiring errors surface at
    /// configuration time.
    /// </para>
    /// </remarks>
    public static class BrowsePathResolver
    {
        /// <summary>
        /// Resolves <paramref name="browsePath"/> against the supplied
        /// <paramref name="rootResolver"/> and returns the leaf node.
        /// </summary>
        /// <param name="context">System context used by FindChild.</param>
        /// <param name="browsePath">Forward-slash separated browse path.</param>
        /// <param name="defaultNamespaceIndex">
        /// Namespace index applied to segments that do not specify one.
        /// </param>
        /// <param name="rootResolver">
        /// Delegate that returns the root <see cref="NodeState"/> for a
        /// given <see cref="QualifiedName"/> (typically the predefined-nodes
        /// dictionary of a <c>CustomNodeManager2</c>).
        /// </param>
        /// <exception cref="ArgumentNullException"/>
        /// <exception cref="ServiceResultException"/>
        public static NodeState Resolve(
            ISystemContext context,
            string browsePath,
            ushort defaultNamespaceIndex,
            Func<QualifiedName, NodeState> rootResolver)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }
            if (rootResolver == null)
            {
                throw new ArgumentNullException(nameof(rootResolver));
            }

            List<QualifiedName> segments = ParseSegments(browsePath, defaultNamespaceIndex);

            NodeState current = rootResolver(segments[0]) ??
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdUnknown,
                    "Browse path '{0}' did not resolve: root segment '{1}' not found.",
                    browsePath,
                    segments[0]);

            for (int i = 1; i < segments.Count; i++)
            {
                current = current.FindChild(context, segments[i]) ??
                    throw ServiceResultException.Create(
                        StatusCodes.BadNodeIdUnknown,
                        "Browse path '{0}' did not resolve: segment '{1}' not found under '{2}'.",
                        browsePath,
                        segments[i],
                        current.BrowseName);
            }

            return current;
        }

        /// <summary>
        /// Parses <paramref name="browsePath"/> into a list of
        /// <see cref="QualifiedName"/> segments. Exposed for tests.
        /// </summary>
        /// <exception cref="ServiceResultException"/>
        public static List<QualifiedName> ParseSegments(
            string browsePath,
            ushort defaultNamespaceIndex)
        {
            if (string.IsNullOrWhiteSpace(browsePath))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameInvalid,
                    "Browse path is null or empty.");
            }

            // Tolerate leading/trailing slashes (but not just slashes).
            ReadOnlySpan<char> input = browsePath.AsSpan().Trim();
            int start = 0;
            int end = input.Length;
            if (input[0] == '/')
            {
                start = 1;
            }
            if (end > start && input[end - 1] == '/')
            {
                end--;
            }
            if (end <= start)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameInvalid,
                    "Browse path '{0}' contains no segments.",
                    browsePath);
            }

            ReadOnlySpan<char> body = input.Slice(start, end - start);
            var segments = new List<QualifiedName>(8);

            int segmentStart = 0;
            for (int i = 0; i <= body.Length; i++)
            {
                if (i == body.Length || body[i] == '/')
                {
                    int len = i - segmentStart;
                    if (len == 0)
                    {
                        throw ServiceResultException.Create(
                            StatusCodes.BadBrowseNameInvalid,
                            "Browse path '{0}' contains an empty segment.",
                            browsePath);
                    }

                    segments.Add(ParseSegment(
                        body.Slice(segmentStart, len),
                        browsePath,
                        defaultNamespaceIndex));
                    segmentStart = i + 1;
                }
            }

            return segments;
        }

        private static QualifiedName ParseSegment(
            ReadOnlySpan<char> segment,
            string fullPath,
            ushort defaultNamespaceIndex)
        {
            ushort ns = defaultNamespaceIndex;
            ReadOnlySpan<char> name = segment;

            // Optional ns=N; prefix.
            if (segment.Length > 3
                && (segment[0] == 'n' || segment[0] == 'N')
                && (segment[1] == 's' || segment[1] == 'S')
                && segment[2] == '=')
            {
                int semi = segment.Slice(3).IndexOf(';');
                if (semi <= 0)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadBrowseNameInvalid,
                        "Browse path '{0}' has malformed namespace prefix in segment '{1}'.",
                        fullPath,
                        segment.ToString());
                }

                ReadOnlySpan<char> nsText = segment.Slice(3, semi);
                if (!ushort.TryParse(
                    nsText.ToString(),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out ns))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadBrowseNameInvalid,
                        "Browse path '{0}': namespace index '{1}' is not a valid UInt16.",
                        fullPath,
                        nsText.ToString());
                }

                name = segment.Slice(3 + semi + 1);
            }

            if (name.IsEmpty)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadBrowseNameInvalid,
                    "Browse path '{0}' has an empty browse name in segment '{1}'.",
                    fullPath,
                    segment.ToString());
            }

            return new QualifiedName(name.ToString(), ns);
        }
    }
}
