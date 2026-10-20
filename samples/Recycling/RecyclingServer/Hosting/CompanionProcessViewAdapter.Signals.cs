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
using System.Globalization;
using System.Linq;
using Opc.Ua.Recycling.Model;
using Opc.Ua.Scales.Server.Runtime;

namespace Opc.Ua.Recycling
{
    public sealed partial class CompanionProcessViewAdapter
    {
        private void ProjectStage(
            RecyclingProcessAssetState node,
            StageSnapshot stage,
            CommittedSnapshot snapshot,
            SnapshotDataType wire)
        {
            Set(node.Stage, Variant.FromStructure(wire.Stages[stage.Index]));
            Set(node.Available, Variant.From(true));
            Set(node.RunState, Variant.From((EquipmentEnum)stage.State));
            Set(node.Timestamp, Variant.From((DateTimeUtc)snapshot.SourceTimestampUtc));
            Set(node.Revision, Variant.From(snapshot.Revision));
            Set(node.Tick, Variant.From(snapshot.Tick));
            Set(node.RunId, Variant.From(snapshot.RunId));
            Set(node.Executions, Variant.From(stage.Executions));
            double throughput = Rate(stage.Transferred.Total / 1_000_000.0, snapshot);
            Number(node.ThroughputKgHr, throughput, snapshot.LogicalSeconds > 0);

            MaterialPackage? package = stage.Process == RecyclingProcess.Bagging
                ? snapshot.Packages.Span.ToArray().LastOrDefault()
                : null;
            ArrayOf<ParcelDataType> material = package == null
                ? wire.Parcels.Span.ToArray().Where(parcel => parcel.Stage == stage.Index).ToArrayOf()
                : [wire.Packages[^1].Material];
            Set(node.Material, Variant.FromStructure(material));
            ArrayOf<MaterialParcel> domainMaterial = package == null
                ? snapshot.Parcels.Span.ToArray().Where(parcel => parcel.Stage == stage.Index).ToArrayOf()
                : [package.Material];
            ArrayOf<SourceContribution> contributions = MaterialLedger.Merge(domainMaterial.Span.ToArray()
                .SelectMany(static parcel => parcel.Contributions.Span.ToArray()));
            Set(node.Contributions, Variant.FromStructure(contributions.Span.ToArray()
                .Select(source => new ContributionDataType
                {
                    SourceId = source.SourceId,
                    SourceZone = source.SourceZone,
                    Mass = new MassDataType
                    {
                        NaturalPolymer = source.Mass.NaturalPolymer,
                        ColoredPolymer = source.Mass.ColoredPolymer,
                        DensePolymer = source.Mass.DensePolymer,
                        Labels = source.Mass.Labels,
                        Dirt = source.Mass.Dirt,
                        Water = source.Mass.Water
                    }
                }).ToArrayOf()));
            Set(node.SourceIds, Variant.From(contributions.Span.ToArray()
                .Select(static source => source.SourceId).ToArrayOf()));
            Set(node.LotId, material.IsEmpty ? Variant.Null :
                Variant.From(snapshot.RunId + ":parcel-" + material[^1].Id.ToString(CultureInfo.InvariantCulture)),
                material.IsEmpty ? StatusCodes.BadNoData : StatusCodes.Good);
            string[] zones = [.. contributions.Span.ToArray()
                .Where(static source => source.Mass.Dry > 0)
                .Select(static source => source.SourceZone)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)];
            bool binMaterial = zones.Length > 0;
            Set(node.SourceBinId, binMaterial ? Variant.From(AssetUri("collection-bins")) : Variant.Null,
                binMaterial ? StatusCodes.Good : StatusCodes.BadNoData);
            Set(node.SourceZone, binMaterial ? Variant.From(zones.Length == 1 ? zones[0] : "mixed") : Variant.Null,
                binMaterial ? StatusCodes.Good : StatusCodes.BadNoData);

