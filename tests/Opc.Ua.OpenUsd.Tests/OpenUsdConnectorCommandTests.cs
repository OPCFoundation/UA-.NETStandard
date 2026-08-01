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

using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Client;

namespace Opc.Ua.OpenUsd.Client.Tests
{
    /// <summary>
    /// Drives §5.10 command actuation and the §9 fail-closed authorisation checks of
    /// <see cref="OpenUsdConnector"/> against an in-memory address space.
    /// </summary>
    [TestFixture]
    [Category("OpenUsd")]
    public sealed class OpenUsdConnectorCommandTests
    {
        private FakeAddressSpace m_space = null!;
        private Mock<ISession> m_session = null!;
        private MockUsdSink m_sink = null!;
        private NodeId m_binding = NodeId.Null;
        private NodeId m_setpoint = NodeId.Null;
        private NodeId m_method = NodeId.Null;

        [SetUp]
        public void SetUp()
        {
            m_space = new FakeAddressSpace();
            m_session = FakeSession.Create(m_space);
            m_sink = new MockUsdSink();

            NodeId facility = m_space.AddObject(Opc.Ua.ObjectIds.Server, "OpenUSD",
                browseNameNamespace: m_space.OpenUsdNamespaceIndex);
            NodeId registry = m_space.AddObject(facility, "Representations");
            NodeId machine = m_space.AddObject(Opc.Ua.ObjectIds.Server, "Machine");
            m_setpoint = m_space.AddVariable(machine, "Setpoint", new Variant(0.0));
            m_method = m_space.AddMethod(machine, "Start");
            NodeId rep = m_space.AddObject(registry, "Robot",
                new NodeId(OpenUsdModel.RepresentationTypeId, m_space.OpenUsdNamespaceIndex));
            m_space.AddVariable(rep, "PrimPath", new Variant("/World/Robot"));
            m_binding = m_space.AddObject(rep, "Command",
                new NodeId(OpenUsdModel.CommandBindingTypeId, m_space.OpenUsdNamespaceIndex));
            m_space.AddVariable(m_binding, "SignalRole", new Variant(1));
        }

        private OpenUsdConnector Connector()
        {
            return new OpenUsdConnector(m_session.Object, m_sink, enableCommands: true);
        }

        private void TargetTheVariable()
        {
            m_space.AddVariable(m_binding, "CommandTargetNodeId", new Variant(m_setpoint));
        }

        private void TargetTheMethod()
        {
            m_space.AddVariable(m_binding, "CommandMethodId", new Variant(m_method));
        }

        private void GrantWrite()
        {
            m_space.SetUserAccessLevel(m_setpoint,
                new DataValue(new Variant((byte)(AccessLevels.CurrentRead | AccessLevels.CurrentWrite))));
        }

        private void SetupWrite(StatusCode result)
        {
            m_session
                .Setup(s => s.WriteAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<WriteValue>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<WriteResponse>(new WriteResponse
                {
                    ResponseHeader = new ResponseHeader(),
                    Results = [result],
                    DiagnosticInfos = []
                }));
        }

        [Test]
        public async Task IssueCommandReturnsFalseWhenNoCommandBindingIsDiscoveredAsync()
        {
            OpenUsdConnector connector = Connector();

            bool issued = await connector.IssueCommandAsync(1.0, CancellationToken.None);

            Assert.That(issued, Is.False);
        }

        [Test]
        public async Task IssueCommandReturnsFalseWhenTheConversionCannotBeInvertedAsync()
        {
            TargetTheVariable();
            m_space.AddVariable(m_binding, "Scale", new Variant(0.0));
            OpenUsdConnector connector = Connector();

            bool issued = await connector.IssueCommandAsync(1.0, CancellationToken.None);

            Assert.That(issued, Is.False);
        }

        [Test]
        public async Task IssueCommandRefusesToWriteWhenTheTargetHidesItsAccessLevelAsync()
        {
            TargetTheVariable();
            OpenUsdConnector connector = Connector();

            bool issued = await connector.IssueCommandAsync(1.0, CancellationToken.None);

            Assert.That(issued, Is.False);
        }

        [Test]
        public async Task IssueCommandRefusesToWriteWhenTheAccessLevelIsNotNumericAsync()
        {
            TargetTheVariable();
            m_space.SetUserAccessLevel(m_setpoint, new DataValue(new Variant("read-write")));
            OpenUsdConnector connector = Connector();

            bool issued = await connector.IssueCommandAsync(1.0, CancellationToken.None);

            Assert.That(issued, Is.False);
        }

        [Test]
        public async Task IssueCommandRefusesToWriteWhenCurrentWriteIsNotGrantedAsync()
        {
            TargetTheVariable();
            m_space.SetUserAccessLevel(m_setpoint, new DataValue(new Variant(AccessLevels.CurrentRead)));
            OpenUsdConnector connector = Connector();

            bool issued = await connector.IssueCommandAsync(1.0, CancellationToken.None);

            Assert.That(issued, Is.False);
        }

