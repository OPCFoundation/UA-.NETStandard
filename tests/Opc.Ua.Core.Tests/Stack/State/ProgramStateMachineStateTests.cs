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

// CA2000: test code; many disposables are ownership-transferred to test fixtures or short-lived,
// making CA2000 noisy without a real leak risk. Disabled file-level for the suite.
#pragma warning disable CA2000
using System;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Core.Tests.Stack.State
{
    /// <summary>
    /// Tests that <see cref="ProgramStateMachineState"/> wires the cause
    /// methods it ships handlers for. Part 3 §5.7 defines
    /// <c>Executable</c> / <c>UserExecutable</c> as "may this Method be
    /// called right now", which for a Part 16 cause is
    /// <see cref="FiniteStateMachineState.IsCausePermitted"/>; left
    /// unwired the attributes stay at the <c>true</c> the node was
    /// constructed with and a client can only discover which causes
    /// apply by calling one and being refused.
    /// </summary>
    [TestFixture]
    [Category("ProgramStateMachineState")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ProgramStateMachineStateTests
    {
        private const ushort kNs = 2;

        private ISystemContext m_context;
        private ITelemetryContext m_telemetry;

        [OneTimeSetUp]
        protected void OneTimeSetUp()
        {
            m_telemetry = NUnitTelemetryContext.Create();
            var messageContext = ServiceMessageContext.Create(m_telemetry);
            messageContext.NamespaceUris.GetIndexOrAppend(Namespaces.OpcUa);
            m_context = new SystemContext(m_telemetry)
            {
                NamespaceUris = messageContext.NamespaceUris
            };
        }

        [OneTimeTearDown]
        protected void OneTimeTearDown()
        {
            (m_context as IDisposable)?.Dispose();
        }

        [Test]
        public void CauseMethodsReportExecutableFromTheCurrentState()
        {
            ProgramStateMachineState machine = CreateMachine(
                out MethodState start, out MethodState halt);

            // The machine starts in Ready: Start applies, Halt applies,
            // Reset (Halted only) does not.
            Assert.Multiple(() =>
            {
                Assert.That(ReadAttribute(start, Attributes.Executable), Is.True);
                Assert.That(ReadAttribute(start, Attributes.UserExecutable), Is.True);
                Assert.That(ReadAttribute(halt, Attributes.Executable), Is.True);
            });

            machine.SetState(m_context, Objects.ProgramStateMachineType_Halted);

            Assert.Multiple(() =>
            {
                Assert.That(ReadAttribute(start, Attributes.Executable), Is.False);
                Assert.That(ReadAttribute(start, Attributes.UserExecutable), Is.False);
                Assert.That(ReadAttribute(halt, Attributes.Executable), Is.False);
            });
        }

        [Test]
        public void CauseMethodsAreBoundToTheMatchingCause()
        {
            ProgramStateMachineState machine = CreateMachine(
                out MethodState start, out _);

            Assert.That(start.OnCallMethod, Is.Not.Null);

            ServiceResult result = start.OnCallMethod!(
                m_context, start, inputArguments: default, outputArguments: []);

            Assert.That(ServiceResult.IsGood(result), Is.True, $"Start must succeed: {result}");
            Assert.That(CurrentStateId(machine),
                Is.EqualTo(Objects.ProgramStateMachineType_Running));
        }

        [Test]
        public void MethodsAddedAfterCreateAreWiredOnDemand()
        {
            ProgramStateMachineState machine = CreateMachine(out _, out _);

            // Create rebuilds the child list from the type template, so
            // a method materialized afterwards has to be wired by the
            // server — the public entry point exists for exactly that.
            MethodState reset = AddMethod(machine, BrowseNames.Reset, "Machine_Reset");
            Assert.That(reset.OnReadExecutable, Is.Null);

            machine.WireCauseMethods(m_context);

            // The machine is in Ready, and Reset is a cause of Halted.
            Assert.Multiple(() =>
            {
                Assert.That(reset.OnCallMethod, Is.Not.Null);
                Assert.That(ReadAttribute(reset, Attributes.Executable), Is.False);
            });

            machine.SetState(m_context, Objects.ProgramStateMachineType_Halted);

            Assert.That(ReadAttribute(reset, Attributes.Executable), Is.True);
        }

        /// <summary>
        /// Builds a machine with two of the five optional cause methods
        /// declared, so the test also covers the "only the materialized
        /// ones are wired" half.
        /// </summary>
        private TestProgramStateMachineState CreateMachine(
            out MethodState start,
            out MethodState halt)
        {
            var machine = new TestProgramStateMachineState
            {
                NodeId = new NodeId("Machine", kNs),
                BrowseName = new QualifiedName("Machine", kNs),
                DisplayName = new LocalizedText("Machine")
            };

            machine.Create(
                m_context,
                machine.NodeId,
                machine.BrowseName,
                displayName: machine.DisplayName,
                assignNodeIds: false);

            start = machine.Start;
            halt = machine.Halt;
            return machine;
        }

        private static MethodState AddMethod(
            ProgramStateMachineState machine,
            string browseName,
            string nodeIdentifier)
        {
            // Executable / UserExecutable default to true on a freshly
            // constructed node — the very value the fix has to replace.
            var method = new MethodState(machine)
            {
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                NodeId = new NodeId(nodeIdentifier, kNs),
                BrowseName = new QualifiedName(browseName, kNs),
                DisplayName = new LocalizedText(browseName),
                Executable = true,
                UserExecutable = true
            };
            machine.AddChild(method);
            return method;
        }

        /// <summary>
        /// Stands in for a generator-emitted subclass whose model
        /// declares two of the optional cause methods. They have to be
        /// materialized in <see cref="InitializeOptionalChildren"/>
        /// because <see cref="NodeState.Create(ISystemContext, NodeId,
        /// QualifiedName, LocalizedText, bool)"/> rebuilds the child
        /// list from the type template.
        /// </summary>
        private sealed class TestProgramStateMachineState : ProgramStateMachineState
        {
            public TestProgramStateMachineState()
                : base(parent: null)
            {
            }

            public MethodState Start { get; private set; }

            public MethodState Halt { get; private set; }

            protected override void InitializeOptionalChildren(ISystemContext context)
            {
                base.InitializeOptionalChildren(context);

                Start = AddMethod(this, BrowseNames.Start, "Machine_Start");
                Halt = AddMethod(this, BrowseNames.Halt, "Machine_Halt");
            }
        }

        private bool ReadAttribute(MethodState method, uint attributeId)
        {
            var value = new DataValue();
            ServiceResult result = method.ReadAttribute(
                m_context, attributeId, default, default, ref value);

            Assert.That(ServiceResult.IsGood(result), Is.True,
                $"read of {Attributes.GetBrowseName(attributeId)} failed: {result}");
            Assert.That(value.WrappedValue.TryGetValue(out bool flag), Is.True,
                $"{Attributes.GetBrowseName(attributeId)} must read as a Boolean.");
            return flag;
        }

        private static uint CurrentStateId(ProgramStateMachineState machine)
        {
            NodeId stateNodeId = machine.CurrentState.Id.Value;
            return stateNodeId.TryGetValue(out uint id) ? id : 0;
        }
    }
}
