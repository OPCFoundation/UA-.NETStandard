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
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Scales.Server.Builders;
using Opc.Ua.Scales.Server.Runtime;

namespace Opc.Ua.Scales.Tests
{
    /// <summary>
    /// The type-specific runtime of each OPC 40200 scale type, the modules,
    /// the scale system, statistics and recipes.
    /// </summary>
    [TestFixture]
    [Category("Scales")]
    [NonParallelizable]
    public sealed class ScaleTypeControllerTests
    {
        private ScalesServerFixture m_fixture = null!;

        [SetUp]
        public async Task SetUpAsync()
        {
            m_fixture = new ScalesServerFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            await m_fixture.DisposeAsync().ConfigureAwait(false);
        }

        private ISystemContext Ctx => m_fixture.Context;

        private ValueTask<ScaleHandle> CreateAsync(ScaleKind kind, Action<IScaleBuilder>? extra = null, string name = "S")
        {
            return m_fixture.Manager.CreateScaleAsync(
                m_fixture.Name(name),
                kind,
                b =>
                {
                    ScalesNodeManagerTests.FullScale(b, name);
                    extra?.Invoke(b);
                });
        }

        private ServiceResult Call(MethodState? method, NodeId objectId, params Variant[] inputs)
        {
            Assert.That(method?.OnCallMethod2, Is.Not.Null, "the method is materialised and bound");
            return method!.OnCallMethod2!(Ctx, method, objectId, inputs.ToArrayOf(), []);
        }

        private static void AssertGood(ServiceResult result)
        {
            Assert.That(ServiceResult.IsGood(result), Is.True, result.ToString());
        }

        private static void AssertStatus(ServiceResult result, StatusCode expected)
        {
            Assert.That(result.StatusCode, Is.EqualTo(expected), result.ToString());
        }

        [Test]
        public async Task LossInWeightDischargesAndRefillsAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.LossInWeight);
            LossInWeightController liw = scale.LossInWeight!;
            LossInWeightScaleState node = liw.Scale;

            AssertGood(Call(node.DischargeStart, node.NodeId));
            Assert.That(liw.Discharging, Is.True);
            AssertStatus(Call(node.DischargeStart, node.NodeId), StatusCodes.BadInvalidState);
            AssertGood(Call(node.DischargeStop, node.NodeId));
            AssertStatus(Call(node.DischargeStop, node.NodeId), StatusCodes.BadInvalidState);
            AssertGood(Call(node.RefillStart, node.NodeId));
            Assert.That(liw.Refilling, Is.True);
            AssertStatus(Call(node.RefillStart, node.NodeId), StatusCodes.BadInvalidState);
            liw.SetActivity(discharging: null, refilling: false);
            AssertStatus(Call(node.RefillStop, node.NodeId), StatusCodes.BadInvalidState);

