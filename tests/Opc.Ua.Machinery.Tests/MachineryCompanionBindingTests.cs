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
using Moq;
using NUnit.Framework;
using Opc.Ua.ISA95.Server.Providers;
using Opc.Ua.Machinery.Jobs;
using Opc.Ua.Machinery.Result;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Machinery.Server.Results;

namespace Opc.Ua.Machinery.Tests
{
    /// <summary>
    /// Exercises companion bindings through the existing Machinery builders.
    /// </summary>
    [TestFixture]
    [Category("Machinery")]
    public sealed class MachineryCompanionBindingTests
    {
        /// <summary>
        /// Deleting a registered root releases only that machine's bindings, not every resource on the shared owner.
        /// </summary>
        [Test]
        public async Task RemovedMachineReleasesOnlyItsOwnedBindingResourcesAsync()
        {
            await using var fixture = new MachineryServerFixture();
            await fixture.StartAsync();
            IMachineryBuildContext context = fixture.CreateBuildContext();
            var first = new Mock<IAsyncDisposable>();
            var second = new Mock<IAsyncDisposable>();
            first.Setup(value => value.DisposeAsync()).Returns(default(ValueTask));
            second.Setup(value => value.DisposeAsync()).Returns(default(ValueTask));
            IMachineHandle<BaseObjectState> firstMachine = await context
                .AddMachine(new QualifiedName("FirstLifetime"))
                .WithRegistrationBinding((_, _, _) => new ValueTask<IAsyncDisposable>(first.Object))
                .BuildAsync();
            IMachineHandle<BaseObjectState> secondMachine = await context
                .AddMachine(new QualifiedName("SecondLifetime"))
                .WithRegistrationBinding((_, _, _) => new ValueTask<IAsyncDisposable>(second.Object))
                .BuildAsync();

            Assert.That(await fixture.Manager.DeleteNodeAsync(fixture.Manager.SystemContext, firstMachine.NodeId),
                Is.True);

            first.Verify(value => value.DisposeAsync(), Times.Once);
            second.Verify(value => value.DisposeAsync(), Times.Never);
            Assert.That(fixture.Manager.FindPredefinedNode(secondMachine.NodeId), Is.SameAs(secondMachine.State));
            Assert.That(await fixture.Manager.DeleteNodeAsync(fixture.Manager.SystemContext, secondMachine.NodeId),
                Is.True);
            second.Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// A callback failure cannot prevent independent resources in the same removed machine from being released.
        /// </summary>
        [Test]
        public async Task RemovedMachineAttemptsEveryOwnedResourceWhenCleanupFailsAsync()
        {
            await using var fixture = new MachineryServerFixture();
            await fixture.StartAsync();
            IMachineryBuildContext context = fixture.CreateBuildContext();
            var first = new Mock<IAsyncDisposable>();
            var failing = new Mock<IAsyncDisposable>();
            first.Setup(value => value.DisposeAsync()).Returns(default(ValueTask));
            failing.Setup(value => value.DisposeAsync()).Throws(new InvalidOperationException("owned cleanup failed"));
            IMachineHandle<BaseObjectState> machine = await context
                .AddMachine(new QualifiedName("FailingLifetime"))
                .WithRegistrationBinding((_, _, _) => new ValueTask<IAsyncDisposable>(first.Object))
                .WithRegistrationBinding((_, _, _) => new ValueTask<IAsyncDisposable>(failing.Object))
                .BuildAsync();

            InvalidOperationException error = (await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await fixture.Manager.DeleteNodeAsync(fixture.Manager.SystemContext, machine.NodeId)))!;

            Assert.That(error.Message, Is.EqualTo("owned cleanup failed"));
            first.Verify(value => value.DisposeAsync(), Times.Once);
            failing.Verify(value => value.DisposeAsync(), Times.Once);
        }

