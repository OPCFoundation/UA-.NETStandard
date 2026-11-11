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

using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Verifies the response byte budget of the Call and Browse services: results are
    /// charged against the message size limit while they are built. The services start
    /// the budget with the message size limit of the server.
    /// </summary>
    public partial class AsyncCustomNodeManagerTests
    {
        /// <summary>
        /// A Call whose outputs exceed the message size limit fails with
        /// BadResponseTooLarge once the limit is passed, without running the
        /// remaining operations or keeping their outputs.
        /// </summary>
        [Test]
        public async Task CallStopsOnceTheOutputsExceedTheResponseSizeLimitAsync()
        {
            const int maxMessageSize = 16 * 1024;
            (MasterNodeManager dispatcher, BaseObjectState owner, MethodState method, Counter calls) =
                await CreateCallDispatcherAsync().ConfigureAwait(false);
            await using (dispatcher.ConfigureAwait(false))
            {
                var requests = new CallMethodRequest[64];
                for (int ii = 0; ii < requests.Length; ii++)
                {
                    requests[ii] = new CallMethodRequest { ObjectId = owner.NodeId, MethodId = method.NodeId };
                }
                using var context = new OperationContext(m_mockSession.Object, DiagnosticsMasks.None);
                context.StartResponseBudget(maxMessageSize);

                ServiceResultException error = await Assert.ThrowsAsync<ServiceResultException>(
                    async () => await dispatcher.CallAsync(context, requests).ConfigureAwait(false));

                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadResponseTooLarge));
                // each output is 4 KiB: the fourth passes the 16 KiB limit.
                Assert.That(calls.Value, Is.EqualTo(4));
            }
        }

        /// <summary>
        /// A Call whose outputs fit the message size limit returns all of them.
        /// </summary>
        [Test]
        public async Task CallWithinTheResponseSizeLimitReturnsAllOutputsAsync()
        {
            const int maxMessageSize = 16 * 1024;
            (MasterNodeManager dispatcher, BaseObjectState owner, MethodState method, Counter calls) =
                await CreateCallDispatcherAsync().ConfigureAwait(false);
            await using (dispatcher.ConfigureAwait(false))
            {
                using var context = new OperationContext(m_mockSession.Object, DiagnosticsMasks.None);
                context.StartResponseBudget(maxMessageSize);

                (ArrayOf<CallMethodResult> results, _) = await dispatcher.CallAsync(
                    context,
                    [
                        new CallMethodRequest { ObjectId = owner.NodeId, MethodId = method.NodeId },
                        new CallMethodRequest { ObjectId = owner.NodeId, MethodId = method.NodeId },
                        new CallMethodRequest { ObjectId = owner.NodeId, MethodId = method.NodeId }
                    ]).ConfigureAwait(false);

                Assert.That(calls.Value, Is.EqualTo(3));
                Assert.That(results.Count, Is.EqualTo(3));
                for (int ii = 0; ii < results.Count; ii++)
                {
                    Assert.That(results[ii].StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(results[ii].OutputArguments.Count, Is.EqualTo(1));
                }
            }
        }

        /// <summary>
        /// A Browse that asks for every reference of a node fails with
        /// BadResponseTooLarge when the references exceed the message size limit.
        /// </summary>
        [Test]
        public async Task BrowseOfMoreReferencesThanTheResponseSizeLimitHoldsFailsAsync()
        {
            const int maxMessageSize = 2048;
            (MasterNodeManager dispatcher, BaseObjectState parent) =
                await CreateBrowseDispatcherAsync(childCount: 300).ConfigureAwait(false);
            await using (dispatcher.ConfigureAwait(false))
            {
                using var context = new OperationContext(m_mockSession.Object, DiagnosticsMasks.None);
                context.StartResponseBudget(maxMessageSize);

                ServiceResultException error = await Assert.ThrowsAsync<ServiceResultException>(
                    async () => await dispatcher.BrowseAsync(
                        context, new ViewDescription(), 0, [CreateBrowseDescription(parent.NodeId)]).ConfigureAwait(false));

                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadResponseTooLarge));
            }
        }

        /// <summary>
        /// A Browse whose references fit the message size limit returns them all,
        /// without a continuation point.
        /// </summary>
        [Test]
        public async Task BrowseWithinTheResponseSizeLimitReturnsAllReferencesAsync()
        {
            const int maxMessageSize = 1024 * 1024;
            (MasterNodeManager dispatcher, BaseObjectState parent) =
                await CreateBrowseDispatcherAsync(childCount: 300).ConfigureAwait(false);
            await using (dispatcher.ConfigureAwait(false))
            {
                using var context = new OperationContext(m_mockSession.Object, DiagnosticsMasks.None);
                context.StartResponseBudget(maxMessageSize);

                (ArrayOf<BrowseResult> results, _) = await dispatcher.BrowseAsync(
                    context, new ViewDescription(), 0, [CreateBrowseDescription(parent.NodeId)]).ConfigureAwait(false);

                Assert.That(results.Count, Is.EqualTo(1));
                Assert.That(results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                Assert.That(results[0].References.Count, Is.GreaterThanOrEqualTo(300));
                Assert.That(results[0].ContinuationPoint.IsEmpty, Is.True);
            }
        }

        /// <summary>
        /// The references fetched for one node are limited to one more than the rest
        /// of the response byte budget can hold at the smallest reference size.
        /// </summary>
        [TestCase(0u, 0L, 0L, 0u)]
        [TestCase(10u, 0L, 0L, 10u)]
        [TestCase(0u, 1800L, 0L, 101u)]
        [TestCase(50u, 1800L, 0L, 50u)]
        [TestCase(500u, 1800L, 0L, 101u)]
        [TestCase(0u, 1800L, 1800L, 1u)]
        [TestCase(0u, 1800L, 5000L, 1u)]
        public void ReferencesPerNodeAreLimitedByTheRestOfTheResponseBudget(
            uint requested, long budget, long charged, uint expected)
        {
            Assert.That(
                NodeManagerServiceDispatcher.LimitReferencesPerNode(requested, budget, charged),
                Is.EqualTo(expected));
        }

        private static BrowseDescription CreateBrowseDescription(NodeId nodeId)
        {
            return new BrowseDescription
            {
                NodeId = nodeId,
                BrowseDirection = BrowseDirection.Forward,
                ReferenceTypeId = NodeId.Null,
                IncludeSubtypes = true,
                NodeClassMask = 0,
                ResultMask = (uint)BrowseResultMask.All
            };
        }


        private MasterNodeManager CreateDispatcher(ITestNodeManager manager)
        {
            m_mockServer.Setup(server => server.Telemetry).Returns(NUnitTelemetryContext.Create());
            var core = new Mock<ICoreNodeManager>();
            core.Setup(value => value.SyncNodeManager).Returns(Mock.Of<INodeManager>());
            var factory = new Mock<IMainNodeManagerFactory>();
            factory.Setup(value => value.CreateConfigurationNodeManager())
                .Returns(CreateUnrestrictedConfigurationNodeManager().Object);
            factory.Setup(value => value.CreateCoreNodeManager(It.IsAny<ushort>()))
                .Returns(core.Object);
            m_mockServer.Setup(server => server.MainNodeManagerFactory).Returns(factory.Object);
            var dispatcher = new MasterNodeManager(m_mockServer.Object, m_configuration, null, manager);
            m_mockServer.Setup(server => server.NodeManager).Returns(dispatcher);
            return dispatcher;
        }

        private async Task<(MasterNodeManager, BaseObjectState, MethodState, Counter)> CreateCallDispatcherAsync()
        {
            ITestNodeManager manager = CreateManager(asynchronousLegacyCall: true);
            MasterNodeManager dispatcher = CreateDispatcher(manager);
            ushort ns = manager.NamespaceIndexes[0];
            var owner = new BaseObjectState(null)
            {
                NodeId = new NodeId("BudgetOwner", ns),
                BrowseName = new QualifiedName("BudgetOwner", ns)
            };
            var method = new MethodState(owner)
            {
                NodeId = new NodeId("BudgetCall", ns),
                BrowseName = new QualifiedName("BudgetCall", ns)
            };
            method.OutputArguments =
                new PropertyState<ArrayOf<Argument>>.Implementation<StructureBuilder<Argument>>(method)
                {
                    Value =
                    [
                        new Argument
                        {
                            Name = "Values",
                            DataType = DataTypeIds.UInt32,
                            ValueRank = ValueRanks.OneDimension
                        }
                    ]
                };
            var calls = new Counter();
            method.OnCallMethodWithResultAsync = (_, _, _, _, _) =>
            {
                calls.Value++;
                ArrayOf<uint> output = new uint[1024];
                return new ValueTask<MethodInvocationResult>(
                    new MethodInvocationResult(ServiceResult.Good, [new Variant(output)]));
            };
            owner.AddChild(method);
            await manager.AddNodeAsync(manager.SystemContext, NodeId.Null, owner).ConfigureAwait(false);
            return (dispatcher, owner, method, calls);
        }

        private async Task<(MasterNodeManager, BaseObjectState)> CreateBrowseDispatcherAsync(int childCount)
        {
            // the system contexts share the type table of the server.
            TypeTable typeTree = m_mockServer.Object.TypeTree;
            typeTree.AddSubtype(ReferenceTypeIds.References, NodeId.Null);
            typeTree.AddSubtype(ReferenceTypeIds.HierarchicalReferences, ReferenceTypeIds.References);
            typeTree.AddSubtype(ReferenceTypeIds.NonHierarchicalReferences, ReferenceTypeIds.References);
            typeTree.AddSubtype(ReferenceTypeIds.HasTypeDefinition, ReferenceTypeIds.NonHierarchicalReferences);
            typeTree.AddSubtype(ReferenceTypeIds.HasChild, ReferenceTypeIds.HierarchicalReferences);
            typeTree.AddSubtype(ReferenceTypeIds.Aggregates, ReferenceTypeIds.HasChild);
            typeTree.AddSubtype(ReferenceTypeIds.HasComponent, ReferenceTypeIds.Aggregates);
            var points = new SessionContinuationPoints(() => new NodeId(99, 1), 10, 10, null);
            m_mockSession.SetupGet(s => s.ContinuationPoints).Returns(points);
            ITestNodeManager manager = CreateManager();
            MasterNodeManager dispatcher = CreateDispatcher(manager);
            ushort ns = manager.NamespaceIndexes[0];
            var parent = new BaseObjectState(null)
            {
                NodeId = new NodeId("BudgetParent", ns),
                BrowseName = new QualifiedName("BudgetParent", ns),
                DisplayName = new LocalizedText("BudgetParent")
            };
            for (int ii = 0; ii < childCount; ii++)
            {
                string name = "Child" + ii.ToString("D3", System.Globalization.CultureInfo.InvariantCulture);
                parent.AddChild(new BaseObjectState(parent)
                {
                    NodeId = new NodeId(name, ns),
                    BrowseName = new QualifiedName(name, ns),
                    DisplayName = new LocalizedText(name),
                    ReferenceTypeId = ReferenceTypeIds.HasComponent
                });
            }
            await manager.AddNodeAsync(manager.SystemContext, NodeId.Null, parent).ConfigureAwait(false);
            return (dispatcher, parent);
        }

        private sealed class Counter
        {
            public int Value { get; set; }
        }
    }
}
