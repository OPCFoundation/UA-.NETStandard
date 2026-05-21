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
using System.Text;

namespace Opc.Ua.Server.FileSystem
{
    /// <summary>
    /// Encodes / decodes the string-identifier shape used by
    /// <see cref="FileSystemNodeManager"/> for files, directories,
    /// and component nodes hung off them.
    /// </summary>
    /// <remarks>
    /// Wire format:
    /// <c>&lt;rootType&gt;:&lt;providerPath&gt;[?&lt;componentPath&gt;]</c>
    /// <list type="bullet">
    /// <item><c>rootType</c> is <c>0</c> for the mount root,
    /// <c>1</c> for a directory, <c>2</c> for a file.</item>
    /// <item><c>providerPath</c> is the provider-relative path
    /// (forward-slash separated). Any literal <c>&amp;</c> or
    /// <c>?</c> in the path is escaped with a leading <c>&amp;</c>.</item>
    /// <item><c>componentPath</c> is an optional <c>/</c>-separated
    /// list of <c>SymbolicName</c> values for a child node hung off
    /// the root (e.g. <c>"Open"</c>, <c>"Size"</c>).</item>
    /// </list>
    /// </remarks>
    internal readonly struct FileSystemNodeId
    {
        /// <summary>Root identifier (mount point).</summary>
        public const int Root = 0;

        /// <summary>Directory identifier.</summary>
        public const int Directory = 1;

        /// <summary>File identifier.</summary>
        public const int File = 2;

        public FileSystemNodeId(
            int rootType,
            string providerPath,
            ushort namespaceIndex,
            string? componentPath = null)
        {
            RootType = rootType;
            ProviderPath = providerPath ?? string.Empty;
            NamespaceIndex = namespaceIndex;
            ComponentPath = componentPath;
        }

        /// <summary>0 = root, 1 = directory, 2 = file.</summary>
        public int RootType { get; }

        /// <summary>Provider-relative path (empty for the root).</summary>
        public string ProviderPath { get; }

        /// <summary>Optional component-name chain.</summary>
        public string? ComponentPath { get; }

        /// <summary>Namespace index of the encoded NodeId.</summary>
        public ushort NamespaceIndex { get; }

        public static NodeId BuildRoot(ushort namespaceIndex)
            => new FileSystemNodeId(Root, string.Empty, namespaceIndex).ToNodeId();

        public static NodeId BuildDirectory(string providerPath, ushort namespaceIndex)
            => new FileSystemNodeId(Directory, providerPath, namespaceIndex).ToNodeId();

        public static NodeId BuildFile(string providerPath, ushort namespaceIndex)
            => new FileSystemNodeId(File, providerPath, namespaceIndex).ToNodeId();

        /// <summary>
        /// Attempts to parse the given <see cref="NodeId"/> into the
        /// FileSystem wire format.
        /// </summary>
        public static bool TryParse(NodeId nodeId, out FileSystemNodeId result)
        {
            result = default;

            if (nodeId.IsNull ||
                !nodeId.TryGetValue(out string? identifier) ||
                string.IsNullOrEmpty(identifier))
            {
                return false;
            }

            int rootType = 0;
            int start = -1;
            for (int ii = 0; ii < identifier.Length; ii++)
            {
                if (!char.IsDigit(identifier[ii]))
                {
                    start = ii;
                    break;
                }
                rootType = (rootType * 10) + (identifier[ii] - '0');
            }

            if (start < 0 || start >= identifier.Length || identifier[start] != ':')
            {
                return false;
            }

            // Read provider path with & escapes; an unescaped ?
            // terminates and starts the component path.
            var buffer = new StringBuilder();
            int index = start + 1;
            int end = identifier.Length;
            bool escaped = false;

            while (index < end)
            {
                char ch = identifier[index++];
                if (!escaped && ch == '&')
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

            string? componentPath = end < identifier.Length
                ? identifier[end..]
                : null;

            result = new FileSystemNodeId(
                rootType,
                buffer.ToString(),
                nodeId.NamespaceIndex,
                componentPath);
            return true;
        }

        /// <summary>
        /// Returns this struct as a <see cref="NodeId"/> with the
        /// existing root + component path.
        /// </summary>
        public NodeId ToNodeId() => ToNodeId(componentName: null);

        /// <summary>
        /// Returns this struct as a <see cref="NodeId"/>, optionally
        /// appending an additional child component name to the
        /// existing <see cref="ComponentPath"/>.
        /// </summary>
        public NodeId ToNodeId(string? componentName)
        {
            var buffer = new StringBuilder();
            buffer.Append(RootType).Append(':');

            for (int ii = 0; ii < ProviderPath.Length; ii++)
            {
                char ch = ProviderPath[ii];
                if (ch is '&' or '?')
                {
                    buffer.Append('&');
                }
                buffer.Append(ch);
            }

            if (!string.IsNullOrEmpty(ComponentPath))
            {
                buffer.Append('?').Append(ComponentPath);
            }
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

        /// <summary>
        /// Encodes a NodeId for an arbitrary child component of the
        /// supplied parent node. Mirrors the old
        /// <c>ModelUtils.ConstructIdForComponent</c> helper.
        /// </summary>
        public static NodeId ConstructIdForComponent(
            NodeState component,
            ushort namespaceIndex)
        {
            if (component == null)
            {
                return NodeId.Null;
            }
            if (component is not BaseInstanceState instance ||
                instance.Parent == null ||
                !instance.Parent.NodeId.TryGetValue(out string? parentId))
            {
                return component.NodeId;
            }

            var buffer = new StringBuilder();
            buffer.Append(parentId);
            buffer.Append(parentId.IndexOf('?', StringComparison.Ordinal) < 0 ? '?' : '/');
            buffer.Append(component.SymbolicName);
            return new NodeId(buffer.ToString(), namespaceIndex);
        }
    }
}
