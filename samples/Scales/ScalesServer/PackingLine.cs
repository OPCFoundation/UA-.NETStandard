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
using System.Text;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Scales;
using Opc.Ua.Scales.Server;
using Opc.Ua.Scales.Server.Builders;
using Opc.Ua.Scales.Server.Hosting;
using Opc.Ua.Scales.Server.Runtime;

namespace ScalesSample
{
    /// <summary>
    /// Builds the sample's weighing equipment: a packing line (a scale system
    /// with a checkweigher, its infeed feeder and label printer) and four
    /// stand-alone scales showing the other scale types.
    /// </summary>
    internal sealed class PackingLine
    {
        public ScaleSystemHandle Line { get; private set; } = null!;

        public ScaleHandle Checkweigher => Line.Scales[0];

        public ScaleHandle Laboratory { get; private set; } = null!;

        public ScaleHandle PieceCounter { get; private set; } = null!;

        public ScaleHandle Weighbridge { get; private set; } = null!;

        public ScaleHandle Batching { get; private set; } = null!;

        /// <summary>
        /// Completes once every scale exists, which the simulation waits for.
        /// </summary>
        public Task Ready => m_ready.Task;

        public async ValueTask ConfigureAsync(IScalesSetupContext context)
        {
            ScalesNodeManager manager = context.Manager;
            QualifiedName Name(string name) => new(name, manager.InstanceNamespaceIndex);

            Line = await manager.CreateScaleSystemAsync(
                Name("PackingLine1"),
                system => system
                    .WithIdentification(Identity("Contoso Weighing", "PL-0001", "PackingLine"))
                    .WithProcessState("Ready", new LocalizedText("Ready for production"))
                    .WithProductionOutput()
                    .WithPackMLState()
                    .WithMachineryBuildingBlocks()
                    .AddScale("Checkweigher1", ScaleKind.Checkweigher, scale => scale
                        .WithIdentification(Identity("Contoso Weighing", "CW-0100", "CW-600"))
                        .WithWeighingRange(new WeighingRangeDefinition(0, 0.6, 0.0001, 0.0002))
                        .WithUnit(ScaleUnits.Kilogram)
                        .WithWeightDetails()
                        .WithZeroAndTare()
                        .WithRegisterWeight()
                        .WithPackMLState()
                        .WithTypeFeatures()
                        .WithProductionPreset(preset => preset
                            .AllowSelection()
                            .AllowManagement()
                            .WithLocking()
                            .AddProduct("CEREAL-500", new LocalizedText("Cereal 500 g"), NominalWeight(0.5))
                            .AddProduct("CEREAL-375", new LocalizedText("Cereal 375 g"), NominalWeight(0.375))
                            .Select("CEREAL-500"))
                        .WithProductionOutput()
                        .AddFeederModule("Infeed", feeder => feeder
                            .WithIdentification(Identity("Contoso Conveyors", "CV-7", "Belt"))
                            .WithMachineryBuildingBlocks()
                            .WithFeederSpeed(0.2, 1.5, MetrePerSecond, 0.8))
                        .AddPrinterModule("Labeler", printer => printer
                            .WithIdentification(Identity("Contoso Print", "LP-3", "Thermal"))
                            .WithLabel("L-100x60", 100, 60, Millimetre))),
                context.CancellationToken).ConfigureAwait(false);

            Laboratory = await manager.CreateScaleAsync(
                Name("AnalyticalBalance"),
                ScaleKind.Laboratory,
                scale => scale
                    .WithIdentification(Identity("Contoso Lab", "AB-220", "XP-220"))
                    .WithWeighingRange(new WeighingRangeDefinition(0, 0.22, 0.0000001, 0.000001))
                    .WithUnit(ScaleUnits.Kilogram)
                    .WithAllowedEngineeringUnits(ScaleUnits.Kilogram, ScaleUnits.Gram, ScaleUnits.Milligram)
                    .WithLegalForTrade()
                    .WithWeightDetails()
                    .WithZeroAndTare()
                    .WithRegisterWeight()
                    .WithTypeFeatures(),
                context.CancellationToken).ConfigureAwait(false);

            PieceCounter = await manager.CreateScaleAsync(
                Name("CountingScale"),
                ScaleKind.PieceCounting,
                scale => scale
                    .WithIdentification(Identity("Contoso Weighing", "PC-15", "Count-15"))
                    .WithWeighingRange(new WeighingRangeDefinition(0, 15, 0.0005, 0.005))
                    .WithAllowedEngineeringUnits(ScaleUnits.Kilogram, ScaleUnits.Gram)
                    .WithWeightDetails()
                    .WithZeroAndTare()
                    .WithTypeFeatures()
                    .WithProductionPreset(preset => preset
                        .AllowSelection()
                        .AddProduct("M4-SCREW", new LocalizedText("M4 x 10 screw"))
                        .Select("M4-SCREW")),
                context.CancellationToken).ConfigureAwait(false);

            Weighbridge = await manager.CreateScaleAsync(
                Name("Weighbridge"),
                ScaleKind.Vehicle,
                scale => scale
                    .WithIdentification(Identity("Contoso Weighing", "WB-60", "Bridge-60t"))
                    .WithWeighingRange(new WeighingRangeDefinition(0, 60000, 20, 20))
                    .WithWeightDetails()
                    .WithRegisterWeight()
                    .WithTypeFeatures()
                    .WithProductionPreset(preset => preset
                        .AddProduct("TRUCK-17", new LocalizedText("Truck 17"), (ctx, product) =>
                        {
                            var vehicle = (VehicleProductState)product;
                            vehicle.AddTare(ctx);
                            vehicle.Tare!.WrappedValue = Variant.From(14200.0);
                            vehicle.AddGetVehicleInformation(ctx);
                        })),
                context.CancellationToken).ConfigureAwait(false);
            Weighbridge.ProductionPreset!.VehicleInformationProvider = vehicleId => vehicleId == "TRUCK-17"
                ? new VehicleInformation
                {
                    Tare = 14200,
                    CarrierId = "CARRIER-4",
                    DriverDisplayName = new LocalizedText("J. Doe"),
                    Destination = new LocalizedText("Depot North")
                }
                : null;

            Batching = await manager.CreateScaleAsync(
                Name("BatchingScale"),
                ScaleKind.Recipe,
                scale => scale
                    .WithIdentification(Identity("Contoso Weighing", "RB-300", "Batch-300"))
                    .WithWeighingRange(new WeighingRangeDefinition(0, 300, 0.05, 0.1))
                    .WithWeightDetails()
                    .WithZeroAndTare()
                    .WithPackMLState()
                    .WithTypeFeatures()
                    .WithRecipeFiles()
                    .WithProductionPreset(preset => preset
                        .AllowSelection()
                        .AddProduct("DOUGH", new LocalizedText("Bread dough"), (ctx, product) =>
                            ((RecipeProductState)product).AddReportFile(ctx))
                        .Select("DOUGH")),
                context.CancellationToken).ConfigureAwait(false);

            // A two-branch recipe: flour and water are weighed in parallel,
            // then the dough rests.
            RecipeController recipes = Batching.Recipes!;
            RecipeState bread = recipes.AddRecipe("BREAD", new LocalizedText("Bread"));
            ushort ns = manager.NamespaceIndices.Scales;
            RecipeElementState flour = recipes.AddRecipeElement(bread, new NodeId(Opc.Ua.Scales.ObjectTypes.WeighingType, ns), "Flour", bread.NodeId);
            RecipeElementState water = recipes.AddRecipeElement(bread, new NodeId(Opc.Ua.Scales.ObjectTypes.WeighingType, ns), "Water", bread.NodeId);
            recipes.AddRecipeElement(bread, new NodeId(Opc.Ua.Scales.ObjectTypes.TimerType, ns), "Rest", flour.NodeId, water.NodeId);
            // The vendor recipe format of this sample is one element name per line.
            recipes.RecipeFileHandler = (recipe, bytes) =>
                Encoding.UTF8.GetString(bytes.ToArray()).Trim().Length > 0
                    ? ServiceResult.Good
                    : ServiceResult.Create(StatusCodes.BadDecodingError, "The recipe file is empty.");
            m_ready.TrySetResult(true);
        }

        private readonly TaskCompletionSource<bool> m_ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static ScaleIdentification Identity(string manufacturer, string serial, string model)
        {
            return new ScaleIdentification
            {
                Manufacturer = new LocalizedText(manufacturer),
                SerialNumber = serial,
                ProductInstanceUri = "urn:contoso:weighing:" + serial,
                Model = new LocalizedText(model),
                YearOfConstruction = 2026,
                Location = "Hall 3"
            };
        }

        private static Action<Opc.Ua.ISystemContext, ProductState> NominalWeight(double kilograms)
        {
            return (context, product) =>
            {
                TargetItemState nominal = ((CheckweigherProductState)product).NominalWeight!;
                nominal.WrappedValue = Variant.From(kilograms);
                if (nominal.EngineeringUnits != null)
                {
                    nominal.EngineeringUnits.Value = ScaleUnits.Kilogram;
                }
            };
        }

        internal static EUInformation MetrePerSecond { get; } = ScaleUnits.Create("MTS", "m/s", "metre per second");

        internal static EUInformation Millimetre { get; } = ScaleUnits.Create("MMT", "mm", "millimetre");
    }
}
