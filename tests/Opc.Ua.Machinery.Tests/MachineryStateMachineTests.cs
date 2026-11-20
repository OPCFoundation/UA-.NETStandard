// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Machinery.Server;
using Opc.Ua.Machinery.Server.Builders;
using Opc.Ua.Machinery.Server.StateMachines;
using ItemIds = Opc.Ua.Machinery.MachineryItemState_StateMachineTypeIds;
using ModeIds = Opc.Ua.Machinery.MachineryOperationModeStateMachineTypeIds;

namespace Opc.Ua.Machinery.Tests
{
    /// <summary>
    /// Covers the two OPC 40001-1 state machines: the tables the driver works
    /// from, and the transitions it applies.
    /// </summary>
    [TestFixture]
    [Category("Machinery")]
    public sealed class MachineryStateMachineTests
    {
        [Test]
        public void ItemStateTableCoversEveryOrderedPair()
        {
            Assert.That(MachineryStateMachineTables.ItemStates, Has.Count.EqualTo(4));
            Assert.That(MachineryStateMachineTables.ItemTransitions, Has.Count.EqualTo(16));

            var pairs = MachineryStateMachineTables.ItemTransitions
                .Select(transition => (transition.FromStateId, transition.ToStateId))
                .ToHashSet();
            Assert.That(
                pairs,
                Has.Count.EqualTo(16),
                "Every from/to pair appears exactly once, self-transitions included.");
        }

        [Test]
        public void OperationModeTableCoversEveryOrderedPair()
        {
            Assert.That(MachineryStateMachineTables.OperationModeStates, Has.Count.EqualTo(4));
            Assert.That(
                MachineryStateMachineTables.OperationModeTransitions,
                Has.Count.EqualTo(16));
        }

        [Test]
        public void StateNumbersMatchThePublishedModel()
        {
            Assert.That(ItemIds.StateNumbers.NotAvailable, Is.Zero);
            Assert.That(ItemIds.StateNumbers.OutOfService, Is.EqualTo(1u));
            Assert.That(ItemIds.StateNumbers.NotExecuting, Is.EqualTo(2u));
            Assert.That(ItemIds.StateNumbers.Executing, Is.EqualTo(3u));

            Assert.That(ModeIds.StateNumbers.None, Is.Zero);
            Assert.That(ModeIds.StateNumbers.Maintenance, Is.EqualTo(1u));
            Assert.That(ModeIds.StateNumbers.Setup, Is.EqualTo(2u));
            Assert.That(ModeIds.StateNumbers.Processing, Is.EqualTo(3u));
        }

        [Test]
        public void ValueMappingIsRoundTrippable()
        {
            foreach (MachineryItemStateValue state in new[]
            {
                MachineryItemStateValue.NotAvailable,
                MachineryItemStateValue.OutOfService,
                MachineryItemStateValue.NotExecuting,
                MachineryItemStateValue.Executing
            })
            {
                Assert.That(
                    MachineryStateMachineTables.ToItemState(
                        MachineryStateMachineTables.ToStateId(state)),
                    Is.EqualTo(state));
            }

            foreach (MachineryOperationModeValue mode in new[]
            {
                MachineryOperationModeValue.None,
                MachineryOperationModeValue.Maintenance,
                MachineryOperationModeValue.Setup,
                MachineryOperationModeValue.Processing
            })
            {
                Assert.That(
                    MachineryStateMachineTables.ToOperationMode(
                        MachineryStateMachineTables.ToStateId(mode)),
                    Is.EqualTo(mode));
            }
        }

        [Test]
        public async Task SelfTransitionIsAppliedRatherThanSkipped()
        {
            await using var fixture = new MachineryServerFixture(MachineryParts.BuildingBlocks);
            await fixture.StartAsync();
            IMachineHandle<BaseObjectState> machine = await BuildAsync(fixture);

            Assert.That(
                await machine.ItemState!.SetStateAsync(MachineryItemStateValue.Executing),
                Is.True);
            Assert.That(
                await machine.ItemState.SetStateAsync(MachineryItemStateValue.Executing),
                Is.True,
                "The model declares FromExecutingToExecuting, so a move to the " +
                "current state is a transition and not a no-op.");

            var stateMachine = (MachineryItemState_StateMachineState)
                fixture.Manager.FindPredefinedNode(machine.ItemState.NodeId)!;
            Assert.That(
                stateMachine.LastTransition!.Value.Text,
                Is.EqualTo("FromExecutingToExecuting"));
        }

