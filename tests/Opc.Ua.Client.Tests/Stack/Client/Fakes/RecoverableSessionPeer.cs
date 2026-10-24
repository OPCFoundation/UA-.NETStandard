/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to
 * deal in the Software without restriction, including without limitation the
 * rights to use, copy, modify, merge, publish, distribute, sublicense, and/or
 * sell copies of the Software, and to permit persons to whom the Software is
 * furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included in
 * all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
 * IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
 * AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
 * LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
 * OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
 * THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Opc.Ua.Client.Tests.Stack.Client.Fakes
{
    /// <summary>
    /// Adds synthetic capability and retained-result exchanges to the existing real-session harness.
    /// This is a wire script, not a replacement for the production recovery coordinator.
    /// </summary>
    internal sealed class RecoverableSessionPeer : IAsyncDisposable
    {
        private RecoverableSessionPeer(ushort namespaceIndex, Action<ScriptedChannel>? configureChannel)
        {
            string[] namespaces = new string[namespaceIndex + 1];
            namespaces[0] = Namespaces.OpcUa;
            for (int i = 1; i < namespaceIndex; i++)
            {
                namespaces[i] = $"urn:synthetic:namespace:{i}";
            }
            namespaces[namespaceIndex] = RecoverableManagerClient.NamespaceUri;
            m_namespaces = namespaces;
            RseNamespaceIndex = namespaceIndex;
            Channels = new SessionChannelHarness(configureChannel: channel =>
            {
                channel.SupportedFeatures = TransportChannelFeatures.Reconnect;
                channel.Channel.MessageContext.NamespaceUris.Update(m_namespaces.ToList());
                channel.Channel.MessageContext.ServerUris.Update(["urn:localhost:server"]);
                channel.RequestHandler = (request, ct) => DispatchAsync(channel, request, ct);
                configureChannel?.Invoke(channel);
            });
        }

        public SessionChannelHarness Channels { get; }
        public Ua.Client.ManagedSession Owner { get; private set; } = null!;
        public ISession Session => Owner;
        public CancellationToken Timeout => m_timeout.Token;
        public bool CapabilityAvailable { get; set; } = true;
        public string ContractVersion { get; set; } = "0.4.0";
        public ushort RseNamespaceIndex { get; }
        public NodeId ManagerId { get; } = new("actual-manager", 1);
        public NodeId CapabilitiesId { get; } = new("actual-capabilities", 1);

        public ArrayOf<NodeId> MethodIds { get; } =
        [
            new("actual-submit", 1), new("actual-get", 1), new("actual-ack", 1),
            new("actual-cancel", 1), new("actual-abandon", 1)
        ];

        public ArrayOf<NodeId> SupportedServices { get; set; } =
        [
            DataTypeIds.ReadRequest, DataTypeIds.WriteRequest, DataTypeIds.CallRequest,
            DataTypeIds.HistoryReadRequest, DataTypeIds.HistoryUpdateRequest, DataTypeIds.BrowseRequest,
            DataTypeIds.BrowseNextRequest, DataTypeIds.TranslateBrowsePathsToNodeIdsRequest,
            DataTypeIds.RegisterNodesRequest, DataTypeIds.UnregisterNodesRequest,
            DataTypeIds.AddNodesRequest, DataTypeIds.AddReferencesRequest,
            DataTypeIds.DeleteNodesRequest, DataTypeIds.DeleteReferencesRequest
        ];

        public ArrayOf<int> SupportedRecoveryModes { get; set; } =
            [(int)ServiceOperationRecoveryMode.Session];

        public DataValue ExecutionDefault { get; set; } = new(Variant.From(12_345.5));
        public DataValue RetentionDefault { get; set; } = new(Variant.From(6789.25));
        public bool FastResult { get; set; } = true;
        public StatusCode SubmitFault { get; set; }
        public StatusCode CollectionFault { get; set; }
        public StatusCode ControlFault { get; set; }
        public StatusCode OriginalFailure { get; set; }
        public StatusCode MethodStatus { get; set; }
        public bool LoseSubmitReply { get; set; }
        public StatusCode LostSubmitStatus { get; set; } = StatusCodes.BadConnectionClosed;
        public bool LoseCollectionReply { get; set; }
        public bool LoseAckReply { get; set; }
        public bool UnknownCollection { get; set; }
        public bool InvalidMethodOutput { get; set; }
        public bool BinaryRoundTripResponses { get; set; }
        public Func<ReadValueId, DataValue>? ReadValueFactory { get; set; }
        public Func<BrowseDescription, ArrayOf<ReferenceDescription>>? BrowseReferenceFactory { get; set; }
        public Func<IEncodeable, CancellationToken, ValueTask>? BeforeRespondAsync { get; set; }
        public Action<IEncodeable, IEncodeable>? MutateResponse { get; set; }
        public Action<CallRequest, CallResponse>? MutateManagerResponse { get; set; }
        public bool NullManagerResponse { get; set; }
        public Func<bool>? CallerHasCompleted { get; set; }
        public bool AckObservedCallerCompletion { get; private set; }
        public ConcurrentQueue<SubmitRequestDataType> Submissions { get; } = new();
        public ConcurrentQueue<GetResultRequestDataType> Collections { get; } = new();
        public ConcurrentQueue<(string MethodName, OperationControlRequestDataType Request)> Controls { get; } = new();
        public ConcurrentQueue<CallRequest> ManagerCalls { get; } = new();
        public ConcurrentQueue<bool> AckCallerCompletions { get; } = new();
        public ConcurrentQueue<IServiceRequest> OrdinaryRequests { get; } = new();
        public ConcurrentQueue<IServiceRequest> DiscoveryRequests { get; } = new();
        public ConcurrentQueue<IServiceResponse> DeliveredResponses { get; } = new();
        public ConcurrentQueue<IEncodeable> DeliveredEnvelopes { get; } = new();
        public TaskCompletionSource<bool> AckEntered { get; } = NewSignal();

        public static TaskCompletionSource<bool> NewSignal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public static async Task<RecoverableSessionPeer> CreateAsync(
            RecoverableSessionOptions? options = null,
            bool enabled = true,
            string construction = "options",
            Action<ConfiguredEndpoint>? configureEndpoint = null,
            ushort namespaceIndex = 4,
            Action<ScriptedChannel>? configureChannel = null)
        {
            var peer = new RecoverableSessionPeer(namespaceIndex, configureChannel);
            try
            {
                var sessionOptions = new ManagedSessionOptions
                {
                    Endpoint = SessionChannelHarness.CreateEndpoint(),
                    RecoverableExecution = enabled ? options ?? new RecoverableSessionOptions() : null
                };
                configureEndpoint?.Invoke(sessionOptions.Endpoint);
                if (construction == "builder")
                {
                    ManagedSessionBuilder builder = new ManagedSessionBuilder(peer.Channels.Configuration, peer.Channels.Telemetry)
                        .UseEndpoint(sessionOptions.Endpoint)
                        .WithChannelManager(peer.Channels.Manager);
                    if (enabled)
                    {
                        builder.PreferRecoverableExecution(options);
                    }
                    peer.Owner = await builder.ConnectAsync(peer.Timeout).ConfigureAwait(false);
                }
                else if (construction == "di")
                {
                    var services = new ServiceCollection();
                    services.AddSingleton(peer.Channels.Telemetry);
                    services.AddSingleton<IClientChannelManager>(peer.Channels.Manager);
                    services.AddOpcUa().AddClient(client =>
                    {
                        client.Configuration = peer.Channels.Configuration;
                        client.Session = sessionOptions;
                    });
                    peer.m_provider = services.BuildServiceProvider();
                    peer.Owner = await peer.m_provider.GetRequiredService<IManagedSessionFactory>()
                        .ConnectAsync(sessionOptions.Endpoint, peer.Timeout).ConfigureAwait(false);
                }
                else
                {
                    peer.Owner = await Ua.Client.ManagedSession.CreateAsync(
                        sessionOptions,
                        peer.Channels.Configuration,
                        new DefaultSessionFactory(peer.Channels.Telemetry),
                        telemetry: peer.Channels.Telemetry,
                        channelManager: peer.Channels.Manager,
                        updateBeforeConnect: false,
                        ct: peer.Timeout).ConfigureAwait(false);
                }
                return peer;
            }
            catch
            {
                await peer.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        public ValueTask RecoverAsync()
        {
            return Channels.Manager.ReconnectAsync((IManagedTransportChannel)Owner.TransportChannel, Timeout);
        }

        public async ValueTask DisposeAsync()
        {
            if (Owner != null)
            {
                await Owner.DisposeAsync().ConfigureAwait(false);
            }
            if (m_provider != null)
            {
                await m_provider.DisposeAsync().ConfigureAwait(false);
            }
            await Channels.DisposeAsync().ConfigureAwait(false);
            m_timeout.Dispose();
        }

        public static IServiceRequest Original(SubmitRequestDataType submit)
        {
            Assert.That(submit.Request.TryGetValue(out IServiceRequest? original), Is.True);
            return original!;
        }

        public static ServiceOperationInfoDataType UnknownInfo(Uuid clientId)
        {
            return new ServiceOperationInfoDataType
            {
                ClientOperationId = clientId,
                State = ServiceOperationState.Unknown,
                ServerToken = default,
                ServiceId = NodeId.Null,
                OutcomeKind = ServiceOperationOutcome.None,
                OutcomeStatus = StatusCodes.Good,
                CancellationRequested = false,
                ExecutionAllowance = 0,
                ResultRetentionTime = 0,
                ExecutionRemaining = 0,
                ResultRemaining = 0,
                HistoryRemaining = 0,
                RetryAfter = 0
            };
        }

        private async ValueTask<IServiceResponse> DispatchAsync(
            ScriptedChannel channel, IServiceRequest request, CancellationToken ct)
        {
            bool managerCall = request is CallRequest managerRequest &&
                managerRequest.MethodsToCall.Count == 1 &&
                managerRequest.MethodsToCall[0].ObjectId == ManagerId;
            if (!managerCall && BeforeRespondAsync != null)
            {
                await BeforeRespondAsync(request, ct).ConfigureAwait(false);
            }
            IServiceResponse response;
            switch (request)
            {
                case ReadRequest { NodesToRead.Count: 2 } read when
                    read.NodesToRead[0].NodeId == VariableIds.Server_NamespaceArray:
                    response = new ReadResponse
                    {
                        Results =
                        [
                            new DataValue(Variant.From(m_namespaces)),
                            new DataValue(Variant.From(ArrayOf.Wrapped("urn:localhost:server")))
                        ]
                    };
                    break;
                case TranslateBrowsePathsToNodeIdsRequest translate when
                    translate.BrowsePaths[0].StartingNode == ObjectIds.Server:
                    DiscoveryRequests.Enqueue(CoreUtils.Clone(request)!);
                    Assert.That(translate.BrowsePaths.Count, Is.EqualTo(1));
                    Assert.That(translate.BrowsePaths[0].RelativePath.Elements[0].TargetName,
                        Is.EqualTo(new QualifiedName("RecoverableExecutionManager", RseNamespaceIndex)));
                    response = new TranslateBrowsePathsToNodeIdsResponse
                    {
                        Results = CapabilityAvailable
                            ? [Target(ManagerId)] : [new BrowsePathResult { StatusCode = StatusCodes.BadNoMatch }]
                    };
                    break;
                case TranslateBrowsePathsToNodeIdsRequest translate when
                    translate.BrowsePaths[0].StartingNode == ManagerId:
                    DiscoveryRequests.Enqueue(CoreUtils.Clone(request)!);
                    Assert.That(translate.BrowsePaths.ToList().Select(path =>
                        path.RelativePath.Elements[0].TargetName.Name), Is.EqualTo(s_memberNames));
                    Assert.That(translate.BrowsePaths.ToList().Select(path =>
                        path.RelativePath.Elements[0].TargetName.NamespaceIndex), Is.All.EqualTo(RseNamespaceIndex));
                    response = new TranslateBrowsePathsToNodeIdsResponse
                    {
                        Results = new[] { CapabilitiesId }.Concat(MethodIds.ToList()).Select(Target).ToArray()
                    };
                    break;
                case TranslateBrowsePathsToNodeIdsRequest translate when
                    translate.BrowsePaths[0].StartingNode == CapabilitiesId:
                    DiscoveryRequests.Enqueue(CoreUtils.Clone(request)!);
                    Assert.That(translate.BrowsePaths.ToList().Select(path =>
                        path.RelativePath.Elements[0].TargetName.Name), Is.EqualTo(s_propertyNames));
                    response = new TranslateBrowsePathsToNodeIdsResponse
                    {
                        Results = s_propertyIds.Select(Target).ToArray()
                    };
                    break;
                case ReadRequest read when read.NodesToRead.ToList().Any(node => s_propertyIds.Contains(node.NodeId)):
                    DiscoveryRequests.Enqueue(CoreUtils.Clone(request)!);
                    Assert.That(read.NodesToRead.ToList().Select(node => node.NodeId), Is.EqualTo(s_propertyIds));
                    Assert.That(read.NodesToRead.ToList().Select(node => node.AttributeId), Is.All.EqualTo(Attributes.Value));
                    response = new ReadResponse
                    {
                        Results =
                        [
                            new DataValue(Variant.From(ContractVersion)),
                            new DataValue(Variant.From(SupportedServices)),
                            ExecutionDefault,
                            RetentionDefault,
                            new DataValue(Variant.From(SupportedRecoveryModes))
                        ]
                    };
                    break;
                case CallRequest call when managerCall:
                    response = await DispatchManagerAsync(channel, call, ct).ConfigureAwait(false);
                    break;
                case ReadRequest read when read.NodesToRead.ToList().All(node =>
                    node.NodeId == new NodeId(100u) || node.NodeId == new NodeId(101u)):
                case WriteRequest:
                case CallRequest:
                case BrowseRequest:
                case BrowseNextRequest:
                case TranslateBrowsePathsToNodeIdsRequest:
                case HistoryReadRequest:
                case HistoryUpdateRequest:
                case RegisterNodesRequest:
                case UnregisterNodesRequest:
                case QueryFirstRequest:
                case QueryNextRequest:
                case AddNodesRequest:
                case AddReferencesRequest:
                case DeleteNodesRequest:
                case DeleteReferencesRequest:
                    OrdinaryRequests.Enqueue(CoreUtils.Clone(request)!);
                    response = CreateOriginalResponse(request);
                    break;
                default:
                    return channel.CreateResponse(request);
            }
            if (!managerCall)
            {
                response.ResponseHeader.RequestHandle = request.RequestHeader.RequestHandle;
                MutateResponse?.Invoke(request, response);
            }
            if (managerCall && NullManagerResponse)
            {
                return null!;
            }
            if (BinaryRoundTripResponses)
            {
                IServiceMessageContext context = channel.Channel.MessageContext;
                response = BinaryDecoder.DecodeMessage<IServiceResponse>(
                    BinaryEncoder.EncodeMessage(response, context), context);
                DeliveredResponses.Enqueue(response);
                if (managerCall &&
                    response is CallResponse { Results.Count: 1 } delivered &&
                    delivered.Results[0].OutputArguments.Count == 1 &&
                    delivered.Results[0].OutputArguments[0].TryGetValue(out ExtensionObject output) &&
                    output.TryGetValue(out IEncodeable? envelope, context))
                {
                    DeliveredEnvelopes.Enqueue(envelope);
                }
            }
            return response;
        }

        private async ValueTask<CallResponse> DispatchManagerAsync(
            ScriptedChannel channel, CallRequest call, CancellationToken ct)
        {
            ManagerCalls.Enqueue(CoreUtils.Clone(call)!);
            CallMethodRequest invocation = call.MethodsToCall[0];
            Assert.That(invocation.InputArguments.Count, Is.EqualTo(1));
            Assert.That(invocation.InputArguments[0].TryGetValue(out ExtensionObject input), Is.True);
            Assert.That(input.TryGetValue(out IEncodeable? argument, channel.Channel.MessageContext), Is.True);
            if (BeforeRespondAsync != null)
            {
                await BeforeRespondAsync(argument!, ct).ConfigureAwait(false);
            }
            IEncodeable? output;
            StatusCode status = StatusCodes.Good;
            switch (argument)
            {
                case SubmitRequestDataType submit:
                    Assert.That(invocation.MethodId, Is.EqualTo(MethodIds[0]));
                    Submissions.Enqueue(CoreUtils.Clone(submit)!);
                    if (StatusCode.IsBad(SubmitFault))
                    {
                        status = SubmitFault;
                        output = null;
                        break;
                    }
                    IServiceRequest original = Original(submit);
                    var token = ByteString.From(Encoding.UTF8.GetBytes(submit.ClientOperationId.ToString()));
                    var retained = new Retained(CoreUtils.Clone(submit)!, token, CreateOriginalResponse(original),
                        ExpandedNodeId.ToNodeId(original.TypeId, channel.Channel.MessageContext.NamespaceUris));
                    Assert.That(m_operations.TryAdd(submit.ClientOperationId, retained), Is.True,
                        "The original operation must never be submitted twice.");
                    if (LoseSubmitReply)
                    {
                        LoseSubmitReply = false;
                        throw new ServiceResultException(LostSubmitStatus);
                    }
                    output = new SubmitResponseDataType
                    {
                        Operation = Info(retained, FastResult
                            ? ServiceOperationState.OutcomeAvailable : ServiceOperationState.Pending),
                        Result = FastResult ? new ExtensionObject(CoreUtils.Clone(retained.Response)!) : default
                    };
                    break;
                case GetResultRequestDataType get:
                    Assert.That(invocation.MethodId, Is.EqualTo(MethodIds[1]));
                    Collections.Enqueue(CoreUtils.Clone(get)!);
                    if (StatusCode.IsBad(CollectionFault))
                    {
                        status = CollectionFault;
                        output = null;
                        break;
                    }
                    Retained collected = Find(get.ClientOperationId, get.ServerToken);
                    Assert.That(get.RecoveryMode, Is.EqualTo(collected.Submission.RecoveryMode));
                    if (LoseCollectionReply)
                    {
                        LoseCollectionReply = false;
                        throw new ServiceResultException(StatusCodes.BadConnectionClosed);
                    }
                    ServiceOperationState state = collected.Acknowledged != 0
                        ? ServiceOperationState.Acknowledged : collected.Abandoned != 0
                            ? ServiceOperationState.Abandoned : ServiceOperationState.OutcomeAvailable;
                    output = new GetResultResponseDataType
                    {
                        Operation = UnknownCollection ? UnknownInfo(get.ClientOperationId) : Info(collected, state),
                        Result = !UnknownCollection && state == ServiceOperationState.OutcomeAvailable
                            ? new ExtensionObject(CoreUtils.Clone(collected.Response)!) : default
                    };
                    break;
                case OperationControlRequestDataType control:
                    string method = invocation.MethodId == MethodIds[2] ? "Acknowledge" :
                        invocation.MethodId == MethodIds[3] ? "Cancel" :
                        invocation.MethodId == MethodIds[4] ? "Abandon" :
                        throw new InvalidOperationException("An actual control Method instance is required.");
                    Controls.Enqueue((method, CoreUtils.Clone(control)!));
                    Retained controlled = Find(Uuid.Empty, control.ServerToken);
                    if (StatusCode.IsBad(ControlFault))
                    {
                        status = ControlFault;
                        output = null;
                        break;
                    }
                    bool already = false;
                    bool applicable = true;
                    switch (method)
                    {
                        case "Acknowledge" when controlled.Abandoned != 0:
                        case "Abandon" when controlled.Acknowledged != 0:
                            applicable = false;
                            break;
                        case "Acknowledge":
                            already = Interlocked.Exchange(ref controlled.Acknowledged, 1) != 0;
                            break;
                        case "Abandon":
                            already = Interlocked.Exchange(ref controlled.Abandoned, 1) != 0;
                            break;
                        case "Cancel":
                            already = Interlocked.Exchange(ref controlled.CancellationRequested, 1) != 0;
                            break;
                    }
                    if (method == "Acknowledge")
                    {
                        AckObservedCallerCompletion = CallerHasCompleted?.Invoke() ?? true;
                        AckCallerCompletions.Enqueue(AckObservedCallerCompletion);
                        AckEntered.TrySetResult(true);
                        if (LoseAckReply)
                        {
                            LoseAckReply = false;
                            throw new ServiceResultException(StatusCodes.BadConnectionClosed);
                        }
                    }
                    output = new ControlResponseDataType
                    {
                        Result = !applicable ? ServiceOperationControlResult.NotApplicable : already
                            ? ServiceOperationControlResult.AlreadyApplied : ServiceOperationControlResult.Applied,
                        Operation = Info(controlled, method switch
                        {
                            "Acknowledge" => ServiceOperationState.Acknowledged,
                            "Abandon" => ServiceOperationState.Abandoned,
                            _ => ServiceOperationState.OutcomeAvailable
                        })
                    };
                    break;
                default:
                    throw new InvalidOperationException("Unexpected manager argument.");
            }
            if (output != null)
            {
                MutateResponse?.Invoke(argument!, output);
            }
            var response = new CallResponse
            {
                ResponseHeader = new ResponseHeader { RequestHandle = call.RequestHeader.RequestHandle },
                Results =
                [
                    new CallMethodResult
                    {
                        StatusCode = status,
                        OutputArguments = output == null ? default : [Variant.From(new ExtensionObject(output))]
                    }
                ]
            };
            MutateManagerResponse?.Invoke(call, response);
            return response;
        }

        private static BrowsePathResult Target(NodeId id)
        {
            return new BrowsePathResult
            {
                Targets =
                [
                    new BrowsePathTarget { TargetId = new ExpandedNodeId(id), RemainingPathIndex = uint.MaxValue }
                ]
            };
        }

        private IServiceResponse CreateOriginalResponse(IServiceRequest request)
        {
            if (StatusCode.IsBad(OriginalFailure))
            {
                return Fault(OriginalFailure, request.RequestHeader.RequestHandle);
            }
            var header = new ResponseHeader
            {
                RequestHandle = request.RequestHeader.RequestHandle,
                Timestamp = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc),
                StringTable = ["synthetic-original"]
            };
            return request switch
            {
                ReadRequest read => new ReadResponse
                {
                    ResponseHeader = header,
                    Results = read.NodesToRead.ToList().Select(node => ReadValueFactory?.Invoke(node) ??
                        new DataValue(Variant.From(node.NodeId == new NodeId(100u) ? 42 : 43))).ToArray()
                },
                WriteRequest write => new WriteResponse
                {
                    ResponseHeader = header,
                    Results = write.NodesToWrite.ToList().Select((_, index) =>
                        index == 0 ? StatusCodes.Good : StatusCodes.BadOutOfRange).ToArray()
                },
                CallRequest call => new CallResponse
                {
                    ResponseHeader = header,
                    Results = call.MethodsToCall.ToList().Select(_ => new CallMethodResult
                    {
                        StatusCode = MethodStatus,
                        OutputArguments = InvalidMethodOutput
                            ? [Variant.From(42)] : [Variant.From(ByteString.From("synthetic-csr"u8))]
                    }).ToArray()
                },
                BrowseRequest browse => new BrowseResponse
                {
                    ResponseHeader = header,
                    Results = browse.NodesToBrowse.ToList().Select(description => new BrowseResult
                    {
                        References = BrowseReferenceFactory?.Invoke(description) ??
                            [new ReferenceDescription
                                {
                                    NodeId = new ExpandedNodeId(101u), BrowseName = new QualifiedName("SyntheticChild")
                                }]
                    }).ToArray()
                },
                HistoryReadRequest history => new HistoryReadResponse
                {
                    ResponseHeader = header,
                    Results = history.NodesToRead.ToList().Select(_ => new HistoryReadResult()).ToArray()
                },
                HistoryUpdateRequest history => new HistoryUpdateResponse
                {
                    ResponseHeader = header,
                    Results = history.HistoryUpdateDetails.ToList().Select(_ => new HistoryUpdateResult()).ToArray()
                },
                BrowseNextRequest browse => new BrowseNextResponse
                {
                    ResponseHeader = header,
                    Results = browse.ReleaseContinuationPoints
                        ? [] : browse.ContinuationPoints.ToList().Select(_ => new BrowseResult()).ToArray()
                },
                TranslateBrowsePathsToNodeIdsRequest translate => new TranslateBrowsePathsToNodeIdsResponse
                {
                    ResponseHeader = header,
                    Results = translate.BrowsePaths.ToList().Select(_ => Target(new NodeId(100u))).ToArray()
                },
                RegisterNodesRequest register => new RegisterNodesResponse
                {
                    ResponseHeader = header,
                    RegisteredNodeIds = register.NodesToRegister
                },
                UnregisterNodesRequest => new UnregisterNodesResponse { ResponseHeader = header },
                QueryFirstRequest or QueryNextRequest => Fault(StatusCodes.BadServiceUnsupported, header.RequestHandle),
                AddNodesRequest add => new AddNodesResponse
                {
                    ResponseHeader = header,
                    Results = add.NodesToAdd.ToList().Select(_ => new AddNodesResult { AddedNodeId = new NodeId(100u) })
                        .ToArray()
                },
                AddReferencesRequest add => new AddReferencesResponse
                {
                    ResponseHeader = header,
                    Results = add.ReferencesToAdd.ToList().Select(_ => StatusCodes.Good).ToArray()
                },
                DeleteNodesRequest delete => new DeleteNodesResponse
                {
                    ResponseHeader = header,
                    Results = delete.NodesToDelete.ToList().Select(_ => StatusCodes.Good).ToArray()
                },
                DeleteReferencesRequest delete => new DeleteReferencesResponse
                {
                    ResponseHeader = header,
                    Results = delete.ReferencesToDelete.ToList().Select(_ => StatusCodes.Good).ToArray()
                },
                _ => throw new InvalidOperationException("Unexpected original service.")
            };
        }

        private Retained Find(Uuid clientId, ByteString token)
        {
            if (clientId != Uuid.Empty)
            {
                Assert.That(token.IsNull, Is.True);
                return m_operations[clientId];
            }
            return m_operations.Values.Single(item => item.Token == token);
        }

        private static ServiceFault Fault(StatusCode status, uint handle)
        {
            return new ServiceFault
            {
                ResponseHeader = new ResponseHeader { ServiceResult = status, RequestHandle = handle }
            };
        }

        private static ServiceOperationInfoDataType Info(Retained retained, ServiceOperationState state)
        {
            if (retained.Acknowledged != 0)
            {
                state = ServiceOperationState.Acknowledged;
            }
            else if (retained.Abandoned != 0)
            {
                state = ServiceOperationState.Abandoned;
            }
            return new ServiceOperationInfoDataType
            {
                ClientOperationId = retained.Submission.ClientOperationId,
                ServerToken = retained.Token,
                ServiceId = retained.ServiceId,
                RecoveryMode = retained.Submission.RecoveryMode,
                CancellationRequested = retained.CancellationRequested != 0,
                State = state,
                OutcomeKind = state == ServiceOperationState.Pending
                    ? ServiceOperationOutcome.None : ServiceOperationOutcome.ServiceResponse,
                OutcomeStatus = state == ServiceOperationState.Pending
                    ? StatusCodes.Good : retained.Response.ResponseHeader.ServiceResult,
                ExecutionAllowance = retained.Submission.ExecutionAllowance,
                ResultRetentionTime = retained.Submission.ResultRetentionTime,
                ExecutionRemaining = state == ServiceOperationState.Pending
                    ? retained.Submission.ExecutionAllowance / 2 : 0,
                ResultRemaining = state == ServiceOperationState.OutcomeAvailable
                    ? retained.Submission.ResultRetentionTime / 2 : 0,
                HistoryRemaining = state is ServiceOperationState.Acknowledged or ServiceOperationState.Abandoned
                    ? retained.Submission.ResultRetentionTime / 2 : 0
            };
        }

        private sealed record Retained(
            SubmitRequestDataType Submission, ByteString Token, IServiceResponse Response, NodeId ServiceId)
        {
            public int Acknowledged;
            public int Abandoned;
            public int CancellationRequested;
        }

        private readonly ArrayOf<string> m_namespaces;

        private static readonly string[] s_memberNames =
            ["Capabilities", "Submit", "GetResult", "Acknowledge", "Cancel", "Abandon"];

        private static readonly string[] s_propertyNames =
            ["ContractVersion", "SupportedServices", "DefaultExecutionAllowance", "DefaultResultRetentionTime",
                "SupportedRecoveryModes"];

        private static readonly NodeId[] s_propertyIds =
            [new("cap-contract", 0), new(93001u), new("cap-execution", 0), new(77123u), new("cap-modes", 0)];

        private readonly ConcurrentDictionary<Uuid, Retained> m_operations = new();
        private readonly CancellationTokenSource m_timeout = new(TimeSpan.FromSeconds(15));
        private ServiceProvider? m_provider;
    }
}
