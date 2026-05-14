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
    using Opc.Ua.Server;
    using System.IO;
    using System.Text;

    /// <summary>
    /// A class that builds NodeIds used by the FileSystem NodeManager
    /// </summary>
    public static class ModelUtils
    {
        /// <summary>
        /// The RootType for a Volume node identfier.
        /// </summary>
        public const int Volume = 0;

        /// <summary>
        /// The RootType for a Directory node identfier.
        /// </summary>
        public const int Directory = 1;

        /// <summary>
        /// The RootType for a File node identfier.
        /// </summary>
        public const int File = 2;

        /// <summary>
        /// Create id for drive
        /// </summary>
        public static NodeId ConstructIdForVolume(string path, ushort namespaceIndex)
            => new FileSystemNodeId(Volume, path, namespaceIndex).ToNodeId();

        /// <summary>
        /// Constructs a NodeId for a directory.
        /// </summary>
        public static NodeId ConstructIdForDirectory(string path, ushort namespaceIndex)
            => new FileSystemNodeId(Directory, path, namespaceIndex).ToNodeId();

        /// <summary>
        /// Constructs a NodeId for a file.
        /// </summary>
        public static NodeId ConstructIdForFile(string path, ushort namespaceIndex)
            => new FileSystemNodeId(File, path, namespaceIndex).ToNodeId();

        public static string GetName(string path)
        {
            string name = Path.GetFileName(path);
            if (string.IsNullOrEmpty(name))
            {
                return path;
            }
            return name;
        }

        /// <summary>
        /// Constructs the node identifier for a component.
        /// </summary>
        public static NodeId ConstructIdForComponent(NodeState component, ushort namespaceIndex)
        {
            if (component == null)
            {
                return NodeId.Null;
            }

            // components must be instances with a parent.
            if (!(component is BaseInstanceState instance) || instance.Parent == null)
            {
                return component.NodeId;
            }

            // parent must have a string identifier.
            if (!instance.Parent.NodeId.TryGetValue(out string parentId))
            {
                return NodeId.Null;
            }

            var buffer = new StringBuilder();
            buffer.Append(parentId);

            // check if the parent is another component.
            int index = parentId.IndexOf('?');

            if (index < 0)
            {
                buffer.Append('?');
            }
            else
            {
                buffer.Append('/');
            }

            buffer.Append(component.SymbolicName);

            // return the node identifier.
            return new NodeId(buffer.ToString(), namespaceIndex);
        }
    }
}
