using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;

namespace Opc.Ua.Server.Tests
{ /// <summary>
  /// Test <see cref="CustomNodeManager2"/>
  /// </summary>
    [TestFixture, Category("CustomNodeManager")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    public class CustomNodeManagerTests
    {
        #region Test Methods
        /// <summary>
        /// Tests the componentCache methods with multiple threads
        /// </summary>
        [Test]
        public async Task TestComponentCacheAsync()
        {
            var fixture = new ServerFixture<StandardServer>();

            try
            {
                // Arrange
                const string ns = "http://test.org/UA/Data/";
                var server = await fixture.StartAsync(TestContext.Out).ConfigureAwait(false);

                var nodeManager = new TestableCustomNodeManger2(server.CurrentInstance, ns);


                var baseObject = new BaseObjectState(null);
                var nodeHandle = new NodeHandle(new NodeId((string)CommonTestWorkers.NodeIdTestSetStatic.First().Identifier, 0), baseObject);

                //Act
                await RunTaskInParallel(() => UseComponentCacheAsync(nodeManager, baseObject, nodeHandle), 100);


                //Assert, that entry was deleted from cache after parallel operations on the same node
                NodeState handleFromCache = nodeManager.LookupNodeInComponentCache(nodeManager.SystemContext, nodeHandle);

                Assert.That(handleFromCache, Is.Null);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Tests the Predefined Nodes methods with multiple threads
        /// </summary>
        [Test]
        public async Task TestPredefinedNodes()
        {
            var fixture = new ServerFixture<StandardServer>();

            try
            {
                // Arrange
                const string ns = "http://test.org/UA/Data/";
                var server = await fixture.StartAsync(TestContext.Out).ConfigureAwait(false);

                var nodeManager = new TestableCustomNodeManger2(server.CurrentInstance, ns);
                var index = server.CurrentInstance.NamespaceUris.GetIndex(ns);

                var baseObject = new DataItemState(null);
                var nodeId = new NodeId((string)CommonTestWorkers.NodeIdTestSetStatic.First().Identifier, (ushort)index);

                baseObject.NodeId = nodeId;

                nodeManager.AddPredefinedNode(nodeManager.SystemContext, baseObject);

                Assert.That(nodeManager.PredefinedNodes.ContainsKey(nodeId), Is.True);

                nodeManager.DeleteNode(nodeManager.SystemContext, nodeId);

                Assert.That(nodeManager.PredefinedNodes, Is.Empty);

                //NodeState nodeState = nodeManager.Find(nodeId);

                //NodeHandle handle = (NodeHandle)nodeManager.GetManagerHandle(nodeId);

                //nodeManager.DeleteAddressSpace();

                //Assert.That(handle, Is.Not.Null);

                ////Act
                ////await RunTaskInParallel(() => UseComponentCacheAsync(nodeManager, baseObject, nodeHandle), 100);


                ////Assert, that entry was deleted from cache after parallel operations on the same node
                //NodeState handleFromCache = nodeManager.LookupNodeInComponentCache(nodeManager.SystemContext, nodeHandle);

                //Assert.That(handleFromCache, Is.Null);
            }
            finally
            {
                await fixture.StopAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Test Methods  AddNodeToComponentCache,  RemoveNodeFromComponentCache & LookupNodeInComponentCache & verify the node is added to the cache
        /// </summary>
        /// <returns></returns>
        private static async Task UseComponentCacheAsync(TestableCustomNodeManger2 nodeManager, BaseObjectState baseObject, NodeHandle nodeHandle)
        {
            //-- Act
            nodeManager.AddNodeToComponentCache(nodeManager.SystemContext, nodeHandle, baseObject);
            nodeManager.AddNodeToComponentCache(nodeManager.SystemContext, nodeHandle, baseObject);

            NodeState handleFromCache = nodeManager.LookupNodeInComponentCache(nodeManager.SystemContext, nodeHandle);

            //-- Assert

            Assert.That(handleFromCache, Is.Not.Null);

            nodeManager.RemoveNodeFromComponentCache(nodeManager.SystemContext, nodeHandle);

            handleFromCache = nodeManager.LookupNodeInComponentCache(nodeManager.SystemContext, nodeHandle);

            Assert.That(handleFromCache, Is.Not.Null);

            nodeManager.RemoveNodeFromComponentCache(nodeManager.SystemContext, nodeHandle);

            await Task.CompletedTask;
        }
        #endregion

        public static async Task<(bool IsSuccess, Exception Error)> RunTaskInParallel(Func<Task> task, int iterations)
        {
            var cancellationTokenSource = new CancellationTokenSource();
            Exception error = null;
            int tasksCompletedCount = 0;
            var result = Parallel.For(0, iterations, new ParallelOptions(),
                          async index => {
                              try
                              {
                                  await task();
                              }
                              catch (Exception ex)
                              {
                                  error = ex;
                                  cancellationTokenSource.Cancel();
                              }
                              finally
                              {
                                  tasksCompletedCount++;
                              }

                          });

            int spinWaitCount = 0;
            int maxSpinWaitCount = 100;
            while (iterations > tasksCompletedCount && error is null && spinWaitCount < maxSpinWaitCount)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100));
                spinWaitCount++;
            }

            return (error == null, error);
        }

    }

    public class TestableCustomNodeManger2 : CustomNodeManager2
    {
        public TestableCustomNodeManger2(IServerInternal server, params string[] namespaceUris) : base(server, namespaceUris)
        { }

        #region componentCache
        public new NodeState AddNodeToComponentCache(ISystemContext context, NodeHandle handle, NodeState node)
        {
            return base.AddNodeToComponentCache(context, handle, node);
        }
        public new void RemoveNodeFromComponentCache(ISystemContext context, NodeHandle handle)
        {
            base.RemoveNodeFromComponentCache(context, handle);
        }
        public new NodeState LookupNodeInComponentCache(ISystemContext context, NodeHandle handle)
        {
            return base.LookupNodeInComponentCache(context, handle);
        }
        #endregion

        #region PredefinedNodes

        public new NodeIdDictionary<NodeState> PredefinedNodes => base.PredefinedNodes;
        public new virtual void AddPredefinedNode(ISystemContext context, NodeState node)
        {
            base.AddPredefinedNode(context, node);
        }

        #endregion
    }
}
