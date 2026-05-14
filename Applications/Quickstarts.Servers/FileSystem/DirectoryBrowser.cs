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
    using System.Collections.Generic;
    using System.IO;

    /// <summary>
    /// Browses the file system folder and files
    /// </summary>
    public class DirectoryBrowser : NodeBrowser
    {
        /// <summary>
        /// Create browser
        /// </summary>
        public DirectoryBrowser(ISystemContext context, ViewDescription view,
            NodeId referenceType, bool includeSubtypes, BrowseDirection browseDirection,
            QualifiedName browseName, IEnumerable<IReference> additionalReferences,
            bool internalOnly, DirectoryObjectState source)
            : base(context, view, referenceType, includeSubtypes, browseDirection,
                browseName, additionalReferences, internalOnly)
        {
            m_source = source;
            m_stage = Stage.Begin;
        }

        /// <summary>
        /// Returns the next reference.
        /// </summary>
        public override IReference Next()
        {
            lock (DataLock)
            {
                // enumerate pre-defined references.
                IReference reference = base.Next();

                if (reference != null)
                {
                    return reference;
                }

                // don't start browsing huge number of references when only internal references are requested.
                if (InternalOnly)
                {
                    return null;
                }

                if (!IsRequired(ReferenceTypeIds.HasComponent, false))
                {
                    return null;
                }

                if (m_stage == Stage.Begin)
                {
                    string[] dirs;
                    string[] files;
                    try
                    {
                        dirs = Directory.GetDirectories(m_source.FullPath);
                    }
                    catch
                    {
                        dirs = System.Array.Empty<string>();
                    }
                    try
                    {
                        files = Directory.GetFiles(m_source.FullPath);
                    }
                    catch
                    {
                        files = System.Array.Empty<string>();
                    }
                    m_directories = new List<string>(dirs);
                    m_filesPending = new List<string>(files);
                    m_stage = Stage.Directories;
                }

                // enumerate segments.
                if (m_stage == Stage.Directories)
                {
                    reference = NextChild();

                    if (reference != null)
                    {
                        return reference;
                    }

                    m_stage = Stage.Files;
                }

                // enumerate files.
                if (m_stage == Stage.Files)
                {
                    reference = NextChild();

                    if (reference != null)
                    {
                        return reference;
                    }

                    m_stage = Stage.Done;
                }

                // all done.
                return null;
            }
        }

        /// <summary>
        /// Returns the next child.
        /// </summary>
        private NodeStateReference NextChild()
        {
            NodeId targetId = NodeId.Null;

            // check if a specific browse name is requested.
            if (!BrowseName.IsNull)
            {
                // browse name must be qualified by the correct namespace.
                if (m_source.BrowseName.NamespaceIndex != BrowseName.NamespaceIndex)
                {
                    return null;
                }

                // look for matching directory.
                if (m_stage == Stage.Directories && m_directories != null)
                {
                    foreach (string name in m_directories)
                    {
                        if (BrowseName.Name == Path.GetFileName(name))
                        {
                            targetId = ModelUtils.ConstructIdForDirectory(name, m_source.NodeId.NamespaceIndex);
                            break;
                        }
                    }
                    m_directories = null;
                }

                // look for matching file.
                if (m_stage == Stage.Files && m_filesPending != null)
                {
                    foreach (string name in m_filesPending)
                    {
                        if (BrowseName.Name == Path.GetFileName(name))
                        {
                            targetId = ModelUtils.ConstructIdForFile(name, m_source.NodeId.NamespaceIndex);
                            break;
                        }
                    }
                    m_filesPending = null;
                }
            }
            // return the child at the next position.
            else
            {
                // look for next directory.
                if (m_stage == Stage.Directories && m_directories != null && m_directories.Count > 0)
                {
                    string name = m_directories[0];
                    m_directories.RemoveAt(0);
                    targetId = ModelUtils.ConstructIdForDirectory(name, m_source.NodeId.NamespaceIndex);
                }
                // look for next file.
                else if (m_stage == Stage.Files && m_filesPending != null && m_filesPending.Count > 0)
                {
                    string name = m_filesPending[0];
                    m_filesPending.RemoveAt(0);
                    targetId = ModelUtils.ConstructIdForFile(name, m_source.NodeId.NamespaceIndex);
                }
            }

            // create reference.
            if (!targetId.IsNull)
            {
                return new NodeStateReference(ReferenceTypeIds.HasComponent, false, targetId);
            }

            return null;
        }

        /// <summary>
        /// The stages available in a browse operation.
        /// </summary>
        private enum Stage
        {
            Begin,
            Directories,
            Files,
            Done
        }

        private Stage m_stage;
        private readonly DirectoryObjectState m_source;
        private List<string> m_filesPending;
        private List<string> m_directories;
    }
}
