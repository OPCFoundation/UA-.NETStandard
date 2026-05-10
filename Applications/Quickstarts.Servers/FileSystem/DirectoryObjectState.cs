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
    using System;
    using System.Collections.Generic;
    using System.IO;

    /// <summary>
    /// A object which maps a segment to directory
    /// </summary>
    public class DirectoryObjectState : FileDirectoryState
    {
        /// <summary>
        /// Gets the full path
        /// </summary>
        public string FullPath { get; }

        /// <summary>
        /// Is volume
        /// </summary>
        public bool IsVolume { get; }

        /// <summary>
        /// Create directory object
        /// </summary>
        public DirectoryObjectState(ISystemContext context, NodeId nodeId,
            string path, bool isVolume) : base(null)
        {
            System.Diagnostics.Contracts.Contract.Assume(context != null);
            FullPath = path;
            IsVolume = isVolume;
            TypeDefinitionId = ObjectTypeIds.FileDirectoryType;
            SymbolicName = path;
            NodeId = nodeId;
            string name = isVolume ? path : ModelUtils.GetName(path);
            BrowseName = new QualifiedName(name, nodeId.NamespaceIndex);
            DisplayName = new LocalizedText(name);
            Description = LocalizedText.Null;
            WriteMask = 0;
            UserWriteMask = 0;
            EventNotifier = EventNotifiers.None;

            DeleteFileSystemObject = new DeleteFileMethodState(this)
            {
                OnCall = OnDeleteFileSystemObject,
                Executable = true,
                UserExecutable = true
            };
            DeleteFileSystemObject.Create(context, MethodIds.FileDirectoryType_DeleteFileSystemObject,
                new QualifiedName(BrowseNames.DeleteFileSystemObject),
                new LocalizedText(BrowseNames.DeleteFileSystemObject), false);

            CreateFile = new CreateFileMethodState(this)
            {
                OnCall = OnCreateFile,
                Executable = true,
                UserExecutable = true
            };
            CreateFile.Create(context, MethodIds.FileDirectoryType_CreateFile,
                new QualifiedName(BrowseNames.CreateFile),
                new LocalizedText(BrowseNames.CreateFile), false);

            CreateDirectory = new CreateDirectoryMethodState(this)
            {
                OnCall = OnCreateDirectory,
                Executable = true,
                UserExecutable = true
            };
            CreateDirectory.Create(context, MethodIds.FileDirectoryType_CreateDirectory,
                new QualifiedName(BrowseNames.CreateDirectory),
                new LocalizedText(BrowseNames.CreateDirectory), false);

            MoveOrCopy = new MoveOrCopyMethodState(this)
            {
                OnCall = OnMoveOrCopy,
                Executable = true,
                UserExecutable = true
            };
            MoveOrCopy.Create(context, MethodIds.FileDirectoryType_MoveOrCopy,
                new QualifiedName(BrowseNames.MoveOrCopy),
                new LocalizedText(BrowseNames.MoveOrCopy), false);
        }

        private ServiceResult OnMoveOrCopy(ISystemContext _context, MethodState _method,
            NodeId _objectId, NodeId objectToMoveOrCopy, NodeId targetDirectory, bool createCopy,
            string newName, ref NodeId newNodeId)
        {
            if (!FileSystemNodeId.TryParse(objectToMoveOrCopy, out FileSystemNodeId objectToMoveOrCopy2) ||
                objectToMoveOrCopy2.RootType == ModelUtils.Volume)
            {
                return ServiceResult.Create(StatusCodes.BadInvalidArgument,
                    "Source is not a directory or file");
            }
            if (!FileSystemNodeId.TryParse(targetDirectory, out FileSystemNodeId targetDirectory2) ||
                targetDirectory2.RootType != ModelUtils.Directory)
            {
                return ServiceResult.Create(StatusCodes.BadInvalidArgument,
                    "Target is not a directory");
            }
            string path = objectToMoveOrCopy2.RootId;
            string dst = Path.Combine(targetDirectory2.RootId, newName ?? Path.GetFileName(path));
            try
            {
                if (File.Exists(path))
                {
                    if (createCopy)
                    {
                        File.Copy(path, dst);
                    }
                    else
                    {
                        File.Move(path, dst);
                    }
                    newNodeId = ModelUtils.ConstructIdForFile(dst,
                        NodeId.NamespaceIndex);
                }
                else if (Directory.Exists(path))
                {
                    if (createCopy)
                    {
                        CopyDirectory(path, dst);
                    }
                    else
                    {
                        Directory.Move(path, dst);
                    }
                    newNodeId = ModelUtils.ConstructIdForDirectory(dst,
                        NodeId.NamespaceIndex);
                }
                else
                {
                    return ServiceResult.Create(StatusCodes.BadNotFound,
                        $"File system object {path} not found");
                }
                return ServiceResult.Good;
            }
            catch (Exception ex)
            {
                return ServiceResult.Create(ex, StatusCodes.BadUserAccessDenied,
                    "Failed to move or copy");
            }
        }

        private ServiceResult OnCreateDirectory(ISystemContext _context, MethodState _method,
            NodeId _objectId, string directoryName, ref NodeId directoryNodeId)
        {
            string name = Path.Combine(FullPath, directoryName);
            if (File.Exists(name) || Directory.Exists(name))
            {
                return ServiceResult.Create(StatusCodes.BadBrowseNameDuplicated,
                    "Directory or file with same name exists");
            }
            Directory.CreateDirectory(name);
            directoryNodeId = ModelUtils.ConstructIdForDirectory(name, NodeId.NamespaceIndex);
            return ServiceResult.Good;
        }

        private ServiceResult OnCreateFile(ISystemContext _context, MethodState _method,
            NodeId _objectId, string fileName, bool requestFileOpen, ref NodeId fileNodeId,
            ref uint fileHandle)
        {
            string name = Path.Combine(FullPath, fileName);
            if (File.Exists(name) || Directory.Exists(name))
            {
                return ServiceResult.Create(StatusCodes.BadBrowseNameDuplicated,
                    "Directory or file with same name exists");
            }
            fileNodeId = ModelUtils.ConstructIdForFile(name, NodeId.NamespaceIndex);
            if (requestFileOpen)
            {
                if (!(_context.SystemHandle is FileSystem system) ||
                    !(system.GetHandle(fileNodeId) is FileHandle handle))
                {
                    return ServiceResult.Create(StatusCodes.BadInvalidState,
                        "Failed to get handle");
                }

                return handle.Open(0x2, out fileHandle); // open for writing
            }
            try
            {
                using (FileStream f = File.Create(name))
                {
                }
            }
            catch (Exception ex)
            {
                return ServiceResult.Create(ex, StatusCodes.BadUserAccessDenied, null);
            }
            fileHandle = 0;
            return StatusCodes.Good;
        }

        private ServiceResult OnDeleteFileSystemObject(ISystemContext _context,
            MethodState _method, NodeId _objectId, NodeId objectToDelete)
        {
            if (!FileSystemNodeId.TryParse(objectToDelete, out FileSystemNodeId objectToDelete2))
            {
                return ServiceResult.Create(StatusCodes.BadInvalidState,
                    "Not a fileSystem object.");
            }
            string path = objectToDelete2.RootId;
            try
            {
                switch (objectToDelete2.RootType)
                {
                    case ModelUtils.File:
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                            break;
                        }
                        return ServiceResult.Create(StatusCodes.BadNotFound,
                            $"File system object {path} not found");
                    case ModelUtils.Directory:
                        if (Directory.Exists(path))
                        {
                            Directory.Delete(path, true);
                            break;
                        }
                        return ServiceResult.Create(StatusCodes.BadNotFound,
                            $"File system object {path} not found");
                    case ModelUtils.Volume:
                        return ServiceResult.Create(StatusCodes.BadUserAccessDenied,
                            "Cannot delete root of filesystem");
                    default:
                        return ServiceResult.Create(StatusCodes.BadInvalidState,
                            "Not a fileSystem object.");
                }
            }
            catch (Exception ex)
            {
                return ServiceResult.Create(ex, StatusCodes.BadUserAccessDenied,
                    "Failed to delete file system object.");
            }
            return ServiceResult.Good;
        }

        /// <summary>
        /// Create browser on directory
        /// </summary>
        public override INodeBrowser CreateBrowser(
            ISystemContext context, ViewDescription view, NodeId referenceType,
            bool includeSubtypes, BrowseDirection browseDirection,
            QualifiedName browseName, IEnumerable<IReference> additionalReferences,
            bool internalOnly)
        {
            NodeBrowser browser = new DirectoryBrowser(
                context, view, referenceType, includeSubtypes,
                browseDirection, browseName, additionalReferences,
                internalOnly, this);

            PopulateBrowser(context, browser);
            return browser;
        }

        /// <summary>
        /// Populates the browser with references that meet the criteria.
        /// </summary>
        protected override void PopulateBrowser(ISystemContext context, NodeBrowser browser)
        {
            base.PopulateBrowser(context, browser);

            // check if the parent segments need to be returned.
            if (browser.IsRequired(ReferenceTypeIds.Organizes, true) && IsVolume)
            {
                // add reference to server
                browser.Add(ReferenceTypeIds.Organizes, true, ObjectIds.Server);
            }
            else if (browser.IsRequired(ReferenceTypeIds.HasComponent, true) && !IsVolume)
            {
                string parent = Path.GetDirectoryName(FullPath);
                if (!string.IsNullOrEmpty(parent))
                {
                    if (Path.GetPathRoot(FullPath) == parent)
                    {
                        browser.Add(ReferenceTypeIds.HasComponent, true,
                            ModelUtils.ConstructIdForVolume(parent, NodeId.NamespaceIndex));
                    }
                    else
                    {
                        browser.Add(ReferenceTypeIds.HasComponent, true,
                            ModelUtils.ConstructIdForDirectory(parent, NodeId.NamespaceIndex));
                    }
                }
            }
        }

        private static void CopyDirectory(string sourcePath, string targetPath)
        {
            foreach (string dirPath in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(dirPath.Replace(sourcePath, targetPath));
            }
            foreach (string newPath in Directory.GetFiles(sourcePath, "*", SearchOption.AllDirectories))
            {
                File.Copy(newPath, newPath.Replace(sourcePath, targetPath), true);
            }
        }
    }
}
