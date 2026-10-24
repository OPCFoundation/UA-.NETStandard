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
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.DataChannels;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Encoding;
using Opc.Ua.PubSub.Groups;
using Opc.Ua.PubSub.Transports.DataChannels;

namespace Quickstarts.ConsoleSecureChannelPubSub
{
    /// <summary>
    /// Runs the ordinary Part 14 runtime with bounded, changing datasets across a live flow revision.
    /// </summary>
    internal static class PubSubWorkload
    {
        /// <summary>
        /// Executes one process role, preserving the real transport and original PubSub messages.
        /// </summary>
        /// <exception cref="InvalidOperationException"></exception>
        /// <exception cref="DataChannelException"></exception>
        internal static async Task<EndpointEvidence> RunAsync(
            SampleOptions options,
            bool server,
            IDataChannelPubSubFlowProvider flowProvider,
            ITelemetryContext telemetry,
            Func<DataChannel, CancellationToken, Task>? revise,
            CancellationToken cancellationToken)
        {
            bool publisher = server != options.Reverse;
            var observedFlows = new ObservedFlowProvider(flowProvider);
            var factory = new ObservedTransportFactory(options.Carrier, observedFlows);
            var source = new SampleSource(0);
            var otherSource = new SampleSource(1000);
            var sink = new SampleSink(0, options.Count);
            var otherSink = new SampleSink(1000, options.Count);
            var unmatched = new UnmatchedSink();
            PubSubApplicationBuilder builder = new PubSubApplicationBuilder(telemetry)
                .WithApplicationId(server ? "sample-server" : "sample-client")
                .UseConfiguration(SampleConfiguration.Build(options, publisher))
                .AddTransportFactory(factory)
                .UseAllStandardEncoders();
            if (publisher)
            {
                builder.AddDataSetSource(SampleConfiguration.TelemetryDataSet, source)
                    .AddDataSetSource(SampleConfiguration.OtherDataSet, otherSource);
            }
            else
            {
                builder.AddSubscribedDataSetSink(SampleConfiguration.TelemetryReader, sink)
                    .AddSubscribedDataSetSink(SampleConfiguration.OtherReader, otherSink)
                    .AddSubscribedDataSetSink(SampleConfiguration.WrongWriterReader, unmatched)
                    .AddSubscribedDataSetSink(SampleConfiguration.WrongPublisherReader, unmatched);
            }
            IPubSubApplication application = builder.Build();
            await using ConfiguredAsyncDisposable applicationLifetime = application.ConfigureAwait(false);
            var cycles = new ProgressCounter();
            var cycleRevisions = new ConcurrentQueue<uint>();
            if (publisher)
            {
                var group = (WriterGroup)application.Connections[0].WriterGroups[0];
                Func<PubSubNetworkMessage, CancellationToken, ValueTask> emit = group.PublishSink ??
                    throw new InvalidOperationException("The standard WriterGroup has no publishing sink.");
                group.PublishSink = async (message, token) =>
                {
                    uint revision = observedFlows.Channel.Revision;
                    await emit(message, token).ConfigureAwait(false);
                    cycleRevisions.Enqueue(revision);
                    cycles.Increment();
                };
            }
            await application.StartAsync(cancellationToken).ConfigureAwait(false);
            DataChannel channel = observedFlows.Channel;
            ObservedTransport transport = factory.Transport;
            try
            {
                int midpoint = options.Count / 2;
                Console.WriteLine(
                    $"{(server ? "server" : "client")}: READY context={channel.GetStatus().ContextId} " +
                    $"flow={channel.ChannelId} publisher={publisher} opener={channel.IsLocalOpener} " +
                    $"protocol={channel.Parameters.Protocol}");

                if (publisher)
                {
                    await PublishRangeAsync(1, midpoint, source, otherSource, cycles, cancellationToken)
                        .ConfigureAwait(false);
                }
                else
                {
                    await Task.WhenAll(sink.WaitAsync(midpoint, cancellationToken),
                        otherSink.WaitAsync(midpoint, cancellationToken)).ConfigureAwait(false);
                }

                if (revise != null)
                {
                    await revise(channel, cancellationToken).ConfigureAwait(false);
                }
                await WaitForAsync(channel, static status =>
                    status.Revision == 2 && status.PendingRevision == 0 && status.State == DataChannelState.Open,
                    cancellationToken)
                    .ConfigureAwait(false);
                if (channel.Parameters.MaxMessageSize != 16384 || channel.Parameters.MaxReceiveBytes != 131072)
                {
                    throw new InvalidOperationException("The live revision did not reduce the negotiated limits.");
                }
                Console.WriteLine(
                    $"{(server ? "server" : "client")}: revision=2 " +
                    $"message={channel.Parameters.MaxMessageSize} receive={channel.Parameters.MaxReceiveBytes}");

                if (publisher)
                {
                    await PublishRangeAsync(
                        midpoint + 1, options.Count, source, otherSource, cycles, cancellationToken)
                        .ConfigureAwait(false);
                    Console.WriteLine($"{(server ? "server" : "client")}: DATA cycles={cycles.Count} " +
                        $"networkMessages={transport.Sent.Length}");
                }
                else
                {
                    await Task.WhenAll(sink.WaitAsync(options.Count, cancellationToken),
                        otherSink.WaitAsync(options.Count, cancellationToken)).ConfigureAwait(false);
                    Console.WriteLine($"{(server ? "server" : "client")}: DATA counters=1..{options.Count} " +
                        $"other=1001..{1000 + options.Count} networkMessages={transport.Received.Length}");
                    await transport.EndOfInput.WaitAsync(cancellationToken).ConfigureAwait(false);
                }
                DataChannelResult ended = await channel.EndAsync(cancellationToken).ConfigureAwait(false);
                if (ended != DataChannelResult.Good)
                {
                    throw new DataChannelException(ended);
                }
                await WaitForAsync(channel, static status => status.State == DataChannelState.Closed, cancellationToken)
                    .ConfigureAwait(false);
                Console.WriteLine($"{(server ? "server" : "client")}: CLOSED result={channel.GetStatus().Result}");

                DataChannelStatusDataType status = channel.GetStatus();
                DataChannelDiagnosticsDataType diagnostics = channel.GetDiagnostics();
                if (status.Result != DataChannelResult.Good ||
                    !status.OpenerEnded ||
                    !status.AcceptorEnded ||
                    unmatched.Deliveries != 0 ||
                    (publisher &&
                        (cycles.Count != options.Count ||
                            transport.Sent.Length < options.Count ||
                            diagnostics.MessagesSent != (ulong)transport.Sent.Length)) ||
                    (!publisher &&
                        (transport.Received.Length < options.Count ||
                            diagnostics.MessagesReceived != (ulong)transport.Received.Length)))
                {
                    throw new InvalidOperationException(
                        $"{(server ? "server" : "client")}: flow={status.State}/{status.Result}; " +
                        $"sent={transport.Sent.Length}/{diagnostics.MessagesSent}; " +
                        $"received={transport.Received.Length}/{diagnostics.MessagesReceived}; " +
                        $"unmatched={unmatched.Deliveries}; expected={options.Count}.");
                }
                var evidence = new EndpointEvidence
                {
                    Role = server ? "server" : "client",
                    Publisher = publisher,
                    Profile = transport.TransportProfileUri,
                    Protocol = channel.Parameters.Protocol
                        ?? throw new InvalidOperationException("The ready flow has no negotiated payload protocol."),
                    ContextId = status.ContextId,
                    ChannelId = channel.ChannelId,
                    LocalOpener = channel.IsLocalOpener,
                    Direction = channel.Parameters.Direction.ToString(),
                    Protection = channel.Parameters.Protection.ToString(),
                    Revision = channel.Revision,
                    MaxMessageSize = channel.Parameters.MaxMessageSize,
                    MaxReceiveBytes = channel.Parameters.MaxReceiveBytes,
                    OrderlyEnd = status.OpenerEnded && status.AcceptorEnded,
                    Sent = transport.Sent,
                    Received = transport.Received,
                    SentRevisions = transport.SentRevisions,
                    ReceivedRevisions = transport.ReceivedRevisions,
                    PublishedRevisions = [.. cycleRevisions],
                    Counters = sink.Counters,
                    OtherCounters = otherSink.Counters,
                    Toggles = sink.Toggles,
                    Temperatures = sink.Temperatures,
                    RejectedReaderDeliveries = unmatched.Deliveries
                };
                await application.StopAsync(cancellationToken).ConfigureAwait(false);
                return evidence;
            }
            catch (OperationCanceledException)
            {
                DataChannelStatusDataType status = channel.GetStatus();
                DataChannelDiagnosticsDataType diagnostics = channel.GetDiagnostics();
                await Console.Error.WriteLineAsync(
                    $"FAILED flow-state {(server ? "server" : "client")}: " +
                    $"state={status.State} revision={status.Revision} pending={status.PendingRevision} " +
                    $"creditStalls={diagnostics.CreditStalls} " +
                    $"sent={diagnostics.MessagesSent} received={diagnostics.MessagesReceived} " +
                    $"cycles={cycles.Count} decoded={sink.Progress.Count}/{otherSink.Progress.Count} " +
                    $"input={transport.EndOfInput.Status} ended={status.OpenerEnded}/{status.AcceptorEnded}")
                    .ConfigureAwait(false);
                throw;
            }
        }