        /// <summary>
        /// Existing component containers retain their identity and reject an already occupied component name.
        /// </summary>
        [Test]
        public async Task ExistingComponentsBindWithoutReplacementOrDuplicateChildrenAsync()
        {
            await using var fixture = new MachineryServerFixture();
            await fixture.StartAsync();
            IMachineryBuildContext context = fixture.CreateBuildContext();
            IMachineBuilder<BaseObjectState> machine = context.AddMachine(new QualifiedName("AdoptComponents"));
            var name = new QualifiedName("Components",
                (ushort)context.Context.NamespaceUris.GetIndex(Opc.Ua.Machinery.Namespaces.Machinery));
            MachineComponentsState components = context.Context.CreateInstanceOfMachineComponentsType(machine.State, name);
            components.ReferenceTypeId = ReferenceTypeIds.HasAddIn;
            machine.State.AddChild(components);
            BaseObjectState existing = context.Context.CreateInstanceOfBaseObjectType(
                components, new QualifiedName("Existing", context.InstanceNamespaceIndex));
            existing.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            components.AddChild(existing);
            NodeId identity = components.NodeId;

            machine.WithComponents(components, builder =>
            {
                ServiceResultException duplicate = Assert.Throws<ServiceResultException>(() =>
                    builder.AddComponent(new QualifiedName("Existing"), _ => { }))!;
                Assert.That(duplicate.StatusCode, Is.EqualTo(StatusCodes.BadBrowseNameDuplicated));
                builder.AddComponent(new QualifiedName("New"), component => component.WithIdentification(data =>
                {
                    data.Manufacturer = new LocalizedText("Component test");
                    data.SerialNumber = "New-1";
                    data.ProductInstanceUri = "urn:test:component:new";
                }));
            });
            await machine.BuildAsync();

            Assert.That(components.NodeId, Is.EqualTo(identity));
            Assert.That(components.Parent, Is.SameAs(machine.State));
            Assert.That(components.BrowseName, Is.EqualTo(name));
            Assert.That(components.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasAddIn));
            Assert.That(fixture.Manager.FindPredefinedNode(identity), Is.SameAs(components));
            var children = new List<BaseInstanceState>();
            machine.State.GetChildren(context.Context, children);
            Assert.That(children.FindAll(node => node is MachineComponentsState), Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Adoption rejects another machine's container and incompatible type/reference metadata without rewriting it.
        /// </summary>
        [Test]
        public async Task ExistingComponentsRejectForeignOrInvalidDeclarationsAsync()
        {
            await using var fixture = new MachineryServerFixture();
            await fixture.StartAsync();
            IMachineryBuildContext context = fixture.CreateBuildContext();
            IMachineBuilder<BaseObjectState> owner = context.AddMachine(new QualifiedName("ComponentOwner"));
            IMachineBuilder<BaseObjectState> other = context.AddMachine(new QualifiedName("OtherOwner"));
            MachineComponentsState components = context.Context.CreateInstanceOfMachineComponentsType(
                owner.State, new QualifiedName("Components", context.InstanceNamespaceIndex));
            components.ReferenceTypeId = ReferenceTypeIds.HasAddIn;
            owner.State.AddChild(components);
            ServiceResultException foreign = Assert.Throws<ServiceResultException>(
                () => other.WithComponents(components, _ => { }))!;
            Assert.That(foreign.StatusCode, Is.EqualTo(StatusCodes.BadInvalidArgument));
            NodeId originalType = components.TypeDefinitionId;
            components.TypeDefinitionId = Opc.Ua.ObjectTypeIds.BaseObjectType;
            ServiceResultException wrongType = Assert.Throws<ServiceResultException>(
                () => owner.WithComponents(components, _ => { }))!;
            Assert.That(wrongType.StatusCode, Is.EqualTo(StatusCodes.BadTypeDefinitionInvalid));
            components.TypeDefinitionId = originalType;
            components.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            ServiceResultException wrongReference = Assert.Throws<ServiceResultException>(
                () => owner.WithComponents(components, _ => { }))!;
            Assert.That(wrongReference.StatusCode, Is.EqualTo(StatusCodes.BadTypeDefinitionInvalid));
            Assert.That(components.Parent, Is.SameAs(owner.State));
            Assert.That(components.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasComponent));
            await owner.AbortAsync();
            await other.AbortAsync();
        }

