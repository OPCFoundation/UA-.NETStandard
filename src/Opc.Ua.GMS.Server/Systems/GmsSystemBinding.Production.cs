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
using System.Threading.Tasks;
using Opc.Ua.GMS.Contracts;
using Opc.Ua.MachineTool;
using Opc.Ua.MachineTool.Server;

namespace Opc.Ua.GMS.Server
{
    internal sealed partial class GmsSystemBinding
    {
        /// <summary>
        /// Selects actual GMS job/part factories while retaining the existing production state/event/lifetime module.
        /// </summary>
        internal MachineToolProductionOptions ProductionOptions(MachineToolProductionOptions options)
        {
            MachineToolFeatures features = options.Features;
            if (Has(GmsFeatures.Production))
            {
                features |= MachineToolFeatures.Production | MachineToolFeatures.ProductionParts;
            }
            if (Has(GmsFeatures.DynamicProduction))
            {
                features |= MachineToolFeatures.DynamicProductionPlan;
            }
            return options with
            {
                Features = features,
                CreateJobAsync = (context, parent, name, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    GMSJobState node = context.CreateInstanceOfGMSJobType(parent, name);
                    string key = name.Name!;
                    GmsJobObservation value = m_jobInput.TryGetValue(key, out GmsJobObservation? sample)
                        ? sample : s_emptyJob;
                    SelectFields(node, JobFields(value));
                    m_jobs.Add(key, node);
                    return new ValueTask<MachineToolProductionNode<ProductionJobState>>(
                        new MachineToolProductionNode<ProductionJobState>(node, new NativeNodeLease(() =>
                        {
                            if (m_jobs.TryGetValue(key, out GMSJobState? current) && ReferenceEquals(current, node))
                            {
                                m_jobs.Remove(key);
                            }
                        })));
                },
                CreatePartAsync = (context, parent, name, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    GMSJobState job = Ancestor<GMSJobState>(parent);
                    ProductionPartSetState set = Ancestor<ProductionPartSetState>(parent);
                    var key = (job.BrowseName.Name!, set.BrowseName.Name!, name.Name!);
                    GMSPartState node = context.CreateInstanceOfGMSPartType(parent, name);
                    GmsPartObservation value = m_partInput.TryGetValue(key, out GmsPartObservation? sample)
                        ? sample : s_emptyPart;
                    SelectFields(node, PartFields(value));
                    foreach ((string field, GmsCatalogObservation? catalog) in Catalogs(value))
                    {
                        if (catalog != null)
                        {
                            _ = AddNativeField(node, field);
                            CatalogState child = Child<CatalogState>(node, GmsName(field));
                            child.AccessLevel = AccessLevels.CurrentRead;
                            child.UserAccessLevel = AccessLevels.CurrentRead;
                            child.CatalogName!.AccessLevel = AccessLevels.CurrentRead;
                            child.CatalogName.UserAccessLevel = AccessLevels.CurrentRead;
                            child.CatalogEntry!.AccessLevel = AccessLevels.CurrentRead;
                            child.CatalogEntry.UserAccessLevel = AccessLevels.CurrentRead;
                        }
                    }
                    m_parts.Add(key, node);
                    return new ValueTask<MachineToolProductionNode<ProductionPartState>>(
                        new MachineToolProductionNode<ProductionPartState>(node, new NativeNodeLease(() =>
                        {
                            if (m_parts.TryGetValue(key, out GMSPartState? current) && ReferenceEquals(current, node))
                            {
                                m_parts.Remove(key);
                            }
                        })));
                }
            };
        }

        /// <summary>
        /// Resolves native factory ancestry without global lookup or fabricated cross-instance identifiers.
        /// </summary>
        /// <typeparam name="T">The actual generated containing type.</typeparam>
        private static T Ancestor<T>(NodeState node) where T : BaseInstanceState
        {
            for (int depth = 0; depth < 8 && node is BaseInstanceState instance; depth++)
            {
                if (instance is T found)
                {
                    return found;
                }
                if (instance.Parent == null)
                {
                    break;
                }
                node = instance.Parent;
            }
            throw Invalid("The retained GMS part factory must be inside its actual job and part set.");
        }

