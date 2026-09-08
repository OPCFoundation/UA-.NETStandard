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

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using NUnit.Framework;
using Opc.Ua.Server.Fluent;

#nullable enable

namespace Opc.Ua.Server.Tests.Fluent
{
    /// <summary>
    /// Covers the node-creation surface on <see cref="INodeManagerBuilder"/>:
    /// staging, NodeId assignment, parenting and registration.
    /// </summary>
    [TestFixture]
    [Category("Fluent")]
    public sealed class NodeManagerBuilderAuthoringTests
    {
        private const string kNamespaceUri = "http://opcfoundation.org/UA/Authoring/";

        [Test]
        public async Task AddFolderRegistersNodeUnderObjectsFolderAsync()
        {
            using var manager = new AuthoringTestManager(
                builder => builder.AddFolder("Machines"));
            await manager.BuildAsync().ConfigureAwait(false);

            NodeState folder = manager.FindByBrowseName("Machines");
            Assert.Multiple(() =>
            {
                Assert.That(folder, Is.InstanceOf<FolderState>());
                Assert.That(folder.NodeId.NamespaceIndex, Is.EqualTo(manager.TestNamespaceIndex));
                Assert.That(
                    HasInverseReference(folder, ReferenceTypeIds.Organizes, ObjectIds.ObjectsFolder),
                    Is.True,
                    "the default parent is mirrored as an inverse Organizes reference");
            });
        }

