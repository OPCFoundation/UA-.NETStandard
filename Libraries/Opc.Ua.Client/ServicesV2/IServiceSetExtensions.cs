#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Services
{
    using Opc.Ua;
    using System.Collections.Generic;
    using System.Threading;

    /// <summary>
    /// Browse result for async enumerator
    /// </summary>
    /// <param name="Description"></param>
    /// <param name="Result"></param>
    public record struct BrowseDescriptionResult(BrowseDescription Description,
        BrowseResult Result);

    /// <summary>
    /// Extended services providing async enumerable support on top of first/next apis
    /// </summary>
    public interface IServiceSetExtensions
    {
        /// <summary>
        /// Enumerates browse results inline
        /// </summary>
        /// <param name="requestHeader"></param>
        /// <param name="view"></param>
        /// <param name="nodesToBrowse"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        IAsyncEnumerable<BrowseDescriptionResult> BrowseAsync(RequestHeader? requestHeader,
            ViewDescription? view, ArrayOf<BrowseDescription> nodesToBrowse,
            CancellationToken ct = default);
    }
}
#endif
