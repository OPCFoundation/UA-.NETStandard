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
 * MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
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
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.XRegistry.Bridge.Sync;
using Opc.Ua.XRegistry.Protocol;
using Opc.Ua.XRegistry.Server.Protocol;

namespace Opc.Ua.XRegistry.Bridge.Tests.Sync
{
    internal sealed class XRegistrySyncFixture : IAsyncDisposable
    {
        public XRegistrySyncFixture(string? model = null, bool journal = false)
        {
            Native = new SyncEndpoint("native-registry", model ?? DefaultModel, Clock, journal);
            Http = new SyncEndpoint("http-registry", model ?? DefaultModel, Clock, journal);
            Telemetry = Mock.Of<ITelemetryContext>(context => context.LoggerFactory == NullLoggerFactory.Instance);
        }

        public SyncEndpoint Native { get; }

        public SyncEndpoint Http { get; }

        public MemoryXRegistrySyncStateStore Store { get; } = new();

        public SyncClock Clock { get; } = new();

        public ITelemetryContext Telemetry { get; }

        public XRegistrySyncOptions Options { get; set; } = new("sync-test", "opc.tcp://native.example/registry",
            "https://http.example/registry")
        {
            OpcUaContext = Writer,
            HttpContext = Writer
        };

        public XRegistrySyncStateManager State => new(Store, Options.JobId, Clock);

        public XRegistrySynchronizer Engine(
            IXRegistrySyncStateStore? store = null,
            XRegistrySyncOptions? options = null)
        {
            return new XRegistrySynchronizer(Native, Http, store ?? Store, options ?? Options, Telemetry, Clock);
        }

        public async Task SeedBothAsync(string path, string json)
        {
            await Native.ChangeAsync(path, json).ConfigureAwait(false);
            await Http.ChangeAsync(path, json).ConfigureAwait(false);
        }

        public async Task BaselineAsync()
        {
            XRegistrySyncReport report = await Engine().RunOnceAsync().ConfigureAwait(false);
            Assert.That(report.Status, Is.EqualTo(XRegistrySyncStatus.Succeeded), Details(report));
            ResetCounts();
        }

        public void ResetCounts()
        {
            Native.ResetCounts();
            Http.ResetCounts();
        }

        public async ValueTask DisposeAsync()
        {
            Native.Dispose();
            Http.Dispose();
            await Store.DisposeAsync().ConfigureAwait(false);
        }

        public static JsonElement Json(string text)
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }

        public static string Details(XRegistrySyncReport report)
        {
            return string.Join(Environment.NewLine, report.Records.Span.ToArray()
                .Select(record => $"{record.Kind} {record.Path}: {record.Detail}"));
        }

        public static XRegistryCallContext Writer { get; } = new("operator")
        {
            IsAuthenticated = true,
            Roles = ["xregistry.write"]
        };

        public const string Group = "/schemagroups/g";
        public const string Resource = "/schemagroups/g/schemas/r";
        public const string VersionPath = "/schemagroups/g/schemas/r/versions/v1";