        [Test]
        public async Task TransitionHandlersSeeEveryMove()
        {
            await using var fixture = new MachineryServerFixture(MachineryParts.BuildingBlocks);
            await fixture.StartAsync();
            IMachineHandle<BaseObjectState> machine = await BuildAsync(fixture);

            var stateMachine = (MachineryItemState_StateMachineState)
                fixture.Manager.FindPredefinedNode(machine.ItemState!.NodeId)!;
            var observed = new List<uint>();
            stateMachine.OnAfterTransition = (_, _, transitionId, _, _, _) =>
            {
                observed.Add(transitionId);
                return ServiceResult.Good;
            };

            await machine.ItemState.SetStateAsync(MachineryItemStateValue.Executing);
            await machine.ItemState.SetStateAsync(MachineryItemStateValue.OutOfService);

            Assert.That(
                observed,
                Is.EqualTo(
                    new[]
                    {
                        ItemIds.TransitionIds.FromNotExecutingToExecuting,
                        ItemIds.TransitionIds.FromExecutingToOutOfService
                    }),
                "The driver composes with the stack's transition delegates, which " +
                "is where StateMachineBuilder.For installs its observers.");
        }

        [Test]
        public async Task AVetoingGuardBlocksTheTransition()
        {
            await using var fixture = new MachineryServerFixture(MachineryParts.BuildingBlocks);
            await fixture.StartAsync();
            IMachineHandle<BaseObjectState> machine = await BuildAsync(fixture);

            var stateMachine = (MachineryItemState_StateMachineState)
                fixture.Manager.FindPredefinedNode(machine.ItemState!.NodeId)!;
            stateMachine.OnBeforeTransition =
                (_, _, _, _, _, _) => StatusCodes.BadUserAccessDenied;

            Assert.That(
                await machine.ItemState.SetStateAsync(MachineryItemStateValue.Executing),
                Is.False);
            Assert.That(
                machine.ItemState.CurrentState,
                Is.EqualTo(MachineryItemStateValue.NotExecuting),
                "A vetoed transition must leave the state alone.");
        }

        [Test]
        public async Task ATransitionCommittedWhileAGuardRunsIsNotOverwritten()
        {
            await using var fixture = new MachineryServerFixture(MachineryParts.BuildingBlocks);
            await fixture.StartAsync().ConfigureAwait(false);
            IMachineHandle<BaseObjectState> machine = await BuildAsync(fixture).ConfigureAwait(false);

            var stateMachine = (MachineryItemState_StateMachineState)
                fixture.Manager.FindPredefinedNode(machine.ItemState!.NodeId)!;
            var guarded = new List<uint>();
            bool interleaved = false;
            stateMachine.OnBeforeTransition = (_, _, transitionId, _, _, _) =>
            {
                guarded.Add(transitionId);
                if (!interleaved)
                {
                    // Another caller commits while this guard runs outside the
                    // driver's lock.
                    interleaved = true;
                    _ = machine.ItemState
                        .SetStateAsync(MachineryItemStateValue.OutOfService)
                        .AsTask()
                        .GetAwaiter()
                        .GetResult();
                }
                return ServiceResult.Good;
            };

            Assert.That(
                await machine.ItemState.SetStateAsync(MachineryItemStateValue.Executing).ConfigureAwait(false),
                Is.True);

            NodeId lastTransition = stateMachine.LastTransition!.Id!.Value;
            Assert.That(
                machine.ItemState.CurrentState,
                Is.EqualTo(MachineryItemStateValue.Executing));
            Assert.That(
                lastTransition,
                Is.EqualTo(new NodeId(
                    ItemIds.TransitionIds.FromOutOfServiceToExecuting,
                    lastTransition.NamespaceIndex)),
                "The stale NotExecuting to Executing transition must not be committed.");
            Assert.That(
                guarded,
                Is.EqualTo(
                    new[]
                    {
                        ItemIds.TransitionIds.FromNotExecutingToExecuting,
                        ItemIds.TransitionIds.FromNotExecutingToOutOfService,
                        ItemIds.TransitionIds.FromOutOfServiceToExecuting
                    }),
                "The transition from the new current state is guarded again.");
        }

        private static ValueTask<IMachineHandle<BaseObjectState>> BuildAsync(
            MachineryServerFixture fixture)
        {
            return fixture.CreateBuildContext()
                .AddMachine(new QualifiedName("StateMachine-Host"))
                .WithIdentification(id =>
                {
                    id.Manufacturer = new LocalizedText("Acme");
                    id.SerialNumber = "SN-SM";
                    id.ProductInstanceUri = "urn:acme:sm";
                })
                .WithMonitoring(monitoring => monitoring
                    .WithMachineryItemState(MachineryItemStateValue.NotExecuting)
                    .WithOperationMode())
                .BuildAsync();
        }
    }
}
