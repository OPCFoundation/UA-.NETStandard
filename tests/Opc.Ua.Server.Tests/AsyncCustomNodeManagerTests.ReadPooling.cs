// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

#pragma warning disable CS0618, UA0031 // Test doubles implement or inspect the legacy IAsyncNodeManager.SyncNodeManager view.

#pragma warning disable CA2000
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies the Read into a pooled <see cref="ReadResultBuffer"/>: the same results as
    /// a Read into exact arrays, and a buffer that is returned and cleared whenever the
    /// read fails.
    /// </summary>
    public partial class AsyncCustomNodeManagerTests
    {
        /// <summary>
        /// A pooled read returns the values of a read into exact arrays, and clears the
        /// state only the dispatch uses.
        /// </summary>
        [Test]
        public async Task PooledReadMatchesAnExactReadAsync()
        {
            (MasterNodeManager dispatcher, ITestNodeManager manager, ISession session) =
                CreateReadDispatcher();
            await using MasterNodeManager disposable = dispatcher;
            NodeId nodeId = await AddPermissionVariableAsync(
                manager, "PooledRead", 21, PermissionType.Browse | PermissionType.Read).ConfigureAwait(false);
            ReadValueId[] nodesToRead = CreatePooledReadRequest(nodeId);

            (ArrayOf<DataValue> exact, _) = await ReadManyAsync(
                dispatcher, session, DiagnosticsMasks.None, CreatePooledReadRequest(nodeId)).ConfigureAwait(false);
            ReadResultBuffer buffer = ReadResultBuffer.CreateUnpooled(nodesToRead.Length);
            using var context = new OperationContext(session, DiagnosticsMasks.None);
            (ArrayOf<DataValue> pooled, ArrayOf<DiagnosticInfo> diagnostics) = await dispatcher.ReadPooledAsync(
                context, 0, TimestampsToReturn.Neither, nodesToRead, buffer).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(dispatcher.PoolsReadResults, Is.True);
                Assert.That(buffer.Count, Is.EqualTo(nodesToRead.Length), "handed back to the caller");
                Assert.That(pooled.Count, Is.EqualTo(exact.Count));
                for (int ii = 0; ii < exact.Count; ii++)
                {
                    Assert.That(pooled[ii].StatusCode, Is.EqualTo(exact[ii].StatusCode), $"value {ii}");
                    Assert.That(pooled[ii].WrappedValue, Is.EqualTo(exact[ii].WrappedValue), $"value {ii}");
                }
                Assert.That((int)pooled[0].WrappedValue, Is.EqualTo(21));
                Assert.That(pooled[3].StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
                Assert.That(diagnostics.IsNull, Is.True);
                Assert.That(buffer.Errors.Take(buffer.Count).All(error => error == null), Is.True);
                Assert.That(buffer.AttributeCache, Is.Empty);
            });
            buffer.Return();
        }

        /// <summary>
        /// A read that fails after the node manager filled the values, here because the
        /// values exceed the response size limit, returns and clears its buffer.
        /// </summary>
        [Test]
        public async Task PooledReadThatFailsMidReadReturnsItsBufferAsync()
        {
            (MasterNodeManager dispatcher, ITestNodeManager manager, ISession session) =
                CreateReadDispatcher();
            await using MasterNodeManager disposable = dispatcher;
            NodeId nodeId = await AddPermissionVariableAsync(
                manager, "PooledReadTooLarge", 22, PermissionType.Browse | PermissionType.Read).ConfigureAwait(false);
            ReadValueId[] nodesToRead = CreatePooledReadRequest(nodeId);
            ReadResultBuffer buffer = ReadResultBuffer.CreateUnpooled(nodesToRead.Length);
            DataValue[] values = buffer.Values;
            using var context = new OperationContext(session, DiagnosticsMasks.None);
            context.StartResponseBudget(1);

            ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await dispatcher.ReadPooledAsync(
                    context, 0, TimestampsToReturn.Both, nodesToRead, buffer).ConfigureAwait(false))
                .ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadResponseTooLarge));
                Assert.That(buffer.Count, Is.Zero, "returned");
                Assert.That(values.All(value => value.IsNull), Is.True, "cleared");
                Assert.That(buffer.Errors.All(value => value == null), Is.True, "cleared");
            });
        }

        /// <summary>
        /// A read rejected before the dispatch, for an invalid parameter or a buffer that
        /// does not fit the request, returns its buffer.
        /// </summary>
        [TestCase("maxAge")]
        [TestCase("timestampsToReturn")]
        [TestCase("bufferSize")]
        public async Task PooledReadRejectedBeforeTheDispatchReturnsItsBufferAsync(string rejection)
        {
            double maxAge = rejection == "maxAge" ? -1 : 0;
            TimestampsToReturn timestampsToReturn = rejection == "timestampsToReturn"
                ? (TimestampsToReturn)17
                : TimestampsToReturn.Both;
            int extraEntries = rejection == "bufferSize" ? 1 : 0;
            StatusCode expected = rejection switch
            {
                "maxAge" => StatusCodes.BadMaxAgeInvalid,
                "timestampsToReturn" => StatusCodes.BadTimestampsToReturnInvalid,
                _ => StatusCodes.Good
            };
            (MasterNodeManager dispatcher, ITestNodeManager manager, ISession session) =
                CreateReadDispatcher();
            await using MasterNodeManager disposable = dispatcher;
            NodeId nodeId = await AddPermissionVariableAsync(
                manager, "PooledReadRejected", 23, PermissionType.Browse | PermissionType.Read).ConfigureAwait(false);
            ReadValueId[] nodesToRead = CreatePooledReadRequest(nodeId);
            ReadResultBuffer buffer = ReadResultBuffer.CreateUnpooled(nodesToRead.Length + extraEntries);
            using var context = new OperationContext(session, DiagnosticsMasks.None);

            Exception? error = null;
            try
            {
                await dispatcher.ReadPooledAsync(
                    context, maxAge, timestampsToReturn, nodesToRead, buffer).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                error = e;
            }

            Assert.Multiple(() =>
            {
                if (expected == StatusCodes.Good)
                {
                    Assert.That(error, Is.TypeOf<ArgumentException>());
                }
                else
                {
                    Assert.That(error, Is.TypeOf<ServiceResultException>());
                    Assert.That(((ServiceResultException)error!).StatusCode, Is.EqualTo(expected));
                }
                Assert.That(buffer.Count, Is.Zero, "returned");
            });
        }

        /// <summary>
        /// A cancelled read either fails and returns its buffer, or completes and hands it
        /// back with the values; it never leaves a half-owned buffer.
        /// </summary>
        [Test]
        public async Task CancelledPooledReadReturnsOrHandsBackItsBufferAsync()
        {
            (MasterNodeManager dispatcher, ITestNodeManager manager, ISession session) =
                CreateReadDispatcher();
            await using MasterNodeManager disposable = dispatcher;
            NodeId nodeId = await AddPermissionVariableAsync(
                manager, "PooledReadCancelled", 24, PermissionType.Browse | PermissionType.Read).ConfigureAwait(false);
            ReadValueId[] nodesToRead = CreatePooledReadRequest(nodeId);
            ReadResultBuffer buffer = ReadResultBuffer.CreateUnpooled(nodesToRead.Length);
            DataValue[] values = buffer.Values;
            using var context = new OperationContext(session, DiagnosticsMasks.None);
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            Exception? error = null;
            ArrayOf<DataValue> results = default;
            try
            {
                (results, _) = await dispatcher.ReadPooledAsync(
                    context, 0, TimestampsToReturn.Both, nodesToRead, buffer, cts.Token).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                error = e;
            }

            if (error != null)
            {
                Assert.That(error, Is.InstanceOf<OperationCanceledException>().Or.InstanceOf<ServiceResultException>());
                Assert.That(buffer.Count, Is.Zero, "returned");
                Assert.That(values.All(value => value.IsNull), Is.True, "cleared");
            }
            else
            {
                Assert.That(buffer.Count, Is.EqualTo(nodesToRead.Length));
                Assert.That(results.Count, Is.EqualTo(nodesToRead.Length));
                buffer.Return();
            }
        }

        /// <summary>
        /// A master node manager whose Read is overridden is not read through the pool,
        /// because the override may keep the results of the base implementation.
        /// </summary>
        [Test]
        public async Task OverriddenReadIsNotPooledAsync()
        {
            m_mockServer.Setup(server => server.Telemetry).Returns(NUnitTelemetryContext.Create());
            m_mockServer.Setup(server => server.ResourceManager).Returns(new ResourceManager(m_configuration));
            var core = new Mock<ICoreNodeManager>();
            core.Setup(value => value.SyncNodeManager).Returns(Mock.Of<INodeManager>());
            var factory = new Mock<IMainNodeManagerFactory>();
            factory.Setup(value => value.CreateConfigurationNodeManager())
                .Returns(CreateUnrestrictedConfigurationNodeManager().Object);
            factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                .Returns(core.Object);
            m_mockServer.Setup(server => server.MainNodeManagerFactory).Returns(factory.Object);

            await using var overriding = new ReadOverridingMasterNodeManager(
                m_mockServer.Object, m_configuration, CreateManager());
            await using var derived = new DerivedReadMasterNodeManager(
                m_mockServer.Object, m_configuration, CreateManager());

            Assert.Multiple(() =>
            {
                Assert.That(overriding.PoolsReadResults, Is.False);
                Assert.That(derived.PoolsReadResults, Is.True);
            });
        }

        private static ReadValueId[] CreatePooledReadRequest(NodeId nodeId)
        {
            return
            [
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.Value },
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.BrowseName },
                new ReadValueId { NodeId = nodeId, AttributeId = Attributes.DisplayName },
                new ReadValueId { NodeId = new NodeId("PooledReadMissing", nodeId.NamespaceIndex), AttributeId = Attributes.Value }
            ];
        }

        private sealed class ReadOverridingMasterNodeManager : MasterNodeManager
        {
            public ReadOverridingMasterNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                ITestNodeManager manager)
                : base(server, configuration, null, manager)
            {
            }

            public int Reads { get; private set; }

            public override ValueTask<(ArrayOf<DataValue> values, ArrayOf<DiagnosticInfo> diagnosticInfos)> ReadAsync(
                OperationContext context,
                double maxAge,
                TimestampsToReturn timestampsToReturn,
                ArrayOf<ReadValueId> nodesToRead,
                CancellationToken cancellationToken = default)
            {
                Reads++;
                return base.ReadAsync(context, maxAge, timestampsToReturn, nodesToRead, cancellationToken);
            }
        }

        private sealed class DerivedReadMasterNodeManager : MasterNodeManager
        {
            public DerivedReadMasterNodeManager(
                IServerInternal server,
                ApplicationConfiguration configuration,
                ITestNodeManager manager)
                : base(server, configuration, null, manager)
            {
            }
        }
    }
}