        /// <summary>
        /// Existing machine and nested component nameplates retain their declared identities.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task ExistingIdentificationKeepsItsDeclaredLocationAsync(bool machineIdentification)
        {
            await using var fixture = new MachineryServerFixture();
            await fixture.StartAsync();
            IMachineryBuildContext context = fixture.CreateBuildContext();
            IMachineBuilder<BaseObjectState> machine = context.AddMachine(new QualifiedName("IdentifiedCompanion"));
            NodeState parent = machine.State;
            if (!machineIdentification)
            {
                FolderState information = context.Context.CreateInstanceOfFolderType(
                    machine.State,
                    new QualifiedName("DeviceInformation", context.InstanceNamespaceIndex));
                machine.State.AddChild(information);
                parent = information;
            }
            ushort diIndex = (ushort)context.Context.NamespaceUris.GetIndex(Opc.Ua.Di.Namespaces.OpcUaDi);
            var name = new QualifiedName(Opc.Ua.Di.BrowseNames.Identification, diIndex);
            MachineryItemIdentificationState identification = machineIdentification
                ? context.Context.CreateInstanceOfMachineIdentificationType(parent, name)
                : context.Context.CreateInstanceOfMachineryComponentIdentificationType(parent, name);
            identification.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
            parent.AddChild(identification);
            NodeId originalId = identification.NodeId;

            await machine.WithIdentification(identification, data =>
            {
                data.Manufacturer = new LocalizedText("Test manufacturer");
                data.SerialNumber = "Existing-1";
                data.ProductInstanceUri = "urn:test:companion:existing-1";
            }).BuildAsync();

            Assert.That(identification.Parent, Is.SameAs(parent));
            Assert.That(identification.NodeId, Is.EqualTo(originalId));
            Assert.That(identification.BrowseName, Is.EqualTo(name));
            Assert.That(identification.ReferenceTypeId, Is.EqualTo(Opc.Ua.ReferenceTypeIds.HasComponent));
            Assert.That(identification.Manufacturer!.Value.Text, Is.EqualTo("Test manufacturer"));
            Assert.That(identification.SerialNumber!.Value, Is.EqualTo("Existing-1"));
            Assert.That(parent.FindChild(context.Context, name), Is.SameAs(identification));
            var siblings = new List<BaseInstanceState>();
            parent.GetChildren(context.Context, siblings);
            Assert.That(siblings.FindAll(child => child.BrowseName == name), Has.Count.EqualTo(1));
        }

        /// <summary>
        /// A companion may declare the base item nameplate without claiming a machine or component specialization.
        /// </summary>
        [Test]
        public async Task ExistingBaseItemIdentificationPreservesTypeWithoutClaimingComponentFacetsAsync()
        {
            await using var fixture = new MachineryServerFixture();
            await fixture.StartAsync();
            IMachineryBuildContext context = fixture.CreateBuildContext();
            IMachineBuilder<BaseObjectState> machine = context.AddMachine(new QualifiedName("ItemNameplate"));
            BaseObjectState accessory = context.Context.CreateInstanceOfBaseObjectType(
                machine.State, new QualifiedName("Accessory", context.InstanceNamespaceIndex));
            machine.State.AddChild(accessory);
            var name = new QualifiedName("Identification",
                (ushort)context.Context.NamespaceUris.GetIndex(Opc.Ua.Di.Namespaces.OpcUaDi));
            MachineryItemIdentificationState identification =
                context.Context.CreateInstanceOfMachineryItemIdentificationType(accessory, name);
            identification.ReferenceTypeId = ReferenceTypeIds.HasComponent;
            accessory.AddChild(identification);
            NodeId type = identification.TypeDefinitionId;
            NodeId nodeId = identification.NodeId;

            await machine.WithIdentification(identification, data =>
            {
                data.Manufacturer = new LocalizedText("Accessory supplier");
                data.SerialNumber = "Item-1";
                data.ComponentName = new LocalizedText("Native item");
                data.Writable = true;
            }).BuildAsync();

            Assert.That(identification.TypeDefinitionId, Is.EqualTo(type));
            Assert.That(identification.NodeId, Is.EqualTo(nodeId));
            Assert.That(identification.Parent, Is.SameAs(accessory));
            Assert.That(identification.BrowseName, Is.EqualTo(name));
            Assert.That(identification.Manufacturer!.Value.Text, Is.EqualTo("Accessory supplier"));
            Assert.That(identification.SerialNumber!.Value, Is.EqualTo("Item-1"));
            Assert.That(identification.ComponentName!.Value.Text, Is.EqualTo("Native item"));
            foreach (QualifiedName unit in fixture.Manager.ConformanceUnits)
            {
                Assert.That(unit.Name, Is.Not.EqualTo(ConformanceUnits.ComponentIdentification));
                Assert.That(unit.Name, Is.Not.EqualTo(ConformanceUnits.ComponentIdentificationMandatory));
                Assert.That(unit.Name, Is.Not.EqualTo(ConformanceUnits.ComponentIdentificationWritable));
                Assert.That(unit.Name, Is.Not.EqualTo(ConformanceUnits.MachineIdentification));
            }
        }