        [Test]
        public async Task NodeIdsAreAssignedBeforeTheBuilderIsReturnedAsync()
        {
            NodeId folderId = NodeId.Null;
            NodeId variableId = NodeId.Null;

            using var manager = new AuthoringTestManager(builder =>
            {
                INodeBuilder<FolderState> folder = builder.AddFolder("Machines");
                folderId = folder.Node.NodeId;
                IVariableBuilder<int> variable = builder.AddVariable<int>(
                    "Speed",
                    folderId);
                variableId = variable.Node.NodeId;
            });
            await manager.BuildAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(folderId.IsNull, Is.False);
                Assert.That(variableId.IsNull, Is.False);
                Assert.That(manager.ContainsPredefined(folderId), Is.True);
                Assert.That(manager.ContainsPredefined(variableId), Is.True);
            });
        }

        [Test]
        public async Task ParentByNodeIdNestsTheChildUnderTheAuthoredParentAsync()
        {
            using var manager = new AuthoringTestManager(builder =>
            {
                INodeBuilder<FolderState> folder = builder.AddFolder("Machines");
                builder.AddObject("Press", folder.Node.NodeId);
            });
            await manager.BuildAsync().ConfigureAwait(false);

            var folder = (FolderState)manager.FindByBrowseName("Machines");
            NodeState press = manager.FindByBrowseName("Press");
            Assert.Multiple(() =>
            {
                Assert.That(((BaseInstanceState)press).Parent, Is.SameAs(folder));
                Assert.That(
                    ((BaseInstanceState)press).ReferenceTypeId,
                    Is.EqualTo(ReferenceTypeIds.HasComponent));
            });
        }

        [Test]
        public async Task AddVariableDerivesDataTypeFromTheClrTypeAsync()
        {
            using var manager = new AuthoringTestManager(
                builder => builder.AddVariable<double>("Pressure"));
            await manager.BuildAsync().ConfigureAwait(false);

            var variable = (BaseDataVariableState)manager.FindByBrowseName("Pressure");
            Assert.Multiple(() =>
            {
                Assert.That(variable.DataType, Is.EqualTo(DataTypeIds.Double));
                Assert.That(variable.ValueRank, Is.EqualTo(ValueRanks.Scalar));
            });
        }

        [Test]
        public async Task AddMethodCreatesAnExecutableMethodAsync()
        {
            using var manager = new AuthoringTestManager(
                builder => builder.AddMethod("Start"));
            await manager.BuildAsync().ConfigureAwait(false);

            var method = (MethodState)manager.FindByBrowseName("Start");
            Assert.Multiple(() =>
            {
                Assert.That(method.Executable, Is.True);
                Assert.That(method.UserExecutable, Is.True);
            });
        }

        [Test]
        public async Task AddKeepsCustomStateTypesStronglyTypedAsync()
        {
            CustomObjectState? staged = null;

            using var manager = new AuthoringTestManager(builder =>
            {
                var node = new CustomObjectState(null)
                {
                    BrowseName = new QualifiedName("Custom", builder.Context.NamespaceUris
                        .GetIndexOrAppend(kNamespaceUri)),
                    DisplayName = new LocalizedText("Custom")
                };
                INodeBuilder<CustomObjectState> added = builder.Add(node);
                staged = added.Node;
            });
            await manager.BuildAsync().ConfigureAwait(false);

            Assert.That(staged, Is.Not.Null);
            Assert.That(manager.FindByBrowseName("Custom"), Is.SameAs(staged));
        }

        [Test]
        public async Task FactoryOverloadReceivesTheResolvedAuthoredParentAsync()
        {
            NodeState? observedParent = null;
            NodeId folderId = NodeId.Null;

            using var manager = new AuthoringTestManager(builder =>
            {
                INodeBuilder<FolderState> folder = builder.AddFolder("Machines");
                folderId = folder.Node.NodeId;
                builder.Add(
                    parent =>
                    {
                        observedParent = parent;
                        return new CustomObjectState(parent)
                        {
                            BrowseName = new QualifiedName(
                                "Child",
                                builder.Context.NamespaceUris.GetIndexOrAppend(kNamespaceUri))
                        };
                    },
                    folderId);
            });
            await manager.BuildAsync().ConfigureAwait(false);

            Assert.That(observedParent, Is.SameAs(manager.FindByBrowseName("Machines")));
            Assert.That(manager.FindByBrowseName("Child"), Is.Not.Null);
        }

        [Test]
        public async Task FactoryOverloadPassesAnIdentityProxyForAnExternalParentAsync()
        {
            NodeId observedParentId = NodeId.Null;

            using var manager = new AuthoringTestManager(builder => builder.Add(
                parent =>
                {
                    observedParentId = parent!.NodeId;
                    return new CustomObjectState(parent)
                    {
                        BrowseName = new QualifiedName(
                            "Detached",
                            builder.Context.NamespaceUris.GetIndexOrAppend(kNamespaceUri))
                    };
                }));
            await manager.BuildAsync().ConfigureAwait(false);

            NodeState node = manager.FindByBrowseName("Detached");
            Assert.Multiple(() =>
            {
                Assert.That(observedParentId, Is.EqualTo(ObjectIds.ObjectsFolder));
                Assert.That(
                    ((BaseInstanceState)node).Parent,
                    Is.Null,
                    "the identity proxy must not survive as a real parent");
                Assert.That(
                    HasInverseReference(
                        node,
                        ReferenceTypeIds.HasComponent,
                        ObjectIds.ObjectsFolder),
                    Is.True,
                    "BaseObjectState(parent) already chose HasComponent for the proxy parent");
            });
        }

        [Test]
        public async Task AddRootKeepsExistingReferencesAndSkipsTheObjectsFolderAsync()
        {
            using var manager = new AuthoringTestManager(builder =>
            {
                ushort ns = builder.Context.NamespaceUris.GetIndexOrAppend(kNamespaceUri);
                var root = new BaseObjectState(null)
                {
                    NodeId = new NodeId("Root", ns),
                    BrowseName = new QualifiedName("Root", ns),
                    DisplayName = new LocalizedText("Root")
                };
                root.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.RootFolder);
                builder.AddRoot(root);
            });
            await manager.BuildAsync().ConfigureAwait(false);

            NodeState root = manager.FindByBrowseName("Root");
            Assert.Multiple(() =>
            {
                Assert.That(
                    HasInverseReference(root, ReferenceTypeIds.Organizes, ObjectIds.RootFolder),
                    Is.True);
                Assert.That(
                    HasInverseReference(root, ReferenceTypeIds.Organizes, ObjectIds.ObjectsFolder),
                    Is.False,
                    "AddRoot does not synthesize an Objects-folder parent");
            });
        }

        [Test]
        public void TryGetNodeResolvesANodeThatIsNotRegisteredYet()
        {
            using var manager = new AuthoringTestManager(builder =>
            {
                INodeBuilder<FolderState> folder = builder.AddFolder("Machines");

                Assert.Multiple(() =>
                {
                    Assert.That(
                        builder.TryGetNode(folder.Node.NodeId, out NodeState? staged),
                        Is.True);
                    Assert.That(staged, Is.SameAs(folder.Node));
                    Assert.That(
                        builder.TryGetNode(new NodeId("Absent", 9), out NodeState? missing),
                        Is.False);
                    Assert.That(missing, Is.Null);
                    Assert.That(builder.TryGetNode(NodeId.Null, out _), Is.False);
                });
            });

            Assert.DoesNotThrowAsync(async () => await manager.BuildAsync().ConfigureAwait(false));
        }

        [Test]
        public async Task ReadHandlersKeyedByIdFireForAuthoredNodesAsync()
        {
            NodeId variableId = NodeId.Null;

            using var manager = new AuthoringTestManager(builder =>
            {
                IVariableBuilder<int> variable = builder.AddVariable<int>("Counter");
                variableId = variable.Node.NodeId;
                variable.OnRead(() => 42);
            });
            await manager.BuildAsync().ConfigureAwait(false);

            // The handler must sit on the instance that ended up registered
            // under the id the builder handed back during Configure.
            var node = (BaseDataVariableState)manager.FindById(variableId);
            Assert.That(node.OnSimpleReadValue, Is.Not.Null);

            var value = Variant.Null;
            ServiceResult result = node.OnSimpleReadValue!(
                manager.TestSystemContext,
                node,
                ref value);

            Assert.Multiple(() =>
            {
                Assert.That(ServiceResult.IsGood(result), Is.True);
                Assert.That(value.TryGetValue(out int read), Is.True);
                Assert.That(read, Is.EqualTo(42));
            });
        }

        [Test]
        public async Task AuthoredNodesAreVisibleToBrowsePathAndNodeIdLookupsAsync()
        {
            using var manager = new AuthoringTestManager(builder =>
            {
                INodeBuilder<FolderState> folder = builder.AddFolder("Machines");
                builder.AddObject("Press", folder.Node.NodeId);

                Assert.Multiple(() =>
                {
                    Assert.That(builder.Node("Machines/Press").Node.BrowseName.Name, Is.EqualTo("Press"));
                    Assert.That(
                        builder.Node(folder.Node.NodeId).Node,
                        Is.SameAs(folder.Node));
                });
            });

            await manager.BuildAsync().ConfigureAwait(false);
            Assert.That(manager.FindByBrowseName("Press"), Is.Not.Null);
        }

        [Test]
        public void QualifiedNameOverloadRejectsNamespaceZero()
        {
            using var manager = new AuthoringTestManager(
                builder => builder.AddFolder(new QualifiedName("Machines", 0)));

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await manager.BuildAsync().ConfigureAwait(false))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadBrowseNameInvalid));
        }

        [Test]
        public void DuplicateNodeIdIsRejected()
        {
            using var manager = new AuthoringTestManager(builder =>
            {
                ushort ns = builder.Context.NamespaceUris.GetIndexOrAppend(kNamespaceUri);
                builder.AddRoot(new BaseObjectState(null)
                {
                    NodeId = new NodeId("Duplicate", ns),
                    BrowseName = new QualifiedName("First", ns)
                });
                builder.AddRoot(new BaseObjectState(null)
                {
                    NodeId = new NodeId("Duplicate", ns),
                    BrowseName = new QualifiedName("Second", ns)
                });
            });

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await manager.BuildAsync().ConfigureAwait(false))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdExists));
        }

        [Test]
        public void ParentInAnOwnedNamespaceMustBeAuthoredFirst()
        {
            using var manager = new AuthoringTestManager(builder => builder.AddObject(
                "Orphan",
                new NodeId("NeverAdded", builder.Context.NamespaceUris.GetIndexOrAppend(
                    kNamespaceUri))));

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await manager.BuildAsync().ConfigureAwait(false))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public void NamespaceZeroNodeIdsAreRejected()
        {
            using var manager = new AuthoringTestManager(builder => builder.AddRoot(
                new BaseObjectState(null)
                {
                    NodeId = new NodeId("InBaseNamespace", 0),
                    BrowseName = new QualifiedName("InBaseNamespace", 1)
                }));

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await manager.BuildAsync().ConfigureAwait(false))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public async Task AddingAfterRegistrationIsRejectedAsync()
        {
            INodeManagerBuilder? retained = null;
            using var manager = new AuthoringTestManager(builder => retained = builder);
            await manager.BuildAsync().ConfigureAwait(false);

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => retained!.AddFolder("TooLate"))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public async Task RegisteringTwiceIsRejectedAsync()
        {
            using var manager = new AuthoringTestManager(builder => builder.AddFolder("Machines"));
            await manager.BuildAsync().ConfigureAwait(false);

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await manager.RegisterAgainAsync().ConfigureAwait(false))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public async Task ManagerWithoutAuthoredNodesRegistersNothingAsync()
        {
            using var manager = new AuthoringTestManager(_ => { });
            await manager.BuildAsync().ConfigureAwait(false);

            Assert.That(manager.PredefinedNodeCount, Is.Zero);
        }


        [Test]
        public async Task QualifiedNameOverloadsCreateInTheGivenNamespaceAsync()
        {
            using var manager = new AuthoringTestManager(builder =>
            {
                ushort ns = builder.Context.NamespaceUris.GetIndexOrAppend(kNamespaceUri);
                builder.AddObject(new QualifiedName("Press", ns));
                builder.AddVariable<int>(new QualifiedName("Count", ns));
                builder.AddMethod(new QualifiedName("Halt", ns));
            });
            await manager.BuildAsync().ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(manager.FindByBrowseName("Press"), Is.InstanceOf<BaseObjectState>());
                Assert.That(manager.FindByBrowseName("Count"), Is.InstanceOf<BaseDataVariableState>());
                Assert.That(manager.FindByBrowseName("Halt"), Is.InstanceOf<MethodState>());
            });
        }

        [Test]
        public async Task AddObjectAppliesAnExplicitTypeDefinitionAsync()
        {
            using var manager = new AuthoringTestManager(
                builder => builder.AddObject("Press", default, ObjectTypeIds.FolderType));
            await manager.BuildAsync().ConfigureAwait(false);

            var press = (BaseObjectState)manager.FindByBrowseName("Press");
            Assert.That(press.TypeDefinitionId, Is.EqualTo(ObjectTypeIds.FolderType));
        }

        [Test]
        public void CreatedNodesAreVisibleToTypeAndDataTypeLookups()
        {
            using var manager = new AuthoringTestManager(builder =>
            {
                builder.AddObject("Press", default, ObjectTypeIds.FolderType);
                builder.AddVariable<float>("Ratio");

                Assert.Multiple(() =>
                {
                    Assert.That(
                        builder.NodeFromTypeId(ObjectTypeIds.FolderType).Node.BrowseName.Name,
                        Is.EqualTo("Press"));
                    Assert.That(
                        builder.VariableFromDataTypeId<float>(DataTypeIds.Float).Node.BrowseName.Name,
                        Is.EqualTo("Ratio"));
                });
            });

            Assert.DoesNotThrowAsync(async () => await manager.BuildAsync().ConfigureAwait(false));
        }

        [Test]
        public async Task GrandchildrenAreRegisteredWithTheirRootAsync()
        {
            using var manager = new AuthoringTestManager(builder =>
            {
                INodeBuilder<FolderState> plant = builder.AddFolder("Plant");
                INodeBuilder<BaseObjectState> line = builder.AddObject(
                    "Line",
                    plant.Node.NodeId);
                builder.AddVariable<int>("Speed", line.Node.NodeId);
            });
            await manager.BuildAsync().ConfigureAwait(false);

            NodeState speed = manager.FindByBrowseName("Speed");
            Assert.Multiple(() =>
            {
                // One root registered; the subtree came along with it.
                Assert.That(manager.PredefinedNodeCount, Is.EqualTo(3));
                Assert.That(
                    ((BaseInstanceState)speed).Parent!.BrowseName.Name,
                    Is.EqualTo("Line"));
                Assert.That(speed.NodeId.NamespaceIndex, Is.EqualTo(manager.TestNamespaceIndex));
            });
        }

        [Test]
        public async Task AddingAnExistingChildDoesNotDuplicateItAsync()
        {
            using var manager = new AuthoringTestManager(builder =>
            {
                ushort ns = builder.Context.NamespaceUris.GetIndexOrAppend(kNamespaceUri);
                var parent = new BaseObjectState(null)
                {
                    NodeId = new NodeId("Parent", ns),
                    BrowseName = new QualifiedName("Parent", ns)
                };
                var child = new BaseDataVariableState(parent)
                {
                    NodeId = new NodeId("Parent.Child", ns),
                    BrowseName = new QualifiedName("Child", ns),
                    DataType = DataTypeIds.Int32,
                    ValueRank = ValueRanks.Scalar
                };
                parent.AddChild(child);
                builder.AddRoot(parent);

                // Re-adding the same instance against the same parent must not
                // append a second child entry.
                builder.Add(child, parent.NodeId);
            });
            await manager.BuildAsync().ConfigureAwait(false);

            var parentNode = (BaseObjectState)manager.FindByBrowseName("Parent");
            var children = new List<BaseInstanceState>();
            parentNode.GetChildren(manager.TestSystemContext, children);
            Assert.That(children, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task AddRootIsIdempotentForTheSameInstanceAsync()
        {
            using var manager = new AuthoringTestManager(builder =>
            {
                ushort ns = builder.Context.NamespaceUris.GetIndexOrAppend(kNamespaceUri);
                var root = new BaseObjectState(null)
                {
                    NodeId = new NodeId("Root", ns),
                    BrowseName = new QualifiedName("Root", ns)
                };
                builder.AddRoot(root);
                builder.AddRoot(root);
            });
            await manager.BuildAsync().ConfigureAwait(false);

            Assert.That(manager.PredefinedNodeCount, Is.EqualTo(1));
        }

        [Test]
        public void NullArgumentsAreRejected()
        {
            using var manager = new AuthoringTestManager(builder =>
            {
                Assert.Multiple(() =>
                {
                    Assert.Throws<ArgumentNullException>(
                        () => builder.Add((Func<NodeState?, BaseObjectState>)null!));
                    Assert.Throws<ArgumentNullException>(
                        () => builder.Add((BaseObjectState)null!));
                    Assert.Throws<ArgumentNullException>(
                        () => builder.AddRoot((BaseObjectState)null!));
                });
            });

            Assert.DoesNotThrowAsync(async () => await manager.BuildAsync().ConfigureAwait(false));
        }

        [Test]
        public void RegisterAuthoredNodesRejectsANullRegisterDelegate()
        {
            using var manager = new AuthoringTestManager(_ => { });
            NodeManagerBuilder builder = manager.CreateBuilder();

            Assert.ThrowsAsync<ArgumentNullException>(
                async () => await builder
                    .RegisterAuthoredNodesAsync(null!)
                    .ConfigureAwait(false));
        }

        [Test]
        public async Task AddingAfterRegistrationButBeforeSealIsRejectedAsync()
        {
            using var manager = new AuthoringTestManager(_ => { });
            NodeManagerBuilder builder = manager.CreateBuilder();
            await builder
                .RegisterAuthoredNodesAsync((_, _) => default)
                .ConfigureAwait(false);

            // The builder is registered but not sealed, so this is the
            // "graph already handed over" guard rather than the seal guard.
            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => builder.AddFolder("TooLate"))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
        }

        [Test]
        public void EmptyBrowseNamesAreRejected()
        {
            using var manager = new AuthoringTestManager(builder =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(
                        Assert.Throws<ServiceResultException>(
                            () => builder.AddFolder(string.Empty))!.StatusCode,
                        Is.EqualTo(StatusCodes.BadBrowseNameInvalid));
                    Assert.That(
                        Assert.Throws<ServiceResultException>(
                            () => builder.AddFolder(default(QualifiedName)))!.StatusCode,
                        Is.EqualTo(StatusCodes.BadBrowseNameInvalid));
                });
            });

            Assert.DoesNotThrowAsync(async () => await manager.BuildAsync().ConfigureAwait(false));
        }

        [Test]
        public void ANodeWithoutABrowseNameIsRejected()
        {
            using var manager = new AuthoringTestManager(builder => builder.AddRoot(
                new BaseObjectState(null)
                {
                    NodeId = new NodeId("NoBrowseName", 2)
                }));

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await manager.BuildAsync().ConfigureAwait(false))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadBrowseNameInvalid));
        }

        [Test]
        public void ANonInstanceNodeCannotBeGivenAParent()
        {
            using var manager = new AuthoringTestManager(builder =>
            {
                ushort ns = builder.Context.NamespaceUris.GetIndexOrAppend(kNamespaceUri);
                INodeBuilder<FolderState> folder = builder.AddFolder("Machines");
                builder.Add(
                    new BaseObjectTypeState
                    {
                        NodeId = new NodeId("SomeType", ns),
                        BrowseName = new QualifiedName("SomeType", ns)
                    },
                    folder.Node.NodeId);
            });

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await manager.BuildAsync().ConfigureAwait(false))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNodeClassInvalid));
        }

        [Test]
        public void AParentIdThatContradictsAnExistingParentIsRejected()
        {
            using var manager = new AuthoringTestManager(builder =>
            {
                ushort ns = builder.Context.NamespaceUris.GetIndexOrAppend(kNamespaceUri);
                INodeBuilder<FolderState> a = builder.AddFolder("A");
                INodeBuilder<FolderState> b = builder.AddFolder("B");

                var child = new BaseObjectState(a.Node)
                {
                    NodeId = new NodeId("Child", ns),
                    BrowseName = new QualifiedName("Child", ns)
                };
                builder.Add(child, b.Node.NodeId);
            });

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await manager.BuildAsync().ConfigureAwait(false))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
        }

        [Test]
        public void AnExistingParentWithoutANodeIdIsRejected()
        {
            using var manager = new AuthoringTestManager(builder =>
            {
                ushort ns = builder.Context.NamespaceUris.GetIndexOrAppend(kNamespaceUri);
                var parent = new BaseObjectState(null)
                {
                    BrowseName = new QualifiedName("Unidentified", ns)
                };
                builder.Add(new BaseObjectState(parent)
                {
                    BrowseName = new QualifiedName("Child", ns)
                });
            });

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await manager.BuildAsync().ConfigureAwait(false))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public void AnExistingParentOutsideTheGraphIsRejected()
        {
            using var manager = new AuthoringTestManager(builder =>
            {
                ushort ns = builder.Context.NamespaceUris.GetIndexOrAppend(kNamespaceUri);
                var parent = new BaseObjectState(null)
                {
                    NodeId = new NodeId("Stranger", ns),
                    BrowseName = new QualifiedName("Stranger", ns)
                };
                builder.Add(new BaseObjectState(parent)
                {
                    BrowseName = new QualifiedName("Child", ns)
                });
            });

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await manager.BuildAsync().ConfigureAwait(false))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdUnknown));
        }

        [Test]
        public void ANodeIdInAnUnownedNamespaceIsRejected()
        {
            using var manager = new AuthoringTestManager(builder => builder.AddRoot(
                new BaseObjectState(null)
                {
                    NodeId = new NodeId("Foreign", 99),
                    BrowseName = new QualifiedName("Foreign", 99)
                }));

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await manager.BuildAsync().ConfigureAwait(false))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public void CollidingWithAPredefinedNodeIsRejected()
        {
            using var manager = new AuthoringTestManager(_ => { });
            NodeId taken = new("Taken", manager.TestNamespaceIndex);
            manager.SeedPredefinedNode(taken);

            NodeManagerBuilder builder = manager.CreateBuilder();
            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => builder.AddRoot(new BaseObjectState(null)
                {
                    NodeId = taken,
                    BrowseName = new QualifiedName("Taken", manager.TestNamespaceIndex)
                }))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdExists));
        }

        [Test]
        public void ANodeIdFactoryThatAssignsNothingIsReported()
        {
            using var manager = new AuthoringTestManager(_ => { });
            manager.UseNullNodeIdFactory();

            NodeManagerBuilder builder = manager.CreateBuilder();
            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => builder.AddFolder("Machines"))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        [Test]
        public void CreationRequiresAnAsyncCustomNodeManagerBackedBuilder()
        {
            var builder = new NodeManagerBuilder(
                new SystemContext(telemetry: null!) { NodeIdFactory = new FixedNodeIdFactory() },
                Mock.Of<IAsyncNodeManager>(),
                defaultNamespaceIndex: 2,
                rootResolver: _ => null!,
                nodeIdResolver: _ => null!,
                typeIdResolver: _ => []);

            ServiceResultException exception = Assert.Throws<ServiceResultException>(
                () => builder.AddFolder("Machines"))!;
            Assert.That(exception.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
        }

        private sealed class FixedNodeIdFactory : INodeIdFactory
        {
            public NodeId New(ISystemContext context, NodeState node)
            {
                return new NodeId(node.BrowseName.Name!, 2);
            }
        }

        private sealed class NullNodeIdFactory : INodeIdFactory
        {
            public NodeId New(ISystemContext context, NodeState node)
            {
                return NodeId.Null;
            }
        }


        [Test]
        public async Task ASubtreeMaterialisedFromATypeModelIsRebasedOffTheDeclarationIdsAsync()
        {
            // NodeState.Create(..., assignNodeIds: false) leaves every child
            // carrying its declaration NodeId: non-null, and in the model's own
            // namespace rather than ns 0. Staging must rebase those before the
            // per-node builder is handed back, otherwise the ids collide with
            // the type-model nodes already in PredefinedNodes.
            using var manager = new AuthoringTestManager(_ => { });
            ushort ns = manager.TestNamespaceIndex;
            NodeId declarationId = new("Declaration.Child", ns);
            manager.SeedTypeHierarchyNode(declarationId);

            NodeManagerBuilder builder = manager.CreateBuilder();
            var instance = new BaseObjectState(null)
            {
                BrowseName = new QualifiedName("Instance", ns)
            };
            var child = new BaseDataVariableState(instance)
            {
                NodeId = declarationId,
                BrowseName = new QualifiedName("Child", ns),
                DataType = DataTypeIds.Int32,
                ValueRank = ValueRanks.Scalar
            };
            instance.AddChild(child);

            INodeBuilder<BaseObjectState> staged = builder.Add(instance);
            await manager.RegisterAsync(builder).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                Assert.That(
                    child.NodeId,
                    Is.Not.EqualTo(declarationId),
                    "the child must be rebased off the declaration id");
                Assert.That(
                    manager.ContainsPredefined(child.NodeId),
                    Is.True,
                    "the rebased child must be the id that got registered");
                Assert.That(
                    manager.ContainsPredefined(staged.Node.NodeId),
                    Is.True);
            });
        }

        [Test]
        public async Task ASubtreeMaterialisedFromAStandardTypeIsRebasedOffTheDeclarationIdsAsync()
        {
            // The same case as above, except the type is a standard one, so
            // its declaration ids are in namespace 0. Those belong to the
            // CoreNodeManager rather than to this manager's PredefinedNodes,
            // where the declaration-collision check looks, so the namespace-0
            // test is the only thing that can catch them - on the root as
            // much as on its children.
            using var manager = new AuthoringTestManager(_ => { });
            ushort ns = manager.TestNamespaceIndex;

            NodeManagerBuilder builder = manager.CreateBuilder();
            var instance = new BaseObjectState(null)
            {
                NodeId = ObjectIds.Server_ServerCapabilities,
                BrowseName = new QualifiedName("Capabilities", ns)
            };
            var child = new BaseDataVariableState(instance)
            {
                NodeId = VariableIds.Server_ServerCapabilities_MaxBrowseContinuationPoints,
                BrowseName = new QualifiedName("MaxBrowseContinuationPoints", ns),
                DataType = DataTypeIds.UInt16,
                ValueRank = ValueRanks.Scalar
            };
            instance.AddChild(child);

            INodeBuilder<BaseObjectState> staged = builder.Add(instance);
            await manager.RegisterAsync(builder).ConfigureAwait(false);

            Assert.Multiple(() =>
            {
                // the root is the half that regressed: exempting it from the
                // namespace-0 test also disabled the test for every
                // descendant, because the test read the root's namespace.
                Assert.That(
                    staged.Node.NodeId.NamespaceIndex,
                    Is.EqualTo(ns),
                    "the root must be rebased off the namespace-0 declaration id");
                Assert.That(
                    child.NodeId.NamespaceIndex,
                    Is.EqualTo(ns),
                    "the child must be rebased off the namespace-0 declaration id");
                Assert.That(manager.ContainsPredefined(staged.Node.NodeId), Is.True);
                Assert.That(manager.ContainsPredefined(child.NodeId), Is.True);
            });
        }

        [Test]
        public async Task AddObjectOnAParentQualifiesAStringNameWithTheBuilderNamespaceAsync()
        {
            NodeState? child = null;

            using var manager = new AuthoringTestManager(builder =>
            {
                INodeBuilder<FolderState> folder = builder.AddFolder("Machines");
                child = folder.AddObject("Press").Node;
            });
            await manager.BuildAsync().ConfigureAwait(false);

            Assert.That(
                child!.BrowseName.NamespaceIndex,
                Is.EqualTo(manager.TestNamespaceIndex),
                "the string overload qualifies with the builder's own namespace");
        }

        [Test]
        public void AddObjectOnAParentRefusesANamespaceZeroBrowseName()
        {
            using var manager = new AuthoringTestManager(_ => { });
            NodeManagerBuilder builder = manager.CreateBuilder();
            INodeBuilder<FolderState> folder = builder.AddFolder("Machines");

            Assert.Multiple(() =>
            {
                // namespace 0 is the OPC UA namespace, which no NodeManager
                // owns, so authoring into it is refused rather than accepted
                // and silently attributed to someone else.
                ServiceResultException namespaceZero = Assert.Throws<ServiceResultException>(
                    () => folder.AddObject(new QualifiedName("Press")))!;
                Assert.That(
                    namespaceZero.StatusCode,
                    Is.EqualTo(StatusCodes.BadBrowseNameInvalid));

                ServiceResultException empty = Assert.Throws<ServiceResultException>(
                    () => folder.AddObject(default(QualifiedName)))!;
                Assert.That(empty.StatusCode, Is.EqualTo(StatusCodes.BadBrowseNameInvalid));

                ServiceResultException emptyString = Assert.Throws<ServiceResultException>(
                    () => folder.AddObject(string.Empty))!;
                Assert.That(emptyString.StatusCode, Is.EqualTo(StatusCodes.BadBrowseNameInvalid));
            });
        }

        private static bool HasInverseReference(
            NodeState node,
            NodeId referenceTypeId,
            NodeId targetId)
        {
            var references = new List<IReference>();
            node.GetReferences(new SystemContext(telemetry: null!), references);
            foreach (IReference reference in references)
            {
                if (reference.IsInverse &&
                    reference.ReferenceTypeId == referenceTypeId &&
                    reference.TargetId == targetId)
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// A hand-written fluent manager that reproduces the order the
        /// source-generated <c>CreateAddressSpaceAsync</c> uses: Configure,
        /// register authored nodes, complete configure, seal.
        /// </summary>
        private sealed class AuthoringTestManager : FluentNodeManagerBase
        {
            public AuthoringTestManager(Action<INodeManagerBuilder> configure)
                : base(CreateMockServer(), kNamespaceUri)
            {
                m_configure = configure;
            }

            public ushort TestNamespaceIndex => NamespaceIndexes[0];

            public ISystemContext TestSystemContext => SystemContext;

            public int PredefinedNodeCount => PredefinedNodes.Count;

            public async Task BuildAsync()
            {
                var externalReferences = new Dictionary<NodeId, IList<IReference>>();
                m_builder = CreateFluentBuilder(TestNamespaceIndex);
                m_configure(m_builder);
                await RegisterAuthoredNodesAsync(m_builder).ConfigureAwait(false);
                await CompleteConfigureAsync(externalReferences).ConfigureAwait(false);
                await m_builder.SealAsync();
            }

            public ValueTask RegisterAgainAsync()
            {
                return RegisterAuthoredNodesAsync(m_builder!, CancellationToken.None);
            }

            public NodeManagerBuilder CreateBuilder()
            {
                m_builder = CreateFluentBuilder(TestNamespaceIndex);
                return m_builder;
            }

            public void SeedTypeHierarchyNode(NodeId nodeId)
            {
                PredefinedNodes[nodeId] = new BaseDataVariableState(null)
                {
                    NodeId = nodeId,
                    BrowseName = new QualifiedName("Declaration", nodeId.NamespaceIndex),
                    IsPartOfTypeHierarchy = true
                };
            }

            public ValueTask RegisterAsync(NodeManagerBuilder builder)
            {
                return RegisterAuthoredNodesAsync(builder, CancellationToken.None);
            }
            public void SeedPredefinedNode(NodeId nodeId)
            {
                PredefinedNodes[nodeId] = new BaseObjectState(null)
                {
                    NodeId = nodeId,
                    BrowseName = new QualifiedName("Seeded", nodeId.NamespaceIndex)
                };
            }

            public void UseNullNodeIdFactory()
            {
                SystemContext.NodeIdFactory = new NullNodeIdFactory();
            }
            public bool ContainsPredefined(NodeId nodeId)
            {
                return PredefinedNodes.ContainsKey(nodeId);
            }

            public NodeState FindById(NodeId nodeId)
            {
                Assert.That(PredefinedNodes.TryGetValue(nodeId, out NodeState? node), Is.True);
                return node!;
            }

            public NodeState FindByBrowseName(string name)
            {
                foreach (NodeState node in PredefinedNodes.Values)
                {
                    if (node.BrowseName.Name == name)
                    {
                        return node;
                    }
                }
                Assert.Fail($"No predefined node named '{name}'.");
                return null!;
            }

            private static IServerInternal CreateMockServer()
            {
                var namespaceUris = new NamespaceTable();
                namespaceUris.Append(Ua.Namespaces.OpcUa);

                var telemetry = new Mock<ITelemetryContext>();
                telemetry
                    .SetupGet(context => context.LoggerFactory)
                    .Returns(NullLoggerFactory.Instance);

                var server = new Mock<IServerInternal>();
                server.SetupGet(value => value.NamespaceUris).Returns(namespaceUris);
                server.SetupGet(value => value.Telemetry).Returns(telemetry.Object);
                server.SetupGet(value => value.MessageContext)
                    .Returns(ServiceMessageContext.Create(telemetry.Object));
                server.SetupGet(value => value.DefaultSystemContext)
                    .Returns(new ServerSystemContext(server.Object));
                return server.Object;
            }

            private readonly Action<INodeManagerBuilder> m_configure;
            private NodeManagerBuilder? m_builder;
        }

        private sealed class CustomObjectState : BaseObjectState
        {
            public CustomObjectState(NodeState? parent)
                : base(parent)
            {
            }
        }
    }
}
