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

using System.Collections.Generic;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.PubSub.Application;
using Opc.Ua.PubSub.Diagnostics;
using Opc.Ua.PubSub.Server.Internal;
using Opc.Ua.PubSub.StateMachine;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.PubSub.Server.Tests
{
    /// <summary>
    /// Coverage for <see cref="PubSubStatusBinding"/>: projection of
    /// the runtime state machine and counters onto the standard
    /// PubSubStatusType / PubSubDiagnosticsType Variables.
    /// </summary>
    [TestFixture]
    [TestSpec("9.1.10", Summary = "Status.State projection")]
    [TestSpec("9.1.11", Summary = "PubSubDiagnostics counters")]
    public class PubSubStatusBindingTests
    {
        private const uint StatusStateNodeId = 17406;
        private const uint StateOperationalByMethod = 17431;
        private const uint StateOperationalByParent = 17436;
        private const uint StateOperationalFromError = 17441;
        private const uint StatePausedByParent = 17446;
        private const uint StateDisabledByMethod = 17451;

        [Test]
        public void Bind_WhenStateVariableExists_SetsInitialValueAndCallback()
        {
            BindingContext ctx = CreateBinding(PubSubDiagnosticsExposure.None);

            ctx.Binding.Bind();

            Assert.That(ctx.Binding.StateBound, Is.True);
            Assert.That(ctx.StateVariable.OnSimpleReadValue, Is.Not.Null);
            Assert.That(ctx.Binding.BoundCounterCount, Is.Zero);
        }

        [Test]
        public void Bind_WithExposureCounters_BindsAllStandardCounters()
        {
            BindingContext ctx = CreateBinding(PubSubDiagnosticsExposure.Counters);

            ctx.Binding.Bind();

            Assert.That(ctx.Binding.BoundCounterCount, Is.EqualTo(5));
            foreach (BaseDataVariableState counter in ctx.Counters)
            {
                Assert.That(counter.OnSimpleReadValue, Is.Not.Null);
            }
        }

        [Test]
        public void StateChanged_PropagatesNewStateToVariable()
        {
            BindingContext ctx = CreateBinding(PubSubDiagnosticsExposure.None);
            ctx.Binding.Bind();

            bool transitioned = ctx.Machine.TryEnable();

            Assert.That(transitioned, Is.True);
            Variant value = ctx.StateVariable.WrappedValue;
            Assert.That(value.TryGetValue(out int stateInt), Is.True);
            Assert.That(stateInt, Is.EqualTo((int)ctx.Machine.State));
        }

        [Test]
        public void CounterCallback_ReadsCurrentCounterValue()
        {
            BindingContext ctx = CreateBinding(PubSubDiagnosticsExposure.Counters);
            ctx.Binding.Bind();

            ctx.Diagnostics.Setup(d => d.Read(PubSubDiagnosticsCounterKind.StateOperationalByMethod)).Returns(42);

            BaseDataVariableState first = ctx.Counters[0];
            Assert.That(first.OnSimpleReadValue, Is.Not.Null);
            var variant = Variant.Null;
            ServiceResult result = first.OnSimpleReadValue!(null!, first, ref variant);
            Assert.That(ServiceResult.IsGood(result), Is.True);
            Assert.That(variant.TryGetValue(out uint counterValue), Is.True);
            Assert.That(counterValue, Is.EqualTo(42u));
        }

        [Test]
        public void Dispose_ClearsCallbacksAndUnsubscribes()
        {
            BindingContext ctx = CreateBinding(PubSubDiagnosticsExposure.Counters);
            ctx.Binding.Bind();
            Assert.That(ctx.StateVariable.OnSimpleReadValue, Is.Not.Null);

            ctx.Binding.Dispose();

            Assert.That(ctx.StateVariable.OnSimpleReadValue, Is.Null);
            foreach (BaseDataVariableState counter in ctx.Counters)
            {
                Assert.That(counter.OnSimpleReadValue, Is.Null);
            }
            ctx.Binding.Dispose();
        }

        [Test]
        public void Bind_WhenStateVariableMissing_DoesNotBindStateButCountersStillWork()
        {
            var diag = new Mock<IPubSubDiagnostics>();
            var nm = new Mock<IDiagnosticsNodeManager>();
            nm.Setup(m => m.FindPredefinedNode<BaseVariableState>(It.IsAny<NodeId>())).Returns((BaseVariableState)null!);

            var machine = new PubSubStateMachine("c", PubSubComponentKind.Application, NullLogger.Instance);
            var appMock = new Mock<IPubSubApplication>();
            appMock.SetupGet(a => a.State).Returns(machine);

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            using var binding = new PubSubStatusBinding(
                appMock.Object, diag.Object, nm.Object, PubSubDiagnosticsExposure.None, telemetry);
            binding.Bind();

            Assert.That(binding.StateBound, Is.False);
            Assert.That(binding.BoundCounterCount, Is.Zero);
        }

        [Test]
        public void Constructor_NullArgs_Throw()
        {
            var diag = new Mock<IPubSubDiagnostics>();
            var nm = new Mock<IDiagnosticsNodeManager>();
            var machine = new PubSubStateMachine("c", PubSubComponentKind.Application, NullLogger.Instance);
            var appMock = new Mock<IPubSubApplication>();
            appMock.SetupGet(a => a.State).Returns(machine);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            Assert.Multiple(() =>
            {
                Assert.That(() => new PubSubStatusBinding(
                    null!, diag.Object, nm.Object, PubSubDiagnosticsExposure.None, telemetry),
                    Throws.ArgumentNullException);
                Assert.That(() => new PubSubStatusBinding(
                    appMock.Object, null!, nm.Object, PubSubDiagnosticsExposure.None, telemetry),
                    Throws.ArgumentNullException);
                Assert.That(() => new PubSubStatusBinding(
                    appMock.Object, diag.Object, null!, PubSubDiagnosticsExposure.None, telemetry),
                    Throws.ArgumentNullException);
                Assert.That(() => new PubSubStatusBinding(
                    appMock.Object, diag.Object, nm.Object, PubSubDiagnosticsExposure.None, null!),
                    Throws.ArgumentNullException);
            });
        }

        private static BindingContext CreateBinding(PubSubDiagnosticsExposure exposure)
        {
            var stateVar = new BaseDataVariableState(null)
            {
                NodeId = new NodeId(StatusStateNodeId),
                BrowseName = new QualifiedName("State")
            };
            var counters = new List<BaseDataVariableState>
            {
                NewCounter(StateOperationalByMethod),
                NewCounter(StateOperationalByParent),
                NewCounter(StateOperationalFromError),
                NewCounter(StatePausedByParent),
                NewCounter(StateDisabledByMethod)
            };

            var diagMock = new Mock<IPubSubDiagnostics>(MockBehavior.Loose);
            diagMock.Setup(d => d.Read(It.IsAny<PubSubDiagnosticsCounterKind>())).Returns(0L);

            var nm = new Mock<IDiagnosticsNodeManager>(MockBehavior.Loose);
            nm.Setup(m => m.FindPredefinedNode<BaseVariableState>(new NodeId(StatusStateNodeId))).Returns(stateVar);
            nm.Setup(m => m.FindPredefinedNode<BaseVariableState>(new NodeId(StateOperationalByMethod))).Returns(counters[0]);
            nm.Setup(m => m.FindPredefinedNode<BaseVariableState>(new NodeId(StateOperationalByParent))).Returns(counters[1]);
            nm.Setup(m => m.FindPredefinedNode<BaseVariableState>(new NodeId(StateOperationalFromError))).Returns(counters[2]);
            nm.Setup(m => m.FindPredefinedNode<BaseVariableState>(new NodeId(StatePausedByParent))).Returns(counters[3]);
            nm.Setup(m => m.FindPredefinedNode<BaseVariableState>(new NodeId(StateDisabledByMethod))).Returns(counters[4]);

            var machine = new PubSubStateMachine("comp", PubSubComponentKind.Application, NullLogger.Instance);
            var appMock = new Mock<IPubSubApplication>();
            appMock.SetupGet(a => a.State).Returns(machine);

            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var binding = new PubSubStatusBinding(
                appMock.Object, diagMock.Object, nm.Object, exposure, telemetry);

            return new BindingContext(binding, stateVar, diagMock, counters, machine);
        }

        private static BaseDataVariableState NewCounter(uint nodeId)
        {
            return new BaseDataVariableState(null)
            {
                NodeId = new NodeId(nodeId),
                BrowseName = new QualifiedName("Counter_" + nodeId)
            };
        }

        private sealed class BindingContext
        {
            public BindingContext(
                PubSubStatusBinding binding,
                BaseDataVariableState stateVariable,
                Mock<IPubSubDiagnostics> diagnostics,
                IList<BaseDataVariableState> counters,
                PubSubStateMachine machine)
            {
                Binding = binding;
                StateVariable = stateVariable;
                Diagnostics = diagnostics;
                Counters = counters;
                Machine = machine;
            }

            public PubSubStatusBinding Binding { get; }
            public BaseDataVariableState StateVariable { get; }
            public Mock<IPubSubDiagnostics> Diagnostics { get; }
            public IList<BaseDataVariableState> Counters { get; }
            public PubSubStateMachine Machine { get; }
        }
    }
}
