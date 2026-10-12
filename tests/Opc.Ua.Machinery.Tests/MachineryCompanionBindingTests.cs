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

using System.Collections.Generic;
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

            ServiceResultException exception = Assert.ThrowsAsync<ServiceResultException>(
                async () => await builder.BuildAsync())!;
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
