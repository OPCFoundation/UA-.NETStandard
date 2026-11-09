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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Opc.Ua.Client;
using Opc.Ua.Mcp;
using Opc.Ua.Tests;

namespace Opc.Ua.Tools.Tests.McpAuthorization
{
    /// <summary>
    /// Runs the real generic tool, guard, managed session and generated request dispatch over a scripted transport.
    /// </summary>
    internal sealed class McpGenericGenerationTestContext : IAsyncDisposable
    {
        /// <summary>
        /// Creates the physical transport without starting another connection or a server process.
        /// </summary>
        private McpGenericGenerationTestContext()
        {
            Telemetry = NUnitTelemetryContext.Create();
            MessageContext = ServiceMessageContext.Create(Telemetry);
            MessageContext.NamespaceUris.Append(OrdinaryNamespace);
            m_endpoint = new ConfiguredEndpoint(null, new EndpointDescription
            {
                EndpointUrl = "opc.tcp://unused.invalid:4840",
                SecurityMode = MessageSecurityMode.SignAndEncrypt,
                SecurityPolicyUri = SecurityPolicies.Basic256Sha256,
                Server = new ApplicationDescription { ApplicationUri = "urn:generic-generation:original" }
            }, EndpointConfiguration.Create());
            m_configuration = new ApplicationConfiguration(Telemetry)
            {
                ClientConfiguration = new ClientConfiguration(),
                SecurityConfiguration = new SecurityConfiguration()
            };
            var channel = new Mock<ITransportChannel>();
            channel.SetupGet(value => value.MessageContext).Returns(MessageContext);
            channel.SetupGet(value => value.EndpointDescription).Returns(m_endpoint.Description);
            channel.SetupGet(value => value.EndpointConfiguration)
                .Returns(m_endpoint.Configuration ?? EndpointConfiguration.Create());
            channel.SetupGet(value => value.OperationTimeout).Returns(30_000);
            channel.Setup(value => value.SendRequestAsync(
                    It.IsAny<IServiceRequest>(), It.IsAny<CancellationToken>()))
                .Returns<IServiceRequest, CancellationToken>(DispatchAsync);
            Inner = new Session(channel.Object, m_configuration, m_endpoint,
                engineFactory: DefaultSubscriptionEngineFactory.Instance);
            Inner.SessionCreated(OriginalSessionId, OriginalAuthentication);
            var factory = new Mock<ISessionFactory>();
            factory.SetupGet(value => value.Telemetry).Returns(Telemetry);
            factory.SetupGet(value => value.SubscriptionEngineFactory)
                .Returns(DefaultSubscriptionEngineFactory.Instance);
            factory.Setup(value => value.CreateAsync(
                    m_configuration,
                    It.Is<ConfiguredEndpoint>(candidate => candidate.EndpointUrl == m_endpoint.EndpointUrl),
                    false, false, It.IsAny<string>(), It.IsAny<uint>(),
                    It.IsAny<IUserIdentity>(), It.IsAny<ArrayOf<string>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(Inner);
            m_factory = factory.Object;
            var services = new ServiceCollection();
            services.AddOpcUaMcpCore(new OpcUaMcpOptions());
            m_provider = services.BuildServiceProvider();
            Manager = m_provider.GetRequiredService<OpcUaSessionManager>();
        }

        /// <summary>
        /// Gets the fixed host registration name, deliberately distinct from the server-provided name.
        /// </summary>
        internal const string RegistrationName = "Production-A";

        /// <summary>
        /// Gets the namespace classified before recovery.
        /// </summary>
        internal const string OrdinaryNamespace = "urn:generic-generation:ordinary";

        /// <summary>
        /// Gets the protected namespace occupying the same index after recovery.
        /// </summary>
        internal const string ProtectedNamespace = "http://opcfoundation.org/UA/GDS/";

        /// <summary>
        /// Gets the original real server-session identifier.
        /// </summary>
        internal static NodeId OriginalSessionId => NodeId.Parse("s=original-session");

        /// <summary>
        /// Gets the original authentication context observed only by the local test transport.
        /// </summary>
        internal static NodeId OriginalAuthentication => NodeId.Parse("s=original-authentication");

        /// <summary>
        /// Gets telemetry for the real stack objects.
        /// </summary>
        internal ITelemetryContext Telemetry { get; }

        /// <summary>
        /// Gets the live namespace mapping that recovery updates in place.
        /// </summary>
        internal IServiceMessageContext MessageContext { get; }

        /// <summary>
        /// Gets the real Session object whose identity is recreated in place.
        /// </summary>
        internal Session Inner { get; }

        /// <summary>
        /// Gets the genuine managed owner registered with the MCP session manager.
        /// </summary>
        internal ManagedSession Managed { get; private set; } = null!;

        /// <summary>
        /// Gets the real policy-bearing MCP session manager.
        /// </summary>
        internal OpcUaSessionManager Manager { get; }

        /// <summary>
        /// Gets the signal that the guard has requested its last ordinary-model classification page.
        /// </summary>
        internal Task ClassificationReached => m_classificationReached.Task;

        /// <summary>
        /// Gets the signal that a mutation has passed preflight and is waiting at the real managed dispatch gate.
        /// </summary>
        internal Task MutationDispatchWaiting => m_mutationDispatchWaiting.Task;

        /// <summary>
        /// Gets or sets the target whose final ownership browse is suspended by the test.
        /// </summary>
        internal NodeId PauseAfterClassifying { get; set; }

        /// <summary>
        /// Gets the namespace observed when the final classification page was requested.
        /// </summary>
        internal string? ClassifiedNamespace { get; private set; }

        /// <summary>
        /// Gets the mutations that actually reached physical transport dispatch.
        /// </summary>
        internal List<IServiceRequest> Mutations { get; } = [];

        /// <summary>
        /// Gets the reads and browses that actually reached physical transport dispatch.
        /// </summary>
        internal List<IServiceRequest> MetadataRequests { get; } = [];

        /// <summary>
        /// Gets or sets a local hook immediately after a mutation is acknowledged by the scripted server.
        /// </summary>
        internal Action? MutationAcknowledged { get; set; }

        /// <summary>
        /// Establishes managed readiness with the public SDK factory and registers its real host name.
        /// </summary>
        internal static async Task<McpGenericGenerationTestContext> CreateAsync()
        {
            var result = new McpGenericGenerationTestContext();
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                result.Managed = await ManagedSession.CreateAsync(
                    result.m_configuration, result.m_endpoint, result.m_factory,
                    telemetry: result.Telemetry, sessionName: "ServerAssignedName",
                    updateBeforeConnect: false, ct: timeout.Token).ConfigureAwait(false);
                _ = await result.Manager.RegisterExistingSessionAsync(
                    RegistrationName, result.Managed, "UserName", timeout.Token).ConfigureAwait(false);
                return result;
            }
            catch
            {
                await result.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Recreates the inner session and makes the old numeric namespace index security-sensitive.
        /// </summary>
        internal void RecreateAsProtectedModel()
        {
            Inner.SessionCreated(NodeId.Parse("s=replacement-session"), NodeId.Parse("s=replacement-authentication"));
            MessageContext.NamespaceUris.Update([Namespaces.OpcUa, ProtectedNamespace]);
        }

        /// <summary>
        /// Releases the final classification reply after the test has changed the server generation.
        /// </summary>
        internal void ReleaseClassification()
        {
            m_resumeClassification.TrySetResult(true);
        }

        /// <summary>
        /// Excludes new service dispatches while the final already-dispatched browse is still completing.
        /// Test-only reflection reaches the existing gate; no production synchronization API is added.
        /// </summary>
        internal async ValueTask PauseMutationDispatchAsync()
        {
            object serviceGate = typeof(ManagedSession)
                .GetField("m_serviceLock", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(Managed)!;
            Type gateType = serviceGate.GetType();
            var admission = (SemaphoreSlim)gateType
                .GetField("m_writer", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(serviceGate)!;
            await admission.WaitAsync().ConfigureAwait(false);
            try
            {
                PropertyInfo callbackProperty = gateType.GetProperty("BeforeReaderLockAsync")!;
                var previous = (Func<CancellationToken, ValueTask>?)callbackProperty.GetValue(serviceGate);
                Func<CancellationToken, ValueTask> observe = async token =>
                {
                    if (previous != null)
                    {
                        await previous(token).ConfigureAwait(false);
                    }
                    m_mutationDispatchWaiting.TrySetResult(true);
                };
                callbackProperty.SetValue(serviceGate, observe);
                m_restoreDispatchCallback = () => callbackProperty.SetValue(serviceGate, previous);
                m_dispatchAdmission = admission;
            }
            catch
            {
                admission.Release();
                throw;
            }
        }

        /// <summary>
        /// Allows the queued mutation to reach the SDK's dispatch-time identity check.
        /// </summary>
        internal void ReleaseMutationDispatch()
        {
            m_restoreDispatchCallback?.Invoke();
            m_restoreDispatchCallback = null;
            SemaphoreSlim? admission = Interlocked.Exchange(ref m_dispatchAdmission, null);
            admission?.Release();
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            ReleaseClassification();
            ReleaseMutationDispatch();
            await m_provider.DisposeAsync().ConfigureAwait(false);
            Inner.DeleteSubscriptionsOnClose = false;
            Inner.SessionCreated(default, default);
            if (Managed != null)
            {
                await Managed.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                await Inner.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Supplies only ordinary metadata until the test permits the final reply, then observes real mutations.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        private async ValueTask<IServiceResponse> DispatchAsync(IServiceRequest request, CancellationToken ct)
        {
            ResponseHeader header = new() { RequestHandle = request.RequestHeader.RequestHandle };
            if (request is BrowseRequest browse)
            {
                MetadataRequests.Add(request);
                BrowseDescription last = browse.NodesToBrowse[0];
                if (!PauseAfterClassifying.IsNull &&
                    last.NodeId == PauseAfterClassifying &&
                    last.ReferenceTypeId == ReferenceTypeIds.HierarchicalReferences &&
                    last.BrowseDirection == BrowseDirection.Inverse)
                {
                    ClassifiedNamespace = MessageContext.NamespaceUris.GetString(last.NodeId.NamespaceIndex);
                    m_classificationReached.TrySetResult(true);
                    await m_resumeClassification.Task.WaitAsync(ct).ConfigureAwait(false);
                }
                return new BrowseResponse
                {
                    ResponseHeader = header,
                    Results = browse.NodesToBrowse.ConvertAll(static _ =>
                        new BrowseResult { StatusCode = StatusCodes.Good, References = [] })
                };
            }
            if (request is ReadRequest read)
            {
                MetadataRequests.Add(request);
                return new ReadResponse
                {
                    ResponseHeader = header,
                    Results = read.NodesToRead.ConvertAll(static _ => new DataValue(Variant.From(17)))
                };
            }
            if (request is CloseSessionRequest)
            {
                return new CloseSessionResponse { ResponseHeader = header };
            }
            Mutations.Add(request);
            IServiceResponse response = request switch
            {
                CallRequest => new CallResponse
                {
                    ResponseHeader = header,
                    Results =
                    [
                        new CallMethodResult
                        {
                            StatusCode = StatusCodes.GoodCompletesAsynchronously,
                            OutputArguments = [Variant.From(17)],
                            InputArgumentResults = [StatusCodes.Good]
                        }
                    ]
                },
                WriteRequest write => new WriteResponse
                {
                    ResponseHeader = header,
                    Results = write.NodesToWrite.ConvertAll(static _ => StatusCodes.GoodClamped)
                },
                HistoryUpdateRequest => new HistoryUpdateResponse
                {
                    ResponseHeader = header,
                    Results =
                    [
                        new HistoryUpdateResult
                        {
                            StatusCode = StatusCodes.Good,
                            OperationResults = [StatusCodes.BadNoEntryExists]
                        }
                    ]
                },
                AddNodesRequest => new AddNodesResponse
                {
                    ResponseHeader = header,
                    Results = [new AddNodesResult { StatusCode = StatusCodes.Good, AddedNodeId = new NodeId(201u, 1) }]
                },
                DeleteNodesRequest delete => new DeleteNodesResponse
                {
                    ResponseHeader = header,
                    Results = delete.NodesToDelete.ConvertAll(static _ => StatusCodes.Good)
                },
                AddReferencesRequest add => new AddReferencesResponse
                {
                    ResponseHeader = header,
                    Results = add.ReferencesToAdd.ConvertAll(static _ => StatusCodes.Good)
                },
                DeleteReferencesRequest delete => new DeleteReferencesResponse
                {
                    ResponseHeader = header,
                    Results = delete.ReferencesToDelete.ConvertAll(static _ => StatusCodes.Good)
                },
                _ => throw new InvalidOperationException("Unexpected scripted service request.")
            };
            MutationAcknowledged?.Invoke();
            return response;
        }

        private readonly ApplicationConfiguration m_configuration;
        private readonly ConfiguredEndpoint m_endpoint;
        private readonly ISessionFactory m_factory;
        private readonly ServiceProvider m_provider;

        private readonly TaskCompletionSource<bool> m_classificationReached =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource<bool> m_resumeClassification =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly TaskCompletionSource<bool> m_mutationDispatchWaiting =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private Action? m_restoreDispatchCallback;
        private SemaphoreSlim? m_dispatchAdmission;
    }
}
#endif