        /// <summary>
        /// Plans native metadata assignments after generic prospective topology has been fully validated.
        /// </summary>
        private void PlanProduction(GmsSystemSnapshot snapshot, List<Action> writes)
        {
            foreach (MachineToolProductionJob job in snapshot.MachineTool.Production.Jobs)
            {
                string key = JobKey(job);
                GmsJobObservation value = m_jobInput.TryGetValue(key, out GmsJobObservation? sample)
                    ? sample : s_emptyJob;
                if (!m_jobs.TryGetValue(key, out GMSJobState? node))
                {
                    throw Invalid("The retained job factory did not return its native GMSJobType.");
                }
                PlanFields(node, JobFields(value), writes);
                foreach (MachineToolProductionPartSet set in job.PartSets)
                {
                    foreach (MachineToolProductionPart part in set.Parts)
                    {
                        var partKey = (key, set.Key, part.Key);
                        GmsPartObservation metadata = m_partInput.TryGetValue(partKey, out GmsPartObservation? supplied)
                            ? supplied : s_emptyPart;
                        if (!m_parts.TryGetValue(partKey, out GMSPartState? partNode))
                        {
                            throw Invalid("The retained part factory did not return its native GMSPartType.");
                        }
                        PlanFields(partNode, PartFields(metadata), writes);
                        foreach ((string name, GmsCatalogObservation? catalog) in Catalogs(metadata))
                        {
                            CatalogState? child = partNode.FindChild(m_context.Context, GmsName(name)) as CatalogState;
                            if (child == null && catalog == null)
                            {
                                continue;
                            }
                            if (child == null || catalog == null)
                            {
                                throw Invalid("A native part catalog's configured presence cannot change in its slot.");
                            }
                            PlanCatalog(child, catalog, writes);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Preserves native Number representations and provider-authored catalog metadata.
        /// </summary>
        private static void PlanCatalog(CatalogState state, GmsCatalogObservation value, List<Action> writes)
        {
            var name = new DataValue(
                Variant.From(value.CatalogName), value.Value.StatusCode, value.Value.SourceTimestamp);
            var entry = new DataValue(
                Variant.From(value.CatalogEntry), value.Value.StatusCode, value.Value.SourceTimestamp);
            var enumeration = new DataValue(Variant.FromStructure(value.EnumValues),
                value.Value.StatusCode, value.Value.SourceTimestamp);
            writes.Add(() =>
            {
                Set(state, value.Value);
                Set(state.CatalogName!, name);
                Set(state.CatalogEntry!, entry);
                Set(state.EnumValues!, enumeration);
                if (state.ValueAsText != null)
                {
                    Set(state.ValueAsText, value.ValueAsText);
                }
            });
        }

        /// <summary>
        /// Stages validated native metadata against the prospective retained-production graph without changing
        /// the cached acquisition's indexes.
        /// </summary>
        private (Dictionary<string, GmsJobObservation> Jobs,
            Dictionary<(string Job, string Set, string Part), GmsPartObservation> Parts) ValidateProduction(
            GmsSystemSnapshot snapshot)
        {
            if ((!Has(GmsFeatures.Production) && (snapshot.Jobs.Count != 0 || snapshot.Parts.Count != 0)) ||
                snapshot.Jobs.Count > m_options.MaximumArrayLength ||
                snapshot.Parts.Count > m_options.MaximumArrayLength)
            {
                throw Invalid(
                    "Native production metadata requires explicit selection and bounded job/part inventories.");
            }
            var jobs = new HashSet<string>(StringComparer.Ordinal);
            var parts = new HashSet<(string Job, string Set, string Part)>();
            if (snapshot.MachineTool.Production == null)
            {
                throw Invalid("The inherited production observation cannot be null.");
            }
            foreach (MachineToolProductionJob job in snapshot.MachineTool.Production.Jobs)
            {
                if (job == null)
                {
                    throw Invalid("A retained job observation cannot be null.");
                }
                string key = JobKey(job);
                jobs.Add(key);
                foreach (MachineToolProductionPartSet set in job.PartSets)
                {
                    if (set == null)
                    {
                        throw Invalid("A retained part set cannot be null.");
                    }
                    foreach (MachineToolProductionPart part in set.Parts)
                    {
                        if (part == null)
                        {
                            throw Invalid("A retained part observation cannot be null.");
                        }
                        parts.Add((key, set.Key, part.Key));
                    }
                }
            }
            var jobInput = new Dictionary<string, GmsJobObservation>(StringComparer.Ordinal);
            foreach (GmsJobObservation value in snapshot.Jobs)
            {
                if (value == null || !jobs.Contains(value.Key) || !jobInput.TryAdd(value.Key, value))
                {
                    throw Invalid("Native job metadata must identify exactly one actual job in this GMS.");
                }
                Text(value.Key, true);
                TextSample(value.BatchIdentifier);
                Nonnegative(value.Duration);
                Nonnegative(value.RemainingTime);
                Enumeration(value.MeasurementReason, 5);
            }
            var partInput = new Dictionary<(string Job, string Set, string Part), GmsPartObservation>();
            foreach (GmsPartObservation value in snapshot.Parts)
            {
                if (value == null)
                {
                    throw Invalid("Native part metadata cannot be null.");
                }
                var key = (value.JobKey, value.PartSetKey, value.PartKey);
                if (!parts.Contains(key) || !partInput.TryAdd(key, value))
                {
                    throw Invalid("Native part metadata must identify exactly one actual part in this GMS.");
                }
                TextSample(value.PartDescription);
                TextSample(value.PartAmendmentStatus);
                TextSample(value.ProductionNumber);
                foreach ((string name, GmsCatalogObservation? catalog) in Catalogs(value))
                {
                    if (catalog != null)
                    {
                        ValidateCatalog(catalog, name == BrowseNames.ProcessParameter);
                    }
                }
            }
            return (jobInput, partInput);
        }

        /// <summary>
        /// Validates native catalog fields; no external catalog access or mutation is performed.
        /// </summary>
        private void ValidateCatalog(GmsCatalogObservation value, bool array)
        {
            if (array)
            {
                NumberArray(value.Value);
            }
            else
            {
                Number(value.Value, required: true);
            }
            Text(value.CatalogName, true);
            Text(value.CatalogEntry, true);
            Raw(value.ValueAsText, BuiltInType.LocalizedText, required: true);
            if (value.ValueAsText.WrappedValue.TryGetValue(out LocalizedText display))
            {
                Text(display.Text);
            }
            if (value.EnumValues.IsNull || value.EnumValues.Count > m_options.MaximumArrayLength)
            {
                throw Invalid("Catalog EnumValues must be explicitly supplied and bounded.");
            }
            var values = new HashSet<long>();
            foreach (EnumValueType enumeration in value.EnumValues)
            {
                if (enumeration == null || !values.Add(enumeration.Value))
                {
                    throw Invalid("Catalog enumeration values must be non-null and distinct.");
                }
                Text(enumeration.DisplayName.Text);
                Text(enumeration.Description.Text);
            }
        }

        /// <summary>
        /// Checks a bounded native Number array without converting its numeric element representation.
        /// </summary>
        private void NumberArray(in DataValue value)
        {
            Variant raw = value.WrappedValue;
            BuiltInType type = raw.TypeInfo.BuiltInType;
            Raw(value, type, required: true, rank: ValueRanks.OneDimension);
            if (raw.IsNull)
            {
                return;
            }
            if (type is < BuiltInType.SByte or > BuiltInType.Double)
            {
                throw Invalid("ProcessParameter must contain a homogeneous native numeric array.");
            }
            int count = raw.TryGetValue(out ArrayOf<sbyte> i8) ? i8.Count :
                raw.TryGetValue(out ArrayOf<byte> u8) ? u8.Count :
                raw.TryGetValue(out ArrayOf<short> i16) ? i16.Count :
                raw.TryGetValue(out ArrayOf<ushort> u16) ? u16.Count :
                raw.TryGetValue(out ArrayOf<int> i32) ? i32.Count :
                raw.TryGetValue(out ArrayOf<uint> u32) ? u32.Count :
                raw.TryGetValue(out ArrayOf<long> i64) ? i64.Count :
                raw.TryGetValue(out ArrayOf<ulong> u64) ? u64.Count :
                raw.TryGetValue(out ArrayOf<float> f32) ? f32.Count :
                raw.TryGetValue(out ArrayOf<double> f64) ? f64.Count : 0;
            if (count > m_options.MaximumArrayLength)
            {
                throw Invalid("ProcessParameter exceeds its configured array bound.");
            }
            if (raw.TryGetValue(out ArrayOf<float> singles))
            {
                foreach (float number in singles)
                {
                    if (float.IsNaN(number) || float.IsInfinity(number))
                    {
                        throw Invalid("ProcessParameter values must be finite.");
                    }
                }
            }
            if (raw.TryGetValue(out ArrayOf<double> doubles))
            {
                foreach (double number in doubles)
                {
                    if (double.IsNaN(number) || double.IsInfinity(number))
                    {
                        throw Invalid("ProcessParameter values must be finite.");
                    }
                }
            }
        }

        /// <summary>
        /// Validates optional raw String observations.
        /// </summary>
        private void TextSample(in DataValue value)
        {
            Raw(value, BuiltInType.String);
            if (value.WrappedValue.TryGetValue(out string text))
            {
                Text(text);
            }
        }

        /// <summary>
        /// Gets the shared stable slot identity without introducing a new standard property.
        /// </summary>
        private static string JobKey(MachineToolProductionJob job) =>
            string.IsNullOrEmpty(job.Key) ? job.Identifier : job.Key;

        /// <summary>
        /// Lists native optional job fields.
        /// </summary>
        private static ArrayOf<(string Name, DataValue Value)> JobFields(GmsJobObservation value) =>
        [
            (BrowseNames.BatchIdentifier, value.BatchIdentifier), (BrowseNames.Duration, value.Duration),
            (BrowseNames.RemainingTime, value.RemainingTime), (BrowseNames.MeasurementReason, value.MeasurementReason)
        ];

        /// <summary>
        /// Lists native optional part properties.
        /// </summary>
        private static ArrayOf<(string Name, DataValue Value)> PartFields(GmsPartObservation value) =>
        [
            (BrowseNames.PartDescription, value.PartDescription),
            (BrowseNames.PartAmendmentStatus, value.PartAmendmentStatus),
            (BrowseNames.ProductionNumber, value.ProductionNumber)
        ];

        /// <summary>
        /// Lists all released native catalog descendants, including the array-valued ProcessParameter.
        /// </summary>
        private static ArrayOf<(string Name, GmsCatalogObservation? Value)> Catalogs(GmsPartObservation value) =>
        [
            (BrowseNames.NestIdentifier, value.NestIdentifier), (BrowseNames.Operator, value.Operator),
            (BrowseNames.PartCarrierIdentifier, value.PartCarrierIdentifier),
            (BrowseNames.ProcessingMachineIdentifier, value.ProcessingMachineIdentifier),
            (BrowseNames.ProcessParameter, value.ProcessParameter)
        ];

        /// <summary>Optional metadata for this serialized prospective production update.</summary>
        private Dictionary<string, GmsJobObservation> m_jobInput = new(StringComparer.Ordinal);

        /// <summary>Optional part metadata for this serialized prospective production update.</summary>
        private Dictionary<(string Job, string Set, string Part), GmsPartObservation> m_partInput = [];

        /// <summary>Actual job nodes with removal and rollback owned by shared production.</summary>
        private readonly Dictionary<string, GMSJobState> m_jobs = new(StringComparer.Ordinal);

        /// <summary>Actual part nodes with removal and rollback owned by shared production.</summary>
        private readonly Dictionary<(string Job, string Set, string Part), GMSPartState> m_parts = [];

        /// <summary>The absence of any selected optional native job metadata.</summary>
        private static readonly GmsJobObservation s_emptyJob = new();

        /// <summary>The absence of any selected optional native part metadata.</summary>
        private static readonly GmsPartObservation s_emptyPart = new();

        /// <summary>
        /// Enlists native indexes in the existing shared node lifetime, without implementing another deletion protocol.
        /// </summary>
        private sealed class NativeNodeLease(Action release) : IAsyncDisposable
        {
            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                release();
                return default;
            }
        }
    }
}
