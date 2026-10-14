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
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Opc.Ua.Tests;

namespace Opc.Ua.Client.Tests.SessionContexts
{
    /// <summary>
    /// Exercises real managed dispatch with a deterministic, caller-scripted transport.
    /// </summary>
    internal sealed class SessionRequestContextTestContext : IAsyncDisposable
    {
        /// <summary>
        /// Creates a connected managed session without opening a socket or starting recovery.
        /// </summary>
        internal SessionRequestContextTestContext(ITelemetryContext? telemetry = null)
        {
            Telemetry = telemetry ?? NUnitTelemetryContext.Create();
            TimeProvider = new FakeTimeProvider();
            MessageContext = ServiceMessageContext.Create(Telemetry);
            MessageContext.NamespaceUris.Append("urn:request-context:original");
            var endpointConfiguration = EndpointConfiguration.Create();
            var endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://localhost:4840",
                SecurityMode = MessageSecurityMode.None,
                SecurityPolicyUri = SecurityPolicies.None,
                Server = new ApplicationDescription { ApplicationUri = "urn:request-context:server" }
            }, endpointConfiguration);
            var configuration = new ApplicationConfiguration(Telemetry)
            {
                ClientConfiguration = new ClientConfiguration(),
                SecurityConfiguration = new SecurityConfiguration()
            };
            Channel = new Mock<ITransportChannel>();
            Channel.SetupGet(channel => channel.MessageContext).Returns(MessageContext);
            Channel.SetupGet(channel => channel.EndpointDescription).Returns(endpoint.Description);
            Channel.SetupGet(channel => channel.EndpointConfiguration).Returns(endpointConfiguration);
            Channel.SetupGet(channel => channel.OperationTimeout).Returns(10_000);
            Channel.Setup(channel => channel.SendRequestAsync(
                    It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()))
                .Returns<IServiceRequest, CancellationToken>(DispatchAsync);
            InnerSession = new Session(
                Channel.Object, configuration, endpoint,
                engineFactory: DefaultSubscriptionEngineFactory.Instance,
                timeProvider: TimeProvider);
            InnerSession.SessionCreated(NodeId.Parse("s=original-session"), NodeId.Parse("s=original-token"));

            var factory = new Mock<ISessionFactory>();
            factory.SetupGet(value => value.Telemetry).Returns(Telemetry);
            ConstructorInfo constructor = typeof(Client.ManagedSession)
                .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
                .Single();
            ManagedSession = (Client.ManagedSession)constructor.Invoke(
            [
                configuration, endpoint, factory.Object, new ReconnectPolicy(), null,
                Telemetry.CreateLogger<Client.ManagedSession>(), null, null, TimeProvider,
                default(ArrayOf<string>), "RequestContextTests", 60_000u,
                false, false, false, false, null, null, null, null
            ]);
            SetField(ManagedSession, typeof(Client.ManagedSession), "m_session", InnerSession);
            SetField(ManagedSession.StateMachine, typeof(ConnectionStateMachine), "m_servicesAvailable", true);
            SetField(ManagedSession.StateMachine, typeof(ConnectionStateMachine), "m_state", ConnectionState.Connected);
            foreach (string name in new[] { "m_servicesReady", "m_settled" })
            {
                var ready = (AsyncManualResetEvent)typeof(ConnectionStateMachine)
                    .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(ManagedSession.StateMachine)!;
                ready.Set();
            }
            ServiceGate = (AsyncReaderWriterLock)typeof(Client.ManagedSession)
                .GetField("m_serviceLock", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(ManagedSession)!;
        }

        /// <summary>
        /// Gets the telemetry used by the real sessions.
        /// </summary>
        internal ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Gets controllable time for the recovery drain.
        /// </summary>
        internal FakeTimeProvider TimeProvider { get; }

        /// <summary>
        /// Gets the original wire message context.
        /// </summary>
        internal IServiceMessageContext MessageContext { get; }

        /// <summary>
        /// Gets the transport whose lifetime remains owned by the managed session.
        /// </summary>
        internal Mock<ITransportChannel> Channel { get; }

        /// <summary>
        /// Gets the real session that can be recreated in place.
        /// </summary>
        internal Session InnerSession { get; }

        /// <summary>
        /// Gets the managed owner passed to the public capture API.
        /// </summary>
        internal Client.ManagedSession ManagedSession { get; }

        /// <summary>
        /// Gets the existing private gate solely for deterministic race tests.
        /// </summary>
        internal AsyncReaderWriterLock ServiceGate { get; }

        /// <summary>
        /// Gets all requests actually delivered to the transport.
        /// </summary>
        internal List<IServiceRequest> Requests { get; } = [];

        /// <summary>
        /// Gets or sets the response script.
        /// </summary>
        internal Func<IServiceRequest, CancellationToken, ValueTask<IServiceResponse>> Handler { get; set; }
            = static (_, _) => new ValueTask<IServiceResponse>(new CloseSessionResponse());

        /// <summary>
        /// Replaces both server identifiers without changing the inner Session reference.
        /// </summary>
        internal void Recreate()
        {
            InnerSession.SessionCreated(NodeId.Parse("s=replacement-session"), NodeId.Parse("s=replacement-token"));
        }

        /// <summary>
        /// Replaces the physical transport without changing public identifiers, as an identity-collision regression.
        /// </summary>
        internal void ReplaceTransport(ITransportChannel channel)
        {
            SetField(InnerSession, typeof(ClientBase), "m_channel", channel);
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            InnerSession.AbandonServerSession();
            await ManagedSession.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Applies a test-only state change without adding a production testing API.
        /// </summary>
        private static void SetField(object target, Type type, string name, object value)
        {
            type.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);
        }

        /// <summary>
        /// Records the request before running the scripted transport operation.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private async ValueTask<IServiceResponse> DispatchAsync(IServiceRequest request, CancellationToken ct)
        {
            Requests.Add(request);
            IServiceResponse response = await Handler(request, ct).ConfigureAwait(false);
            ResponseHeader header = response switch
            {
                CallResponse value => value.ResponseHeader ??= new ResponseHeader(),
                HistoryReadResponse value => value.ResponseHeader ??= new ResponseHeader(),
                ReadResponse value => value.ResponseHeader ??= new ResponseHeader(),
                TranslateBrowsePathsToNodeIdsResponse value => value.ResponseHeader ??= new ResponseHeader(),
                CloseSessionResponse value => value.ResponseHeader ??= new ResponseHeader(),
                _ => response.ResponseHeader
                    ?? throw new InvalidOperationException("The scripted response has no header.")
            };
            header.RequestHandle = request.RequestHeader.RequestHandle;
            return response;
        }
    }
}
