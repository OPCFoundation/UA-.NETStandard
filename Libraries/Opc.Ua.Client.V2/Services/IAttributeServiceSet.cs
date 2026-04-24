// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Services
{
    using Opc.Ua;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Attribute service set
    /// <see href="https://reference.opcfoundation.org/Core/Part4/v105/docs/5.11"/>
    /// </summary>
    public interface IAttributeServiceSet
    {
        /// <summary>
        /// Read service
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <param name="maxAge"></param>
        /// <param name="timestampsToReturn"></param>
        /// <param name="nodesToRead"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<ReadResponse> ReadAsync(RequestHeader? requestHeader, double maxAge,
            TimestampsToReturn timestampsToReturn, ReadValueIdCollection nodesToRead,
            CancellationToken ct = default);

        /// <summary>
        /// Write service
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <param name="nodesToWrite"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<WriteResponse> WriteAsync(RequestHeader? requestHeader,
            WriteValueCollection nodesToWrite, CancellationToken ct = default);

        /// <summary>
        /// History read service
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <param name="historyReadDetails"></param>
        /// <param name="timestampsToReturn"></param>
        /// <param name="releaseContinuationPoints"></param>
        /// <param name="nodesToRead"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<HistoryReadResponse> HistoryReadAsync(RequestHeader? requestHeader,
            ExtensionObject historyReadDetails, TimestampsToReturn timestampsToReturn,
            bool releaseContinuationPoints, HistoryReadValueIdCollection nodesToRead,
            CancellationToken ct = default);

        /// <summary>
        /// History update service
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <param name="historyUpdateDetails"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        Task<HistoryUpdateResponse> HistoryUpdateAsync(RequestHeader? requestHeader,
            ExtensionObjectCollection historyUpdateDetails, CancellationToken ct = default);
    }
}