            MaterialMass processed = stage.Received - stage.Inventory;
            MaterialMass output = stage.Transferred;
            bool outputExists = output.Total > 0;
            bool dryOutputExists = output.Dry > 0;
            bool running = stage.State == EquipmentState.Executing;
            bool powered = IsPowered(stage);
            double motorLoad = powered ? m_defaults.RunningMotorLoadPct : 0;
            double contamination = Fraction(
                output.ColoredPolymer + output.DensePolymer + output.Labels + output.Dirt, output.Dry);
            switch (node)
            {
                case FeedConveyorState feed:
                    Number(feed.BeltSpeedMps, running ? m_defaults.BeltSpeedMps : 0);
                    Number(feed.ThroughputTph, throughput / 1000, snapshot.LogicalSeconds > 0);
                    Set(feed.JamAlarm, Variant.From(stage.JamAlarm));
                    Set(feed.JamEvents, Variant.From(checked((ulong)stage.JamEvents)));
                    break;
                case OpticalSortingStationState sorting:
                    double unwanted = processed.ColoredPolymer + processed.DensePolymer;
                    double removed = stage.Rejected.ColoredPolymer + stage.Rejected.DensePolymer;
                    Number(sorting.ClassificationAccuracyPct, 100 * Fraction(removed, unwanted), unwanted > 0);
                    Number(sorting.ContaminationPpm, contamination * 1_000_000, dryOutputExists);
                    Number(sorting.DriftScore,
                        Fraction(output.ColoredPolymer, processed.ColoredPolymer), processed.ColoredPolymer > 0);
                    Set(sorting.CameraHealth, Variant.From(CameraHealth(stage)));
                    break;
                case PrimaryReductionState primary:
                    Number(primary.MotorLoadPct, motorLoad);
                    Number(primary.FeedRateKgMin, Rate(processed.Total / 1_000_000.0, snapshot) / 60,
                        snapshot.LogicalSeconds > 0);
                    Set(primary.JamAlarm, Variant.From(stage.JamAlarm));
                    Set(primary.JamEvents, Variant.From(checked((ulong)stage.JamEvents)));
                    break;
                case SecondaryReductionState secondary:
                    Number(secondary.FlakeSizeAvgMm, m_defaults.FlakeSizeMm, dryOutputExists);
                    Set(secondary.JamAlarm, Variant.From(stage.JamAlarm));
                    Set(secondary.JamEvents, Variant.From(checked((ulong)stage.JamEvents)));
                    break;
                case PreWashState prewash:
                    Set(prewash.PumpStatus, Variant.From(powered));
                    Number(prewash.TurbidityNtu,
                        m_defaults.TurbidityMultiplier * Fraction(stage.Rejected.Dirt, stage.Additions.Water),
                        stage.Additions.Water > 0);
                    Number(prewash.WaterTempC, m_defaults.AmbientTemperatureC);
                    break;
                case FrictionWashState friction:
                    Set(friction.FaultState, Variant.From((EquipmentEnum)stage.State));
                    Number(friction.WashEffectivenessPct,
                        100 * Fraction(stage.Rejected.Dirt, processed.Dirt), processed.Dirt > 0);
                    Number(friction.MotorLoadPct, motorLoad);
                    break;
                case HotWashState hotwash:
                    Set(hotwash.FaultState, Variant.From((EquipmentEnum)stage.State));
                    Number(hotwash.BathTempC, ThermalTemperature(stage, m_defaults.BathTemperatureC));
                    CommittedNumber(hotwash.Ph, stage.Ph, stage.PhAvailable, stage.PhSourceTimestampUtc);
                    Number(hotwash.AdhesiveRemovalPct,
                        100 * Fraction(stage.Rejected.Labels, processed.Labels), processed.Labels > 0);
                    break;
                case DensitySeparationState density:
                    Set(density.CirculationPumpStatus, Variant.From(powered));
                    Number(density.SeparationPurityPct,
                        100 * Fraction(output.NaturalPolymer + output.ColoredPolymer, output.Dry), dryOutputExists);
                    Number(density.HdpeRecoveryPct,
                        100 * Fraction(output.NaturalPolymer + output.ColoredPolymer,
                            processed.NaturalPolymer + processed.ColoredPolymer),
                        processed.NaturalPolymer + processed.ColoredPolymer > 0);
                    break;
                case MechanicalDewateringState mechanical:
                    Number(mechanical.OutletMoisturePct, 100 * Fraction(output.Water, output.Total), outputExists);
                    break;
                case SampleExtrusionState extrusion:
                    Number(extrusion.MeltTempC, ThermalTemperature(stage, m_defaults.MeltTemperatureC));
                    Number(extrusion.MeltPressureBar, running ? m_defaults.MeltPressureBar : 0);
                    Number(extrusion.ExtrusionStabilityScore,
                        powered ? 100 * Math.Max(0, 1 - contamination - Fraction(output.Water, output.Total)) : 0,
                        dryOutputExists);
                    break;
                case SampleMeltFiltrationState filtration:
                    double loading = Math.Min(1,
                        (stage.Rejected.Dirt + stage.Rejected.Labels) / (m_defaults.FilterCapacityKg * 1_000_000));
                    Set(filtration.AlarmState, Variant.From((EquipmentEnum)stage.State));
                    Number(filtration.CloggingPct, 100 * loading);
                    Number(filtration.DiffPressureBar,
                        running
                            ? m_defaults.CleanFilterPressureBar + (m_defaults.LoadedFilterPressureBar * loading) : 0);
                    Number(filtration.MeltCleanlinessIndex,
                        100 * (1 - Fraction(output.Dirt + output.Labels, output.Dry)), dryOutputExists);
                    break;
                case SamplePelletizingState pelletizing:
                    Set(pelletizing.FaultState, Variant.From((EquipmentEnum)stage.State));
                    Number(pelletizing.PelletSizeAvgMm, m_defaults.PelletSizeMm, dryOutputExists);
                    Set(pelletizing.StrandBreakAlarm, Variant.From(stage.StrandBreakAlarm));
                    Set(pelletizing.StrandBreakEvents, Variant.From(checked((ulong)stage.StrandBreakEvents)));
                    break;
                case BaggingStationState bagging:
                    ProjectPackage(bagging, stage, package, snapshot);
                    break;
            }
        }

