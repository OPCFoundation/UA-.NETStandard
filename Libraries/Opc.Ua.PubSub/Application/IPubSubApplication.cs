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
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.PubSub.Connections;
using Opc.Ua.PubSub.MetaData;
using Opc.Ua.PubSub.StateMachine;

namespace Opc.Ua.PubSub.Application
{
    /// <summary>
    /// Top-level runtime aggregator for a single PubSub
    /// application. Hosts the connections, the shared
    /// <see cref="IDataSetMetaDataRegistry"/>, and the root state
    /// machine all child components cascade from.
    /// </summary>
    /// <remarks>
    /// Implements the Application abstraction described in
    /// <see href="https://reference.opcfoundation.org/specs/OPC-10000-14/v1.05.06/9.1.2">
    /// Part 14 §9.1.2 PubSub address space root</see>.
    /// </remarks>
    public interface IPubSubApplication : IAsyncDisposable
    {
        /// <summary>
        /// Application identifier (typically the OPC UA application
        /// URI). Surfaces in diagnostics and logging.
        /// </summary>
        string ApplicationId { get; }

        /// <summary>
        /// Configured connections.
        /// </summary>
        IReadOnlyList<IPubSubConnection> Connections { get; }

        /// <summary>
        /// Shared metadata registry. Publishers register; subscribers
        /// resolve at decode time.
        /// </summary>
        IDataSetMetaDataRegistry MetaDataRegistry { get; }

        /// <summary>
        /// Root state machine. Disabling the application cascades
        /// disable to every connection per Part 14 §9.1.3.
        /// </summary>
        PubSubStateMachine State { get; }

        /// <summary>
        /// Starts the application, opening all configured
        /// connections.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask StartAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops the application, cascading disable to all
        /// connections and draining in-flight operations.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        ValueTask StopAsync(CancellationToken cancellationToken = default);
    }
}
