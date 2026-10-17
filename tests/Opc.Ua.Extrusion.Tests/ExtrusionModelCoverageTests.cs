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
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Extrusion.Server;
using Opc.Ua.Machinery.Server.StateMachines;

namespace Opc.Ua.Extrusion.Tests
{
    /// <summary>
    /// Exercises every concrete type, every selected declaration and owner/lifetime invariants.
    /// </summary>
    [TestFixture]
    [NonParallelizable]
    public sealed class ExtrusionModelCoverageTests
    {
        /// <summary>
        /// Loads one real owner for independent serially executed device tests.
        /// </summary>
        [OneTimeSetUp]
        public async Task StartAsync()
        {
            m_fixture = new ExtrusionFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Releases the owner and its common background/file bindings.
        /// </summary>
        [OneTimeTearDown]
        public async Task StopAsync()
        {
            await m_fixture.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Proves all eleven concrete siblings construct with mandatory-only and complete optional selections.
        /// </summary>
        [TestCase(2, false)]
        [TestCase(3, false)]
        [TestCase(4, false)]
        [TestCase(5, false)]
        [TestCase(6, false)]
        [TestCase(7, false)]
        [TestCase(8, false)]
        [TestCase(9, false)]
        [TestCase(10, false)]
        [TestCase(11, false)]
        [TestCase(12, false)]
        [TestCase(2, true)]
        [TestCase(3, true)]
        [TestCase(4, true)]
        [TestCase(5, true)]
        [TestCase(6, true)]
        [TestCase(7, true)]
        [TestCase(8, true)]
        [TestCase(9, true)]
        [TestCase(10, true)]
        [TestCase(11, true)]
        [TestCase(12, true)]
        public async Task EveryConcretePartPreservesTheRealTypeAndDeclaredMembersAsync(int part, bool all)
        {
            ExtrusionDeviceOptions options = ExtrusionFixture.Options($"part{part}-{all}", all);
            if (all)
            {
                SelectRepeatedEquipment(options.Features, part);
            }
            ExtrusionDeviceState node = await ExtrusionFixture.CreateAsync(m_fixture.Runtime, part, options)
                .ConfigureAwait(false);
            var provider = new ExtrusionModelProvider();
            Assert.Multiple(() =>
            {
                Assert.That(
                    m_fixture.Manager.SystemContext.NamespaceUris.GetString(node.TypeDefinitionId.NamespaceIndex),
                    Is.EqualTo(provider.NamespaceUris[part]));
                Assert.That(node.NodeId.NamespaceIndex, Is.EqualTo(m_fixture.Runtime.InstanceNamespaceIndex));
                Assert.That(node.IsPresent, Is.Not.Null);
                Assert.That(node.LineId!.Value, Is.EqualTo("TestLine"));
                Assert.That(node.Identification, Is.Not.Null);
                Assert.That(node.MachineryBuildingBlocks, Is.Not.Null);
                Assert.That(node.BrowseName.Name, Does.Not.Contain("<"));
                Assert.That(m_fixture.Manager.FindPredefinedNode(node.NodeId), Is.SameAs(node));
            });
            var identities = new HashSet<NodeId> { node.NodeId };
            foreach ((string path, BaseInstanceState child) in ExtrusionNodeAccess.Walk(
                m_fixture.Runtime.SystemContext, node))
            {
                Assert.Multiple(() =>
                {
                    Assert.That(identities.Add(child.NodeId), Is.True, path);
                    Assert.That(child.NodeId.NamespaceIndex, Is.EqualTo(m_fixture.Runtime.InstanceNamespaceIndex),
                        path);
                    Assert.That(child.BrowseName.Name, Does.Not.Contain("<"), path);
                    Assert.That(m_fixture.Manager.FindPredefinedNode(child.NodeId), Is.SameAs(child), path);
                });
            }
            if (part == 2)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(node.Target, Is.Null, "OPC 40084-2 §6.2 prohibits component-only Target.");
                    Assert.That(node.Strand, Is.Null, "OPC 40084-2 §6.2 prohibits component-only Strand.");
                });
            }
            if (part == 11)
            {
                Assert.That(node, Is.InstanceOf<HaulOff.HaulOff_InterfaceState>(),
                    "The corrugator really derives from HaulOff, never Extruder.");
            }
            if (all)
            {
                AssertDirectMemberMatrix(node);
            }
        }

