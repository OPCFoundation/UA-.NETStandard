/* ========================================================================
 * Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
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
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Server
{
    /// <summary>
    /// The master node manager for the server.
    /// </summary>
    public class MasterNodeManager : IDisposable
    {
        /// <summary>
        /// Initializes the object with default values.
        /// </summary>
        public MasterNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            string dynamicNamespaceUri,
            params INodeManager[] additionalManagers)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            Server = server ?? throw new ArgumentNullException(nameof(server));
            m_nodeManagers = [];
            m_maxContinuationPointsPerBrowse = (uint)configuration.ServerConfiguration
                .MaxBrowseContinuationPoints;

            // ensure the dynamic namespace uris.
            int dynamicNamespaceIndex = 1;

            if (!string.IsNullOrEmpty(dynamicNamespaceUri))
            {
                dynamicNamespaceIndex = server.NamespaceUris.GetIndex(dynamicNamespaceUri);

                if (dynamicNamespaceIndex == -1)
                {
                    dynamicNamespaceIndex = server.NamespaceUris.Append(dynamicNamespaceUri);
                }
            }

            // need to build a table of NamespaceIndexes and their NodeManagers.
            List<INodeManager> registeredManagers;
            var namespaceManagers = new Dictionary<int, List<INodeManager>>
            {
                [0] = [],
                [1] = registeredManagers = []
            };

            // always add the diagnostics and configuration node manager to the start of the list.
            var configurationAndDiagnosticsManager = new ConfigurationNodeManager(
                server,
                configuration);
            RegisterNodeManager(
                configurationAndDiagnosticsManager,
                registeredManagers,
                namespaceManagers);

            // add the core node manager second because the diagnostics node manager takes priority.
            // always add the core node manager to the second of the list.
            m_nodeManagers.Add(
                new CoreNodeManager(Server, configuration, (ushort)dynamicNamespaceIndex));

            // register core node manager for default UA namespace.
            namespaceManagers[0].Add(m_nodeManagers[1]);

            // register core node manager for built-in server namespace.
            namespaceManagers[1].Add(m_nodeManagers[1]);

            // add the custom NodeManagers provided by the application.
            if (additionalManagers != null)
            {
                foreach (INodeManager nodeManager in additionalManagers)
                {
                    RegisterNodeManager(nodeManager, registeredManagers, namespaceManagers);
                }

                // build table from dictionary.
                NamespaceManagers = new INodeManager[Server.NamespaceUris.Count][];

                for (int ii = 0; ii < NamespaceManagers.Length; ii++)
                {
                    if (namespaceManagers.TryGetValue(ii, out registeredManagers))
                    {
                        NamespaceManagers[ii] = [.. registeredManagers];
                    }
                }
            }
        }

        /// <summary>
        /// Registers the node manager with the master node manager.
        /// </summary>
        private void RegisterNodeManager(
            INodeManager nodeManager,
            List<INodeManager> registeredManagers,
            Dictionary<int, List<INodeManager>> namespaceManagers)
        {
            m_nodeManagers.Add(nodeManager);

            // ensure the NamespaceUris supported by the NodeManager are in the Server's NamespaceTable.
            if (nodeManager.NamespaceUris != null)
            {
                foreach (string namespaceUri in nodeManager.NamespaceUris)
                {
                    // look up the namespace uri.
                    int index = Server.NamespaceUris.GetIndex(namespaceUri);

                    if (index == -1)
                    {
                        index = Server.NamespaceUris.Append(namespaceUri);
                    }

                    // add manager to list for the namespace.
                    if (!namespaceManagers.TryGetValue(index, out registeredManagers))
                    {
                        namespaceManagers[index] = registeredManagers = [];
                    }

                    registeredManagers.Add(nodeManager);
                }
            }
        }

        /// <summary>
        /// Frees any unmanaged resources.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// An overrideable version of the Dispose.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                List<INodeManager> nodeManagers = null;

                lock (m_lock)
                {
                    nodeManagers = [.. m_nodeManagers];
                    m_nodeManagers.Clear();
                }

                foreach (INodeManager nodeManager in nodeManagers)
                {
                    Utils.SilentDispose(nodeManager);
                }
            }
        }

        /// <summary>
        /// Adds a reference to the table of external references.
        /// </summary>
        /// <remarks>
        /// This is a convenience function used by custom NodeManagers.
        /// </remarks>
        public static void CreateExternalReference(
            IDictionary<NodeId, IList<IReference>> externalReferences,
            NodeId sourceId,
            NodeId referenceTypeId,
            bool isInverse,
            NodeId targetId)
        {
            var reference = new ReferenceNode
            {
                ReferenceTypeId = referenceTypeId,
                IsInverse = isInverse,
                TargetId = targetId
            };

            if (!externalReferences.TryGetValue(sourceId, out IList<IReference> references))
            {
                externalReferences[sourceId] = references = [];
            }

            references.Add(reference);
        }

        /// <summary>
        /// Determine the required history access permission depending on the HistoryUpdateDetails
        /// </summary>
        /// <param name="historyUpdateDetails">The HistoryUpdateDetails passed in</param>
        /// <returns>The corresponding history access permission</returns>
        protected static PermissionType DetermineHistoryAccessPermission(
            HistoryUpdateDetails historyUpdateDetails)
        {
            Type detailsType = historyUpdateDetails.GetType();

            if (detailsType == typeof(UpdateDataDetails))
            {
                var updateDataDetails = (UpdateDataDetails)historyUpdateDetails;
                return GetHistoryPermissionType(updateDataDetails.PerformInsertReplace);
            }
            else if (detailsType == typeof(UpdateStructureDataDetails))
            {
                var updateStructureDataDetails = (UpdateStructureDataDetails)historyUpdateDetails;
                return GetHistoryPermissionType(updateStructureDataDetails.PerformInsertReplace);
            }
            else if (detailsType == typeof(UpdateEventDetails))
            {
                var updateEventDetails = (UpdateEventDetails)historyUpdateDetails;
                return GetHistoryPermissionType(updateEventDetails.PerformInsertReplace);
            }
            else if (detailsType == typeof(DeleteRawModifiedDetails) ||
                detailsType == typeof(DeleteAtTimeDetails) ||
                detailsType == typeof(DeleteEventDetails))
            {
                return PermissionType.DeleteHistory;
            }

            return PermissionType.ModifyHistory;
        }

        /// <summary>
        ///  Determine the History PermissionType depending on PerformUpdateType
        /// </summary>
        /// <returns>The corresponding PermissionType</returns>
        protected static PermissionType GetHistoryPermissionType(PerformUpdateType updateType)
        {
            switch (updateType)
            {
                case PerformUpdateType.Insert:
                    return PermissionType.InsertHistory;
                case PerformUpdateType.Update:
                    return PermissionType.InsertHistory | PermissionType.ModifyHistory;
                default: // PerformUpdateType.Replace or PerformUpdateType.Remove
                    return PermissionType.ModifyHistory;
            }
        }

        /// <summary>
        /// Returns the core node manager.
        /// </summary>
        public CoreNodeManager CoreNodeManager => m_nodeManagers[1] as CoreNodeManager;

        /// <summary>
        /// Returns the diagnostics node manager.
        /// </summary>
        public DiagnosticsNodeManager DiagnosticsNodeManager
            => m_nodeManagers[0] as DiagnosticsNodeManager;

        /// <summary>
        /// Returns the configuration node manager.
        /// </summary>
        public ConfigurationNodeManager ConfigurationNodeManager
            => m_nodeManagers[0] as ConfigurationNodeManager;

        /// <summary>
        /// Creates the node managers and start them
        /// </summary>
        public virtual void Startup()
        {
            lock (m_lock)
            {
                Utils.LogInfo(
                    Utils.TraceMasks.StartStop,
                    "MasterNodeManager.Startup - NodeManagers={0}",
                    m_nodeManagers.Count);

                // create the address spaces.
                var externalReferences = new Dictionary<NodeId, IList<IReference>>();

                for (int ii = 0; ii < m_nodeManagers.Count; ii++)
                {
                    INodeManager nodeManager = m_nodeManagers[ii];

                    try
                    {
                        nodeManager.CreateAddressSpace(externalReferences);
                    }
                    catch (Exception e)
                    {
                        Utils.LogError(
                            e,
                            "Unexpected error creating address space for NodeManager #{0}.",
                            ii);
                        throw;
                    }
                }

                // update external references.
                for (int ii = 0; ii < m_nodeManagers.Count; ii++)
                {
                    INodeManager nodeManager = m_nodeManagers[ii];

                    try
                    {
                        nodeManager.AddReferences(externalReferences);
                    }
                    catch (Exception e)
                    {
                        Utils.LogError(
                            e,
                            "Unexpected error adding references for NodeManager #{0}.",
                            ii);
                        throw;
                    }
                }
            }
        }

        /// <summary>
        /// Signals that a session is closing.
        /// </summary>
        public virtual void SessionClosing(
            OperationContext context,
            NodeId sessionId,
            bool deleteSubscriptions)
        {
            lock (m_lock)
            {
                for (int ii = 0; ii < m_nodeManagers.Count; ii++)
                {
                    if (m_nodeManagers[ii] is INodeManager2 nodeManager)
                    {
                        try
                        {
                            nodeManager.SessionClosing(context, sessionId, deleteSubscriptions);
                        }
                        catch (Exception e)
                        {
                            Utils.LogError(
                                e,
                                "Unexpected error closing session for NodeManager #{0}.",
                                ii);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Shuts down the node managers.
        /// </summary>
        public virtual void Shutdown()
        {
            lock (m_lock)
            {
                Utils.LogInfo(
                    Utils.TraceMasks.StartStop,
                    "MasterNodeManager.Shutdown - NodeManagers={0}",
                    m_nodeManagers.Count);

                foreach (INodeManager nodeManager in m_nodeManagers)
                {
                    nodeManager.DeleteAddressSpace();
                }
            }
        }

        /// <summary>
        /// Registers the node manager as the node manager for Nodes in the specified namespace.
        /// </summary>
        /// <param name="namespaceUri">The URI of the namespace.</param>
        /// <param name="nodeManager">The NodeManager which owns node in the namespace.</param>
        /// <remarks>
        /// <para>
        /// Multiple NodeManagers may register interest in a Namespace.
        /// The order in which this method is called determines the precedence if multiple NodeManagers exist.
        /// This method adds the namespaceUri to the Server's Namespace table if it does not already exist.
        /// </para>
        /// <para>This method is thread safe and can be called at anytime.</para>
        /// <para>
        /// This method does not have to be called for any namespaces that were in the NodeManager's
        /// NamespaceUri property when the MasterNodeManager was created.
        /// </para>
        /// </remarks>
        /// <exception cref="ArgumentNullException">Throw if the namespaceUri or the nodeManager are null.</exception>
        public void RegisterNamespaceManager(string namespaceUri, INodeManager nodeManager)
        {
            if (string.IsNullOrEmpty(namespaceUri))
            {
                throw new ArgumentNullException(nameof(namespaceUri));
            }

            if (nodeManager == null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }

            // look up the namespace uri.
            int index = Server.NamespaceUris.GetIndex(namespaceUri);

            if (index < 0)
            {
                index = Server.NamespaceUris.Append(namespaceUri);
            }

            // allocate a new table (using arrays instead of collections because lookup efficiency is critical).
            var namespaceManagers = new INodeManager[Server.NamespaceUris.Count][];

            m_readWriterLockSlim.EnterWriteLock();
            try
            {
                // copy existing values.
                for (int ii = 0; ii < NamespaceManagers.Length; ii++)
                {
                    if (NamespaceManagers.Length >= ii)
                    {
                        namespaceManagers[ii] = NamespaceManagers[ii];
                    }
                }

                // allocate a new array for the index being updated.
                INodeManager[] registeredManagers = namespaceManagers[index];

                if (registeredManagers == null)
                {
                    registeredManagers = new INodeManager[1];
                }
                else
                {
                    registeredManagers = new INodeManager[registeredManagers.Length + 1];
                    Array.Copy(
                        namespaceManagers[index],
                        registeredManagers,
                        namespaceManagers[index].Length);
                }

                // add new node manager to the end of the list.
                registeredManagers[^1] = nodeManager;
                namespaceManagers[index] = registeredManagers;

                // replace the table.
                NamespaceManagers = namespaceManagers;
            }
            finally
            {
                m_readWriterLockSlim.ExitWriteLock();
            }
        }

        /// <summary>
        /// Unregisters the node manager as the node manager for Nodes in the specified namespace.
        /// </summary>
        /// <param name="namespaceUri">The URI of the namespace.</param>
        /// <param name="nodeManager">The NodeManager which no longer owns nodes in the namespace.</param>
        /// <returns>A value indicating whether the node manager was successfully unregistered.</returns>
        /// <exception cref="ArgumentNullException">Throw if the namespaceUri or the nodeManager are null.</exception>
        public bool UnregisterNamespaceManager(string namespaceUri, INodeManager nodeManager)
        {
            if (string.IsNullOrEmpty(namespaceUri))
            {
                throw new ArgumentNullException(nameof(namespaceUri));
            }

            if (nodeManager == null)
            {
                throw new ArgumentNullException(nameof(nodeManager));
            }

            // look up the namespace uri.
            int namespaceIndex = Server.NamespaceUris.GetIndex(namespaceUri);
            if (namespaceIndex < 0)
            {
                return false;
            }

            // look up the node manager in the registered node managers for the namespace.
            int nodeManagerIndex = Array.IndexOf(NamespaceManagers[namespaceIndex], nodeManager);
            if (nodeManagerIndex < 0)
            {
                return false;
            }

            // allocate a new table (using arrays instead of collections because lookup efficiency is critical).
            var namespaceManagers = new INodeManager[Server.NamespaceUris.Count][];

            m_readWriterLockSlim.EnterWriteLock();
            try
            {
                // copy existing values.
                for (int ii = 0; ii < NamespaceManagers.Length; ii++)
                {
                    if (NamespaceManagers.Length >= ii)
                    {
                        namespaceManagers[ii] = NamespaceManagers[ii];
                    }
                }

                // allocate a new smaller array to support element removal for the index being updated.
                var registeredManagers = new INodeManager[namespaceManagers[namespaceIndex].Length -
                    1];

                // begin by populating the new array with existing elements up to the target index.
                if (nodeManagerIndex > 0)
                {
                    Array.Copy(
                        namespaceManagers[namespaceIndex],
                        0,
                        registeredManagers,
                        0,
                        nodeManagerIndex);
                }

                // finish by populating the new array with existing elements after the target index.
                if (nodeManagerIndex < namespaceManagers[namespaceIndex].Length - 1)
                {
                    Array.Copy(
                        namespaceManagers[namespaceIndex],
                        nodeManagerIndex + 1,
                        registeredManagers,
                        nodeManagerIndex,
                        namespaceManagers[namespaceIndex].Length - nodeManagerIndex - 1);
                }

                // update the array for the target index.
                namespaceManagers[namespaceIndex] = registeredManagers;

                // replace the table.
                NamespaceManagers = namespaceManagers;

                return true;
            }
            finally
            {
                m_readWriterLockSlim.ExitWriteLock();
            }
        }

        /// <summary>
        /// Returns node handle and its node manager.
        /// </summary>
        public virtual object GetManagerHandle(NodeId nodeId, out INodeManager nodeManager)
        {
            nodeManager = null;
            object handle;

            // null node ids have no manager.
            if (NodeId.IsNull(nodeId))
            {
                return null;
            }

            // use the namespace index to select the node manager.
            int index = nodeId.NamespaceIndex;

            m_readWriterLockSlim.EnterReadLock();
            try
            {
                // check if node managers are registered - use the core node manager if unknown.
                if (index >= NamespaceManagers.Length || NamespaceManagers[index] == null)
                {
                    handle = m_nodeManagers[1].GetManagerHandle(nodeId);

                    if (handle != null)
                    {
                        nodeManager = m_nodeManagers[1];
                        return handle;
                    }

                    return null;
                }

                // check each of the registered node managers.
                INodeManager[] nodeManagers = NamespaceManagers[index];

                for (int ii = 0; ii < nodeManagers.Length; ii++)
                {
                    handle = nodeManagers[ii].GetManagerHandle(nodeId);

                    if (handle != null)
                    {
                        nodeManager = nodeManagers[ii];
                        return handle;
                    }
                }
            }
            finally
            {
                m_readWriterLockSlim.ExitReadLock();
            }

            // node not recognized.
            return null;
        }

        /// <summary>
        /// Adds the references to the target.
        /// </summary>
        public virtual void AddReferences(NodeId sourceId, IList<IReference> references)
        {
            // find source node.
            object sourceHandle = GetManagerHandle(sourceId, out INodeManager nodeManager);
            if (sourceHandle == null)
            {
                return;
            }

            var map = new Dictionary<NodeId, IList<IReference>> { { sourceId, references } };
            nodeManager.AddReferences(map);
        }

        /// <summary>
        /// Deletes the references to the target.
        /// </summary>
        public virtual void DeleteReferences(NodeId targetId, IList<IReference> references)
        {
            foreach (ReferenceNode reference in references.OfType<ReferenceNode>())
            {
                var sourceId = ExpandedNodeId.ToNodeId(reference.TargetId, Server.NamespaceUris);

                // find source node.
                object sourceHandle = GetManagerHandle(sourceId, out INodeManager nodeManager);

                if (sourceHandle == null)
                {
                    continue;
                }

                // delete the reference.
                nodeManager.DeleteReference(
                    sourceHandle,
                    reference.ReferenceTypeId,
                    !reference.IsInverse,
                    targetId,
                    false);
            }
        }

        /// <summary>
        /// Deletes the specified references.
        /// </summary>
        public void RemoveReferences(List<LocalReference> referencesToRemove)
        {
            for (int ii = 0; ii < referencesToRemove.Count; ii++)
            {
                LocalReference reference = referencesToRemove[ii];

                // find source node.
                object sourceHandle = GetManagerHandle(
                    reference.SourceId,
                    out INodeManager nodeManager);

                if (sourceHandle == null)
                {
                    continue;
                }

                // delete the reference.
                nodeManager.DeleteReference(
                    sourceHandle,
                    reference.ReferenceTypeId,
                    reference.IsInverse,
                    reference.TargetId,
                    false);
            }
        }

        /// <summary>
        /// Registers a set of node ids.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="nodesToRegister"/> is <c>null</c>.</exception>
        public virtual void RegisterNodes(
            OperationContext context,
            NodeIdCollection nodesToRegister,
            out NodeIdCollection registeredNodeIds)
        {
            if (nodesToRegister == null)
            {
                throw new ArgumentNullException(nameof(nodesToRegister));
            }

            // return the node id provided.
            registeredNodeIds = new NodeIdCollection(nodesToRegister.Count);

            for (int ii = 0; ii < nodesToRegister.Count; ii++)
            {
                registeredNodeIds.Add(nodesToRegister[ii]);
            }

            Utils.LogTrace(
                Utils.TraceMasks.ServiceDetail,
                "MasterNodeManager.RegisterNodes - Count={0}",
                nodesToRegister.Count);

            // it is up to the node managers to assign the handles.
            /*
            List<bool> processedNodes = new List<bool>(new bool[itemsToDelete.Count]);

            for (int ii = 0; ii < m_nodeManagers.Count; ii++)
            {
                m_nodeManagers[ii].RegisterNodes(
                    context,
                    nodesToRegister,
                    registeredNodeIds,
                    processedNodes);
            }
            */
        }

        /// <summary>
        /// Unregisters a set of node ids.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="nodesToUnregister"/> is <c>null</c>.</exception>
        public virtual void UnregisterNodes(
            OperationContext context,
            NodeIdCollection nodesToUnregister)
        {
            if (nodesToUnregister == null)
            {
                throw new ArgumentNullException(nameof(nodesToUnregister));
            }

            Utils.LogTrace(
                Utils.TraceMasks.ServiceDetail,
                "MasterNodeManager.UnregisterNodes - Count={0}",
                nodesToUnregister.Count);

            // it is up to the node managers to assign the handles.
            /*
            List<bool> processedNodes = new List<bool>(new bool[itemsToDelete.Count]);

            for (int ii = 0; ii < m_nodeManagers.Count; ii++)
            {
                m_nodeManagers[ii].RegisterNodes(
                    context,
                    nodesToUnregister,
                    processedNodes);
            }
            */
        }

        /// <summary>
        /// Translates a start node id plus a relative paths into a node id.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="browsePaths"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public virtual void TranslateBrowsePathsToNodeIds(
            OperationContext context,
            BrowsePathCollection browsePaths,
            out BrowsePathResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            if (browsePaths == null)
            {
                throw new ArgumentNullException(nameof(browsePaths));
            }

            bool diagnosticsExist = false;
            results = new BrowsePathResultCollection(browsePaths.Count);
            diagnosticInfos = new DiagnosticInfoCollection(browsePaths.Count);

            for (int ii = 0; ii < browsePaths.Count; ii++)
            {
                // check if request has timed out or been cancelled.
                if (StatusCode.IsBad(context.OperationStatus))
                {
                    throw new ServiceResultException(context.OperationStatus);
                }

                BrowsePath browsePath = browsePaths[ii];

                var result = new BrowsePathResult { StatusCode = StatusCodes.Good };
                results.Add(result);

                ServiceResult error;

                // need to trap unexpected exceptions to handle bugs in the node managers.
                try
                {
                    error = TranslateBrowsePath(context, browsePath, result);
                }
                catch (Exception e)
                {
                    error = ServiceResult.Create(
                        e,
                        StatusCodes.BadUnexpectedError,
                        "Unexpected error translating browse path.");
                }

                if (ServiceResult.IsGood(error))
                {
                    // check for no match.
                    if (result.Targets.Count == 0)
                    {
                        error = StatusCodes.BadNoMatch;
                    }
                    // put a placeholder for diagnostics.
                    else if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        diagnosticInfos.Add(null);
                    }
                }

                // check for error.
                if (error != null && error.Code != StatusCodes.Good)
                {
                    result.StatusCode = error.StatusCode;

                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        DiagnosticInfo diagnosticInfo = ServerUtils.CreateDiagnosticInfo(
                            Server,
                            context,
                            error);
                        diagnosticInfos.Add(diagnosticInfo);
                        diagnosticsExist = true;
                    }
                }
            }

            // clear the diagnostics array if no diagnostics requested or no errors occurred.
            UpdateDiagnostics(context, diagnosticsExist, ref diagnosticInfos);
        }

        /// <summary>
        /// Updates the diagnostics return parameter.
        /// </summary>
        protected void UpdateDiagnostics(
            OperationContext context,
            bool diagnosticsExist,
            ref DiagnosticInfoCollection diagnosticInfos)
        {
            if (diagnosticInfos == null)
            {
                return;
            }

            if (diagnosticsExist && context.StringTable.Count == 0)
            {
                diagnosticsExist = false;

                for (int ii = 0; !diagnosticsExist && ii < diagnosticInfos.Count; ii++)
                {
                    DiagnosticInfo diagnosticInfo = diagnosticInfos[ii];

                    int depth = 0;
                    while (diagnosticInfo != null && depth++ < DiagnosticInfo.MaxInnerDepth)
                    {
                        if (!string.IsNullOrEmpty(diagnosticInfo.AdditionalInfo))
                        {
                            diagnosticsExist = true;
                            break;
                        }

                        diagnosticInfo = diagnosticInfo.InnerDiagnosticInfo;
                    }
                }
            }

            if (!diagnosticsExist)
            {
                diagnosticInfos = null;
            }
        }

        /// <summary>
        /// Translates a browse path.
        /// </summary>
        protected ServiceResult TranslateBrowsePath(
            OperationContext context,
            BrowsePath browsePath,
            BrowsePathResult result)
        {
            Debug.Assert(browsePath != null);
            Debug.Assert(result != null);

            // check for valid start node.
            object sourceHandle = GetManagerHandle(
                browsePath.StartingNode,
                out INodeManager nodeManager);

            if (sourceHandle == null)
            {
                return StatusCodes.BadNodeIdUnknown;
            }

            // check the relative path.
            RelativePath relativePath = browsePath.RelativePath;

            if (relativePath.Elements == null || relativePath.Elements.Count == 0)
            {
                return StatusCodes.BadNothingToDo;
            }

            for (int ii = 0; ii < relativePath.Elements.Count; ii++)
            {
                RelativePathElement element = relativePath.Elements[ii];

                if (element == null || QualifiedName.IsNull(relativePath.Elements[ii].TargetName))
                {
                    return StatusCodes.BadBrowseNameInvalid;
                }

                if (NodeId.IsNull(element.ReferenceTypeId))
                {
                    element.ReferenceTypeId = ReferenceTypeIds.References;
                    element.IncludeSubtypes = true;
                }
            }
            // validate access rights and role permissions
            ServiceResult serviceResult = ValidatePermissions(
                context,
                nodeManager,
                sourceHandle,
                PermissionType.Browse,
                null,
                true);
            if (ServiceResult.IsGood(serviceResult))
            {
                // translate path only if validation is passing
                TranslateBrowsePath(
                    context,
                    nodeManager,
                    sourceHandle,
                    relativePath,
                    result.Targets,
                    0);
            }

            return serviceResult;
        }

        /// <summary>
        /// Recursively processes the elements in the RelativePath starting at the specified index.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        private void TranslateBrowsePath(
            OperationContext context,
            INodeManager nodeManager,
            object sourceHandle,
            RelativePath relativePath,
            BrowsePathTargetCollection targets,
            int index)
        {
            Debug.Assert(nodeManager != null);
            Debug.Assert(sourceHandle != null);
            Debug.Assert(relativePath != null);
            Debug.Assert(targets != null);

            // check for end of list.
            if (index < 0 || index >= relativePath.Elements.Count)
            {
                return;
            }

            // follow the next hop.
            RelativePathElement element = relativePath.Elements[index];

            // check for valid reference type.
            if (!element.IncludeSubtypes && NodeId.IsNull(element.ReferenceTypeId))
            {
                return;
            }

            // check for valid target name.
            if (QualifiedName.IsNull(element.TargetName))
            {
                throw new ServiceResultException(StatusCodes.BadBrowseNameInvalid);
            }

            var targetIds = new List<ExpandedNodeId>();
            var externalTargetIds = new List<NodeId>();

            try
            {
                nodeManager.TranslateBrowsePath(
                    context,
                    sourceHandle,
                    element,
                    targetIds,
                    externalTargetIds);
            }
            catch (Exception e)
            {
                Utils.LogError(e, "Unexpected error translating browse path.");
                return;
            }

            // must check the browse name on all external targets.
            for (int ii = 0; ii < externalTargetIds.Count; ii++)
            {
                // get the browse name from another node manager.
                var description = new ReferenceDescription();

                UpdateReferenceDescription(
                    context,
                    externalTargetIds[ii],
                    NodeClass.Unspecified,
                    BrowseResultMask.BrowseName,
                    description);

                // add to list if target name matches.
                if (description.BrowseName == element.TargetName)
                {
                    bool found = false;

                    for (int jj = 0; jj < targetIds.Count; jj++)
                    {
                        if (targetIds[jj] == externalTargetIds[ii])
                        {
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        targetIds.Add(externalTargetIds[ii]);
                    }
                }
            }

            // check if done after a final hop.
            if (index == relativePath.Elements.Count - 1)
            {
                for (int ii = 0; ii < targetIds.Count; ii++)
                {
                    // Check the role permissions for target nodes
                    object targetHandle = GetManagerHandle(
                        ExpandedNodeId.ToNodeId(targetIds[ii], Server.NamespaceUris),
                        out INodeManager targetNodeManager);

                    if (targetHandle != null && targetNodeManager != null)
                    {
                        NodeMetadata nodeMetadata = targetNodeManager.GetNodeMetadata(
                            context,
                            targetHandle,
                            BrowseResultMask.All);
                        ServiceResult serviceResult = ValidateRolePermissions(
                            context,
                            nodeMetadata,
                            PermissionType.Browse);

                        if (ServiceResult.IsBad(serviceResult))
                        {
                            // Remove target node without role permissions.
                            continue;
                        }
                    }

                    var target = new BrowsePathTarget
                    {
                        TargetId = targetIds[ii],
                        RemainingPathIndex = uint.MaxValue
                    };

                    targets.Add(target);
                }

                return;
            }

            // process next hops.
            for (int ii = 0; ii < targetIds.Count; ii++)
            {
                ExpandedNodeId targetId = targetIds[ii];

                // check for external reference.
                if (targetId.IsAbsolute)
                {
                    var target = new BrowsePathTarget
                    {
                        TargetId = targetId,
                        RemainingPathIndex = (uint)(index + 1)
                    };

                    targets.Add(target);
                    continue;
                }

                // check for valid start node.
                sourceHandle = GetManagerHandle((NodeId)targetId, out nodeManager);

                if (sourceHandle == null)
                {
                    continue;
                }

                // recursively follow hops.
                TranslateBrowsePath(
                    context,
                    nodeManager,
                    sourceHandle,
                    relativePath,
                    targets,
                    index + 1);
            }
        }

        /// <summary>
        /// Returns the set of references that meet the filter criteria.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public virtual void Browse(
            OperationContext context,
            ViewDescription view,
            uint maxReferencesPerNode,
            BrowseDescriptionCollection nodesToBrowse,
            out BrowseResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (nodesToBrowse == null)
            {
                throw new ArgumentNullException(nameof(nodesToBrowse));
            }

            if (view != null && !NodeId.IsNull(view.ViewId))
            {
                object viewHandle =
                    GetManagerHandle(view.ViewId, out INodeManager viewManager)
                    ?? throw new ServiceResultException(StatusCodes.BadViewIdUnknown);

                NodeMetadata metadata = viewManager.GetNodeMetadata(
                    context,
                    viewHandle,
                    BrowseResultMask.NodeClass);

                if (metadata == null || metadata.NodeClass != NodeClass.View)
                {
                    throw new ServiceResultException(StatusCodes.BadViewIdUnknown);
                }

                // validate access rights and role permissions
                ServiceResult validationResult = ValidatePermissions(
                    context,
                    viewManager,
                    viewHandle,
                    PermissionType.Browse,
                    null,
                    true);
                if (ServiceResult.IsBad(validationResult))
                {
                    throw new ServiceResultException(validationResult);
                }
                view.Handle = viewHandle;
            }

            bool diagnosticsExist = false;
            results = new BrowseResultCollection(nodesToBrowse.Count);
            diagnosticInfos = new DiagnosticInfoCollection(nodesToBrowse.Count);

            uint continuationPointsAssigned = 0;

            for (int ii = 0; ii < nodesToBrowse.Count; ii++)
            {
                // check if request has timed out or been cancelled.
                if (StatusCode.IsBad(context.OperationStatus))
                {
                    // release all allocated continuation points.
                    foreach (BrowseResult current in results)
                    {
                        if (current != null &&
                            current.ContinuationPoint != null &&
                            current.ContinuationPoint.Length > 0)
                        {
                            ContinuationPoint cp = context.Session
                                .RestoreContinuationPoint(current.ContinuationPoint);
                            cp.Dispose();
                        }
                    }

                    throw new ServiceResultException(context.OperationStatus);
                }

                BrowseDescription nodeToBrowse = nodesToBrowse[ii];

                // initialize result.
                var result = new BrowseResult { StatusCode = StatusCodes.Good };
                results.Add(result);

                ServiceResult error;

                // need to trap unexpected exceptions to handle bugs in the node managers.
                try
                {
                    error = Browse(
                        context,
                        view,
                        maxReferencesPerNode,
                        continuationPointsAssigned < m_maxContinuationPointsPerBrowse,
                        nodeToBrowse,
                        result);
                }
                catch (Exception e)
                {
                    error = ServiceResult.Create(
                        e,
                        StatusCodes.BadUnexpectedError,
                        "Unexpected error browsing node.");
                }

                // check for continuation point.
                if (result.ContinuationPoint != null && result.ContinuationPoint.Length > 0)
                {
                    continuationPointsAssigned++;
                }

                // check for error.
                result.StatusCode = error.StatusCode;

                if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                {
                    DiagnosticInfo diagnosticInfo = null;

                    if (error != null && error.Code != StatusCodes.Good)
                    {
                        diagnosticInfo = ServerUtils.CreateDiagnosticInfo(Server, context, error);
                        diagnosticsExist = true;
                    }

                    diagnosticInfos.Add(diagnosticInfo);
                }
            }

            // clear the diagnostics array if no diagnostics requested or no errors occurred.
            UpdateDiagnostics(context, diagnosticsExist, ref diagnosticInfos);
        }

        /// <summary>
        /// Prepare a cache per NodeManager and unique NodeId that holds the attributes needed to validate the AccessRestrictions and RolePermissions.
        /// This cache is then used in subsequenct calls to avoid triggering unnecessary time consuming callbacks.
        /// The current services that benefit from this are the Read service
        /// </summary>
        /// <typeparam name="T">One of the following types used in the service calls:
        ///     ReadValueId used in the Read service</typeparam>
        /// <param name="nodesCollection">The collection of nodes on which the service operates uppon</param>
        /// <param name="uniqueNodesServiceAttributes">The resulting cache that holds the values of the AccessRestrictions and RolePermissions attributes needed for Read service</param>
        /// <exception cref="ArgumentException"></exception>
        private static void PrepareValidationCache<T>(
            List<T> nodesCollection,
            out Dictionary<NodeId, List<object>> uniqueNodesServiceAttributes)
        {
            var uniqueNodes = new HashSet<NodeId>();
            for (int i = 0; i < nodesCollection.Count; i++)
            {
                Type listType = typeof(T);
                NodeId nodeId = null;

                if (listType == typeof(ReadValueId))
                {
                    nodeId = (nodesCollection[i] as ReadValueId)?.NodeId;
                }

                if (nodeId == null)
                {
                    throw new ArgumentException(
                        "Provided List<T> nodesCollection is of wrong type, T should be type BrowseDescription, ReadValueId or CallMethodRequest",
                        nameof(nodesCollection));
                }

                uniqueNodes.Add(nodeId);
            }
            // uniqueNodesReadAttributes is the place where the attributes for each unique nodeId are kept on the services
            uniqueNodesServiceAttributes = [];
            foreach (NodeId uniqueNode in uniqueNodes)
            {
                uniqueNodesServiceAttributes.Add(uniqueNode, []);
            }
        }

        /// <summary>
        /// Continues a browse operation that was previously halted.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public virtual void BrowseNext(
            OperationContext context,
            bool releaseContinuationPoints,
            ByteStringCollection continuationPoints,
            out BrowseResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (continuationPoints == null)
            {
                throw new ArgumentNullException(nameof(continuationPoints));
            }

            bool diagnosticsExist = false;
            results = new BrowseResultCollection(continuationPoints.Count);
            diagnosticInfos = new DiagnosticInfoCollection(continuationPoints.Count);

            uint continuationPointsAssigned = 0;

            for (int ii = 0; ii < continuationPoints.Count; ii++)
            {
                ContinuationPoint cp = null;

                // check if request has timed out or been canceled.
                if (StatusCode.IsBad(context.OperationStatus))
                {
                    // release all allocated continuation points.
                    foreach (BrowseResult current in results)
                    {
                        if (current != null &&
                            current.ContinuationPoint != null &&
                            current.ContinuationPoint.Length > 0)
                        {
                            cp = context.Session
                                .RestoreContinuationPoint(current.ContinuationPoint);
                            cp.Dispose();
                        }
                    }

                    throw new ServiceResultException(context.OperationStatus);
                }

                // find the continuation point.
                cp = context.Session.RestoreContinuationPoint(continuationPoints[ii]);

                // validate access rights and role permissions
                if (cp != null)
                {
                    ServiceResult validationResult = ValidatePermissions(
                        context,
                        cp.Manager,
                        cp.NodeToBrowse,
                        PermissionType.Browse,
                        null,
                        true);
                    if (ServiceResult.IsBad(validationResult))
                    {
                        var badResult = new BrowseResult { StatusCode = validationResult.Code };
                        results.Add(badResult);

                        // put placeholder for diagnostics
                        diagnosticInfos.Add(null);
                        continue;
                    }
                }

                // initialize result.
                var result = new BrowseResult { StatusCode = StatusCodes.Good };
                results.Add(result);

                // check if simply releasing the continuation point.
                if (releaseContinuationPoints)
                {
                    cp?.Dispose();

                    continue;
                }

                ServiceResult error = null;

                // check if continuation point has expired.
                if (cp == null)
                {
                    error = StatusCodes.BadContinuationPointInvalid;
                }

                if (cp != null)
                {
                    // need to trap unexpected exceptions to handle bugs in the node managers.
                    try
                    {
                        ReferenceDescriptionCollection references = result.References;

                        error = FetchReferences(
                            context,
                            continuationPointsAssigned < m_maxContinuationPointsPerBrowse,
                            ref cp,
                            ref references);

                        result.References = references;
                    }
                    catch (Exception e)
                    {
                        error = ServiceResult.Create(
                            e,
                            StatusCodes.BadUnexpectedError,
                            "Unexpected error browsing node.");
                    }

                    // check for continuation point.
                    if (result.ContinuationPoint != null && result.ContinuationPoint.Length > 0)
                    {
                        continuationPointsAssigned++;
                    }
                }

                // check for error.
                result.StatusCode = error.StatusCode;

                if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                {
                    DiagnosticInfo diagnosticInfo = null;

                    if (error != null && error.Code != StatusCodes.Good)
                    {
                        diagnosticInfo = ServerUtils.CreateDiagnosticInfo(Server, context, error);
                        diagnosticsExist = true;
                    }

                    diagnosticInfos.Add(diagnosticInfo);
                }

                // check for continuation point.
                if (cp != null)
                {
                    result.StatusCode = StatusCodes.Good;
                    result.ContinuationPoint = cp.Id.ToByteArray();
                }
            }

            // clear the diagnostics array if no diagnostics requested or no errors occurred.
            UpdateDiagnostics(context, diagnosticsExist, ref diagnosticInfos);
        }

        /// <summary>
        /// Returns the set of references that meet the filter criteria.
        /// </summary>
        protected ServiceResult Browse(
            OperationContext context,
            ViewDescription view,
            uint maxReferencesPerNode,
            bool assignContinuationPoint,
            BrowseDescription nodeToBrowse,
            BrowseResult result)
        {
            Debug.Assert(context != null);
            Debug.Assert(nodeToBrowse != null);
            Debug.Assert(result != null);

            // find node manager that owns the node.
            object handle = GetManagerHandle(nodeToBrowse.NodeId, out INodeManager nodeManager);

            if (handle == null)
            {
                return StatusCodes.BadNodeIdUnknown;
            }

            if (!NodeId.IsNull(nodeToBrowse.ReferenceTypeId) &&
                !Server.TypeTree.IsKnown(nodeToBrowse.ReferenceTypeId))
            {
                return StatusCodes.BadReferenceTypeIdInvalid;
            }

            if (nodeToBrowse.BrowseDirection is < BrowseDirection.Forward or > BrowseDirection.Both)
            {
                return StatusCodes.BadBrowseDirectionInvalid;
            }

            // validate access rights and role permissions
            ServiceResult validationResult = ValidatePermissions(
                context,
                nodeManager,
                handle,
                PermissionType.Browse,
                null,
                true);
            if (ServiceResult.IsBad(validationResult))
            {
                return validationResult;
            }

            // create a continuation point.
            var cp = new ContinuationPoint
            {
                Manager = nodeManager,
                View = view,
                NodeToBrowse = handle,
                MaxResultsToReturn = maxReferencesPerNode,
                BrowseDirection = nodeToBrowse.BrowseDirection,
                ReferenceTypeId = nodeToBrowse.ReferenceTypeId,
                IncludeSubtypes = nodeToBrowse.IncludeSubtypes,
                NodeClassMask = nodeToBrowse.NodeClassMask,
                ResultMask = (BrowseResultMask)nodeToBrowse.ResultMask,
                Index = 0,
                Data = null
            };

            // check if reference type left unspecified.
            if (NodeId.IsNull(cp.ReferenceTypeId))
            {
                cp.ReferenceTypeId = ReferenceTypeIds.References;
                cp.IncludeSubtypes = true;
            }

            // loop until browse is complete or max results.
            ReferenceDescriptionCollection references = result.References;
            ServiceResult error = FetchReferences(
                context,
                assignContinuationPoint,
                ref cp,
                ref references);
            result.References = references;

            // save continuation point.
            if (cp != null)
            {
                result.StatusCode = StatusCodes.Good;
                result.ContinuationPoint = cp.Id.ToByteArray();
            }

            // all is good.
            return error;
        }

        /// <summary>
        /// Loops until browse is complete for max results reached.
        /// </summary>
        protected ServiceResult FetchReferences(
            OperationContext context,
            bool assignContinuationPoint,
            ref ContinuationPoint cp,
            ref ReferenceDescriptionCollection references)
        {
            Debug.Assert(context != null);
            Debug.Assert(cp != null);
            Debug.Assert(references != null);

            INodeManager nodeManager = cp.Manager;
            var nodeClassMask = (NodeClass)cp.NodeClassMask;
            BrowseResultMask resultMask = cp.ResultMask;

            // loop until browse is complete or max results.
            while (cp != null)
            {
                // fetch next batch.
                nodeManager.Browse(context, ref cp, references);

                var referencesToKeep = new ReferenceDescriptionCollection(references.Count);

                // check for incomplete reference descriptions.
                for (int ii = 0; ii < references.Count; ii++)
                {
                    ReferenceDescription reference = references[ii];

                    // check if filtering must be applied.
                    if (reference.Unfiltered)
                    {
                        // ignore unknown external references.
                        if (reference.NodeId.IsAbsolute)
                        {
                            continue;
                        }

                        // update the description.
                        bool include = UpdateReferenceDescription(
                            context,
                            (NodeId)reference.NodeId,
                            nodeClassMask,
                            resultMask,
                            reference);

                        if (!include)
                        {
                            continue;
                        }
                    }

                    // add to list.
                    referencesToKeep.Add(reference);
                }

                // replace list.
                references = referencesToKeep;

                // check if browse limit reached.
                if (cp != null && references.Count >= cp.MaxResultsToReturn)
                {
                    if (!assignContinuationPoint)
                    {
                        return StatusCodes.BadNoContinuationPoints;
                    }

                    cp.Id = Guid.NewGuid();
                    context.Session.SaveContinuationPoint(cp);
                    break;
                }
            }

            // all is good.
            return ServiceResult.Good;
        }

        /// <summary>
        /// Updates the reference description with the node attributes.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="targetId"/> is <c>null</c>.</exception>
        private bool UpdateReferenceDescription(
            OperationContext context,
            NodeId targetId,
            NodeClass nodeClassMask,
            BrowseResultMask resultMask,
            ReferenceDescription description)
        {
            if (targetId == null)
            {
                throw new ArgumentNullException(nameof(targetId));
            }

            if (description == null)
            {
                throw new ArgumentNullException(nameof(description));
            }

            // find node manager that owns the node.
            object handle = GetManagerHandle(targetId, out INodeManager nodeManager);

            // dangling reference - nothing more to do.
            if (handle == null)
            {
                return false;
            }

            // fetch the node attributes.
            NodeMetadata metadata = nodeManager.GetNodeMetadata(context, handle, resultMask);

            if (metadata == null)
            {
                return false;
            }

            // check nodeclass filter.
            if (nodeClassMask != NodeClass.Unspecified &&
                ((int)metadata.NodeClass & (int)nodeClassMask) == 0)
            {
                return false;
            }

            // update attributes.
            description.NodeId = metadata.NodeId;

            description.SetTargetAttributes(
                resultMask,
                metadata.NodeClass,
                metadata.BrowseName,
                metadata.DisplayName,
                metadata.TypeDefinition);

            description.Unfiltered = false;

            return true;
        }

        /// <summary>
        /// Reads a set of nodes.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="nodesToRead"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public virtual void Read(
            OperationContext context,
            double maxAge,
            TimestampsToReturn timestampsToReturn,
            ReadValueIdCollection nodesToRead,
            out DataValueCollection values,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            if (nodesToRead == null)
            {
                throw new ArgumentNullException(nameof(nodesToRead));
            }

            if (maxAge < 0)
            {
                throw new ServiceResultException(StatusCodes.BadMaxAgeInvalid);
            }

            if (timestampsToReturn is < TimestampsToReturn.Source or > TimestampsToReturn.Neither)
            {
                throw new ServiceResultException(StatusCodes.BadTimestampsToReturnInvalid);
            }

            bool diagnosticsExist = false;
            values = new DataValueCollection(nodesToRead.Count);
            diagnosticInfos = new DiagnosticInfoCollection(nodesToRead.Count);

            // create empty list of errors.
            var errors = new List<ServiceResult>(values.Count);
            for (int ii = 0; ii < nodesToRead.Count; ii++)
            {
                errors.Add(null);
            }

            // add placeholder for each result.
            bool validItems = false;

            Utils.LogTrace(
                Utils.TraceMasks.ServiceDetail,
                "MasterNodeManager.Read - Count={0}",
                nodesToRead.Count);

            PrepareValidationCache(
                nodesToRead,
                out Dictionary<NodeId, List<object>> uniqueNodesReadAttributes);

            for (int ii = 0; ii < nodesToRead.Count; ii++)
            {
                // add default value to values collection
                values.Add(null);
                // add placeholder for diagnostics
                diagnosticInfos.Add(null);

                // pre-validate and pre-parse parameter.
                errors[ii] = ValidateReadRequest(
                    context,
                    nodesToRead[ii],
                    uniqueNodesReadAttributes);

                // return error status.
                if (ServiceResult.IsBad(errors[ii]))
                {
                    nodesToRead[ii].Processed = true;
                }
                // found at least one valid item.
                else
                {
                    nodesToRead[ii].Processed = false;
                    validItems = true;
                }
            }

            // call each node manager.
            if (validItems)
            {
                for (int ii = 0; ii < m_nodeManagers.Count; ii++)
                {
#if VERBOSE
                    Utils.LogTrace(
                        (int)Utils.TraceMasks.ServiceDetail,
                        "MasterNodeManager.Read - Calling NodeManager {0} of {1}",
                        ii,
                        m_nodeManagers.Count);
#endif
                    m_nodeManagers[ii].Read(context, maxAge, nodesToRead, values, errors);
                }
            }

            // process results.
            for (int ii = 0; ii < nodesToRead.Count; ii++)
            {
                DataValue value = values[ii];

                // set an error code for nodes that were not handled by any node manager.
                if (!nodesToRead[ii].Processed)
                {
                    value = values[ii] = new DataValue(
                        StatusCodes.BadNodeIdUnknown,
                        DateTime.UtcNow);
                    errors[ii] = new ServiceResult(values[ii].StatusCode);
                }

                // update the diagnostic info and ensure the status code in the data value is the same as the error code.
                if (errors[ii] != null && errors[ii].Code != StatusCodes.Good)
                {
                    value ??= values[ii] = new DataValue(errors[ii].Code, DateTime.UtcNow);

                    value.StatusCode = errors[ii].Code;

                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        diagnosticInfos[ii] = ServerUtils.CreateDiagnosticInfo(
                            Server,
                            context,
                            errors[ii]);
                        diagnosticsExist = true;
                    }
                }

                // apply the timestamp filters.
                if (timestampsToReturn is not TimestampsToReturn.Server and not TimestampsToReturn.Both)
                {
                    value.ServerTimestamp = DateTime.MinValue;
                }

                if (timestampsToReturn is not TimestampsToReturn.Source and not TimestampsToReturn.Both)
                {
                    value.SourceTimestamp = DateTime.MinValue;
                }
            }

            // clear the diagnostics array if no diagnostics requested or no errors occurred.
            UpdateDiagnostics(context, diagnosticsExist, ref diagnosticInfos);
        }

        /// <summary>
        /// Reads the history of a set of items.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public virtual void HistoryRead(
            OperationContext context,
            ExtensionObject historyReadDetails,
            TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints,
            HistoryReadValueIdCollection nodesToRead,
            out HistoryReadResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            // validate history details parameter.
            if (ExtensionObject.IsNull(historyReadDetails))
            {
                throw new ServiceResultException(StatusCodes.BadHistoryOperationInvalid);
            }

            if (historyReadDetails.Body is not HistoryReadDetails details)
            {
                throw new ServiceResultException(StatusCodes.BadHistoryOperationInvalid);
            }

            // create result lists.
            bool diagnosticsExist = false;
            results = new HistoryReadResultCollection(nodesToRead.Count);
            diagnosticInfos = new DiagnosticInfoCollection(nodesToRead.Count);

            // pre-validate items.
            bool validItems = false;
            // create empty list of errors.
            var errors = new List<ServiceResult>(results.Count);
            for (int ii = 0; ii < nodesToRead.Count; ii++)
            {
                errors.Add(null);
            }

            for (int ii = 0; ii < nodesToRead.Count; ii++)
            {
                // Limit permission restrictions to Client initiated service call
                HistoryReadResult result = null;
                DiagnosticInfo diagnosticInfo = null;

                // pre-validate and pre-parse parameter.
                errors[ii] = ValidateHistoryReadRequest(context, nodesToRead[ii]);

                // return error status.
                if (ServiceResult.IsBad(errors[ii]))
                {
                    nodesToRead[ii].Processed = true;
                    result = new HistoryReadResult { StatusCode = errors[ii].Code };

                    // add diagnostics if requested.
                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        diagnosticInfo = ServerUtils.CreateDiagnosticInfo(
                            Server,
                            context,
                            errors[ii]);
                        diagnosticsExist = true;
                    }
                }
                // found at least one valid item.
                else
                {
                    nodesToRead[ii].Processed = false;
                    validItems = true;
                }

                results.Add(result);
                diagnosticInfos.Add(diagnosticInfo);
            }

            // call each node manager.
            if (validItems)
            {
                foreach (INodeManager nodeManager in m_nodeManagers)
                {
                    nodeManager.HistoryRead(
                        context,
                        details,
                        timestampsToReturn,
                        releaseContinuationPoints,
                        nodesToRead,
                        results,
                        errors);
                }

                for (int ii = 0; ii < nodesToRead.Count; ii++)
                {
                    HistoryReadResult result = results[ii];

                    // set an error code for nodes that were not handled by any node manager.
                    if (!nodesToRead[ii].Processed)
                    {
                        nodesToRead[ii].Processed = true;
                        result = results[ii] = new HistoryReadResult();
                        result.StatusCode = StatusCodes.BadNodeIdUnknown;
                        errors[ii] = results[ii].StatusCode;
                    }

                    // update the diagnostic info and ensure the status code in the result is the same as the error code.
                    if (errors[ii] != null && errors[ii].Code != StatusCodes.Good)
                    {
                        result ??= results[ii] = new HistoryReadResult();

                        result.StatusCode = errors[ii].Code;

                        // add diagnostics if requested.
                        if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                        {
                            diagnosticInfos[ii] = ServerUtils.CreateDiagnosticInfo(
                                Server,
                                context,
                                errors[ii]);
                            diagnosticsExist = true;
                        }
                    }
                }
            }

            // clear the diagnostics array if no diagnostics requested or no errors occurred.
            UpdateDiagnostics(context, diagnosticsExist, ref diagnosticInfos);
        }

        /// <summary>
        /// Writes a set of values.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        public virtual void Write(
            OperationContext context,
            WriteValueCollection nodesToWrite,
            out StatusCodeCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (nodesToWrite == null)
            {
                throw new ArgumentNullException(nameof(nodesToWrite));
            }

            int count = nodesToWrite.Count;

            bool diagnosticsExist = false;
            results = new StatusCodeCollection(count);
            diagnosticInfos = new DiagnosticInfoCollection(count);

            // add placeholder for each result.
            bool validItems = false;

            for (int ii = 0; ii < count; ii++)
            {
                StatusCode result = StatusCodes.Good;
                DiagnosticInfo diagnosticInfo = null;

                // pre-validate and pre-parse parameter. Validate also access rights and role permissions
                ServiceResult error = ValidateWriteRequest(context, nodesToWrite[ii]);

                // return error status.
                if (ServiceResult.IsBad(error))
                {
                    nodesToWrite[ii].Processed = true;
                    result = error.Code;

                    // add diagnostics if requested.
                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        diagnosticInfo = ServerUtils.CreateDiagnosticInfo(Server, context, error);
                        diagnosticsExist = true;
                    }
                }
                // found at least one valid item.
                else
                {
                    nodesToWrite[ii].Processed = false;
                    validItems = true;
                }

                results.Add(result);
                diagnosticInfos.Add(diagnosticInfo);
            }

            // call each node manager.
            if (validItems)
            {
                var errors = new List<ServiceResult>(count);
                errors.AddRange(new ServiceResult[count]);

                foreach (INodeManager nodeManager in m_nodeManagers)
                {
                    nodeManager.Write(context, nodesToWrite, errors);
                }

                for (int ii = 0; ii < nodesToWrite.Count; ii++)
                {
                    if (!nodesToWrite[ii].Processed)
                    {
                        errors[ii] = StatusCodes.BadNodeIdUnknown;
                    }

                    if (errors[ii] != null && errors[ii].Code != StatusCodes.Good)
                    {
                        results[ii] = errors[ii].Code;

                        // add diagnostics if requested.
                        if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                        {
                            diagnosticInfos[ii] = ServerUtils.CreateDiagnosticInfo(
                                Server,
                                context,
                                errors[ii]);
                            diagnosticsExist = true;
                        }
                    }

                    ServerUtils.ReportWriteValue(
                        nodesToWrite[ii].NodeId,
                        nodesToWrite[ii].Value,
                        results[ii]);
                }
            }

            // clear the diagnostics array if no diagnostics requested or no errors occurred.
            UpdateDiagnostics(context, diagnosticsExist, ref diagnosticInfos);
        }

        /// <summary>
        /// Updates the history for a set of nodes.
        /// </summary>
        public virtual void HistoryUpdate(
            OperationContext context,
            ExtensionObjectCollection historyUpdateDetails,
            out HistoryUpdateResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
            Type detailsType = null;
            var nodesToUpdate = new List<HistoryUpdateDetails>();

            // verify that all extension objects in the list have the same type.
            foreach (ExtensionObject details in historyUpdateDetails)
            {
                if (detailsType == null)
                {
                    detailsType = details.Body.GetType();
                }

                if (!ExtensionObject.IsNull(details))
                {
                    nodesToUpdate.Add(details.Body as HistoryUpdateDetails);
                }
            }

            // create result lists.
            bool diagnosticsExist = false;
            results = new HistoryUpdateResultCollection(nodesToUpdate.Count);
            diagnosticInfos = new DiagnosticInfoCollection(nodesToUpdate.Count);

            // pre-validate items.
            bool validItems = false;

            // create empty list of errors.
            var errors = new List<ServiceResult>(results.Count);
            for (int ii = 0; ii < nodesToUpdate.Count; ii++)
            {
                errors.Add(null);
            }

            for (int ii = 0; ii < nodesToUpdate.Count; ii++)
            {
                HistoryUpdateResult result = null;
                DiagnosticInfo diagnosticInfo = null;

                // check the type of details parameter.
                ServiceResult error;
                if (nodesToUpdate[ii].GetType() != detailsType)
                {
                    error = StatusCodes.BadHistoryOperationInvalid;
                }
                // pre-validate and pre-parse parameter.
                else
                {
                    error = ValidateHistoryUpdateRequest(context, nodesToUpdate[ii]);
                }

                // return error status.
                if (ServiceResult.IsBad(error))
                {
                    nodesToUpdate[ii].Processed = true;
                    result = new HistoryUpdateResult { StatusCode = error.Code };

                    // add diagnostics if requested.
                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        diagnosticInfo = ServerUtils.CreateDiagnosticInfo(Server, context, error);
                        diagnosticsExist = true;
                    }
                }
                // found at least one valid item.
                else
                {
                    nodesToUpdate[ii].Processed = false;
                    validItems = true;
                }

                results.Add(result);
                diagnosticInfos.Add(diagnosticInfo);
            }

            // call each node manager.
            if (validItems)
            {
                foreach (INodeManager nodeManager in m_nodeManagers)
                {
                    nodeManager.HistoryUpdate(context, detailsType, nodesToUpdate, results, errors);
                }

                for (int ii = 0; ii < nodesToUpdate.Count; ii++)
                {
                    HistoryUpdateResult result = results[ii];

                    // set an error code for nodes that were not handled by any node manager.
                    if (!nodesToUpdate[ii].Processed)
                    {
                        nodesToUpdate[ii].Processed = true;
                        result = results[ii] = new HistoryUpdateResult();
                        result.StatusCode = StatusCodes.BadNodeIdUnknown;
                        errors[ii] = result.StatusCode;
                    }

                    // update the diagnostic info and ensure the status code in the result is the same as the error code.
                    if (errors[ii] != null && errors[ii].Code != StatusCodes.Good)
                    {
                        result ??= results[ii] = new HistoryUpdateResult();

                        result.StatusCode = errors[ii].Code;

                        // add diagnostics if requested.
                        if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                        {
                            diagnosticInfos[ii] = ServerUtils.CreateDiagnosticInfo(
                                Server,
                                context,
                                errors[ii]);
                            diagnosticsExist = true;
                        }
                    }
                }
            }

            // clear the diagnostics array if no diagnostics requested or no errors occurred.
            UpdateDiagnostics(context, diagnosticsExist, ref diagnosticInfos);
        }

        /// <summary>
        /// Calls a method defined on an object.
        /// </summary>
        public virtual ValueTask<(CallMethodResultCollection results, DiagnosticInfoCollection diagnosticInfos)> CallAsync(
            OperationContext context,
            CallMethodRequestCollection methodsToCall,
            CancellationToken cancellationToken = default)
        {
            return CallInternalAsync(
                context,
                methodsToCall,
                sync: false,
                cancellationToken);
        }

        /// <summary>
        /// Calls a method defined on an object.
        /// </summary>
        public virtual void Call(
            OperationContext context,
            CallMethodRequestCollection methodsToCall,
            out CallMethodResultCollection results,
            out DiagnosticInfoCollection diagnosticInfos)
        {
#pragma warning disable CA2012 // Use ValueTasks correctly
            (results, diagnosticInfos) = CallInternalAsync(
                context,
                methodsToCall,
                sync: true).Result;
#pragma warning restore CA2012 // Use ValueTasks correctly
        }

        /// <summary>
        /// Calls a method defined on an object.
        /// </summary>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="context"/> is <c>null</c>.</exception>
        public virtual async ValueTask<(
            CallMethodResultCollection results,
            DiagnosticInfoCollection diagnosticInfos
            )> CallInternalAsync(
                OperationContext context,
                CallMethodRequestCollection methodsToCall,
                bool sync,
                CancellationToken cancellationToken = default)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (methodsToCall == null)
            {
                throw new ArgumentNullException(nameof(methodsToCall));
            }

            bool diagnosticsExist = false;
            var results = new CallMethodResultCollection(methodsToCall.Count);
            var diagnosticInfos = new DiagnosticInfoCollection(methodsToCall.Count);
            var errors = new List<ServiceResult>(methodsToCall.Count);

            // add placeholder for each result.
            bool validItems = false;

            for (int ii = 0; ii < methodsToCall.Count; ii++)
            {
                results.Add(null);
                errors.Add(null);

                if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                {
                    diagnosticInfos.Add(null);
                }

                // validate request parameters.
                errors[ii] = ValidateCallRequestItem(context, methodsToCall[ii]);

                if (ServiceResult.IsBad(errors[ii]))
                {
                    methodsToCall[ii].Processed = true;

                    // add diagnostics if requested.
                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        diagnosticInfos[ii] = ServerUtils.CreateDiagnosticInfo(
                            Server,
                            context,
                            errors[ii]);
                        diagnosticsExist = true;
                    }

                    continue;
                }

                // found at least one valid item.
                validItems = true;
                methodsToCall[ii].Processed = false;
            }

            // call each node manager.
            if (validItems)
            {
                foreach (INodeManager nodeManager in m_nodeManagers)
                {
                    if (nodeManager is IAsyncNodeManager asyncNodeManager)
                    {
                        // call async node manager
                        if (sync)
                        {
                            Utils.LogWarning(
                                "Async Method called sychronously. Prefer using CallAsync for best performance. NodeManager={0}",
                                nodeManager);
                            asyncNodeManager.CallAsync(
                                context,
                                methodsToCall,
                                results,
                                errors,
                                cancellationToken).AsTask().GetAwaiter().GetResult();
                        }
                        else
                        {
                            await asyncNodeManager.CallAsync(
                                context,
                                methodsToCall,
                                results,
                                errors,
                                cancellationToken).ConfigureAwait(false);
                        }
                    }
                    else
                    {
                        nodeManager.Call(context, methodsToCall, results, errors);
                    }
                }
            }

            for (int ii = 0; ii < methodsToCall.Count; ii++)
            {
                // set an error code for calls that were not handled by any node manager.
                if (!methodsToCall[ii].Processed)
                {
                    results[ii] = new CallMethodResult();
                    errors[ii] = StatusCodes.BadNodeIdUnknown;
                }

                // update the diagnostic info and ensure the status code in the result is the same as the error code.
                if (errors[ii] != null && errors[ii].Code != StatusCodes.Good)
                {
                    if (results[ii] == null)
                    {
                        results[ii] = new CallMethodResult();
                    }

                    results[ii].StatusCode = errors[ii].Code;

                    // add diagnostics if requested.
                    if ((context.DiagnosticsMask & DiagnosticsMasks.OperationAll) != 0)
                    {
                        diagnosticInfos[ii] = ServerUtils.CreateDiagnosticInfo(
                            Server,
                            context,
                            errors[ii]);
                        diagnosticsExist = true;
                    }
                }
            }

            // clear the diagnostics array if no diagnostics requested or no errors occurred.
            UpdateDiagnostics(context, diagnosticsExist, ref diagnosticInfos);

            return (results, diagnosticInfos);
        }

        /// <summary>
        /// Handles condition refresh request.
        /// </summary>
        public virtual void ConditionRefresh(
            OperationContext context,
            IList<IEventMonitoredItem> monitoredItems)
        {
            foreach (INodeManager nodeManager in m_nodeManagers)
            {
                try
                {
                    nodeManager.ConditionRefresh(context, monitoredItems);
                }
                catch (Exception e)
                {
                    Utils.LogError(e, "Error calling ConditionRefresh on NodeManager.");
                }
            }
        }

        /// <summary>
        /// Creates a set of monitored items.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        /// <exception cref="ServiceResultException"></exception>
        public virtual void CreateMonitoredItems(
            OperationContext context,
            uint subscriptionId,
            double publishingInterval,
            TimestampsToReturn timestampsToReturn,
            IList<MonitoredItemCreateRequest> itemsToCreate,
            IList<ServiceResult> errors,
            IList<MonitoringFilterResult> filterResults,
            IList<IMonitoredItem> monitoredItems,
            bool createDurable)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (itemsToCreate == null)
            {
                throw new ArgumentNullException(nameof(itemsToCreate));
            }

            if (errors == null)
            {
                throw new ArgumentNullException(nameof(errors));
            }

            if (filterResults == null)
            {
                throw new ArgumentNullException(nameof(filterResults));
            }

            if (monitoredItems == null)
            {
                throw new ArgumentNullException(nameof(monitoredItems));
            }

            if (publishingInterval < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(publishingInterval));
            }

            if (timestampsToReturn is < TimestampsToReturn.Source or > TimestampsToReturn.Neither)
            {
                throw new ServiceResultException(StatusCodes.BadTimestampsToReturnInvalid);
            }

            // add placeholder for each result.
            bool validItems = false;

            for (int ii = 0; ii < itemsToCreate.Count; ii++)
            {
                // validate request parameters.
                errors[ii] = ValidateMonitoredItemCreateRequest(context, itemsToCreate[ii]);

                if (ServiceResult.IsBad(errors[ii]))
                {
                    itemsToCreate[ii].Processed = true;
                    continue;
                }

                // found at least one valid item.
                validItems = true;
                itemsToCreate[ii].Processed = false;
            }

            // call each node manager.
            if (validItems)
            {
                // create items for event filters.
                CreateMonitoredItemsForEvents(
                    context,
                    subscriptionId,
                    publishingInterval,
                    timestampsToReturn,
                    itemsToCreate,
                    errors,
                    filterResults,
                    monitoredItems,
                    createDurable,
                    ref m_lastMonitoredItemId);

                // create items for data access.
                foreach (INodeManager nodeManager in m_nodeManagers)
                {
                    nodeManager.CreateMonitoredItems(
                        context,
                        subscriptionId,
                        publishingInterval,
                        timestampsToReturn,
                        itemsToCreate,
                        errors,
                        filterResults,
                        monitoredItems,
                        createDurable,
                        ref m_lastMonitoredItemId);
                }

                // fill results for unknown nodes.
                for (int ii = 0; ii < errors.Count; ii++)
                {
                    if (!itemsToCreate[ii].Processed)
                    {
                        errors[ii] = new ServiceResult(StatusCodes.BadNodeIdUnknown);
                    }
                }
            }
        }

        /// <summary>
        /// Create monitored items for event subscriptions.
        /// </summary>
        private void CreateMonitoredItemsForEvents(
            OperationContext context,
            uint subscriptionId,
            double publishingInterval,
            TimestampsToReturn timestampsToReturn,
            IList<MonitoredItemCreateRequest> itemsToCreate,
            IList<ServiceResult> errors,
            IList<MonitoringFilterResult> filterResults,
            IList<IMonitoredItem> monitoredItems,
            bool createDurable,
            ref long globalIdCounter)
        {
            for (int ii = 0; ii < itemsToCreate.Count; ii++)
            {
                MonitoredItemCreateRequest itemToCreate = itemsToCreate[ii];

                if (!itemToCreate.Processed)
                {
                    // must make sure the filter is not null before checking its type.
                    if (ExtensionObject.IsNull(itemToCreate.RequestedParameters.Filter))
                    {
                        continue;
                    }

                    // all event subscriptions required an event filter.
                    if (itemToCreate.RequestedParameters.Filter.Body is not EventFilter filter)
                    {
                        continue;
                    }

                    itemToCreate.Processed = true;

                    // only the value attribute may be used with an event subscription.
                    if (itemToCreate.ItemToMonitor.AttributeId != Attributes.EventNotifier)
                    {
                        errors[ii] = StatusCodes.BadFilterNotAllowed;
                        continue;
                    }

                    // the index range parameter has no meaning for event subscriptions.
                    if (!string.IsNullOrEmpty(itemToCreate.ItemToMonitor.IndexRange))
                    {
                        errors[ii] = StatusCodes.BadIndexRangeInvalid;
                        continue;
                    }

                    // the data encoding has no meaning for event subscriptions.
                    if (!QualifiedName.IsNull(itemToCreate.ItemToMonitor.DataEncoding))
                    {
                        errors[ii] = StatusCodes.BadDataEncodingInvalid;
                        continue;
                    }

                    // validate the event filter.
                    EventFilter.Result result = filter.Validate(
                        new FilterContext(Server.NamespaceUris, Server.TypeTree, context));

                    if (ServiceResult.IsBad(result.Status))
                    {
                        errors[ii] = result.Status;
                        filterResults[ii] = result.ToEventFilterResult(
                            context.DiagnosticsMask,
                            context.StringTable);
                        continue;
                    }

                    // check if a valid node.
                    object handle = GetManagerHandle(
                        itemToCreate.ItemToMonitor.NodeId,
                        out INodeManager nodeManager);

                    if (handle == null)
                    {
                        errors[ii] = StatusCodes.BadNodeIdUnknown;
                        continue;
                    }
                    NodeMetadata nodeMetadata = nodeManager.GetNodeMetadata(
                        context,
                        handle,
                        BrowseResultMask.All);

                    errors[ii] = ValidateRolePermissions(
                        context,
                        nodeMetadata,
                        PermissionType.ReceiveEvents);

                    if (ServiceResult.IsBad(errors[ii]))
                    {
                        continue;
                    }

                    // create a globally unique identifier.
                    uint monitoredItemId = Utils.IncrementIdentifier(ref globalIdCounter);

                    IEventMonitoredItem monitoredItem = Server.EventManager.CreateMonitoredItem(
                        context,
                        nodeManager,
                        handle,
                        subscriptionId,
                        monitoredItemId,
                        timestampsToReturn,
                        publishingInterval,
                        itemToCreate,
                        filter,
                        createDurable);

                    // subscribe to all node managers.
                    if (itemToCreate.ItemToMonitor.NodeId == Objects.Server)
                    {
                        foreach (INodeManager manager in m_nodeManagers)
                        {
                            try
                            {
                                manager.SubscribeToAllEvents(
                                    context,
                                    subscriptionId,
                                    monitoredItem,
                                    false);
                            }
                            catch (Exception e)
                            {
                                Utils.LogError(
                                    e,
                                    "NodeManager threw an exception subscribing to all events. NodeManager={0}",
                                    manager);
                            }
                        }
                    }
                    // only subscribe to the node manager that owns the node.
                    else
                    {
                        ServiceResult error = nodeManager.SubscribeToEvents(
                            context,
                            handle,
                            subscriptionId,
                            monitoredItem,
                            false);

                        if (ServiceResult.IsBad(error))
                        {
                            Server.EventManager.DeleteMonitoredItem(monitoredItem.Id);
                            errors[ii] = error;
                            continue;
                        }
                    }

                    monitoredItems[ii] = monitoredItem;
                    errors[ii] = StatusCodes.Good;
                }
            }
        }

        /// <summary>
        /// Restore a set of monitored items after a Server Restart.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="itemsToRestore"/> is <c>null</c>.</exception>
        /// <exception cref="InvalidOperationException"></exception>
        public virtual void RestoreMonitoredItems(
            IList<IStoredMonitoredItem> itemsToRestore,
            IList<IMonitoredItem> monitoredItems,
            IUserIdentity savedOwnerIdentity)
        {
            if (itemsToRestore == null)
            {
                throw new ArgumentNullException(nameof(itemsToRestore));
            }

            if (monitoredItems == null)
            {
                throw new ArgumentNullException(nameof(monitoredItems));
            }

            if (Server.IsRunning)
            {
                throw new InvalidOperationException(
                    "Subscription restore can only occur on startup");
            }

            // create items for event filters.
            RestoreMonitoredItemsForEvents(itemsToRestore, monitoredItems);

            // create items for data access.
            foreach (INodeManager nodeManager in m_nodeManagers)
            {
                nodeManager.RestoreMonitoredItems(
                    itemsToRestore,
                    monitoredItems,
                    savedOwnerIdentity);
            }

            m_lastMonitoredItemId = itemsToRestore.Max(i => i.Id);
        }

        /// <summary>
        /// Restore monitored items for event subscriptions.
        /// </summary>
        private void RestoreMonitoredItemsForEvents(
            IList<IStoredMonitoredItem> itemsToRestore,
            IList<IMonitoredItem> monitoredItems)
        {
            for (int ii = 0; ii < itemsToRestore.Count; ii++)
            {
                IStoredMonitoredItem item = itemsToRestore[ii];

                if (!item.IsRestored)
                {
                    // all event subscriptions required an event filter.
                    if (item.OriginalFilter is not EventFilter)
                    {
                        continue;
                    }

                    item.IsRestored = true;

                    // check if a valid node.
                    object handle = GetManagerHandle(item.NodeId, out INodeManager nodeManager);

                    if (handle == null)
                    {
                        continue;
                    }

                    IEventMonitoredItem monitoredItem = Server.EventManager.RestoreMonitoredItem(
                        nodeManager,
                        handle,
                        item);

                    // subscribe to all node managers.
                    if (item.NodeId == Objects.Server)
                    {
                        foreach (INodeManager manager in m_nodeManagers)
                        {
                            try
                            {
                                manager.SubscribeToAllEvents(
                                    new OperationContext(monitoredItem),
                                    monitoredItem.SubscriptionId,
                                    monitoredItem,
                                    false);
                            }
                            catch (Exception e)
                            {
                                Utils.LogError(
                                    e,
                                    "NodeManager threw an exception subscribing to all events. NodeManager={0}",
                                    manager);
                            }
                        }
                    }
                    // only subscribe to the node manager that owns the node.
                    else
                    {
                        ServiceResult error = nodeManager.SubscribeToEvents(
                            new OperationContext(monitoredItem),
                            handle,
                            monitoredItem.SubscriptionId,
                            monitoredItem,
                            false);

                        if (ServiceResult.IsBad(error))
                        {
                            Server.EventManager.DeleteMonitoredItem(monitoredItem.Id);
                            continue;
                        }
                    }

                    monitoredItems[ii] = monitoredItem;
                }
            }
        }

        /// <summary>
        /// Modifies a set of monitored items.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        /// <exception cref="ServiceResultException"></exception>
        public virtual void ModifyMonitoredItems(
            OperationContext context,
            TimestampsToReturn timestampsToReturn,
            IList<IMonitoredItem> monitoredItems,
            IList<MonitoredItemModifyRequest> itemsToModify,
            IList<ServiceResult> errors,
            IList<MonitoringFilterResult> filterResults)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (itemsToModify == null)
            {
                throw new ArgumentNullException(nameof(itemsToModify));
            }

            if (monitoredItems == null)
            {
                throw new ArgumentNullException(nameof(monitoredItems));
            }

            if (errors == null)
            {
                throw new ArgumentNullException(nameof(errors));
            }

            if (filterResults == null)
            {
                throw new ArgumentNullException(nameof(filterResults));
            }

            if (timestampsToReturn is < TimestampsToReturn.Source or > TimestampsToReturn.Neither)
            {
                throw new ServiceResultException(StatusCodes.BadTimestampsToReturnInvalid);
            }

            bool validItems = false;

            for (int ii = 0; ii < itemsToModify.Count; ii++)
            {
                // check for errors.
                if (ServiceResult.IsBad(errors[ii]) || monitoredItems[ii] == null)
                {
                    itemsToModify[ii].Processed = true;
                    continue;
                }

                // validate request parameters.
                errors[ii] = ValidateMonitoredItemModifyRequest(itemsToModify[ii]);

                if (ServiceResult.IsBad(errors[ii]))
                {
                    itemsToModify[ii].Processed = true;
                    continue;
                }

                // found at least one valid item.
                validItems = true;
                itemsToModify[ii].Processed = false;
            }

            // call each node manager.
            if (validItems)
            {
                // modify items for event filters.
                ModifyMonitoredItemsForEvents(
                    context,
                    timestampsToReturn,
                    monitoredItems,
                    itemsToModify,
                    errors,
                    filterResults);

                // let each node manager figure out which items it owns.
                foreach (INodeManager nodeManager in m_nodeManagers)
                {
                    nodeManager.ModifyMonitoredItems(
                        context,
                        timestampsToReturn,
                        monitoredItems,
                        itemsToModify,
                        errors,
                        filterResults);
                }

                // update results.
                for (int ii = 0; ii < errors.Count; ii++)
                {
                    if (!itemsToModify[ii].Processed)
                    {
                        errors[ii] = new ServiceResult(StatusCodes.BadMonitoredItemIdInvalid);
                    }
                }
            }
        }

        /// <summary>
        /// Modify monitored items for event subscriptions.
        /// </summary>
        private void ModifyMonitoredItemsForEvents(
            OperationContext context,
            TimestampsToReturn timestampsToReturn,
            IList<IMonitoredItem> monitoredItems,
            IList<MonitoredItemModifyRequest> itemsToModify,
            IList<ServiceResult> errors,
            IList<MonitoringFilterResult> filterResults)
        {
            for (int ii = 0; ii < itemsToModify.Count; ii++)
            {
                // all event subscriptions are handled by the event manager.
                if (monitoredItems[ii] is not IEventMonitoredItem monitoredItem ||
                    (monitoredItem.MonitoredItemType & MonitoredItemTypeMask.Events) == 0)
                {
                    continue;
                }

                MonitoredItemModifyRequest itemToModify = itemsToModify[ii];
                itemToModify.Processed = true;

                // check for a valid filter.
                if (ExtensionObject.IsNull(itemToModify.RequestedParameters.Filter))
                {
                    errors[ii] = StatusCodes.BadEventFilterInvalid;
                    continue;
                }

                // all event subscriptions required an event filter.

                if (itemToModify.RequestedParameters.Filter.Body is not EventFilter filter)
                {
                    errors[ii] = StatusCodes.BadEventFilterInvalid;
                    continue;
                }

                // validate the event filter.
                EventFilter.Result result = filter.Validate(
                    new FilterContext(Server.NamespaceUris, Server.TypeTree, context));

                if (ServiceResult.IsBad(result.Status))
                {
                    errors[ii] = result.Status;
                    filterResults[ii] = result.ToEventFilterResult(
                        context.DiagnosticsMask,
                        context.StringTable);
                    continue;
                }

                // modify the item.
                Server.EventManager.ModifyMonitoredItem(
                    context,
                    monitoredItem,
                    timestampsToReturn,
                    itemToModify,
                    filter);

                // subscribe to all node managers.
                if ((monitoredItem.MonitoredItemType & MonitoredItemTypeMask.AllEvents) != 0)
                {
                    foreach (INodeManager manager in m_nodeManagers)
                    {
                        manager.SubscribeToAllEvents(
                            context,
                            monitoredItem.SubscriptionId,
                            monitoredItem,
                            false);
                    }
                }
                // only subscribe to the node manager that owns the node.
                else
                {
                    monitoredItem.NodeManager.SubscribeToEvents(
                        context,
                        monitoredItem.ManagerHandle,
                        monitoredItem.SubscriptionId,
                        monitoredItem,
                        false);
                }

                errors[ii] = StatusCodes.Good;
            }
        }

        /// <summary>
        /// Transfers a set of monitored items.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        public virtual void TransferMonitoredItems(
            OperationContext context,
            bool sendInitialValues,
            IList<IMonitoredItem> monitoredItems,
            IList<ServiceResult> errors)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (monitoredItems == null)
            {
                throw new ArgumentNullException(nameof(monitoredItems));
            }

            if (errors == null)
            {
                throw new ArgumentNullException(nameof(errors));
            }

            var processedItems = new List<bool>(monitoredItems.Count);

            // preset results for unknown nodes
            for (int ii = 0; ii < monitoredItems.Count; ii++)
            {
                processedItems.Add(monitoredItems[ii] == null);
                errors[ii] = StatusCodes.BadMonitoredItemIdInvalid;
            }

            // call each node manager.
            foreach (INodeManager nodeManager in m_nodeManagers)
            {
                nodeManager.TransferMonitoredItems(
                    context,
                    sendInitialValues,
                    monitoredItems,
                    processedItems,
                    errors);
            }
        }

        /// <summary>
        /// Deletes a set of monitored items.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        public virtual void DeleteMonitoredItems(
            OperationContext context,
            uint subscriptionId,
            IList<IMonitoredItem> itemsToDelete,
            IList<ServiceResult> errors)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (itemsToDelete == null)
            {
                throw new ArgumentNullException(nameof(itemsToDelete));
            }

            if (errors == null)
            {
                throw new ArgumentNullException(nameof(errors));
            }

            var processedItems = new List<bool>(itemsToDelete.Count);

            for (int ii = 0; ii < itemsToDelete.Count; ii++)
            {
                processedItems.Add(ServiceResult.IsBad(errors[ii]) || itemsToDelete[ii] == null);
            }

            // delete items for event filters.
            DeleteMonitoredItemsForEvents(
                context,
                subscriptionId,
                itemsToDelete,
                processedItems,
                errors);

            // call each node manager.
            foreach (INodeManager nodeManager in m_nodeManagers)
            {
                nodeManager.DeleteMonitoredItems(context, itemsToDelete, processedItems, errors);
            }

            // fill results for unknown nodes.
            for (int ii = 0; ii < errors.Count; ii++)
            {
                if (!processedItems[ii])
                {
                    errors[ii] = StatusCodes.BadMonitoredItemIdInvalid;
                }
            }
        }

        /// <summary>
        /// Delete monitored items for event subscriptions.
        /// </summary>
        private void DeleteMonitoredItemsForEvents(
            OperationContext context,
            uint subscriptionId,
            IList<IMonitoredItem> monitoredItems,
            List<bool> processedItems,
            IList<ServiceResult> errors)
        {
            for (int ii = 0; ii < monitoredItems.Count; ii++)
            {
                // all event subscriptions are handled by the event manager.
                if (monitoredItems[ii] is not IEventMonitoredItem monitoredItem ||
                    (monitoredItem.MonitoredItemType & MonitoredItemTypeMask.Events) == 0)
                {
                    continue;
                }

                processedItems[ii] = true;

                // unsubscribe to all node managers.
                if ((monitoredItem.MonitoredItemType & MonitoredItemTypeMask.AllEvents) != 0)
                {
                    foreach (INodeManager manager in m_nodeManagers)
                    {
                        manager.SubscribeToAllEvents(context, subscriptionId, monitoredItem, true);
                    }
                }
                // only unsubscribe to the node manager that owns the node.
                else
                {
                    monitoredItem.NodeManager.SubscribeToEvents(
                        context,
                        monitoredItem.ManagerHandle,
                        subscriptionId,
                        monitoredItem,
                        true);
                }

                // delete the item.
                Server.EventManager.DeleteMonitoredItem(monitoredItem.Id);

                // success.
                errors[ii] = StatusCodes.Good;
            }
        }

        /// <summary>
        /// Changes the monitoring mode for a set of items.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="context"/> is <c>null</c>.</exception>
        public virtual void SetMonitoringMode(
            OperationContext context,
            MonitoringMode monitoringMode,
            IList<IMonitoredItem> itemsToModify,
            IList<ServiceResult> errors)
        {
            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            if (itemsToModify == null)
            {
                throw new ArgumentNullException(nameof(itemsToModify));
            }

            if (errors == null)
            {
                throw new ArgumentNullException(nameof(errors));
            }

            // call each node manager.
            var processedItems = new List<bool>(itemsToModify.Count);

            for (int ii = 0; ii < itemsToModify.Count; ii++)
            {
                processedItems.Add(ServiceResult.IsBad(errors[ii]) || itemsToModify[ii] == null);
            }

            // delete items for event filters.
            SetMonitoringModeForEvents(
                context,
                monitoringMode,
                itemsToModify,
                processedItems,
                errors);

            foreach (INodeManager nodeManager in m_nodeManagers)
            {
                nodeManager.SetMonitoringMode(
                    context,
                    monitoringMode,
                    itemsToModify,
                    processedItems,
                    errors);
            }

            // fill results for unknown nodes.
            for (int ii = 0; ii < errors.Count; ii++)
            {
                if (!processedItems[ii])
                {
                    errors[ii] = StatusCodes.BadMonitoredItemIdInvalid;
                }
            }
        }

        /// <summary>
        /// Delete monitored items for event subscriptions.
        /// </summary>
        private static void SetMonitoringModeForEvents(
            OperationContext context,
            MonitoringMode monitoringMode,
            IList<IMonitoredItem> monitoredItems,
            List<bool> processedItems,
            IList<ServiceResult> errors)
        {
            for (int ii = 0; ii < monitoredItems.Count; ii++)
            {
                // all event subscriptions are handled by the event manager.
                if (monitoredItems[ii] is not IEventMonitoredItem monitoredItem ||
                    (monitoredItem.MonitoredItemType & MonitoredItemTypeMask.Events) == 0)
                {
                    continue;
                }

                processedItems[ii] = true;

                // set the monitoring mode.
                monitoredItem.SetMonitoringMode(monitoringMode);

                // success.
                errors[ii] = StatusCodes.Good;
            }
        }

        /// <summary>
        /// The server that the node manager belongs to.
        /// </summary>
        protected IServerInternal Server { get; }

        /// <summary>
        /// The node managers being managed.
        /// </summary>
        public IList<INodeManager> NodeManagers => m_nodeManagers;

        /// <summary>
        /// The namespace managers being managed
        /// </summary>
        internal INodeManager[][] NamespaceManagers { get; private set; }

        /// <summary>
        /// Validates a monitoring attributes parameter.
        /// </summary>
        protected static ServiceResult ValidateMonitoringAttributes(MonitoringParameters attributes)
        {
            // check for null structure.
            if (attributes == null)
            {
                return new ServiceResult(StatusCodes.BadStructureMissing);
            }

            // check for known filter.
            if (!ExtensionObject.IsNull(attributes.Filter) &&
                attributes.Filter.Body is not MonitoringFilter)
            {
                return new ServiceResult(StatusCodes.BadMonitoredItemFilterInvalid);
            }

            // passed basic validation.
            return null;
        }

        /// <summary>
        /// Validates a monitoring filter.
        /// </summary>
        protected static ServiceResult ValidateMonitoringFilter(ExtensionObject filter)
        {
            // check that no filter is specified for non-value attributes.
            if (!ExtensionObject.IsNull(filter))
            {
                // validate data change filter.
                if (filter.Body is DataChangeFilter datachangeFilter)
                {
                    ServiceResult error = datachangeFilter.Validate();

                    if (ServiceResult.IsBad(error))
                    {
                        return error;
                    }
                }
            }

            // passed basic validation.
            return null;
        }

        /// <summary>
        /// Validates a monitored item create request parameter.
        /// </summary>
        protected ServiceResult ValidateMonitoredItemCreateRequest(
            OperationContext operationContext,
            MonitoredItemCreateRequest item)
        {
            // check for null structure.
            if (item == null)
            {
                return new ServiceResult(StatusCodes.BadStructureMissing);
            }

            // validate read value id component. Validate also access rights and permissions
            ServiceResult error = ValidateReadRequest(
                operationContext,
                item.ItemToMonitor,
                null,
                true);

            if (ServiceResult.IsBad(error))
            {
                return error;
            }

            // check for valid monitoring mode.
            if ((int)item.MonitoringMode is < 0 or > ((int)MonitoringMode.Reporting))
            {
                return new ServiceResult(StatusCodes.BadMonitoringModeInvalid);
            }

            // check for null structure.
            MonitoringParameters attributes = item.RequestedParameters;

            error = ValidateMonitoringAttributes(attributes);

            if (ServiceResult.IsBad(error))
            {
                return error;
            }

            // check that no filter is specified for non-value attributes.
            if (item.ItemToMonitor.AttributeId is not Attributes.Value and not Attributes
                .EventNotifier)
            {
                if (!ExtensionObject.IsNull(attributes.Filter))
                {
                    return new ServiceResult(StatusCodes.BadFilterNotAllowed);
                }
            }
            else
            {
                error = ValidateMonitoringFilter(attributes.Filter);

                if (ServiceResult.IsBad(error))
                {
                    return error;
                }
            }

            // passed basic validation.
            return null;
        }

        /// <summary>
        /// Validates a monitored item modify request parameter.
        /// </summary>
        protected static ServiceResult ValidateMonitoredItemModifyRequest(
            MonitoredItemModifyRequest item)
        {
            // check for null structure.
            if (item == null)
            {
                return new ServiceResult(StatusCodes.BadStructureMissing);
            }

            // check for null structure.
            MonitoringParameters attributes = item.RequestedParameters;

            ServiceResult error = ValidateMonitoringAttributes(attributes);

            if (ServiceResult.IsBad(error))
            {
                return error;
            }

            // validate monitoring filter.
            error = ValidateMonitoringFilter(attributes.Filter);

            if (ServiceResult.IsBad(error))
            {
                return error;
            }

            // passed basic validation.
            return null;
        }

        /// <summary>
        /// Validates a call request item parameter. It validates also access rights and role permissions
        /// </summary>
        protected ServiceResult ValidateCallRequestItem(
            OperationContext operationContext,
            CallMethodRequest callMethodRequest)
        {
            // check for null structure.
            if (callMethodRequest == null)
            {
                return StatusCodes.BadStructureMissing;
            }

            // check object id.
            if (NodeId.IsNull(callMethodRequest.ObjectId))
            {
                return StatusCodes.BadNodeIdInvalid;
            }

            // check method id.
            if (NodeId.IsNull(callMethodRequest.MethodId))
            {
                return StatusCodes.BadMethodInvalid;
            }

            // check input arguments
            if (callMethodRequest.InputArguments == null)
            {
                return StatusCodes.BadStructureMissing;
            }

            return StatusCodes.Good;
        }

        /// <summary>
        /// Validates a Read or MonitoredItemCreate request. It validates also access rights and role permissions
        /// </summary>
        protected ServiceResult ValidateReadRequest(
            OperationContext operationContext,
            ReadValueId readValueId,
            Dictionary<NodeId, List<object>> uniqueNodesReadAttributes = null,
            bool permissionsOnly = false)
        {
            ServiceResult serviceResult = ReadValueId.Validate(readValueId);

            if (ServiceResult.IsGood(serviceResult))
            {
                //any attribute other than Value or RolePermissions
                PermissionType requestedPermission = PermissionType.Browse;
                if (readValueId.AttributeId == Attributes.RolePermissions)
                {
                    requestedPermission = PermissionType.ReadRolePermissions;
                }
                else if (readValueId.AttributeId == Attributes.Value)
                {
                    requestedPermission = PermissionType.Read;
                }

                // check access rights and role permissions
                serviceResult = ValidatePermissions(
                    operationContext,
                    readValueId.NodeId,
                    requestedPermission,
                    uniqueNodesReadAttributes,
                    permissionsOnly);
            }
            return serviceResult;
        }

        /// <summary>
        /// Validates a Write request. It validates also access rights and role permissions
        /// </summary>
        protected ServiceResult ValidateWriteRequest(
            OperationContext operationContext,
            WriteValue writeValue)
        {
            ServiceResult serviceResult = WriteValue.Validate(writeValue);

            if (ServiceResult.IsGood(serviceResult))
            {
                PermissionType requestedPermission = PermissionType.WriteAttribute; //any attribute other than Value, RolePermissions or Historizing
                if (writeValue.AttributeId == Attributes.RolePermissions)
                {
                    requestedPermission = PermissionType.WriteRolePermissions;
                }
                else if (writeValue.AttributeId == Attributes.Historizing)
                {
                    requestedPermission = PermissionType.WriteHistorizing;
                }
                else if (writeValue.AttributeId == Attributes.Value)
                {
                    requestedPermission = PermissionType.Write;
                }

                // check access rights and permissions
                serviceResult = ValidatePermissions(
                    operationContext,
                    writeValue.NodeId,
                    requestedPermission,
                    null,
                    true);
            }
            return serviceResult;
        }

        /// <summary>
        /// Validates a HistoryRead request. It validates also access rights and role permissions
        /// </summary>
        protected ServiceResult ValidateHistoryReadRequest(
            OperationContext operationContext,
            HistoryReadValueId historyReadValueId)
        {
            ServiceResult serviceResult = HistoryReadValueId.Validate(historyReadValueId);

            if (ServiceResult.IsGood(serviceResult))
            {
                // check access rights and permissions
                serviceResult = ValidatePermissions(
                    operationContext,
                    historyReadValueId.NodeId,
                    PermissionType.ReadHistory,
                    null,
                    true);
            }
            return serviceResult;
        }

        /// <summary>
        ///  Validates a HistoryUpdate request. It validates also access rights and role permissions
        /// </summary>
        protected ServiceResult ValidateHistoryUpdateRequest(
            OperationContext operationContext,
            HistoryUpdateDetails historyUpdateDetails)
        {
            ServiceResult serviceResult = HistoryUpdateDetails.Validate(historyUpdateDetails);

            if (ServiceResult.IsGood(serviceResult))
            {
                // check access rights and permissions
                PermissionType requiredPermission = DetermineHistoryAccessPermission(
                    historyUpdateDetails);
                serviceResult = ValidatePermissions(
                    operationContext,
                    historyUpdateDetails.NodeId,
                    requiredPermission,
                    null,
                    true);
            }

            return serviceResult;
        }

        /// <summary>
        /// Check if the Base NodeClass attributes and NameSpace meta-data attributes
        /// are valid for the given operation context of the specified node.
        /// </summary>
        /// <param name="context">The Operation Context</param>
        /// <param name="nodeId">The node whose attributes are validated</param>
        /// <param name="requestedPermision">The requested permission</param>
        /// <param name="uniqueNodesServiceAttributes">The cache holding the values of the attributes neeeded to be used in subsequent calls</param>
        /// <param name="permissionsOnly">Only the AccessRestrictions and RolePermission attributes are read. Should be false if uniqueNodesServiceAttributes is not null</param>
        /// <returns>StatusCode Good if permission is granted, BadUserAccessDenied if not granted
        /// or a bad status code describing the validation process failure </returns>
        protected ServiceResult ValidatePermissions(
            OperationContext context,
            NodeId nodeId,
            PermissionType requestedPermision,
            Dictionary<NodeId, List<object>> uniqueNodesServiceAttributes = null,
            bool permissionsOnly = false)
        {
            if (context.Session != null)
            {
                object nodeHandle = GetManagerHandle(nodeId, out INodeManager nodeManager);

                return ValidatePermissions(
                    context,
                    nodeManager,
                    nodeHandle,
                    requestedPermision,
                    uniqueNodesServiceAttributes,
                    permissionsOnly);
            }
            return StatusCodes.Good;
        }

        /// <summary>
        /// Check if the Base NodeClass attributes and NameSpace meta-data attributes
        /// are valid for the given operation context of the specified node.
        /// </summary>
        /// <param name="context">The Operation Context</param>
        /// <param name="nodeManager">The node manager handling the nodeHandle</param>
        /// <param name="nodeHandle">The node handle of the node whose attributes are validated</param>
        /// <param name="requestedPermision">The requested permission</param>
        /// <param name="uniqueNodesServiceAttributes">The cache holding the values of the attributes neeeded to be used in subsequent calls</param>
        /// <param name="permissionsOnly">Only the AccessRestrictions and RolePermission attributes are read. Should be false if uniqueNodesServiceAttributes is not null</param>
        /// <returns>StatusCode Good if permission is granted, BadUserAccessDenied if not granted
        /// or a bad status code describing the validation process failure </returns>
        protected ServiceResult ValidatePermissions(
            OperationContext context,
            INodeManager nodeManager,
            object nodeHandle,
            PermissionType requestedPermision,
            Dictionary<NodeId, List<object>> uniqueNodesServiceAttributes = null,
            bool permissionsOnly = false)
        {
            ServiceResult serviceResult = StatusCodes.Good;

            // check if validation is necessary
            if (context.Session != null && nodeManager != null && nodeHandle != null)
            {
                NodeMetadata nodeMetadata = null;
                // First attempt to retrieve just the Permission metadata with or without cache optimization
                // If it happens that nodemanager does not fully implement INodeManager2.GetPermissionMetadata or not INodeManager2,
                // fallback to INodeManager.GetNodeMetadata
                if (nodeManager is INodeManager2 nodeManager2)
                {
                    nodeMetadata = nodeManager2.GetPermissionMetadata(
                        context,
                        nodeHandle,
                        BrowseResultMask.NodeClass,
                        uniqueNodesServiceAttributes,
                        permissionsOnly);
                }
                // If not INodeManager2 or GetPermissionMetadata() returns null.
                nodeMetadata ??= nodeManager.GetNodeMetadata(
                    context,
                    nodeHandle,
                    BrowseResultMask.NodeClass);

                if (nodeMetadata != null)
                {
                    // check RolePermissions
                    serviceResult = ValidateRolePermissions(
                        context,
                        nodeMetadata,
                        requestedPermision);

                    if (ServiceResult.IsGood(serviceResult))
                    {
                        // check AccessRestrictions
                        serviceResult = ValidateAccessRestrictions(context, nodeMetadata);
                    }
                }
            }

            return serviceResult;
        }

        /// <summary>
        /// Validate the AccessRestrictions attribute
        /// </summary>
        /// <param name="context">The Operation Context</param>
        /// <param name="nodeMetadata">Metadata</param>
        /// <returns>Good if the AccessRestrictions passes the validation</returns>
        protected static ServiceResult ValidateAccessRestrictions(
            OperationContext context,
            NodeMetadata nodeMetadata)
        {
            ServiceResult serviceResult = StatusCodes.Good;
            AccessRestrictionType restrictions = AccessRestrictionType.None;

            if (nodeMetadata.AccessRestrictions != AccessRestrictionType.None)
            {
                restrictions = nodeMetadata.AccessRestrictions;
            }
            else if (nodeMetadata.DefaultAccessRestrictions != AccessRestrictionType.None)
            {
                restrictions = nodeMetadata.DefaultAccessRestrictions;
            }
            if (restrictions != AccessRestrictionType.None)
            {
                bool encryptionRequired =
                    (restrictions & AccessRestrictionType.EncryptionRequired) ==
                    AccessRestrictionType.EncryptionRequired;
                bool signingRequired =
                    (restrictions & AccessRestrictionType.SigningRequired) ==
                    AccessRestrictionType.SigningRequired;
                bool sessionRequired =
                    (restrictions & AccessRestrictionType.SessionRequired) ==
                    AccessRestrictionType.SessionRequired;
                bool applyRestrictionsToBrowse =
                    (restrictions & AccessRestrictionType.ApplyRestrictionsToBrowse) ==
                    AccessRestrictionType.ApplyRestrictionsToBrowse;

                bool browseOperation =
                    context.RequestType
                        is RequestType.Browse
                            or RequestType.BrowseNext
                            or RequestType.TranslateBrowsePathsToNodeIds;

                if ((
                        encryptionRequired &&
                        context.ChannelContext.EndpointDescription
                            .SecurityMode != MessageSecurityMode.SignAndEncrypt &&
                        context.ChannelContext.EndpointDescription.TransportProfileUri !=
                            Profiles.HttpsBinaryTransport &&
                        ((applyRestrictionsToBrowse && browseOperation) || !browseOperation)
                    ) ||
                    (
                        signingRequired &&
                        context.ChannelContext.EndpointDescription
                            .SecurityMode != MessageSecurityMode.Sign &&
                        context.ChannelContext.EndpointDescription
                            .SecurityMode != MessageSecurityMode.SignAndEncrypt &&
                        context.ChannelContext.EndpointDescription.TransportProfileUri !=
                            Profiles.HttpsBinaryTransport &&
                        ((applyRestrictionsToBrowse && browseOperation) || !browseOperation)
                    ) ||
                    (sessionRequired && context.Session == null))
                {
                    serviceResult = ServiceResult.Create(
                        StatusCodes.BadSecurityModeInsufficient,
                        "Access restricted to nodeId {0} due to insufficient security mode.",
                        nodeMetadata.NodeId);
                }
            }

            return serviceResult;
        }

        /// <summary>
        /// Validates the role permissions
        /// </summary>
        protected internal static ServiceResult ValidateRolePermissions(
            OperationContext context,
            NodeMetadata nodeMetadata,
            PermissionType requestedPermission)
        {
            if (nodeMetadata == null || requestedPermission == PermissionType.None)
            {
                // no permission is required hence the validation passes
                return StatusCodes.Good;
            }

            // get the intersection of user role permissions and role permissions
            RolePermissionTypeCollection userRolePermissions = null;
            if (nodeMetadata.UserRolePermissions != null &&
                nodeMetadata.UserRolePermissions.Count > 0)
            {
                userRolePermissions = nodeMetadata.UserRolePermissions;
            }
            else if (nodeMetadata.DefaultUserRolePermissions != null &&
                nodeMetadata.DefaultUserRolePermissions.Count > 0)
            {
                userRolePermissions = nodeMetadata.DefaultUserRolePermissions;
            }

            RolePermissionTypeCollection rolePermissions;
            if (nodeMetadata.RolePermissions != null && nodeMetadata.RolePermissions.Count > 0)
            {
                rolePermissions = nodeMetadata.RolePermissions;
            }
            else
            {
                rolePermissions = nodeMetadata.DefaultRolePermissions;
            }

            if ((userRolePermissions == null || userRolePermissions.Count == 0) &&
                (rolePermissions == null || rolePermissions.Count == 0))
            {
                // there is no restriction from role permissions
                return StatusCodes.Good;
            }

            // group all permissions defined in rolePermissions by RoleId
            var roleIdPermissions = new Dictionary<NodeId, PermissionType>();
            if (rolePermissions != null && rolePermissions.Count > 0)
            {
                foreach (RolePermissionType rolePermission in rolePermissions)
                {
                    if (roleIdPermissions.ContainsKey(rolePermission.RoleId))
                    {
                        roleIdPermissions[rolePermission.RoleId] |= (PermissionType)rolePermission
                            .Permissions;
                    }
                    else
                    {
                        roleIdPermissions[rolePermission.RoleId] =
                            ((PermissionType)rolePermission.Permissions) & requestedPermission;
                    }
                }
            }

            // group all permissions defined in userRolePermissions by RoleId
            var roleIdPermissionsDefinedForUser = new Dictionary<NodeId, PermissionType>();
            if (userRolePermissions != null && userRolePermissions.Count > 0)
            {
                foreach (RolePermissionType rolePermission in userRolePermissions)
                {
                    if (roleIdPermissionsDefinedForUser.ContainsKey(rolePermission.RoleId))
                    {
                        roleIdPermissionsDefinedForUser[rolePermission.RoleId] |= (PermissionType)
                            rolePermission.Permissions;
                    }
                    else
                    {
                        roleIdPermissionsDefinedForUser[rolePermission.RoleId] =
                            ((PermissionType)rolePermission.Permissions) & requestedPermission;
                    }
                }
            }

            Dictionary<NodeId, PermissionType> commonRoleIdPermissions;
            if (rolePermissions == null || rolePermissions.Count == 0)
            {
                // there were no role permissions defined for this node only user role permissions
                commonRoleIdPermissions = roleIdPermissionsDefinedForUser;
            }
            else if (userRolePermissions == null || userRolePermissions.Count == 0)
            {
                // there were no role permissions defined for this node only user role permissions
                commonRoleIdPermissions = roleIdPermissions;
            }
            else
            {
                commonRoleIdPermissions = [];
                // intersect role permissions from node and user
                foreach (NodeId roleId in roleIdPermissions.Keys)
                {
                    if (roleIdPermissionsDefinedForUser.TryGetValue(
                        roleId,
                        out PermissionType value))
                    {
                        commonRoleIdPermissions[roleId] = roleIdPermissions[roleId] & value;
                    }
                }
            }

            NodeIdCollection currentRoleIds = context?.UserIdentity?.GrantedRoleIds;
            if (currentRoleIds == null || currentRoleIds.Count == 0)
            {
                return ServiceResult.Create(
                    StatusCodes.BadUserAccessDenied,
                    "Current user has no granted role.");
            }

            foreach (NodeId currentRoleId in currentRoleIds)
            {
                if (commonRoleIdPermissions.TryGetValue(currentRoleId, out PermissionType value) &&
                    value != PermissionType.None)
                {
                    // there is one role that current session has na is listed in requested role
                    return StatusCodes.Good;
                }
            }
            return ServiceResult.Create(
                StatusCodes.BadUserAccessDenied,
                "The requested permission {0} is not granted for node id {1}.",
                requestedPermission,
                nodeMetadata.NodeId);
        }

        private readonly Lock m_lock = new();
        private readonly List<INodeManager> m_nodeManagers;
        private long m_lastMonitoredItemId;
        private readonly uint m_maxContinuationPointsPerBrowse;
        private readonly ReaderWriterLockSlim m_readWriterLockSlim = new();
    }

    /// <summary>
    /// Stores a reference between NodeManagers that is needs to be created or deleted.
    /// </summary>
    public class LocalReference
    {
        /// <summary>
        /// Initializes the reference.
        /// </summary>
        public LocalReference(
            NodeId sourceId,
            NodeId referenceTypeId,
            bool isInverse,
            NodeId targetId)
        {
            SourceId = sourceId;
            ReferenceTypeId = referenceTypeId;
            IsInverse = isInverse;
            TargetId = targetId;
        }

        /// <summary>
        /// The source of the reference.
        /// </summary>
        public NodeId SourceId { get; }

        /// <summary>
        /// The type of reference.
        /// </summary>
        public NodeId ReferenceTypeId { get; }

        /// <summary>
        /// True if the reference is an inverse reference.
        /// </summary>
        public bool IsInverse { get; }

        /// <summary>
        /// The target of the reference.
        /// </summary>
        public NodeId TargetId { get; }
    }
}
