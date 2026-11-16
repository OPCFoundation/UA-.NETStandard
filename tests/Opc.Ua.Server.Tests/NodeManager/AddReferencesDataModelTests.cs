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
 *
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

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.NodeManager
{
    /// <summary>
    /// AddReferences enforces the data-model rules of OPC UA Part 3 and returns
    /// Bad_ReferenceNotAllowed or Bad_NodeClassInvalid (Part 4 5.8.4) instead of
    /// adding a Reference that corrupts the address space.
    /// </summary>
    [TestFixture]
    [Category("NodeManagement")]
    public sealed class AddReferencesDataModelTests
    {
        /// <summary>
        /// An instance that already has a TypeDefinition cannot get a second one,
        /// whichever direction the request uses.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SecondTypeDefinitionIsRejectedAsync(bool inverse)
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);

            StatusCode result = await harness.AddReferenceAsync(
                harness.Typed.NodeId,
                ReferenceTypeIds.HasTypeDefinition,
                harness.ObjectType.NodeId,
                inverse).ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(StatusCodes.BadReferenceNotAllowed));
            Assert.That(harness.Typed.ReferenceExists(
                ReferenceTypeIds.HasTypeDefinition, false, harness.ObjectType.NodeId), Is.False);
            Assert.That(harness.ObjectType.ReferenceExists(
                ReferenceTypeIds.HasTypeDefinition, true, harness.Typed.NodeId), Is.False);
        }

        /// <summary>
        /// A second HasTypeDefinition added through AddReferences is rejected as well.
        /// </summary>
        [Test]
        public async Task TypeDefinitionAddedByAddReferencesCannotBeReplacedAsync()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);

            StatusCode first = await harness.AddReferenceAsync(
                harness.Untyped.NodeId,
                ReferenceTypeIds.HasTypeDefinition,
                harness.ObjectType.NodeId).ConfigureAwait(false);
            StatusCode second = await harness.AddReferenceAsync(
                harness.Untyped.NodeId,
                ReferenceTypeIds.HasTypeDefinition,
                harness.OtherObjectType.NodeId).ConfigureAwait(false);

            Assert.That(first, Is.EqualTo(StatusCodes.Good));
            Assert.That(second, Is.EqualTo(StatusCodes.BadReferenceNotAllowed));
        }

        /// <summary>
        /// An Interface is never the TargetNode of a HasTypeDefinition (Part 3 4.10.2).
        /// </summary>
        [Test]
        public async Task TypeDefinitionToInterfaceIsRejectedAsync()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);

            StatusCode result = await harness.AddReferenceAsync(
                harness.Untyped.NodeId,
                ReferenceTypeIds.HasTypeDefinition,
                harness.Interface.NodeId).ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(StatusCodes.BadReferenceNotAllowed));
        }

        /// <summary>
        /// An abstract type cannot be the TypeDefinition of an instance.
        /// </summary>
        [Test]
        public async Task TypeDefinitionToAbstractTypeIsRejectedAsync()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);

            StatusCode result = await harness.AddReferenceAsync(
                harness.Untyped.NodeId,
                ReferenceTypeIds.HasTypeDefinition,
                harness.AbstractObjectType.NodeId).ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(StatusCodes.BadReferenceNotAllowed));
        }

        /// <summary>
        /// An Object is typed by an ObjectType and a Variable by a VariableType only;
        /// a non-instance cannot be the source of a HasTypeDefinition.
        /// </summary>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public async Task TypeDefinitionOfWrongNodeClassIsRejectedAsync(int scenario)
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            (NodeId source, NodeId target) = scenario switch
            {
                0 => (harness.Untyped.NodeId, harness.VariableType.NodeId),
                1 => (harness.UntypedVariable.NodeId, harness.ObjectType.NodeId),
                _ => (harness.ObjectType.NodeId, harness.OtherObjectType.NodeId)
            };

            StatusCode result = await harness.AddReferenceAsync(
                source, ReferenceTypeIds.HasTypeDefinition, target).ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(StatusCodes.BadReferenceNotAllowed));
        }

        /// <summary>
        /// An instance with a ModellingRule cannot get a second one.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task SecondModellingRuleIsRejectedAsync(bool inverse)
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);

            StatusCode result = await harness.AddReferenceAsync(
                harness.WithModellingRule.NodeId,
                ReferenceTypeIds.HasModellingRule,
                harness.OtherModellingRule.NodeId,
                inverse).ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(StatusCodes.BadReferenceNotAllowed));
            Assert.That(harness.WithModellingRule.ReferenceExists(
                ReferenceTypeIds.HasModellingRule, false, harness.OtherModellingRule.NodeId), Is.False);
        }

        /// <summary>
        /// The TargetNode of a HasProperty is a Variable.
        /// </summary>
        [Test]
        public async Task HasPropertyToObjectIsRejectedAsync()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);

            StatusCode result = await harness.AddReferenceAsync(
                harness.Folder.NodeId,
                ReferenceTypeIds.HasProperty,
                harness.Untyped.NodeId).ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(StatusCodes.BadReferenceNotAllowed));
            Assert.That(harness.Folder.ReferenceExists(
                ReferenceTypeIds.HasProperty, false, harness.Untyped.NodeId), Is.False);
        }

        /// <summary>
        /// A Property is never the SourceNode of a hierarchical Reference (Part 3 5.6.3),
        /// whether it is a PropertyState or a Variable referenced by HasProperty.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public async Task PropertyAsHierarchicalSourceIsRejectedAsync(bool inverse, bool referencedProperty)
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);
            NodeId property = harness.Property.NodeId;
            if (referencedProperty)
            {
                StatusCode added = await harness.AddReferenceAsync(
                    harness.Folder.NodeId,
                    ReferenceTypeIds.HasProperty,
                    harness.UntypedVariable.NodeId).ConfigureAwait(false);
                Assert.That(added, Is.EqualTo(StatusCodes.Good));
                property = harness.UntypedVariable.NodeId;
            }

            StatusCode result = await harness.AddReferenceAsync(
                property,
                ReferenceTypeIds.HasComponent,
                harness.DataVariable.NodeId,
                inverse).ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(StatusCodes.BadReferenceNotAllowed));
        }

        /// <summary>
        /// HasSubtype only connects two types of the same NodeClass.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task HasSubtypeBetweenIncompatibleNodesIsRejectedAsync(bool instances)
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);

            StatusCode result = await harness.AddReferenceAsync(
                instances ? harness.Folder.NodeId : harness.ObjectType.NodeId,
                ReferenceTypeIds.HasSubtype,
                instances ? harness.Untyped.NodeId : harness.VariableType.NodeId).ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(StatusCodes.BadReferenceNotAllowed));
        }

        /// <summary>
        /// A TargetNodeClass that does not match the TargetNode is Bad_NodeClassInvalid.
        /// </summary>
        [Test]
        public async Task MismatchedTargetNodeClassIsRejectedAsync()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);

            StatusCode result = await harness.AddReferenceAsync(
                harness.Folder.NodeId,
                ReferenceTypeIds.Organizes,
                harness.Untyped.NodeId,
                targetNodeClass: NodeClass.Variable).ConfigureAwait(false);

            Assert.That(result, Is.EqualTo(StatusCodes.BadNodeClassInvalid));
            Assert.That(harness.Folder.ReferenceExists(
                ReferenceTypeIds.Organizes, false, harness.Untyped.NodeId), Is.False);
        }

        /// <summary>
        /// References that follow the data model are still added.
        /// </summary>
        [Test]
        public async Task ValidReferencesAreAddedAsync()
        {
            await using var harness = new Harness();
            await harness.InitializeAsync().ConfigureAwait(false);

            StatusCode organizes = await harness.AddReferenceAsync(
                harness.Folder.NodeId,
                ReferenceTypeIds.Organizes,
                harness.Untyped.NodeId,
                targetNodeClass: NodeClass.Object).ConfigureAwait(false);
            StatusCode typeDefinition = await harness.AddReferenceAsync(
                harness.Untyped.NodeId,
                ReferenceTypeIds.HasTypeDefinition,
                harness.ObjectType.NodeId,
                inverse: true).ConfigureAwait(false);
            StatusCode property = await harness.AddReferenceAsync(
                harness.DataVariable.NodeId,
                ReferenceTypeIds.HasProperty,
                harness.Property.NodeId,
                targetNodeClass: NodeClass.Variable).ConfigureAwait(false);
            StatusCode modellingRule = await harness.AddReferenceAsync(
                harness.Untyped.NodeId,
                ReferenceTypeIds.HasModellingRule,
                harness.OtherModellingRule.NodeId).ConfigureAwait(false);
            StatusCode subtype = await harness.AddReferenceAsync(
                harness.ObjectType.NodeId,
                ReferenceTypeIds.HasSubtype,
                harness.OtherObjectType.NodeId).ConfigureAwait(false);

            Assert.That(organizes, Is.EqualTo(StatusCodes.Good));
            Assert.That(typeDefinition, Is.EqualTo(StatusCodes.Good));
            Assert.That(property, Is.EqualTo(StatusCodes.Good));
            Assert.That(modellingRule, Is.EqualTo(StatusCodes.Good));
            Assert.That(subtype, Is.EqualTo(StatusCodes.Good));
            Assert.That(harness.Untyped.ReferenceExists(
                ReferenceTypeIds.HasTypeDefinition, false, harness.ObjectType.NodeId), Is.True);
        }

        /// <summary>
        /// Owns a real dispatcher and an opted-in manager with a small typed address space.
        /// </summary>
        private sealed class Harness : IAsyncDisposable
        {
            public Harness()
            {
                Mock<IServerInternal> server = DeterministicServerMock.Create(out m_queues);
                server.SetupGet(value => value.Telemetry).Returns(NUnitTelemetryContext.Create());
                var types = (TypeTable)server.Object.TypeTree;
                types.AddSubtype(ReferenceTypeIds.References, NodeId.Null);
                types.AddSubtype(ReferenceTypeIds.HierarchicalReferences, ReferenceTypeIds.References);
                types.AddSubtype(ReferenceTypeIds.NonHierarchicalReferences, ReferenceTypeIds.References);
                types.AddSubtype(ReferenceTypeIds.HasChild, ReferenceTypeIds.HierarchicalReferences);
                types.AddSubtype(ReferenceTypeIds.Organizes, ReferenceTypeIds.HierarchicalReferences);
                types.AddSubtype(ReferenceTypeIds.Aggregates, ReferenceTypeIds.HasChild);
                types.AddSubtype(ReferenceTypeIds.HasSubtype, ReferenceTypeIds.HasChild);
                types.AddSubtype(ReferenceTypeIds.HasComponent, ReferenceTypeIds.Aggregates);
                types.AddSubtype(ReferenceTypeIds.HasProperty, ReferenceTypeIds.Aggregates);
                types.AddSubtype(ReferenceTypeIds.HasTypeDefinition, ReferenceTypeIds.NonHierarchicalReferences);
                types.AddSubtype(ReferenceTypeIds.HasModellingRule, ReferenceTypeIds.NonHierarchicalReferences);
                types.AddSubtype(ObjectTypeIds.BaseObjectType, NodeId.Null);
                types.AddSubtype(ObjectTypeIds.BaseInterfaceType, ObjectTypeIds.BaseObjectType);
                types.AddSubtype(VariableTypeIds.BaseVariableType, NodeId.Null);
                types.AddSubtype(VariableTypeIds.BaseDataVariableType, VariableTypeIds.BaseVariableType);
                types.AddSubtype(VariableTypeIds.PropertyType, VariableTypeIds.BaseVariableType);
                var configuration = new ApplicationConfiguration { ServerConfiguration = new ServerConfiguration() };
                Owner = new ManagementManager(server.Object, configuration);
                ushort ns = Owner.NamespaceIndex;

                ObjectType = CreateObjectType("ObjectType", ObjectTypeIds.BaseObjectType, false, ns);
                OtherObjectType = CreateObjectType("OtherObjectType", ObjectTypeIds.BaseObjectType, false, ns);
                AbstractObjectType = CreateObjectType("AbstractObjectType", ObjectTypeIds.BaseObjectType, true, ns);
                Interface = CreateObjectType("IInterface", ObjectTypeIds.BaseInterfaceType, true, ns);
                VariableType = new BaseDataVariableTypeState
                {
                    NodeId = new NodeId("VariableType", ns),
                    BrowseName = new QualifiedName("VariableType", ns),
                    SuperTypeId = VariableTypeIds.BaseDataVariableType
                };
                types.AddSubtype(ObjectType.NodeId, ObjectTypeIds.BaseObjectType);
                types.AddSubtype(OtherObjectType.NodeId, ObjectTypeIds.BaseObjectType);
                types.AddSubtype(AbstractObjectType.NodeId, ObjectTypeIds.BaseObjectType);
                types.AddSubtype(Interface.NodeId, ObjectTypeIds.BaseInterfaceType);
                types.AddSubtype(VariableType.NodeId, VariableTypeIds.BaseDataVariableType);

                Folder = CreateObject("Folder", ns);
                Untyped = CreateObject("Untyped", ns);
                Typed = CreateObject("Typed", ns);
                Typed.TypeDefinitionId = ObjectTypeIds.BaseObjectType;
                ModellingRule = CreateObject("ModellingRule", ns);
                OtherModellingRule = CreateObject("OtherModellingRule", ns);
                WithModellingRule = CreateObject("WithModellingRule", ns);
                WithModellingRule.ModellingRuleId = ModellingRule.NodeId;
                Property = new PropertyState(null)
                {
                    NodeId = new NodeId("Property", ns),
                    BrowseName = new QualifiedName("Property", ns),
                    TypeDefinitionId = VariableTypeIds.PropertyType
                };
                DataVariable = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("DataVariable", ns),
                    BrowseName = new QualifiedName("DataVariable", ns),
                    TypeDefinitionId = VariableTypeIds.BaseDataVariableType
                };
                UntypedVariable = new BaseDataVariableState(null)
                {
                    NodeId = new NodeId("UntypedVariable", ns),
                    BrowseName = new QualifiedName("UntypedVariable", ns)
                };

                Dispatcher = DeterministicServerMock.CreateMasterNodeManager(server, [Owner]);
                Context = new OperationContext(
                    new RequestHeader(), null, RequestType.AddReferences, RequestLifetime.None);
            }

            public MasterNodeManager Dispatcher { get; }
            public ManagementManager Owner { get; }
            public OperationContext Context { get; }
            public BaseObjectTypeState ObjectType { get; }
            public BaseObjectTypeState OtherObjectType { get; }
            public BaseObjectTypeState AbstractObjectType { get; }
            public BaseObjectTypeState Interface { get; }
            public BaseDataVariableTypeState VariableType { get; }
            public BaseObjectState Folder { get; }
            public BaseObjectState Untyped { get; }
            public BaseObjectState Typed { get; }
            public BaseObjectState ModellingRule { get; }
            public BaseObjectState OtherModellingRule { get; }
            public BaseObjectState WithModellingRule { get; }
            public PropertyState Property { get; }
            public BaseDataVariableState DataVariable { get; }
            public BaseDataVariableState UntypedVariable { get; }

            public async Task InitializeAsync()
            {
                NodeState[] nodes =
                [
                    ObjectType, OtherObjectType, AbstractObjectType, Interface, VariableType,
                    Folder, Untyped, Typed, ModellingRule, OtherModellingRule, WithModellingRule,
                    Property, DataVariable, UntypedVariable
                ];
                foreach (NodeState node in nodes)
                {
                    await Owner.RegisterAsync(node).ConfigureAwait(false);
                }
            }

            public async Task<StatusCode> AddReferenceAsync(
                NodeId source,
                NodeId referenceTypeId,
                NodeId target,
                bool inverse = false,
                NodeClass targetNodeClass = NodeClass.Unspecified)
            {
                // an inverse request names the forward target as its SourceNode.
                var item = new AddReferencesItem
                {
                    SourceNodeId = inverse ? target : source,
                    ReferenceTypeId = referenceTypeId,
                    IsForward = !inverse,
                    TargetNodeId = inverse ? source : target,
                    TargetNodeClass = targetNodeClass
                };
                (ArrayOf<StatusCode> results, _) = await Dispatcher.AddReferencesAsync(
                    Context, [item], CancellationToken.None).ConfigureAwait(false);
                return results[0];
            }

            public async ValueTask DisposeAsync()
            {
                Context.Dispose();
                await Dispatcher.DisposeAsync().ConfigureAwait(false);
                m_queues.Dispose();
            }

            private static BaseObjectTypeState CreateObjectType(
                string name, NodeId superTypeId, bool isAbstract, ushort ns)
            {
                return new BaseObjectTypeState
                {
                    NodeId = new NodeId(name, ns),
                    BrowseName = new QualifiedName(name, ns),
                    SuperTypeId = superTypeId,
                    IsAbstract = isAbstract
                };
            }

            private static BaseObjectState CreateObject(string name, ushort ns)
            {
                return new BaseObjectState(null)
                {
                    NodeId = new NodeId(name, ns),
                    BrowseName = new QualifiedName(name, ns)
                };
            }

            private readonly MonitoredItemQueueFactory m_queues;
        }

        /// <summary>
        /// Opts in to NodeManagement while keeping the production reference mutation.
        /// </summary>
        private sealed class ManagementManager : AsyncCustomNodeManager
        {
            public ManagementManager(IServerInternal server, ApplicationConfiguration configuration)
                : base(server, configuration, NullLogger.Instance, DeterministicServerMock.TestNamespaceUri)
            {
            }

            /// <inheritdoc/>
            public override bool AllowNodeManagement => true;

            public ValueTask RegisterAsync(NodeState node)
            {
                return AddPredefinedNodeAsync(SystemContext, node);
            }
        }
    }
}
