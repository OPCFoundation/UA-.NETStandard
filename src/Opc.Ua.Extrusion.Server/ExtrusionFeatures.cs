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
using System.Linq;

namespace Opc.Ua.Extrusion.Server
{
    /// <summary>
    /// Selects real optional members and the number of real placeholder instances.
    /// Paths are slash-separated standard BrowseNames, relative to the device.
    /// </summary>
    /// <remarks>
    /// Selection is checked against the loaded declarations, not a second hand-maintained model.
    /// A selected object includes its optional descendants. Placeholder counts are explicit:
    /// enabling an optional container never invents equipment, jobs or datasets.
    /// </remarks>
    public sealed class ExtrusionFeatures
    {
        /// <summary>
        /// Selects an optional member, including the optional members below it.
        /// </summary>
        public ExtrusionFeatures Include(string path)
        {
            ValidatePath(path);
            m_included.Add(path);
            return this;
        }

        /// <summary>
        /// Selects every applicable optional member, but no optional placeholder instances.
        /// </summary>
        public ExtrusionFeatures IncludeAll()
        {
            m_all = true;
            return this;
        }

        /// <summary>
        /// Excludes an optional subtree from an otherwise broader selection.
        /// Mandatory declarations cannot be excluded.
        /// </summary>
        public ExtrusionFeatures Exclude(string path)
        {
            ValidatePath(path);
            m_excluded.Add(path);
            return this;
        }

        /// <summary>
        /// Selects a placeholder and its exact cardinality, for example
        /// <c>FilterPackage_&lt;Nr&gt;/ScreenPackage_&lt;Nr&gt;</c>.
        /// Indices start at one and are formatted with three digits.
        /// </summary>
        public ExtrusionFeatures WithInstances(string declarationPath, int count)
        {
            ValidatePath(declarationPath);
            if (count is < 0 or > 999)
            {
                throw new ArgumentOutOfRangeException(nameof(count),
                    "A container supports zero through 999 instances.");
            }
            m_counts[declarationPath] = count;
            return this;
        }

        /// <summary>
        /// Takes a private selection snapshot so later caller changes cannot change a live device.
        /// </summary>
        internal ExtrusionFeatures Copy()
        {
            var result = new ExtrusionFeatures { m_all = m_all };
            result.m_included.UnionWith(m_included);
            result.m_excluded.UnionWith(m_excluded);
            foreach (KeyValuePair<string, int> pair in m_counts)
            {
                result.m_counts.Add(pair.Key, pair.Value);
            }
            return result;
        }

        /// <summary>
        /// Determines whether an optional declaration belongs to the selected graph.
        /// </summary>
        internal bool Includes(string path)
        {
            if (m_excluded.Any(excluded => IsAtOrBelow(path, excluded)))
            {
                return false;
            }
            return m_all || m_included.Any(included =>
                IsAtOrBelow(path, included) || IsAtOrBelow(included, path)) ||
                m_counts.Any(pair => pair.Value > 0 && IsAtOrBelow(pair.Key, path));
        }

        /// <summary>
        /// Limits recursive declaration inventory to the finite paths explicitly selected by the caller.
        /// </summary>
        internal bool HasDescendantSelections(string path)
        {
            string prefix = path + "/";
            return m_included.Concat(m_excluded).Concat(m_counts.Keys)
                .Any(selection => selection.StartsWith(prefix, StringComparison.Ordinal));
        }

        /// <summary>
        /// Gets an explicitly selected cardinality or the mandatory-placeholder default.
        /// </summary>
        internal int Count(string path, bool mandatory, int mandatoryDefault)
        {
            return m_counts.TryGetValue(path, out int count)
                ? count
                : mandatory ? mandatoryDefault : 0;
        }

        /// <summary>
        /// Rejects misspelled paths and attempts to hide mandatory declarations.
        /// </summary>
        internal void ValidateSelections(ISet<string> available, ISet<string> mandatory)
        {
            foreach (string path in m_included.Concat(m_excluded).Concat(m_counts.Keys))
            {
                if (!available.Contains(path))
                {
                    throw new ArgumentException($"The model does not declare extrusion feature '{path}'.");
                }
            }
            foreach (string path in m_excluded)
            {
                if (mandatory.Contains(path))
                {
                    throw new ArgumentException($"Mandatory extrusion feature '{path}' cannot be excluded.");
                }
            }
        }

        /// <summary>
        /// Checks path syntax before it enters a selection.
        /// </summary>
        private static void ValidatePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || path[0] == '/' || path[path.Length - 1] == '/' ||
                path.Contains("//", StringComparison.Ordinal))
            {
                throw new ArgumentException("A nonempty relative BrowseName path is required.", nameof(path));
            }
        }

        /// <summary>
        /// Compares complete path segments, not similarly prefixed BrowseNames.
        /// </summary>
        private static bool IsAtOrBelow(string path, string parent)
        {
            return path.Equals(parent, StringComparison.Ordinal) ||
                path.StartsWith(parent + "/", StringComparison.Ordinal);
        }

        /// <summary>The explicit optional subtree selections.</summary>
        private readonly HashSet<string> m_included = new(StringComparer.Ordinal);

        /// <summary>The explicit exclusions.</summary>
        private readonly HashSet<string> m_excluded = new(StringComparer.Ordinal);

        /// <summary>The exact configured placeholder cardinalities.</summary>
        private readonly Dictionary<string, int> m_counts = new(StringComparer.Ordinal);

        /// <summary>Whether all optional declarations are selected.</summary>
        private bool m_all;
    }
}
