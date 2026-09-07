/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a copy
 * of this software and associated documentation files (the "Software"), to deal
 * in the Software without restriction, including without limitation the rights
 * to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
 * copies of the Software, and to permit persons to whom the Software is
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

#nullable enable

using System;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Types.Tests.State
{
    /// <summary>
    /// Pins factory ownership and logging behavior at the protected variable initialization seam.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Category("NodeStateLogger")]
    public sealed class NodeStateLoggerTests
    {
        [Test]
        public void InitializeRetainsTheExactFactoryLoggerWithoutATypedWrapper()
        {
            var logger = new Mock<ILogger>(MockBehavior.Strict);
            var factory = new Mock<ILoggerFactory>(MockBehavior.Strict);
            factory.Setup(f => f.CreateLogger(kCategory)).Returns(logger.Object);
            var node = new LoggerVariable();

            node.InitializeLogger(CreateTelemetry(factory.Object));

            Assert.That(GetLogger(node), Is.SameAs(logger.Object));
            Assert.That(GetLogger(node), Is.Not.InstanceOf<ILogger<BaseVariableState>>());
            factory.Verify(f => f.CreateLogger(kCategory), Times.Once);
            factory.VerifyNoOtherCalls();
            logger.VerifyNoOtherCalls();
        }

        [Test]
        public void FactoryCacheIsSharedAcrossNodesAndContextsIncludingReinitialization()
        {
            using var provider = new RecordingLoggerProvider();
            using ILoggerFactory factory = LoggerFactory.Create(b => b.AddProvider(provider));
            ITelemetryContext firstContext = CreateTelemetry(factory);
            ITelemetryContext secondContext = CreateTelemetry(factory);
            var first = new LoggerVariable();
            var second = new LoggerVariable();

            first.InitializeLogger(firstContext);
            second.InitializeLogger(secondContext);
            ILogger expected = factory.CreateLogger(kCategory);
            Assert.That(GetLogger(first), Is.SameAs(expected));
            Assert.That(GetLogger(second), Is.SameAs(expected));
            first.InitializeLogger(secondContext);
            Assert.That(GetLogger(first), Is.SameAs(expected));

            WriteProbe(GetLogger(first), "first");
            WriteProbe(GetLogger(second), "second");
            Assert.That(provider.Records, Has.Count.EqualTo(2));
            Assert.That(provider.Records[0].CategoryName, Is.EqualTo(kCategory));
            Assert.That(provider.Records[0].Message, Is.EqualTo("first"));
            Assert.That(provider.Records[1].CategoryName, Is.EqualTo(kCategory));
            Assert.That(provider.Records[1].Message, Is.EqualTo("second"));
        }

        [Test]
        public void NonCachingFactoryRemainsAuthoritativeOnEveryInitialization()
        {
            var firstLogger = new Mock<ILogger>(MockBehavior.Strict);
            var secondLogger = new Mock<ILogger>(MockBehavior.Strict);
            var thirdLogger = new Mock<ILogger>(MockBehavior.Strict);
            var factory = new Mock<ILoggerFactory>(MockBehavior.Strict);
            factory.SetupSequence(f => f.CreateLogger(kCategory))
                .Returns(firstLogger.Object)
                .Returns(secondLogger.Object)
                .Returns(thirdLogger.Object);
            ITelemetryContext telemetry = CreateTelemetry(factory.Object);
            var first = new LoggerVariable();
            var second = new LoggerVariable();

            first.InitializeLogger(telemetry);
            second.InitializeLogger(telemetry);
            Assert.That(GetLogger(first), Is.SameAs(firstLogger.Object));
            Assert.That(GetLogger(second), Is.SameAs(secondLogger.Object));
            first.InitializeLogger(telemetry);
            Assert.That(GetLogger(first), Is.SameAs(thirdLogger.Object));
            Assert.That(GetLogger(second), Is.SameAs(secondLogger.Object));
            factory.Verify(f => f.CreateLogger(kCategory), Times.Exactly(3));
            factory.VerifyNoOtherCalls();
        }

        [Test]
        public void IndependentFactoriesKeepTheirProvidersAndReinitializationSwitchesOnlyOneNode()
        {
            using var firstProvider = new RecordingLoggerProvider();
            using var secondProvider = new RecordingLoggerProvider();
            using ILoggerFactory firstFactory = LoggerFactory.Create(b => b.AddProvider(firstProvider));
            using ILoggerFactory secondFactory = LoggerFactory.Create(b => b.AddProvider(secondProvider));
            ITelemetryContext firstContext = CreateTelemetry(firstFactory);
            ITelemetryContext secondContext = CreateTelemetry(secondFactory);
            var first = new LoggerVariable();
            var second = new LoggerVariable();
            first.InitializeLogger(firstContext);
            second.InitializeLogger(secondContext);

            Assert.That(GetLogger(first), Is.Not.SameAs(GetLogger(second)));
            WriteProbe(GetLogger(first), "before");
            WriteProbe(GetLogger(second), "independent");
            first.InitializeLogger(secondContext);
            Assert.That(GetLogger(first), Is.SameAs(secondFactory.CreateLogger(kCategory)));
            Assert.That(GetLogger(second), Is.SameAs(GetLogger(first)));
            WriteProbe(GetLogger(first), "after");
            Assert.That(firstProvider.Records, Has.Count.EqualTo(1));
            Assert.That(firstProvider.Records[0].Message, Is.EqualTo("before"));
            Assert.That(secondProvider.Records, Has.Count.EqualTo(2));
            Assert.That(secondProvider.Records[0].Message, Is.EqualTo("independent"));
            Assert.That(secondProvider.Records[1].Message, Is.EqualTo("after"));
            Assert.That(secondProvider.Records[1].CategoryName, Is.EqualTo(kCategory));
        }

        [Test]
        public void FactoryFailurePropagatesAndDoesNotReplaceThePreviousLogger()
        {
            var failure = new InvalidOperationException("factory failed");
            var factory = new Mock<ILoggerFactory>(MockBehavior.Strict);
            factory.SetupSequence(f => f.CreateLogger(kCategory))
                .Returns(NullLogger.Instance)
                .Throws(failure);
            ITelemetryContext telemetry = CreateTelemetry(factory.Object);
            var node = new LoggerVariable();
            node.InitializeLogger(telemetry);

            Assert.That(Assert.Throws<InvalidOperationException>(() => node.InitializeLogger(telemetry)),
                Is.SameAs(failure));
            Assert.That(GetLogger(node), Is.SameAs(NullLogger.Instance));
            factory.Verify(f => f.CreateLogger(kCategory), Times.Exactly(2));
            factory.VerifyNoOtherCalls();
        }

        [TestCase(false)]
        [TestCase(true)]
        public void MissingTelemetryOrFactoryUsesTheExistingFallback(bool missingFactory)
        {
            ITelemetryContext? telemetry = missingFactory ? CreateTelemetry(null!) : null;
            ILoggerFactory fallback = telemetry.GetLoggerFactory();
            var previousLogger = new Mock<ILogger>(MockBehavior.Strict);
            var factory = new Mock<ILoggerFactory>(MockBehavior.Strict);
            factory.Setup(f => f.CreateLogger(kCategory)).Returns(previousLogger.Object);
            var node = new LoggerVariable();
            node.InitializeLogger(CreateTelemetry(factory.Object));
            Assert.That(GetLogger(node), Is.SameAs(previousLogger.Object));

            node.InitializeLogger(telemetry!);

            Assert.That(GetLogger(node), Is.Not.SameAs(previousLogger.Object));
            Assert.That(GetLogger(node), Is.SameAs(fallback.CreateLogger(kCategory)));
            Assert.That(GetLogger(node).IsEnabled(LogLevel.Error),
                Is.EqualTo(fallback.CreateLogger(kCategory).IsEnabled(LogLevel.Error)));
            factory.Verify(f => f.CreateLogger(kCategory), Times.Once);
            factory.VerifyNoOtherCalls();
        }

        [Test]
        public void FactoryLoggerForwardsScopesStateEventAndExceptionWithoutChangingIdentity()
        {
            var logger = new Mock<ILogger>(MockBehavior.Strict);
            var scope = new Mock<IDisposable>(MockBehavior.Strict);
            var provider = new Mock<ILoggerProvider>(MockBehavior.Strict);
            var exception = new InvalidOperationException("probe failure");
            var eventId = new EventId(731, "LoggerProbe");
            Func<string, Exception?, string> formatter = static (state, error) => $"{state}: {error!.Message}";
            logger.Setup(l => l.IsEnabled(LogLevel.Warning)).Returns(true);
            logger.Setup(l => l.BeginScope("node-scope")).Returns(scope.Object);
            logger.Setup(l => l.Log(LogLevel.Warning, eventId, "payload", exception, formatter));
            scope.Setup(s => s.Dispose());
            provider.Setup(p => p.CreateLogger(kCategory)).Returns(logger.Object);
            using ILoggerFactory factory = LoggerFactory.Create(b => b
                .SetMinimumLevel(LogLevel.Warning)
                .AddProvider(provider.Object));
            var node = new LoggerVariable();
            node.InitializeLogger(CreateTelemetry(factory));
            ILogger stored = GetLogger(node);

            Assert.That(stored.IsEnabled(LogLevel.Information), Is.False);
            Assert.That(stored.IsEnabled(LogLevel.Warning), Is.True);
            using (stored.BeginScope("node-scope"))
            {
                stored.Log(LogLevel.Warning, eventId, "payload", exception, formatter);
            }

            provider.Verify(p => p.CreateLogger(kCategory), Times.Once);
            logger.Verify(l => l.BeginScope("node-scope"), Times.Once);
            logger.Verify(l => l.Log(LogLevel.Warning, eventId, "payload", exception, formatter), Times.Once);
            scope.Verify(s => s.Dispose(), Times.Once);
            logger.Verify(l => l.IsEnabled(LogLevel.Warning), Times.Once);
            logger.VerifyNoOtherCalls();
        }

        [TestCase(LogLevel.Error, true)]
        [TestCase(LogLevel.Critical, false)]
        [TestCase(LogLevel.None, false)]
        public void ExportFailurePreservesGeneratedLogContractAndFactoryFiltering(LogLevel minimum, bool enabled)
        {
            using var provider = new RecordingLoggerProvider();
            using ILoggerFactory factory = LoggerFactory.Create(b => b
                .SetMinimumLevel(minimum)
                .AddProvider(provider));
            ITelemetryContext telemetry = CreateTelemetry(factory);
            var failure = new InvalidOperationException("copy failed");
            var value = new Mock<IEncodeable>();
            value.Setup(v => v.Clone()).Throws(failure);
            var node = new LoggerVariable
            {
                NodeId = new NodeId(42u, 2),
                Value = new ExtensionObject(value.Object)
            };
            node.InitializeLogger(telemetry);
            var exported = new VariableNode();

            node.ExportTo(new SystemContext(telemetry), exported);

            value.Verify(v => v.Clone(), Times.Once);
            Assert.That(exported.NodeId, Is.EqualTo(new NodeId(42u, 2)));
            Assert.That(exported.Value.IsNull, Is.True);
            Assert.That(provider.Records, Has.Count.EqualTo(enabled ? 1 : 0));
            if (enabled)
            {
                RecordedLogRecord record = provider.Records[0];
                Assert.That(record.CategoryName, Is.EqualTo(kCategory));
                Assert.That(record.LogLevel, Is.EqualTo(LogLevel.Error));
                Assert.That(record.EventId.Id, Is.Zero);
                Assert.That(record.EventId.Name, Is.EqualTo("ExportNodeError"));
                Assert.That(record.Exception, Is.SameAs(failure));
                Assert.That(record.Message, Is.EqualTo("Unexpected error exporting node"));
                Assert.That(record.Properties, Has.Count.EqualTo(1));
                Assert.That(record.Properties["{OriginalFormat}"], Is.EqualTo("Unexpected error exporting node"));
            }
        }

        [Test]
        public void BorrowedDependencyInjectionFactoryOutlivesScopesAndIsDisposedOnlyByItsOwner()
        {
            using var recording = new RecordingLoggerProvider();
            var provider = new Mock<ILoggerProvider>(MockBehavior.Strict);
            provider.Setup(p => p.CreateLogger(kCategory)).Returns(recording.CreateLogger(kCategory));
            provider.Setup(p => p.Dispose());
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<ILoggerProvider>(_ => provider.Object);
            using ServiceProvider owner = services.BuildServiceProvider();
            ILoggerFactory factory = owner.GetRequiredService<ILoggerFactory>();
            var node = new LoggerVariable();
            using (IServiceScope scope = owner.CreateScope())
            {
                ILoggerFactory borrowed = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
                Assert.That(borrowed, Is.SameAs(factory));
                node.InitializeLogger(CreateTelemetry(borrowed));
                WriteProbe(GetLogger(node), "inside");
            }
            node.InitializeLogger(CreateTelemetry(factory));
            WriteProbe(GetLogger(node), "after scope");
            Assert.That(GetLogger(node), Is.SameAs(factory.CreateLogger(kCategory)));
            Assert.That(recording.Records, Has.Count.EqualTo(2));
            Assert.That(recording.Records[0].Message, Is.EqualTo("inside"));
            Assert.That(recording.Records[1].Message, Is.EqualTo("after scope"));
            provider.Verify(p => p.Dispose(), Times.Never);

            owner.Dispose();

            provider.Verify(p => p.Dispose(), Times.Once);
            Assert.Throws<ObjectDisposedException>(() => factory.CreateLogger(kCategory));
        }

        [Test]
        public void PublicTypedExtensionStillCreatesDistinctTypedWrappersAndDelegatesToTheFactory()
        {
            var logger = new Mock<ILogger>(MockBehavior.Strict);
            var factory = new Mock<ILoggerFactory>(MockBehavior.Strict);
            var eventId = new EventId(732, "TypedProbe");
            var exception = new InvalidOperationException("typed failure");
            Func<string, Exception?, string> formatter = static (state, _) => state;
            logger.Setup(l => l.Log(LogLevel.Error, eventId, "typed", exception, formatter));
            factory.Setup(f => f.CreateLogger(kCategory)).Returns(logger.Object);
            ITelemetryContext telemetry = CreateTelemetry(factory.Object);

            ILogger<BaseVariableState> first = telemetry.CreateLogger<BaseVariableState>();
            ILogger<BaseVariableState> second = telemetry.CreateLogger<BaseVariableState>();

            Assert.That(first, Is.TypeOf<Logger<BaseVariableState>>());
            Assert.That(second, Is.TypeOf<Logger<BaseVariableState>>());
            Assert.That(first, Is.Not.SameAs(second));
            Assert.That(first, Is.Not.SameAs(logger.Object));
            first.Log(LogLevel.Error, eventId, "typed", exception, formatter);
            second.Log(LogLevel.Error, eventId, "typed", exception, formatter);
            factory.Verify(f => f.CreateLogger(kCategory), Times.Exactly(2));
            logger.Verify(l => l.Log(LogLevel.Error, eventId, "typed", exception, formatter), Times.Exactly(2));
            factory.VerifyNoOtherCalls();
            logger.VerifyNoOtherCalls();
        }

        private static ITelemetryContext CreateTelemetry(ILoggerFactory factory)
        {
            var telemetry = new Mock<ITelemetryContext>(MockBehavior.Strict);
            telemetry.SetupGet(t => t.LoggerFactory).Returns(factory);
            return telemetry.Object;
        }

        private static ILogger GetLogger(BaseVariableState node)
        {
            // Managed-only storage identity check: observable forwarding alone cannot detect a typed wrapper.
            FieldInfo field = typeof(BaseVariableState).GetField("m_logger", BindingFlags.Instance |
                BindingFlags.NonPublic) ?? throw new InvalidOperationException("Variable logger field not found.");
            return field.GetValue(node) as ILogger
                ?? throw new InvalidOperationException("Variable logger is not initialized.");
        }

        private static void WriteProbe(ILogger logger, string message)
        {
            logger.Log(LogLevel.Warning, new EventId(730, "ProviderProbe"), message, null,
                static (state, _) => state);
        }

        private const string kCategory = "Opc.Ua.BaseVariableState";

        private sealed class LoggerVariable : BaseDataVariableState
        {
            public LoggerVariable()
                : base(null)
            {
            }

            public void InitializeLogger(ITelemetryContext telemetry)
            {
                Initialize(telemetry);
            }

            public void ExportTo(ISystemContext context, VariableNode node)
            {
                Export(context, node);
            }
        }
    }
}
