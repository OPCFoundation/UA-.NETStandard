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
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.ISA95.Server.Builders;
using Opc.Ua.ISA95.Server.Providers;

namespace Opc.Ua.ISA95.Tests.Common
{
    /// <summary>
    /// Tests for the OPC-10030 geospatial location provider and the wiring helper
    /// that binds it to a <see cref="GeoSpatialLocationState"/> variable.
    /// </summary>
    [TestFixture]
    public class Isa95GeoSpatialLocationTests
    {
        private const string SourceId = "plant";
        private static readonly string[] s_berlin = ["Berlin"];
        private static readonly string[] s_munich = ["Munich"];
        private static readonly string[] s_zurichPlant = ["Building 4, Zurich Plant"];
        private static readonly string[] s_wktWithHeight =
            ["SRID=4326;POINT Z (8.5417 47.3769 408)"];
        private static readonly string[] s_wktAndLabel =
            ["POINT (8.5417 47.3769)", "Building 4, Zurich Plant"];

        [Test]
        public async Task BinderReadServesProviderLabels()
        {
            var fixture = new Isa95CommonTestContext();
            GeoSpatialLocationState state = CreateLocationVariable(fixture);
            using InMemoryGeoLocationProvider provider = CreateProvider("Berlin");
            Isa95ModelBuilder builder = fixture.CreateBuilder();

            using IDisposable binding = builder.BindGeoSpatialLocation(
                state,
                provider,
                SourceId);

            AttributeReadResult result = await state.OnReadValueAsync!(
                fixture.Context,
                state,
                NumericRange.Null,
                QualifiedName.Null,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result.Result), Is.True);
            Assert.That(ReadLiterals(result.Value), Is.EqualTo(s_berlin));
        }

        [Test]
        public async Task BinderProjectsAPositionAsWellKnownText()
        {
            var fixture = new Isa95CommonTestContext();
            GeoSpatialLocationState state = CreateLocationVariable(fixture);
            using var provider = new InMemoryGeoLocationProvider();
            provider.Update(
                SourceId,
                new GeoPosition(47.3769, 8.5417, 408.0, EpsgCode: 4326));
            Isa95ModelBuilder builder = fixture.CreateBuilder();

            using IDisposable binding = builder.BindGeoSpatialLocation(
                state,
                provider,
                SourceId);

            AttributeReadResult result = await state.OnReadValueAsync!(
                fixture.Context,
                state,
                NumericRange.Null,
                QualifiedName.Null,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                ReadLiterals(result.Value),
                Is.EqualTo(s_wktWithHeight));
        }

