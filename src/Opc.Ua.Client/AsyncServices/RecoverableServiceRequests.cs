/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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

using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.Client
{
    /// <summary>
    /// Defines the closed, NativeAOT-safe set of eligible original request/response pairs.
    /// </summary>
    internal static class RecoverableServiceRequests
    {
        /// <summary>
        /// Validates the selected generated response type before preparing or sending an original request.
        /// </summary>
        /// <typeparam name="TResponse">The exact generated original response type.</typeparam>
        /// <exception cref="ServiceResultException"></exception>
        internal static NodeId GetRequestType<TResponse>(IServiceRequest request) where TResponse : IServiceResponse
        {
            return request switch
            {
                ReadRequest when typeof(TResponse) == typeof(ReadResponse) => DataTypeIds.ReadRequest,
                WriteRequest when typeof(TResponse) == typeof(WriteResponse) => DataTypeIds.WriteRequest,
                CallRequest when typeof(TResponse) == typeof(CallResponse) => DataTypeIds.CallRequest,
                HistoryReadRequest when typeof(TResponse) == typeof(HistoryReadResponse) =>
                    DataTypeIds.HistoryReadRequest,
                HistoryUpdateRequest when typeof(TResponse) == typeof(HistoryUpdateResponse) =>
                    DataTypeIds.HistoryUpdateRequest,
                BrowseRequest when typeof(TResponse) == typeof(BrowseResponse) => DataTypeIds.BrowseRequest,
                BrowseNextRequest when typeof(TResponse) == typeof(BrowseNextResponse) => DataTypeIds.BrowseNextRequest,
                TranslateBrowsePathsToNodeIdsRequest when
                    typeof(TResponse) == typeof(TranslateBrowsePathsToNodeIdsResponse)
                    => DataTypeIds.TranslateBrowsePathsToNodeIdsRequest,
                RegisterNodesRequest when typeof(TResponse) == typeof(RegisterNodesResponse) =>
                    DataTypeIds.RegisterNodesRequest,
                UnregisterNodesRequest when typeof(TResponse) == typeof(UnregisterNodesResponse) =>
                    DataTypeIds.UnregisterNodesRequest,
                QueryFirstRequest when typeof(TResponse) == typeof(QueryFirstResponse) => DataTypeIds.QueryFirstRequest,
                QueryNextRequest when typeof(TResponse) == typeof(QueryNextResponse) => DataTypeIds.QueryNextRequest,
                AddNodesRequest when typeof(TResponse) == typeof(AddNodesResponse) => DataTypeIds.AddNodesRequest,
                AddReferencesRequest when typeof(TResponse) == typeof(AddReferencesResponse) =>
                    DataTypeIds.AddReferencesRequest,
                DeleteNodesRequest when typeof(TResponse) == typeof(DeleteNodesResponse) =>
                    DataTypeIds.DeleteNodesRequest,
                DeleteReferencesRequest when typeof(TResponse) == typeof(DeleteReferencesResponse) =>
                    DataTypeIds.DeleteReferencesRequest,
                _ => throw new ServiceResultException(StatusCodes.BadServiceUnsupported,
                    "The request and response are not an eligible recoverable Service pair.")
            };
        }

        /// <summary>
        /// Uses the existing generated Service methods without reflection, a new query engine or a retry wrapper.
        /// </summary>
        /// <typeparam name="TResponse">The exact generated original response type.</typeparam>
        /// <exception cref="ServiceResultException"></exception>
        internal static async ValueTask<TResponse> InvokeAsync<TResponse>(
            ISessionClient client, IServiceRequest request, CancellationToken ct)
            where TResponse : class, IServiceResponse
        {
            _ = GetRequestType<TResponse>(request);
            IServiceResponse response = request switch
            {
                ReadRequest r => await client.ReadAsync(
                    r.RequestHeader, r.MaxAge, r.TimestampsToReturn, r.NodesToRead, ct).ConfigureAwait(false),
                WriteRequest r => await client.WriteAsync(r.RequestHeader, r.NodesToWrite, ct).ConfigureAwait(false),
                CallRequest r => await client.CallAsync(r.RequestHeader, r.MethodsToCall, ct).ConfigureAwait(false),
                HistoryReadRequest r => await client.HistoryReadAsync(r.RequestHeader, r.HistoryReadDetails,
                    r.TimestampsToReturn, r.ReleaseContinuationPoints, r.NodesToRead, ct).ConfigureAwait(false),
                HistoryUpdateRequest r => await client.HistoryUpdateAsync(r.RequestHeader, r.HistoryUpdateDetails, ct)
                    .ConfigureAwait(false),
                BrowseRequest r => await client.BrowseAsync(r.RequestHeader, r.View, r.RequestedMaxReferencesPerNode,
                    r.NodesToBrowse, ct).ConfigureAwait(false),
                BrowseNextRequest r => await client.BrowseNextAsync(r.RequestHeader, r.ReleaseContinuationPoints,
                    r.ContinuationPoints, ct).ConfigureAwait(false),
                TranslateBrowsePathsToNodeIdsRequest r => await client.TranslateBrowsePathsToNodeIdsAsync(
                    r.RequestHeader, r.BrowsePaths, ct).ConfigureAwait(false),
                RegisterNodesRequest r => await client.RegisterNodesAsync(r.RequestHeader, r.NodesToRegister, ct)
                    .ConfigureAwait(false),
                UnregisterNodesRequest r => await client.UnregisterNodesAsync(r.RequestHeader, r.NodesToUnregister, ct)
                    .ConfigureAwait(false),
                QueryFirstRequest r => await client.QueryFirstAsync(r.RequestHeader, r.View, r.NodeTypes, r.Filter,
                    r.MaxDataSetsToReturn, r.MaxReferencesToReturn, ct).ConfigureAwait(false),
                QueryNextRequest r => await client.QueryNextAsync(r.RequestHeader, r.ReleaseContinuationPoint,
                    r.ContinuationPoint, ct).ConfigureAwait(false),
                AddNodesRequest r => await client.AddNodesAsync(r.RequestHeader, r.NodesToAdd, ct)
                    .ConfigureAwait(false),
                AddReferencesRequest r => await client.AddReferencesAsync(r.RequestHeader, r.ReferencesToAdd, ct)
                    .ConfigureAwait(false),
                DeleteNodesRequest r => await client.DeleteNodesAsync(r.RequestHeader, r.NodesToDelete, ct)
                    .ConfigureAwait(false),
                DeleteReferencesRequest r => await client.DeleteReferencesAsync(
                    r.RequestHeader, r.ReferencesToDelete, ct).ConfigureAwait(false),
                _ => throw new ServiceResultException(StatusCodes.BadServiceUnsupported)
            };
            return response is TResponse typed ? typed : throw InvalidResponse();
        }

        /// <summary>
        /// Checks the complete original response and ordinary per-request result/diagnostic cardinalities.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        internal static void ValidateResponse(IServiceRequest request, IServiceResponse response)
        {
            if (response is ServiceFault)
            {
                if (!StatusCode.IsBad(response.ResponseHeader.ServiceResult))
                {
                    throw InvalidResponse();
                }
                return;
            }
            bool good = StatusCode.IsGood(response.ResponseHeader.ServiceResult);
            switch (request)
            {
                case ReadRequest r when response is ReadResponse value:
                    if (good)
                    {
                        ClientBase.ValidateResponse(value.Results, r.NodesToRead);
                        ClientBase.ValidateDiagnosticInfos(value.DiagnosticInfos, r.NodesToRead);
                    }
                    break;
                case WriteRequest r when response is WriteResponse value:
                    if (good)
                    {
                        ClientBase.ValidateResponse(value.Results, r.NodesToWrite);
                        ClientBase.ValidateDiagnosticInfos(value.DiagnosticInfos, r.NodesToWrite);
                    }
                    break;
                case CallRequest r when response is CallResponse value:
                    if (good)
                    {
                        ClientBase.ValidateResponse(value.Results, r.MethodsToCall);
                        ClientBase.ValidateDiagnosticInfos(value.DiagnosticInfos, r.MethodsToCall);
                    }
                    break;
                case HistoryReadRequest r when response is HistoryReadResponse value:
                    if (good)
                    {
                        ClientBase.ValidateResponse(value.Results, r.NodesToRead);
                        ClientBase.ValidateDiagnosticInfos(value.DiagnosticInfos, r.NodesToRead);
                    }
                    break;
                case HistoryUpdateRequest r when response is HistoryUpdateResponse value:
                    if (good)
                    {
                        ClientBase.ValidateResponse(value.Results, r.HistoryUpdateDetails);
                        ClientBase.ValidateDiagnosticInfos(value.DiagnosticInfos, r.HistoryUpdateDetails);
                    }
                    break;
                case BrowseRequest r when response is BrowseResponse value:
                    if (good)
                    {
                        ClientBase.ValidateResponse(value.Results, r.NodesToBrowse);
                        ClientBase.ValidateDiagnosticInfos(value.DiagnosticInfos, r.NodesToBrowse);
                    }
                    break;
                case BrowseNextRequest r when response is BrowseNextResponse value:
                    if (good)
                    {
                        if (r.ReleaseContinuationPoints)
                        {
                            if (!value.Results.IsEmpty || !value.DiagnosticInfos.IsEmpty)
                            {
                                throw InvalidResponse();
                            }
                        }
                        else
                        {
                            ClientBase.ValidateResponse(value.Results, r.ContinuationPoints);
                            ClientBase.ValidateDiagnosticInfos(value.DiagnosticInfos, r.ContinuationPoints);
                        }
                    }
                    break;
                case TranslateBrowsePathsToNodeIdsRequest r
                    when response is TranslateBrowsePathsToNodeIdsResponse value:
                    if (good)
                    {
                        ClientBase.ValidateResponse(value.Results, r.BrowsePaths);
                        ClientBase.ValidateDiagnosticInfos(value.DiagnosticInfos, r.BrowsePaths);
                    }
                    break;
                case RegisterNodesRequest r when response is RegisterNodesResponse value:
                    if (good)
                    {
                        ClientBase.ValidateResponse(value.RegisteredNodeIds, r.NodesToRegister);
                    }
                    break;
                case UnregisterNodesRequest when response is UnregisterNodesResponse:
                case QueryNextRequest when response is QueryNextResponse:
                    break;
                case QueryFirstRequest r when response is QueryFirstResponse value:
                    if (good)
                    {
                        if (!value.ParsingResults.IsEmpty)
                        {
                            ClientBase.ValidateResponse(value.ParsingResults, r.NodeTypes);
                        }
                        ClientBase.ValidateDiagnosticInfos(value.DiagnosticInfos, r.NodeTypes);
                    }
                    break;
                case AddNodesRequest r when response is AddNodesResponse value:
                    if (good)
                    {
                        ClientBase.ValidateResponse(value.Results, r.NodesToAdd);
                        ClientBase.ValidateDiagnosticInfos(value.DiagnosticInfos, r.NodesToAdd);
                    }
                    break;
                case AddReferencesRequest r when response is AddReferencesResponse value:
                    if (good)
                    {
                        ClientBase.ValidateResponse(value.Results, r.ReferencesToAdd);
                        ClientBase.ValidateDiagnosticInfos(value.DiagnosticInfos, r.ReferencesToAdd);
                    }
                    break;
                case DeleteNodesRequest r when response is DeleteNodesResponse value:
                    if (good)
                    {
                        ClientBase.ValidateResponse(value.Results, r.NodesToDelete);
                        ClientBase.ValidateDiagnosticInfos(value.DiagnosticInfos, r.NodesToDelete);
                    }
                    break;
                case DeleteReferencesRequest r when response is DeleteReferencesResponse value:
                    if (good)
                    {
                        ClientBase.ValidateResponse(value.Results, r.ReferencesToDelete);
                        ClientBase.ValidateDiagnosticInfos(value.DiagnosticInfos, r.ReferencesToDelete);
                    }
                    break;
                default:
                    throw InvalidResponse();
            }
        }

        /// <summary>
        /// Reports a wrong original response without treating it as an accepted operation failure.
        /// </summary>
        private static ServiceResultException InvalidResponse()
        {
            return new ServiceResultException(StatusCodes.BadDecodingError,
                "The retained original response does not match its submitted request.");
        }
    }
}
