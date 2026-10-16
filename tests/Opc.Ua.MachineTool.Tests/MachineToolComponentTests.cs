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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Machinery;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.MachineTool.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.MachineTool.Tests
{
    /// <summary>
    /// Exercises the released Components CU through existing Machinery and DI bindings.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    [Category("MachineTool")]
    public sealed class MachineToolComponentTests
    {
        /// <summary>
        /// Explicit selection uses the Machinery-qualified default name and HasAddIn, without another manager.
        /// </summary>
        [Test]
        public async Task ComponentsReuseSharedIdentificationAndDefaultAddInPlacementAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            await using MachineToolRuntime<MachineToolState> runtime = await Configure(
                fixture.Context.AddMachineTool("Components")).BuildAsync().ConfigureAwait(false);

            Assert.That(runtime.Components, Is.Not.Null);
            Assert.That(runtime.Components!.ReferenceTypeId, Is.EqualTo(ReferenceTypeIds.HasAddIn));
            Assert.That(runtime.Components.BrowseName,
                Is.EqualTo(new QualifiedName("Components",
                    (ushort)fixture.Context.Context.NamespaceUris.GetIndex(Machinery.Namespaces.Machinery))));
            Assert.That(runtime.Components.Parent, Is.SameAs(runtime.State));
            var members = new List<BaseInstanceState>();
            runtime.Components.GetChildren(fixture.Context.Context, members);
            var motor = (BaseObjectState)members.Find(node => node.BrowseName.Name == "Spindle")!;
            Assert.That(motor, Is.Not.Null);
            var identityName = new QualifiedName("Identification",
                (ushort)fixture.Context.Context.NamespaceUris.GetIndex(Di.Namespaces.OpcUaDi));
            var identification = (MachineryItemIdentificationState)motor.FindChild(
                fixture.Context.Context, identityName)!;
            Assert.That(identification.SerialNumber!.Value, Is.EqualTo("Spindle-1"));
            Assert.That(fixture.Context.Manager.FindPredefinedNode(motor.NodeId), Is.SameAs(motor));
        }

        /// <summary>
        /// A preexisting generated vendor Components subtype must be bound in place instead of replaced or duplicated.
        /// </summary>
        [Test]
        public async Task ExistingVendorComponentsAreAdoptedWithoutCopyingAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            var type = new BaseObjectTypeState
            {
                NodeId = new NodeId(Guid.NewGuid(), fixture.Context.InstanceNamespaceIndex),
                BrowseName = new QualifiedName("VendorComponents", fixture.Context.InstanceNamespaceIndex),
                DisplayName = new LocalizedText("Vendor components"),
                SuperTypeId = ExpandedNodeId.ToNodeId(Machinery.ObjectTypeIds.MachineComponentsType,
                    fixture.Context.Context.NamespaceUris),
                IsAbstract = false,
                IsPartOfTypeHierarchy = true
            };
            await fixture.Context.Manager.AddPredefinedNodeAsync(type).ConfigureAwait(false);
            MachineComponentsState? original = null;
            MachineToolBuilder<MachineToolState> builder = fixture.Context.AddMachineTool("VendorComponents",
                (context, parent, name) =>
                {
                    MachineToolState machine = context.CreateInstanceOfMachineToolType(parent, name);
                    original = context.CreateInstanceOfMachineComponentsType(machine,
                        new QualifiedName("Components",
                            (ushort)context.NamespaceUris.GetIndex(Machinery.Namespaces.Machinery)));
                    original.TypeDefinitionId = type.NodeId;
                    original.ReferenceTypeId = ReferenceTypeIds.HasAddIn;
                    machine.AddChild(original);
                    return machine;
                });
            await using MachineToolRuntime<MachineToolState> runtime =
                await Configure(builder).BuildAsync().ConfigureAwait(false);

            Assert.That(runtime.Components, Is.SameAs(original));
            Assert.That(runtime.Components!.TypeDefinitionId, Is.EqualTo(type.NodeId));
            var members = new List<BaseInstanceState>();
            runtime.State.GetChildren(fixture.Context.Context, members);
            Assert.That(members.FindAll(node => node is MachineComponentsState), Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Components can reference an already bound physical component without copying or owning it.
        /// </summary>
        [Test]
        public async Task ExistingComponentReferencesPreserveIdentityAndOriginalLifetimeAsync()
        {
            await using var fixture = new MachineToolRuntimeFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            await using MachineToolRuntime<MachineToolState> owner =
                await Configure(fixture.Context.AddMachineTool("Owner")).BuildAsync().ConfigureAwait(false);
            var members = new List<BaseInstanceState>();
            owner.Components!.GetChildren(fixture.Context.Context, members);
            BaseInstanceState component = members.Find(node => node.BrowseName.Name == "Spindle")!;
            await using MachineToolRuntime<MachineToolState> referencing = await fixture.Context
                .AddMachineTool("Reference")
                .WithProvider(new InMemoryMachineToolProvider(new MachineToolSnapshot()))
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Shared owner");
                    identity.SerialNumber = "Reference";
                    identity.ProductInstanceUri = "urn:components:reference";
                }).WithComponents(components =>
                {
                    components.State.AddReference(ReferenceTypeIds.HasComponent, false, component.NodeId);
                    component.AddReference(ReferenceTypeIds.HasComponent, true, components.State.NodeId);
                }).BuildAsync().ConfigureAwait(false);
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            await using var transport = new ClientFixture(telemetry);
            await transport.LoadClientConfigurationAsync(fixture.Root, "ComponentReferences").ConfigureAwait(false);
            using ISession session = await transport.ConnectAsync(fixture.Endpoint, SecurityPolicies.None)
                .ConfigureAwait(false);
            BrowseResponse response = await session.BrowseAsync(null, null, 100,
                [
                    new BrowseDescription
                    {
                        NodeId = referencing.Components!.NodeId, BrowseDirection = BrowseDirection.Forward,
                        ReferenceTypeId = ReferenceTypeIds.HasComponent, IncludeSubtypes = true,
                        NodeClassMask = (uint)NodeClass.Object, ResultMask = (uint)BrowseResultMask.All
                    }
                ], CancellationToken.None).ConfigureAwait(false);
            Assert.That(response.Results[0].References.ToArray()!.Any(reference =>
                ExpandedNodeId.ToNodeId(reference.NodeId, session.NamespaceUris) == component.NodeId), Is.True);
            await referencing.DisposeAsync().ConfigureAwait(false);
            Assert.That(fixture.Context.Manager.FindPredefinedNode(component.NodeId), Is.SameAs(component));
            Assert.That(component.Parent, Is.SameAs(owner.Components));
            await session.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        }

        /// <summary>
        /// Configures the same public shared bindings for both ordinary construction and adoption.
        /// </summary>
        private static MachineToolBuilder<MachineToolState> Configure(MachineToolBuilder<MachineToolState> builder)
        {
            return builder.WithProvider(new InMemoryMachineToolProvider(new MachineToolSnapshot()))
                .WithIdentification(identity =>
                {
                    identity.Manufacturer = new LocalizedText("Components");
                    identity.SerialNumber = "Machine";
                    identity.ProductInstanceUri = "urn:components:machine";
                }).WithComponents(components =>
                    components.AddComponent(new QualifiedName("Spindle"), ConfigureComponent));
        }

        /// <summary>
        /// Uses the shared Machinery/DI component identification module.
        /// </summary>
        private static void ConfigureComponent(IMachineComponentBuilder component)
        {
            component.WithIdentification(identity =>
            {
                identity.Manufacturer = new LocalizedText("Components");
                identity.SerialNumber = "Spindle-1";
                identity.ProductInstanceUri = "urn:components:spindle";
            });
        }
    }
}
