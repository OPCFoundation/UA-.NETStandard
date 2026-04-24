// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client
{
    using System;
    using System.Collections.Generic;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Endpoint discovery services
    /// </summary>
    public interface IDiscovery
    {
        /// <summary>
        /// Find endpoints using the specified discovery url and locales.
        /// </summary>
        /// <param name="discoveryUrl"></param>
        /// <param name="locales"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<IReadOnlySet<FoundEndpoint>> FindEndpointsAsync(Uri discoveryUrl,
            IReadOnlyList<string>? locales = null, CancellationToken ct = default);
    }
}
