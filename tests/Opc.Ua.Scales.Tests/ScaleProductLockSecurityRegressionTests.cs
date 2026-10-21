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
using Opc.Ua.Di.Server.Locking;
using Opc.Ua.Scales.Server;
using Opc.Ua.Scales.Server.Builders;
using Opc.Ua.Scales.Server.Runtime;
using Opc.Ua.Server;
using Opc.Ua.Tests;

namespace Opc.Ua.Scales.Tests
{
    /// <summary>
    /// Exercises remote product mutations against the real DI session-owner lock service without a server.
    /// </summary>
    [TestFixture]
    [Category("Scales")]
    public sealed class ScaleProductLockSecurityRegressionTests
    {
        /// <summary>
        /// Direct and nested writes cannot change the filling calculation while another session owns the product.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF43ProductLockProtectsValueWritesAndFillingCalculationAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            var product = (AutomaticFillingProductState)scale.ProductionPreset!.ActiveProduct!;
            TargetItemState target = product.TargetWeight!;
            AutomaticFillingController filling = scale.AutomaticFilling!;
            await harness.AcquireAsync(product).ConfigureAwait(false);

            AssertDenied(await WriteAsync(harness.Other, target, Variant.From(12.0)).ConfigureAwait(false));
            AssertDenied(await WriteAsync(harness.Other, target.PlusTolerance!, Variant.From(10.0))
                .ConfigureAwait(false));
            Assert.That(ReadDouble(target), Is.EqualTo(10));
            Assert.That(ReadDouble(target.PlusTolerance!), Is.EqualTo(1));
            Assert.That(filling.EvaluateFilling(12), Is.EqualTo(ToleranceState.Over_2));
            Assert.That(ReadDouble(filling.Scale.Deviation!), Is.EqualTo(2));

            AssertGood(await WriteAsync(harness.Owner, target, Variant.From(12.0)).ConfigureAwait(false));
            Assert.That(ReadDouble(target), Is.EqualTo(12));
            Assert.That(filling.EvaluateFilling(12), Is.EqualTo(ToleranceState.In_0));
            Assert.That(ReadDouble(filling.Scale.Deviation!), Is.Zero);

            await harness.ReleaseAsync(product).ConfigureAwait(false);
            AssertGood(await WriteAsync(harness.Other, target, Variant.From(14.0)).ConfigureAwait(false));
            Assert.That(filling.EvaluateFilling(12), Is.EqualTo(ToleranceState.Under_1));
            Assert.That(ReadDouble(filling.Scale.Deviation!), Is.EqualTo(-2));
        }

