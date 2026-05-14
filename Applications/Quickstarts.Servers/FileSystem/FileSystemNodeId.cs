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

namespace Quickstarts.FileSystem
{
    using Opc.Ua;
    using System.Text;

    /// <summary>
    /// Stores the elements of a string-encoded NodeId used by the FileSystem
    /// NodeManager. Replaces the obsolete <c>Opc.Ua.Server.ParsedNodeId</c>.
    /// </summary>
    /// <remarks>
    /// The wire format is: <c>&lt;rootType&gt;:&lt;rootId&gt;[?&lt;componentPath&gt;]</c>.
    /// <list type="bullet">
    /// <item><c>rootType</c> is an integer; FileSystem uses 0 = Volume, 1 = Directory, 2 = File.</item>
    /// <item><c>rootId</c> is the path string. Any literal <c>&amp;</c> or <c>?</c> in the path is escaped with a leading <c>&amp;</c>.</item>
    /// <item><c>componentPath</c> is an optional <c>/</c>-separated list of <c>SymbolicName</c> values identifying a child node.</item>
    /// </list>
    /// Examples:
    /// <list type="bullet">
    /// <item><c>0:C:\</c> — the C: volume</item>
    /// <item><c>1:C:\Windows</c> — a directory</item>
    /// <item><c>2:C:\file.txt?Open</c> — the Open method on a file</item>
    /// <item><c>2:C:\file.txt?Size</c> — the Size property on a file</item>
    /// </list>
    /// </remarks>
    internal readonly struct FileSystemNodeId
    {
        /// <summary>
        /// Creates a new <see cref="FileSystemNodeId"/>.
        /// </summary>
        /// <param name="rootType">The root-node type discriminator (0 = Volume, 1 = Directory, 2 = File).</param>
        /// <param name="rootId">The root path identifier.</param>
        /// <param name="namespaceIndex">The namespace index of the resulting NodeId.</param>
        /// <param name="componentPath">Optional <c>/</c>-separated child symbolic-name path.</param>
        public FileSystemNodeId(int rootType, string rootId, ushort namespaceIndex,
            string componentPath = null)
        {
            RootType = rootType;
            RootId = rootId;
            NamespaceIndex = namespaceIndex;
            ComponentPath = componentPath;
        }

        /// <summary>
        /// The root-node type discriminator. FileSystem uses 0 = Volume, 1 = Directory, 2 = File.
        /// </summary>
        public int RootType { get; }

        /// <summary>
        /// The root path identifier (volume name, full directory path, or full file path).
        /// </summary>
        public string RootId { get; }

        /// <summary>
        /// Optional <c>/</c>-separated symbolic-name chain identifying a child component.
        /// </summary>
        public string ComponentPath { get; }

        /// <summary>
        /// The namespace index of the original NodeId.
        /// </summary>
        public ushort NamespaceIndex { get; }

        /// <summary>
        /// Attempts to parse the given <see cref="NodeId"/> in the FileSystem
        /// wire format.
        /// </summary>
        /// <param name="nodeId">The NodeId to parse. Only string-identifier NodeIds with the
        /// <c>&lt;rootType&gt;:&lt;rootId&gt;[?&lt;componentPath&gt;]</c> shape are accepted.</param>
        /// <param name="result">On success, the parsed result. On failure, <see langword="default"/>.</param>
        /// <returns><see langword="true"/> when the NodeId could be parsed; otherwise <see langword="false"/>.</returns>
        public static bool TryParse(NodeId nodeId, out FileSystemNodeId result)
        {
            result = default;

            // can only parse non-null string node identifiers.
            if (nodeId.IsNull)
            {
                return false;
            }

            if (!nodeId.TryGetValue(out string identifier) ||
                string.IsNullOrEmpty(identifier))
            {
                return false;
            }

            // extract the root type prefix (leading run of decimal digits).
            int rootType = 0;
            int start = 0;

            for (int ii = 0; ii < identifier.Length; ii++)
            {
                if (!char.IsDigit(identifier[ii]))
                {
                    start = ii;
                    break;
                }

                rootType *= 10;
                rootType += identifier[ii] - '0';
            }

            if (start >= identifier.Length || identifier[start] != ':')
            {
                return false;
            }

            // extract the rootId (with & escapes), terminated by an unescaped ?.
            var buffer = new StringBuilder();

            int index = start + 1;
            int end = identifier.Length;
            bool escaped = false;

            while (index < end)
            {
                char ch = identifier[index++];

                // skip any escape character but keep the one after it.
                if (ch == '&')
                {
                    escaped = true;
                    continue;
                }

                if (!escaped && ch == '?')
                {
                    end = index;
                    break;
                }

                buffer.Append(ch);
                escaped = false;
            }

            string componentPath = null;
            if (end < identifier.Length)
            {
                componentPath = identifier.Substring(end);
            }

            result = new FileSystemNodeId(
                rootType,
                buffer.ToString(),
                nodeId.NamespaceIndex,
                componentPath);

            return true;
        }

        /// <summary>
        /// Renders this struct back to a <see cref="NodeId"/>.
        /// </summary>
        public NodeId ToNodeId() => ToNodeId(componentName: null);

        /// <summary>
        /// Renders this struct back to a <see cref="NodeId"/>, appending an
        /// additional child component name to the existing <see cref="ComponentPath"/>.
        /// </summary>
        /// <param name="componentName">Symbolic name of an additional child to append.
        /// If <see langword="null"/> or empty, only the existing root + component path is rendered.</param>
        public NodeId ToNodeId(string componentName)
        {
            var buffer = new StringBuilder();

            // write the root type prefix.
            buffer.Append(RootType).Append(':');

            // write the root id, escaping & and ?.
            if (RootId != null)
            {
                for (int ii = 0; ii < RootId.Length; ii++)
                {
                    char ch = RootId[ii];

                    if (ch is '&' or '?')
                    {
                        buffer.Append('&');
                    }

                    buffer.Append(ch);
                }
            }

            // append the existing component path, if any.
            if (!string.IsNullOrEmpty(ComponentPath))
            {
                buffer.Append('?').Append(ComponentPath);
            }

            // append the new component name, if any.
            if (!string.IsNullOrEmpty(componentName))
            {
                if (string.IsNullOrEmpty(ComponentPath))
                {
                    buffer.Append('?');
                }
                else
                {
                    buffer.Append('/');
                }

                buffer.Append(componentName);
            }

            return new NodeId(buffer.ToString(), NamespaceIndex);
        }
    }
}
