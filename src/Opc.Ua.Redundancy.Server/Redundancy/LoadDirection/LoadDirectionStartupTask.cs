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
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Opc.Ua.Server;
using Opc.Ua.Server.Hosting;

namespace Opc.Ua.Redundancy.Server
{
    /// <summary>
    /// Server startup task that activates the <see cref="ServerLoadDirector"/> once the server is started: it builds
    /// the context-dependent collaborators (peer view, direction policy, endpoint directory and publisher) with the
    /// populated server message context and the local ServerUri, then calls <see cref="ServerLoadDirector.Configure"/>.
    /// </summary>
    public sealed class LoadDirectionStartupTask : IServerStartupTask
    {
        /// <summary>
        /// Creates the task.
        /// </summary>
        /// <param name="store">The shared store the direction signals and endpoints are gossiped through.</param>
        /// <param name="protector">Protects record integrity.</param>
        /// <param name="options">The load-direction options.</param>
        /// <param name="director">The director to activate.</param>
        /// <param name="timeProvider">The time source for staleness checks.</param>
        public LoadDirectionStartupTask(
            ISharedKeyValueStore store,
            IRecordProtector protector,
            LoadDirectionOptions options,
            ServerLoadDirector director,
            TimeProvider timeProvider)
        {
            m_store = store ?? throw new ArgumentNullException(nameof(store));
            m_protector = protector ?? throw new ArgumentNullException(nameof(protector));
            m_options = options ?? throw new ArgumentNullException(nameof(options));
            m_director = director ?? throw new ArgumentNullException(nameof(director));
            m_timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        }

        /// <inheritdoc/>
        public ValueTask OnServerStartedAsync(IServerInternal server, CancellationToken cancellationToken = default)
        {
            if (server == null)
            {
                throw new ArgumentNullException(nameof(server));
            }

            string[] serverUris = server.ServerUris.ToArray();
            string? localServerUri = serverUris.Length > 0 ? serverUris[0] : null;
            if (string.IsNullOrEmpty(localServerUri))
            {
                server.Telemetry
                    .CreateLogger<LoadDirectionStartupTask>()
                    .LoadDirectionDisabledLocalServerUriUnavailable();
                return default;
            }

            IServiceMessageContext context = server.MessageContext;
            var view = new SharedPeerDirectionView(m_store, context, m_protector, m_options, m_timeProvider);
            var policy = new BandedServerDirectionPolicy(view, m_options, LoadDirectionRandom.NextIndex);
            var directory = new SharedPeerEndpointDirectory(m_store, context, m_protector, m_options);
            var endpointPublisher = new SharedPeerEndpointPublisher(
                m_store, context, m_protector, m_options, localServerUri!);

            m_director.Configure(policy, directory, endpointPublisher, localServerUri!);
            return default;
        }

        private readonly ISharedKeyValueStore m_store;
        private readonly IRecordProtector m_protector;
        private readonly LoadDirectionOptions m_options;
        private readonly ServerLoadDirector m_director;
        private readonly TimeProvider m_timeProvider;
    }

    /// <summary>
    /// Source-generated log messages for <see cref="LoadDirectionStartupTask"/>.
    /// </summary>
    internal static partial class LoadDirectionStartupTaskLog
    {
        [LoggerMessage(EventId = RedundancyServerEventIds.LoadDirectionStartupTask + 0, Level = LogLevel.Warning,
            Message = "Load direction disabled: the local ServerUri is unavailable.")]
        public static partial void LoadDirectionDisabledLocalServerUriUnavailable(this ILogger logger);
    }

}