        private void ProjectPackage(
            BaggingStationState node,
            StageSnapshot stage,
            MaterialPackage? package,
            CommittedSnapshot snapshot)
        {
            bool present = package != null;
            MaterialMass mass = package?.Material.Mass ?? default;
            Number(node.BagWeightKg, mass.Total / 1_000_000.0, present);
            Number(node.BagsPerHr, Rate(snapshot.Packages.Count, snapshot), snapshot.LogicalSeconds > 0);
            Set(node.PackageCount, Variant.From((long)snapshot.Packages.Count));
            bool colorAvailable = stage.ColorAvailable && package != null && stage.ColorMaterialId == package.Id;
            CommittedNumber(node.ColourR, stage.Color.Red, colorAvailable, stage.ColorSourceTimestampUtc);
            CommittedNumber(node.ColourG, stage.Color.Green, colorAvailable, stage.ColorSourceTimestampUtc);
            CommittedNumber(node.ColourB, stage.Color.Blue, colorAvailable, stage.ColorSourceTimestampUtc);
            Set(node.QualityClassification, present ? Variant.From(PackageDisposition(package!, snapshot)) :
                Variant.Null, present ? StatusCodes.Good : StatusCodes.BadNoData);
        }

        private static DispositionEnum PackageDisposition(MaterialPackage package, CommittedSnapshot snapshot)
        {
            HoldDisposition[] dispositions = snapshot.Holds.Span.ToArray()
                .Where(hold => package.Material.HoldIds.Contains(hold.Id))
                .Select(static hold => hold.Disposition).ToArray();
            return dispositions.Contains(HoldDisposition.Rejected)
                ? DispositionEnum.Rejected
                : dispositions.Contains(HoldDisposition.Held) ? DispositionEnum.Held : DispositionEnum.Released;
        }

        private static Di.DeviceHealthEnumeration CameraHealth(StageSnapshot stage)
        {
            return stage.State is EquipmentState.Unavailable or EquipmentState.ElectricalConstraint
                ? Di.DeviceHealthEnumeration.FAILURE
                : Di.DeviceHealthEnumeration.NORMAL;
        }

        private double ThermalTemperature(StageSnapshot stage, double heated)
        {
            return stage.ThermalDemandWatts > 0 && stage.State is not (
                EquipmentState.ThermalConstraint or EquipmentState.ElectricalConstraint or
                EquipmentState.Unavailable or EquipmentState.Stopped)
                ? heated
                : m_defaults.AmbientTemperatureC;
        }

        private static bool IsPowered(StageSnapshot stage)
        {
            return stage.ElectricalDemandWatts > 0 && stage.State is not (
                EquipmentState.ElectricalConstraint or EquipmentState.ThermalConstraint or
                EquipmentState.Unavailable or EquipmentState.Stopped);
        }