        /// <summary>
        /// Invalid machine identification is rejected before changing an existing nameplate.
        /// </summary>
        [Test]
        public async Task InvalidIdentificationDoesNotPartiallyRewriteTheExistingNameplateAsync()
        {
            await using var fixture = new MachineryServerFixture();
            await fixture.StartAsync();
            IMachineryBuildContext context = fixture.CreateBuildContext();
            IMachineBuilder<BaseObjectState> machine = context.AddMachine(new QualifiedName("InvalidIdentity"));
            MachineIdentificationState identification = context.Context.CreateInstanceOfMachineIdentificationType(
                machine.State,
                new QualifiedName("Identification", context.InstanceNamespaceIndex));
            machine.State.AddChild(identification);
            identification.Manufacturer!.Value = new LocalizedText("Original");
            identification.SerialNumber!.Value = "Original-1";
            identification.ProductInstanceUri!.Value = "urn:test:original";

            ServiceResultException error = Assert.Throws<ServiceResultException>(() =>
                machine.WithIdentification(identification, data =>
                {
                    data.Manufacturer = new LocalizedText("Replacement");
                    data.SerialNumber = "Replacement-1";
                }))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(identification.Manufacturer.Value.Text, Is.EqualTo("Original"));
            Assert.That(identification.SerialNumber.Value, Is.EqualTo("Original-1"));
            Assert.That(identification.ProductInstanceUri.Value, Is.EqualTo("urn:test:original"));
            await machine.BuildAsync();
        }

        [Test]
        public async Task ExistingResultManagementKeepsItsIdentityAndPublishesResultsAsync()
        {
            await using var fixture = new MachineryServerFixture();
            await fixture.StartAsync();
            IMachineryBuildContext context = fixture.CreateBuildContext();
            IMachineBuilder<BaseObjectState> builder = context.AddMachine(new QualifiedName("Companion"));
            var browseName = new QualifiedName("CompanionResults", context.InstanceNamespaceIndex);
            ResultManagementState management = context.Context.CreateInstanceOfResultManagementType(
                builder.State,
                browseName);
            management.ReferenceTypeId = Opc.Ua.ReferenceTypeIds.HasComponent;
            builder.State.AddChild(management);
            NodeId originalId = management.NodeId;
            NodeId originalType = management.TypeDefinitionId;

            IMachineHandle<BaseObjectState> machine = await builder
                .WithResultManagement(
                    management,
                    results => results.WithInMemoryStore(2).WithResultsFolder(1))
                .BuildAsync();
            await machine.Results!.PublishAsync(new MachineryResult(new ResultDataType
            {
                ResultMetaData = new ResultMetaDataType { ResultId = "R-1" },
                ResultContent = new[] { Variant.From(12.5) }.ToArrayOf()
            }));

            Assert.That(management.Parent, Is.SameAs(machine.State));
            Assert.That(management.BrowseName, Is.EqualTo(browseName));
            Assert.That(management.NodeId, Is.EqualTo(originalId));
            Assert.That(management.TypeDefinitionId, Is.EqualTo(originalType));
            Assert.That(management.ReferenceTypeId, Is.EqualTo(Opc.Ua.ReferenceTypeIds.HasComponent));
            Assert.That(
                fixture.Manager.FindPredefinedNode(management.GetLatestResult!.NodeId),
                Is.SameAs(management.GetLatestResult));
            var children = new List<BaseInstanceState>();
            management.Results!.GetChildren(context.Context, children);
            Assert.That(children, Has.Count.EqualTo(1));
            var slot = (ResultState)children[0];
            Assert.That(slot.Value!.ResultMetaData!.ResultId, Is.EqualTo("R-1"));
        }

