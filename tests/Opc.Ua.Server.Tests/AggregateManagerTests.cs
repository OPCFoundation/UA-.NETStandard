// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Tests for <see cref="AggregateManager"/>.
    /// </summary>
    [TestFixture]
    [Category("Aggregators")]
    [Parallelizable]
    public class AggregateManagerTests
    {
        private ITelemetryContext m_telemetry;
        private Mock<IServerInternal> m_server;
        private Mock<IDiagnosticsNodeManager> m_diagnostics;

        [SetUp]
        public void SetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            m_diagnostics = new Mock<IDiagnosticsNodeManager>();
            m_diagnostics
                .Setup(d => d.AddAggregateFunctionAsync(
                    It.IsAny<NodeId>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
                .Returns(new ValueTask());
            m_server = new Mock<IServerInternal>();
            m_server.SetupGet(s => s.Telemetry).Returns(m_telemetry);
            m_server.SetupGet(s => s.DiagnosticsNodeManager).Returns(m_diagnostics.Object);
        }

        private AggregateManager CreateManager()
        {
            return new AggregateManager(m_server.Object);
        }

        [Test]
        public void IsSupportedReturnsFalseForNullAggregateId()
        {
            using AggregateManager manager = CreateManager();

            Assert.That(manager.IsSupported(NodeId.Null), Is.False);
        }

        [Test]
        public void IsSupportedReturnsFalseForUnknownAggregateId()
        {
            using AggregateManager manager = CreateManager();

            Assert.That(manager.IsSupported(new NodeId(9999)), Is.False);
        }

        [Test]
        public void MinimumProcessingIntervalRoundTrips()
        {
            using AggregateManager manager = CreateManager();

            Assert.That(manager.MinimumProcessingInterval, Is.EqualTo(1000));

            manager.MinimumProcessingInterval = 250;

            Assert.That(manager.MinimumProcessingInterval, Is.EqualTo(250));
        }

        [Test]
        public void GetDefaultConfigurationReturnsPart13Defaults()
        {
            using AggregateManager manager = CreateManager();

            AggregateConfiguration configuration = manager.GetDefaultConfiguration(NodeId.Null);

            Assert.That(configuration.TreatUncertainAsBad, Is.True);
            Assert.That(configuration.PercentDataBad, Is.EqualTo(100));
            Assert.That(configuration.PercentDataGood, Is.EqualTo(100));
            Assert.That(configuration.UseSlopedExtrapolation, Is.False);
        }

        [Test]
        public void SetDefaultConfigurationIsReturnedByGetDefaultConfiguration()
        {
            using AggregateManager manager = CreateManager();
            var custom = new AggregateConfiguration
            {
                PercentDataBad = 50,
                PercentDataGood = 60,
                TreatUncertainAsBad = false,
                UseSlopedExtrapolation = true,
                UseServerCapabilitiesDefaults = false
            };

            manager.SetDefaultConfiguration(custom);

            Assert.That(manager.GetDefaultConfiguration(NodeId.Null), Is.SameAs(custom));
        }

        [Test]
        public void CreateCalculatorReturnsNullForNullAggregateId()
        {
            using AggregateManager manager = CreateManager();

            IAggregateCalculator calculator = manager.CreateCalculator(
                NodeId.Null,
                new DateTimeUtc(2024, 1, 1, 0, 0, 0),
                new DateTimeUtc(2024, 1, 1, 0, 1, 0),
                1000,
                false,
                manager.GetDefaultConfiguration(NodeId.Null))!;

            Assert.That(calculator, Is.Null);
        }

        [Test]
        public void CreateCalculatorReturnsNullForUnknownAggregateId()
        {
            using AggregateManager manager = CreateManager();

            IAggregateCalculator calculator = manager.CreateCalculator(
                new NodeId(9999),
                new DateTimeUtc(2024, 1, 1, 0, 0, 0),
                new DateTimeUtc(2024, 1, 1, 0, 1, 0),
                1000,
                false,
                manager.GetDefaultConfiguration(NodeId.Null))!;

            Assert.That(calculator, Is.Null);
        }

        [Test]
        public async Task RegisterFactoryAsyncMakesAggregateSupportedAndCreatable()
        {
            using AggregateManager manager = CreateManager();
            NodeId aggregateId = ObjectIds.AggregateFunction_Average;
            var configuration = new AggregateConfiguration
            {
                PercentDataBad = 100,
                PercentDataGood = 100,
                TreatUncertainAsBad = false,
                UseSlopedExtrapolation = false,
                UseServerCapabilitiesDefaults = false
            };

            await manager.RegisterFactoryAsync(
                aggregateId,
                "Average",
                (a, s, e, p, stepped, c, t) => new AverageAggregateCalculator(a, s, e, p, stepped, c, t),
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(manager.IsSupported(aggregateId), Is.True);

            IAggregateCalculator calculator = manager.CreateCalculator(
                aggregateId,
                new DateTimeUtc(2024, 1, 1, 0, 0, 0),
                new DateTimeUtc(2024, 1, 1, 0, 1, 0),
                1000,
                false,
                configuration)!;

            Assert.That(calculator, Is.Not.Null);
            m_diagnostics.Verify(
                d => d.AddAggregateFunctionAsync(aggregateId, "Average", true, It.IsAny<CancellationToken>()),
                Times.Once);
        }

        [Test]
        public async Task CreateCalculatorUsesServerCapabilitiesDefaultsWhenRequested()
        {
            using AggregateManager manager = CreateManager();
            NodeId aggregateId = ObjectIds.AggregateFunction_Average;
            await manager.RegisterFactoryAsync(
                aggregateId,
                "Average",
                (a, s, e, p, stepped, c, t) => new AverageAggregateCalculator(a, s, e, p, stepped, c, t),
                CancellationToken.None).ConfigureAwait(false);

            var configuration = new AggregateConfiguration
            {
                UseServerCapabilitiesDefaults = true
            };

            IAggregateCalculator calculator = manager.CreateCalculator(
                aggregateId,
                new DateTimeUtc(2024, 1, 1, 0, 0, 0),
                new DateTimeUtc(2024, 1, 1, 0, 1, 0),
                1000,
                false,
                configuration)!;

            Assert.That(calculator, Is.Not.Null);
        }

        [Test]
        public async Task RegisterFactoryRemovesAggregateSupport()
        {
            using AggregateManager manager = CreateManager();
            NodeId aggregateId = ObjectIds.AggregateFunction_Average;
            await manager.RegisterFactoryAsync(
                aggregateId,
                "Average",
                (a, s, e, p, stepped, c, t) => new AverageAggregateCalculator(a, s, e, p, stepped, c, t),
                CancellationToken.None).ConfigureAwait(false);
            Assert.That(manager.IsSupported(aggregateId), Is.True);

            manager.RegisterFactory(aggregateId);

            Assert.That(manager.IsSupported(aggregateId), Is.False);
        }
    }
}