        /// <summary>
        /// Creates actual mandatory placeholders and honors independently selected nested filter packages.
        /// </summary>
        [Test]
        public async Task CutterAndFilterCreateRealRepeatedTypedInstancesAsync()
        {
            var cutter = await m_fixture.Runtime.CreateCutterAsync(ExtrusionFixture.Options("mandatory-products"))
                .ConfigureAwait(false);
            var products = ExtrusionNodeAccess.Children(m_fixture.Runtime.SystemContext, cutter.Node.CuttingProducts!)
                .OfType<Cutter.CuttingProductState>().ToArray();
            Assert.That(products, Has.Length.EqualTo(5));
            Assert.That(products.Select(product => product.BrowseName.Name),
                Is.EqualTo(s_cuttingProductNames));

            ExtrusionDeviceOptions settings = ExtrusionFixture.Options("filter-packages", all: true);
            settings.Features.WithInstances("FilterPackage_<Nr>", 2)
                .WithInstances("FilterPackage_<Nr>/ScreenPackage_<Nr>", 3);
            var filter = await m_fixture.Runtime.CreateFilterAsync(settings).ConfigureAwait(false);
            Filter.FilterPackageState[] packages = ExtrusionNodeAccess.Children(
                m_fixture.Runtime.SystemContext, filter.Node).OfType<Filter.FilterPackageState>().ToArray();
            Assert.That(packages, Has.Length.EqualTo(2));
            foreach (Filter.FilterPackageState package in packages)
            {
                Assert.That(ExtrusionNodeAccess.Children(m_fixture.Runtime.SystemContext, package)
                    .OfType<Filter.ScreenPackageState>().Count(), Is.EqualTo(3));
            }
            Assert.That(filter.Node.TypeDefinitionId, Is.EqualTo(ExpandedNodeId.ToNodeId(
                Filter.ObjectTypeIds.Filter_InterfaceType, m_fixture.Runtime.SystemContext.NamespaceUris)));
        }

        /// <summary>
        /// Rejects impossible feature/cardinality selections before reserving a real device identity.
        /// </summary>
        [Test]
        public async Task InvalidSelectionsAreRejectedAndNamesRemainReusableAsync()
        {
            ExtrusionDeviceOptions unknown = ExtrusionFixture.Options("invalid-selection");
            unknown.Features.Include("InventedExtruderPressure");
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await m_fixture.Runtime.CreateExtruderAsync(unknown).ConfigureAwait(false));
            var valid = await m_fixture.Runtime.CreateExtruderAsync(ExtrusionFixture.Options("invalid-selection"))
                .ConfigureAwait(false);
            Assert.That(valid.NodeId.IsNull, Is.False);

            ExtrusionDeviceOptions cutter = ExtrusionFixture.Options("zero-products");
            cutter.Features.WithInstances("CuttingProducts/CuttingProduct_<Nr>", 0);
            Assert.ThrowsAsync<ArgumentException>(async () =>
                await m_fixture.Runtime.CreateCutterAsync(cutter).ConfigureAwait(false));