            liw.PublishHopper(12.5, 40, binWeight: 100);
            Assert.That(node.HopperWeight!.WrappedValue.TryGetValue(out double hopper), Is.True);
            Assert.That(hopper, Is.EqualTo(12.5));
            Assert.That(scale.Continuous, Is.Not.Null, "a loss-in-weight scale is a continuous scale");
        }

        [Test]
        [CancelAfter(60000)]
        public async Task EquipmentReportsAreAtomicAgainstTheScaleLockAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.LossInWeight);
            LossInWeightController liw = scale.LossInWeight!;
            LossInWeightScaleState node = liw.Scale;
            liw.SetActivity(discharging: true, refilling: false);

            // The equipment flips between two complete states; a method or an
            // Update block holds the scale lock and must never see half of one.
            const int iterations = 5000;
            Task equipment = Task.Run(() =>
            {
                for (int ii = 0; ii < iterations; ii++)
                {
                    liw.SetActivity(discharging: false, refilling: true);
                    liw.SetActivity(discharging: true, refilling: false);
                }
            });
            int torn = 0;
            Task observer = Task.Run(() =>
            {
                for (int ii = 0; ii < iterations; ii++)
                {
                    scale.Update(() =>
                    {
                        if (node.Discharging!.Value == node.Refilling!.Value)
                        {
                            torn++;
                        }
                    });
                }
            });
            await Task.WhenAll(equipment, observer);

            Assert.That(torn, Is.Zero, "an observer under the scale lock saw a half-applied SetActivity");
        }

        [Test]
        [CancelAfter(60000)]
        public async Task CalibrationReportsAreAtomicAgainstTheScaleLockAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.Laboratory);
            LaboratoryController lab = scale.Laboratory!;
            LaboratoryScaleState node = lab.Scale;

            const int iterations = 5000;
            Task equipment = Task.Run(() =>
            {
                for (int ii = 0; ii < iterations; ii++)
                {
                    scale.Update(() =>
                    {
                        node.CalibrationRunning!.Value = true;
                        node.CalibrationNeeded!.Value = true;
                    });
                    lab.CompleteCalibration();
                }
            });
            int torn = 0;
            Task observer = Task.Run(() =>
            {
                for (int ii = 0; ii < iterations; ii++)
                {
                    scale.Update(() =>
                    {
                        if (node.CalibrationRunning!.Value != node.CalibrationNeeded!.Value)
                        {
                            torn++;
                        }
                    });
                }
            });
            await Task.WhenAll(equipment, observer);

            Assert.That(torn, Is.Zero, "an observer under the scale lock saw a half-applied CompleteCalibration");
            Assert.That(() => scale.Update(null!), Throws.ArgumentNullException);
        }

        [Test]
        public async Task ContinuousScaleTotalizesAndResetsAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.Continuous, b => b.AddTotalizer("Shift"));
            ContinuousController continuous = scale.Continuous!;
            Assert.That(continuous.Totalizers, Has.Count.EqualTo(2), "master and shift totalizer");

            continuous.PublishFlow(120, load: 5, speed: 1.2, controlMagnitude: 55);
            continuous.Totalize(10);
            continuous.Totalize(2.5);
            TotalizerState master = continuous.Scale.MasterTotalizer!;
            Assert.That(continuous.TotalOf(master), Is.EqualTo(12.5));

            AssertGood(Call(master.ResetTotalizer, master.NodeId));
            Assert.That(continuous.TotalOf(master), Is.Zero);
            TotalizerState shift = continuous.Totalizers.ToArray()!.First(t => t != master);
            Assert.That(continuous.TotalOf(shift), Is.EqualTo(12.5), "the shift totalizer keeps its total");
            Assert.That(() => m_fixture.Manager.CreateScaleAsync(
                m_fixture.Name("NotContinuous"),
                ScaleKind.Simple,
                b => b.AddTotalizer("x")).AsTask(), Throws.InvalidOperationException);
        }

        [Test]
        public async Task PieceCountingReferencesAndCountsAsync()
        {
            ScaleHandle scale = await CreateAsync(
                ScaleKind.PieceCounting,
                b => b
                    .WithAllowedEngineeringUnits(ScaleUnits.Kilogram, ScaleUnits.Gram)
                    .WithProductionPreset(p => p.AllowSelection().AddProduct("Screws", new LocalizedText("M4")).Select("Screws")));
            PieceCountingController counting = scale.PieceCounting!;
            PieceCountingScaleState node = counting.Scale;
            var product = (PieceCountingProductState)scale.ProductionPreset!.ActiveProduct!;

            SetReferencePieceWeightMethodState setWeight = node.SetReferencePieceWeight!;
            AssertGood(setWeight.OnCall!(Ctx, setWeight, node.NodeId, 5, ScaleUnits.Gram));
            Assert.That(counting.ReferencePieceWeight, Is.EqualTo(0.005).Within(1e-12));
            AssertStatus(setWeight.OnCall(Ctx, setWeight, node.NodeId, 0, ScaleUnits.Gram), StatusCodes.BadOutOfRange);
            AssertStatus(setWeight.OnCall(Ctx, setWeight, node.NodeId, 5, ScaleUnits.Pound), StatusCodes.BadInvalidArgument);

            scale.PublishLoad(0.5);
            Assert.That(counting.CurrentPieceCount, Is.EqualTo(100UL));

            SetNumberOfReferencePiecesMethodState setCount = node.SetNumberOfReferencePieces!;
            AssertGood(setCount.OnCall!(Ctx, setCount, node.NodeId, 10));
            AssertStatus(setCount.OnCall(Ctx, setCount, node.NodeId, 0), StatusCodes.BadOutOfRange);

            StartReferenceMethodState start = node.StartReference!;
            scale.PublishLoad(0.08);
            AssertGood(start.OnCall!(Ctx, start, node.NodeId, 20));
            Assert.That(counting.ReferencePieceWeight, Is.EqualTo(0.004).Within(1e-12));
            AssertStatus(start.OnCall(Ctx, start, node.NodeId, 0), StatusCodes.BadOutOfRange);
            scale.PublishLoad(0);
            AssertStatus(start.OnCall(Ctx, start, node.NodeId, 5), StatusCodes.BadInvalidState);

            product.AddSetTargetItemCount(Ctx);
            product.AddSetTargetPieceCount(Ctx);
        }

        [Test]
        public async Task PieceCountingLocalCallsMatchTheMethodsAsync()
        {
            ScaleHandle scale = await CreateAsync(
                ScaleKind.PieceCounting,
                b => b
                    .WithAllowedEngineeringUnits(ScaleUnits.Kilogram, ScaleUnits.Gram)
                    .WithProductionPreset(p => p.AllowSelection().AddProduct("Screws", new LocalizedText("M4")).Select("Screws")));
            PieceCountingController counting = scale.PieceCounting!;
            var product = (PieceCountingProductState)scale.ProductionPreset!.ActiveProduct!;

            // The local call takes the real weight, not the method's UInt32.
            AssertGood(counting.SetReferencePieceWeight(4.5, ScaleUnits.Gram));
            Assert.That(counting.ReferencePieceWeight, Is.EqualTo(0.0045).Within(1e-12));
            AssertStatus(counting.SetReferencePieceWeight(0), StatusCodes.BadOutOfRange);
            AssertStatus(counting.SetReferencePieceWeight(double.NaN), StatusCodes.BadOutOfRange);
            AssertStatus(counting.SetReferencePieceWeight(5, ScaleUnits.Pound), StatusCodes.BadInvalidArgument);
            scale.PublishLoad(0.45);
            Assert.That(counting.CurrentPieceCount, Is.EqualTo(100UL));

            AssertGood(counting.SetNumberOfReferencePieces(10));
            Assert.That(product.NumberOfReferencePieces!.WrappedValue.TryGetValue(out uint pieces), Is.True);
            Assert.That(pieces, Is.EqualTo(10u));
            AssertStatus(counting.SetNumberOfReferencePieces(0), StatusCodes.BadOutOfRange);

            scale.PublishLoad(0.08);
            AssertGood(counting.StartReference(20));
            Assert.That(counting.ReferencePieceWeight, Is.EqualTo(0.004).Within(1e-12));

            scale.CommandInterceptor = command => command == ScaleCommand.StartReference
                ? ServiceResult.Create(StatusCodes.BadUserAccessDenied, "interlocked")
                : ServiceResult.Good;
            AssertStatus(counting.StartReference(20), StatusCodes.BadUserAccessDenied);
        }

        [Test]
        public async Task PieceCountingProductTargetsCanBeSetAsync()
        {
            ScaleHandle scale = await CreateAsync(
                ScaleKind.PieceCounting,
                b => b.WithProductionPreset(p => p.AddProduct(
                    "Nuts",
                    new LocalizedText("M6"),
                    (ctx, product) =>
                    {
                        var pieces = (PieceCountingProductState)product;
                        pieces.AddSetTargetItemCount(ctx);
                        pieces.AddSetTargetPieceCount(ctx);
                    })));
            var product = (PieceCountingProductState)scale.ProductionPreset!.Find("Nuts")!;
            AssertGood(product.SetTargetItemCount!.OnCall!(Ctx, product.SetTargetItemCount, product.NodeId, 12));
            Assert.That(product.TargetItemCount!.WrappedValue.TryGetValue(out uint items), Is.True);
            Assert.That(items, Is.EqualTo(12u));
            AssertGood(product.SetTargetPieceCount!.OnCall!(Ctx, product.SetTargetPieceCount, product.NodeId, 50, 2, 1));
            Assert.That(product.TargetPieceCount!.PlusTolerance, Is.Not.Null);
        }

        [Test]
        public async Task LaboratoryShieldsLevelingCalibrationAndIonisatorAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.Laboratory);
            LaboratoryController lab = scale.Laboratory!;
            LaboratoryScaleState node = lab.Scale;

            CloseDraftShieldsMethodState close = node.CloseDraftShields!;
            OpenDraftShieldsMethodState open = node.OpenDraftShields!;
            AssertGood(close.OnCall!(Ctx, close, node.NodeId, DraftShieldType.All_3));
            Assert.That(node.DraftShieldLeftClosed!.Value, Is.True);
            Assert.That(node.DraftShieldRightClosed!.Value, Is.True);
            Assert.That(node.DraftShieldTopClosed!.Value, Is.True);
            AssertGood(open.OnCall!(Ctx, open, node.NodeId, DraftShieldType.Top_2));
            Assert.That(node.DraftShieldTopClosed.Value, Is.False);
            AssertGood(open.OnCall(Ctx, open, node.NodeId, DraftShieldType.Left_1));
            AssertGood(open.OnCall(Ctx, open, node.NodeId, DraftShieldType.Right_0));
            AssertStatus(open.OnCall(Ctx, open, node.NodeId, (DraftShieldType)9), StatusCodes.BadInvalidArgument);

            AssertGood(Call(node.StartLeveling, node.NodeId));
            Assert.That(node.LevelingRunning!.Value, Is.True);
            AssertStatus(Call(node.StartLeveling, node.NodeId), StatusCodes.BadInvalidState);
            lab.CompleteLeveling();
            Assert.That(node.LevelingRunning.Value, Is.False);

            lab.SetCalibrationNeeded(true);
            lab.CompleteImmediately = true;
            AssertGood(Call(node.StartCalibration, node.NodeId));
            Assert.That(node.CalibrationRunning!.Value, Is.False);
            Assert.That(node.CalibrationNeeded!.Value, Is.False);

            AssertGood(Call(node.StartIonisator, node.NodeId));
            Assert.That(node.IonisatorRunning!.Value, Is.True);
            AssertGood(Call(node.StopIonisator, node.NodeId));
            Assert.That(node.IonisatorRunning.Value, Is.False);
        }

        [Test]
        public async Task LaboratoryWithoutShieldsReportsNotSupportedAsync()
        {
            ScaleHandle scale = await m_fixture.Manager.CreateScaleAsync(
                m_fixture.Name("Lab"),
                ScaleKind.Laboratory,
                b => b
                    .WithIdentification(ScalesNodeManagerTests.Identity("lab"))
                    .WithWeighingRange(new WeighingRangeDefinition(0, 0.2, 0.00001, 0.001))
                    .With<LaboratoryScaleState>((ctx, lab) => lab.AddOpenDraftShields(ctx)));
            LaboratoryScaleState node = scale.Laboratory!.Scale;
            AssertStatus(
                node.OpenDraftShields!.OnCall!(Ctx, node.OpenDraftShields, node.NodeId, DraftShieldType.Top_2),
                StatusCodes.BadNotSupported);
            Assert.That(
                () => m_fixture.Manager.CreateScaleAsync(
                    m_fixture.Name("Wrong"),
                    ScaleKind.Simple,
                    b => b.With<LaboratoryScaleState>((ctx, lab) => { })).AsTask(),
                Throws.InvalidOperationException);
        }

        [Test]
        public async Task HopperPublishesLevelsAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.Hopper);
            scale.Hopper!.PublishLevels(true, false, 95, 5);
            Assert.That(scale.Hopper.Scale.LimitMax!.Value, Is.True);
            Assert.That(scale.Hopper.Scale.LevelMin!.WrappedValue.TryGetValue(out double level), Is.True);
            Assert.That(level, Is.EqualTo(5));
        }

        [Test]
        public async Task AutomaticFillingEvaluatesAgainstTheTargetAsync()
        {
            ScaleHandle scale = await CreateAsync(
                ScaleKind.AutomaticFilling,
                b => b.WithProductionPreset(p => p.AllowSelection().AddProduct(
                    "Flour",
                    new LocalizedText("Flour 1 kg"),
                    (ctx, product) =>
                    {
                        TargetItemState target = ((AutomaticFillingProductState)product).TargetWeight!;
                        target.AddPlusTolerance(ctx);
                        target.AddMinusTolerance(ctx);
                        target.WrappedValue = Variant.From(1.0);
                        target.PlusTolerance!.WrappedValue = Variant.From(0.01);
                        target.MinusTolerance!.WrappedValue = Variant.From(0.005);
                    }).Select("Flour")));
            AutomaticFillingController filling = scale.AutomaticFilling!;
            Assert.That(filling.EvaluateFilling(1.002), Is.EqualTo(ToleranceState.In_0));
            Assert.That(filling.EvaluateFilling(1.02), Is.EqualTo(ToleranceState.Over_2));
            Assert.That(filling.EvaluateFilling(0.99), Is.EqualTo(ToleranceState.Under_1));
            Assert.That(filling.Scale.ToleranceState!.Value, Is.EqualTo(ToleranceState.Under_1));

            ScaleHandle noProduct = await CreateAsync(ScaleKind.AutomaticFilling, name: "S2");
            Assert.That(noProduct.AutomaticFilling!.EvaluateFilling(5), Is.EqualTo(ToleranceState.In_0));
        }

        [Test]
        public async Task VehicleLocalCallsMatchTheMethodsAsync()
        {
            ScaleHandle scale = await CreateAsync(
                ScaleKind.Vehicle,
                b => b
                    .WithWeighingRange(new WeighingRangeDefinition(0, 60000, 20, 20))
                    .WithProductionPreset(p => p
                        .AddProduct("TRUCK-1", new LocalizedText("Truck 1"))
                        .AddProduct(
                            "TRUCK-2",
                            new LocalizedText("Truck 2"),
                            (ctx, product) =>
                            {
                                var vehicle = (VehicleProductState)product;
                                vehicle.AddTare(ctx);
                                vehicle.Tare!.WrappedValue = Variant.From(12000.0);
                            })));
            VehicleController vehicles = scale.Vehicle!;

            scale.PublishLoad(40000);
            AssertGood(vehicles.InboundWeighing("TRUCK-1"));
            scale.PublishLoad(15000);
            AssertGood(vehicles.OutboundWeighing("TRUCK-1"));
            var truck1 = (VehicleProductState)scale.ProductionPreset!.Find("TRUCK-1")!;
            Assert.That(truck1.DeltaWeight!.Value.Gross, Is.EqualTo(25000).Within(1e-6));

            scale.PublishLoad(30000);
            AssertGood(vehicles.OnePassWeighing("TRUCK-2"));
            var truck2 = (VehicleProductState)scale.ProductionPreset.Find("TRUCK-2")!;
            Assert.That(truck2.DeltaWeight!.Value.Gross, Is.EqualTo(18000).Within(1e-6));
            AssertStatus(vehicles.OnePassWeighing("TRUCK-1"), StatusCodes.BadInvalidState);
            AssertStatus(vehicles.InboundWeighing("nope"), StatusCodes.BadNotFound);
        }

        [Test]
        public async Task VehicleWeighingComputesTheDeltaAsync()
        {
            ScaleHandle scale = await CreateAsync(
                ScaleKind.Vehicle,
                b => b
                    .WithWeighingRange(new WeighingRangeDefinition(0, 60000, 20, 20))
                    .WithProductionPreset(p => p
                        .AddProduct("TRUCK-1", new LocalizedText("Truck 1"))
                        .AddProduct(
                            "TRUCK-2",
                            new LocalizedText("Truck 2"),
                            (ctx, product) =>
                            {
                                var vehicle = (VehicleProductState)product;
                                vehicle.AddTare(ctx);
                                vehicle.Tare!.WrappedValue = Variant.From(12000.0);
                                vehicle.AddGetVehicleInformation(ctx);
                            })));
            VehicleScaleState node = scale.Vehicle!.Scale;

            scale.PublishLoad(40000);
            AssertGood(node.InboundWeighing!.OnCall!(Ctx, node.InboundWeighing, node.NodeId, "TRUCK-1"));
            scale.PublishLoad(15000);
            AssertGood(node.OutboundWeighing!.OnCall!(Ctx, node.OutboundWeighing, node.NodeId, "TRUCK-1"));
            var truck1 = (VehicleProductState)scale.ProductionPreset!.Find("TRUCK-1")!;
            Assert.That(truck1.DeltaWeight!.Value.Gross, Is.EqualTo(25000).Within(1e-6));
            Assert.That(truck1.InboundScale!.Value, Is.EqualTo(scale.NodeId));

            scale.PublishLoad(30000);
            AssertGood(node.OnePassWeighing!.OnCall!(Ctx, node.OnePassWeighing, node.NodeId, "TRUCK-2"));
            var truck2 = (VehicleProductState)scale.ProductionPreset.Find("TRUCK-2")!;
            Assert.That(truck2.DeltaWeight!.Value.Gross, Is.EqualTo(18000).Within(1e-6));

            AssertStatus(node.OnePassWeighing.OnCall(Ctx, node.OnePassWeighing, node.NodeId, "TRUCK-1"), StatusCodes.BadInvalidState);
            AssertStatus(node.InboundWeighing.OnCall(Ctx, node.InboundWeighing, node.NodeId, "nope"), StatusCodes.BadNotFound);
            scale.PublishLoad(30000, stable: false);
            AssertStatus(node.OutboundWeighing.OnCall(Ctx, node.OutboundWeighing, node.NodeId, "TRUCK-2"), StatusCodes.BadInvalidState);

            GetVehicleInformationMethodState info = truck2.GetVehicleInformation!;
            AssertStatus(info.OnCall!(Ctx, info, truck2.NodeId, "TRUCK-2"), StatusCodes.BadNotSupported);
            scale.ProductionPreset.VehicleInformationProvider = id => id == "TRUCK-2"
                ? new VehicleInformation
                {
                    Tare = 11800,
                    TareExpirationDate = new DateTime(2027, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                    CarrierId = "C1",
                    CarrierDisplayName = new LocalizedText("Carrier"),
                    DriverId = "D1",
                    DriverDisplayName = new LocalizedText("Driver"),
                    Customer = new LocalizedText("Customer"),
                    Supplier = new LocalizedText("Supplier"),
                    Destination = new LocalizedText("Depot")
                }
                : null;
            AssertGood(info.OnCall(Ctx, info, truck2.NodeId, "TRUCK-2"));
            Assert.That(truck2.DriverId!.Value, Is.EqualTo("D1"));
            Assert.That(truck2.Destination!.Value.Text, Is.EqualTo("Depot"));
            AssertStatus(info.OnCall(Ctx, info, truck2.NodeId, "TRUCK-9"), StatusCodes.BadNotFound);
        }

        [Test]
        public async Task FeederAndPrinterModulesAsync()
        {
            ScaleHandle scale = await CreateAsync(
                ScaleKind.Checkweigher,
                b => b
                    .AddFeederModule("Infeed", f => f
                        .WithIdentification(ScalesNodeManagerTests.Identity("F1") with { ProductInstanceUri = null })
                        .WithMachineryBuildingBlocks()
                        .WithFeederSpeed(0.2, 2.0, ScalesTestUnits.MetrePerSecond, 1.0))
                    .AddPrinterModule("Labeler", p => p
                        .WithIdentification(ScalesNodeManagerTests.Identity("P1"))
                        .WithMachineryBuildingBlocks()
                        .WithLabel("L-50", 50, 30, ScalesTestUnits.Millimetre)
                        .With((ctx, module) => module.AddAssetId(ctx))));
            Assert.That(scale.Modules, Has.Count.EqualTo(2));
            ScaleModuleHandle feeder = scale.Modules.ToArray()!.First(m => m.Feeder != null);
            ScaleModuleHandle printer = scale.Modules.ToArray()!.First(m => m.Printer != null);
            Assert.That(feeder.Feeder!.Identification!.TypeDefinitionId.TryGetValue(out uint idType), Is.True);
            Assert.That(idType, Is.EqualTo(Opc.Ua.Machinery.ObjectTypes.MachineryComponentIdentificationType));

            SetFeederSpeedMethodState setSpeed = feeder.Feeder.SetFeederSpeed!;
            AssertGood(setSpeed.OnCall!(Ctx, setSpeed, feeder.NodeId, 1.5f, ScalesTestUnits.MetrePerSecond));
            AssertStatus(setSpeed.OnCall(Ctx, setSpeed, feeder.NodeId, 3f, ScalesTestUnits.MetrePerSecond), StatusCodes.BadOutOfRange);
            AssertStatus(setSpeed.OnCall(Ctx, setSpeed, feeder.NodeId, 1f, ScaleUnits.Kilogram), StatusCodes.BadInvalidArgument);
            AssertStatus(printer.SetFeederSpeed(1f, ScalesTestUnits.MetrePerSecond), StatusCodes.BadNotSupported);

            feeder.PublishFeeder(false, load: 3, expectedRunning: true);
            Assert.That(feeder.Notifications.IsAlarmActive(ScaleNotificationId.FeederNotRunning), Is.True);
            feeder.PublishFeeder(true, expectedRunning: true);
            Assert.That(feeder.Notifications.IsAlarmActive(ScaleNotificationId.FeederNotRunning), Is.False);
            feeder.SetItemState(ScaleItemState.Executing);
            printer.PublishFeeder(true);
            feeder.PublishPrinterStock(10, 10);

            printer.PublishPrinterStock(42, 88);
            Assert.That(printer.Printer!.LabelStock!.WrappedValue.TryGetValue(out double stock), Is.True);
            Assert.That(stock, Is.EqualTo(42));

            scale.CommandInterceptor = command => ServiceResult.Create(StatusCodes.BadDeviceFailure, "no");
            AssertStatus(setSpeed.OnCall(Ctx, setSpeed, feeder.NodeId, 1.5f, ScalesTestUnits.MetrePerSecond), StatusCodes.BadDeviceFailure);
            Assert.That(scale.Scale.SubDevices!.SupportedTypes, Is.Not.Null);
        }

        [Test]
        public async Task ModuleWithoutIdentificationIsRejectedAsync()
        {
            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await CreateAsync(ScaleKind.Simple, b => b.AddFeederModule("F")));
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await CreateAsync(ScaleKind.Simple, b => b.AddPrinterModule("P", p => p.WithFeederSpeed(0, 1, ScaleUnits.Percent, 0)), "S2"));
            Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await CreateAsync(ScaleKind.Simple, b => b.AddFeederModule("F", f => f.WithLabel("x", 1, 1, ScaleUnits.Percent)), "S3"));
        }

        [Test]
        public async Task WeighingModulesAreScalesOfTheirOwnAsync()
        {
            ScaleHandle scale = await CreateAsync(
                ScaleKind.TotalizingHopper,
                b => b.AddWeighingModule("Bridge1", m => ScalesNodeManagerTests.FullScale(m, "Bridge1")));
            Assert.That(scale.WeighingModules, Has.Count.EqualTo(1));
            ScaleHandle bridge = scale.WeighingModules[0];
            Assert.That(bridge.Kind, Is.EqualTo(ScaleKind.WeighingModule));
            bridge.PublishLoad(1);
            Assert.That(bridge.CurrentReading.Gross, Is.EqualTo(1));
            Assert.That(m_fixture.Manager.FindScale(bridge.NodeId), Is.SameAs(bridge));
            Assert.That(m_fixture.Manager.FindScale(NodeId.Null), Is.Null);
        }

        [Test]
        public async Task CheckweigherStatisticsCountAcceptedAndRejectedAsync()
        {
            ScaleHandle scale = await CreateAsync(
                ScaleKind.Checkweigher,
                b => b.WithProductionPreset(p => p.AddProduct(
                    "P1",
                    new LocalizedText("Box"),
                    (ctx, product) =>
                    {
                        var checkweigher = (CheckweigherProductState)product;
                        checkweigher.AddStatistic(ctx);
                    })));
            var product = (CheckweigherProductState)scale.ProductionPreset!.Find("P1")!;
            var statistic = (CheckweigherStatisticState)product.Statistic!;
            statistic.AddTotalPackages(Ctx);
            statistic.AddTotalPackagesAccepted(Ctx);
            statistic.AddTotalPackagesRejected(Ctx);
            statistic.AddPackagesAcceptedWithLowerToleranceLimit1(Ctx);
            statistic.AddPackagesRejectedByMetal(Ctx);
            statistic.AddPercentageLowerToleranceLimit(Ctx);
            statistic.AddLastItem(Ctx);
            statistic.TotalPackages!.AddMeanValue(Ctx);
            statistic.TotalPackages.AddStandardDeviation(Ctx);

            var recorder = new ScaleStatistics(Ctx, statistic);
            recorder.RecordAccepted(1.0);
            recorder.RecordAccepted(0.98, belowLowerToleranceLimit1: true, itemId: "I2");
            recorder.RecordRejected(1.2, CheckweigherRejectReason.Metal, "I3");
            recorder.RecordRejected(double.NaN, CheckweigherRejectReason.Vision);
#if NET8_0_OR_GREATER
            CheckweigherRejectReason[] reasons = Enum.GetValues<CheckweigherRejectReason>();
#else
            var reasons = (CheckweigherRejectReason[])Enum.GetValues(typeof(CheckweigherRejectReason));
#endif
            foreach (CheckweigherRejectReason reason in reasons)
            {
                recorder.RecordRejected(1.0, reason);
            }

            Assert.That(statistic.TotalPackagesAccepted!.ItemCount!.WrappedValue.TryGetValue(out ulong accepted), Is.True);
            Assert.That(accepted, Is.EqualTo(2UL));
            Assert.That(statistic.PackagesRejectedByMetal!.ItemCount!.WrappedValue.TryGetValue(out ulong metal), Is.True);
            Assert.That(metal, Is.EqualTo(2UL));
            Assert.That(statistic.PercentageLowerToleranceLimit!.WrappedValue.TryGetValue(out double percent), Is.True);
            Assert.That(percent, Is.GreaterThan(0));
            Assert.That(statistic.TotalPackages.MeanValue!.WrappedValue.TryGetValue(out double mean), Is.True);
            Assert.That(mean, Is.GreaterThan(0.9));

            recorder.Reset("shift change");
            Assert.That(statistic.TotalPackagesAccepted.ItemCount.WrappedValue.TryGetValue(out ulong reset), Is.True);
            Assert.That(reset, Is.Zero);
            recorder.Record(2.5, "I9");
            Assert.That(scale.ProductionOutput, Is.Not.Null);
        }

        [Test]
        public async Task CheckweigherStatisticsResetTheTolerancePercentageAsync()
        {
            ScaleHandle scale = await CreateAsync(
                ScaleKind.Checkweigher,
                b => b.WithProductionPreset(p => p.AddProduct(
                    "P1",
                    new LocalizedText("Box"),
                    (ctx, product) => ((CheckweigherProductState)product).AddStatistic(ctx)))).ConfigureAwait(false);
            var product = (CheckweigherProductState)scale.ProductionPreset!.Find("P1")!;
            var statistic = (CheckweigherStatisticState)product.Statistic!;
            statistic.AddTotalPackages(Ctx);
            statistic.AddPackagesAcceptedWithLowerToleranceLimit1(Ctx);
            statistic.AddPercentageLowerToleranceLimit(Ctx);
            var recorder = new ScaleStatistics(Ctx, statistic);

            recorder.RecordAccepted(0.98, belowLowerToleranceLimit1: true);
            Assert.That(statistic.PercentageLowerToleranceLimit!.WrappedValue.TryGetValue(out double before), Is.True);
            Assert.That(before, Is.EqualTo(100.0));

            // The percentage is derived from the counters and restarts with them.
            recorder.Reset("shift change");
            Assert.That(statistic.PercentageLowerToleranceLimit.WrappedValue.TryGetValue(out double after), Is.True);
            Assert.That(after, Is.Zero);
        }

        private static readonly string[] s_breadElements = ["Flour", "Water", "Rest"];

        [Test]
        [CancelAfter(30000)]
        public async Task HandlersMayCompleteElementsSynchronouslyAsync()
        {
            ScaleHandle scale = await CreateAsync(
                ScaleKind.Recipe,
                b => b.WithProductionPreset(p => p.AllowSelection().AddProduct(
                    "Dough",
                    new LocalizedText("Dough"),
                    (ctx, product) => ((RecipeProductState)product).Report!.Value = default).Select("Dough")));
            RecipeController recipes = scale.Recipes!;
            ushort ns = m_fixture.Manager.NamespaceIndices.Scales;
            RecipeState bread = recipes.AddRecipe("BREAD", new LocalizedText("Bread"));
            RecipeElementState flour = recipes.AddRecipeElement(bread, new NodeId(ObjectTypes.WeighingType, ns), "Flour", bread.NodeId);
            RecipeElementState water = recipes.AddRecipeElement(bread, new NodeId(ObjectTypes.WeighingType, ns), "Water", bread.NodeId);
            recipes.AddRecipeElement(bread, new NodeId(ObjectTypes.TimerType, ns), "Rest", flour.NodeId, water.NodeId);

            // Every element takes no time, so the handler completes it at once;
            // the events must be raised outside the controller's lock for that.
            var started = new List<string>();
            RecipeState? completed = null;
            recipes.ElementStarted += (_, e) =>
            {
                started.Add(e.Element.BrowseName.Name!);
                AssertGood(recipes.CompleteElement(e.Element));
            };
            recipes.RecipeCompleted += (_, recipe) => completed = recipe;

            AssertGood(Call(recipes.Scale.StartRecipe, scale.NodeId, bread.NodeId));

            Assert.That(completed, Is.SameAs(bread));
            Assert.That(recipes.State, Is.EqualTo(RecipeRunState.Idle));
            Assert.That(started, Is.EquivalentTo(s_breadElements));
            Assert.That(started[^1], Is.EqualTo("Rest"), "the join starts after both branches");
            var product = (RecipeProductState)scale.ProductionPreset!.ActiveProduct!;
            string[] report = [.. product.Report!.Value.ToArray()!.Select(entry => entry.ReportMessage.Text ?? string.Empty)];
            foreach (string element in s_breadElements)
            {
                Assert.That(report.Count(line => line == $"Element '{element}' started."), Is.EqualTo(1), element);
                Assert.That(report.Count(line => line == $"Element '{element}' completed."), Is.EqualTo(1), element);
            }
            Assert.That(report.Count(line => line == "Recipe 'BREAD' completed."), Is.EqualTo(1));
        }

        [Test]
        public async Task RecipesAreManagedAndProcessedAlongTheirGraphAsync()
        {
            ScaleHandle scale = await CreateAsync(
                ScaleKind.Recipe,
                b => b.WithProductionPreset(p => p.AllowSelection().AddProduct(
                    "Batch",
                    new LocalizedText("Batch"),
                    (ctx, product) => ((RecipeProductState)product).Report!.Value = default).Select("Batch")));
            RecipeController recipes = scale.Recipes!;
            RecipeManagementState management = recipes.Scale.Recipes!;
            ushort ns = m_fixture.Manager.NamespaceIndices.Scales;

            NodeId recipeId = NodeId.Null;
            AddRecipeMethodState addRecipe = management.AddRecipe!;
            AssertGood(addRecipe.OnCall!(Ctx, addRecipe, management.NodeId, "R1", new LocalizedText("Bread"), ref recipeId));
            NodeId dup = NodeId.Null;
            AssertStatus(addRecipe.OnCall(Ctx, addRecipe, management.NodeId, "R1", new LocalizedText("again"), ref dup), StatusCodes.BadNodeIdExists);
            AssertStatus(
                addRecipe.OnCall(Ctx, addRecipe, management.NodeId, string.Empty, new LocalizedText("x"), ref dup),
                StatusCodes.BadInvalidArgument);
            RecipeState recipe = recipes.Recipes["R1"];
            Assert.That(recipe.NodeId, Is.EqualTo(recipeId));

            // Flour and water run in parallel after the start; mixing joins them.
            AddRecipeElementMethodState addElement = recipe.AddRecipeElement!;
            NodeId weighing = new(ObjectTypes.WeighingType, ns);
            NodeId timer = new(ObjectTypes.TimerType, ns);
            NodeId flour = NodeId.Null;
            NodeId water = NodeId.Null;
            NodeId mix = NodeId.Null;
            AssertGood(addElement.OnCall!(Ctx, addElement, recipe.NodeId, weighing, "Flour", new[] { recipe.NodeId }.ToArrayOf(), ref flour));
            AssertGood(addElement.OnCall(Ctx, addElement, recipe.NodeId, weighing, "Water", new[] { recipe.NodeId }.ToArrayOf(), ref water));
            AssertGood(addElement.OnCall(Ctx, addElement, recipe.NodeId, timer, "Mix", new[] { flour, water }.ToArrayOf(), ref mix));
            NodeId bad = NodeId.Null;
            AssertStatus(
                addElement.OnCall(Ctx, addElement, recipe.NodeId, timer, "Mix", new[] { flour }.ToArrayOf(), ref bad),
                StatusCodes.BadBrowseNameDuplicated);
            AssertStatus(addElement.OnCall(Ctx, addElement, recipe.NodeId, timer, "X", default, ref bad), StatusCodes.BadInvalidArgument);
            AssertStatus(
                addElement.OnCall(Ctx, addElement, recipe.NodeId, timer, "X", new[] { new NodeId(999u, ns) }.ToArrayOf(), ref bad),
                StatusCodes.BadNodeIdUnknown);
            AssertStatus(
                addElement.OnCall(
                    Ctx,
                    addElement,
                    recipe.NodeId,
                    new NodeId(ObjectTypes.RecipeElementType, ns),
                    "X",
                    new[] { recipe.NodeId }.ToArrayOf(),
                    ref bad),
                StatusCodes.BadTypeMismatch);
            AssertStatus(
                addElement.OnCall(Ctx, addElement, recipe.NodeId, timer, string.Empty, new[] { recipe.NodeId }.ToArrayOf(), ref bad),
                StatusCodes.BadInvalidArgument);
            RecipeElementState extra = recipes.AddRecipeElement(recipe, new NodeId(ObjectTypes.UserInstructionType, ns), "Note", recipe.NodeId);
            RecipeElementState activation = recipes.AddRecipeElement(recipe, new NodeId(ObjectTypes.ActivationType, ns), "Heat", extra.NodeId);
            recipes.AddRecipeElement(recipe, new NodeId(ObjectTypes.AnalogConditionSleepType, ns), "Wait", activation.NodeId);
            recipes.AddRecipeElement(recipe, new NodeId(ObjectTypes.EdgeTriggeredSleepType, ns), "Edge", activation.NodeId);
            RemoveRecipeElementMethodStateResult removedExtra = await recipe.RemoveRecipeElement!.OnCallAsync!(
                Ctx,
                recipe.RemoveRecipeElement,
                recipe.NodeId,
                extra.NodeId,
                default);
            AssertGood(removedExtra.ServiceResult);
            RemoveRecipeElementMethodStateResult unknownElement = await recipe.RemoveRecipeElement.OnCallAsync(
                Ctx,
                recipe.RemoveRecipeElement,
                recipe.NodeId,
                new NodeId(777u, ns),
                default);
            AssertStatus(unknownElement.ServiceResult, StatusCodes.BadNodeIdUnknown);

            var started = new List<string>();
            RecipeState? completed = null;
            recipes.ElementStarted += (_, e) => started.Add(e.Element.BrowseName.Name!);
            recipes.RecipeCompleted += (_, r) => completed = r;

            AssertGood(Call(recipes.Scale.StartRecipe, scale.NodeId, recipeId));
            Assert.That(recipes.State, Is.EqualTo(RecipeRunState.Running));
            string[] allElements = ["Flour", "Water", "Heat"];
            string[] firstElements = ["Flour", "Water"];
            Assert.That(started, Is.EquivalentTo(allElements).Or.EquivalentTo(firstElements));
            AssertStatus(Call(recipes.Scale.StartRecipe, scale.NodeId, recipeId), StatusCodes.BadInvalidState);
            AssertStatus(Call(recipes.Scale.ContinueRecipe, scale.NodeId, recipeId), StatusCodes.BadInvalidState);
            AssertGood(Call(recipes.Scale.StopRecipe, scale.NodeId, recipeId));
            Assert.That(recipes.State, Is.EqualTo(RecipeRunState.Paused));
            AssertStatus(Call(recipes.Scale.SkipCurrentRecipeElement, scale.NodeId, recipeId), StatusCodes.BadInvalidState);
            AssertGood(Call(recipes.Scale.ContinueRecipe, scale.NodeId, recipeId));

            RecipeElementState flourElement = recipes.CurrentElements.ToArray()!
                .First(e => e.BrowseName.Name == "Flour");
            AssertGood(recipes.CompleteElement(flourElement));
            Assert.That(started, Does.Not.Contain("Mix"), "the join waits for water");
            Assert.That(recipes.CompleteElement(flourElement).StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            while (recipes.State == RecipeRunState.Running)
            {
                AssertGood(Call(recipes.Scale.SkipCurrentRecipeElement, scale.NodeId, recipeId));
            }
            Assert.That(started, Does.Contain("Mix"));
            Assert.That(completed, Is.SameAs(recipe));
            var product = (RecipeProductState)scale.ProductionPreset!.ActiveProduct!;
            Assert.That(product.Report!.Value.Count, Is.GreaterThan(3));

            AssertGood(Call(recipes.Scale.StartRecipe, scale.NodeId, recipeId));
            RemoveRecipeMethodStateResult busy = await management.RemoveRecipe!.OnCallAsync!(
                Ctx,
                management.RemoveRecipe,
                management.NodeId,
                "R1",
                default);
            AssertStatus(busy.ServiceResult, StatusCodes.BadInvalidState);
            Assert.That(
                () => recipes.AddRecipeElement(recipe, timer, "Late", recipe.NodeId),
                Throws.InstanceOf<ServiceResultException>(),
                "a recipe in processing cannot be edited");
        }

        [Test]
        public async Task RecipeProcessingGuardsAsync()
        {
            ScaleHandle scale = await CreateAsync(ScaleKind.Recipe);
            RecipeController recipes = scale.Recipes!;
            ushort ns = m_fixture.Manager.NamespaceIndices.Scales;
            RecipeState empty = recipes.AddRecipe("Empty", new LocalizedText("Empty"));
            Assert.That(() => recipes.AddRecipe("Empty", new LocalizedText("dup")), Throws.InstanceOf<ServiceResultException>());

            AssertStatus(recipes.Start(new NodeId(4242u, ns)), StatusCodes.BadNodeIdUnknown);
            AssertStatus(recipes.Start(empty.NodeId), StatusCodes.BadInvalidState);
            AssertStatus(recipes.Stop(empty.NodeId), StatusCodes.BadInvalidState);
            AssertStatus(recipes.Abort(empty.NodeId), StatusCodes.BadInvalidState);

            RecipeState recipe = recipes.AddRecipe("R", new LocalizedText("R"));
            recipes.AddRecipeElement(recipe, new NodeId(ObjectTypes.TimerType, ns), "T", recipe.NodeId);
            AssertGood(scale.PackML!.Execute(PackML.PackMLCommand.Abort));
            AssertStatus(recipes.Start(recipe.NodeId), StatusCodes.BadInvalidState);
            AssertGood(scale.PackML.Execute(PackML.PackMLCommand.Clear));
            AssertGood(scale.PackML.Execute(PackML.PackMLCommand.Reset));
            AssertGood(recipes.Start(recipe.NodeId));
            Assert.That(recipes.ActiveRecipe, Is.SameAs(recipe));
            Assert.That(
                () => recipes.AddRecipeElement(recipe, new NodeId(ObjectTypes.TimerType, ns), "T2", recipe.NodeId),
                Throws.InstanceOf<ServiceResultException>());
            AssertGood(recipes.Abort(recipe.NodeId));
            Assert.That(recipes.State, Is.EqualTo(RecipeRunState.Idle));

            recipes.MarkUploadedFromFile(recipe);
            recipe.AddRecipeFile(Ctx);
            Assert.That(
                () => recipes.AddRecipeElement(recipe, new NodeId(ObjectTypes.TimerType, ns), "T3", recipe.NodeId),
                Throws.InstanceOf<ServiceResultException>());
            RemoveRecipeMethodStateResult missing = await recipes.Scale.Recipes!.RemoveRecipe!.OnCallAsync!(
                Ctx,
                recipes.Scale.Recipes.RemoveRecipe,
                recipes.Scale.Recipes.NodeId,
                "missing",
                default);
            AssertStatus(missing.ServiceResult, StatusCodes.BadNotFound);
            RemoveRecipeMethodStateResult removed = await recipes.Scale.Recipes.RemoveRecipe.OnCallAsync(
                Ctx,
                recipes.Scale.Recipes.RemoveRecipe,
                recipes.Scale.Recipes.NodeId,
                "Empty",
                default);
            AssertGood(removed.ServiceResult);
            Assert.That(recipes.Recipes.ContainsKey("Empty"), Is.False);
        }

        [Test]
        public async Task RecipeFilesAreUploadedParsedAndReportedAsync()
        {
            ScaleHandle scale = await CreateAsync(
                ScaleKind.Recipe,
                b => b
                    .WithRecipeFiles()
                    .WithProductionPreset(p => p.AllowSelection().AddProduct(
                        "Batch",
                        new LocalizedText("Batch"),
                        (ctx, product) => ((RecipeProductState)product).AddReportFile(ctx)).Select("Batch")));
            RecipeController recipes = scale.Recipes!;
            RecipeState recipe = recipes.AddRecipe("R1", new LocalizedText("Bread"));
            FileState file = recipe.RecipeFile!;
            Assert.That(file.Writable!.Value, Is.True);

            uint Open(byte mode)
            {
                uint handle = 0;
                AssertGood(file.Open!.OnCall!(Ctx, file.Open, file.NodeId, mode, ref handle));
                return handle;
            }

            byte[] payload = [1, 2, 3, 4, 5];
            uint writeHandle = Open(6);
            AssertGood(file.Write!.OnCall!(Ctx, file.Write, file.NodeId, writeHandle, ByteString.From(payload)));
            AssertStatus(file.Close!.OnCall!(Ctx, file.Close, file.NodeId, writeHandle), StatusCodes.BadNotSupported);
            Assert.That(recipes.ReadRecipeFile(recipe), Has.Length.Zero, "a rejected upload keeps the old content");

            ByteString parsed = default;
            StatusCode openDuringUpload = StatusCodes.Good;
            recipes.RecipeFileHandler = (r, bytes) =>
            {
                parsed = bytes;
                // A handle opened while the upload is being applied would be
                // dropped by the replacement, so the binder refuses it.
                uint during = 0;
                openDuringUpload = file.Open!.OnCall!(Ctx, file.Open, file.NodeId, 1, ref during).StatusCode;
                return ServiceResult.Good;
            };
            writeHandle = Open(6);
            uint second = 0;
            AssertStatus(file.Open!.OnCall!(Ctx, file.Open, file.NodeId, 6, ref second), StatusCodes.BadInvalidState);
            AssertGood(file.Write.OnCall(Ctx, file.Write, file.NodeId, writeHandle, ByteString.From(payload)));
            AssertGood(file.Write.OnCall(Ctx, file.Write, file.NodeId, writeHandle, ByteString.Empty));
            ulong position = 0;
            AssertGood(file.GetPosition!.OnCall!(Ctx, file.GetPosition, file.NodeId, writeHandle, ref position));
            Assert.That(position, Is.EqualTo(5UL));
            ByteString ignored = ByteString.Empty;
            AssertStatus(file.Read!.OnCall!(Ctx, file.Read, file.NodeId, writeHandle, 10, ref ignored), StatusCodes.BadInvalidState);
            AssertGood(file.Close.OnCall(Ctx, file.Close, file.NodeId, writeHandle));
            Assert.That(parsed.ToArray(), Is.EqualTo(payload));
            Assert.That(openDuringUpload, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            Assert.That(recipes.ReadRecipeFile(recipe).ToArray(), Is.EqualTo(payload));
            Assert.That(file.Size!.Value, Is.EqualTo(5UL));
            Assert.That(
                (
                    ) => recipes.AddRecipeElement(recipe,
                    new NodeId(ObjectTypes.TimerType, m_fixture.Manager.NamespaceIndices.Scales),
                    "T",
                    recipe.NodeId),
                Throws.InstanceOf<ServiceResultException>(),
                "a recipe uploaded as a file is not edited element by element");

            uint readHandle = Open(1);
            AssertGood(file.SetPosition!.OnCall!(Ctx, file.SetPosition, file.NodeId, readHandle, 2));
            AssertStatus(file.SetPosition.OnCall(Ctx, file.SetPosition, file.NodeId, readHandle, 99), StatusCodes.BadInvalidArgument);
            ByteString data = ByteString.Empty;
            AssertGood(file.Read.OnCall(Ctx, file.Read, file.NodeId, readHandle, 10, ref data));
            Assert.That(data.Span.ToArray(), Is.EqualTo(new byte[] { 3, 4, 5 }));
            AssertGood(file.Read.OnCall(Ctx, file.Read, file.NodeId, readHandle, 10, ref data));
            Assert.That(data.Span.Length, Is.Zero);
            AssertStatus(file.Write.OnCall(Ctx, file.Write, file.NodeId, readHandle, ByteString.From(payload)), StatusCodes.BadInvalidState);
            AssertGood(file.Close.OnCall(Ctx, file.Close, file.NodeId, readHandle));
            AssertStatus(file.Close.OnCall(Ctx, file.Close, file.NodeId, readHandle), StatusCodes.BadInvalidArgument);
            uint bad = 0;
            AssertStatus(file.Open!.OnCall!(Ctx, file.Open, file.NodeId, 2, ref bad), StatusCodes.BadNotSupported);

            // The recipe report is served as the product's read-only ReportFile.
            RecipeState runnable = recipes.AddRecipe("R2", new LocalizedText("Rolls"));
            recipes.AddRecipeElement(runnable, new NodeId(ObjectTypes.TimerType, m_fixture.Manager.NamespaceIndices.Scales), "T", runnable.NodeId);
            AssertGood(recipes.Start(runnable.NodeId));
            var product = (RecipeProductState)scale.ProductionPreset!.ActiveProduct!;
            FileState report = product.ReportFile!;
            Assert.That(report.Writable!.Value, Is.False);
            uint reportHandle = 0;
            AssertStatus(report.Open!.OnCall!(Ctx, report.Open, report.NodeId, 6, ref reportHandle), StatusCodes.BadNotWritable);
            AssertGood(report.Open.OnCall(Ctx, report.Open, report.NodeId, 1, ref reportHandle));
            ByteString text = ByteString.Empty;
            AssertGood(report.Read!.OnCall!(Ctx, report.Read, report.NodeId, reportHandle, 4096, ref text));
            Assert.That(System.Text.Encoding.UTF8.GetString(text.Span.ToArray()), Does.Contain("Rolls").Or.Contain("started"));

            Assert.That(
                () => m_fixture.Manager.CreateScaleAsync(m_fixture.Name("NoRecipe"), ScaleKind.Simple, b => b.WithRecipeFiles()).AsTask(),
                Throws.InvalidOperationException);
        }

        [Test]
        public async Task ScaleSystemHostsScalesAndResetsGlobalStatisticsAsync()
        {
            ScaleSystemHandle system = await m_fixture.Manager.CreateScaleSystemAsync(
                m_fixture.Name("Line"),
                s => s
                    .WithIdentification(ScalesNodeManagerTests.Identity("SYS"))
                    .WithProcessState("Run", new LocalizedText("Running"))
                    .WithProductionOutput()
                    .WithPackMLState()
                    .WithMachineryBuildingBlocks()
                    .WithProductionPreset(p => p.AllowSelection().AllowManagement().AddProduct("P", new LocalizedText("P")).Select("P"))
                    .AddScale("Infeed", ScaleKind.Checkweigher, b => ScalesNodeManagerTests.FullScale(b, "IN"))
                    .AddScale("Filler", ScaleKind.AutomaticFilling, b => ScalesNodeManagerTests.FullScale(b, "FI")));
            Assert.That(system.Scales, Has.Count.EqualTo(2));
            Assert.That(m_fixture.Manager.ScaleSystems, Has.Count.EqualTo(1));
            Assert.That(m_fixture.Manager.Scales, Has.Count.EqualTo(2));
            Assert.That(system.PackML!.CurrentState, Is.EqualTo(PackML.PackMLStateNumbers.Idle));
            string[] currentProducts = ["P"];
            Assert.That(system.ProductionPreset!.CurrentProducts, Is.EqualTo(currentProducts));
            Assert.That(system.System.ProcessStateMessage!.Value.Text, Is.EqualTo("Running"));

            system.ProductionOutput!.Record(1);
            system.Scales[0].ProductionOutput!.Record(2);
            AssertGood(Call(system.System.ResetGlobalStatistics, system.NodeId));
            Assert.That(system.System.ProductionOutput!.TotalPackages!.ItemCount!.WrappedValue.TryGetValue(out ulong count), Is.True);
            Assert.That(count, Is.Zero);
            Assert.That(system.System.ProductionOutput.ResetCondition!.Value, Is.EqualTo("ResetGlobalStatistics"));

            system.CommandInterceptor = _ => ServiceResult.Create(StatusCodes.BadUserAccessDenied, "no");
            AssertStatus(Call(system.System.ResetGlobalStatistics, system.NodeId), StatusCodes.BadUserAccessDenied);
            system.SetProcessState("Stop", new LocalizedText("Stopped"));
            Assert.That(system.System.ProcessStateId!.Value, Is.EqualTo("Stop"));
            system.Notifications.RaiseEvent(ScaleNotificationId.GeneralSystemFault, new LocalizedText("x"));

            string[] profiles = [.. m_fixture.Manager.ServerProfiles.ToList()];
            Assert.That(profiles, Does.Contain(ScalesProfiles.ScaleSystem));
        }

        [Test]
        public async Task ScaleSystemWithoutIdentificationIsRejectedAsync()
        {
            Assert.ThrowsAsync<ServiceResultException>(async () =>
                await m_fixture.Manager.CreateScaleSystemAsync(m_fixture.Name("NoId"), s => { }));
            ScaleSystemHandle minimal = await m_fixture.Manager.CreateScaleSystemAsync(
                m_fixture.Name("Minimal"),
                s => s.WithIdentification(ScalesNodeManagerTests.Identity("MIN")));
            Assert.That(minimal.System.ProcessStateMessage!.Value.Text, Is.Empty);
            Assert.That(minimal.PackML, Is.Null);
        }
    }

    internal static class ScalesTestUnits
    {
        public static EUInformation MetrePerSecond { get; } = ScaleUnits.Create("MTS", "m/s", "metre per second");

        public static EUInformation Millimetre { get; } = ScaleUnits.Create("MMT", "mm", "millimetre");
    }
}
