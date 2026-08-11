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
using System.Globalization;
using System.Text;

namespace Opc.Ua.Aas
{
    /// <summary>
    /// The metamodel's own path convention: short names joined by <c>.</c>,
    /// with <c>[n]</c> for a member of a list.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the path the AAS API of IDTA-01002 Part 2 addresses an element
    /// by, and clause 6.1.3 derives the element's String NodeId from it, so a
    /// Client that holds an <c>idShortPath</c> reaches the same node a Client
    /// that holds a NodeId reaches.
    /// </para>
    /// <para>
    /// An <c>OperationVariable</c> wrapper is not a node. Its value element
    /// has the path <c>&lt;operation-path&gt;.&lt;role&gt;[&lt;index&gt;]</c>,
    /// where the role and index rather than the element's own short name
    /// identify its containment position.
    /// </para>
    /// </remarks>
    public static class AasIdShortPath
    {
        /// <summary>
        /// Appends a named child segment to a parent path.
        /// </summary>
        /// <param name="parentPath">The parent's path, or an empty string for a submodel's direct child.</param>
        /// <param name="idShort">The child's short name.</param>
        /// <returns>The child's path.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="parentPath"/> or <paramref name="idShort"/> is <c>null</c>.</exception>
        public static string AppendName(string parentPath, string idShort)
        {
            if (parentPath is null)
            {
                throw new ArgumentNullException(nameof(parentPath));
            }

            if (idShort is null)
            {
                throw new ArgumentNullException(nameof(idShort));
            }


            return parentPath.Length == 0 ? idShort : string.Concat(parentPath, ".", idShort);
        }

        /// <summary>
        /// Appends a list-member segment to a parent path.
        /// </summary>
        /// <remarks>
        /// A member of a <c>SubmodelElementList</c> has no short name — the
        /// metamodel does not give it one — so it is addressed by index.
        /// </remarks>
        /// <param name="parentPath">The list's path.</param>
        /// <param name="index">The member's zero-based position.</param>
        /// <returns>The member's path.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="parentPath"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative.</exception>
        public static string AppendIndex(string parentPath, int index)
        {
            if (parentPath is null)
            {
                throw new ArgumentNullException(nameof(parentPath));
            }

            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }


            return string.Concat(
                parentPath,
                "[",
                index.ToString(CultureInfo.InvariantCulture),
                "]");
        }

        /// <summary>
        /// Appends the segment of one <c>OperationVariable</c> value element.
        /// </summary>
        /// <param name="operationPath">The owning operation's path.</param>
        /// <param name="role">The role array the variable belongs to.</param>
        /// <param name="index">The variable's zero-based position within that role.</param>
        /// <returns>The value element's path.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="operationPath"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="index"/> is negative.</exception>
        public static string AppendOperationVariable(
            string operationPath,
            AasOperationVariableRole role,
            int index)
        {
            if (operationPath is null)
            {
                throw new ArgumentNullException(nameof(operationPath));
            }

            if (index < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }


            return AppendIndex(AppendName(operationPath, NameOf(role)), index);
        }

        /// <summary>
        /// Returns the exact metamodel field name of an operation variable role.
        /// </summary>
        /// <param name="role">The role.</param>
        /// <returns><c>inputVariables</c>, <c>outputVariables</c> or <c>inoutputVariables</c>.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="role"/> is not a defined value.</exception>
        public static string NameOf(AasOperationVariableRole role)
        {
            return role switch
            {
                AasOperationVariableRole.Input => "inputVariables",
                AasOperationVariableRole.Output => "outputVariables",
                AasOperationVariableRole.Inoutput => "inoutputVariables",
                _ => throw new ArgumentOutOfRangeException(nameof(role))
            };
        }

        /// <summary>
        /// Resolves an operation variable role from its metamodel field name.
        /// </summary>
        /// <param name="name">The field name.</param>
        /// <param name="role">The role when the return value is <c>true</c>.</param>
        /// <returns><c>true</c> when <paramref name="name"/> names a role.</returns>
        public static bool TryParseRole(string? name, out AasOperationVariableRole role)
        {
            switch (name)
            {
                case "inputVariables":
                    role = AasOperationVariableRole.Input;
                    return true;
                case "outputVariables":
                    role = AasOperationVariableRole.Output;
                    return true;
                case "inoutputVariables":
                    role = AasOperationVariableRole.Inoutput;
                    return true;
                default:
                    role = default;
                    return false;
            }
        }

        /// <summary>
        /// Splits a path into its segments.
        /// </summary>
        /// <remarks>
        /// A short name may itself contain <c>[</c>, <c>]</c> or <c>.</c> in
        /// principle, so parsing is only sound for a path this class produced
        /// from AAS-conformant short names. Where a segment is malformed the
        /// method reports failure rather than guessing.
        /// </remarks>
        /// <param name="path">The path.</param>
        /// <param name="segments">The segments when the return value is <c>true</c>.</param>
        /// <returns><c>true</c> when the path parses.</returns>
        public static bool TryParse(
            string? path,
            out IReadOnlyList<AasIdShortPathSegment> segments)
        {
            segments = Array.Empty<AasIdShortPathSegment>();

            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            var parsed = new List<AasIdShortPathSegment>();
            var name = new StringBuilder();

            for (int i = 0; i < path.Length; i++)
            {
                char c = path[i];

                if (c == '.')
                {
                    if (name.Length == 0)
                    {
                        return false;
                    }

                    parsed.Add(AasIdShortPathSegment.ForName(name.ToString()));
                    name.Clear();
                    continue;
                }

                if (c != '[')
                {
                    name.Append(c);
                    continue;
                }

                if (name.Length > 0)
                {
                    parsed.Add(AasIdShortPathSegment.ForName(name.ToString()));
                    name.Clear();
                }
                else if (parsed.Count == 0)
                {
                    return false;
                }

                int close = path.IndexOf(']', i + 1);
                if (close < 0)
                {
                    return false;
                }

                string digits = path.Substring(i + 1, close - i - 1);
                if (digits.Length == 0 ||
                    (digits.Length > 1 && digits[0] == '0') ||
                    !int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int index))
                {
                    return false;
                }

                parsed.Add(AasIdShortPathSegment.ForIndex(index));
                i = close;

                if (i + 1 < path.Length && path[i + 1] == '.')
                {
                    i++;
                }
            }

            if (name.Length > 0)
            {
                parsed.Add(AasIdShortPathSegment.ForName(name.ToString()));
            }

            if (parsed.Count == 0)
            {
                return false;
            }

            segments = parsed;
            return true;
        }
    }
}
