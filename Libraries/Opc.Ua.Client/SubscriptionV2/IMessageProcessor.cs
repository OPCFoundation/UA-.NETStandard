#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) 2005-2020 The OPC Foundation, Inc. All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client.Subscriptions
{
    using Opc.Ua;
    using System.Collections.Generic;
    using System.Threading.Tasks;

    /// <summary>
    /// A subscription component that processes received messages
    /// and dispatches to the subscribers.
    /// </summary>
    internal interface IMessageProcessor
    {
        /// <summary>
        /// Subscription id on the server. Used to look up and
        /// locate the subscription from messages received.
        /// </summary>
        uint Id { get; }

        /// <summary>
        /// Allows the session to add the notification message
        /// to the subscription for dispatch. This is called by
        /// the subscription manager when a message is received.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="availableSequenceNumbers"></param>
        /// <param name="stringTable"></param>
        ValueTask OnPublishReceivedAsync(NotificationMessage message,
            IReadOnlyList<uint>? availableSequenceNumbers,
            IReadOnlyList<string> stringTable);
    }
}
#endif
