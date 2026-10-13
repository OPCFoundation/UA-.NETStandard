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

#if NET10_0
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Opc.Ua.Client;

namespace Opc.Ua.Tools.Tests.McpCompanion
{
    /// <summary>
    /// Keeps the existing model script below a real ManagedSession and its generation-bound transport dispatch.
    /// The public factory establishes readiness; production capture has no mock or private-state bypass.
    /// </summary>
    internal sealed class GdsManagedSession : IAsyncDisposable
    {
        /// <summary>
        /// Creates the transport-backed inner session without opening a socket or a server process.
        /// </summary>
        private GdsManagedSession(RemoteModelTestSession remote, TimeProvider clock)
        {
            m_remote = remote;
            ITelemetryContext telemetry = remote.Manager.Telemetry;
            ConfiguredEndpoint endpoint = remote.Session.Object.ConfiguredEndpoint;
            EndpointConfiguration endpointConfiguration = endpoint.Configuration ?? EndpointConfiguration.Create();
            m_configuration = new ApplicationConfiguration(telemetry)
            {
                ClientConfiguration = new ClientConfiguration(),
                SecurityConfiguration = new SecurityConfiguration()
            };
            var channel = new Mock<ITransportChannel>();
            channel.SetupGet(value => value.MessageContext).Returns(remote.Context);
            channel.SetupGet(value => value.EndpointDescription).Returns(endpoint.Description);
            channel.SetupGet(value => value.EndpointConfiguration).Returns(endpointConfiguration);
            channel.SetupGet(value => value.OperationTimeout).Returns(30_000);
            channel.Setup(value => value.SendRequestAsync(
                    It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()))
                .Returns<IServiceRequest, CancellationToken>(DispatchAsync);
            Inner = new Session(channel.Object, m_configuration, endpoint,
                engineFactory: DefaultSubscriptionEngineFactory.Instance, timeProvider: clock);
            Inner.NamespaceUris.Update(remote.Context.NamespaceUris.ToArray());
            Inner.SessionCreated(remote.Id("session"), remote.Id("authentication"));
            var factory = new Mock<ISessionFactory>();
            factory.SetupGet(value => value.Telemetry).Returns(telemetry);
            factory.SetupGet(value => value.SubscriptionEngineFactory)
                .Returns(DefaultSubscriptionEngineFactory.Instance);
            factory.Setup(value => value.CreateAsync(
                    m_configuration, endpoint, false, false, It.IsAny<string>(), It.IsAny<uint>(),
                    It.IsAny<IUserIdentity>(), It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Inner);
            m_factory = factory.Object;
        }

        /// <summary>
        /// Gets the actual session that can be recreated in place.
        /// </summary>
        internal Session Inner { get; }

        /// <summary>
        /// Gets the genuine managed owner accepted by SessionRequestContext.
        /// </summary>
        internal ManagedSession Managed { get; private set; } = null!;

        /// <summary>
        /// Gets only requests which reached physical transport dispatch.
        /// </summary>
        internal List<IServiceRequest> Requests { get; } = [];

        /// <summary>
        /// Uses the public factory to connect the genuine managed owner and establish all readiness signals.
        /// </summary>
        internal static async Task<GdsManagedSession> CreateAsync(
            RemoteModelTestSession remote, TimeProvider clock)
        {
            var result = new GdsManagedSession(remote, clock);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            result.Managed = await ManagedSession.CreateAsync(
                result.m_configuration, remote.Session.Object.ConfiguredEndpoint, result.m_factory,
                telemetry: remote.Manager.Telemetry, sessionName: "GdsManagedSession",
                timeProvider: clock, updateBeforeConnect: false, ct: timeout.Token).ConfigureAwait(false);
            return result;
        }

        /// <summary>
        /// Recreates the same inner object with new server authentication identity.
        /// </summary>
        internal void Recreate(string name)
        {
            Inner.SessionCreated(m_remote.Id(name), m_remote.Id(name + "-authentication"));
        }

        /// <summary>
        /// Simulates remote session invalidation after an acknowledged response.
        /// </summary>
        internal void Disconnect()
        {
            Inner.SessionCreated(default, default);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            Inner.DeleteSubscriptionsOnClose = false;
            Inner.SessionCreated(default, default);
            await Managed.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Routes real generated requests through the established mock server model.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private async ValueTask<IServiceResponse> DispatchAsync(IServiceRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            IServiceResponse response = request switch
            {
                BrowseRequest browse => await m_remote.Session.Object.BrowseAsync(
                    browse.RequestHeader, browse.View, browse.RequestedMaxReferencesPerNode,
                    browse.NodesToBrowse, ct).ConfigureAwait(false),
                BrowseNextRequest next => await m_remote.Session.Object.BrowseNextAsync(
                    next.RequestHeader, next.ReleaseContinuationPoints, next.ContinuationPoints, ct)
                    .ConfigureAwait(false),
                ReadRequest read => await m_remote.Session.Object.ReadAsync(
                    read.RequestHeader, read.MaxAge, read.TimestampsToReturn, read.NodesToRead, ct)
                    .ConfigureAwait(false),
                TranslateBrowsePathsToNodeIdsRequest translate =>
                    await m_remote.Session.Object.TranslateBrowsePathsToNodeIdsAsync(
                        translate.RequestHeader, translate.BrowsePaths, ct).ConfigureAwait(false),
                CallRequest call => await m_remote.Session.Object.CallAsync(
                    call.RequestHeader, call.MethodsToCall, ct).ConfigureAwait(false),
                CloseSessionRequest => new CloseSessionResponse { ResponseHeader = new ResponseHeader() },
                _ => throw new InvalidOperationException("Unexpected service in the GDS transport test seam.")
            };
            ResponseHeader header = response.ResponseHeader ??
                throw new InvalidOperationException("The test response has no header.");
            header.RequestHandle = request.RequestHeader.RequestHandle;
            return response;
        }

        private readonly RemoteModelTestSession m_remote;
        private readonly ApplicationConfiguration m_configuration;
        private readonly ISessionFactory m_factory;
    }
}
#endif
