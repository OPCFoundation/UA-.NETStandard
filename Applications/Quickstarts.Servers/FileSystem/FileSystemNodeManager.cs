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
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;

    /// <summary>
    /// A node manager for a server that exposes the host file system
    /// (drives, directories, and files) under the Server object using the
    /// FileDirectoryType / FileType companion model from Part 5.
    /// </summary>
    public class FileSystemNodeManager : CustomNodeManager2
    {
        /// <summary>
        /// Initializes the node manager.
        /// </summary>
        public FileSystemNodeManager(IServerInternal server, ApplicationConfiguration configuration) :
            base(server, configuration, Namespaces.FileSystem)
        {
            SystemContext.SystemHandle = m_system = new FileSystem();
            SystemContext.NodeIdFactory = this;

            var namespaceUris = new List<string> {
                Namespaces.FileSystem
            };
            NamespaceUris = namespaceUris;

            m_namespaceIndex = Server.NamespaceUris.GetIndexOrAppend(namespaceUris[0]);
            // get the configuration for the node manager.
            // use suitable defaults if no configuration exists.
            m_configuration = new FileSystemServerConfiguration();
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_system.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Creates the NodeId for the specified node.
        /// </summary>
        public override NodeId New(ISystemContext context, NodeState node)
        {
            return ModelUtils.ConstructIdForComponent(node, NamespaceIndex);
        }

        /// <summary>
        /// Does any initialization required before the address space can be used.
        /// </summary>
        public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
        {
            lock (Lock)
            {
                // find the top level segments and link them to the Server folder.
                foreach (DriveInfo fs in DriveInfo.GetDrives())
                {
                    if (!externalReferences.TryGetValue(ObjectIds.Server, out IList<IReference> references))
                    {
                        externalReferences[ObjectIds.Server] = references = new List<IReference>();
                    }

                    // construct the NodeId of a segment.
                    NodeId fsId = ModelUtils.ConstructIdForVolume(fs.Name, m_namespaceIndex);

                    // add an organizes reference from the server to the volume.
                    references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, fsId));
                }
            }
        }

        /// <summary>
        /// Frees any resources allocated for the address space.
        /// </summary>
        public override void DeleteAddressSpace()
        {
            lock (Lock)
            {
            }
        }

        /// <summary>
        /// Returns a unique handle for the node.
        /// </summary>
        protected override NodeHandle GetManagerHandle(ServerSystemContext context, NodeId nodeId,
            IDictionary<NodeId, NodeState> cache)
        {
            lock (Lock)
            {
                // quickly exclude nodes that are not in the namespace.
                if (!IsNodeIdInNamespace(nodeId))
                {
                    return null;
                }

                if (nodeId.IdType != IdType.String && PredefinedNodes.TryGetValue(nodeId, out NodeState node))
                {
                    return new NodeHandle
                    {
                        NodeId = nodeId,
                        Node = node,
                        Validated = true
                    };
                }

                // parse the identifier.
                if (FileSystemNodeId.TryParse(nodeId, out FileSystemNodeId parsedNodeId))
                {
                    return new NodeHandle
                    {
                        NodeId = nodeId,
                        Validated = false,
                        Node = null,
                        ParsedNodeId = parsedNodeId
                    };
                }

                return null;
            }
        }

        /// <summary>
        /// Verifies that the specified node exists.
        /// </summary>
        protected override NodeState ValidateNode(ServerSystemContext context,
            NodeHandle handle, IDictionary<NodeId, NodeState> cache)
        {
            // not valid if no root.
            if (handle == null)
            {
                return null;
            }

            // check if previously validated.
            if (handle.Validated)
            {
                return handle.Node;
            }

            NodeState target = null;

            // check if already in the cache.
            if (cache != null)
            {
                if (cache.TryGetValue(handle.NodeId, out target))
                {
                    // nulls mean a NodeId which was previously found to be invalid has been referenced again.
                    if (target == null)
                    {
                        return null;
                    }

                    handle.Node = target;
                    handle.Validated = true;
                    return handle.Node;
                }

                target = null;
            }

            try
            {
                // check if the node id has been parsed.
                if (handle.ParsedNodeId is not FileSystemNodeId parsedNodeId)
                {
                    return null;
                }

                NodeState root = null;

                // Validate drive
                if (parsedNodeId.RootType == ModelUtils.Volume)
                {
                    DriveInfo volume = DriveInfo.GetDrives().FirstOrDefault(d => d.Name == parsedNodeId.RootId);

                    // volume does not exist.
                    if (volume == null)
                    {
                        return null;
                    }

                    NodeId rootId = ModelUtils.ConstructIdForVolume(volume.Name, m_namespaceIndex);

                    // create a temporary object to use for the operation.
#pragma warning disable CA2000 // Dispose objects before losing scope
                    root = new DirectoryObjectState(context, rootId, volume.Name, true);
#pragma warning restore CA2000 // Dispose objects before losing scope
                }
                // Validate directory
                else if (parsedNodeId.RootType == ModelUtils.Directory)
                {
                    if (!Directory.Exists(parsedNodeId.RootId))
                    {
                        return null;
                    }

                    NodeId rootId = ModelUtils.ConstructIdForDirectory(parsedNodeId.RootId, m_namespaceIndex);

#pragma warning disable CA2000 // Dispose objects before losing scope
                    root = new DirectoryObjectState(context, rootId, parsedNodeId.RootId, false);
#pragma warning restore CA2000 // Dispose objects before losing scope
                }
                // Validate file
                else if (parsedNodeId.RootType == ModelUtils.File)
                {
                    if (!File.Exists(parsedNodeId.RootId))
                    {
                        return null;
                    }

                    NodeId rootId = ModelUtils.ConstructIdForFile(parsedNodeId.RootId, m_namespaceIndex);

#pragma warning disable CA2000 // Dispose objects before losing scope
                    root = new FileObjectState(context, rootId, parsedNodeId.RootId);
#pragma warning restore CA2000 // Dispose objects before losing scope
                }
                // unknown root type.
                else
                {
                    return null;
                }

                // all done if no components to validate.
                if (string.IsNullOrEmpty(parsedNodeId.ComponentPath))
                {
                    handle.Validated = true;
                    handle.Node = target = root;
                    return handle.Node;
                }

                // validate component.
                NodeState component = root.FindChildBySymbolicName(context, parsedNodeId.ComponentPath);

                // component does not exist.
                if (component == null)
                {
                    return null;
                }

                // found a valid component.
                handle.Validated = true;
                handle.Node = target = component;
                return handle.Node;
            }
            finally
            {
                // store the node in the cache to optimize subsequent lookups.
                cache?.Add(handle.NodeId, target);
            }
        }

        private readonly ushort m_namespaceIndex;

#pragma warning disable IDE0052 // Remove unread private members
        private readonly FileSystemServerConfiguration m_configuration;
        private readonly FileSystem m_system;
#pragma warning restore IDE0052 // Remove unread private members
    }
}