        [Test]
        public async Task IssueCommandRefusesToWriteWhenTheReadOfTheEffectiveRightFaultsAsync()
        {
            TargetTheVariable();
            GrantWrite();
            OpenUsdConnector connector = Connector();
            m_space.FaultingAttributes.Add(Attributes.UserAccessLevel);

            bool issued = await connector.IssueCommandAsync(1.0, CancellationToken.None);

            Assert.That(issued, Is.False);
        }

        [Test]
        public async Task IssueCommandRefusesToWriteWhenRolePermissionsWithholdWriteAsync()
        {
            TargetTheVariable();
            GrantWrite();
            m_space.SetUserRolePermissions(m_setpoint, new DataValue(new Variant(new ExtensionObject[]
            {
                new(new RolePermissionType
                {
                    RoleId = Opc.Ua.ObjectIds.WellKnownRole_Observer,
                    Permissions = (uint)PermissionType.Read
                })
            })));
            OpenUsdConnector connector = Connector();

            bool issued = await connector.IssueCommandAsync(1.0, CancellationToken.None);

            Assert.That(issued, Is.False);
        }

        [Test]
        public async Task IssueCommandWritesWhenRolePermissionsGrantWriteAsync()
        {
            TargetTheVariable();
            GrantWrite();
            m_space.SetUserRolePermissions(m_setpoint, new DataValue(new Variant(new ExtensionObject[]
            {
                new(new RolePermissionType
                {
                    RoleId = Opc.Ua.ObjectIds.WellKnownRole_Operator,
                    Permissions = (uint)(PermissionType.Read | PermissionType.Write)
                })
            })));
            SetupWrite(StatusCodes.Good);
            OpenUsdConnector connector = Connector();

            bool issued = await connector.IssueCommandAsync(2.0, CancellationToken.None);

            Assert.That(issued, Is.True);
        }

        [Test]
        public async Task IssueCommandTreatsUndecodableRolePermissionsAsPermissiveAsync()
        {
            TargetTheVariable();
            GrantWrite();
            m_space.SetUserRolePermissions(m_setpoint, new DataValue(new Variant("not-a-permission-array")));
            SetupWrite(StatusCodes.Good);
            OpenUsdConnector connector = Connector();

            bool issued = await connector.IssueCommandAsync(2.0, CancellationToken.None);

            Assert.That(issued, Is.True);
        }

        [Test]
        public async Task IssueCommandReturnsFalseWhenTheServerRejectsTheWriteAsync()
        {
            TargetTheVariable();
            GrantWrite();
            SetupWrite(StatusCodes.BadUserAccessDenied);
            OpenUsdConnector connector = Connector();

            bool issued = await connector.IssueCommandAsync(2.0, CancellationToken.None);

            Assert.That(issued, Is.False);
        }

        [Test]
        public async Task IssueCommandAppliesTheInverseOffsetAndScaleAsync()
        {
            TargetTheVariable();
            GrantWrite();
            m_space.AddVariable(m_binding, "Scale", new Variant(2.0));
            m_space.AddVariable(m_binding, "Offset", new Variant(1.0));
            WriteValue? written = null;
            m_session
                .Setup(s => s.WriteAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<WriteValue>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, ArrayOf<WriteValue> values, CancellationToken _) =>
                {
                    written = values[0];
                    return new ValueTask<WriteResponse>(new WriteResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [StatusCodes.Good],
                        DiagnosticInfos = []
                    });
                });
            OpenUsdConnector connector = Connector();

            bool issued = await connector.IssueCommandAsync(5.0, CancellationToken.None);

