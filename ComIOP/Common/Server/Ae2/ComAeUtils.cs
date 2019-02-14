/* ========================================================================
 * Copyright (c) 2005-2019 The OPC Foundation, Inc. All rights reserved.
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
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Xml;
using System.Runtime.Serialization;
using System.Security.Cryptography.X509Certificates;
using System.Runtime.InteropServices;
using Opc.Ua.Client;
using OpcRcw.Hda;

namespace Opc.Ua.Com.Server
{
    /// <summary>
    /// Defines numerous functions used to browse the UA server event mode.
    /// </summary>
    public static class ComAeUtils
    {
        /// <summary>
        /// Browses the address space and returns the references found.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="nodesToBrowse">The set of browse operations to perform.</param>
        /// <param name="throwOnError">if set to <c>true</c> a exception will be thrown on an error.</param>
        /// <returns>
        /// The references found. Null if an error occurred.
        /// </returns>
        public static ReferenceDescriptionCollection Browse(Session session, BrowseDescriptionCollection nodesToBrowse, bool throwOnError)
        {
            try
            {
                ReferenceDescriptionCollection references = new ReferenceDescriptionCollection();
                BrowseDescriptionCollection unprocessedOperations = new BrowseDescriptionCollection();

                while (nodesToBrowse.Count > 0)
                {
                    // start the browse operation.
                    BrowseResultCollection results = null;
                    DiagnosticInfoCollection diagnosticInfos = null;

                    session.Browse(
                        null,
                        null,
                        0,
                        nodesToBrowse,
                        out results,
                        out diagnosticInfos);

                    ClientBase.ValidateResponse(results, nodesToBrowse);
                    ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToBrowse);

                    ByteStringCollection continuationPoints = new ByteStringCollection();

                    for (int ii = 0; ii < nodesToBrowse.Count; ii++)
                    {
                        // check for error.
                        if (StatusCode.IsBad(results[ii].StatusCode))
                        {
                            // this error indicates that the server does not have enough simultaneously active 
                            // continuation points. This request will need to be resent after the other operations
                            // have been completed and their continuation points released.
                            if (results[ii].StatusCode == StatusCodes.BadNoContinuationPoints)
                            {
                                unprocessedOperations.Add(nodesToBrowse[ii]);
                            }

                            continue;
                        }

                        // check if all references have been fetched.
                        if (results[ii].References.Count == 0)
                        {
                            continue;
                        }

                        // save results.
                        references.AddRange(results[ii].References);

                        // check for continuation point.
                        if (results[ii].ContinuationPoint != null)
                        {
                            continuationPoints.Add(results[ii].ContinuationPoint);
                        }
                    }

                    // process continuation points.
                    ByteStringCollection revisedContiuationPoints = new ByteStringCollection();

                    while (continuationPoints.Count > 0)
                    {
                        // continue browse operation.
                        session.BrowseNext(
                            null,
                            false,
                            continuationPoints,
                            out results,
                            out diagnosticInfos);

                        ClientBase.ValidateResponse(results, continuationPoints);
                        ClientBase.ValidateDiagnosticInfos(diagnosticInfos, continuationPoints);

                        for (int ii = 0; ii < continuationPoints.Count; ii++)
                        {
                            // check for error.
                            if (StatusCode.IsBad(results[ii].StatusCode))
                            {
                                continue;
                            }

                            // check if all references have been fetched.
                            if (results[ii].References.Count == 0)
                            {
                                continue;
                            }

                            // save results.
                            references.AddRange(results[ii].References);

                            // check for continuation point.
                            if (results[ii].ContinuationPoint != null)
                            {
                                revisedContiuationPoints.Add(results[ii].ContinuationPoint);
                            }
                        }

                        // check if browsing must continue;
                        revisedContiuationPoints = continuationPoints;
                    }

                    // check if unprocessed results exist.
                    nodesToBrowse = unprocessedOperations;
                }

                // return complete list.
                return references;
            }
            catch (Exception exception)
            {
                if (throwOnError)
                {
                    throw new ServiceResultException(exception, StatusCodes.BadUnexpectedError);
                }

                return null;
            }
        }

        /// <summary>
        /// Browses the address space and returns the references found.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="nodeToBrowse">The NodeId for the starting node.</param>
        /// <param name="throwOnError">if set to <c>true</c> a exception will be thrown on an error.</param>
        /// <returns>
        /// The references found. Null if an error occurred.
        /// </returns>
        public static ReferenceDescriptionCollection Browse(Session session, BrowseDescription nodeToBrowse, bool throwOnError)
        {
            try
            {
                ReferenceDescriptionCollection references = new ReferenceDescriptionCollection();

                // construct browse request.
                BrowseDescriptionCollection nodesToBrowse = new BrowseDescriptionCollection();
                nodesToBrowse.Add(nodeToBrowse);

                // start the browse operation.
                BrowseResultCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                session.Browse(
                    null,
                    null,
                    0,
                    nodesToBrowse,
                    out results,
                    out diagnosticInfos);

                ClientBase.ValidateResponse(results, nodesToBrowse);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToBrowse);

                do
                {
                    // check for error.
                    if (StatusCode.IsBad(results[0].StatusCode))
                    {
                        throw new ServiceResultException(results[0].StatusCode);
                    }

                    // process results.
                    for (int ii = 0; ii < results[0].References.Count; ii++)
                    {
                        references.Add(results[0].References[ii]);
                    }

                    // check if all references have been fetched.
                    if (results[0].References.Count == 0 || results[0].ContinuationPoint == null)
                    {
                        break;
                    }

                    // continue browse operation.
                    ByteStringCollection continuationPoints = new ByteStringCollection();
                    continuationPoints.Add(results[0].ContinuationPoint);

                    session.BrowseNext(
                        null,
                        false,
                        continuationPoints,
                        out results,
                        out diagnosticInfos);

                    ClientBase.ValidateResponse(results, continuationPoints);
                    ClientBase.ValidateDiagnosticInfos(diagnosticInfos, continuationPoints);
                }
                while (true);

                //return complete list.
                return references;
            }
            catch (Exception exception)
            {
                if (throwOnError)
                {
                    throw new ServiceResultException(exception, StatusCodes.BadUnexpectedError);
                }

                return null;
            }
        }

        /// <summary>
        /// Browses the address space and returns all of the supertypes of the specified type node.
        /// </summary>
        /// <param name="session">The session.</param>
        /// <param name="typeId">The NodeId for a type node in the address space.</param>
        /// <param name="throwOnError">if set to <c>true</c> a exception will be thrown on an error.</param>
        /// <returns>
        /// The references found. Null if an error occurred.
        /// </returns>
        public static ReferenceDescriptionCollection BrowseSuperTypes(Session session, NodeId typeId, bool throwOnError)
        {
            ReferenceDescriptionCollection supertypes = new ReferenceDescriptionCollection();

            try
            {
                // find all of the children of the field.
                BrowseDescription nodeToBrowse = new BrowseDescription();

                nodeToBrowse.NodeId = typeId;
                nodeToBrowse.BrowseDirection = BrowseDirection.Inverse;
                nodeToBrowse.ReferenceTypeId = ReferenceTypeIds.HasSubtype;
                nodeToBrowse.IncludeSubtypes = false; // more efficient to use IncludeSubtypes=False when possible.
                nodeToBrowse.NodeClassMask = 0; // the HasSubtype reference already restricts the targets to Types. 
                nodeToBrowse.ResultMask = (uint)BrowseResultMask.All;

                ReferenceDescriptionCollection references = Browse(session, nodeToBrowse, throwOnError);

                while (references != null && references.Count > 0)
                {
                    // should never be more than one supertype.
                    supertypes.Add(references[0]);

                    // only follow references within this server.
                    if (references[0].NodeId.IsAbsolute)
                    {
                        break;
                    }

                    // get the references for the next level up.
                    nodeToBrowse.NodeId = (NodeId)references[0].NodeId;
                    references = Browse(session, nodeToBrowse, throwOnError);
                }

                // return complete list.
                return supertypes;
            }
            catch (Exception exception)
            {
                if (throwOnError)
                {
                    throw new ServiceResultException(exception, StatusCodes.BadUnexpectedError);
                }

                return null;
            }
        }

        /// <summary>
        /// Returns the node ids for a set of relative paths.
        /// </summary>
        /// <param name="session">An open session with the server to use.</param>
        /// <param name="startNodeId">The starting node for the relative paths.</param>
        /// <param name="namespacesUris">The namespace URIs referenced by the relative paths.</param>
        /// <param name="relativePaths">The relative paths.</param>
        /// <returns>A collection of local nodes.</returns>
        public static List<NodeId> TranslateBrowsePaths(
            Session session,
            NodeId startNodeId,
            NamespaceTable namespacesUris,
            params string[] relativePaths)
        {
            // build the list of browse paths to follow by parsing the relative paths.
            BrowsePathCollection browsePaths = new BrowsePathCollection();

            if (relativePaths != null)
            {
                for (int ii = 0; ii < relativePaths.Length; ii++)
                {
                    BrowsePath browsePath = new BrowsePath();

                    // The relative paths used indexes in the namespacesUris table. These must be 
                    // converted to indexes used by the server. An error occurs if the relative path
                    // refers to a namespaceUri that the server does not recognize.

                    // The relative paths may refer to ReferenceType by their BrowseName. The TypeTree object
                    // allows the parser to look up the server's NodeId for the ReferenceType.

                    browsePath.RelativePath = RelativePath.Parse(
                        relativePaths[ii],
                        session.TypeTree,
                        namespacesUris,
                        session.NamespaceUris);

                    browsePath.StartingNode = startNodeId;

                    browsePaths.Add(browsePath);
                }
            }

            // make the call to the server.
            BrowsePathResultCollection results;
            DiagnosticInfoCollection diagnosticInfos;

            ResponseHeader responseHeader = session.TranslateBrowsePathsToNodeIds(
                null,
                browsePaths,
                out results,
                out diagnosticInfos);

            // ensure that the server returned valid results.
            Session.ValidateResponse(results, browsePaths);
            Session.ValidateDiagnosticInfos(diagnosticInfos, browsePaths);

            // collect the list of node ids found.
            List<NodeId> nodes = new List<NodeId>();

            for (int ii = 0; ii < results.Count; ii++)
            {
                // check if the start node actually exists.
                if (StatusCode.IsBad(results[ii].StatusCode))
                {
                    nodes.Add(null);
                    continue;
                }

                // an empty list is returned if no node was found.
                if (results[ii].Targets.Count == 0)
                {
                    nodes.Add(null);
                    continue;
                }

                // Multiple matches are possible, however, the node that matches the type model is the
                // one we are interested in here. The rest can be ignored.
                BrowsePathTarget target = results[ii].Targets[0];

                if (target.RemainingPathIndex != UInt32.MaxValue)
                {
                    nodes.Add(null);
                    continue;
                }

                // The targetId is an ExpandedNodeId because it could be node in another server. 
                // The ToNodeId function is used to convert a local NodeId stored in a ExpandedNodeId to a NodeId.
                nodes.Add(ExpandedNodeId.ToNodeId(target.TargetId, session.NamespaceUris));
            }

            // return whatever was found.
            return nodes;
        }

        /// <summary>
        /// Collects instance declarations nodes from with a type.
        /// </summary>
        public static void CollectInstanceDeclarations(
            Session session,
            ComNamespaceMapper mapper,
            NodeId typeId,
            AeEventAttribute parent,
            List<AeEventAttribute> instances,
            IDictionary<string, AeEventAttribute> map)
        {
            // find the children.
            BrowseDescription nodeToBrowse = new BrowseDescription();

            if (parent == null)
            {
                nodeToBrowse.NodeId = typeId;
            }
            else
            {
                nodeToBrowse.NodeId = parent.NodeId;
            }

            nodeToBrowse.BrowseDirection = BrowseDirection.Forward;
            nodeToBrowse.ReferenceTypeId = ReferenceTypeIds.HasChild;
            nodeToBrowse.IncludeSubtypes = true;
            nodeToBrowse.NodeClassMask = (uint)(NodeClass.Object | NodeClass.Variable);
            nodeToBrowse.ResultMask = (uint)BrowseResultMask.All;

            // ignore any browsing errors.
            ReferenceDescriptionCollection references = Browse(session, nodeToBrowse, false);

            if (references == null)
            {
                return;
            }

            // process the children.
            List<NodeId> nodeIds = new List<NodeId>();
            List<AeEventAttribute> children = new List<AeEventAttribute>();

            for (int ii = 0; ii < references.Count; ii++)
            {
                ReferenceDescription reference = references[ii];

                if (reference.NodeId.IsAbsolute)
                {
                    continue;
                }

                // create a new declaration.
                AeEventAttribute child = new AeEventAttribute();

                child.RootTypeId = typeId;
                child.NodeId = (NodeId)reference.NodeId;
                child.BrowseName = reference.BrowseName;
                child.NodeClass = reference.NodeClass;

                if (!LocalizedText.IsNullOrEmpty(reference.DisplayName))
                {
                    child.DisplayName = reference.DisplayName.Text;
                }
                else
                {
                    child.DisplayName = reference.BrowseName.Name;
                }

                if (parent != null)
                {
                    child.BrowsePath = new QualifiedNameCollection(parent.BrowsePath);
                    child.BrowsePathDisplayText = Utils.Format("{0}/{1}", parent.BrowsePathDisplayText, mapper.GetLocalBrowseName(reference.BrowseName));
                    child.DisplayPath = Utils.Format("{0}/{1}", parent.DisplayPath, reference.DisplayName);
                }
                else
                {
                    child.BrowsePath = new QualifiedNameCollection();
                    child.BrowsePathDisplayText = Utils.Format("{0}", reference.BrowseName);
                    child.DisplayPath = Utils.Format("{0}", reference.DisplayName);
                }

                child.BrowsePath.Add(reference.BrowseName);

                // check if reading an overridden declaration.
                AeEventAttribute overriden = null;

                if (map.TryGetValue(child.BrowsePathDisplayText, out overriden))
                {
                    child.OverriddenDeclaration = overriden;
                }

                map[child.BrowsePathDisplayText] = child;

                // add to list.
                children.Add(child);
                nodeIds.Add(child.NodeId);
            }

            // check if nothing more to do.
            if (children.Count == 0)
            {
                return;
            }

            // find the modelling rules.
            List<NodeId> modellingRules = FindTargetOfReference(session, nodeIds, Opc.Ua.ReferenceTypeIds.HasModellingRule, false);

            if (modellingRules != null)
            {
                for (int ii = 0; ii < nodeIds.Count; ii++)
                {
                    children[ii].ModellingRule = modellingRules[ii];

                    // if the modelling rule is null then the instance is not part of the type declaration.
                    if (NodeId.IsNull(modellingRules[ii]))
                    {
                        map.Remove(children[ii].BrowsePathDisplayText);
                    }
                }
            }

            // update the descriptions.
            UpdateInstanceDescriptions(session, children, false);

            // recusively collect instance declarations for the tree below.
            for (int ii = 0; ii < children.Count; ii++)
            {
                if (!NodeId.IsNull(children[ii].ModellingRule))
                {
                    instances.Add(children[ii]);
                    CollectInstanceDeclarations(session, mapper, typeId, children[ii], instances, map);
                }
            }
        }

        /// <summary>
        /// Collects instance declarations nodes from with a type.
        /// </summary>
        public static void CollectInstanceDeclarations(
            Session session,
            ComNamespaceMapper mapper,
            NodeState node,
            AeEventAttribute parent,
            List<AeEventAttribute> instances,
            IDictionary<string, AeEventAttribute> map)
        {
            List<BaseInstanceState> children = new List<BaseInstanceState>();
            node.GetChildren(session.SystemContext, children);
            
            if (children.Count == 0)
            {
                return;
            }

            // process the children.
            for (int ii = 0; ii < children.Count; ii++)
            {
                BaseInstanceState instance = children[ii];

                // only interested in objects and variables.
                if (instance.NodeClass != NodeClass.Object && instance.NodeClass != NodeClass.Variable)
                {
                    return;
                }

                // ignore instances without a modelling rule.
                if (NodeId.IsNull(instance.ModellingRuleId))
                {
                    return;
                }

                // create a new declaration.
                AeEventAttribute declaration = new AeEventAttribute();

                declaration.RootTypeId = (parent != null)?parent.RootTypeId:node.NodeId;
                declaration.NodeId = (NodeId)instance.NodeId;
                declaration.BrowseName = instance.BrowseName;
                declaration.NodeClass = instance.NodeClass;
                declaration.Description = (instance.Description != null)?instance.Description.ToString():null;
                
                // get data type information.
                BaseVariableState variable = instance as BaseVariableState;
 
                if (variable != null)
                {
                    declaration.DataType = variable.DataType;
                    declaration.ValueRank = variable.ValueRank;

                    if (!NodeId.IsNull(variable.DataType))
                    {
                        declaration.BuiltInType = DataTypes.GetBuiltInType(declaration.DataType, session.TypeTree);
                    }
                }

                if (!LocalizedText.IsNullOrEmpty(instance.DisplayName))
                {
                    declaration.DisplayName = instance.DisplayName.Text;
                }
                else
                {
                    declaration.DisplayName = instance.BrowseName.Name;
                }

                if (parent != null)
                {
                    declaration.BrowsePath = new QualifiedNameCollection(parent.BrowsePath);
                    declaration.BrowsePathDisplayText = Utils.Format("{0}/{1}", parent.BrowsePathDisplayText, mapper.GetLocalBrowseName(instance.BrowseName));
                    declaration.DisplayPath = Utils.Format("{0}/{1}", parent.DisplayPath, instance.DisplayName);
                }
                else
                {
                    declaration.BrowsePath = new QualifiedNameCollection();
                    declaration.BrowsePathDisplayText = Utils.Format("{0}", instance.BrowseName);
                    declaration.DisplayPath = Utils.Format("{0}", instance.DisplayName);
                }

                declaration.BrowsePath.Add(instance.BrowseName);

                // check if reading an overridden declaration.
                AeEventAttribute overriden = null;

                if (map.TryGetValue(declaration.BrowsePathDisplayText, out overriden))
                {
                    declaration.OverriddenDeclaration = overriden;
                }

                map[declaration.BrowsePathDisplayText] = declaration;

                // only interested in variables.
                if (instance.NodeClass == NodeClass.Variable)
                {
                    instances.Add(declaration);
                }

                // recusively build tree.
                CollectInstanceDeclarations(session, mapper, instance, declaration, instances, map);
            }
        }

        /// <summary>
        /// Finds the targets for the specified reference.
        /// </summary>
        private static List<NodeId> FindTargetOfReference(Session session, List<NodeId> nodeIds, NodeId referenceTypeId, bool throwOnError)
        {
            try
            {
                // construct browse request.
                BrowseDescriptionCollection nodesToBrowse = new BrowseDescriptionCollection();

                for (int ii = 0; ii < nodeIds.Count; ii++)
                {
                    BrowseDescription nodeToBrowse = new BrowseDescription();
                    nodeToBrowse.NodeId = nodeIds[ii];
                    nodeToBrowse.BrowseDirection = BrowseDirection.Forward;
                    nodeToBrowse.ReferenceTypeId = referenceTypeId;
                    nodeToBrowse.IncludeSubtypes = false;
                    nodeToBrowse.NodeClassMask = 0;
                    nodeToBrowse.ResultMask = (uint)BrowseResultMask.None;
                    nodesToBrowse.Add(nodeToBrowse);
                }

                // start the browse operation.
                BrowseResultCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                session.Browse(
                    null,
                    null,
                    1,
                    nodesToBrowse,
                    out results,
                    out diagnosticInfos);

                ClientBase.ValidateResponse(results, nodesToBrowse);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToBrowse);

                List<NodeId> targetIds = new List<NodeId>();
                ByteStringCollection continuationPoints = new ByteStringCollection();

                for (int ii = 0; ii < nodeIds.Count; ii++)
                {
                    targetIds.Add(null);

                    // check for error.
                    if (StatusCode.IsBad(results[ii].StatusCode))
                    {
                        continue;
                    }

                    // check for continuation point.
                    if (results[ii].ContinuationPoint != null && results[ii].ContinuationPoint.Length > 0)
                    {
                        continuationPoints.Add(results[ii].ContinuationPoint);
                    }

                    // get the node id.
                    if (results[ii].References.Count > 0)
                    {
                        if (NodeId.IsNull(results[ii].References[0].NodeId) || results[ii].References[0].NodeId.IsAbsolute)
                        {
                            continue;
                        }

                        targetIds[ii] = (NodeId)results[ii].References[0].NodeId;
                    }
                }

                // release continuation points.
                if (continuationPoints.Count > 0)
                {
                    session.BrowseNext(
                        null,
                        true,
                        continuationPoints,
                        out results,
                        out diagnosticInfos);

                    ClientBase.ValidateResponse(results, nodesToBrowse);
                    ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToBrowse);
                }

                //return complete list.
                return targetIds;
            }
            catch (Exception exception)
            {
                if (throwOnError)
                {
                    throw new ServiceResultException(exception, StatusCodes.BadUnexpectedError);
                }

                return null;
            }
        }

        /// <summary>
        /// Finds the targets for the specified reference.
        /// </summary>
        private static void UpdateInstanceDescriptions(Session session, List<AeEventAttribute> instances, bool throwOnError)
        {
            try
            {
                ReadValueIdCollection nodesToRead = new ReadValueIdCollection();

                for (int ii = 0; ii < instances.Count; ii++)
                {
                    ReadValueId nodeToRead = new ReadValueId();
                    nodeToRead.NodeId = instances[ii].NodeId;
                    nodeToRead.AttributeId = Attributes.Description;
                    nodesToRead.Add(nodeToRead);

                    nodeToRead = new ReadValueId();
                    nodeToRead.NodeId = instances[ii].NodeId;
                    nodeToRead.AttributeId = Attributes.DataType;
                    nodesToRead.Add(nodeToRead);

                    nodeToRead = new ReadValueId();
                    nodeToRead.NodeId = instances[ii].NodeId;
                    nodeToRead.AttributeId = Attributes.ValueRank;
                    nodesToRead.Add(nodeToRead);
                }

                // start the browse operation.
                DataValueCollection results = null;
                DiagnosticInfoCollection diagnosticInfos = null;

                session.Read(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    nodesToRead,
                    out results,
                    out diagnosticInfos);

                ClientBase.ValidateResponse(results, nodesToRead);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, nodesToRead);

                // update the instances.
                for (int ii = 0; ii < nodesToRead.Count; ii += 3)
                {
                    AeEventAttribute instance = instances[ii / 3];

                    instance.Description = results[ii].GetValue<LocalizedText>(LocalizedText.Null).Text;
                    instance.DataType = results[ii+1].GetValue<NodeId>(NodeId.Null);
                    instance.ValueRank = results[ii+2].GetValue<int>(ValueRanks.Any);
                    
                    if (!NodeId.IsNull(instance.DataType))
                    {
                        instance.BuiltInType = DataTypes.GetBuiltInType(instance.DataType, session.TypeTree);
                    }
                }
            }
            catch (Exception exception)
            {
                if (throwOnError)
                {
                    throw new ServiceResultException(exception, StatusCodes.BadUnexpectedError);
                }
            }
        }
    }
}
