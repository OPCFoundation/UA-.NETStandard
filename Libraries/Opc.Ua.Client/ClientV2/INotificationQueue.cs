#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client
{
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Queue notifications
    /// </summary>
    internal interface INotificationQueue
    {
        /// <summary>
        /// Queues notifications to consumers
        /// </summary>
        /// <param name="notification"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        ValueTask QueueAsync(Notification notification,
            CancellationToken ct = default);
    }
}
#endif