        public const string DefaultModel = /*lang=json,strict*/ """
            {
              "attributes":{"*":{"type":"any"}},
              "groups":{
                "schemagroups":{
                  "singular":"schemagroup",
                  "attributes":{"*":{"type":"any"}},
                  "resources":{
                    "schemas":{"singular":"schema","hasdocument":true,"attributes":{"*":{"type":"any"}},
                      "metaattributes":{"*":{"type":"any"}}},
                    "events":{"singular":"event","hasdocument":false,"attributes":{"*":{"type":"any"}}}
                  }
                },
                "othergroups":{
                  "singular":"othergroup",
                  "attributes":{"*":{"type":"any"}},
                  "resources":{
                    "schemas":{"singular":"schema","hasdocument":true,"attributes":{"*":{"type":"any"}}}
                  }
                }
              }
            }
            """;
    }

    internal sealed class SyncEndpoint : IXRegistryOperationJournalEndpoint, IDisposable
    {
        public SyncEndpoint(string registryId, string model, TimeProvider clock, bool journal)
        {
            m_store = new FixtureTransactionStore(journal);
            m_endpoint = new XRegistryTransactionalEndpoint(new XRegistryTransactionalOptions
            {
                RegistryId = registryId,
                Model = XRegistrySyncFixture.Json(model),
                PublicRoot = new Uri("https://" + registryId + ".example/"),
                MaxEntities = 10_000
            }, m_store, clock);
        }

        public List<XRegistryRequest> Mutations { get; } = [];

        public int Reads { get; private set; }

        public int Inspections { get; private set; }

        public int Committed { get; private set; }

        public int Acknowledged { get; private set; }

        public int JournalQueries { get; private set; }

        public bool FailAfterMutation { get; set; }

        public bool FailBeforeMutation { get; set; }

        public bool FailInspection { get; set; }

        public bool Qualified { get; set; } = true;

        public bool RejectOperationId { get; set; }

        public JsonElement ModelOverride { get; set; }

        public string? ReadFailurePath { get; set; }

        public int ReadFailureStatus { get; set; } = 403;

        public Func<XRegistryRequest, CancellationToken, ValueTask>? BeforeExecuteAsync { get; set; }

        public Func<XRegistryRequest, XRegistryResponse, CancellationToken, ValueTask>? AfterExecuteAsync { get; set; }

        public Func<XRegistryRequest, XRegistryResponse, XRegistryResponse>? TransformResponse { get; set; }

        public Func<CancellationToken, ValueTask<XRegistryEndpointDescription>>? InspectOverrideAsync { get; set; }

        public async ValueTask<XRegistryEndpointDescription> InspectAsync(
            XRegistryCallContext context,
            CancellationToken cancellationToken = default)
        {
            Inspections++;
            if (FailInspection)
            {
                throw new UnauthorizedAccessException("Fixture inspection denied.");
            }
            if (InspectOverrideAsync is not null)
            {
                return await InspectOverrideAsync(cancellationToken).ConfigureAwait(false);
            }
            XRegistryEndpointDescription description = await m_endpoint.InspectAsync(context, cancellationToken)
                .ConfigureAwait(false);
            return description with
            {
                Model = ModelOverride.ValueKind == JsonValueKind.Undefined ? description.Model : ModelOverride,
                SupportsAtomicMutations = Qualified,
                SupportsConditionalMutations = Qualified
            };
        }

        public async ValueTask<XRegistryResponse> ExecuteAsync(
            XRegistryRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request.IsMutation)
            {
                Mutations.Add(request);
                if (FailBeforeMutation)
                {
                    FailBeforeMutation = false;
                    throw new TimeoutException("Fixture lost the request before dispatch.");
                }
                if (RejectOperationId && request.OperationId is not null)
                {
                    return new XRegistryResponse(405)
                    {
                        Error = new XRegistryError("action_not_supported", "No replay.")
                    };
                }
            }
            else
            {
                Reads++;
                if (request.Path == ReadFailurePath)
                {
                    return new XRegistryResponse(ReadFailureStatus)
                    {
                        Error = new XRegistryError("fixture_read_failed", "Fixture inventory failure.")
                    };
                }
            }
            if (BeforeExecuteAsync is not null)
            {
                await BeforeExecuteAsync(request, cancellationToken).ConfigureAwait(false);
            }
            XRegistryResponse response = await m_endpoint.ExecuteAsync(request, cancellationToken)
                .ConfigureAwait(false);
            if (request.IsMutation && response.IsSuccess)
            {
                Committed++;
            }
            if (AfterExecuteAsync is not null)
            {
                await AfterExecuteAsync(request, response, cancellationToken).ConfigureAwait(false);
            }
            if (request.IsMutation && FailAfterMutation)
            {
                FailAfterMutation = false;
                throw new TimeoutException("Fixture committed but lost the response.");
            }
            if (request.IsMutation && response.IsSuccess)
            {
                Acknowledged++;
            }
            return TransformResponse?.Invoke(request, response) ?? response;
        }

        public ValueTask<XRegistryOperationOutcome> GetOperationOutcomeAsync(
            string operationId,
            XRegistryCallContext context,
            CancellationToken cancellationToken = default)
        {
            JournalQueries++;
            return m_endpoint.GetOperationOutcomeAsync(operationId, context, cancellationToken);
        }

        public async Task ChangeAsync(string path, string json, XRegistryAction action = XRegistryAction.Replace)
        {
            XRegistryResponse response = await m_endpoint.ExecuteAsync(new XRegistryRequest(action, path)
            {
                Context = XRegistrySyncFixture.Writer,
                View = XRegistryView.Metadata,
                Metadata = XRegistrySyncFixture.Json(json)
            }).ConfigureAwait(false);
            Assert.That(response.IsSuccess, Is.True,
                $"Fixture setup failed: {response.StatusCode} {response.Error?.Code}: {response.Error?.Detail}");
        }

        public async Task DeleteAsync(string path)
        {
            XRegistryResponse response = await m_endpoint.ExecuteAsync(
                new XRegistryRequest(XRegistryAction.Delete, path)
                {
                    Context = XRegistrySyncFixture.Writer
                }).ConfigureAwait(false);
            Assert.That(response.IsSuccess, Is.True, $"Fixture delete failed: {response.Error?.Detail}");
        }

        public Task<XRegistryResponse> ReadAsync(string path, XRegistryView view = XRegistryView.Metadata)
        {
            return m_endpoint.ExecuteAsync(new XRegistryRequest(XRegistryAction.Read, path)
            {
                Context = XRegistrySyncFixture.Writer,
                View = view
            }).AsTask();
        }

        public async Task<int> JournalCountAsync()
        {
            ByteString bytes = await m_store.LoadAsync().ConfigureAwait(false);
            using var document = JsonDocument.Parse(bytes.Memory);
            return document.RootElement.GetProperty("operations").EnumerateObject().Count();
        }

        public async Task SetEpochAsync(string path, string epoch)
        {
            ByteString current = await m_store.LoadAsync().ConfigureAwait(false);
            JsonObject snapshot = JsonNode.Parse(current.Span)!.AsObject();
            snapshot["entries"]![path]!["metadata"]!["epoch"] = JsonNode.Parse(epoch);
            ByteString updated = new XRegistryProtocolCodec().EncodeJson(snapshot);
            Assert.That(await m_store.CommitAsync(current, updated).ConfigureAwait(false), Is.True);
        }

        public void ResetCounts()
        {
            Mutations.Clear();
            Reads = 0;
            Inspections = 0;
            Committed = 0;
            Acknowledged = 0;
            JournalQueries = 0;
        }

        public void Dispose()
        {
            m_endpoint.Dispose();
        }

        private readonly FixtureTransactionStore m_store;
        private readonly XRegistryTransactionalEndpoint m_endpoint;

        private sealed class FixtureTransactionStore(bool replay) : IXRegistryTransactionStore
        {
            public bool SupportsDurableReplay => replay;

            public ValueTask<ByteString> LoadAsync(CancellationToken cancellationToken = default)
            {
                return m_inner.LoadAsync(cancellationToken);
            }

            public ValueTask<bool> CommitAsync(
                ByteString expected,
                ByteString replacement,
                CancellationToken cancellationToken = default)
            {
                return m_inner.CommitAsync(expected, replacement, cancellationToken);
            }

            private readonly InMemoryXRegistryTransactionStore m_inner = new();
        }
    }

    internal sealed class SyncClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()
        {
            lock (m_gate)
            {
                return m_now;
            }
        }

        public void Advance(TimeSpan elapsed)
        {
            ManualTimer[] timers;
            DateTimeOffset now;
            lock (m_gate)
            {
                m_now += elapsed;
                now = m_now;
                timers = [.. m_timers];
            }
            foreach (ManualTimer timer in timers)
            {
                timer.Fire(now);
            }
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            lock (m_gate)
            {
                m_timers.Add(timer);
                timer.Change(dueTime, period);
            }
            return timer;
        }

        private DateTimeOffset m_now = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);
        private readonly List<ManualTimer> m_timers = [];
        private readonly Lock m_gate = new();

        private sealed class ManualTimer(SyncClock owner, TimerCallback callback, object? state) : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                m_due = dueTime == Timeout.InfiniteTimeSpan ? DateTimeOffset.MaxValue : owner.GetUtcNow() + dueTime;
                return true;
            }

            public void Fire(DateTimeOffset now)
            {
                if (now >= m_due)
                {
                    m_due = DateTimeOffset.MaxValue;
                    callback(state);
                }
            }

            public void Dispose()
            {
                lock (owner.m_gate)
                {
                    owner.m_timers.Remove(this);
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return default;
            }

            private DateTimeOffset m_due;
        }
    }
}
