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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Extensions to ISessionClient that are not dependent on anything internal
    /// to the client but layer over ISessionClient.
    /// </summary>
    public static class SessionClientExtensions
    {
        /// <summary>
        /// Reads the values for a set of variables.
        /// </summary>
        public static async ValueTask<(
            ArrayOf<Variant>,
            ArrayOf<ServiceResult>
            )> ReadValuesAsync(
                this ISessionClient session,
                ArrayOf<NodeId> variableIds,
                ArrayOf<TypeInfo> expectedTypes,
                CancellationToken ct = default)
        {
            (ArrayOf<DataValue> dataValues, ArrayOf<ServiceResult> errorValues) =
                await session.ReadValuesAsync(
                    variableIds,
                    ct).ConfigureAwait(false);

            var values = new Variant[dataValues.Count];
            var errors = new ServiceResult[errorValues.Count];
            for (int ii = 0; ii < variableIds.Count; ii++)
            {
                if (dataValues[ii].WrappedValue.TypeInfo != expectedTypes[ii])
                {
                    errors[ii] = ServiceResult.Create(
                        StatusCodes.BadTypeMismatch,
                        "Value {0} does not have expected type: {1}.",
                        dataValues[ii],
                        expectedTypes[ii]);
                    continue;
                }
                // suitable value found.
                errors[ii] = errorValues[ii];
                values[ii] = dataValues[ii].WrappedValue;
            }
            return (values, errors);
        }

        /// <summary>
        /// Reads the values for a set of variables.
        /// </summary>
        public static async ValueTask<(
            ArrayOf<object>,
            ArrayOf<ServiceResult>
            )> ReadValuesAsync(
                this ISessionClient session,
                ArrayOf<NodeId> variableIds,
                ArrayOf<Type> expectedTypes,
                CancellationToken ct = default)
        {
            (ArrayOf<DataValue> dataValues, ArrayOf<ServiceResult> errorValues) =
                await session.ReadValuesAsync(variableIds, ct).ConfigureAwait(false);

            ServiceResult[] errors = new ServiceResult[errorValues.Count];
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
                errors[ii] = errorValues[ii];
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
            ByteString,
            ArrayOf<ReferenceDescription>
            )> BrowseAsync(
                this ISessionClient session,
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
            (
                ResponseHeader? responseHeader,
                ArrayOf<ByteString> continuationPoints,
                ArrayOf<ArrayOf<ReferenceDescription>> referencesList,
                ArrayOf<ServiceResult> errors
            ) =
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
            ByteString,
            ArrayOf<ReferenceDescription>
            )> BrowseNextAsync(
                this ISessionClient session,
                RequestHeader? requestHeader,
                bool releaseContinuationPoint,
                ByteString continuationPoint,
                CancellationToken ct = default)
        {
            (
                ResponseHeader? responseHeader,
                ArrayOf<ByteString> revisedContinuationPoints,
                ArrayOf<ArrayOf<ReferenceDescription>> referencesList,
                ArrayOf<ServiceResult> errors
            ) =
                await session.BrowseNextAsync(
                    requestHeader,
                    [continuationPoint],
                    releaseContinuationPoint,
                    ct).ConfigureAwait(false);
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
            ArrayOf<ArrayOf<ReferenceDescription>>,
            ArrayOf<ServiceResult>
            )> ManagedBrowseAsync(
                this ISessionClient session,
                RequestHeader? requestHeader,
                ViewDescription? view,
                ArrayOf<NodeId> nodesToBrowse,
                uint maxResultsToReturn,
                BrowseDirection browseDirection,
                NodeId referenceTypeId,
                bool includeSubtypes,
                uint nodeClassMask,
                CancellationToken ct = default)
        {
            var browser = new Browser(session, new BrowserOptions
            {
                RequestHeader = requestHeader,
                View = view,
                MaxReferencesReturned = maxResultsToReturn,
                BrowseDirection = browseDirection,
                ReferenceTypeId = referenceTypeId,
                IncludeSubtypes = includeSubtypes,
                NodeClassMask = (int)nodeClassMask
            });
            ResultSet<ArrayOf<ReferenceDescription>> result =
                await browser.BrowseAsync(nodesToBrowse, ct).ConfigureAwait(false);
            return (result.Results, result.Errors);
        }

        /// <summary>
        /// Fetches all references for the specified node.
        /// </summary>
        /// <param name="session">session to use</param>
        /// <param name="nodeId">The node id.</param>
        /// <param name="ct">Cancellation token to cancel operation with</param>
        public static async Task<ArrayOf<ReferenceDescription>> FetchReferencesAsync(
            this ISessionClient session,
            NodeId nodeId,
            CancellationToken ct = default)
        {
            (ArrayOf<ArrayOf<ReferenceDescription>> descriptions, _) =
                await session.ManagedBrowseAsync(
                    null,
                    null,
                    [nodeId],
                    0,
                    BrowseDirection.Both,
                    default,
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
        /// <param name="nodeIds">The node ids to collect references for.</param>
        /// <param name="ct">Cancellation token to cancel operation with</param>
        /// <returns>A list of reference description lists and the errors reported
        /// by the server.</returns>
        public static Task<(
            ArrayOf<ArrayOf<ReferenceDescription>>,
            ArrayOf<ServiceResult>
            )> FetchReferencesAsync(
                this ISessionClient session,
                ArrayOf<NodeId> nodeIds,
                CancellationToken ct = default)
        {
            return session.ManagedBrowseAsync(
                null,
                null,
                nodeIds,
                0,
                BrowseDirection.Both,
                default,
                true,
                0,
                ct);
        }

        /// <summary>
        /// Reads the values for the node attributes and returns a list of nodes.
        /// </summary>
        /// <remarks>
        /// If the nodeclass for the nodes in the array of NodeId is already known
        /// and passed as nodeClass, reads only values of required attributes.
        /// Otherwise NodeClass.Unspecified should be used.
        /// </remarks>
        /// <param name="session">The session to use</param>
        /// <param name="nodeIds">The array of NodeIds to read.</param>
        /// <param name="nodeClass">The nodeClass of all nodes in the array.
        /// Set to <c>NodeClass.Unspecified</c> if the nodeclass is unknown.</param>
        /// <param name="optionalAttributes">Set to <c>true</c> if optional attributes
        /// should not be omitted.</param>
        /// <param name="ct">The cancellation token.</param>
        /// <returns>The array of nodes and associated errors.</returns>
        public static async Task<(ArrayOf<Node>, ArrayOf<ServiceResult>)> ReadNodesAsync(
            this ISessionClient session,
            ArrayOf<NodeId> nodeIds,
            NodeClass nodeClass,
            bool optionalAttributes = false,
            CancellationToken ct = default)
        {
            var nodeCacheContext = new NodeCacheContext(session);
            ResultSet<Node> result = await nodeCacheContext.FetchNodesAsync(
                null,
                nodeIds,
                nodeClass,
                !optionalAttributes,
                ct).ConfigureAwait(false);
            return (result.Results, result.Errors);
        }

        /// <summary>
        /// Reads the values for the node attributes and returns a array of nodes.
        /// Reads the nodeclass of the nodeIds, then reads
        /// the values for the node attributes and returns a array of nodes.
        /// </summary>
        /// <param name="session">The session to use</param>
        /// <param name="nodeIds">The array of NodeIds to read.</param>
        /// <param name="optionalAttributes">If optional attributes to read.</param>
        /// <param name="ct">The cancellation token.</param>
        public static async Task<(ArrayOf<Node>, ArrayOf<ServiceResult>)> ReadNodesAsync(
            this ISessionClient session,
            ArrayOf<NodeId> nodeIds,
            bool optionalAttributes = false,
            CancellationToken ct = default)
        {
            var nodeCacheContext = new NodeCacheContext(session);
            ResultSet<Node> result = await nodeCacheContext.FetchNodesAsync(
                null,
                nodeIds,
                !optionalAttributes,
                ct).ConfigureAwait(false);
            return (result.Results, result.Errors);
        }

        /// <summary>
        /// Reads the values for the node attributes and returns a node object.
        /// </summary>
        /// <param name="session">The session to use</param>
        /// <param name="nodeId">The nodeId.</param>
        /// <param name="ct">The cancellation token for the request.</param>
        public static Task<Node> ReadNodeAsync(
            this ISessionClient session,
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
            this ISessionClient session,
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
                !optionalAttributes,
                ct).AsTask();
        }

        /// <summary>
        /// Read display name for a set of nodes
        /// </summary>
        /// <param name="session">The session to use</param>
        /// <param name="nodeIds">node for which to read display name</param>
        /// <param name="ct">Cancellation token to use to cancel the operation</param>
        /// <returns>Paired list of displaynames and potential errors per node</returns>
        public static async Task<(ArrayOf<string>, ArrayOf<ServiceResult>)> ReadDisplayNameAsync(
            this ISessionClient session,
            ArrayOf<NodeId> nodeIds,
            CancellationToken ct = default)
        {
            var displayNames = new List<string>();
            var errors = new List<ServiceResult>();

            // build list of values to read.
            ArrayOf<ReadValueId> valuesToRead = nodeIds.ConvertAll(n => new ReadValueId
            {
                NodeId = n,
                AttributeId = Attributes.DisplayName,
                IndexRange = null,
                DataEncoding = QualifiedName.Null
            });

            // read the values.

            ReadResponse response = await session.ReadAsync(
                null,
                int.MaxValue,
                TimestampsToReturn.Neither,
                valuesToRead,
                ct).ConfigureAwait(false);

            ArrayOf<DataValue> results = response.Results;
            ArrayOf<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos;
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

                if (!displayName.IsNullOrEmpty)
                {
                    displayNames[ii] = displayName.Text;
                }
            }

            return (displayNames.ToArrayOf(), errors.ToArrayOf());
        }

        /// <summary>
        /// Returns the data description for the encoding.
        /// </summary>
        /// <param name="session">The session to use</param>
        /// <param name="encodingId">The encoding Id.</param>
        /// <param name="ct">Cancellation token to use to cancel the operation</param>
        /// <exception cref="ServiceResultException"></exception>
        public static async Task<ReferenceDescription> FindDataDescriptionAsync(
            this ISessionClient session,
            NodeId encodingId,
            CancellationToken ct = default)
        {
            var browser = new Browser(session, new BrowserOptions
            {
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = ReferenceTypeIds.HasDescription,
                IncludeSubtypes = false,
                NodeClassMask = 0
            });

            ArrayOf<ReferenceDescription> references =
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
        /// Reads the value for a node of type T or throws if not matching the type.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="session">The session to use</param>
        /// <param name="nodeId">The node Id.</param>
        /// <param name="ct">The cancellation token for the request.</param>
        /// <exception cref="ServiceResultException"></exception>
        public static async Task<T> ReadValueAsync<T>(
            this ISessionClient session,
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
            this ISessionClient session,
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
        /// Reads the values for a array of nodes. Returns diagnostic errors.
        /// </summary>
        /// <param name="session">The session to use</param>
        /// <param name="nodeIds">The node Id.</param>
        /// <param name="ct">The cancellation token for the request.</param>
        public static async Task<(ArrayOf<DataValue>, ArrayOf<ServiceResult>)> ReadValuesAsync(
            this ISessionClient session,
            ArrayOf<NodeId> nodeIds,
            CancellationToken ct = default)
        {
            var nodeCacheContext = new NodeCacheContext(session);
            ResultSet<DataValue> result = await nodeCacheContext.FetchValuesAsync(
                null,
                nodeIds,
                ct).ConfigureAwait(false);
            return (result.Results, result.Errors);
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
            ArrayOf<ByteString> continuationPoints,
            ArrayOf<ArrayOf<ReferenceDescription>> referencesList,
            ArrayOf<ServiceResult> errors
        )> BrowseAsync(
            this ISessionClient session,
            RequestHeader? requestHeader,
            ViewDescription? view,
            ArrayOf<NodeId> nodesToBrowse,
            uint maxResultsToReturn,
            BrowseDirection browseDirection,
            NodeId referenceTypeId,
            bool includeSubtypes,
            uint nodeClassMask,
            CancellationToken ct = default)
        {
            ArrayOf<BrowseDescription> browseDescriptions = nodesToBrowse.
                ConvertAll(nodeToBrowse => new BrowseDescription
                {
                    NodeId = nodeToBrowse,
                    BrowseDirection = browseDirection,
                    ReferenceTypeId = referenceTypeId,
                    IncludeSubtypes = includeSubtypes,
                    NodeClassMask = nodeClassMask,
                    ResultMask = (uint)BrowseResultMask.All
                });

            BrowseResponse browseResponse = await session.BrowseAsync(
                requestHeader,
                view,
                maxResultsToReturn,
                browseDescriptions,
                ct).ConfigureAwait(false);

            ArrayOf<BrowseResult> results = browseResponse.Results;
            ArrayOf<DiagnosticInfo> diagnosticInfos = browseResponse.DiagnosticInfos;

            ClientBase.ValidateResponse(results, browseDescriptions);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, browseDescriptions);

            int ii = 0;
            var errors = new List<ServiceResult>();
            var continuationPoints = new List<ByteString>();
            var referencesList = new List<ArrayOf<ReferenceDescription>>();
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

            return (
                browseResponse.ResponseHeader,
                continuationPoints.ToArrayOf(),
                referencesList.ToArrayOf(),
                errors.ToArrayOf());
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
            ArrayOf<ByteString> revisedContinuationPoints,
            ArrayOf<ArrayOf<ReferenceDescription>> referencesList,
            ArrayOf<ServiceResult> errors
        )> BrowseNextAsync(
            this ISessionClient session,
            RequestHeader? requestHeader,
            ArrayOf<ByteString> continuationPoints,
            bool releaseContinuationPoint,
            CancellationToken ct = default)
        {
            BrowseNextResponse response = await session.BrowseNextAsync(
                requestHeader,
                releaseContinuationPoint,
                continuationPoints,
                ct).ConfigureAwait(false);

            ArrayOf<BrowseResult> results = response.Results;
            ArrayOf<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos;

            ClientBase.ValidateResponse(results, continuationPoints);
            ClientBase.ValidateDiagnosticInfos(diagnosticInfos, continuationPoints);

            int ii = 0;
            var errors = new List<ServiceResult>();
            var revisedContinuationPoints = new List<ByteString>();
            var referencesList = new List<ArrayOf<ReferenceDescription>>();
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

            return (
                response.ResponseHeader,
                revisedContinuationPoints.ToArrayOf(),
                referencesList.ToArrayOf(),
                errors.ToArrayOf());
        }

        /// <summary>
        /// Reads a byte string value safely in fragments if needed. Uses the byte
        /// string size limits to chunk the reads if needed. The first read happens
        /// as usual and no stream is allocated, if the result is below the limits
        /// the buffer that is read into is returned, otherwise buffers are added
        /// to a memory stream whose content is finally returned.
        /// </summary>
        /// <param name="session">session to use</param>
        /// <param name="nodeId">The node id of a byte string variable</param>
        /// <param name="maxByteStringLength">A chunk size to enforce</param>
        /// <param name="ct">Cancellation token to cancel operation with</param>
        /// <exception cref="ServiceResultException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public static async ValueTask<ByteString> ReadBytesAsync(
            this ISessionClient session,
            NodeId nodeId,
            int maxByteStringLength,
            CancellationToken ct = default)
        {
            if (maxByteStringLength == 0 ||
                maxByteStringLength > session.MessageContext.MaxByteStringLength)
            {
                maxByteStringLength = session.MessageContext.MaxByteStringLength;
            }
            if (maxByteStringLength <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxByteStringLength),
                    "maxByteStringLength must be a positive integer.");
            }

            int offset = 0;
            MemoryStream? stream = null;
            try
            {
                while (true)
                {
                    var valueToRead = new ReadValueId
                    {
                        NodeId = nodeId,
                        AttributeId = Attributes.Value,
                        IndexRange = new NumericRange(
                            offset,
                            // Range is inclusive and starts at 0. Therefore
                            // to read 5 bytes you need to specify 0-4.
                            offset + maxByteStringLength - 1).ToString(),
                        DataEncoding = QualifiedName.Null
                    };
                    ArrayOf<ReadValueId> readValueIds = [valueToRead];

                    ReadResponse result = await session.ReadAsync(
                        null,
                        0,
                        TimestampsToReturn.Neither,
                        readValueIds,
                        ct)
                        .ConfigureAwait(false);

                    ResponseHeader responseHeader = result.ResponseHeader;
                    ArrayOf<DataValue> results = result.Results;
                    ArrayOf<DiagnosticInfo> diagnosticInfos = result.DiagnosticInfos;
                    ClientBase.ValidateResponse(results, readValueIds);
                    ClientBase.ValidateDiagnosticInfos(diagnosticInfos, readValueIds);

                    Variant wrappedValue = results[0].WrappedValue;

                    // First call returned bytes, next a string, that we cannot tolerate
                    // But we allow null byte string which signals the end of the stream.
                    if (!wrappedValue.IsNull &&
                        (wrappedValue.TypeInfo.BuiltInType != BuiltInType.ByteString ||
                            wrappedValue.TypeInfo.ValueRank != ValueRanks.Scalar))
                    {
                        throw new ServiceResultException(
                            StatusCodes.BadTypeMismatch,
                            "Value is not a scalar ByteString.");
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

                    if (!results[0].WrappedValue.TryGet(out ByteString chunk) || chunk.Length == 0)
                    {
                        // End of stream - fast path (no stream allocated yet)
                        // will return empty array constant.
                        break;
                    }
                    if (chunk.Length < maxByteStringLength && offset == 0)
                    {
                        // Fast path for small values, just return the chunk
                        return chunk;
                    }
                    stream ??= new MemoryStream();
#if NET8_0_OR_GREATER
                    await stream.WriteAsync(chunk.Memory, ct).ConfigureAwait(false);
#else
                    stream.Write(chunk.ToArray(), 0, chunk.Length);
#endif
                    if (chunk.Length < maxByteStringLength)
                    {
                        break;
                    }
                    offset += maxByteStringLength;
                }
                return stream != null ? stream.ToArray().ToByteString() : ByteString.Empty;
            }
            finally
            {
#if NET8_0_OR_GREATER
                if (stream != null)
                {
                    await stream.DisposeAsync().ConfigureAwait(false);
                }
#else
                stream?.Dispose();
#endif
            }
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
        public static async Task<ArrayOf<Variant>> CallAsync(
            this ISessionClient session,
            NodeId objectId,
            NodeId methodId,
            CancellationToken ct = default,
            params Variant[] args)
        {
            var request = new CallMethodRequest
            {
                ObjectId = objectId,
                MethodId = methodId,
                InputArguments = args
            };

            ArrayOf<CallMethodRequest> requests = [request];

            CallResponse response = await session.CallAsync(
                null,
                requests,
                ct).ConfigureAwait(false);

            ArrayOf<CallMethodResult> results = response.Results;
            ArrayOf<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos;

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

            return results[0].OutputArguments;
        }

        /// <summary>
        /// Call the ResendData method on the server for all subscriptions.
        /// </summary>
        public static async ValueTask<ArrayOf<ServiceResult>> ResendDataAsync(
            this ISessionClient session,
            ArrayOf<uint> subscriptionIds,
            CancellationToken ct = default)
        {
            ArrayOf<CallMethodRequest> requests = subscriptionIds
                .ConvertAll(subscriptionId => new CallMethodRequest
                {
                    ObjectId = ObjectIds.Server,
                    MethodId = MethodIds.Server_ResendData,
                    InputArguments = [Variant.From(subscriptionId)]
                });

            var errors = new List<ServiceResult>(requests.Count);

            CallResponse response = await session.CallAsync(null, requests, ct).ConfigureAwait(false);
            ArrayOf<CallMethodResult> results = response.Results;
            ArrayOf<DiagnosticInfo> diagnosticInfos = response.DiagnosticInfos;
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
            return errors.ToArrayOf();
        }
    }
}
