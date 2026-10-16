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
using NUnit.Framework;
using Opc.Ua.Di.Server;
using Opc.Ua.Machinery;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Robotics.Server;
using Opc.Ua.Robotics.Server.Builders;

namespace Opc.Ua.CranesHoists.Tests
{
    /// <summary>
    /// Tests the shared binding transaction with real crane-generated roots.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class CraneBindingTransactionTests
    {
        /// <summary>
        /// Both stock owners preserve the crane's native ownership and shared nameplate behavior.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task NativeCraneBindingPreservesOwnershipAndBothDiscoveryPaths(bool roboticsOwner)
        {
            await using var fixture = new CraneServerFixture(roboticsOwner);
            await fixture.StartAsync().ConfigureAwait(false);
            IRoboticsBuildContext robotics = fixture.RoboticsContext();
            IMachineryBuildContext machinery = fixture.MachineryContext();
            CraneMotionDeviceSystemState? created = null;
            NodeId originalId = NodeId.Null;
            IMotionDeviceSystemBuilder built = await robotics.AddMotionDeviceSystemAsync(
                new QualifiedName("NativeCrane", robotics.InstanceNamespaceIndex),
                (context, parent, name) =>
                {
                    created = context.CreateInstanceOfCraneMotionDeviceSystemType(parent, name);
                    originalId = created.NodeId;
                    return created;
                },
                system =>
                {
                    ConfigureTopology(system);
                    var crane = (CraneMotionDeviceSystemState)system.State;
                    system.WithRegistrationParticipant(machinery.BindMachine(
                        crane,
                        machine => machine.WithIdentification(crane.Identification!, Identify)));
                }).ConfigureAwait(false);

            Assert.That(built.State, Is.SameAs(created));
            Assert.That(built.State.Parent, Is.SameAs(robotics.DeviceSet));
            Assert.That(built.State.ReferenceTypeId, Is.EqualTo(Opc.Ua.ReferenceTypeIds.HasComponent));
            Assert.That(built.State.NodeId, Is.EqualTo(originalId));
            Assert.That(built.State.TypeDefinitionId, Is.EqualTo(NodeId.Create(
                CranesHoists.ObjectTypes.CraneMotionDeviceSystemType,
                CranesHoistsModel.NamespaceUri,
                robotics.Context.NamespaceUris)));
            Assert.That(fixture.Manager.FindPredefinedNode(originalId), Is.SameAs(created));
            Assert.That(machinery.MachinesFolder.ReferenceExists(
                Opc.Ua.ReferenceTypeIds.Organizes,
                false,
                originalId), Is.True);
            var children = new List<BaseInstanceState>();
            machinery.MachinesFolder.GetChildren(machinery.Context, children);
            Assert.That(children, Does.Not.Contain(created), "Discovery must not re-own the root.");
            Assert.That(created!.Identification!.Manufacturer!.Value.Text, Is.EqualTo("Crane test manufacturer"));
            Assert.That(created.Identification.SerialNumber!.Value, Is.EqualTo("CR-1"));
            Assert.That(created.Identification.ProductInstanceUri!.Value, Is.EqualTo("urn:crane:test:CR-1"));
            Assert.That(fixture.ReservedCount, Is.Zero);

            await fixture.Manager.DeleteNodeAsync(fixture.Manager.SystemContext, originalId).ConfigureAwait(false);
            Assert.That(machinery.MachinesFolder.ReferenceExists(
                Opc.Ua.ReferenceTypeIds.Organizes,
                false,
                originalId), Is.False);
        }

        /// <summary>
        /// Failure after publication removes only the attempted tree and permits a same-name retry.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FailedBindingRollsBackReferencesReservationsAndFacets(bool roboticsOwner)
        {
            await using var fixture = new CraneServerFixture(roboticsOwner);
            await fixture.StartAsync().ConfigureAwait(false);
            IRoboticsBuildContext robotics = fixture.RoboticsContext();
            IMachineryBuildContext machinery = fixture.MachineryContext();
            ArrayOf<QualifiedName> before = fixture.Manager.ConformanceUnits;
            CraneMotionDeviceSystemState? failed = null;
            var name = new QualifiedName("RetryCrane", robotics.InstanceNamespaceIndex);
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await robotics.AddMotionDeviceSystemAsync(
                    name,
                    static (context, parent, browseName) =>
                        context.CreateInstanceOfCraneMotionDeviceSystemType(parent, browseName),
                    system =>
                    {
                        ConfigureTopology(system);
                        failed = (CraneMotionDeviceSystemState)system.State;
                        system.WithRegistrationParticipant(machinery.BindMachine(
                            failed,
                            machine => machine.WithIdentification(failed.Identification!, Identify)));
                        system.WithRegistrationParticipant(new FailingParticipant(fixture.Manager, failed));
                    }).ConfigureAwait(false))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadResourceUnavailable));
            Assert.That(fixture.Manager.FindPredefinedNode(failed!.NodeId), Is.Null);
            Assert.That(machinery.MachinesFolder.ReferenceExists(
                Opc.Ua.ReferenceTypeIds.Organizes,
                false,
                failed.NodeId), Is.False);
            Assert.That(fixture.ReservedCount, Is.Zero, fixture.ReservedNames);
            Assert.That(fixture.Manager.ConformanceUnits, Is.EqualTo(before));

            IMotionDeviceSystemBuilder retry = await robotics.AddMotionDeviceSystemAsync(
                name,
                static (context, parent, browseName) =>
                    context.CreateInstanceOfCraneMotionDeviceSystemType(parent, browseName),
                system =>
                {
                    ConfigureTopology(system);
                    var crane = (CraneMotionDeviceSystemState)system.State;
                    system.WithRegistrationParticipant(machinery.BindMachine(
                        crane,
                        machine => machine.WithIdentification(crane.Identification!, Identify)));
                }).ConfigureAwait(false);
            Assert.That(fixture.Manager.FindPredefinedNode(retry.State.NodeId), Is.SameAs(retry.State));
            Assert.That(fixture.ReservedCount, Is.Zero);
        }

        /// <summary>
        /// A validation failure before publication releases both contexts and never advertises a facet.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task InvalidIdentificationLeavesNoPublishedNodesOrReservedNames(bool roboticsOwner)
        {
            await using var fixture = new CraneServerFixture(roboticsOwner);
            await fixture.StartAsync().ConfigureAwait(false);
            IRoboticsBuildContext robotics = fixture.RoboticsContext();
            IMachineryBuildContext machinery = fixture.MachineryContext();
            var name = new QualifiedName("InvalidCrane", robotics.InstanceNamespaceIndex);
            ArrayOf<QualifiedName> before = fixture.Manager.ConformanceUnits;
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await robotics.AddMotionDeviceSystemAsync(
                    name,
                    static (context, parent, browseName) =>
                        context.CreateInstanceOfCraneMotionDeviceSystemType(parent, browseName),
                    system =>
                    {
                        ConfigureTopology(system);
                        var crane = (CraneMotionDeviceSystemState)system.State;
                        system.WithRegistrationParticipant(machinery.BindMachine(
                            crane,
                            machine => machine.WithIdentification(crane.Identification!, data =>
                                data.SerialNumber = "MissingManufacturer")));
                    }).ConfigureAwait(false))!;

            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadConfigurationError));
            Assert.That(fixture.ReservedCount, Is.Zero);
            Assert.That(fixture.Manager.ConformanceUnits, Is.EqualTo(before));
            await robotics.AddMotionDeviceSystemAsync(
                name,
                static (context, parent, browseName) =>
                    context.CreateInstanceOfCraneMotionDeviceSystemType(parent, browseName),
                system =>
                {
                    ConfigureTopology(system);
                    var crane = (CraneMotionDeviceSystemState)system.State;
                    system.WithRegistrationParticipant(machinery.BindMachine(
                        crane,
                        machine => machine.WithIdentification(crane.Identification!, Identify)));
                }).ConfigureAwait(false);
            Assert.That(fixture.ReservedCount, Is.Zero);
        }

        /// <summary>
        /// Every native descendant is indexed once, even after shared Machinery bindings add children.
        /// </summary>
        [Test]
        public async Task CompleteCraneSubtreeIsRegisteredExactlyOnce()
        {
            await using var fixture = new CraneServerFixture(countRegistrations: true);
            await fixture.StartAsync().ConfigureAwait(false);
            IRoboticsBuildContext robotics = fixture.RoboticsContext();
            IMachineryBuildContext machinery = fixture.MachineryContext();
            IMotionDeviceSystemBuilder built = await robotics.AddMotionDeviceSystemAsync(
                new QualifiedName("CountedCrane", robotics.InstanceNamespaceIndex),
                static (context, parent, name) => context.CreateInstanceOfCraneMotionDeviceSystemType(parent, name),
                system =>
                {
                    ConfigureTopology(system);
                    var crane = (CraneMotionDeviceSystemState)system.State;
                    system.WithRegistrationParticipant(machinery.BindMachine(
                        crane,
                        machine => machine.WithIdentification(crane.Identification!, Identify)));
                }).ConfigureAwait(false);
            var nodes = new List<NodeState> { built.State };
            for (int i = 0; i < nodes.Count; i++)
            {
                var children = new List<BaseInstanceState>();
                nodes[i].GetChildren(robotics.Context, children);
                nodes.AddRange(children);
                Assert.That(fixture.RegistrationCount(nodes[i].NodeId), Is.EqualTo(1),
                    nodes[i].BrowseName.ToString());
            }
            Assert.That(nodes, Has.Count.GreaterThan(40));
            Assert.That(fixture.ReservedCount, Is.Zero);
        }

        /// <summary>
        /// Cancellation before or after publication never commits discovery or Machinery facets.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task CancelledBindingReleasesBothContexts(bool roboticsOwner, bool afterPublication)
        {
            await using var fixture = new CraneServerFixture(roboticsOwner);
            await fixture.StartAsync().ConfigureAwait(false);
            using var cancellation = new CancellationTokenSource();
            IRoboticsBuildContext robotics = fixture.RoboticsContext();
            IMachineryBuildContext machinery = fixture.MachineryContext();
            ArrayOf<QualifiedName> before = fixture.Manager.ConformanceUnits;
            var name = new QualifiedName("CancelledCrane", robotics.InstanceNamespaceIndex);
            CraneMotionDeviceSystemState? failed = null;
            Assert.ThrowsAsync<OperationCanceledException>(async () =>
                await robotics.AddMotionDeviceSystemAsync(
                    name,
                    static (context, parent, browseName) =>
                        context.CreateInstanceOfCraneMotionDeviceSystemType(parent, browseName),
                    system =>
                    {
                        ConfigureTopology(system);
                        failed = (CraneMotionDeviceSystemState)system.State;
                        system.WithRegistrationParticipant(machinery.BindMachine(
                            failed,
                            machine => machine.WithIdentification(failed.Identification!, Identify)));
                        system.WithRegistrationParticipant(new CancellingParticipant(
                            fixture.Manager, failed, cancellation, afterPublication));
                    }, cancellation.Token).ConfigureAwait(false));
            Assert.That(fixture.Manager.FindPredefinedNode(failed!.NodeId), Is.Null);
            Assert.That(fixture.ReservedCount, Is.Zero, fixture.ReservedNames);
            Assert.That(fixture.Manager.ConformanceUnits, Is.EqualTo(before));
            await robotics.AddMotionDeviceSystemAsync(
                name,
                static (context, parent, browseName) =>
                    context.CreateInstanceOfCraneMotionDeviceSystemType(parent, browseName),
                system =>
                {
                    ConfigureTopology(system);
                    var crane = (CraneMotionDeviceSystemState)system.State;
                    system.WithRegistrationParticipant(machinery.BindMachine(
                        crane,
                        machine => machine.WithIdentification(crane.Identification!, Identify)));
                }).ConfigureAwait(false);
            await machinery.SealAsync().ConfigureAwait(false);
            await robotics.SealAsync().ConfigureAwait(false);
            Assert.That(fixture.ReservedCount, Is.Zero);
        }

        /// <summary>
        /// A rejected binding must never delete an already indexed, unrelated tree.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task ConflictingRootIdentityNeverDeletesTheExistingTree(bool roboticsOwner)
        {
            await using var fixture = new CraneServerFixture(roboticsOwner);
            await fixture.StartAsync().ConfigureAwait(false);
            IRoboticsBuildContext robotics = fixture.RoboticsContext();
            IMachineryBuildContext machinery = fixture.MachineryContext();
            BaseObjectState existing = robotics.Context.CreateInstanceOfBaseObjectType(
                robotics.DeviceSet, new QualifiedName("Existing", robotics.InstanceNamespaceIndex));
            robotics.DeviceSet.AddChild(existing);
            await fixture.Manager.AddPredefinedNodeAsync(existing).ConfigureAwait(false);
            ServiceResultException error = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await robotics.AddMotionDeviceSystemAsync(
                    new QualifiedName("ConflictingCrane", robotics.InstanceNamespaceIndex),
                    (context, parent, name) =>
                    {
                        CraneMotionDeviceSystemState crane = context.CreateInstanceOfCraneMotionDeviceSystemType(
                            parent, name);
                        crane.NodeId = existing.NodeId;
                        return crane;
                    },
                    system =>
                    {
                        ConfigureTopology(system);
                        var crane = (CraneMotionDeviceSystemState)system.State;
                        system.WithRegistrationParticipant(machinery.BindMachine(
                            crane, machine => machine.WithIdentification(crane.Identification!, Identify)));
                    }).ConfigureAwait(false))!;
            Assert.That(error.StatusCode, Is.EqualTo(StatusCodes.BadInvalidState));
            Assert.That(fixture.Manager.FindPredefinedNode(existing.NodeId), Is.SameAs(existing));
            var children = new List<BaseInstanceState>();
            robotics.DeviceSet.GetChildren(robotics.Context, children);
            Assert.That(children, Does.Contain(existing));
        }

        /// <summary>
        /// Pre-build failures can be explicitly abandoned without poisoning a machine's name.
        /// </summary>
        [Test]
        public async Task AbortingBeforeBuildReleasesTheMachineNameAndAllocation()
        {
            await using var fixture = new CraneServerFixture();
            await fixture.StartAsync().ConfigureAwait(false);
            IMachineryBuildContext machinery = fixture.MachineryContext();
            IMachineBuilder<BaseObjectState> builder = machinery.AddMachine(new QualifiedName("Abandoned"));
            Assert.Throws<ServiceResultException>(() =>
                builder.WithIdentification(data => data.SerialNumber = "Incomplete"));
            await builder.AbortAsync().ConfigureAwait(false);
            Assert.That(fixture.ReservedCount, Is.Zero);
            Assert.ThrowsAsync<ServiceResultException>(async () => await builder.BuildAsync().ConfigureAwait(false));
            IMachineHandle<BaseObjectState> retry = await machinery.AddMachine(new QualifiedName("Abandoned"))
                .WithIdentification(Identify).BuildAsync().ConfigureAwait(false);
            Assert.That(fixture.Manager.FindPredefinedNode(retry.NodeId), Is.SameAs(retry.State));
            Assert.That(fixture.ReservedCount, Is.Zero);
        }

        /// <summary>
        /// Returned binding resources unwind on later failure and survive success until node removal.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task DeferredBindingResourcesFollowTheActualRootLifetime(bool roboticsOwner, bool fail)
        {
            await using var fixture = new CraneServerFixture(roboticsOwner);
            await fixture.StartAsync().ConfigureAwait(false);
            IRoboticsBuildContext robotics = fixture.RoboticsContext();
            IMachineryBuildContext machinery = fixture.MachineryContext();
            var resource = new CountingResource();
            CraneMotionDeviceSystemState? root = null;

            async Task BuildAsync()
            {
                await robotics.AddMotionDeviceSystemAsync(
                    new QualifiedName("ResourceCrane", robotics.InstanceNamespaceIndex),
                    static (context, parent, name) => context.CreateInstanceOfCraneMotionDeviceSystemType(parent, name),
                    system =>
                    {
                        ConfigureTopology(system);
                        root = (CraneMotionDeviceSystemState)system.State;
                        system.WithRegistrationParticipant(machinery.BindMachine(root, machine =>
                        {
                            machine.WithIdentification(root.Identification!, Identify)
                                .WithRegistrationBinding((state, _, _) =>
                                {
                                    Assert.That(fixture.Manager.FindPredefinedNode(state.NodeId), Is.SameAs(state));
                                    return new ValueTask<IAsyncDisposable>(resource);
                                });
                        }));
                        if (fail)
                        {
                            system.WithRegistrationParticipant(new FailingParticipant(fixture.Manager, root));
                        }
                    }).ConfigureAwait(false);
            }

            if (fail)
            {
                Assert.ThrowsAsync<ServiceResultException>(BuildAsync);
            }
            else
            {
                await BuildAsync().ConfigureAwait(false);
                Assert.That(resource.DisposeCount, Is.Zero);
                await fixture.Manager.DeleteNodeAsync(fixture.Manager.SystemContext, root!.NodeId)
                    .ConfigureAwait(false);
            }
            Assert.That(resource.DisposeCount, Is.EqualTo(1));
            Assert.That(fixture.Manager.FindPredefinedNode(root!.NodeId), Is.Null);
            Assert.That(fixture.ReservedCount, Is.Zero, fixture.ReservedNames);
        }

        /// <summary>
        /// Configures actual inherited Robotics obligations rather than substituting an empty root.
        /// </summary>
        internal static void ConfigureTopology(IMotionDeviceSystemBuilder system)
        {
            ushort ns = system.BuildContext.InstanceNamespaceIndex;
            IMotionDeviceBuilder device = system.AddMotionDevice(
                new QualifiedName("Hoist", ns),
                static (context, parent, name) => context.CreateInstanceOfCraneMotionDeviceType(parent, name));
            IAxisBuilder axis = device.AddAxis(
                new QualifiedName("Lift", ns),
                static (context, parent, name) => context.CreateInstanceOfCraneAxisType(parent, name));
            IPowerTrainBuilder train = device.AddPowerTrain("DriveTrain");
            train.AddMotor(
                new QualifiedName("LiftMotor", ns),
                static (context, parent, name) => context.CreateInstanceOfCraneMotorType(parent, name));
            axis.Requires(train);
            train.Moves(axis);
            ISafetyStateBuilder safety = system.AddSafetyState("Safety");
            IControllerBuilder controller = system.AddController("Controller");
            controller.AddSoftware("ControllerSoftware");
            controller.AddTaskControl("ControllerTask");
            controller.Controls(device).UsesSafetyState(safety);
        }

        /// <summary>
        /// Supplies a complete Machinery machine nameplate.
        /// </summary>
        internal static void Identify(MachineryIdentificationData data)
        {
            data.Manufacturer = new LocalizedText("Crane test manufacturer");
            data.SerialNumber = "CR-1";
            data.ProductInstanceUri = "urn:crane:test:CR-1";
        }

        /// <summary>
        /// Fails after other participants bound, proving no early facet commitment.
        /// </summary>
        private sealed class FailingParticipant : ICompanionRegistrationParticipant
        {
            /// <summary>
            /// Binds the same registration owner and root.
            /// </summary>
            internal FailingParticipant(DiNodeManager manager, BaseObjectState root)
            {
                Manager = manager;
                Root = root;
            }

            /// <inheritdoc/>
            public DiNodeManager Manager { get; }

            /// <inheritdoc/>
            public BaseObjectState Root { get; }

            /// <inheritdoc/>
            public ValueTask PrepareAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Assert.That(Manager.FindPredefinedNode(Root.NodeId), Is.Null);
                return default;
            }

            /// <inheritdoc/>
            public ValueTask BindAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Assert.That(Manager.FindPredefinedNode(Root.NodeId), Is.SameAs(Root));
                throw ServiceResultException.Create(StatusCodes.BadResourceUnavailable, "Test binding refusal.");
            }

            /// <inheritdoc/>
            public void Commit()
            {
                Assert.Fail("A failed registration must not commit.");
            }

            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                return default;
            }
        }

        /// <summary>
        /// Cancels at an exact registration phase without delays or racing worker tasks.
        /// </summary>
        private sealed class CancellingParticipant : ICompanionRegistrationParticipant
        {
            /// <summary>
            /// Captures the caller-owned cancellation source and selected phase.
            /// </summary>
            internal CancellingParticipant(
                DiNodeManager manager,
                BaseObjectState root,
                CancellationTokenSource cancellation,
                bool afterPublication)
            {
                Manager = manager;
                Root = root;
                m_cancellation = cancellation;
                m_afterPublication = afterPublication;
            }

            /// <inheritdoc/>
            public DiNodeManager Manager { get; }

            /// <inheritdoc/>
            public BaseObjectState Root { get; }

            /// <inheritdoc/>
            public ValueTask PrepareAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!m_afterPublication)
                {
                    m_cancellation.Cancel();
                }
                return default;
            }

            /// <inheritdoc/>
            public ValueTask BindAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                m_cancellation.Cancel();
                return default;
            }

            /// <inheritdoc/>
            public void Commit()
            {
                Assert.Fail("Cancellation must precede commitment.");
            }

            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                return default;
            }

            private readonly CancellationTokenSource m_cancellation;
            private readonly bool m_afterPublication;
        }

        /// <summary>
        /// Records exact disposal counts to detect lost or double-owned bindings.
        /// </summary>
        private sealed class CountingResource : IAsyncDisposable
        {
            /// <summary>
            /// Gets the number of lifetime releases.
            /// </summary>
            internal int DisposeCount { get; private set; }

            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                DisposeCount++;
                return default;
            }
        }
    }
}
