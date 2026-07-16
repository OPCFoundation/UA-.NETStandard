/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;

#nullable enable
#pragma warning disable CA2000

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// Tests for <see cref="RuntimeValueBuilderExtensions"/> and
    /// <see cref="IValueUpdater{TValue}"/> — the runtime value-update surface
    /// that pushes changes to subscribed MonitoredItems after the fluent
    /// builder is sealed (issue #3973).
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    public class RuntimeValueBuilderExtensionsTests
    {
        private const ushort kNs = 2;

        [Test]
        public void BindThrowsOnNullBuilder()
        {
            Assert.Throws<ArgumentNullException>(
                () => RuntimeValueBuilderExtensions.Bind<double>(
                    null!, out _));
        }

        [Test]
        public void BindSetValueUpdatesNodeAndRaisesValueChange()
        {
            using var h = RuntimeHarness.Create();
            IVariableBuilder<double> vb = h.Builder.Variable<double>(h.VariableId);
            vb.Bind(out IValueUpdater<double> updater);

            Func<int> notifications = CountValueChanges(h.Variable);

            updater.SetValue(42.5);

            Assert.That(
                h.Variable.WrappedValue.TryGetValue(out double stored), Is.True);
            Assert.That(stored, Is.EqualTo(42.5));
            Assert.That(
                (uint)h.Variable.StatusCode.Code,
                Is.EqualTo((uint)StatusCodes.Good));
            Assert.That(notifications(), Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void SetValueAppliesStatusCodeAndTimestamp()
        {
            using var h = RuntimeHarness.Create();
            IVariableBuilder<double> vb = h.Builder.Variable<double>(h.VariableId);
            vb.Bind(out IValueUpdater<double> updater);

            var timestamp = new DateTime(2030, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            updater.SetValue(7.0, StatusCodes.BadNoData, timestamp);

            Assert.That(
                (uint)h.Variable.StatusCode.Code,
                Is.EqualTo((uint)StatusCodes.BadNoData));
            Assert.That(
                ((DateTime)h.Variable.Timestamp).ToUniversalTime(),
                Is.EqualTo(timestamp));
        }

        [Test]
        public void NotifyChangeRaisesValueChangeWithoutMutatingValue()
        {
            using var h = RuntimeHarness.Create();
            IVariableBuilder<double> vb = h.Builder.Variable<double>(h.VariableId);
            vb.Bind(out IValueUpdater<double> updater);
            updater.SetValue(11.0);

            Func<int> notifications = CountValueChanges(h.Variable);
            updater.NotifyChange();

            Assert.That(h.Variable.WrappedValue.TryGetValue(out double stored), Is.True);
            Assert.That(stored, Is.EqualTo(11.0));
            Assert.That(notifications(), Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void PollEveryThrowsWhenManagerIsNotFluent()
        {
            using var h = RuntimeHarness.CreateWithoutFluentBase();
            IVariableBuilder<double> vb = h.Builder.Variable<double>(h.VariableId);

            ServiceResultException ex = Assert.Throws<ServiceResultException>(
                () => vb.PollEvery(TimeSpan.FromMilliseconds(25), () => 1.0))!;
            Assert.That(
                ex.StatusCode, Is.EqualTo((uint)StatusCodes.BadConfigurationError));
        }

        [Test]
        public void PollEveryValidatesArguments()
        {
            using var h = RuntimeHarness.Create();
            IVariableBuilder<double> vb = h.Builder.Variable<double>(h.VariableId);

            Assert.Throws<ArgumentNullException>(
                () => vb.PollEvery(TimeSpan.FromMilliseconds(25), (Func<double>)null!));
            Assert.Throws<ArgumentOutOfRangeException>(
                () => vb.PollEvery(TimeSpan.Zero, () => 1.0));
        }

        [Test]
        public void PollEveryPrimesInitialValueImmediately()
        {
            using var h = RuntimeHarness.Create();
            h.Builder.Variable<double>(h.VariableId)
                .PollEvery(TimeSpan.FromMilliseconds(25), () => 3.25);

            Assert.That(h.Variable.WrappedValue.TryGetValue(out double stored), Is.True);
            Assert.That(stored, Is.EqualTo(3.25));
        }

        [Test]
        public async Task PollEveryPushesChangesToSubscribers()
        {
            using var h = RuntimeHarness.Create();
            double current = 1.0;
            h.Builder.Variable<double>(h.VariableId)
                .PollEvery(TimeSpan.FromMilliseconds(25), () => Volatile.Read(ref current));

            Func<int> notifications = CountValueChanges(h.Variable);
            h.Builder.Seal();

            Volatile.Write(ref current, 2.0);

            await WaitForAsync(
                () => h.Variable.WrappedValue.TryGetValue(out double d) && d == 2.0,
                "Sampled value change should update the node.")
                .ConfigureAwait(false);
            Assert.That(
                notifications(),
                Is.GreaterThanOrEqualTo(1),
                "A change notification should reach subscribed MonitoredItems.");
        }

        [Test]
        public async Task PollEveryDoesNotNotifyWhenValueUnchanged()
        {
            using var h = RuntimeHarness.Create();
            h.Builder.Variable<double>(h.VariableId)
                .PollEvery(TimeSpan.FromMilliseconds(25), () => 5.0);

            Func<int> notifications = CountValueChanges(h.Variable);
            h.Builder.Seal();

            // Let several ticks elapse; the constant getter must not raise
            // repeated value-change notifications.
            await Task.Delay(150).ConfigureAwait(false);
            Assert.That(notifications(), Is.Zero);
        }

        private static Func<int> CountValueChanges(BaseVariableState variable)
        {
            var box = new StrongBox<int>(0);
            NodeStateChangedHandler? previous = variable.OnStateChanged;
            variable.OnStateChanged = (ctx, node, mask) =>
            {
                previous?.Invoke(ctx, node, mask);
                if ((mask & NodeStateChangeMasks.Value) != 0)
                {
                    Interlocked.Increment(ref box.Value);
                }
            };
            return () => Volatile.Read(ref box.Value);
        }

        private static async Task WaitForAsync(
            Func<bool> condition,
            string? message = null,
            int timeoutMs = 5000)
        {
            DateTime end = DateTime.UtcNow + TimeSpan.FromMilliseconds(timeoutMs);
            while (!condition())
            {
                if (DateTime.UtcNow > end)
                {
                    Assert.Fail(
                        message ?? $"Condition not satisfied within {timeoutMs}ms.");
                }
                await Task.Delay(10).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Test harness with a resolvable <see cref="BaseDataVariableState{T}"/>
        /// and (optionally) a <see cref="FluentNodeManagerBase"/> owner so the
        /// <c>PollEvery</c> sampling loop has a simulation registry to attach to.
        /// </summary>
        private sealed class RuntimeHarness : IDisposable
        {
            internal NodeManagerBuilder Builder { get; private set; } = null!;
            internal BaseDataVariableState<double> Variable { get; private set; } = null!;
            internal NodeId VariableId { get; private set; }

            private TestFluentManager? m_manager;

            internal static RuntimeHarness Create()
            {
                var harness = new RuntimeHarness
                {
                    m_manager = new TestFluentManager()
                };
                ServerSystemContext ctx = harness.m_manager.SystemContext;
                harness.BuildVariable();
                harness.Builder = harness.CreateBuilder(ctx, harness.m_manager);
                harness.m_manager.AttachToBuilder(harness.Builder);
                return harness;
            }

            internal static RuntimeHarness CreateWithoutFluentBase()
            {
                var harness = new RuntimeHarness();
                var ctx = new SystemContext(telemetry: null!)
                {
                    NamespaceUris = CreateNamespaceTable()
                };
                harness.BuildVariable();
                harness.Builder = harness.CreateBuilder(ctx, Mock.Of<IAsyncNodeManager>());
                return harness;
            }

            private NodeManagerBuilder CreateBuilder(
                ISystemContext ctx, IAsyncNodeManager manager)
            {
                var byId = new Dictionary<NodeId, NodeState> { [Variable.NodeId] = Variable };
                return new NodeManagerBuilder(
                    ctx,
                    manager,
                    defaultNamespaceIndex: kNs,
                    rootResolver: _ => null!,
                    nodeIdResolver: id => byId.TryGetValue(id, out NodeState? n) ? n! : null!,
                    typeIdResolver: _ => []);
            }

            private void BuildVariable()
            {
                var variable = BaseDataVariableState<double>.With<VariantBuilder>(parent: null!);
                variable.NodeId = new NodeId("Root.Value", kNs);
                variable.BrowseName = new QualifiedName("Value", kNs);
                variable.DisplayName = new LocalizedText("Value");
                variable.DataType = DataTypeIds.Double;
                variable.ValueRank = ValueRanks.Scalar;
                variable.Value = 0.0;
                Variable = variable;
                VariableId = variable.NodeId;
            }

            public void Dispose()
            {
                m_manager?.Dispose();
                m_manager = null;
            }
        }

        private static NamespaceTable CreateNamespaceTable()
        {
            var ns = new NamespaceTable();
            ns.Append(Ua.Namespaces.OpcUa);
            return ns;
        }

        private sealed class TestFluentManager : FluentNodeManagerBase
        {
            public TestFluentManager()
                : base(CreateMockServer(), TestNamespaceUri)
            {
            }

            private const string TestNamespaceUri = "urn:test:runtime-value";

            private static IServerInternal CreateMockServer()
            {
                NamespaceTable ns = CreateNamespaceTable();

                var mockTelemetry = new Mock<ITelemetryContext>();
                var mock = new Mock<IServerInternal>();
                mock.SetupGet(m => m.NamespaceUris).Returns(ns);
                mock.SetupGet(m => m.Telemetry).Returns(mockTelemetry.Object);
                IServiceMessageContext msgCtx = ServiceMessageContext.Create(mockTelemetry.Object);
                mock.SetupGet(m => m.MessageContext).Returns(msgCtx);
                mock.SetupGet(m => m.DefaultSystemContext).Returns(
                    new ServerSystemContext(mock.Object));
                return mock.Object;
            }
        }
    }
}