        [Test]
        public async Task CompanionJobsRequireResponsesWithoutChangingLegacyReceiverOnlySetupAsync()
        {
            await using var fixture = new MachineryServerFixture();
            await fixture.StartAsync();
            IMachineryBuildContext context = fixture.CreateBuildContext();
            var receiver = new Mock<IIsa95JobOrderReceiverV2>(MockBehavior.Strict);
            IMachineHandle<BaseObjectState> legacy = await context
                .AddMachine(new QualifiedName("LegacyReceiverOnly"))
                .WithJobManagement(jobs => jobs.WithJobOrderReceiver(receiver.Object))
                .BuildAsync();
            Assert.That(legacy.NodeId.IsNull, Is.False);

            IMachineBuilder<BaseObjectState> builder = context.AddMachine(new QualifiedName("CompanionJobs"));
            JobManagementState management = context.Context.CreateInstanceOfJobManagementType(
                builder.State,
                new QualifiedName("Jobs", context.InstanceNamespaceIndex));
            builder.State.AddChild(management);
            builder.WithJobManagement(management, jobs => jobs.WithJobOrderReceiver(receiver.Object));

            ServiceResultException exception = (await Assert.ThrowsAsync<ServiceResultException>(
                async () => await builder.BuildAsync()))!;
            Assert.That(exception.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadConfigurationError));
            Assert.That(exception.Message, Does.Contain("response provider"));
            Assert.That(fixture.Manager.FindPredefinedNode(builder.State.NodeId), Is.Null);
            Assert.That(fixture.Manager.FindPredefinedNode(management.NodeId), Is.Null);
        }

        [Test]
        public async Task ResultFactoriesKeepSpecializedVariablesAndEventsAsync()
        {
            await using var fixture = new MachineryServerFixture();
            await fixture.StartAsync();
            IMachineryBuildContext context = fixture.CreateBuildContext();
            var slots = new List<ResultState>();
            ResultReadyEventState? createdEvent = null;
            ResultManagementState? management = null;
            NodeId eventBaseType = NodeId.Create(
                Opc.Ua.Machinery.Result.ObjectTypes.ResultReadyEventType,
                Opc.Ua.Machinery.Result.Namespaces.MachineryResult,
                context.Context.NamespaceUris);
            IMachineHandle<BaseObjectState> machine = await context
                .AddMachine(new QualifiedName("TypedResults"))
                .WithResultManagement(results =>
                {
                    management = results.State;
                    results.WithInMemoryStore(3)
                        .WithResultsFolder(2, (systemContext, parent, browseName) =>
                        {
                            ResultState slot = systemContext.CreateInstanceOfResultType(parent, browseName);
                            slots.Add(slot);
                            return slot;
                        })
                        .WithResultReadyEvents(eventBaseType, (systemContext, parent, result) =>
                        {
                            createdEvent = systemContext.CreateInstanceOfResultReadyEventType(parent, default);
                            ResultState content = systemContext.CreateInstanceOfResultType(createdEvent, default);
                            content.DisplayName = new LocalizedText("Specialized result");
                            createdEvent.CreateOrReplaceResult(systemContext, content);
                            return createdEvent;
                        });
                })
                .BuildAsync();
            var reported = new List<IFilterTarget>();
            management!.OnReportEventAsync = (_, _, value, _) =>
            {
                reported.Add(value);
                return default;
            };

            await machine.Results!.PublishAsync(new MachineryResult(new ResultDataType
            {
                ResultMetaData = new ResultMetaDataType { ResultId = "Typed-1" }
            }));
            await machine.Results.PublishAsync(new MachineryResult(new ResultDataType
            {
                ResultMetaData = new ResultMetaDataType { ResultId = "Typed-2" }
            }));

            Assert.That(slots[0].Value!.ResultMetaData!.ResultId, Is.EqualTo("Typed-2"));
            Assert.That(slots[1].Value!.ResultMetaData!.ResultId, Is.EqualTo("Typed-1"));
            Assert.That(fixture.Manager.FindPredefinedNode(slots[0].NodeId), Is.SameAs(slots[0]));
            Assert.That(reported, Has.Count.EqualTo(2));
            Assert.That(reported[1], Is.SameAs(createdEvent));
            Assert.That(createdEvent!.Result!.DisplayName.Text, Is.EqualTo("Specialized result"));
            Assert.That(createdEvent.Result.Value!.ResultMetaData!.ResultId, Is.EqualTo("Typed-2"));
            Assert.That(
                fixture.Manager.FindPredefinedNode<BaseObjectTypeState>(createdEvent.EventType!.Value)!.IsAbstract,
                Is.False);
        }