            ExtrusionDeviceOptions missing = ExtrusionFixture.Options("missing-authority");
            missing.Features.Include("Maintenance");
            missing.Common.Maintenance = null;
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_fixture.Runtime.CreateExtruderAsync(missing).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadConfigurationError));
        }

        /// <summary>
        /// OPC 40083 declares the MaterialList DensityUnit without a value: an unbound list keeps a null
        /// unit, and a bound catalogue publishes the configured unit its densities are given in.
        /// </summary>
        [Test]
        public async Task MaterialListPublishesOnlyTheConfiguredDensityUnitAsync()
        {
            ExtrusionDeviceState unbound = await ExtrusionFixture.CreateAsync(
                m_fixture.Runtime, 2, ExtrusionFixture.Options("density-unit-unbound")).ConfigureAwait(false);
            var unboundLine = (Line.ExtrusionLine_InterfaceState)unbound;

            ExtrusionDeviceOptions options = ExtrusionFixture.Options("density-unit-bound", all: true);
            ExtrusionDeviceState bound = await ExtrusionFixture.CreateAsync(m_fixture.Runtime, 2, options)
                .ConfigureAwait(false);
            var boundLine = (Line.ExtrusionLine_InterfaceState)bound;

            Assert.Multiple(() =>
            {
                Assert.That(unboundLine.MaterialList!.DensityUnit!.Value, Is.Null);
                Assert.That(boundLine.MaterialList!.DensityUnit!.Value, Is.Not.Null);
                Assert.That(boundLine.MaterialList.DensityUnit.Value.UnitId,
                    Is.EqualTo(ExtrusionFixture.KilogramPerCubicMetre.UnitId));
                Assert.That(boundLine.MaterialList.DensityUnit.Value,
                    Is.Not.SameAs(options.Common.MaterialDensityUnit));
            });
        }

        /// <summary>
        /// A bound material catalogue without a configured DensityUnit is rejected during creation.
        /// </summary>
        [Test]
        public void BoundMaterialListWithoutDensityUnitIsRejected()
        {
            ExtrusionDeviceOptions options = ExtrusionFixture.Options("density-unit-missing", all: true);
            options.Common.MaterialDensityUnit = null;

            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await ExtrusionFixture.CreateAsync(m_fixture.Runtime, 2, options).ConfigureAwait(false))!;

            Assert.Multiple(() =>
            {
                Assert.That(failure.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadConfigurationError));
                Assert.That(failure.Message, Does.Contain(nameof(ExtrusionCommonProviders.MaterialDensityUnit)));
            });
        }

        /// <summary>
        /// Serializes concurrent creation across the duplicate check and registration.
        /// </summary>
        [Test]
        public async Task ConcurrentSameIdentityHasExactlyOneWinnerAsync()
        {
            Task<bool>[] creations = Enumerable.Range(0, 8).Select(_ => TryCreateAsync()).ToArray();
            bool[] results = await Task.WhenAll(creations).ConfigureAwait(false);
            Assert.That(results.Count(created => created), Is.EqualTo(1));

            async Task<bool> TryCreateAsync()
            {
                try
                {
                    await m_fixture.Runtime.CreateExtruderAsync(ExtrusionFixture.Options("concurrent"))
                        .ConfigureAwait(false);
                    return true;
                }
                catch (ServiceResultException exception) when (
                    exception.StatusCode == StatusCodes.BadBrowseNameDuplicated)
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// A post-registration failure removes the real root, descendants and name reservation.
        /// </summary>
        [Test]
        public async Task RegistrationFailureRollsBackAndRetriesCleanlyAsync()
        {
            NodeId failed = NodeId.Null;
            m_fixture.Runtime.RegisteredForTest = node =>
            {
                failed = node.NodeId;
                throw new InvalidOperationException("Injected registration failure.");
            };
            try
            {
                Assert.ThrowsAsync<InvalidOperationException>(async () =>
                    await m_fixture.Runtime.CreateFilterAsync(ExtrusionFixture.Options("rollback"))
                        .ConfigureAwait(false));
                Assert.That(m_fixture.Manager.FindPredefinedNode(failed), Is.Null);
            }
            finally
            {
                m_fixture.Runtime.RegisteredForTest = null;
            }
            var retried = await m_fixture.Runtime.CreateFilterAsync(ExtrusionFixture.Options("rollback"))
                .ConfigureAwait(false);
            Assert.That(retried.NodeId, Is.EqualTo(failed));
        }

        /// <summary>
        /// Rejects cancelled creation without adding an instance.
        /// </summary>
        [Test]
        public void CancelledCreationDoesNotPublishAnInstance()
        {
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            int before = m_fixture.Runtime.Devices.Count;
            Assert.CatchAsync<OperationCanceledException>(async () =>
                await m_fixture.Runtime.CreateExtruderAsync(ExtrusionFixture.Options("cancelled"),
                    cancellationToken: cancelled.Token).ConfigureAwait(false));
            Assert.That(m_fixture.Runtime.Devices.Count, Is.EqualTo(before));
        }

        /// <summary>
        /// Applies absolute observations with source timestamps and never independently advances quantities.
        /// </summary>
        [Test]
        public async Task AbsoluteSnapshotsAreAtomicAndIdempotentAsync()
        {
            var filter = await m_fixture.Runtime.CreateFilterAsync(ExtrusionFixture.Options("snapshots"))
                .ConfigureAwait(false);
            var snapshot = new ExtrusionSnapshot(1,
            [
                new ExtrusionValue(filter.Node.IsPresent!.NodeId,
                    new DataValue(Variant.From(true), StatusCodes.Good, ExtrusionFixture.Timestamp)),
                new ExtrusionValue(filter.Node.FiltrationFineness!.NodeId,
                    new DataValue(Variant.From(24.0), StatusCodes.Uncertain, ExtrusionFixture.Timestamp))
            ]);
            await filter.ApplySnapshotAsync(snapshot).ConfigureAwait(false);
            await filter.ApplySnapshotAsync(snapshot).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(filter.Node.IsPresent.Value, Is.True);
                Assert.That(filter.Node.FiltrationFineness.Value, Is.EqualTo(24.0));
                Assert.That(filter.Node.FiltrationFineness.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Uncertain));
                Assert.That(filter.Node.FiltrationFineness.Timestamp, Is.EqualTo(ExtrusionFixture.Timestamp));
            });
            var invalid = new ExtrusionSnapshot(2,
            [
                new ExtrusionValue(filter.Node.IsPresent.NodeId,
                    new DataValue(Variant.From(false), StatusCodes.Good, ExtrusionFixture.Timestamp)),
                new ExtrusionValue(filter.Node.FiltrationFineness.NodeId,
                    new DataValue(Variant.From("wrong type"), StatusCodes.Good, ExtrusionFixture.Timestamp))
            ]);
            ServiceResultException failure = Assert.ThrowsAsync<ServiceResultException>(async () =>
                await filter.ApplySnapshotAsync(invalid).ConfigureAwait(false))!;
            Assert.That(failure.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadTypeMismatch));
            Assert.That(filter.Node.IsPresent.Value, Is.True, "The valid prefix of an invalid update was not applied.");
            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await filter.ApplySnapshotAsync(snapshot with { Revision = 0 }).ConfigureAwait(false));
        }

        /// <summary>
        /// Inactive execution substates carry the required bad status, not a misleading valid state.
        /// </summary>
        [Test]
        public async Task ExecutionSubmachineUsesRealStateIdsAndBadStateNotActiveAsync()
        {
            var device = await m_fixture.Runtime.CreateExtruderAsync(ExtrusionFixture.Options("states", all: true))
                .ConfigureAwait(false);
            await device.State.ApplyAsync(new ExtrusionOperatingSnapshot(1,
                MachineryItemStateValue.NotExecuting, MachineryOperationModeValue.None,
                ExtrusionExecutingState.ReadyToRun, ExtrusionFixture.Timestamp)).ConfigureAwait(false);
            var current = (BaseVariableState)device.Find(
                "MachineryBuildingBlocks/MachineryItemState/ExtrusionExecutingSubState/CurrentState");
            Assert.That(current.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadStateNotActive));
            await device.State.ApplyAsync(new ExtrusionOperatingSnapshot(2,
                MachineryItemStateValue.Executing, MachineryOperationModeValue.Processing,
                ExtrusionExecutingState.ControlledRun, ExtrusionFixture.Timestamp)).ConfigureAwait(false);
            Assert.Multiple(() =>
            {
                Assert.That(current.StatusCode, Is.EqualTo((StatusCode)StatusCodes.Good));
                Assert.That(current.WrappedValue.TryGetValue(out LocalizedText state) && state.Text == "ControlledRun",
                    Is.True);
                Assert.That(current.Timestamp, Is.EqualTo(ExtrusionFixture.Timestamp));
            });
            var stateId = (BaseVariableState)device.Find(
                "MachineryBuildingBlocks/MachineryItemState/ExtrusionExecutingSubState/CurrentState/Id");
            Assert.That(stateId.WrappedValue.TryGetValue(out NodeId id), Is.True);
            Assert.That(m_fixture.Manager.FindPredefinedNode(id), Is.Not.Null);
        }

        /// <summary>
        /// Observing the same operating state does not invent a transition or move its committed time.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task RepeatedOperatingStatesPreserveLastTransitionTimeAsync(bool rejectInterveningSnapshot)
        {
            var device = await m_fixture.Runtime.CreateExtruderAsync(
                ExtrusionFixture.Options("transition-time-" + rejectInterveningSnapshot, all: true), node =>
                {
                    var machine = (FiniteStateMachineState)ExtrusionNodeAccess.Find(
                        m_fixture.Runtime.SystemContext, node, "MachineryBuildingBlocks/MachineryItemState")!;
                    machine.AddLastTransition(m_fixture.Runtime.SystemContext);
                    machine.LastTransition!.AddTransitionTime(m_fixture.Runtime.SystemContext);
                }).ConfigureAwait(false);
            var initial = new ExtrusionOperatingSnapshot(1,
                MachineryItemStateValue.NotExecuting, MachineryOperationModeValue.None,
                ExtrusionExecutingState.ReadyToRun, ExtrusionFixture.Timestamp);
            await device.State.ApplyAsync(initial).ConfigureAwait(false);
            DateTimeUtc transitionTime = (DateTimeUtc)ExtrusionFixture.Timestamp.ToDateTime().AddSeconds(1);
            var executing = initial with
            {
                Revision = 2,
                ItemState = MachineryItemStateValue.Executing,
                OperationMode = MachineryOperationModeValue.Processing,
                ExecutingState = ExtrusionExecutingState.ControlledRun,
                SourceTimestamp = transitionTime
            };
            await device.State.ApplyAsync(executing).ConfigureAwait(false);
            const string path = "MachineryBuildingBlocks/MachineryItemState/LastTransition";
            var transition = (BaseVariableState)device.Find(path);
            var time = (BaseVariableState)device.Find(path + "/TransitionTime");
            Variant previousTransition = transition.WrappedValue;
            Assert.That(time.WrappedValue.TryGetValue(out DateTimeUtc committed), Is.True);
            Assert.That(committed, Is.EqualTo(transitionTime));
            if (rejectInterveningSnapshot)
            {
                Assert.ThrowsAsync<ServiceResultException>(() => device.State.ApplyAsync(initial with
                {
                    SourceTimestamp = (DateTimeUtc)transitionTime.ToDateTime().AddSeconds(5)
                }).AsTask());
            }

            await device.State.ApplyAsync(executing with
            {
                Revision = 3,
                SourceTimestamp = (DateTimeUtc)transitionTime.ToDateTime().AddSeconds(10)
            }).ConfigureAwait(false);

            Assert.That(transition.WrappedValue, Is.EqualTo(previousTransition));
            Assert.That(time.WrappedValue.TryGetValue(out DateTimeUtc repeated), Is.True);
            Assert.That(repeated, Is.EqualTo(transitionTime));
        }

        /// <summary>
        /// Compares selected root members against the independent generated member evidence.
        /// </summary>
        private void AssertDirectMemberMatrix(ExtrusionDeviceState node)
        {
            using JsonDocument matrix = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(TestContext.CurrentContext.TestDirectory, "feature-matrix.json")));
            NodeState declaration = m_fixture.Manager.FindPredefinedNode(node.TypeDefinitionId)!;
            foreach (JsonElement member in matrix.RootElement.GetProperty("members").EnumerateArray())
            {
                if (member.GetProperty("type").GetString() != declaration.BrowseName.Name ||
                    member.GetProperty("modellingRule").GetString() is "i=11508" or "i=11510")
                {
                    continue;
                }
                string name = member.GetProperty("member").GetString()!;
                string uri = member.GetProperty("memberNamespaceUri").GetString()!;
                var browseName = new QualifiedName(name,
                    (ushort)m_fixture.Runtime.SystemContext.NamespaceUris.GetIndex(uri));
                Assert.That(node.FindChildWithQualifiedName(m_fixture.Runtime.SystemContext, browseName),
                    Is.Not.Null, declaration.BrowseName.Name + "/" + name);
            }
        }

        /// <summary>
        /// Exercises every family-owned repeated equipment type, including inherited Corrugator drives.
        /// </summary>
        private static void SelectRepeatedEquipment(ExtrusionFeatures features, int part)
        {
            if (part is 3 or 4 or 5 or 6 or 7 or 8 or 11)
            {
                features.WithInstances("TemperatureZones/TemperatureZone_<Nr>", 1);
            }
            switch (part)
            {
                case 3:
                    features.WithInstances("Feeders/Feeder_<Nr>", 2)
                        .WithInstances("AdditionalDrives/<Name>_<Nr>", 1)
                        .WithInstances("ScrewTemperatures/TemperatureZone_<Nr>", 1);
                    break;
                case 4:
                case 11:
                    features.WithInstances("Drives/<Name>_<Nr>", 2);
                    break;
                case 6:
                    features.WithInstances("FilterPackage_<Nr>", 2)
                        .WithInstances("FilterPackage_<Nr>/ScreenPackage_<Nr>", 2);
                    break;
                case 8:
                    features.WithInstances("KnifePackage_<Nr>", 2).WithInstances("DiePlate_<Nr>", 1);
                    break;
                case 10:
                    features.WithInstances("CalibrationZones/CalibrationZone_<Nr>", 2);
                    break;
                case 12:
                    features.WithInstances("Rolls/Roll_<Nr>", 2)
                        .WithInstances("Gaps/Gap_<Nr>", 1)
                        .WithInstances("Rolls/Roll_<Nr>/PeripheralDevices/CleaningSystem_<Nr>", 1)
                        .WithInstances("Rolls/Roll_<Nr>/PeripheralDevices/InfraredHeatingSystem_<Nr>", 1)
                        .WithInstances("Rolls/Roll_<Nr>/RollBending/ReferencePoint_<Nr>", 1)
                        .WithInstances("CouplingProductForces/CouplingProductForce_<Nr>", 1)
                        .WithInstances("CouplingProductTensions/CouplingProductTension_<Nr>", 1);
                    break;
            }
        }

        /// <summary>The five mandatory default cutting-product instance names.</summary>
        private static readonly string[] s_cuttingProductNames =
        [
            "CuttingProduct_001", "CuttingProduct_002", "CuttingProduct_003",
            "CuttingProduct_004", "CuttingProduct_005"
        ];

        /// <summary>The shared serial test owner.</summary>
        private ExtrusionFixture m_fixture = null!;
    }
}