        /// <summary>
        /// A product without an active lock keeps its ordinary value-write behavior.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        [Category("SecurityRegression")]
        public async Task SecurityF43UnlockedProductKeepsOrdinaryWritesAsync(bool lockable)
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct, lockable);
            var product = (AutomaticFillingProductState)scale.ProductionPreset!.ActiveProduct!;

            AssertGood(await WriteAsync(harness.Other, product.TargetWeight!, Variant.From(15.0))
                .ConfigureAwait(false));

            Assert.That(ReadDouble(product.TargetWeight!), Is.EqualTo(15));
            Assert.That(scale.AutomaticFilling!.EvaluateFilling(15), Is.EqualTo(ToleranceState.In_0));
        }

        /// <summary>
        /// All reference-setting Calls preserve the caller's session before changing cached or product values.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="command"/> is not a supported reference-setting command.
        /// </exception>
        [TestCase(ScaleCommand.SetReferencePieceWeight)]
        [TestCase(ScaleCommand.SetNumberOfReferencePieces)]
        [TestCase(ScaleCommand.StartReference)]
        [Category("SecurityRegression")]
        public async Task SecurityF43ScaleReferenceCallsRespectSessionOwnerAsync(ScaleCommand command)
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.PieceCounting);
            PieceCountingController counting = scale.PieceCounting!;
            var product = (PieceCountingProductState)scale.ProductionPreset!.ActiveProduct!;
            AssertGood(counting.SetReferencePieceWeight(2));
            AssertGood(counting.SetNumberOfReferencePieces(4));
            scale.PublishLoad(12);
            await harness.AcquireAsync(product).ConfigureAwait(false);

            (
                MethodState Method,
                ArrayOf<Variant> Inputs,
                double Weight,
                uint Pieces,
                ulong Count) scenario = command switch
                {
                    ScaleCommand.SetReferencePieceWeight => (
                        counting.Scale.SetReferencePieceWeight!,
                        [Variant.From(3u), Variant.From(new ExtensionObject(ScaleUnits.Kilogram))],
                        3, 4u, 4UL),
                    ScaleCommand.SetNumberOfReferencePieces => (
                        counting.Scale.SetNumberOfReferencePieces!, [Variant.From(6u)], 2, 6u, 6UL),
                    ScaleCommand.StartReference => (
                        counting.Scale.StartReference!, [Variant.From(3u)], 4, 3u, 3UL),
                    _ => throw new ArgumentOutOfRangeException(nameof(command))
                };

            AssertDenied(await CallAsync(harness.Other, scenario.Method, scale.NodeId, scenario.Inputs)
                .ConfigureAwait(false));
            AssertReference(counting, product, 2, 4, 6);

            AssertGood(await CallAsync(harness.Owner, scenario.Method, scale.NodeId, scenario.Inputs)
                .ConfigureAwait(false));
            AssertReference(counting, product, scenario.Weight, scenario.Pieces, scenario.Count);
        }

        /// <summary>
        /// Base weighing commands must not bypass the product lock by recalculating CurrentItemCount.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="command"/> is not a supported base weighing command.
        /// </exception>
        [TestCase(ScaleCommand.SetZero)]
        [TestCase(ScaleCommand.SetTare)]
        [TestCase(ScaleCommand.ClearTare)]
        [TestCase(ScaleCommand.SetPresetTare)]
        [TestCase(ScaleCommand.RegisterWeight)]
        [Category("SecurityRegression")]
        public async Task SecurityF43BaseWeighingCallsCannotMutateLockedProductAsync(ScaleCommand command)
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(
                ScaleKind.PieceCounting,
                (context, state) => ((PieceCountingProductState)state).AddCurrentItemCount(context));
            PieceCountingController counting = scale.PieceCounting!;
            var product = (PieceCountingProductState)scale.ProductionPreset!.ActiveProduct!;
            AssertGood(counting.SetReferencePieceWeight(0.5));
            scale.PublishLoad(2);
            if (command == ScaleCommand.ClearTare)
            {
                AssertGood(scale.SetPresetTare(1));
            }
            await harness.AcquireAsync(product).ConfigureAwait(false);
            MethodState method = command switch
            {
                ScaleCommand.SetZero => scale.Scale.SetZero!,
                ScaleCommand.SetTare => scale.Scale.SetTare!,
                ScaleCommand.ClearTare => scale.Scale.ClearTare!,
                ScaleCommand.SetPresetTare => scale.Scale.SetPresetTare!,
                ScaleCommand.RegisterWeight => scale.Scale.RegisterWeight!,
                _ => throw new ArgumentOutOfRangeException(nameof(command))
            };
            ArrayOf<Variant> inputs = command == ScaleCommand.SetPresetTare
                ? [Variant.From(1.0), Variant.From(new ExtensionObject(ScaleUnits.Kilogram))]
                : [];
            ScaleReading before = scale.CurrentReading;
            ulong countBefore = counting.CurrentPieceCount;
            Variant productCountBefore = product.CurrentItemCount!.WrappedValue;

            AssertDenied(await CallAsync(harness.Other, method, scale.NodeId, inputs).ConfigureAwait(false));
            Assert.That(scale.CurrentReading.Gross, Is.EqualTo(before.Gross));
            Assert.That(scale.CurrentReading.Net, Is.EqualTo(before.Net));
            Assert.That(scale.CurrentReading.Tare, Is.EqualTo(before.Tare));
            Assert.That(counting.CurrentPieceCount, Is.EqualTo(countBefore));
            Assert.That(product.CurrentItemCount.WrappedValue, Is.EqualTo(productCountBefore));
            Assert.That(scale.RegisteredReading, Is.Null);

            AssertGood(await CallAsync(harness.Owner, method, scale.NodeId, inputs).ConfigureAwait(false));
            ulong expected = command switch
            {
                ScaleCommand.SetZero or ScaleCommand.SetTare => 0,
                ScaleCommand.SetPresetTare => 2,
                _ => 4
            };
            Assert.That(counting.CurrentPieceCount, Is.EqualTo(expected));
            Assert.That(product.CurrentItemCount.WrappedValue.TryGetValue(out ulong productCount), Is.True);
            Assert.That(productCount, Is.EqualTo(expected));
            if (command == ScaleCommand.RegisterWeight)
            {
                Assert.That(scale.RegisteredReading, Is.Not.Null);
                Assert.That(scale.RegisteredReading!.Net, Is.EqualTo(2));
            }
        }

        /// <summary>
        /// A selection change during publication cannot redirect an authorized command into a locked product.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF43RemoteCountingPublicationKeepsAuthorizedProductAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(
                ScaleKind.PieceCounting,
                (context, state) =>
                {
                    var product = (PieceCountingProductState)state;
                    product.AddCurrentItemCount(context);
                    product.CurrentItemCount!.WrappedValue = Variant.From(100UL);
                });
            ScaleProductionPreset preset = scale.ProductionPreset!;
            var first = (PieceCountingProductState)preset.Find("A")!;
            var locked = (PieceCountingProductState)preset.Find("B")!;
            AssertGood(scale.PieceCounting!.SetReferencePieceWeight(1));
            scale.PublishLoad(2);
            await harness.AcquireAsync(locked).ConfigureAwait(false);
            scale.Scale.CurrentWeight!.OnStateChanged = (_, _, _) => AssertGood(preset.Switch("B"));

            AssertGood(await CallAsync(harness.Other, scale.Scale.SetTare!, scale.NodeId, [])
                .ConfigureAwait(false));

            Assert.That(preset.ActiveProduct, Is.SameAs(locked), "the selection changed during weight publication");
            Assert.That(first.CurrentItemCount!.WrappedValue.TryGetValue(out ulong firstCount), Is.True);
            Assert.That(firstCount, Is.Zero);
            Assert.That(locked.CurrentItemCount!.WrappedValue.TryGetValue(out ulong lockedCount), Is.True);
            Assert.That(lockedCount, Is.EqualTo(100));
            Assert.That(harness.Locks.GetState(locked.NodeId).Locked, Is.True);
        }

        /// <summary>
        /// Equipment-side setters and direct node updates do not impersonate a remote caller.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF43TrustedEquipmentCanUpdateLockedProductAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.PieceCounting);
            PieceCountingController counting = scale.PieceCounting!;
            var product = (PieceCountingProductState)scale.ProductionPreset!.ActiveProduct!;
            await harness.AcquireAsync(product).ConfigureAwait(false);

            AssertGood(counting.SetReferencePieceWeight(3));
            AssertGood(counting.SetNumberOfReferencePieces(5));
            scale.PublishLoad(12);
            AssertReference(counting, product, 3, 5, 4);
            AssertGood(counting.StartReference(6));
            AssertReference(counting, product, 2, 6, 6);

            product.NumberOfReferencePieces!.WrappedValue = Variant.From(8u);
            Assert.That(ReadUInt32(product.NumberOfReferencePieces), Is.EqualTo(8));
            AssertGood(await WriteAsync(harness.Context, product.NumberOfReferencePieces, Variant.From(9u))
                .ConfigureAwait(false));
            Assert.That(ReadUInt32(product.NumberOfReferencePieces), Is.EqualTo(9));
            Assert.That(harness.Locks.GetState(product.NodeId).Locked, Is.True);
            AssertDenied(await WriteAsync(harness.Other, product.NumberOfReferencePieces, Variant.From(10u))
                .ConfigureAwait(false));
            Assert.That(ReadUInt32(product.NumberOfReferencePieces), Is.EqualTo(9));

            AssertGood(scale.SetTare());
            Assert.That(counting.CurrentPieceCount, Is.Zero);
            AssertGood(scale.ClearTare());
            Assert.That(counting.CurrentPieceCount, Is.EqualTo(6));
            AssertGood(scale.SetPresetTare(2));
            Assert.That(scale.CurrentReading.Net, Is.EqualTo(10));
            AssertGood(scale.ClearTare());
            AssertGood(scale.RegisterWeight());
            Assert.That(scale.RegisteredReading!.Net, Is.EqualTo(12));
            scale.PublishLoad(2);
            AssertGood(scale.SetZero());
            Assert.That(scale.CurrentReading.Net, Is.Zero);
        }

        /// <summary>
        /// Optional targets and their tolerance values remain protected after a real product method creates them.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF43DynamicallyCreatedTargetsKeepProductLockAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(
                ScaleKind.PieceCounting,
                (context, state) =>
                {
                    var pieces = (PieceCountingProductState)state;
                    pieces.AddSetTargetPieceCount(context)
                        .AddSetTargetItemCount(context);
                });
            var product = (PieceCountingProductState)scale.ProductionPreset!.ActiveProduct!;
            await harness.AcquireAsync(product).ConfigureAwait(false);

            AssertDenied(await CallAsync(harness.Other, product.SetTargetPieceCount!, product.NodeId,
                [Variant.From(50u), Variant.From(2u), Variant.From(1u)]).ConfigureAwait(false));
            Assert.That(product.TargetPieceCount, Is.Null);
            AssertGood(await CallAsync(harness.Owner, product.SetTargetPieceCount!, product.NodeId,
                [Variant.From(50u), Variant.From(2u), Variant.From(1u)]).ConfigureAwait(false));
            TargetItemState target = product.TargetPieceCount!;
            AssertDenied(await WriteAsync(harness.Other, target, Variant.From(60u)).ConfigureAwait(false));
            AssertDenied(await WriteAsync(harness.Other, target.PlusTolerance!, Variant.From(10u))
                .ConfigureAwait(false));
            Assert.That(ReadUInt32(target), Is.EqualTo(50));
            Assert.That(ReadUInt32(target.PlusTolerance!), Is.EqualTo(2));
            AssertGood(await WriteAsync(harness.Owner, target, Variant.From(60u)).ConfigureAwait(false));
            AssertGood(await WriteAsync(harness.Owner, target.PlusTolerance!, Variant.From(3u))
                .ConfigureAwait(false));
            Assert.That(ReadUInt32(target), Is.EqualTo(60));
            Assert.That(ReadUInt32(target.PlusTolerance!), Is.EqualTo(3));

            AssertGood(await CallAsync(harness.Owner, product.SetTargetItemCount!, product.NodeId,
                [Variant.From(7u)]).ConfigureAwait(false));
            AssertDenied(await WriteAsync(harness.Other, product.TargetItemCount!, Variant.From(8u))
                .ConfigureAwait(false));
            Assert.That(ReadUInt32(product.TargetItemCount!), Is.EqualTo(7));
        }

        /// <summary>
        /// Later optional children are guarded when their parent publishes its changed child set.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF43LaterOptionalDescendantKeepsProductLockAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling);
            var product = (AutomaticFillingProductState)scale.ProductionPreset!.ActiveProduct!;
            TargetItemState target = product.TargetWeight!;
            await harness.AcquireAsync(product).ConfigureAwait(false);

            target.AddPlusTolerance(harness.Context);
            target.PlusTolerance!.WrappedValue = Variant.From(1.0);
            await target.ClearChangeMasksAsync(harness.Context, includeChildren: true).ConfigureAwait(false);

            AssertDenied(await WriteAsync(harness.Other, target.PlusTolerance, Variant.From(10.0))
                .ConfigureAwait(false));
            Assert.That(ReadDouble(target.PlusTolerance), Is.EqualTo(1));
            AssertGood(await WriteAsync(harness.Owner, target.PlusTolerance, Variant.From(2.0))
                .ConfigureAwait(false));
            Assert.That(ReadDouble(target.PlusTolerance), Is.EqualTo(2));
        }

        /// <summary>
        /// An independently registered new zone inherits its enclosing product's lock rather than its own NodeId.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF43NewZoneValuesKeepEnclosingProductLockAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(
                ScaleKind.Catchweigher,
                (context, state) => ((CatchweigherProductState)state).AddAddZone(context));
            var product = (CatchweigherProductState)scale.ProductionPreset!.ActiveProduct!;
            await harness.AcquireAsync(product).ConfigureAwait(false);
            var outputs = new List<Variant>();
            AssertGood(await CallAsync(harness.Owner, product.AddZone!, product.NodeId,
                [
                    Variant.From(new LocalizedText("Zone")),
                    Variant.From(0.0),
                    Variant.From(20.0),
                    Variant.From(new ExtensionObject(ScaleUnits.Kilogram))
                ], outputs).ConfigureAwait(false));
            Assert.That(outputs, Has.Count.EqualTo(1));
            Assert.That(outputs[0].TryGetValue(out NodeId zoneId), Is.True);
            var zone = (ZoneState)harness.Registered[zoneId];

            AssertDenied(await WriteAsync(harness.Other, zone.LowerLimit!, Variant.From(1.0))
                .ConfigureAwait(false));
            Assert.That(ReadDouble(zone.LowerLimit!), Is.Zero);
            AssertGood(await WriteAsync(harness.Owner, zone.LowerLimit!, Variant.From(2.0))
                .ConfigureAwait(false));
            Assert.That(ReadDouble(zone.LowerLimit!), Is.EqualTo(2));
        }

        /// <summary>
        /// Switching to an unlocked product must not silently deselect another session's locked product.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF43SwitchChecksThePreviousProductLockAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.Simple);
            ScaleProductionPreset preset = scale.ProductionPreset!;
            ProductState previous = preset.ActiveProduct!;
            ProductState next = preset.Find("B")!;
            await harness.AcquireAsync(previous).ConfigureAwait(false);

            AssertDenied(await CallAsync(harness.Other, preset.Preset.SwitchProduct!, preset.Preset.NodeId,
                [Variant.From("B")]).ConfigureAwait(false));
            Assert.That(preset.CurrentProducts, Has.Count.EqualTo(1));
            Assert.That(preset.CurrentProducts[0], Is.EqualTo("A"));
            Assert.That(previous.ProductMode!.Value, Is.True);
            Assert.That(next.ProductMode!.Value, Is.False);

            AssertGood(await CallAsync(harness.Owner, preset.Preset.SwitchProduct!, preset.Preset.NodeId,
                [Variant.From("B")]).ConfigureAwait(false));
            Assert.That(preset.CurrentProducts, Has.Count.EqualTo(1));
            Assert.That(preset.CurrentProducts[0], Is.EqualTo("B"));
            Assert.That(previous.ProductMode.Value, Is.False);
            Assert.That(next.ProductMode.Value, Is.True);
        }

        /// <summary>
        /// Vehicle methods authorize the named product, not the unrelated product currently selected.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF43VehicleCallsAndLateValuesRespectNamedProductLockAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(
                ScaleKind.Vehicle,
                (context, state) => ((VehicleProductState)state).AddGetVehicleInformation(context));
            VehicleController vehicles = scale.Vehicle!;
            ScaleProductionPreset preset = scale.ProductionPreset!;
            var product = (VehicleProductState)preset.Find("B")!;
            product.VehicleId!.Value = "Truck";
            await harness.AcquireAsync(product).ConfigureAwait(false);
            preset.VehicleInformationProvider = _ => new VehicleInformation { Tare = 2 };
            AssertGood(await CallAsync(harness.Owner, product.GetVehicleInformation!, product.NodeId,
                [Variant.From("Truck")]).ConfigureAwait(false));
            AssertDenied(await WriteAsync(harness.Other, product.Tare!, Variant.From(100.0)).ConfigureAwait(false));
            Assert.That(ReadDouble(product.Tare!), Is.EqualTo(2));
            AssertGood(await WriteAsync(harness.Owner, product.Tare!, Variant.From(3.0)).ConfigureAwait(false));

            scale.PublishLoad(40);
            AssertGood(vehicles.InboundWeighing("Truck"));
            Variant inbound = product.InboundWeight!.WrappedValue;
            Variant outbound = product.OutboundWeight!.WrappedValue;
            Variant delta = product.DeltaWeight!.WrappedValue;
            scale.PublishLoad(10);
            foreach (MethodState method in new MethodState[]
            {
                vehicles.Scale.InboundWeighing!,
                vehicles.Scale.OutboundWeighing!,
                vehicles.Scale.OnePassWeighing!
            })
            {
                AssertDenied(await CallAsync(harness.Other, method, scale.NodeId, [Variant.From("Truck")])
                    .ConfigureAwait(false));
                Assert.That(product.InboundWeight.WrappedValue, Is.EqualTo(inbound));
                Assert.That(product.OutboundWeight.WrappedValue, Is.EqualTo(outbound));
                Assert.That(product.DeltaWeight.WrappedValue, Is.EqualTo(delta));
            }

            AssertGood(await CallAsync(harness.Owner, vehicles.Scale.OutboundWeighing!, scale.NodeId,
                [Variant.From("Truck")]).ConfigureAwait(false));
            Assert.That(
                product.DeltaWeight.Value.Gross,
                Is.EqualTo(30),
                "denied calls did not consume the inbound cache");
            AssertGood(await CallAsync(harness.Owner, vehicles.Scale.OnePassWeighing!, scale.NodeId,
                [Variant.From("Truck")]).ConfigureAwait(false));
            Assert.That(product.DeltaWeight.Value.Gross, Is.EqualTo(7));
            scale.PublishLoad(20);
            AssertGood(await CallAsync(harness.Owner, vehicles.Scale.InboundWeighing!, scale.NodeId,
                [Variant.From("Truck")]).ConfigureAwait(false));
            Assert.That(product.InboundWeight.Value.Gross, Is.EqualTo(20));
            Assert.That(preset.ActiveProduct, Is.SameAs(preset.Find("A")));
        }

        /// <summary>
        /// Recipe commands must not alter the active product's report or processing state for a different session.
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">
        /// <paramref name="command"/> is not a supported recipe command.
        /// </exception>
        [TestCase(ScaleCommand.StartRecipe)]
        [TestCase(ScaleCommand.StopRecipe)]
        [TestCase(ScaleCommand.ContinueRecipe)]
        [TestCase(ScaleCommand.SkipCurrentRecipeElement)]
        [TestCase(ScaleCommand.AbortRecipe)]
        [Category("SecurityRegression")]
        public async Task SecurityF43RecipeCallsRespectProductLockAsync(ScaleCommand command)
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(
                ScaleKind.Recipe,
                (_, state) => ((RecipeProductState)state).Report!.Value = default);
            RecipeController recipes = scale.Recipes!;
            var product = (RecipeProductState)scale.ProductionPreset!.ActiveProduct!;
            RecipeState recipe = recipes.AddRecipe("Recipe", new LocalizedText("Recipe"));
            recipes.AddRecipeElement(
                recipe,
                new NodeId(ObjectTypes.TimerType, ScaleNamespaceIndices.Resolve(harness.Context.NamespaceUris).Scales),
                "Step",
                recipe.NodeId);
            await harness.AcquireAsync(product).ConfigureAwait(false);
            if (command != ScaleCommand.StartRecipe)
            {
                AssertGood(recipes.Start(recipe.NodeId));
            }
            if (command == ScaleCommand.ContinueRecipe)
            {
                AssertGood(recipes.Stop(recipe.NodeId));
            }
            MethodState method = command switch
            {
                ScaleCommand.StartRecipe => recipes.Scale.StartRecipe!,
                ScaleCommand.StopRecipe => recipes.Scale.StopRecipe!,
                ScaleCommand.ContinueRecipe => recipes.Scale.ContinueRecipe!,
                ScaleCommand.SkipCurrentRecipeElement => recipes.Scale.SkipCurrentRecipeElement!,
                ScaleCommand.AbortRecipe => recipes.Scale.AbortRecipe!,
                _ => throw new ArgumentOutOfRangeException(nameof(command))
            };
            RecipeRunState stateBefore = recipes.State;
            RecipeState? activeBefore = recipes.ActiveRecipe;
            ArrayOf<RecipeElementState> elementsBefore = recipes.CurrentElements;
            ArrayOf<RecipeReportElementType> reportBefore = product.Report!.Value;

            AssertDenied(await CallAsync(harness.Other, method, scale.NodeId, [Variant.From(recipe.NodeId)])
                .ConfigureAwait(false));
            Assert.That(recipes.State, Is.EqualTo(stateBefore));
            Assert.That(recipes.ActiveRecipe, Is.SameAs(activeBefore));
            Assert.That(recipes.CurrentElements.ToArray(), Is.EqualTo(elementsBefore.ToArray()));
            Assert.That(product.Report.Value.ToArray(), Is.EqualTo(reportBefore.ToArray()));

            AssertGood(await CallAsync(harness.Owner, method, scale.NodeId, [Variant.From(recipe.NodeId)])
                .ConfigureAwait(false));
            RecipeRunState expected = command switch
            {
                ScaleCommand.StopRecipe => RecipeRunState.Paused,
                ScaleCommand.SkipCurrentRecipeElement or ScaleCommand.AbortRecipe => RecipeRunState.Idle,
                _ => RecipeRunState.Running
            };
            Assert.That(recipes.State, Is.EqualTo(expected));
            Assert.That(product.Report.Value.Count, Is.GreaterThan(reportBefore.Count));
        }

        /// <summary>
        /// Runtime-created products carry both their caller-owned DI lock and descendant write guards.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF43NewProductIsLockedForCreatingSessionAsync()
        {
            using var harness = new Harness();
            ScaleHandle scale = harness.CreateScale(ScaleKind.AutomaticFilling, ConfigureFillingProduct);
            ScaleProductionPreset preset = scale.ProductionPreset!;
            var outputs = new List<Variant>();
            AssertGood(await CallAsync(harness.Owner, preset.Preset.AddProduct!, preset.Preset.NodeId,
                [Variant.From("New product"), Variant.From("C"), Variant.From(NodeId.Null)], outputs)
                .ConfigureAwait(false));
            var product = (AutomaticFillingProductState)preset.Find("C")!;
            Assert.That(outputs, Has.Count.EqualTo(1));
            Assert.That(outputs[0].TryGetValue(out NodeId productId), Is.True);
            Assert.That(productId, Is.EqualTo(product.NodeId));
            Assert.That(harness.Locks.GetState(productId).Locked, Is.True);

            AssertDenied(await WriteAsync(harness.Other, product.TargetWeight!, Variant.From(10.0))
                .ConfigureAwait(false));
            AssertGood(await WriteAsync(harness.Owner, product.TargetWeight!, Variant.From(20.0))
                .ConfigureAwait(false));
            Assert.That(ReadDouble(product.TargetWeight!), Is.EqualTo(20));
        }

        /// <summary>
        /// Lock denial happens before an existing asynchronous value callback, which remains active for the owner.
        /// </summary>
        [Test]
        [Category("SecurityRegression")]
        public async Task SecurityF43LockGuardPreservesExistingValueCallbackAsync()
        {
            using var harness = new Harness();
            int calls = 0;
            bool applicationAllowsWrites = true;
            ScaleHandle scale = harness.CreateScale(
                ScaleKind.AutomaticFilling,
                (context, state) =>
                {
                    ConfigureFillingProduct(context, state);
                    TargetItemState target = ((AutomaticFillingProductState)state).TargetWeight!;
                    target.OnReadUserAccessLevel = (_, _, ref value) =>
                    {
                        if (!applicationAllowsWrites)
                        {
                            value = (byte)(value & ~AccessLevels.CurrentWrite);
                        }
                        return ServiceResult.Good;
                    };
                    target.OnSimpleWriteValueAsync =
                        (_, _, value, _) =>
                        {
                            calls++;
                            ServiceResult result = value.TryGetValue(out double weight) && weight <= 20
                                ? ServiceResult.Good
                                : new ServiceResult(StatusCodes.BadOutOfRange);
                            return new ValueTask<AttributeWriteResult>(new AttributeWriteResult(result));
                        };
                });
            var product = (AutomaticFillingProductState)scale.ProductionPreset!.ActiveProduct!;
            await harness.AcquireAsync(product).ConfigureAwait(false);

            AssertDenied(await WriteAsync(harness.Other, product.TargetWeight!, Variant.From(15.0))
                .ConfigureAwait(false));
            Assert.That(calls, Is.Zero);
            ServiceResult invalid = await WriteAsync(harness.Owner, product.TargetWeight!, Variant.From(21.0))
                .ConfigureAwait(false);
            Assert.That(invalid.StatusCode, Is.EqualTo(StatusCodes.BadOutOfRange));
            Assert.That(ReadDouble(product.TargetWeight!), Is.EqualTo(10));
            AssertGood(await WriteAsync(harness.Owner, product.TargetWeight!, Variant.From(15.0))
                .ConfigureAwait(false));
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(ReadDouble(product.TargetWeight!), Is.EqualTo(15));
            applicationAllowsWrites = false;
            AssertDenied(await WriteAsync(harness.Owner, product.TargetWeight!, Variant.From(16.0))
                .ConfigureAwait(false));
            Assert.That(calls, Is.EqualTo(2));
            Assert.That(ReadDouble(product.TargetWeight!), Is.EqualTo(15));
        }

        /// <summary>
        /// Supplies a filling target whose changed value would change the production result.
        /// </summary>
        private static void ConfigureFillingProduct(ISystemContext context, ProductState product)
        {
            TargetItemState target = ((AutomaticFillingProductState)product).TargetWeight!;
            target.AddPlusTolerance(context)
                .AddMinusTolerance(context);
            target.WrappedValue = Variant.From(10.0);
            target.PlusTolerance!.WrappedValue = Variant.From(1.0);
            target.MinusTolerance!.WrappedValue = Variant.From(1.0);
        }

        /// <summary>
        /// Enters the real method validation and generated callback dispatch path.
        /// </summary>
        private static async ValueTask<ServiceResult> CallAsync(
            ISystemContext context,
            MethodState method,
            NodeId objectId,
            ArrayOf<Variant> inputs,
            List<Variant>? outputs = null)
        {
            var errors = new List<ServiceResult>();
            ServiceResult result = await method.CallAsync(context, objectId, inputs, errors, outputs ?? [])
                .ConfigureAwait(false);
            foreach (ServiceResult error in errors)
            {
                AssertGood(error);
            }
            return result;
        }

        /// <summary>
        /// Uses the ordinary asynchronous Value-attribute path, not the lock helper directly.
        /// </summary>
        private static ValueTask<ServiceResult> WriteAsync(
            ISystemContext context,
            BaseVariableState variable,
            Variant value)
        {
            return variable.WriteAttributeAsync(context, Attributes.Value, default, new DataValue(value));
        }

        /// <summary>
        /// Checks that both the node values and the controller's live reference retain the expected state.
        /// </summary>
        private static void AssertReference(
            PieceCountingController counting,
            PieceCountingProductState product,
            double weight,
            uint pieces,
            ulong count)
        {
            Assert.That(counting.ReferencePieceWeight, Is.EqualTo(weight));
            Assert.That(ReadDouble(product.ReferencePieceWeight!), Is.EqualTo(weight));
            Assert.That(ReadUInt32(product.NumberOfReferencePieces!), Is.EqualTo(pieces));
            Assert.That(counting.CurrentPieceCount, Is.EqualTo(count));
            Assert.That(counting.Scale.CurrentPieceCount!.WrappedValue.TryGetValue(out ulong publishedCount), Is.True);
            Assert.That(publishedCount, Is.EqualTo(count));
        }

        /// <summary>
        /// Requires the exact access-denial status rather than an unrelated failure.
        /// </summary>
        private static void AssertDenied(ServiceResult result)
        {
            Assert.That(result.StatusCode, Is.EqualTo(StatusCodes.BadUserAccessDenied), result.ToString());
        }

        /// <summary>
        /// Requires successful dispatch.
        /// </summary>
        private static void AssertGood(ServiceResult result)
        {
            Assert.That(ServiceResult.IsGood(result), Is.True, result.ToString());
        }

        /// <summary>
        /// Reads a real-valued product parameter.
        /// </summary>
        private static double ReadDouble(BaseVariableState variable)
        {
            Assert.That(variable.WrappedValue.TryGetValue(out double value), Is.True);
            return value;
        }

        /// <summary>
        /// Reads a piece-count parameter without boxing.
        /// </summary>
        private static uint ReadUInt32(BaseVariableState variable)
        {
            Assert.That(variable.WrappedValue.TryGetValue(out uint value), Is.True);
            return value;
        }

        /// <summary>
        /// Builds real scale nodes, controllers and locks with in-memory address-space services.
        /// </summary>
        private sealed class Harness : IDisposable
        {
            /// <summary>
            /// Creates different sessions with the same username so identity equality cannot authorize the attacker.
            /// </summary>
            public Harness()
            {
                ITelemetryContext telemetry = NUnitTelemetryContext.Create();
                var server = new Mock<IServerInternal>();
                var namespaces = new NamespaceTable();
                namespaces.Append("urn:security:f43:instances");
                namespaces.Append(Namespaces.Scales);
                namespaces.Append(Di.Namespaces.OpcUaDi);
                namespaces.Append(Machinery.Namespaces.Machinery);
                namespaces.Append(IA.Namespaces.IA);
                namespaces.Append(PackML.Namespaces.PackML);
                var types = new TypeTable(namespaces);
                types.AddSubtype(Ua.DataTypeIds.BaseDataType, NodeId.Null);
                types.AddSubtype(Ua.DataTypeIds.Structure, Ua.DataTypeIds.BaseDataType);
                types.AddSubtype(Ua.DataTypeIds.EUInformation, Ua.DataTypeIds.Structure);
                server.SetupGet(value => value.NamespaceUris).Returns(namespaces);
                server.SetupGet(value => value.ServerUris).Returns(new StringTable());
                server.SetupGet(value => value.TypeTree).Returns(types);
                server.SetupGet(value => value.Factory).Returns(EncodeableFactory.Create());
                server.SetupGet(value => value.Telemetry).Returns(telemetry);
                Context = new ServerSystemContext(server.Object)
                {
                    NodeIdFactory = new InstanceNodeIdFactory()
                };
                var identity = new Mock<IUserIdentity>();
                identity.SetupGet(value => value.DisplayName).Returns("same-operator");
                identity.SetupGet(value => value.TokenType).Returns(UserTokenType.UserName);
                Owner = Context.Copy();
                Owner.SessionId = new NodeId("owner-session", 1);
                Owner.UserIdentity = identity.Object;
                Other = Context.Copy();
                Other.SessionId = new NodeId("other-session", 1);
                Other.UserIdentity = identity.Object;
                Locks = new DefaultLockService(timeProvider: new FixedTimeProvider());
                m_services = new ScaleRuntimeServices(
                    Context,
                    ScaleNamespaceIndices.Resolve(namespaces),
                    new ScalesServerOptions(),
                    Register,
                    (nodeId, _) => new ValueTask<bool>(Registered.Remove(nodeId)),
                    (type, baseType) => type == baseType,
                    Locks,
                    telemetry.CreateLogger<Harness>());
            }

            /// <summary>
            /// Gets the trusted equipment context.
            /// </summary>
            public ServerSystemContext Context { get; }

            /// <summary>
            /// Gets the product-lock owner's remote request context.
            /// </summary>
            public ServerSystemContext Owner { get; }

            /// <summary>
            /// Gets the independent session with the same username and normal write permission.
            /// </summary>
            public ServerSystemContext Other { get; }

            /// <summary>
            /// Gets the production DI lock service.
            /// </summary>
            public DefaultLockService Locks { get; }

            /// <summary>
            /// Gets the in-memory published nodes.
            /// </summary>
            public Dictionary<NodeId, NodeState> Registered { get; } = [];

            /// <summary>
            /// Uses the same factory, builder and controller creation as the server runtime.
            /// </summary>
            public ScaleHandle CreateScale(
                ScaleKind kind,
                Action<ISystemContext, ProductState>? configureProduct = null,
                bool lockable = true)
            {
                ScaleDeviceState scale = ScaleNodes.CreateScale(
                    Context,
                    kind,
                    new FolderState(null),
                    new QualifiedName("Scale", 1));
                var builder = new ScaleBuilder(m_services, scale, kind);
                builder.WithIdentification(ScalesNodeManagerTests.Identity("SecurityF43"))
                    .WithWeighingRange(new WeighingRangeDefinition(0, 100, 0.01, 0.01))
                    .WithZeroAndTare()
                    .WithRegisterWeight()
                    .WithTypeFeatures()
                    .WithProductionPreset(preset =>
                    {
                        preset.AllowManagement().AllowSelection();
                        if (lockable)
                        {
                            preset.WithLocking();
                        }
                        preset.AddProduct("A", new LocalizedText("A"), configureProduct)
                            .AddProduct("B", new LocalizedText("B"), configureProduct)
                            .Select("A");
                    });
                builder.Finish();
                m_services.Register(scale);
                return builder.CreateHandle();
            }

            /// <summary>
            /// Acquires the real product lock through its remotely callable DI method.
            /// </summary>
            public async ValueTask AcquireAsync(ProductState product)
            {
                Assert.That(product.Lock, Is.Not.Null);
                var outputs = new List<Variant>();
                AssertGood(await CallAsync(Owner, product.Lock!.InitLock!, product.Lock.NodeId,
                    [Variant.From("owner")], outputs).ConfigureAwait(false));
                Assert.That(outputs, Has.Count.EqualTo(1));
                Assert.That(outputs[0].TryGetValue(out int status), Is.True);
                Assert.That(status, Is.EqualTo(LockStatus.Ok));
            }

            /// <summary>
            /// Releases the lock using the owning session, not a privileged break operation.
            /// </summary>
            public async ValueTask ReleaseAsync(ProductState product)
            {
                var outputs = new List<Variant>();
                AssertGood(await CallAsync(Owner, product.Lock!.ExitLock!, product.Lock.NodeId, [], outputs)
                    .ConfigureAwait(false));
                Assert.That(outputs, Has.Count.EqualTo(1));
                Assert.That(outputs[0].TryGetValue(out int status), Is.True);
                Assert.That(status, Is.EqualTo(LockStatus.Ok));
            }

            /// <inheritdoc/>
            public void Dispose()
            {
                m_services.Dispose();
                Locks.Dispose();
            }

            /// <summary>
            /// Completes each generated node's ordinary registration lifecycle and indexes the subtree.
            /// </summary>
            private void Register(NodeState node)
            {
                node.CreateAsPredefinedNode(Context);
                Registered[node.NodeId] = node;
                var children = new List<BaseInstanceState>();
                node.GetChildren(Context, children);
                foreach (BaseInstanceState child in children)
                {
                    Register(child);
                }
            }

            /// <summary>The real runtime dependencies.</summary>
            private readonly ScaleRuntimeServices m_services;
        }

        /// <summary>
        /// Supplies unique instance NodeIds without a node manager or external state.
        /// </summary>
        private sealed class InstanceNodeIdFactory : INodeIdFactory
        {
            /// <inheritdoc/>
            public NodeId New(ISystemContext context, NodeState node)
            {
                return new NodeId((uint)Interlocked.Increment(ref m_nextId), 1);
            }

            /// <summary>The last allocated instance id.</summary>
            private int m_nextId;
        }

        /// <summary>
        /// Keeps lock ownership independent of wall-clock scheduling.
        /// </summary>
        private sealed class FixedTimeProvider : TimeProvider
        {
            /// <inheritdoc/>
            public override DateTimeOffset GetUtcNow()
            {
                return new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.Zero);
            }
        }
    }
}