        [Test]
        public async Task ResultBindingsRejectForeignNodesAndDuplicateBindingsAsync()
        {
            await using var fixture = new MachineryServerFixture();
            await fixture.StartAsync();
            IMachineryBuildContext context = fixture.CreateBuildContext();
            IMachineBuilder<BaseObjectState> first = context.AddMachine(new QualifiedName("First"));
            IMachineBuilder<BaseObjectState> second = context.AddMachine(new QualifiedName("Second"));
            ResultManagementState management = context.Context.CreateInstanceOfResultManagementType(
                first.State,
                new QualifiedName("Results", context.InstanceNamespaceIndex));
            first.State.AddChild(management);

            ServiceResultException foreign = Assert.Throws<ServiceResultException>(() =>
                second.WithResultManagement(management, results => results.WithInMemoryStore()))!;
            Assert.That(foreign.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidArgument));
            first.WithResultManagement(management, results => results.WithInMemoryStore());
            ServiceResultException duplicate = Assert.Throws<ServiceResultException>(() =>
                first.WithResultManagement(management, results => results.WithInMemoryStore()))!;
            Assert.That(duplicate.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));

            await first.BuildAsync();
            await second.BuildAsync();
            Assert.That(management.Parent, Is.SameAs(first.State));
            Assert.That(fixture.Manager.FindPredefinedNode(management.NodeId), Is.SameAs(management));
        }

        /// <summary>
        /// Companion result samples keep their acquisition metadata when the fixed result ring advances.
        /// </summary>
        [Test]
        public async Task ResultProjectionPreservesAcquisitionMetadataAcrossRingSlotsAsync()
        {
            await using var fixture = new MachineryServerFixture();
            await fixture.StartAsync();
            IMachineryBuildContext context = fixture.CreateBuildContext();
            var slots = new List<ResultState>();
            int preparations = 0;
            DateTimeUtc firstTime = new DateTime(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
            DateTimeUtc secondTime = new DateTime(2026, 3, 1, 12, 0, 1, DateTimeKind.Utc);
            ResultReadyEventState? reported = null;
            ResultManagementState? management = null;
            IMachineHandle<BaseObjectState> machine = await context
                .AddMachine(new QualifiedName("ProjectedResults"))
                .WithResultManagement(results =>
                {
                    management = results.State;
                    results.WithInMemoryStore(1)
                        .WithResultsFolder(2, (systemContext, parent, name) =>
                        {
                            ResultState slot = systemContext.CreateInstanceOfResultType(parent, name);
                            slots.Add(slot);
                            return slot;
                        })
                        .WithResultProjection(result =>
                        {
                            preparations++;
                            DateTimeUtc timestamp = result.ResultId == "First" ? firstTime : secondTime;
                            StatusCode quality = result.ResultId == "First"
                                ? StatusCodes.UncertainLastUsableValue : StatusCodes.BadNoData;
                            return (_, slot) =>
                            {
                                slot.Timestamp = timestamp;
                                slot.StatusCode = quality;
                                slot.ResultMetaData!.Timestamp = timestamp;
                                slot.ResultMetaData.StatusCode = quality;
                            };
                        });
                }).BuildAsync();
            management!.OnReportEventAsync = (_, _, value, _) =>
            {
                reported = (ResultReadyEventState)value;
                return default;
            };

            await machine.Results!.PublishAsync(new MachineryResult(new ResultDataType
            {
                ResultMetaData = new ResultMetaDataType { ResultId = "First" }
            }));
            await machine.Results.PublishAsync(new MachineryResult(new ResultDataType
            {
                ResultMetaData = new ResultMetaDataType { ResultId = "Second" }
            }));

            Assert.That(preparations, Is.EqualTo(2),
                "A ring shift must retain its prepared sample even after that result leaves the backing store.");
            Assert.That(slots[0].Timestamp, Is.EqualTo(secondTime));
            Assert.That(slots[0].StatusCode, Is.EqualTo(StatusCodes.BadNoData));
            Assert.That(slots[1].Timestamp, Is.EqualTo(firstTime));
            Assert.That(slots[1].StatusCode, Is.EqualTo(StatusCodes.UncertainLastUsableValue));
            Assert.That(slots[1].ResultMetaData!.Timestamp, Is.EqualTo(firstTime));
            Assert.That(reported!.Result!.Timestamp, Is.EqualTo(secondTime));
            Assert.That(reported.Result.StatusCode, Is.EqualTo(StatusCodes.BadNoData));
        }

        /// <summary>
        /// Final and intermediate results select their configured event bases without changing the shared protocol.
        /// </summary>
        [Test]
        public async Task ResultFactorySelectsAmongRegisteredEventBasesAsync()
        {
            await using var fixture = new MachineryServerFixture();
            await fixture.StartAsync();
            IMachineryBuildContext context = fixture.CreateBuildContext();
            NodeId readyBase = NodeId.Create(
                Opc.Ua.Machinery.Result.ObjectTypes.ResultReadyEventType,
                Opc.Ua.Machinery.Result.Namespaces.MachineryResult,
                context.Context.NamespaceUris);
            var intermediate = new BaseObjectTypeState
            {
                NodeId = new NodeId("CompanionIntermediateResult", context.InstanceNamespaceIndex),
                BrowseName = new QualifiedName("CompanionIntermediateResult", context.InstanceNamespaceIndex),
                DisplayName = new LocalizedText("Companion intermediate result"),
                SuperTypeId = readyBase,
                IsAbstract = true
            };
            await fixture.Manager.AddPredefinedNodeAsync(intermediate);
            fixture.Manager.Server.TypeTree.AddSubtype(intermediate.NodeId, readyBase);
            ResultManagementState? management = null;
            IMachineHandle<BaseObjectState> machine = await context
                .AddMachine(new QualifiedName("SelectedResultEvents"))
                .WithResultManagement(results =>
                {
                    management = results.State;
                    results.WithInMemoryStore(3).WithResultReadyEvents(
                        [readyBase, intermediate.NodeId],
                        (systemContext, parent, result) =>
                        {
                            ResultReadyEventState value =
                                systemContext.CreateInstanceOfResultReadyEventType(parent, default);
                            value.TypeDefinitionId = result.ResultId == "Intermediate" ? intermediate.NodeId : readyBase;
                            return value;
                        });
                }).BuildAsync();
            var observedTypes = new List<NodeId>();
            management!.OnReportEventAsync = (_, _, value, _) =>
            {
                observedTypes.Add(((ResultReadyEventState)value).EventType!.Value);
                return default;
            };

            await machine.Results!.PublishAsync(new MachineryResult(new ResultDataType
            {
                ResultMetaData = new ResultMetaDataType { ResultId = "Intermediate" }
            }));
            await machine.Results.PublishAsync(new MachineryResult(new ResultDataType
            {
                ResultMetaData = new ResultMetaDataType { ResultId = "Final" }
            }));

            Assert.That(observedTypes, Has.Count.EqualTo(2));
            Assert.That(observedTypes[0], Is.Not.EqualTo(observedTypes[1]));
            Assert.That(context.Context.TypeTable.FindSuperType(observedTypes[0]), Is.EqualTo(intermediate.NodeId));
            Assert.That(context.Context.TypeTable.FindSuperType(observedTypes[1]), Is.EqualTo(readyBase));
            foreach (NodeId type in observedTypes)
            {
                Assert.That(fixture.Manager.FindPredefinedNode<BaseObjectTypeState>(type)!.IsAbstract, Is.False);
            }
        }

        /// <summary>
        /// Invalid per-result projection cannot enter the backing store or alter a published result slot.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public async Task RejectedResultProjectionDoesNotReachStorageAsync(bool returnNull)
        {
            await using var fixture = new MachineryServerFixture();
            await fixture.StartAsync();
            IMachineryBuildContext context = fixture.CreateBuildContext();
            var store = new Mock<IWritableMachineryResultStore>();
            IMachineHandle<BaseObjectState> machine = await context
                .AddMachine(new QualifiedName("RejectedProjection"))
                .WithResultManagement(results => results.WithStore(store.Object)
                    .WithResultProjection(_ => returnNull ? null! :
                        throw new FormatException("Invalid acquired companion sample.")))
                .BuildAsync();
            var result = new MachineryResult(new ResultDataType
            {
                ResultMetaData = new ResultMetaDataType { ResultId = "Rejected" }
            });

            if (returnNull)
            {
                ServiceResultException error = (await Assert.ThrowsAsync<ServiceResultException>(
                    async () => await machine.Results!.PublishAsync(result)))!;
                Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            }
            else
            {
                await Assert.ThrowsAsync<FormatException>(async () => await machine.Results!.PublishAsync(result));
            }
            store.Verify(value => value.AddResultAsync(
                It.IsAny<MachineryResult>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        /// <summary>
        /// Duplicate or empty event type selections cannot weaken the declared event-factory contract.
        /// </summary>
        [Test]
        public async Task ResultEventSelectionRejectsDuplicateAndEmptyBasesAsync()
        {
            await using var fixture = new MachineryServerFixture();
            await fixture.StartAsync();
            IMachineryBuildContext context = fixture.CreateBuildContext();
            NodeId readyBase = NodeId.Create(
                Opc.Ua.Machinery.Result.ObjectTypes.ResultReadyEventType,
                Opc.Ua.Machinery.Result.Namespaces.MachineryResult,
                context.Context.NamespaceUris);
            IMachineBuilder<BaseObjectState> machine = context.AddMachine(new QualifiedName("InvalidEventSelection"));
            machine.WithResultManagement(results =>
            {
                results.WithInMemoryStore();
                Assert.Throws<ArgumentOutOfRangeException>(() => results.WithResultReadyEvents(
                    ArrayOf<NodeId>.Empty,
                    static (systemContext, parent, _) =>
                        systemContext.CreateInstanceOfResultReadyEventType(parent, default)));
                Assert.Throws<ArgumentException>(() => results.WithResultReadyEvents(
                    [readyBase, readyBase],
                    static (systemContext, parent, _) =>
                        systemContext.CreateInstanceOfResultReadyEventType(parent, default)));
            });

            await machine.BuildAsync();
        }

        /// <summary>
        /// A genuinely asynchronous result notification must not block the thread invoking PublishAsync.
        /// </summary>
        [Test]
        public async Task ResultPublicationYieldsWhileAnAsyncNotificationIsPendingAsync()
        {
            await using var fixture = new MachineryServerFixture();
            await fixture.StartAsync();
            IMachineryBuildContext context = fixture.CreateBuildContext();
            ResultState? slot = null;
            IMachineHandle<BaseObjectState> machine = await context
                .AddMachine(new QualifiedName("AsyncResultNotification"))
                .WithResultManagement(results => results.WithInMemoryStore().WithResultsFolder(1,
                    (systemContext, parent, name) =>
                    {
                        slot = systemContext.CreateInstanceOfResultType(parent, name);
                        return slot;
                    })).BuildAsync();
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var returned = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            slot!.OnStateChangedAsync = (_, _, _, _) =>
            {
                entered.TrySetResult(true);
                return new ValueTask(release.Task);
            };
            Task publish = Task.Run(async () =>
            {
                ValueTask pending = machine.Results!.PublishAsync(new MachineryResult(new ResultDataType
                {
                    ResultMetaData = new ResultMetaDataType { ResultId = "Async" }
                }));
                returned.TrySetResult(true);
                await pending.ConfigureAwait(false);
            });
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                await returned.Task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
                Assert.That(publish.IsCompleted, Is.False);
            }
            finally
            {
                release.TrySetResult(true);
                await publish.ConfigureAwait(false);
            }
        }

        [Test]
        public async Task CompanionResultFactoriesRejectUnrelatedEventTypesAndNullNodesAsync()
        {
            await using var fixture = new MachineryServerFixture();
            await fixture.StartAsync();
            IMachineryBuildContext context = fixture.CreateBuildContext();
            IMachineBuilder<BaseObjectState> machine = context.AddMachine(new QualifiedName("InvalidFactories"));
            IResultManagementBuilder? management = null;
            machine.WithResultManagement(results =>
            {
                management = results;
                results.WithInMemoryStore();
            });

            ServiceResultException wrongType = Assert.Throws<ServiceResultException>(() =>
                management!.WithResultReadyEvents(
                    Opc.Ua.ObjectTypeIds.BaseEventType,
                    static (systemContext, parent, _) =>
                        systemContext.CreateInstanceOfResultReadyEventType(parent, default)))!;
            Assert.That(wrongType.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadTypeDefinitionInvalid));
            ServiceResultException nullNode = Assert.Throws<ServiceResultException>(() =>
                management!.WithResultsFolder(1, static (_, _, _) => null!))!;
            Assert.That(nullNode.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadConfigurationError));
            await machine.BuildAsync();
        }
    }
}