        private static double Rate(double amount, CommittedSnapshot snapshot)
        {
            return snapshot.LogicalSeconds > 0 ? amount * 3600 / snapshot.LogicalSeconds : 0;
        }

        private static double Fraction(double part, double total)
        {
            return total > 0 ? part / total : 0;
        }

        private static void Number(BaseVariableState? variable, double value, bool applicable = true)
        {
            Set(variable, applicable ? Variant.From(value) : Variant.Null,
                applicable ? StatusCodes.Good : StatusCodes.BadNoData);
        }

        private void CommittedNumber(
            BaseVariableState? variable, double value, bool applicable, DateTime sourceTimestampUtc)
        {
            Number(variable, value, applicable);
            m_signalTimestamps[variable!.NodeId] = sourceTimestampUtc;
        }

        private static void Set(BaseVariableState? variable, Variant value, StatusCode status = default)
        {
            if (variable == null)
            {
                throw new InvalidOperationException("The required declared projection member was not created.");
            }
            variable.WrappedValue = value;
            variable.StatusCode = status;
        }

        private void ConfigureMeasurements(RecyclingProcessAssetState state)
        {
            ISystemContext context = RequireContext().Context;
            ushort sample = context.NamespaceUris.GetIndexOrAppend(RecyclingModelMapping.NamespaceUri);
            var type = new NodeId(Model.VariableTypes.SyntheticMeasurementType, sample);
            foreach (BaseVariableState variable in Descendants(state).OfType<BaseVariableState>())
            {
                if (variable.TypeDefinitionId != type)
                {
                    continue;
                }
                (string semantic, string code, string symbol, string formula, double high) =
                    MeasurementDescription(variable.BrowseName.Name!);
                EUInformation unit = code.Length == 0
                    ? new EUInformation
                    {
                        NamespaceUri = RecyclingModelMapping.NamespaceUri,
                        UnitId = symbol switch
                        {
                            "t/h" => 1,
                            "kg/min" => 2,
                            "NTU" => 3,
                            "ratio" => 4,
                            "score (0..100)" => 5,
                            "pH" => 6,
                            "bags/h" => 7,
                            "RGB channel" => 8,
                            _ => throw new InvalidOperationException("An explicit sample unit identity is required.")
                        },
                        DisplayName = new LocalizedText(symbol),
                        Description = new LocalizedText(symbol + " (sample engineering unit)")
                    }
                    : ScaleUnits.Create(code, symbol, symbol);
                Set(Child(variable, "EngineeringUnits", 0), Variant.FromStructure(unit));
                Set(Child(variable, "EURange", 0), Variant.FromStructure(new Range(code == "CEL" ? -50 : 0, high)));
                Set(Child(variable, "SemanticField", sample), Variant.From(semantic));
                Set(Child(variable, "Formula", sample), Variant.From(formula));
                variable.Description = new LocalizedText(
                    semantic + ": " + formula + " Synthetic sample; not original telemetry parity.");
            }
        }

        private BaseVariableState Child(NodeState node, string name, ushort ns)
        {
            return node.FindChild(RequireContext().Context, new QualifiedName(name, ns)) as BaseVariableState ??
                throw new InvalidOperationException("Missing declared member: " + name);
        }