        /// <summary>
        /// Samples through the real scheduler and waits for actual successful transport sends.
        /// </summary>
        private static async Task PublishRangeAsync(
            int first,
            int last,
            SampleSource source,
            SampleSource otherSource,
            ProgressCounter cycles,
            CancellationToken cancellationToken)
        {
            for (int sequence = first; sequence <= last; sequence++)
            {
                await source.ReleaseAsync(sequence, cancellationToken).ConfigureAwait(false);
                await otherSource.ReleaseAsync(sequence, cancellationToken).ConfigureAwait(false);
                await cycles.WaitAsync(sequence, cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Observes real library transitions, including faults, without implementing the control protocol.
        /// </summary>
        private static async Task WaitForAsync(
            DataChannel channel,
            Func<DataChannelStatusDataType, bool> completed,
            CancellationToken cancellationToken)
        {
            var changed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Inspect(DataChannelStatusDataType status)
            {
                if (completed(status))
                {
                    changed.TrySetResult();
                }
                else if (status.State is DataChannelState.Closed or DataChannelState.Faulted)
                {
                    changed.TrySetException(new DataChannelException(status.Result));
                }
            }
            void OnChanged(DataChannelStateChangedEventArgs args) => Inspect(args.Status);
            channel.StateChanged += OnChanged;
            try
            {
                Inspect(channel.GetStatus());
                await changed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                channel.StateChanged -= OnChanged;
            }
        }
    }
}
