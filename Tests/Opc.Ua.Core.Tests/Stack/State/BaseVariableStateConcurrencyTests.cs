using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Opc.Ua.Core.Tests.Stack.State
{
    /// <summary>
    /// Tests for the NodeState classes.
    /// </summary>
    [TestFixture]
    [SetCulture("en-us"), SetUICulture("en-us")]
    public class BaseVariableStateConcurrencyTests
    {

        [Test]
        public void NodeStateHandlerConcurrencyTest()
        {
            var testNodeState = new AnalogUnitRangeState(null);
            var serviceMessageContext = new ServiceMessageContext();

            var systemContext = new SystemContext() { NamespaceUris = serviceMessageContext.NamespaceUris };

            testNodeState.Create(
                systemContext,
                new NodeId("TestNode", (ushort)7),
                new QualifiedName("TestNode", (ushort)7),
                new LocalizedText("TestNode"),
                true);

            testNodeState.WriteMask = AttributeWriteMask.AccessLevel | AttributeWriteMask.UserAccessLevel;

            var result = testNodeState.WriteAttribute(systemContext, Attributes.AccessLevel, NumericRange.Empty, new DataValue(new Variant(AccessLevels.CurrentReadOrWrite)));
            Assert.IsTrue(ServiceResult.IsGood(result));
            result = testNodeState.WriteAttribute(systemContext, Attributes.UserAccessLevel, NumericRange.Empty, new DataValue(new Variant(AccessLevels.CurrentReadOrWrite)));
            Assert.IsTrue(ServiceResult.IsGood(result));

            bool running = true;


            var task = Task.Run(() =>
            {
                while (running)
                {
                    testNodeState.OnWriteValue = ValueChangedHandler;
                    testNodeState.OnWriteValue = null;
                }
            });

            DateTime utcNow = DateTime.UtcNow;

            while (DateTime.UtcNow - utcNow < TimeSpan.FromSeconds(1))
            {
                var writeResult = testNodeState.WriteAttribute(
                                    systemContext,
                                    Attributes.Value,
                                    NumericRange.Empty,
                                    new DataValue(new Variant(15.5)));
                Assert.IsTrue(ServiceResult.IsGood(writeResult), "Expected Good ServiceResult but was: {0}", writeResult);
            }

            running = false;

            task.Wait();
        }

        [Test]
        public void NodeStateCollectionConcurrencyTest()
        {
            var testNodeState = new AnalogUnitRangeState(null);
            var serviceMessageContext = new ServiceMessageContext();

            var systemContext = new SystemContext() { NamespaceUris = serviceMessageContext.NamespaceUris };

            testNodeState.Create(
                new SystemContext() { NamespaceUris = serviceMessageContext.NamespaceUris },
                new NodeId("TestNode", (ushort)7),
                new QualifiedName("TestNode", (ushort)7),
                new LocalizedText("TestNode"),
                true);

            bool running = true;

            uint index = 0;
            BlockingCollection<ExpandedNodeId> referenceTargets = new BlockingCollection<ExpandedNodeId>();

            var task = Task.Run(() =>
            {
                while (running)
                {
                    var target = new ExpandedNodeId(index++, "test.namespace");
                    testNodeState.AddReference(ReferenceTypeIds.HasComponent, false, target);
                    referenceTargets.Add(target);
                }

            });

            DateTime utcNow = DateTime.UtcNow;

            while (DateTime.UtcNow - utcNow < TimeSpan.FromSeconds(1))
            {
                bool tryTakeSuccess = referenceTargets.TryTake(out ExpandedNodeId target, TimeSpan.FromSeconds(1));
                if(tryTakeSuccess)
                {
                    var removeReferenceSuccess = testNodeState.RemoveReference(ReferenceTypeIds.HasComponent, false, target);
                    Assert.IsTrue(removeReferenceSuccess);
                }
            }

            running = false;

            task.Wait();
            
        }

        private static ServiceResult ValueChangedHandler(
            ISystemContext context,
            NodeState node,
            NumericRange indexRange,
            QualifiedName dataEncoding,
            ref object value,
            ref StatusCode statusCode,
            ref DateTime timestamp)
        {
            return ServiceResult.Good;
        }
    }
    
}
