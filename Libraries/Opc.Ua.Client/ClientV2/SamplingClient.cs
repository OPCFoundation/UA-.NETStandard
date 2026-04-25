#if OPCUA_CLIENT_V2
// ------------------------------------------------------------
//  Copyright (c) Microsoft.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Opc.Ua.Client
{
    using Microsoft.Extensions.Logging;
    using Opc.Ua.Client.Sessions;
    using Opc.Ua.Client.Subscriptions;
    using System;
    using System.Collections.Generic;
    using System.Collections.Immutable;
    using System.Diagnostics;
    using System.Linq;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// The sampling client class allows applications to register nodes for
    /// periodic sampling using read calls on top of a session.
    /// </summary>
    internal sealed class SamplingClient : IAsyncDisposable
    {
        /// <summary>
        /// Create samplers
        /// </summary>
        /// <param name="session"></param>
        /// <param name="telemetry"></param>
        public SamplingClient(Sessions.ISession session, IV2TelemetryContext telemetry)
        {
            _session = session;
            _observability = telemetry;
            _logger = telemetry.LoggerFactory.CreateLogger<SamplingClient>();
        }

        /// <summary>
        /// Register for sampling
        /// </summary>
        /// <param name="name"></param>
        /// <param name="item"></param>
        /// <param name="samplingRate"></param>
        /// <param name="maxAge"></param>
        /// <param name="registration"></param>
        /// <returns></returns>
        public IAsyncDisposable Register(string name, ReadValueId item,
            TimeSpan samplingRate, TimeSpan maxAge, INotificationQueue registration)
        {
            if (samplingRate <= TimeSpan.Zero)
            {
                samplingRate = TimeSpan.FromSeconds(1);
            }
            if (maxAge < TimeSpan.Zero)
            {
                maxAge = TimeSpan.Zero;
            }
            lock (_samplers)
            {
                var key = (samplingRate, maxAge);
#pragma warning disable CA2000 // Dispose objects before losing scope
                var sampledNode = new Registration(this, key, name, item, registration);
#pragma warning restore CA2000 // Dispose objects before losing scope
                if (!_samplers.TryGetValue(key, out var sampler))
                {
                    sampler = new Sampler(this, samplingRate, maxAge, sampledNode);
                    _samplers.Add(key, sampler);
                }
                else
                {
                    sampler.Add(sampledNode);
                }
                return sampledNode;
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            List<Sampler> samplers;
            lock (_samplers)
            {
                samplers = [.. _samplers.Values];
                _samplers.Clear();
            }
            foreach (var sampler in samplers)
            {
                await sampler.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// A sampled node registered with a sampler
        /// </summary>
        /// <param name="Key"> Sampler key </param>
        /// <param name="InitialValue"> Item to monito </param>
        /// <param name="Queue"> Registration </param>
        /// <param name="Name"> Id of the sampled value in the resulting dataset </param>
        /// <param name="Client"></param>
        internal sealed record Registration((TimeSpan, TimeSpan) Key,
            ReadValueId InitialValue, INotificationQueue Queue, string Name,
            SamplingClient Client) : IAsyncDisposable
        {
            /// <summary>
            /// Create node
            /// </summary>
            /// <param name="outer"></param>
            /// <param name="key"></param>
            /// <param name="name"></param>
            /// <param name="item"></param>
            /// <param name="queue"></param>
            public Registration(SamplingClient outer, (TimeSpan, TimeSpan) key,
                string name, ReadValueId item, INotificationQueue queue)
                : this(key, item, queue, name, outer) => item.Handle = this;

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                Sampler? sampler;
                lock (Client._samplers)
                {
                    if (!Client._samplers.TryGetValue(Key, out sampler)
                        || !sampler.Remove(this))
                    {
                        return;
                    }
                    Client._samplers.Remove(Key);
                }
                await sampler.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Instead of subscribing through subscription the sampler bundles
        /// values to read directly
        /// </summary>
        internal sealed class Sampler : IAsyncDisposable
        {
            /// <summary>
            /// Creates the sampler
            /// </summary>
            /// <param name="outer"></param>
            /// <param name="samplingRate"></param>
            /// <param name="maxAge"></param>
            /// <param name="value"></param>
            public Sampler(SamplingClient outer, TimeSpan samplingRate,
                TimeSpan maxAge, Registration value)
            {
                _sampledNodes = ImmutableHashSet<Registration>.Empty.Add(value);
                _outer = outer;
                _cts = new CancellationTokenSource();
                _samplingRate = samplingRate;
                _maxAge = maxAge;
                _timer = new PeriodicTimer(_samplingRate);
                _sampler = RunAsync(_cts.Token);
            }

            /// <inheritdoc/>
            public async ValueTask DisposeAsync()
            {
                try
                {
                    await _cts.CancelAsync().ConfigureAwait(false);
                    _timer.Dispose();
                    await _sampler.ConfigureAwait(false);
                    _outer._logger.LogInformation(
                        "Closed sampler for {Rate}", _samplingRate);
                }
                catch (OperationCanceledException) { }
                finally
                {
                    _cts.Dispose();
                }
            }

            /// <summary>
            /// Add sampler
            /// </summary>
            /// <param name="node"></param>
            /// <returns></returns>
            internal Sampler Add(Registration node)
            {
                _sampledNodes = _sampledNodes.Add(node);
                return this;
            }

            /// <summary>
            /// Remove sampler
            /// </summary>
            /// <param name="value"></param>
            /// <returns></returns>
            internal bool Remove(Registration value)
            {
                _sampledNodes = _sampledNodes.Remove(value);
                return _sampledNodes.Count == 0;
            }

            /// <summary>
            /// Run sampling of values on the periodic timer
            /// </summary>
            /// <param name="ct"></param>
            /// <returns></returns>
            private async Task RunAsync(CancellationToken ct)
            {
                // Grab the current session
                var session = _outer._session;

                var sw = Stopwatch.StartNew();
                for (var sequenceNumber = 1u; !ct.IsCancellationRequested; sequenceNumber++)
                {
                    if (sequenceNumber == 0u)
                    {
                        continue;
                    }

                    var sampledNodes = _sampledNodes;
                    ArrayOf<ReadValueId> nodesToRead = sampledNodes
                        .Select(n => n.InitialValue).ToArray();
                    try
                    {
                        // Wait until period completed
                        if (!await _timer.WaitForNextTickAsync(ct).ConfigureAwait(false))
                        {
                            continue;
                        }

                        sw.Restart();

                        // Perform the read.
                        var timeout = _samplingRate.TotalMilliseconds / 2;
                        var response = await session.AttributeServiceSet.ReadAsync(new RequestHeader
                        {
                            Timestamp = _outer._observability.TimeProvider.GetUtcNow().UtcDateTime,
                            TimeoutHint = (uint)timeout,
                            ReturnDiagnostics = 0
                        }, _maxAge.TotalMilliseconds, Opc.Ua.TimestampsToReturn.Both,
                            nodesToRead, ct).ConfigureAwait(false);

                        // Notify clients of the values
                        await QueueAsync(sequenceNumber, sampledNodes, response.Results.ToArray() ?? [],
                            sw.Elapsed).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) { }
                    catch (ServiceResultException sre)
                    {
                        await QueueAsync(sequenceNumber, sampledNodes,
                            (uint)sre.StatusCode, sw.Elapsed).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        var error = new ServiceResult(ex).StatusCode;
                        await QueueAsync(sequenceNumber, sampledNodes,
                            error.Code, sw.Elapsed).ConfigureAwait(false);
                    }
                }
            }

            /// <summary>
            /// Notify results
            /// </summary>
            /// <param name="seq"></param>
            /// <param name="nodesToRead"></param>
            /// <param name="values"></param>
            /// <param name="elapsed"></param>
            private async ValueTask QueueAsync(uint seq, ImmutableHashSet<Registration> nodesToRead,
                IReadOnlyList<DataValue> values, TimeSpan elapsed)
            {
                var missed = GetMissed(elapsed);
                await Task.WhenAll(nodesToRead.Zip(values)
                    .GroupBy(n => n.First.Queue)
                    .Select(b => QueueAsyncCore(seq, (uint)StatusCodes.Good, missed, b.Key, [.. b])))
                    .ConfigureAwait(false);

                async Task QueueAsyncCore(uint seq, uint statusCode, int missed,
                    INotificationQueue queue, List<(Registration, DataValue)> items)
                {
                    var values = items
                        .ConvertAll(v => CreateSample(v.Item1, statusCode, missed > 0, v.Item2));
                    var changes = new PeriodicData(seq,
                        _outer._observability.TimeProvider.GetUtcNow().UtcDateTime, values,
                        PublishState.None, []);
                    await queue.QueueAsync(changes).ConfigureAwait(false);
                }
            }

            /// <summary>
            /// Notify error status code
            /// </summary>
            /// <param name="seq"></param>
            /// <param name="nodesToRead"></param>
            /// <param name="statusCode"></param>
            /// <param name="elapsed"></param>
            private async ValueTask QueueAsync(uint seq, ImmutableHashSet<Registration> nodesToRead,
                uint statusCode, TimeSpan elapsed)
            {
                var missed = GetMissed(elapsed);
                await Task.WhenAll(nodesToRead
                    .GroupBy(n => n.Queue)
                    .Select(batch => QueueAsyncCore(seq, statusCode, missed, batch.Key, [.. batch])))
                    .ConfigureAwait(false);

                async Task QueueAsyncCore(uint seq, uint statusCode, int missed,
                    INotificationQueue queue, List<Registration> items)
                {
                    var values = items
                        .ConvertAll(v => CreateSample(v, statusCode, missed > 0));
                    var changes = new PeriodicData(seq,
                        _outer._observability.TimeProvider.GetUtcNow().UtcDateTime, values,
                        PublishState.None, []);
                    await queue.QueueAsync(changes).ConfigureAwait(false);
                }
            }

            /// <summary>
            /// Create sample value
            /// </summary>
            /// <param name="node"></param>
            /// <param name="statusCode"></param>
            /// <param name="overflowBit"></param>
            /// <param name="dataValue"></param>
            /// <param name="diagnosticInfo"></param>
            /// <returns></returns>
            private static PeriodicValue CreateSample(Registration node, uint statusCode,
                bool overflowBit, DataValue? dataValue = null, DiagnosticInfo? diagnosticInfo = null)
            {
                dataValue ??= new DataValue();
                dataValue.StatusCode = statusCode;
                dataValue.StatusCode.SetOverflow(overflowBit);
                return new PeriodicValue(node.Name, node.InitialValue, dataValue, diagnosticInfo);
            }

            private int GetMissed(TimeSpan elapsed)
            {
                return (int)Math.Round(elapsed.TotalMilliseconds / _samplingRate.TotalMilliseconds);
            }

            private ImmutableHashSet<Registration> _sampledNodes;
            private readonly CancellationTokenSource _cts;
            private readonly Task _sampler;
            private readonly SamplingClient _outer;
            private readonly TimeSpan _samplingRate;
            private readonly TimeSpan _maxAge;
            private readonly PeriodicTimer _timer;
        }

        private readonly Sessions.ISession _session;
        private readonly Dictionary<(TimeSpan, TimeSpan), Sampler> _samplers = [];
        private readonly IV2TelemetryContext _observability;
        private readonly ILogger _logger;
    }
}
#endif
