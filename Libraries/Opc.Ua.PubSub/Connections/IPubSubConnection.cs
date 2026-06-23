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

using System;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Groups;
using Opc.Ua.PubSub.StateMachine;

namespace Opc.Ua.PubSub.Connections
{
    /// <summary>
    /// Runtime view of one <see cref="PubSubConnectionDataType"/>:
    /// the transport binding, the publisher identity, and the
    /// writer / reader groups owned by the connection.
    /// </summary>
    /// <remarks>
    /// Implements the PubSubConnection contract from
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/6.2.7">
    /// Part 14 §6.2.7 PubSubConnection</see>.
    /// </remarks>
    public interface IPubSubConnection
    {
        /// <summary>
        /// Connection name (matches
        /// <see cref="PubSubConnectionDataType.Name"/>).
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Publisher identity advertised in outbound NetworkMessage
        /// headers. Configured per connection per Part 14 §6.2.7.
        /// </summary>
        PublisherId PublisherId { get; }

        /// <summary>
        /// Transport profile URI bound to the connection (e.g.
        /// <see cref="Profiles.PubSubUdpUadpTransport"/>).
        /// </summary>
        string TransportProfileUri { get; }

        /// <summary>
        /// Writer groups attached to this connection.
        /// </summary>
        ArrayOf<IWriterGroup> WriterGroups { get; }

        /// <summary>
        /// Reader groups attached to this connection.
        /// </summary>
        ArrayOf<IReaderGroup> ReaderGroups { get; }

        /// <summary>
        /// Original configuration record this runtime view was
        /// instantiated from.
        /// </summary>
        PubSubConnectionDataType Configuration { get; }

        /// <summary>
        /// State machine participating in the application cascade.
        /// </summary>
        PubSubStateMachine State { get; }

        /// <summary>
        /// Drives the connection to the
        /// <see cref="PubSubState.Operational"/> state via the
        /// <see cref="PubSubStateTransitionReason.ByMethod"/>
        /// transition.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask EnableAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Drives the connection to the
        /// <see cref="PubSubState.Disabled"/> state via the
        /// <see cref="PubSubStateTransitionReason.ByMethod"/>
        /// transition, cascading to all child groups and writers /
        /// readers per Part 14 §9.1.3.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask DisableAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Invokes a PubSub Action through this connection and awaits the
        /// correlated response.
        /// </summary>
        ValueTask<PubSubActionResponse> InvokeActionAsync(
            PubSubActionRequest request,
            TimeSpan timeout,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Registers a responder-side Action handler for a target.
        /// </summary>
        void RegisterActionHandler(
            PubSubActionTarget target,
            IPubSubActionHandler handler,
            bool allowUnsecured = false);
    }
}