        [Test]
        public async Task BinderPublishesPositionAndLabelsTogether()
        {
            var fixture = new Isa95CommonTestContext();
            GeoSpatialLocationState state = CreateLocationVariable(fixture);
            using var provider = new InMemoryGeoLocationProvider();
            provider.Update(
                SourceId,
                new GeoLocationSample(
                    new GeoPosition(47.3769, 8.5417),
                    null,
                    s_zurichPlant.ToArrayOf(),
                    StatusCodes.Good,
                    DateTimeUtc.Now));
            Isa95ModelBuilder builder = fixture.CreateBuilder();

            using IDisposable binding = builder.BindGeoSpatialLocation(
                state,
                provider,
                SourceId);

            AttributeReadResult result = await state.OnReadValueAsync!(
                fixture.Context,
                state,
                NumericRange.Null,
                QualifiedName.Null,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(
                ReadLiterals(result.Value),
                Is.EqualTo(s_wktAndLabel));
        }

        [Test]
        public void BinderReadPropagatesProviderFault()
        {
            var fixture = new Isa95CommonTestContext();
            GeoSpatialLocationState state = CreateLocationVariable(fixture);
            using InMemoryGeoLocationProvider provider = CreateProvider("Berlin");
            provider.Fault(SourceId, new InvalidOperationException("boom"));
            Isa95ModelBuilder builder = fixture.CreateBuilder();

            using IDisposable binding = builder.BindGeoSpatialLocation(
                state,
                provider,
                SourceId);

            Assert.That(
                async () => await state.OnReadValueAsync!(
                    fixture.Context,
                    state,
                    NumericRange.Null,
                    QualifiedName.Null,
                    CancellationToken.None).ConfigureAwait(false),
                Throws.InvalidOperationException);
        }

        [Test]
        public async Task BinderReadFaultSurfacesAsBadStatusThroughStack()
        {
            var fixture = new Isa95CommonTestContext();
            GeoSpatialLocationState state = CreateLocationVariable(fixture);
            state.AccessLevel = AccessLevels.CurrentRead;
            state.UserAccessLevel = AccessLevels.CurrentRead;
            using InMemoryGeoLocationProvider provider = CreateProvider("Berlin");
            provider.Fault(SourceId, new InvalidOperationException("boom"));
            Isa95ModelBuilder builder = fixture.CreateBuilder();

            using IDisposable binding = builder.BindGeoSpatialLocation(
                state,
                provider,
                SourceId);

            (ServiceResult result, DataValue value) = await state.ReadAttributeAsync(
                fixture.Context,
                Attributes.Value,
                NumericRange.Null,
                QualifiedName.Null,
                new DataValue(),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsBad(result), Is.True);
            Assert.That(StatusCode.IsBad(value.StatusCode), Is.True);
        }

        [Test]
        public async Task BinderReadHonoursCancellation()
        {
            var fixture = new Isa95CommonTestContext();
            GeoSpatialLocationState state = CreateLocationVariable(fixture);
            using InMemoryGeoLocationProvider provider = CreateProvider("Berlin");
            Isa95ModelBuilder builder = fixture.CreateBuilder();
            using IDisposable binding = builder.BindGeoSpatialLocation(
                state,
                provider,
                SourceId);
            using var cts = new CancellationTokenSource();
            await cts.CancelAsync().ConfigureAwait(false);

            Assert.That(
                async () => await state.OnReadValueAsync!(
                    fixture.Context,
                    state,
                    NumericRange.Null,
                    QualifiedName.Null,
                    cts.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>());
        }

        [Test]
        public async Task BinderAppliesUpdatesToVariable()
        {
            var fixture = new Isa95CommonTestContext();
            GeoSpatialLocationState state = CreateLocationVariable(fixture);
            using InMemoryGeoLocationProvider provider = CreateProvider("Berlin");
            Isa95ModelBuilder builder = fixture.CreateBuilder();

            using IDisposable binding = builder.BindGeoSpatialLocation(
                state,
                provider,
                SourceId);

            bool applied = await WaitForValueAsync(state, "Munich", provider)
                .ConfigureAwait(false);
            Assert.That(applied, Is.True);
            Assert.That(ReadLiterals(state.Value), Is.EqualTo(s_munich));
        }

        [Test]
        public async Task BinderDoesNotSubscribeWhenTheProviderCannotPush()
        {
            var fixture = new Isa95CommonTestContext();
            GeoSpatialLocationState state = CreateLocationVariable(fixture);
            var provider = new PollOnlyProvider();
            Isa95ModelBuilder builder = fixture.CreateBuilder();

            using IDisposable binding = builder.BindGeoSpatialLocation(
                state,
                provider,
                SourceId);

            await Task.Delay(50).ConfigureAwait(false);

            Assert.That(provider.WatchCallCount, Is.Zero);
            AttributeReadResult result = await state.OnReadValueAsync!(
                fixture.Context,
                state,
                NumericRange.Null,
                QualifiedName.Null,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(ReadLiterals(result.Value), Is.EqualTo(s_berlin));
        }

        [Test]
        public async Task BinderLogsProviderUpdateStreamFailure()
        {
            var ns = new NamespaceTable();
            ushort instanceNs = ns.GetIndexOrAppend("urn:test:isa95:instance");
            ns.GetIndexOrAppend(Namespaces.ISA95);
            var logger = new CapturingLogger();
            var context = new SystemContext(new CapturingTelemetry(logger))
            {
                NamespaceUris = ns,
                NodeIdFactory = new SimpleNodeIdFactory()
            };
            var root = new FolderState(null)
            {
                NodeId = new NodeId("Root", instanceNs),
                BrowseName = new QualifiedName("Root", instanceNs),
                TypeDefinitionId = Ua.ObjectTypeIds.FolderType
            };
            GeoSpatialLocationState state =
                context.CreateInstanceOfGeoSpatialLocationType(
                    root,
                    new QualifiedName("Location", instanceNs));
            var provider = new FaultingUpdateProvider();

            using IDisposable binding =
                Isa95GeoSpatialLocationBinder.Bind(context, state, provider, SourceId);

            bool logged = false;
            for (int attempt = 0; attempt < 100 && !logged; attempt++)
            {
                logged = logger.Contains(9500);
                if (!logged)
                {
                    await Task.Delay(20).ConfigureAwait(false);
                }
            }

            Assert.That(logged, Is.True);
        }

        [Test]
        public async Task CreateGeoSpatialLocationWithoutProviderReturnsUnboundVariable()
        {
            var fixture = new Isa95CommonTestContext();
            Isa95ModelBuilder builder = fixture.CreateBuilder();

            using Isa95GeoSpatialLocationBinding binding =
                await builder.CreateGeoSpatialLocationAsync(fixture.Root, "Location").ConfigureAwait(false);

            Assert.That(binding.State, Is.TypeOf<GeoSpatialLocationState>());
            Assert.That(
                binding.State.TypeDefinitionId,
                Is.EqualTo(fixture.Resolve(VariableTypeIds.GeoSpatialLocationType)));
            Assert.That(binding.State.NodeId, Is.EqualTo(fixture.ExpectedChildId("Location")));
            Assert.That(binding.State.OnReadValueAsync, Is.Null);
            Assert.That(fixture.RegisterCount, Is.EqualTo(1));
        }

        [Test]
        public async Task CreateGeoSpatialLocationWithProviderBindsReads()
        {
            var fixture = new Isa95CommonTestContext();
            Isa95ModelBuilder builder = fixture.CreateBuilder();
            using InMemoryGeoLocationProvider provider = CreateProvider("Berlin");

            using Isa95GeoSpatialLocationBinding binding =
                await builder.CreateGeoSpatialLocationAsync(
                    fixture.Root,
                    "Location",
                    provider,
                    SourceId).ConfigureAwait(false);

            Assert.That(binding.State.OnReadValueAsync, Is.Not.Null);
            AttributeReadResult result = await binding.State.OnReadValueAsync!(
                fixture.Context,
                binding.State,
                NumericRange.Null,
                QualifiedName.Null,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(ReadLiterals(result.Value), Is.EqualTo(s_berlin));
        }

        [Test]
        public void CreateGeoSpatialLocationWithProviderRequiresASourceId()
        {
            var fixture = new Isa95CommonTestContext();
            Isa95ModelBuilder builder = fixture.CreateBuilder();
            using InMemoryGeoLocationProvider provider = CreateProvider("Berlin");

            Assert.That(
                async () => await builder.CreateGeoSpatialLocationAsync(
                    fixture.Root,
                    "Location",
                    provider).ConfigureAwait(false),
                Throws.ArgumentException);
        }

        private static InMemoryGeoLocationProvider CreateProvider(params string[] labels)
        {
            var provider = new InMemoryGeoLocationProvider();
            provider.Update(SourceId, GeoLocationSample.Good(labels.ToArrayOf()));
            return provider;
        }

        private static string[] ReadLiterals(Variant value)
        {
            return value.TryGetValue(out ArrayOf<string> literals)
                ? [.. literals]
                : [];
        }

        private static async Task<bool> WaitForValueAsync(
            GeoSpatialLocationState state,
            string expected,
            InMemoryGeoLocationProvider provider)
        {
            for (int attempt = 0; attempt < 100; attempt++)
            {
                provider.Update(
                    SourceId,
                    GeoLocationSample.Good(new[] { expected }.ToArrayOf()));
                string[] literals = ReadLiterals(state.Value);
                if (literals.Length == 1 && literals[0] == expected)
                {
                    return true;
                }
                await Task.Delay(20).ConfigureAwait(false);
            }
            string[] final = ReadLiterals(state.Value);
            return final.Length == 1 && final[0] == expected;
        }

        private static GeoSpatialLocationState CreateLocationVariable(
            Isa95CommonTestContext fixture)
        {
            return fixture.Context.CreateInstanceOfGeoSpatialLocationType(
                fixture.Root,
                new QualifiedName("Location", fixture.InstanceNamespaceIndex));
        }

        private sealed class FaultingUpdateProvider : IGeoLocationProvider
        {
            public bool SupportsPush => true;

            public ValueTask<GeoLocationSample> ReadAsync(
                string sourceId,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<GeoLocationSample>(
                    GeoLocationSample.Good(s_berlin.ToArrayOf()));
            }

            public async IAsyncEnumerable<GeoLocationSample> WatchAsync(
                string sourceId,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                yield return GeoLocationSample.Good(s_munich.ToArrayOf());
                await Task.Yield();
                throw new InvalidOperationException("stream boom");
            }
        }

        private sealed class PollOnlyProvider : IGeoLocationProvider
        {
            public bool SupportsPush => false;

            public int WatchCallCount => m_watchCallCount;

            public ValueTask<GeoLocationSample> ReadAsync(
                string sourceId,
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<GeoLocationSample>(
                    GeoLocationSample.Good(s_berlin.ToArrayOf()));
            }

            public async IAsyncEnumerable<GeoLocationSample> WatchAsync(
                string sourceId,
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                Interlocked.Increment(ref m_watchCallCount);
                await Task.Yield();
                yield break;
            }

            private int m_watchCallCount;
        }
        private sealed class SimpleNodeIdFactory : INodeIdFactory
        {
            public NodeId New(ISystemContext context, NodeState node)
            {
                if (!node.NodeId.IsNull)
                {
                    return node.NodeId;
                }
                if (node is BaseInstanceState instance && instance.Parent != null)
                {
                    string name = instance.SymbolicName ?? instance.BrowseName.Name ?? "Node";
                    return new NodeId(
                        instance.Parent.NodeId.IdentifierAsString + "_" + name,
                        instance.Parent.NodeId.NamespaceIndex);
                }
                return node.NodeId;
            }
        }

        private sealed class CapturingTelemetry : ITelemetryContext
        {
            public CapturingTelemetry(ILogger logger)
            {
                LoggerFactory = new SingleLoggerFactory(logger);
            }

            public Meter CreateMeter()
            {
                return new Meter("Opc.Ua.ISA95.Tests");
            }

            public ILoggerFactory LoggerFactory { get; }

            public ActivitySource ActivitySource { get; } =
                new ActivitySource("Opc.Ua.ISA95.Tests");
        }

        private sealed class SingleLoggerFactory : ILoggerFactory
        {
            public SingleLoggerFactory(ILogger logger)
            {
                m_logger = logger;
            }

            public void AddProvider(ILoggerProvider provider)
            {
            }

            public ILogger CreateLogger(string categoryName)
            {
                return m_logger;
            }

            public void Dispose()
            {
            }

            private readonly ILogger m_logger;
        }

        private sealed class CapturingLogger : ILogger
        {
            public bool Contains(int eventId)
            {
                lock (m_gate)
                {
                    return m_events.Contains(eventId);
                }
            }

            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return true;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (m_gate)
                {
                    m_events.Add(eventId.Id);
                }
            }

            private readonly Lock m_gate = new();
            private readonly List<int> m_events = [];
        }
    }
}
