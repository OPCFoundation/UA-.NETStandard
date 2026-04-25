#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client
{
    using Microsoft.Extensions.Options;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Subscribe to notifications
    /// </summary>
    public interface ISubscribe
    {
        /// <summary>
        /// Subscribe to notifications
        /// </summary>
        /// <param name="subscription"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask<IReader<Notification>> SubscribeAsync(
            IOptionsMonitor<SubscriptionClientOptions> subscription,
            CancellationToken ct = default);
    }
}
#endif
