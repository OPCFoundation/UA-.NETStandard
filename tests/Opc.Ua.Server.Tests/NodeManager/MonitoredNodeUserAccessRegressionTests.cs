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

using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// Verifies that value changes reported through a MonitoredNode honour a user dependent
    /// UserAccessLevel of each subscriber instead of the user that caused the change.
    /// </summary>
    [TestFixture]
    [Category("NodeManager")]
    public sealed class MonitoredNodeUserAccessRegressionTests
    {
        /// <summary>
        /// A subscriber without CurrentRead receives Bad_UserAccessDenied although the writer could read.
        /// </summary>
        [Test]
        public async Task SubscriberWithoutReadAccessDoesNotReceiveWriterSnapshotAsync()
        {
            var writer = new Mock<IUserIdentity>();
            var denied = new Mock<IUserIdentity>();
            (DataValue value, ServiceResult _) = await ReportChangeAsync(
                writer.Object, denied.Object, denied.Object).ConfigureAwait(false);

            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(value.WrappedValue.IsNull, Is.True);
        }

        /// <summary>
        /// A subscriber with CurrentRead receives the value although the writer itself could not read it.
        /// </summary>
        [Test]
        public async Task SubscriberWithReadAccessReceivesValueWriterCouldNotReadAsync()
        {
            var writer = new Mock<IUserIdentity>();
            var subscriber = new Mock<IUserIdentity>();
            (DataValue value, ServiceResult _) = await ReportChangeAsync(
                writer.Object, subscriber.Object, writer.Object).ConfigureAwait(false);

            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue, Is.EqualTo(new Variant(42)));
        }

        /// <summary>
        /// The subscriber receives the value of the reported change, not a later value of the
        /// node, also when the writer could not read it.
        /// </summary>
        [Test]
        public async Task SubscriberReceivesReportedValueNotLaterValueWhenWriterCouldNotReadAsync()
        {
            var writer = new Mock<IUserIdentity>();
            var subscriber = new Mock<IUserIdentity>();
            (DataValue value, ServiceResult _) = await ReportChangeAsync(
                writer.Object, subscriber.Object, writer.Object, valueAfterReport: 999).ConfigureAwait(false);

            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            Assert.That(value.WrappedValue, Is.EqualTo(new Variant(42)));
        }

        /// <summary>
        /// A reporter context that is not a ServerSystemContext must not lend the reporter's
        /// identity to the subscriber's access check.
        /// </summary>
        [Test]
        public async Task SubscriberAccessIsCheckedWithSubscriberIdentityForPlainReporterContextAsync()
        {
            var writer = new Mock<IUserIdentity>();
            var denied = new Mock<IUserIdentity>();
            (DataValue value, ServiceResult _) = await ReportChangeAsync(
                writer.Object, denied.Object, denied.Object, plainReporterContext: true).ConfigureAwait(false);

            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(value.WrappedValue.IsNull, Is.True);
        }

        private static async Task<(DataValue, ServiceResult)> ReportChangeAsync(
            IUserIdentity writerIdentity,
            IUserIdentity subscriberIdentity,
            IUserIdentity deniedIdentity,
            int? valueAfterReport = null,
            bool plainReporterContext = false)
        {
            // holds the processing of the change until the node has changed again.
            var processingGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (valueAfterReport == null)
            {
                processingGate.SetResult(true);
            }

            Mock<IServerInternal> server = DeterministicServerMock.Create(out MonitoredItemQueueFactory queues);
            using (queues)
            {
                var node = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("userAccessNode", 1),
                    BrowseName = new QualifiedName("userAccessNode", 1),
                    DataType = DataTypeIds.Int32,
                    Value = 42,
                    AccessLevel = AccessLevels.CurrentReadOrWrite,
                    UserAccessLevel = AccessLevels.CurrentReadOrWrite
                };
                node.OnReadUserAccessLevel = (ISystemContext context, NodeState _, ref byte value) =>
                {
                    if (ReferenceEquals((context as ISessionSystemContext)?.UserIdentity, deniedIdentity))
                    {
                        value = (byte)(value & ~AccessLevels.CurrentRead);
                    }
                    return ServiceResult.Good;
                };

                var nodeManager = new Mock<IAsyncNodeManager>();
                nodeManager
                    .Setup(m => m.ValidateRolePermissionsAsync(
                        It.IsAny<OperationContext>(),
                        It.IsAny<NodeId>(),
                        It.IsAny<PermissionType>(),
                        It.IsAny<CancellationToken>()))
                    .Returns(async () =>
                    {
                        await processingGate.Task.ConfigureAwait(false);
                        return ServiceResult.Good;
                    });

                var subscriberSession = new Mock<ISession>();
                subscriberSession.SetupGet(s => s.Id).Returns(new NodeId("subscriber", 1));
                subscriberSession.SetupGet(s => s.EffectiveIdentity).Returns(subscriberIdentity);
                var item = new Mock<IDataChangeMonitoredItem2>();
                item.SetupGet(m => m.Id).Returns(1u);
                item.SetupGet(m => m.AttributeId).Returns(Attributes.Value);
                item.SetupGet(m => m.IndexRange).Returns(NumericRange.Null);
                item.SetupGet(m => m.DataEncoding).Returns(QualifiedName.Null);
                item.SetupGet(m => m.Session).Returns(subscriberSession.Object);
                item.SetupGet(m => m.EffectiveIdentity).Returns(subscriberIdentity);

                var writerSession = new Mock<ISession>();
                writerSession.SetupGet(s => s.Id).Returns(new NodeId("writer", 1));
                writerSession.SetupGet(s => s.EffectiveIdentity).Returns(writerIdentity);
                using var writerOperation = new OperationContext(
                    new RequestHeader(), null, RequestType.Write, RequestLifetime.None, writerSession.Object);
                ISystemContext writerContext = plainReporterContext
                    ? new SessionSystemContext(server.Object.Telemetry) { UserIdentity = writerIdentity }
                    : new ServerSystemContext(server.Object, writerOperation);

                var monitoredNode = new MonitoredNode2(nodeManager.Object, server.Object, node);
                monitoredNode.Add(item.Object);
                await monitoredNode.OnMonitoredNodeChangedAsync(
                    writerContext, node, NodeStateChangeMasks.Value).ConfigureAwait(false);
                if (valueAfterReport != null)
                {
                    node.Value = valueAfterReport.Value;
                    processingGate.SetResult(true);
                }
                await monitoredNode.DisposeAsync().ConfigureAwait(false);

                var queued = item.Invocations
                    .Where(i => i.Method.Name == nameof(IDataChangeMonitoredItem2.QueueValue))
                    .Select(i => ((DataValue)i.Arguments[0], (ServiceResult)i.Arguments[1]))
                    .ToList();
                Assert.That(queued, Has.Count.EqualTo(1));
                return queued[0];
            }
        }
    }
}
