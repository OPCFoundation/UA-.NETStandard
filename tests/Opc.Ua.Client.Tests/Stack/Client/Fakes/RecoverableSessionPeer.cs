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
        private RecoverableSessionPeer()
        {
            Channels = new SessionChannelHarness(configureChannel: channel =>
            {
                channel.SupportedFeatures = TransportChannelFeatures.Reconnect;
                channel.Channel.MessageContext.ServerUris.Update(["urn:localhost:server"]);
                channel.RequestHandler = (request, ct) => DispatchAsync(channel, request, ct);
            });
        }

        public SessionChannelHarness Channels { get; }
        public Ua.Client.ManagedSession Owner { get; private set; } = null!;
        public ISession Session => Owner;
        public CancellationToken Timeout => m_timeout.Token;
        public bool CapabilityAvailable { get; set; } = true;
        public string ContractVersion { get; set; } = "0.2.0";

        public ArrayOf<NodeId> SupportedServices { get; set; } =
            [DataTypeIds.ReadRequest, DataTypeIds.WriteRequest, DataTypeIds.CallRequest];

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
        public Func<IServiceRequest, CancellationToken, ValueTask>? BeforeRespondAsync { get; set; }
        public Action<IServiceRequest, IServiceResponse>? MutateResponse { get; set; }
        public Func<bool>? CallerHasCompleted { get; set; }
        public bool AckObservedCallerCompletion { get; private set; }
        public ConcurrentQueue<SubmitServiceRequest> Submissions { get; } = new();
        public ConcurrentQueue<GetServiceResultRequest> Collections { get; } = new();
        public ConcurrentQueue<ControlServiceExecutionRequest> Controls { get; } = new();
        public ConcurrentQueue<bool> AckCallerCompletions { get; } = new();
        public ConcurrentQueue<IServiceRequest> OrdinaryRequests { get; } = new();
        public ConcurrentQueue<IServiceRequest> DiscoveryRequests { get; } = new();
        public ConcurrentQueue<IServiceResponse> DeliveredResponses { get; } = new();
        public TaskCompletionSource<bool> AckEntered { get; } = NewSignal();

        public static TaskCompletionSource<bool> NewSignal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        public static async Task<RecoverableSessionPeer> CreateAsync(
            RecoverableSessionOptions? options = null,
            bool enabled = true,
            string construction = "options",
            Action<ConfiguredEndpoint>? configureEndpoint = null)
        {
            var peer = new RecoverableSessionPeer();
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

        public static IServiceRequest Original(SubmitServiceRequest submit)
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
            if (BeforeRespondAsync != null)
            {
                await BeforeRespondAsync(request, ct).ConfigureAwait(false);
            }
            IServiceResponse response;
            switch (request)
            {
                case ReadRequest { NodesToRead.Count: 1 } read when read.NodesToRead[0].NodeId == s_root:
                    DiscoveryRequests.Enqueue(CoreUtils.Clone(request)!);
                    Assert.That(read.NodesToRead[0].AttributeId, Is.EqualTo(Attributes.NodeClass));
                    response = new ReadResponse
                    {
                        Results = [new DataValue(Variant.From((int)NodeClass.Object),
                            CapabilityAvailable ? StatusCodes.Good : StatusCodes.BadNodeIdUnknown)]
                    };
                    break;
                case TranslateBrowsePathsToNodeIdsRequest translate:
                    DiscoveryRequests.Enqueue(CoreUtils.Clone(request)!);
                    Assert.That(translate.BrowsePaths.ToList().Select(path => path.StartingNode),
                        Is.All.EqualTo(s_root));
                    Assert.That(translate.BrowsePaths.ToList().Select(path =>
                        path.RelativePath.Elements[0].TargetName.Name), Is.EqualTo(s_propertyNames));
                    response = new TranslateBrowsePathsToNodeIdsResponse
                    {
                        Results = s_propertyIds.Select(id => new BrowsePathResult
                        {
                            Targets = [new BrowsePathTarget
                            {
                                TargetId = new ExpandedNodeId(id), RemainingPathIndex = uint.MaxValue
                            }]
                        }).ToArray()
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
                            RetentionDefault
                        ]
                    };
                    break;
                case SubmitServiceRequest submit:
                    Submissions.Enqueue(CoreUtils.Clone(submit)!);
                    if (StatusCode.IsBad(SubmitFault))
                    {
                        response = Fault(SubmitFault, request.RequestHeader.RequestHandle);
                        break;
                    }
                    IServiceRequest original = Original(submit);
                    var token = ByteString.From(Encoding.UTF8.GetBytes(submit.ClientOperationId.ToString()));
                    var retained = new Retained(
                        CoreUtils.Clone(submit)!, token, CreateOriginalResponse(original),
                        ExpandedNodeId.ToNodeId(original.TypeId, channel.Channel.MessageContext.NamespaceUris));
                    Assert.That(m_operations.TryAdd(submit.ClientOperationId, retained), Is.True,
                        "The original operation must never be submitted twice.");
                    if (LoseSubmitReply)
                    {
                        LoseSubmitReply = false;
                        throw new ServiceResultException(LostSubmitStatus);
                    }
                    response = new SubmitServiceResponse
                    {
                        Operation = Info(retained, FastResult
                            ? ServiceOperationState.OutcomeAvailable : ServiceOperationState.Pending),
                        Result = FastResult ? new ExtensionObject(CoreUtils.Clone(retained.Response)!) : default
                    };
                    break;
                case GetServiceResultRequest get:
                    Collections.Enqueue(CoreUtils.Clone(get)!);
                    if (StatusCode.IsBad(CollectionFault))
                    {
                        response = Fault(CollectionFault, request.RequestHeader.RequestHandle);
                        break;
                    }
                    Retained collected = Find(get.ClientOperationId, get.ServerToken);
                    if (LoseCollectionReply)
                    {
                        LoseCollectionReply = false;
                        throw new ServiceResultException(StatusCodes.BadConnectionClosed);
                    }
                    response = UnknownCollection
                        ? new GetServiceResultResponse
                        {
                            Operation = UnknownInfo(get.ClientOperationId),
                            Result = default
                        }
                        : new GetServiceResultResponse
                        {
                            Operation = Info(collected, ServiceOperationState.OutcomeAvailable),
                            Result = new ExtensionObject(CoreUtils.Clone(collected.Response)!)
                        };
                    break;
                case ControlServiceExecutionRequest control:
                    Controls.Enqueue(CoreUtils.Clone(control)!);
                    Assert.That(control.Action, Is.EqualTo(ServiceOperationControlAction.Acknowledge));
                    Retained acknowledged = Find(Uuid.Empty, control.ServerToken);
                    if (StatusCode.IsBad(ControlFault))
                    {
                        response = Fault(ControlFault, request.RequestHeader.RequestHandle);
                        break;
                    }
                    bool already = Interlocked.Exchange(ref acknowledged.Acknowledged, 1) != 0;
                    AckObservedCallerCompletion = CallerHasCompleted?.Invoke() ?? true;
                    AckCallerCompletions.Enqueue(AckObservedCallerCompletion);
                    AckEntered.TrySetResult(true);
                    if (LoseAckReply)
                    {
                        LoseAckReply = false;
                        throw new ServiceResultException(StatusCodes.BadConnectionClosed);
                    }
                    response = new ControlServiceExecutionResponse
                    {
                        Result = already
                            ? ServiceOperationControlResult.AlreadyApplied : ServiceOperationControlResult.Applied,
                        Operation = Info(acknowledged, ServiceOperationState.Acknowledged)
                    };
                    break;
                case ReadRequest read when read.NodesToRead.ToList().All(node =>
                    node.NodeId == new NodeId(100u) || node.NodeId == new NodeId(101u)):
                case WriteRequest:
                case CallRequest:
                case BrowseRequest:
                    OrdinaryRequests.Enqueue(CoreUtils.Clone(request)!);
                    response = CreateOriginalResponse(request);
                    break;
                default:
                    return channel.CreateResponse(request);
            }
            response.ResponseHeader.RequestHandle = request.RequestHeader.RequestHandle;
            MutateResponse?.Invoke(request, response);
            if (BinaryRoundTripResponses)
            {
                IServiceMessageContext context = channel.Channel.MessageContext;
                response = BinaryDecoder.DecodeMessage<IServiceResponse>(
                    BinaryEncoder.EncodeMessage(response, context), context);
                DeliveredResponses.Enqueue(response);
            }
            return response;
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
            return new ServiceOperationInfoDataType
            {
                ClientOperationId = retained.Submission.ClientOperationId,
                ServerToken = retained.Token,
                ServiceId = retained.ServiceId,
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
                HistoryRemaining = state == ServiceOperationState.Acknowledged
                    ? retained.Submission.ResultRetentionTime / 2 : 0
            };
        }

        private sealed record Retained(
            SubmitServiceRequest Submission, ByteString Token, IServiceResponse Response, NodeId ServiceId)
        {
            public int Acknowledged;
        }

        private static readonly NodeId s_root = new(1200100u);

        private static readonly string[] s_propertyNames =
            ["ContractVersion", "SupportedServices", "DefaultExecutionAllowance", "DefaultResultRetentionTime"];

        private static readonly NodeId[] s_propertyIds =
            [new("cap-contract", 0), new(93001u), new("cap-execution", 0), new(77123u)];

        private readonly ConcurrentDictionary<Uuid, Retained> m_operations = new();
        private readonly CancellationTokenSource m_timeout = new(TimeSpan.FromSeconds(15));
        private ServiceProvider? m_provider;
    }
}
