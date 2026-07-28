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
        [Test]
        public async Task StaticProviderReadReturnsCurrentValue()
        {
            using var provider = new Isa95GeoSpatialLocationProvider("Berlin");

            Isa95GeoSpatialLocation location = await provider.GetCurrentAsync().ConfigureAwait(false);

            Assert.That(location.Value, Is.EqualTo("Berlin"));
            Assert.That(location.StatusCode, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task StaticProviderUpdateNotifiesSubscribersInOrder()
        {
            using var provider = new Isa95GeoSpatialLocationProvider("Berlin");
            using var cts = new CancellationTokenSource();

            await using IAsyncEnumerator<Isa95GeoSpatialLocation> enumerator =
                provider.SubscribeAsync(cts.Token).GetAsyncEnumerator(cts.Token);

            ValueTask<bool> first = enumerator.MoveNextAsync();
            provider.Update(Isa95GeoSpatialLocation.Good("Munich"));
            Assert.That(await first.ConfigureAwait(false), Is.True);
            Assert.That(enumerator.Current.Value, Is.EqualTo("Munich"));

            ValueTask<bool> second = enumerator.MoveNextAsync();
            provider.Update("Hamburg", StatusCodes.Good, DateTime.UtcNow);
            Assert.That(await second.ConfigureAwait(false), Is.True);
            Assert.That(enumerator.Current.Value, Is.EqualTo("Hamburg"));

            await cts.CancelAsync().ConfigureAwait(false);
        }

        [Test]
        public async Task StaticProviderSubscriptionEndsOnCancellation()
        {
            using var provider = new Isa95GeoSpatialLocationProvider("Berlin");
            using var cts = new CancellationTokenSource();

            var consume = Task.Run(async () =>
            {
                await foreach (Isa95GeoSpatialLocation _ in provider.SubscribeAsync(cts.Token))
                {
                    // Drain until cancelled.
                }
            });

            await cts.CancelAsync().ConfigureAwait(false);
            await consume.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

            Assert.That(consume.Status, Is.EqualTo(TaskStatus.RanToCompletion));
        }

        [Test]
        public void StaticProviderFaultPropagatesToReader()
        {
            using var provider = new Isa95GeoSpatialLocationProvider("Berlin");
            provider.Fault(new InvalidOperationException("boom"));

            Assert.That(
                async () => await provider.GetCurrentAsync().ConfigureAwait(false),
                Throws.InvalidOperationException);
        }

        [Test]
        public async Task StaticProviderUpdateClearsFault()
        {
            using var provider = new Isa95GeoSpatialLocationProvider("Berlin");
            provider.Fault(new InvalidOperationException("boom"));

            provider.Update(Isa95GeoSpatialLocation.Good("Cologne"));
            Isa95GeoSpatialLocation location = await provider.GetCurrentAsync().ConfigureAwait(false);

            Assert.That(location.Value, Is.EqualTo("Cologne"));
        }

        [Test]
        public async Task BinderReadServesProviderValue()
        {
            var fixture = new Isa95CommonTestContext();
            GeoSpatialLocationState state = CreateLocationVariable(fixture);
            using var provider = new Isa95GeoSpatialLocationProvider("Berlin");
            Isa95ModelBuilder builder = fixture.CreateBuilder();

            using IDisposable binding = builder.BindGeoSpatialLocation(state, provider);

            AttributeReadResult result = await state.OnReadValueAsync!(
                fixture.Context,
                state,
                NumericRange.Null,
                QualifiedName.Null,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(ServiceResult.IsGood(result.Result), Is.True);
            Assert.That(result.Value.TryGetValue(out string text), Is.True);
            Assert.That(text, Is.EqualTo("Berlin"));
        }

        [Test]
        public void BinderReadPropagatesProviderFault()
        {
            var fixture = new Isa95CommonTestContext();
            GeoSpatialLocationState state = CreateLocationVariable(fixture);
            using var provider = new Isa95GeoSpatialLocationProvider("Berlin");
            provider.Fault(new InvalidOperationException("boom"));
            Isa95ModelBuilder builder = fixture.CreateBuilder();

            using IDisposable binding = builder.BindGeoSpatialLocation(state, provider);

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
            using var provider = new Isa95GeoSpatialLocationProvider("Berlin");
            provider.Fault(new InvalidOperationException("boom"));
            Isa95ModelBuilder builder = fixture.CreateBuilder();

            using IDisposable binding = builder.BindGeoSpatialLocation(state, provider);

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
            using var provider = new Isa95GeoSpatialLocationProvider("Berlin");
            Isa95ModelBuilder builder = fixture.CreateBuilder();
            using IDisposable binding = builder.BindGeoSpatialLocation(state, provider);
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
            using var provider = new Isa95GeoSpatialLocationProvider("Berlin");
            Isa95ModelBuilder builder = fixture.CreateBuilder();

            using IDisposable binding = builder.BindGeoSpatialLocation(state, provider);

            bool applied = await WaitForValueAsync(state, "Munich", provider).ConfigureAwait(false);
            Assert.That(applied, Is.True);
            Assert.That(state.Value.TryGetValue(out string text), Is.True);
            Assert.That(text, Is.EqualTo("Munich"));
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
                Isa95GeoSpatialLocationBinder.Bind(context, state, provider);

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
            using var provider = new Isa95GeoSpatialLocationProvider("Berlin");

            using Isa95GeoSpatialLocationBinding binding =
                await builder.CreateGeoSpatialLocationAsync(
                    fixture.Root,
                    "Location",
                    provider).ConfigureAwait(false);

            Assert.That(binding.State.OnReadValueAsync, Is.Not.Null);
            AttributeReadResult result = await binding.State.OnReadValueAsync!(
                fixture.Context,
                binding.State,
                NumericRange.Null,
                QualifiedName.Null,
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(result.Value.TryGetValue(out string text), Is.True);
            Assert.That(text, Is.EqualTo("Berlin"));
        }

        private static async Task<bool> WaitForValueAsync(
            GeoSpatialLocationState state,
            string expected,
            Isa95GeoSpatialLocationProvider provider)
        {
            for (int attempt = 0; attempt < 100; attempt++)
            {
                provider.Update(Isa95GeoSpatialLocation.Good(expected));
                if (state.Value.TryGetValue(out string text) && text == expected)
                {
                    return true;
                }
                await Task.Delay(20).ConfigureAwait(false);
            }
            return state.Value.TryGetValue(out string final) && final == expected;
        }

        private static GeoSpatialLocationState CreateLocationVariable(
            Isa95CommonTestContext fixture)
        {
            return fixture.Context.CreateInstanceOfGeoSpatialLocationType(
                fixture.Root,
                new QualifiedName("Location", fixture.InstanceNamespaceIndex));
        }

        private sealed class FaultingUpdateProvider : IIsa95GeoSpatialLocationProvider
        {
            public ValueTask<Isa95GeoSpatialLocation> GetCurrentAsync(
                CancellationToken cancellationToken = default)
            {
                return new ValueTask<Isa95GeoSpatialLocation>(
                    Isa95GeoSpatialLocation.Good("Berlin"));
            }

            public async IAsyncEnumerable<Isa95GeoSpatialLocation> SubscribeAsync(
                [EnumeratorCancellation] CancellationToken cancellationToken = default)
            {
                yield return Isa95GeoSpatialLocation.Good("Munich");
                await Task.Yield();
                throw new InvalidOperationException("stream boom");
            }
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