        private static (string Semantic, string Code, string Symbol, string Formula, double High)
            MeasurementDescription(string name)
        {
            return name switch
            {
                "ThroughputKgHr" => ("throughput_kg_hr", "KGH", "kg/h",
                    "Run-to-date transferred wet kg * 3600 / committed logical seconds.", 10_000_000),
                "ThroughputTph" => ("throughput_tph", string.Empty, "t/h",
                    "Run-to-date transferred wet tonnes * 3600 / committed logical seconds.", 10_000),
                "BeltSpeedMps" => ("belt_speed_mps", "MTS", "m/s",
                    "Configured nominal belt speed only when a transfer is committed.", 100),
                "ClassificationAccuracyPct" => ("classification_accuracy_pct", "P1", "%",
                    "Proxy: cumulative rejected colored+dense polymer / processed colored+dense polymer * 100.", 100),
                "ContaminationPpm" => ("contamination_ppm", "59", "ppm",
                    "Cumulative non-natural-polymer dry output / total dry output * 1000000.", 1_000_000),
                "DriftScore" => ("drift_score", string.Empty, "ratio",
                    "Proxy: cumulative outgoing colored polymer / processed colored polymer.", 1),
                "MotorLoadPct" => ("motor_load_pct", "P1", "%",
                    "Configured synthetic running load when committed electrical demand is met.", 100),
                "FeedRateKgMin" => ("feed_rate_kg_min", string.Empty, "kg/min",
                    "Run-to-date processed wet kg * 60 / committed logical seconds.", 1_000_000),
                "FlakeSizeAvgMm" => ("flake_size_avg_mm", "MMT", "mm",
                    "Configured nominal size after first output; no size-distribution simulation.", 1000),
                "TurbidityNtu" => ("turbidity_ntu", string.Empty, "NTU",
                    "Configured multiplier * cumulative removed dirt / added wash water; uncalibrated proxy.",
                    1_000_000_000),
                "WaterTempC" => ("water_temp_c", "CEL", "degC", "Configured synthetic ambient temperature.", 400),
                "WashEffectivenessPct" => ("wash_effectiveness_pct", "P1", "%",
                    "Cumulative removed dirt / processed dirt * 100.", 100),
                "BathTempC" => ("bath_temp_c", "CEL", "degC",
                    "Configured heated/ambient temperature selected by committed thermal demand and constraints.", 100),
                "Ph" => ("pH", string.Empty, "pH",
                    "Committed configured synthetic hotwash pH; not an acid/base model. Uses its own source time.", 14),
                "AdhesiveRemovalPct" => ("adhesive_removal_pct", "P1", "%",
                    "Label-removal proxy: cumulative removed labels / processed labels * 100; not chemistry.", 100),
                "SeparationPurityPct" => ("separation_purity_pct", "P1", "%",
                    "Cumulative natural+colored target polymer / dry output * 100.", 100),
                "HdpeRecoveryPct" => ("hdpe_recovery_pct", "P1", "%",
                    "Target-polymer proxy: cumulative outgoing / processed natural+colored polymer * 100.", 100),
                "OutletMoisturePct" => ("outlet_moisture_pct", "P1", "%",
                    "Cumulative outgoing water / wet output * 100.", 100),
                "MeltTempC" => ("melt_temp_c", "CEL", "degC",
                    "Configured heated/ambient temperature selected by committed thermal demand and constraints.", 400),
                "MeltPressureBar" => ("melt_pressure_bar", "BAR", "bar",
                    "Configured working pressure when a transfer is committed, otherwise zero.", 1000),
                "ExtrusionStabilityScore" => ("extrusion_stability_score", string.Empty, "score (0..100)",
                    "Proxy: powered * 100 * max(0, 1 - cumulative contamination - wet moisture fraction).", 100),
                "DiffPressureBar" => ("diff_pressure_bar", "BAR", "bar",
                    "While transferring: configured clean pressure + configured loading rise * loading fraction.",
                    1000),
                "CloggingPct" => ("clogging_pct", "P1", "%",
                    "min(100, cumulative rejected dirt+labels kg / configured filter capacity kg * 100).", 100),
                "MeltCleanlinessIndex" => ("melt_cleanliness_index", string.Empty, "score (0..100)",
                    "100 * (1 - cumulative outgoing dirt+labels / dry output).", 100),
                "PelletSizeAvgMm" => ("pellet_size_avg_mm", "MMT", "mm",
                    "Configured nominal size after first output; no size-distribution simulation.", 1000),
                "BagWeightKg" => ("bag_weight_kg", "KGM", "kg", "Latest committed complete package wet mass.", 1000),
                "BagsPerHr" => ("bags_per_hr", string.Empty, "bags/h",
                    "Absolute complete package count * 3600 / committed logical seconds.", 1_000_000_000),
                "ColourR" => ("colour_r", string.Empty, "RGB channel",
                    "Committed package red: floored polymer-mass-weighted synthetic anchors; not calibration.", 255),
                "ColourG" => ("colour_g", string.Empty, "RGB channel",
                    "Committed package green: floored polymer-mass-weighted synthetic anchors; not calibration.", 255),
                "ColourB" => ("colour_b", string.Empty, "RGB channel",
                    "Committed package blue: floored polymer-mass-weighted synthetic anchors; not calibration.", 255),
                _ => throw new InvalidOperationException("An explicit field/unit/formula mapping is required: " + name)
            };
        }
    }
}
