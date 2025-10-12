using System.Collections.Generic;
using NUnit.Framework;
using Opc.Ua.Tests;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Core.Tests.Stack.State
{
    /// <summary>
    /// Tests for RemoveChild functionality in generated state classes
    /// </summary>
    [TestFixture]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Category("RemoveChild")]
    [Parallelizable(ParallelScope.All)]
    public class RemoveChildTests
    {
        [Test]
        public void ServerRedundancyState_RemoveChild_RemovesOptionalChild()
        {
            // Arrange
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var serviceMessageContext = new ServiceMessageContext(telemetry);
            var systemContext = new SystemContext(telemetry)
            {
                NamespaceUris = serviceMessageContext.NamespaceUris
            };

            var serverRedundancy = new ServerRedundancyState(null);
            serverRedundancy.Create(
                systemContext,
                new NodeId("ServerRedundancy", 0),
                new QualifiedName("ServerRedundancy", 0),
                new LocalizedText("ServerRedundancy"),
                true);

            // Initialize optional child
            var redundantServerArray = new PropertyState<RedundantServerDataType[]>(serverRedundancy)
            {
                BrowseName = new QualifiedName(BrowseNames.RedundantServerArray, 0),
                NodeId = new NodeId("RedundantServerArray", 0)
            };
            serverRedundancy.RedundantServerArray = redundantServerArray;

            // Verify child exists
            var children = new List<BaseInstanceState>();
            serverRedundancy.GetChildren(systemContext, children);
            Assert.IsTrue(children.Contains(redundantServerArray), "RedundantServerArray should be in children before removal");

            // Act
            serverRedundancy.RemoveChild(redundantServerArray);

            // Assert
            children.Clear();
            serverRedundancy.GetChildren(systemContext, children);
            Assert.IsFalse(children.Contains(redundantServerArray), "RedundantServerArray should not be in children after removal");
        }

        [Test]
        public void ServerRedundancyState_RemoveChild_BySettingPropertyToNull()
        {
            // Arrange
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var serviceMessageContext = new ServiceMessageContext(telemetry);
            var systemContext = new SystemContext(telemetry)
            {
                NamespaceUris = serviceMessageContext.NamespaceUris
            };

            var serverRedundancy = new ServerRedundancyState(null);
            serverRedundancy.Create(
                systemContext,
                new NodeId("ServerRedundancy", 0),
                new QualifiedName("ServerRedundancy", 0),
                new LocalizedText("ServerRedundancy"),
                true);

            // Initialize optional child
            var redundantServerArray = new PropertyState<RedundantServerDataType[]>(serverRedundancy)
            {
                BrowseName = new QualifiedName(BrowseNames.RedundantServerArray, 0),
                NodeId = new NodeId("RedundantServerArray", 0)
            };
            serverRedundancy.RedundantServerArray = redundantServerArray;

            // Verify child exists
            var children = new List<BaseInstanceState>();
            serverRedundancy.GetChildren(systemContext, children);
            Assert.IsTrue(children.Contains(redundantServerArray), "RedundantServerArray should be in children before removal");

            // Act - setting to null should also remove from children
            serverRedundancy.RedundantServerArray = null;

            // Assert
            children.Clear();
            serverRedundancy.GetChildren(systemContext, children);
            Assert.IsFalse(children.Contains(redundantServerArray), "RedundantServerArray should not be in children after setting to null");
        }

        [Test]
        public void BaseObjectState_RemoveChild_RemovesDynamicChild()
        {
            // Arrange
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var serviceMessageContext = new ServiceMessageContext(telemetry);
            var systemContext = new SystemContext(telemetry)
            {
                NamespaceUris = serviceMessageContext.NamespaceUris
            };

            var objectState = new BaseObjectState(null);
            objectState.Create(
                systemContext,
                new NodeId("Object", 0),
                new QualifiedName("Object", 0),
                new LocalizedText("Object"),
                true);

            // Add a dynamic child
            var dynamicChild = new BaseObjectState(objectState)
            {
                BrowseName = new QualifiedName("DynamicChild", 0),
                NodeId = new NodeId("DynamicChild", 0)
            };
            objectState.AddChild(dynamicChild);

            // Verify child exists
            var children = new List<BaseInstanceState>();
            objectState.GetChildren(systemContext, children);
            Assert.IsTrue(children.Contains(dynamicChild), "DynamicChild should be in children before removal");

            // Act
            objectState.RemoveChild(dynamicChild);

            // Assert
            children.Clear();
            objectState.GetChildren(systemContext, children);
            Assert.IsFalse(children.Contains(dynamicChild), "DynamicChild should not be in children after removal");
        }
    }
}