            Assert.That(issued, Is.True);
            Assert.That(written, Is.Not.Null);
            Assert.That(written!.Value.WrappedValue.TryGetValue(out double actual), Is.True);
            Assert.That(actual, Is.EqualTo(2.0).Within(1e-9));
        }

        [Test]
        public async Task IssueCommandRefusesToCallWhenTheMethodHidesUserExecutableAsync()
        {
            TargetTheMethod();
            OpenUsdConnector connector = Connector();

            bool issued = await connector.IssueCommandAsync(1.0, CancellationToken.None);

            Assert.That(issued, Is.False);
        }

        [Test]
        public async Task IssueCommandRefusesToCallWhenUserExecutableIsFalseAsync()
        {
            TargetTheMethod();
            m_space.SetUserExecutable(m_method, new DataValue(new Variant(false)));
            OpenUsdConnector connector = Connector();

            bool issued = await connector.IssueCommandAsync(1.0, CancellationToken.None);

            Assert.That(issued, Is.False);
        }

        [Test]
        public async Task IssueCommandRefusesToCallWhenTheReadOfUserExecutableFaultsAsync()
        {
            TargetTheMethod();
            m_space.SetUserExecutable(m_method, new DataValue(new Variant(true)));
            OpenUsdConnector connector = Connector();
            m_space.FaultingAttributes.Add(Attributes.UserExecutable);

            bool issued = await connector.IssueCommandAsync(1.0, CancellationToken.None);

            Assert.That(issued, Is.False);
        }

        [Test]
        public async Task IssueCommandCallsTheMethodOnItsAggregatingParentAsync()
        {
            TargetTheMethod();
            m_space.SetUserExecutable(m_method, new DataValue(new Variant(true)));
            CallMethodRequest? called = null;
            m_session
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, ArrayOf<CallMethodRequest> requests, CancellationToken _) =>
                {
                    called = requests[0];
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [new CallMethodResult { StatusCode = StatusCodes.Good }],
                        DiagnosticInfos = []
                    });
                });
            OpenUsdConnector connector = Connector();

            bool issued = await connector.IssueCommandAsync(3.0, CancellationToken.None);

            Assert.That(issued, Is.True);
            Assert.That(called, Is.Not.Null);
            Assert.That(called!.MethodId, Is.EqualTo(m_method));
            Assert.That(called.ObjectId.IsNull, Is.False);
        }

        [Test]
        public async Task IssueCommandReturnsFalseWhenTheCallFaultsAsync()
        {
            TargetTheMethod();
            m_space.SetUserExecutable(m_method, new DataValue(new Variant(true)));
            m_session
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Throws(new ServiceResultException(StatusCodes.BadUserAccessDenied));
            OpenUsdConnector connector = Connector();

            bool issued = await connector.IssueCommandAsync(3.0, CancellationToken.None);

            Assert.That(issued, Is.False);
        }

        [Test]
        public async Task IssueCommandUsesTheDeclaredCommandTargetAsTheMethodOwnerAsync()
        {
            TargetTheMethod();
            NodeId owner = m_space.AddObject(Opc.Ua.ObjectIds.Server, "Owner");
            m_space.AddVariable(m_binding, "CommandTargetNodeId", new Variant(owner));
            m_space.SetUserExecutable(m_method, new DataValue(new Variant(true)));
            CallMethodRequest? called = null;
            m_session
                .Setup(s => s.CallAsync(
                    It.IsAny<RequestHeader>(),
                    It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns((RequestHeader _, ArrayOf<CallMethodRequest> requests, CancellationToken _) =>
                {
                    called = requests[0];
                    return new ValueTask<CallResponse>(new CallResponse
                    {
                        ResponseHeader = new ResponseHeader(),
                        Results = [new CallMethodResult { StatusCode = StatusCodes.Good }],
                        DiagnosticInfos = []
                    });
                });
            OpenUsdConnector connector = Connector();

            bool issued = await connector.IssueCommandAsync(3.0, CancellationToken.None);

            Assert.That(issued, Is.True);
            Assert.That(called!.ObjectId, Is.EqualTo(owner));
        }

        [Test]
        public async Task IssueCommandIgnoresABindingSuppressedByItsEnabledTombstoneAsync()
        {
            TargetTheVariable();
            m_space.AddVariable(m_binding, "Enabled", new Variant(false));
            OpenUsdConnector connector = Connector();

            bool issued = await connector.IssueCommandAsync(1.0, CancellationToken.None);

            Assert.That(issued, Is.False);
        }

        [Test]
        public async Task IssueCommandIgnoresAnObservableBindingAsync()
        {
            NodeId observable = m_space.AddObject(Opc.Ua.ObjectIds.Server, "Ignored");
            Assert.That(observable.IsNull, Is.False);
            m_space.SetValue(m_binding, new DataValue(new Variant(0)));
            m_space.AddVariable(m_binding, "CommandTargetNodeId", new Variant(m_setpoint));
            m_space.SetValue(FindSignalRole(), new DataValue(new Variant(0)));
            OpenUsdConnector connector = Connector();

            bool issued = await connector.IssueCommandAsync(1.0, CancellationToken.None);

            Assert.That(issued, Is.False);
        }

        private NodeId FindSignalRole()
        {
            BrowseResponse response = m_space.Browse(
            [
                new BrowseDescription
                {
                    NodeId = m_binding,
                    BrowseDirection = BrowseDirection.Forward,
                    ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                    IncludeSubtypes = true
                }
            ]);
            ArrayOf<ReferenceDescription> refs = response.Results[0].References;
            for (int i = 0; i < refs.Count; i++)
            {
                if (refs[i].BrowseName.Name == "SignalRole")
                {
                    return ExpandedNodeId.ToNodeId(refs[i].NodeId, m_space.NamespaceUris);
                }
            }
            return NodeId.Null;
        }
    }
}
