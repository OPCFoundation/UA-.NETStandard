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

#pragma warning disable CA2000
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{
    public partial class AsyncCustomNodeManagerTests
    {
        /// <summary>
        /// A write through the dispatcher writes the nodes the session may write, denies the
        /// others, and keeps one diagnostics entry per value, alone and with other values.
        /// </summary>
        [Test]
        public async Task WriteResultsAndDiagnosticsAreKeptPerValueAsync()
        {
            (MasterNodeManager dispatcher, ITestNodeManager manager, ISession session) =
                CreateReadDispatcher();
            await using MasterNodeManager disposable = dispatcher;
            NodeId allowed = await AddWritableVariableAsync(
                manager, "WriteAllowed", 1, PermissionType.Browse | PermissionType.Read | PermissionType.Write)
                .ConfigureAwait(false);
            NodeId denied = await AddWritableVariableAsync(
                manager, "WriteDenied", 2, PermissionType.Browse | PermissionType.Read)
                .ConfigureAwait(false);
            var unknown = new NodeId("WriteUnknown", manager.NamespaceIndexes[0]);

            (ArrayOf<StatusCode> single, ArrayOf<DiagnosticInfo> singleDiagnostics) = await WriteManyAsync(
                dispatcher, session, DiagnosticsMasks.OperationAll, WriteInt32(allowed, 10)).ConfigureAwait(false);
            int afterSingle = ReadInt32(manager, allowed);
            (ArrayOf<StatusCode> many, ArrayOf<DiagnosticInfo> diagnostics) = await WriteManyAsync(
                dispatcher,
                session,
                DiagnosticsMasks.OperationAll,
                WriteInt32(allowed, 20),
                WriteInt32(denied, 21),
                WriteInt32(unknown, 22)).ConfigureAwait(false);
            (_, ArrayOf<DiagnosticInfo> none) = await WriteManyAsync(
                dispatcher, session, DiagnosticsMasks.None, WriteInt32(denied, 23)).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(single.Count, Is.EqualTo(1));
                Assert.That(single[0], Is.EqualTo(StatusCodes.Good));
                Assert.That(singleDiagnostics.IsNull, Is.True);
                Assert.That(afterSingle, Is.EqualTo(10));
                Assert.That(many.Count, Is.EqualTo(3));
                Assert.That(many[0], Is.EqualTo(StatusCodes.Good));
                Assert.That(many[1], Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(many[2], Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(diagnostics.Count, Is.EqualTo(3));
                Assert.That(diagnostics[0], Is.Null);
                Assert.That(diagnostics[1], Is.Not.Null);
                Assert.That(diagnostics[2], Is.Not.Null);
                Assert.That(none.IsNull, Is.True);
                Assert.That(ReadInt32(manager, allowed), Is.EqualTo(20));
                Assert.That(ReadInt32(manager, denied), Is.EqualTo(2));
            });
        }

        /// <summary>
        /// A node manager with the default handle lookup that is asked first for a namespace
        /// finds its in-memory nodes without a handle, for the validation and for the Read or
        /// Write: no handle is routed.
        /// </summary>
        [Test]
        public async Task DefaultHandleLookupFindsTheNodeWithoutAHandleAsync()
        {
            // the second namespace is served by this node manager only.
            var manager = new TestableAsyncCustomNodeManager(
                m_mockServer.Object,
                m_configuration,
                m_useSamplingGroups,
                m_mockLogger.Object,
                m_testNamespaceUri,
                m_testNamespaceUri + "/DefaultLookup");
            SetupMasterNodeManager(manager);
            (MasterNodeManager dispatcher, _, ISession session) = CreateReadDispatcher(manager);
            await using MasterNodeManager disposable = dispatcher;
            ushort ns = manager.NamespaceIndexes[1];
            NodeId nodeId = await AddWritableVariableAsync(
                manager, "DefaultLookup", 1, PermissionType.Browse | PermissionType.Read | PermissionType.Write, ns)
                .ConfigureAwait(false);
            NodeId denied = await AddWritableVariableAsync(
                manager, "DefaultLookupDenied", 2, PermissionType.Browse | PermissionType.Read, ns)
                .ConfigureAwait(false);

            WriteValue writeValue = WriteInt32(nodeId, 5);
            WriteValue deniedValue = WriteInt32(denied, 6);
            (ArrayOf<StatusCode> results, _) = await WriteManyAsync(
                dispatcher, session, DiagnosticsMasks.None, writeValue, deniedValue).ConfigureAwait(false);
            (ArrayOf<StatusCode> single, _) = await WriteManyAsync(
                dispatcher, session, DiagnosticsMasks.None, WriteInt32(denied, 7)).ConfigureAwait(false);
            var readValueId = new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value };
            DataValue value = await ReadAsync(dispatcher, session, readValueId).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(manager.FindNodeWithDefaultLookup(nodeId), Is.SameAs(manager.PredefinedNodes[nodeId]));
                Assert.That(manager.FindNodeWithDefaultLookup(new NodeId("DefaultLookupUnknown", ns)), Is.Null);
                Assert.That(manager.FindNodeWithDefaultLookup(new NodeId("DefaultLookup", 0)), Is.Null);
                Assert.That(results[0], Is.EqualTo(StatusCodes.Good));
                Assert.That(results[1], Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(single[0], Is.EqualTo(StatusCodes.BadUserAccessDenied));
                Assert.That(writeValue.Handle, Is.Null);
                Assert.That(ReadInt32(manager, nodeId), Is.EqualTo(5));
                Assert.That(ReadInt32(manager, denied), Is.EqualTo(2));
                Assert.That((int)value.WrappedValue, Is.EqualTo(5));
                Assert.That(readValueId.Handle, Is.Null);
            });
        }

        /// <summary>
        /// A node manager that overrides the handle lookup resolves each node once per Read
        /// or Write: the request validation routes the handle it resolved to the operation.
        /// </summary>
        [Test]
        public async Task OverriddenHandleLookupRoutesTheValidatedHandleAsync()
        {
            var manager = new HandleLookupTestNodeManager(
                m_mockServer.Object,
                m_configuration,
                m_useSamplingGroups,
                m_mockLogger.Object,
                m_testNamespaceUri);
            SetupMasterNodeManager(manager);
            (MasterNodeManager dispatcher, _, ISession session) = CreateReadDispatcher(manager);
            await using MasterNodeManager disposable = dispatcher;
            NodeId nodeId = await AddWritableVariableAsync(
                manager, "OverriddenLookup", 1, PermissionType.Browse | PermissionType.Read | PermissionType.Write)
                .ConfigureAwait(false);

            WriteValue writeValue = WriteInt32(nodeId, 7);
            int before = manager.HandleLookups;
            (ArrayOf<StatusCode> results, _) = await WriteManyAsync(
                dispatcher, session, DiagnosticsMasks.None, writeValue).ConfigureAwait(false);
            int writeLookups = manager.HandleLookups - before;

            var readValueId = new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value };
            before = manager.HandleLookups;
            DataValue value = await ReadAsync(dispatcher, session, readValueId).ConfigureAwait(false);
            int readLookups = manager.HandleLookups - before;

            Assert.Multiple(() =>
            {
                Assert.That(manager.FindNodeWithDefaultLookup(nodeId), Is.Null);
                Assert.That(results[0], Is.EqualTo(StatusCodes.Good));
                Assert.That(ReadInt32(manager, nodeId), Is.EqualTo(7));
                Assert.That(writeValue.Handle, Is.InstanceOf<NodeHandle>());
                Assert.That(((NodeHandle)writeValue.Handle).RoutedBy, Is.SameAs(manager));
                Assert.That(((NodeHandle)writeValue.Handle).Node, Is.SameAs(manager.PredefinedNodes[nodeId]));
                Assert.That(writeLookups, Is.EqualTo(1));
                Assert.That((int)value.WrappedValue, Is.EqualTo(7));
                Assert.That(readValueId.Handle, Is.InstanceOf<NodeHandle>());
                Assert.That(readLookups, Is.EqualTo(1));
            });
        }

        /// <summary>
        /// A write value that is written again does not write through the handle routed for
        /// an earlier write of another node.
        /// </summary>
        [Test]
        public async Task RoutedHandleOfAnEarlierWriteIsNotReusedAsync()
        {
            var manager = new HandleLookupTestNodeManager(
                m_mockServer.Object,
                m_configuration,
                m_useSamplingGroups,
                m_mockLogger.Object,
                m_testNamespaceUri);
            SetupMasterNodeManager(manager);
            (MasterNodeManager dispatcher, _, ISession session) = CreateReadDispatcher(manager);
            await using MasterNodeManager disposable = dispatcher;
            NodeId first = await AddWritableVariableAsync(
                manager, "RoutedWriteFirst", 1, PermissionType.Browse | PermissionType.Read | PermissionType.Write)
                .ConfigureAwait(false);
            NodeId second = await AddWritableVariableAsync(
                manager, "RoutedWriteSecond", 2, PermissionType.Browse | PermissionType.Read | PermissionType.Write)
                .ConfigureAwait(false);

            WriteValue writeValue = WriteInt32(first, 11);
            (ArrayOf<StatusCode> results, _) = await WriteManyAsync(
                dispatcher, session, DiagnosticsMasks.None, writeValue).ConfigureAwait(false);
            Assert.That(results[0], Is.EqualTo(StatusCodes.Good));

            writeValue.NodeId = second;
            writeValue.Value = new DataValue(new Variant(12));
            (results, _) = await WriteManyAsync(
                dispatcher, session, DiagnosticsMasks.None, writeValue).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(results[0], Is.EqualTo(StatusCodes.Good));
                Assert.That(ReadInt32(manager, first), Is.EqualTo(11));
                Assert.That(ReadInt32(manager, second), Is.EqualTo(12));
            });
        }

        private static WriteValue WriteInt32(NodeId nodeId, int value)
        {
            return new WriteValue
            {
                NodeId = nodeId,
                AttributeId = Attributes.Value,
                Value = new DataValue(new Variant(value))
            };
        }

        private static int ReadInt32(ITestNodeManager manager, NodeId nodeId)
        {
            return (int)((BaseDataVariableState)manager.PredefinedNodes[nodeId]).Value;
        }

        private static async Task<NodeId> AddWritableVariableAsync(
            ITestNodeManager manager,
            string name,
            int value,
            PermissionType readerPermissions,
            ushort? namespaceIndex = null)
        {
            ServerSystemContext context = manager.SystemContext;
            ushort nsIdx = namespaceIndex ?? manager.NamespaceIndexes[0];
            var variable = new BaseDataVariableState(null);
            variable.CreateAsPredefinedNode(context);
            variable.NodeId = new NodeId(name, nsIdx);
            variable.BrowseName = new QualifiedName(name, nsIdx);
            variable.DataType = DataTypeIds.Int32;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.Value = value;
            variable.RolePermissions =
            [
                new RolePermissionType { RoleId = s_readerRoleId, Permissions = (uint)readerPermissions }
            ];
            await manager.AddNodeAsync(context, default, variable).ConfigureAwait(false);
            return variable.NodeId;
        }

        private static async Task<(ArrayOf<StatusCode>, ArrayOf<DiagnosticInfo>)> WriteManyAsync(
            MasterNodeManager dispatcher,
            ISession session,
            DiagnosticsMasks diagnosticsMask,
            params WriteValue[] nodesToWrite)
        {
            using var context = new OperationContext(session, diagnosticsMask);
            return await dispatcher.WriteAsync(context, nodesToWrite).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// A node manager that overrides the handle lookup and counts the handles it resolves.
    /// </summary>
    public sealed class HandleLookupTestNodeManager : TestableAsyncCustomNodeManager
    {
        /// <summary>
        /// Creates the node manager.
        /// </summary>
        public HandleLookupTestNodeManager(
            IServerInternal server,
            ApplicationConfiguration configuration,
            bool useSamplingGroups,
            ILogger logger,
            params string[] namespaceUris)
            : base(server, configuration, useSamplingGroups, logger, namespaceUris)
        {
        }

        /// <summary>
        /// The number of handles resolved for nodes of the node manager.
        /// </summary>
        public int HandleLookups => Volatile.Read(ref m_handleLookups);

        /// <inheritdoc/>
        protected override async ValueTask<NodeHandle> GetManagerHandleAsync(
            ServerSystemContext context,
            NodeId nodeId,
            IDictionary<NodeId, NodeState> cache,
            CancellationToken cancellationToken = default)
        {
            NodeHandle handle = await base.GetManagerHandleAsync(
                context, nodeId, cache, cancellationToken).ConfigureAwait(false);
            if (handle != null)
            {
                Interlocked.Increment(ref m_handleLookups);
            }
            return handle!;
        }

        private int m_handleLookups;
    }
}
