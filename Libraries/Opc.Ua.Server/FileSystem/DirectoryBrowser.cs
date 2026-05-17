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

using System.Collections.Generic;
using System.Threading;

namespace Opc.Ua.Server.FileSystem
{
    /// <summary>
    /// Enumerates the immediate children (files + directories) of a
    /// <see cref="DirectoryObjectState"/> via the underlying
    /// <see cref="IFileSystemProvider"/>.
    /// </summary>
    internal sealed class DirectoryBrowser : NodeBrowser
    {
        public DirectoryBrowser(
            ISystemContext context, ViewDescription? view,
            NodeId referenceType, bool includeSubtypes, BrowseDirection browseDirection,
            QualifiedName browseName, IEnumerable<IReference>? additionalReferences,
            bool internalOnly,
            FileSystemNodeManager manager,
            DirectoryObjectState source)
            : base(context, view, referenceType, includeSubtypes, browseDirection,
                browseName, additionalReferences, internalOnly)
        {
            m_manager = manager;
            m_source = source;
            m_stage = Stage.Begin;
        }

        public override IReference? Next()
        {
            lock (DataLock)
            {
                IReference? reference = base.Next();
                if (reference != null)
                {
                    return reference;
                }

                if (InternalOnly || m_manager == null)
                {
                    return null;
                }
                if (!IsRequired(ReferenceTypeIds.HasComponent, false))
                {
                    return null;
                }

                if (m_stage == Stage.Begin)
                {
                    m_pending = LoadEntries();
                    m_stage = Stage.Children;
                }

                if (m_stage == Stage.Children)
                {
                    reference = NextChild();
                    if (reference != null)
                    {
                        return reference;
                    }
                    m_stage = Stage.Done;
                }

                return null;
            }
        }

        private List<FileSystemEntry> LoadEntries()
        {
            var list = new List<FileSystemEntry>();
            try
            {
                IAsyncEnumerator<FileSystemEntry> enumerator = m_manager.Provider
                    .EnumerateAsync(m_source.ProviderPath, CancellationToken.None)
                    .GetAsyncEnumerator(CancellationToken.None);
                try
                {
                    while (enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
                    {
                        list.Add(enumerator.Current);
                    }
                }
                finally
                {
                    enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult();
                }
            }
            catch
            {
                // Browse continues without children when the provider
                // can't enumerate (e.g. permission denied).
            }
            return list;
        }

        private NodeStateReference? NextChild()
        {
            if (m_pending == null)
            {
                return null;
            }

            // Named child requested — scan once and stop.
            if (!BrowseName.IsNull)
            {
                if (m_source.BrowseName.NamespaceIndex != BrowseName.NamespaceIndex)
                {
                    m_pending = null;
                    return null;
                }
                foreach (FileSystemEntry entry in m_pending)
                {
                    if (entry.Name == BrowseName.Name)
                    {
                        m_pending = null;
                        return CreateReference(entry);
                    }
                }
                m_pending = null;
                return null;
            }

            if (m_pending.Count == 0)
            {
                return null;
            }
            FileSystemEntry head = m_pending[0];
            m_pending.RemoveAt(0);
            return CreateReference(head);
        }

        private NodeStateReference CreateReference(FileSystemEntry entry)
        {
            NodeId targetId = entry.IsDirectory
                ? FileSystemNodeId.BuildDirectory(entry.Path, m_manager.NamespaceIndex)
                : FileSystemNodeId.BuildFile(entry.Path, m_manager.NamespaceIndex);
            return new NodeStateReference(ReferenceTypeIds.HasComponent, false, targetId);
        }

        private enum Stage
        {
            Begin,
            Children,
            Done
        }

        private readonly FileSystemNodeManager m_manager;
        private readonly DirectoryObjectState m_source;
        private List<FileSystemEntry>? m_pending;
        private Stage m_stage;
    }
}
