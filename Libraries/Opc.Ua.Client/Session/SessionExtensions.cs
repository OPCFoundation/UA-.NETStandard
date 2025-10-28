/* ========================================================================
 * Copyright (c) 2005-2022 The OPC Foundation, Inc. All rights reserved.
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

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Extensions to ISession that are not dependent on anything internal
    /// to the Session but layer over ISession or ISessionClient.
    /// </summary>
    public static class SessionExtensions
    {
        /// <summary>
        /// Reads the values for a set of variables.
        /// </summary>
        public static async ValueTask<(
            IList<object>,
            IList<ServiceResult>
            )> ReadValuesAsync(
                this ISession session,
                IList<NodeId> variableIds,
                IList<Type> expectedTypes,
                CancellationToken ct = default)
        {
            (DataValueCollection dataValues, IList<ServiceResult> errors) =
                await session.ReadValuesAsync(
                    variableIds,
                    ct).ConfigureAwait(false);

            object[] values = new object[dataValues.Count];
            for (int ii = 0; ii < variableIds.Count; ii++)
            {
                object value = dataValues[ii].Value;

                // extract the body from extension objects.
                if (value is ExtensionObject extension &&
                    extension.Body is IEncodeable)
                {
                    value = extension.Body;
                }

                // check expected type.
                if (expectedTypes[ii] != null &&
                    !expectedTypes[ii].IsInstanceOfType(value))
                {
                    errors[ii] = ServiceResult.Create(
                        StatusCodes.BadTypeMismatch,
                        "Value {0} does not have expected type: {1}.",
                        value,
                        expectedTypes[ii].Name);
                    continue;
                }

                // suitable value found.
                values[ii] = value;
            }
            return (values, errors);
        }

        /// <summary>
        /// Invokes the Browse service.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static async ValueTask<(
            ResponseHeader,
            byte[],
            ReferenceDescriptionCollection
            )> BrowseAsync(
                this ISession session,
                RequestHeader? requestHeader,
                ViewDescription? view,
                NodeId nodeToBrowse,
                uint maxResultsToReturn,
                BrowseDirection browseDirection,
                NodeId referenceTypeId,
                bool includeSubtypes,
                uint nodeClassMask,
                CancellationToken ct = default)
        {
            ResponseHeader responseHeader;
            IList<ServiceResult> errors;
            IList<ReferenceDescriptionCollection> referencesList;
            ByteStringCollection continuationPoints;
            (responseHeader, continuationPoints, referencesList, errors) =
                await session.BrowseAsync(
                    requestHeader,
                    view,
                    [nodeToBrowse],
                    maxResultsToReturn,
                    browseDirection,
                    referenceTypeId,
                    includeSubtypes,
                    nodeClassMask,
                    ct).ConfigureAwait(false);

            Debug.Assert(errors.Count <= 1);
            if (errors.Count > 0 && StatusCode.IsBad(errors[0].StatusCode))
            {
                throw new ServiceResultException(errors[0]);
            }

            Debug.Assert(referencesList.Count == 1);
            Debug.Assert(continuationPoints.Count == 1);
            return (responseHeader, continuationPoints[0], referencesList[0]);
        }

        /// <summary>
        /// Invokes the BrowseNext service.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static async ValueTask<(
            ResponseHeader,
            byte[],
            ReferenceDescriptionCollection
            )> BrowseNextAsync(
                this ISession session,
                RequestHeader? requestHeader,
                bool releaseContinuationPoint,
                byte[]? continuationPoint,
                CancellationToken ct = default)
        {
            ResponseHeader responseHeader;
            IList<ServiceResult> errors;
            IList<ReferenceDescriptionCollection> referencesList;

            ByteStringCollection revisedContinuationPoints;
            (responseHeader, revisedContinuationPoints, referencesList, errors) =
                await session.BrowseNextAsync(requestHeader, [continuationPoint], releaseContinuationPoint, ct)
                    .ConfigureAwait(false);
            Debug.Assert(errors.Count <= 1);
            if (errors.Count > 0 && StatusCode.IsBad(errors[0].StatusCode))
            {
                throw new ServiceResultException(errors[0]);
            }

            Debug.Assert(referencesList.Count == 1);
            Debug.Assert(revisedContinuationPoints.Count == 1);
            return (responseHeader, revisedContinuationPoints[0], referencesList[0]);
        }

        /// <summary>
        /// Managed browsing using browser
        /// </summary>
        public static async Task<(
            IList<ReferenceDescriptionCollection>,
            IList<ServiceResult>
            )> ManagedBrowseAsync(
                this ISession session,
                RequestHeader? requestHeader,
                ViewDescription? view,
                IList<NodeId> nodesToBrowse,
                uint maxResultsToReturn,
                BrowseDirection browseDirection,
                NodeId? referenceTypeId,
                bool includeSubtypes,
                uint nodeClassMask,
                CancellationToken ct = default)
        {
            var browser = new Browser(session, new BrowserOptions
            {
                RequestHeader = requestHeader,
                View = view,
                MaxReferencesReturned = maxResultsToReturn,
                ContinuationPointPolicy = session.ContinuationPointPolicy,
                BrowseDirection = browseDirection,
                ReferenceTypeId = referenceTypeId ?? NodeId.Null,
                IncludeSubtypes = includeSubtypes,
                NodeClassMask = (int)nodeClassMask
            });
            ResultSet<ReferenceDescriptionCollection> result =
                await browser.BrowseAsync([.. nodesToBrowse], ct).ConfigureAwait(false);
            return (result.Results.ToList(), result.Errors.ToList());
        }

        /// <summary>
        /// Fetches all references for the specified node.
        /// </summary>
        /// <param name="session">session to use</param>
        /// <param name="nodeId">The node id.</param>
        /// <param name="ct">Cancelation token to cancel operation with</param>
        public static async Task<ReferenceDescriptionCollection> FetchReferencesAsync(
            this ISession session,
            NodeId nodeId,
            CancellationToken ct = default)
        {
            (IList<ReferenceDescriptionCollection> descriptions, _) =
                await session.ManagedBrowseAsync(
                    null,
                    null,
                    [nodeId],
                    0,
                    BrowseDirection.Both,
                    null,
                    true,
                    0,
                    ct)
                .ConfigureAwait(false);
            return descriptions[0];
        }

        /// <summary>
        /// Fetches all references for the specified nodes.
        /// </summary>
        /// <param name="session">session to use</param>
        /// <param name="nodeIds">The node id collection.</param>
        /// <param name="ct">Cancelation token to cancel operation with</param>
        /// <returns>A list of reference collections and the errors reported
        /// by the server.</returns>
        public static Task<(
            IList<ReferenceDescriptionCollection>,
            IList<ServiceResult>
            )> FetchReferencesAsync(
                this ISession session,
                IList<NodeId> nodeIds,
                CancellationToken ct = default)
        {
            return session.ManagedBrowseAsync(
                null,
                null,
                nodeIds,
                0,
                BrowseDirection.Both,
                null,
                true,
                0,
                ct);
        }

        /// <summary>
        /// Reads the values for the node attributes and returns a node object collection.
        /// </summary>
        /// <remarks>
        /// If the nodeclass for the nodes in nodeIdCollection is already known
        /// and passed as nodeClass, reads only values of required attributes.
        /// Otherwise NodeClass.Unspecified should be used.
        /// </remarks>
        /// <param name="session">The session to use</param>
        /// <param name="nodeIds">The nodeId collection to read.</param>
        /// <param name="nodeClass">The nodeClass of all nodes in the collection.
        /// Set to <c>NodeClass.Unspecified</c> if the nodeclass is unknown.</param>
        /// <param name="optionalAttributes">Set to <c>true</c> if optional attributes
        /// should not be omitted.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The node collection and associated errors.</returns>
        public static async Task<(IList<Node>, IList<ServiceResult>)> ReadNodesAsync(
            this ISession session,
            IList<NodeId> nodeIds,
            NodeClass nodeClass,
            bool optionalAttributes = false,
            CancellationToken ct = default)
        {
            var nodeCacheContext = new NodeCacheContext(session);
            ResultSet<Node> result = await nodeCacheContext.FetchNodesAsync(
                null,
                [.. nodeIds],
                nodeClass,
                optionalAttributes,
                ct).ConfigureAwait(false);
            return (result.Results.ToList(), result.Errors.ToList());
        }

        /// <summary>
        /// Reads the values for the node attributes and returns a node object collection.
        /// Reads the nodeclass of the nodeIds, then reads
        /// the values for the node attributes and returns a node collection.
        /// </summary>
        /// <param name="session">The session to use</param>
        /// <param name="nodeIds">The nodeId collection.</param>
        /// <param name="optionalAttributes">If optional attributes to read.</param>
        /// <param name="ct">The cancellation token.</param>
        public static async Task<(IList<Node>, IList<ServiceResult>)> ReadNodesAsync(
            this ISession session,
            IList<NodeId> nodeIds,
            bool optionalAttributes = false,
            CancellationToken ct = default)
        {
            var nodeCacheContext = new NodeCacheContext(session);
            ResultSet<Node> result = await nodeCacheContext.FetchNodesAsync(
                null,
                [.. nodeIds],
                optionalAttributes,
                ct).ConfigureAwait(false);
            return (result.Results.ToList(), result.Errors.ToList());
        }

        /// <summary>
        /// Reads the values for the node attributes and returns a node object.
        /// </summary>
        /// <param name="session">The session to use</param>
        /// <param name="nodeId">The nodeId.</param>
        /// <param name="ct">The cancellation token for the request.</param>
        public static Task<Node> ReadNodeAsync(
            this ISession session,
            NodeId nodeId,
            CancellationToken ct = default)
        {
            return session.ReadNodeAsync(nodeId, NodeClass.Unspecified, true, ct);
        }

        /// <summary>
        /// Reads the values for the node attributes and returns a node object.
        /// </summary>
        /// <remarks>
        /// If the nodeclass is known, only the supported attribute values are read.
        /// </remarks>
        /// <param name="session">The session to use</param>
        /// <param name="nodeId">The nodeId.</param>
        /// <param name="nodeClass">The nodeclass of the node to read.</param>
        /// <param name="optionalAttributes">Read optional attributes.</param>
        /// <param name="ct">The cancellation token for the request.</param>
        public static Task<Node> ReadNodeAsync(
            this ISession session,
            NodeId nodeId,
            NodeClass nodeClass,
            bool optionalAttributes = true,
            CancellationToken ct = default)
        {
            var nodeCacheContext = new NodeCacheContext(session);
            return nodeCacheContext.FetchNodeAsync(
                null,
                nodeId,
                nodeClass,
                optionalAttributes,
                ct).AsTask();
        }

        /// <summary>
        /// Read display name for a set of nodes
        /// </summary>
        /// <param name="session">The session to use</param>
        /// <param name="nodeIds">node for which to read display name</param>
        /// <param name="ct">Cancellation token to use to cancel the operation</param>
        /// <returns>Paired list of displaynames and potential errors per node</returns>
        public static async Task<(IList<string>, IList<ServiceResult>)> ReadDisplayNameAsync(
            this ISession session,
            IList<NodeId> nodeIds,
            CancellationToken ct = default)
        {
            var displayNames = new List<string>();
            var errors = new List<ServiceResult>();

            // build list of values to read.
            var valuesToRead = new ReadValueIdCollection();

            for (int ii = 0; ii < nodeIds.Count; ii++)
            {
                var valueToRead = new ReadValueId
                {
                    NodeId = nodeIds[ii],
                    AttributeId = Attributes.DisplayName,
                    IndexRange = null,
                    DataEncoding = null
                };

                valuesToRead.Add(valueToRead);
            }

            // read the values.

            ReadResponse response = await session.ReadAsync(
                null,
                int.MaxValue,
                TimestampsToReturn.Neither,
                valuesToRead,
                ct).ConfigureAwait(false);

            DataValueCollection results = response.Results;
            DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;
            ResponseHeader responseHeader = response.ResponseHeader;

            // verify that the server returned the correct number of results.
            ClientBase.ValidateResponse(results, valuesToRead);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, valuesToRead);

            for (int ii = 0; ii < nodeIds.Count; ii++)
            {
                displayNames.Add(string.Empty);
                errors.Add(ServiceResult.Good);

                // process any diagnostics associated with bad or uncertain data.
                if (StatusCode.IsNotGood(results[ii].StatusCode))
                {
                    errors[ii] = new ServiceResult(
                        results[ii].StatusCode,
                        ii,
                        diagnosticInfos,
                        responseHeader.StringTable);
                    continue;
                }

                // extract the name.
                LocalizedText displayName = results[ii].GetValue(LocalizedText.Null);

                if (!LocalizedText.IsNullOrEmpty(displayName))
                {
                    displayNames[ii] = displayName.Text;
                }
            }

            return (displayNames, errors);
        }

        /// <summary>
        /// Returns the available encodings for a node
        /// </summary>
        /// <param name="session">The session to use</param>
        /// <param name="variableId">The variable node.</param>
        /// <param name="ct">Cancellation token to use to cancel the operation</param>
        /// <exception cref="ServiceResultException"></exception>
        public static async Task<ReferenceDescriptionCollection> ReadAvailableEncodingsAsync(
            this ISession session,
            NodeId variableId,
            CancellationToken ct = default)
        {
            if (await session.NodeCache.FindAsync(variableId, ct).ConfigureAwait(false)
                is not VariableNode variable)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdInvalid,
                    "NodeId does not refer to a valid variable node.");
            }

            // no encodings available if there was a problem reading the
            // data type for the node.
            if (NodeId.IsNull(variable.DataType))
            {
                return [];
            }

            // no encodings for non-structures.
            if (!await session.NodeCache.IsTypeOfAsync(
                variable.DataType,
                DataTypes.Structure,
                ct).ConfigureAwait(false))
            {
                return [];
            }

            // look for cached values.
            IList<INode> encodings = await session.NodeCache.FindAsync(
                variableId,
                ReferenceTypeIds.HasEncoding,
                false,
                true,
                ct).ConfigureAwait(false);

            if (encodings.Count > 0)
            {
                var references = new ReferenceDescriptionCollection();

                foreach (INode encoding in encodings)
                {
                    var reference = new ReferenceDescription
                    {
                        ReferenceTypeId = ReferenceTypeIds.HasEncoding,
                        IsForward = true,
                        NodeId = encoding.NodeId,
                        NodeClass = encoding.NodeClass,
                        BrowseName = encoding.BrowseName,
                        DisplayName = encoding.DisplayName,
                        TypeDefinition = encoding.TypeDefinitionId
                    };

                    references.Add(reference);
                }

                return references;
            }

            var browser = new Browser(session)
            {
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HasEncoding,
                IncludeSubtypes = false,
                NodeClassMask = 0
            };

            return await browser.BrowseAsync(variable.DataType, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Returns the data description for the encoding.
        /// </summary>
        /// <param name="session">The session to use</param>
        /// <param name="encodingId">The encoding Id.</param>
        /// <param name="ct">Cancellation token to use to cancel the operation</param>
        /// <exception cref="ServiceResultException"></exception>
        public static async Task<ReferenceDescription> FindDataDescriptionAsync(
            this ISession session,
            NodeId encodingId,
            CancellationToken ct = default)
        {
            var browser = new Browser(session)
            {
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HasDescription,
                IncludeSubtypes = false,
                NodeClassMask = 0
            };

            ReferenceDescriptionCollection references =
                await browser.BrowseAsync(encodingId, ct).ConfigureAwait(false);

            if (references.Count == 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNodeIdInvalid,
                    "Encoding does not refer to a valid data description.");
            }

            return references[0];
        }

        /// <summary>
        /// Finds the NodeIds for the components for an instance.
        /// </summary>
        public static async Task<(NodeIdCollection, IList<ServiceResult>)> FindComponentIdsAsync(
            this ISession session,
            NodeId instanceId,
            IList<string> componentPaths,
            CancellationToken ct = default)
        {
            var componentIds = new NodeIdCollection();
            var errors = new List<ServiceResult>();

            // build list of paths to translate.
            var pathsToTranslate = new BrowsePathCollection();

            for (int ii = 0; ii < componentPaths.Count; ii++)
            {
                var pathToTranslate = new BrowsePath
                {
                    StartingNode = instanceId,
                    RelativePath = RelativePath.Parse(componentPaths[ii], session.TypeTree)
                };

                pathsToTranslate.Add(pathToTranslate);
            }

            // translate the paths.

            TranslateBrowsePathsToNodeIdsResponse response = await session.TranslateBrowsePathsToNodeIdsAsync(
                null,
                pathsToTranslate,
                ct).ConfigureAwait(false);

            BrowsePathResultCollection results = response.Results;
            DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;
            ResponseHeader responseHeader = response.ResponseHeader;

            // verify that the server returned the correct number of results.
            ClientBase.ValidateResponse(results, pathsToTranslate);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, pathsToTranslate);

            for (int ii = 0; ii < componentPaths.Count; ii++)
            {
                componentIds.Add(NodeId.Null);
                errors.Add(ServiceResult.Good);

                // process any diagnostics associated with any error.
                if (StatusCode.IsBad(results[ii].StatusCode))
                {
                    errors[ii] = new ServiceResult(
                        results[ii].StatusCode,
                        ii,
                        diagnosticInfos,
                        responseHeader.StringTable);
                    continue;
                }

                // Expecting exact one NodeId for a local node.
                // Report an error if the server returns anything other than that.

                if (results[ii].Targets.Count == 0)
                {
                    errors[ii] = ServiceResult.Create(
                        StatusCodes.BadTargetNodeIdInvalid,
                        "Could not find target for path: {0}.",
                        componentPaths[ii]);

                    continue;
                }

                if (results[ii].Targets.Count != 1)
                {
                    errors[ii] = ServiceResult.Create(
                        StatusCodes.BadTooManyMatches,
                        "Too many matches found for path: {0}.",
                        componentPaths[ii]);

                    continue;
                }

                if (results[ii].Targets[0].RemainingPathIndex != uint.MaxValue)
                {
                    errors[ii] = ServiceResult.Create(
                        StatusCodes.BadTargetNodeIdInvalid,
                        "Cannot follow path to external server: {0}.",
                        componentPaths[ii]);

                    continue;
                }

                if (NodeId.IsNull(results[ii].Targets[0].TargetId))
                {
                    errors[ii] = ServiceResult.Create(
                        StatusCodes.BadUnexpectedError,
                        "Server returned a null NodeId for path: {0}.",
                        componentPaths[ii]);

                    continue;
                }

                if (results[ii].Targets[0].TargetId.IsAbsolute)
                {
                    errors[ii] = ServiceResult.Create(
                        StatusCodes.BadUnexpectedError,
                        "Server returned a remote node for path: {0}.",
                        componentPaths[ii]);

                    continue;
                }

                // suitable target found.
                componentIds[ii] = ExpandedNodeId.ToNodeId(
                    results[ii].Targets[0].TargetId,
                    session.NamespaceUris);
            }
            return (componentIds, errors);
        }

        /// <summary>
        /// Reads the value for a node of type T or throws if not matching the type.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="session">The session to use</param>
        /// <param name="nodeId">The node Id.</param>
        /// <param name="ct">The cancellation token for the request.</param>
        /// <exception cref="ServiceResultException"></exception>
        public static async Task<T> ReadValueAsync<T>(
            this ISession session,
            NodeId nodeId,
            CancellationToken ct = default)
        {
            DataValue dataValue = await session.ReadValueAsync(nodeId, ct).ConfigureAwait(false);
            object value = dataValue.Value;

            if (value is ExtensionObject extension)
            {
                value = extension.Body;
            }

            if (!typeof(T).IsInstanceOfType(value))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadTypeMismatch,
                    "Server returned value unexpected type: {0}",
                    value != null ? value.GetType().Name : "(null)");
            }
            return (T)value;
        }

        /// <summary>
        /// Reads the value for a node.
        /// </summary>
        /// <param name="session">The session to use</param>
        /// <param name="nodeId">The node Id.</param>
        /// <param name="ct">The cancellation token for the request.</param>
        /// <exception cref="ServiceResultException"></exception>
        public static Task<DataValue> ReadValueAsync(
            this ISession session,
            NodeId nodeId,
            CancellationToken ct = default)
        {
            var nodeCacheContext = new NodeCacheContext(session);
            return nodeCacheContext.FetchValueAsync(
                null,
                nodeId,
                ct).AsTask();
        }

        /// <summary>
        /// Reads the values for a node collection. Returns diagnostic errors.
        /// </summary>
        /// <param name="session">The session to use</param>
        /// <param name="nodeIds">The node Id.</param>
        /// <param name="ct">The cancellation token for the request.</param>
        public static async Task<(DataValueCollection, IList<ServiceResult>)> ReadValuesAsync(
            this ISession session,
            IList<NodeId> nodeIds,
            CancellationToken ct = default)
        {
            var nodeCacheContext = new NodeCacheContext(session);
            ResultSet<DataValue> result = await nodeCacheContext.FetchValuesAsync(
                null,
                [.. nodeIds],
                ct).ConfigureAwait(false);
            return (new DataValueCollection(result.Results), result.Errors.ToList());
        }

        /// <summary>
        /// Browses the nodes in the server.
        /// </summary>
        /// <param name="session">The session to use</param>
        /// <param name="requestHeader">Request header</param>
        /// <param name="view">View to use</param>
        /// <param name="nodesToBrowse">nodes to browse</param>
        /// <param name="maxResultsToReturn">max results to return</param>
        /// <param name="browseDirection">Direction of browse</param>
        /// <param name="referenceTypeId">Reference type to follow</param>
        /// <param name="includeSubtypes">Include subtypes</param>
        /// <param name="nodeClassMask">Node classes to match</param>
        /// <param name="ct">Cancellation token to cancel the operation</param>
        /// <returns></returns>
        public static async Task<(
            ResponseHeader responseHeader,
            ByteStringCollection continuationPoints,
            IList<ReferenceDescriptionCollection> referencesList,
            IList<ServiceResult> errors
        )> BrowseAsync(
            this ISession session,
            RequestHeader? requestHeader,
            ViewDescription? view,
            IList<NodeId> nodesToBrowse,
            uint maxResultsToReturn,
            BrowseDirection browseDirection,
            NodeId referenceTypeId,
            bool includeSubtypes,
            uint nodeClassMask,
            CancellationToken ct = default)
        {
            var browseDescriptions = new BrowseDescriptionCollection();
            foreach (NodeId nodeToBrowse in nodesToBrowse)
            {
                var description = new BrowseDescription
                {
                    NodeId = nodeToBrowse,
                    BrowseDirection = browseDirection,
                    ReferenceTypeId = referenceTypeId,
                    IncludeSubtypes = includeSubtypes,
                    NodeClassMask = nodeClassMask,
                    ResultMask = (uint)BrowseResultMask.All
                };

                browseDescriptions.Add(description);
            }

            BrowseResponse browseResponse = await session.BrowseAsync(
                    requestHeader,
                    view,
                    maxResultsToReturn,
                    browseDescriptions,
                    ct)
                .ConfigureAwait(false);

            BrowseResultCollection results = browseResponse.Results;
            DiagnosticInfoCollection diagnosticInfos = browseResponse.DiagnosticInfos;

            ClientBase.ValidateResponse(results, browseDescriptions);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, browseDescriptions);

            int ii = 0;
            var errors = new List<ServiceResult>();
            var continuationPoints = new ByteStringCollection();
            var referencesList = new List<ReferenceDescriptionCollection>();
            foreach (BrowseResult result in results)
            {
                if (StatusCode.IsBad(result.StatusCode))
                {
                    errors.Add(
                        new ServiceResult(
                            result.StatusCode,
                            ii,
                            diagnosticInfos,
                            browseResponse.ResponseHeader.StringTable));
                }
                else
                {
                    errors.Add(ServiceResult.Good);
                }
                continuationPoints.Add(result.ContinuationPoint);
                referencesList.Add(result.References);
                ii++;
            }

            return (browseResponse.ResponseHeader, continuationPoints, referencesList, errors);
        }

        /// <summary>
        /// Browse next
        /// </summary>
        /// <param name="session">The session to use</param>
        /// <param name="requestHeader"></param>
        /// <param name="continuationPoints"></param>
        /// <param name="releaseContinuationPoint"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        public static async Task<(
            ResponseHeader responseHeader,
            ByteStringCollection revisedContinuationPoints,
            IList<ReferenceDescriptionCollection> referencesList,
            IList<ServiceResult> errors
        )> BrowseNextAsync(
            this ISession session,
            RequestHeader? requestHeader,
            ByteStringCollection? continuationPoints,
            bool releaseContinuationPoint,
            CancellationToken ct = default)
        {
            BrowseNextResponse response = await session.BrowseNextAsync(
                    requestHeader,
                    releaseContinuationPoint,
                    continuationPoints,
                    ct)
                .ConfigureAwait(false);

            BrowseResultCollection results = response.Results;
            DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;

            ClientBase.ValidateResponse(results, continuationPoints);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, continuationPoints);

            int ii = 0;
            var errors = new List<ServiceResult>();
            var revisedContinuationPoints = new ByteStringCollection();
            var referencesList = new List<ReferenceDescriptionCollection>();
            foreach (BrowseResult result in results)
            {
                if (StatusCode.IsBad(result.StatusCode))
                {
                    errors.Add(
                        new ServiceResult(
                            result.StatusCode,
                            ii,
                            diagnosticInfos,
                            response.ResponseHeader.StringTable));
                }
                else
                {
                    errors.Add(ServiceResult.Good);
                }
                revisedContinuationPoints.Add(result.ContinuationPoint);
                referencesList.Add(result.References);
                ii++;
            }

            return (response.ResponseHeader, revisedContinuationPoints, referencesList, errors);
        }

        /// <summary>
        /// Calls the specified method and returns the output arguments.
        /// </summary>
        /// <param name="session">The session to use</param>
        /// <param name="objectId">The NodeId of the object that provides the method.</param>
        /// <param name="methodId">The NodeId of the method to call.</param>
        /// <param name="ct">The cancellation token for the request.</param>
        /// <param name="args">The input arguments.</param>
        /// <returns>The list of output argument values.</returns>
        /// <exception cref="ServiceResultException"></exception>
        public static async Task<IList<object>> CallAsync(
            this ISession session,
            NodeId objectId,
            NodeId methodId,
            CancellationToken ct = default,
            params object[] args)
        {
            var inputArguments = new VariantCollection();

            if (args != null)
            {
                for (int ii = 0; ii < args.Length; ii++)
                {
                    inputArguments.Add(new Variant(args[ii]));
                }
            }

            var request = new CallMethodRequest
            {
                ObjectId = objectId,
                MethodId = methodId,
                InputArguments = inputArguments
            };

            var requests = new CallMethodRequestCollection { request };

            CallMethodResultCollection results;
            DiagnosticInfoCollection diagnosticInfos;

            CallResponse response = await session.CallAsync(null, requests, ct).ConfigureAwait(false);

            results = response.Results;
            diagnosticInfos = response.DiagnosticInfos;

            ClientBase.ValidateResponse(results, requests);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, requests);

            if (StatusCode.IsBad(results[0].StatusCode))
            {
                throw ServiceResultException.Create(
                    results[0].StatusCode,
                    0,
                    diagnosticInfos,
                    response.ResponseHeader.StringTable);
            }

            var outputArguments = new List<object>();

            foreach (Variant arg in results[0].OutputArguments)
            {
                outputArguments.Add(arg.Value);
            }

            return outputArguments;
        }

        /// <summary>
        /// Reads a byte string which is too large for the (server side) encoder to handle.
        /// </summary>
        /// <param name="session">session to use</param>
        /// <param name="nodeId">The node id of a byte string variable</param>
        /// <param name="ct">Cancelation token to cancel operation with</param>
        /// <exception cref="ServiceResultException"></exception>
        public static async Task<byte[]> ReadByteStringInChunksAsync(
            this ISession session,
            NodeId nodeId,
            CancellationToken ct = default)
        {
            int count = (int)session.ServerMaxByteStringLength;
            if (count <= 1)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadIndexRangeNoData,
                    "The MaxByteStringLength is not known or too small for reading data in chunks.");
            }

            int offset = 0;
            using var bytes = new MemoryStream();
            while (true)
            {
                var valueToRead = new ReadValueId
                {
                    NodeId = nodeId,
                    AttributeId = Attributes.Value,
                    IndexRange = new NumericRange(offset, offset + count - 1).ToString(),
                    DataEncoding = null
                };
                var readValueIds = new ReadValueIdCollection { valueToRead };

                ReadResponse result = await session.ReadAsync(
                    null,
                    0,
                    TimestampsToReturn.Neither,
                    readValueIds,
                    ct)
                    .ConfigureAwait(false);

                ResponseHeader responseHeader = result.ResponseHeader;
                DataValueCollection results = result.Results;
                DiagnosticInfoCollection diagnosticInfos = result.DiagnosticInfos;
                ClientBase.ValidateResponse(results, readValueIds);
                ClientBase.ValidateDiagnosticInfos(diagnosticInfos, readValueIds);

                if (offset == 0)
                {
                    Variant wrappedValue = results[0].WrappedValue;
                    if (wrappedValue.TypeInfo.BuiltInType != BuiltInType.ByteString ||
                        wrappedValue.TypeInfo.ValueRank != ValueRanks.Scalar)
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadTypeMismatch,
                            "Value is not a ByteString scalar.");
                    }
                }

                if (StatusCode.IsBad(results[0].StatusCode))
                {
                    if (results[0].StatusCode == StatusCodes.BadIndexRangeNoData)
                    {
                        // this happens when the previous read has fetched all remaining data
                        break;
                    }
                    ServiceResult serviceResult = ClientBase.GetResult(
                        results[0].StatusCode,
                        0,
                        diagnosticInfos,
                        responseHeader);
                    throw new ServiceResultException(serviceResult);
                }

                if (results[0].Value is not byte[] chunk || chunk.Length == 0)
                {
                    break;
                }

                bytes.Write(chunk, 0, chunk.Length);
                if (chunk.Length < count)
                {
                    break;
                }
                offset += count;
            }
            return bytes.ToArray();
        }

        /// <summary>
        /// Call the ResendData method on the server for all subscriptions.
        /// </summary>
        public static async ValueTask<IReadOnlyList<ServiceResult>> ResendDataAsync(
            this ISession session,
            IEnumerable<uint> subscriptionIds,
            CancellationToken ct = default)
        {
            CallMethodRequestCollection requests = CreateCallRequestsForResendData(subscriptionIds);

            var errors = new List<ServiceResult>(requests.Count);
            CallResponse response = await session.CallAsync(null, requests, ct).ConfigureAwait(false);
            CallMethodResultCollection results = response.Results;
            DiagnosticInfoCollection diagnosticInfos = response.DiagnosticInfos;
            ResponseHeader responseHeader = response.ResponseHeader;
            ClientBase.ValidateResponse(results, requests);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, requests);

            int ii = 0;
            foreach (CallMethodResult value in results)
            {
                ServiceResult result = ServiceResult.Good;
                if (StatusCode.IsNotGood(value.StatusCode))
                {
                    result = ClientBase.GetResult(value.StatusCode, ii, diagnosticInfos, responseHeader);
                }
                errors.Add(result);
                ii++;
            }

            return errors;
        }

        /// <summary>
        /// Saves all the subscriptions of the session.
        /// </summary>
        /// <param name="session">session to use</param>
        /// <param name="filePath">The file path.</param>
        /// <param name="knownTypes">Known types</param>
        public static void Save(
            this ISession session,
            string filePath,
            IEnumerable<Type>? knownTypes = null)
        {
            session.Save(filePath, session.Subscriptions, knownTypes);
        }

        /// <summary>
        /// Load the list of subscriptions saved in a file.
        /// </summary>
        /// <param name="session">session to use</param>
        /// <param name="filePath">The file path.</param>
        /// <param name="transferSubscriptions">Load the subscriptions for transfer
        /// after load.</param>
        /// <param name="knownTypes">Additional known types that may be needed to
        /// read the saved subscriptions.</param>
        /// <returns>The list of loaded subscriptions</returns>
        public static IEnumerable<Subscription> Load(
            this ISession session,
            string filePath,
            bool transferSubscriptions = false,
            IEnumerable<Type>? knownTypes = null)
        {
            using FileStream stream = File.OpenRead(filePath);
            return session.Load(stream, transferSubscriptions, knownTypes);
        }

        /// <summary>
        /// Saves a set of subscriptions to a file.
        /// </summary>
        public static void Save(
            this ISession session,
            string filePath,
            IEnumerable<Subscription> subscriptions,
            IEnumerable<Type>? knownTypes = null)
        {
            using var stream = new FileStream(filePath, FileMode.Create);
            session.Save(stream, subscriptions, knownTypes);
        }

        /// <summary>
        /// Creates resend data call requests for the subscriptions.
        /// </summary>
        /// <param name="subscriptionIds">The subscriptions to call resend data.</param>
        private static CallMethodRequestCollection CreateCallRequestsForResendData(
            IEnumerable<uint> subscriptionIds)
        {
            var requests = new CallMethodRequestCollection();

            foreach (uint subscriptionId in subscriptionIds)
            {
                var inputArguments = new VariantCollection { new Variant(subscriptionId) };

                var request = new CallMethodRequest
                {
                    ObjectId = ObjectIds.Server,
                    MethodId = MethodIds.Server_ResendData,
                    InputArguments = inputArguments
                };

                requests.Add(request);
            }
            return requests;
        }
    }
}
