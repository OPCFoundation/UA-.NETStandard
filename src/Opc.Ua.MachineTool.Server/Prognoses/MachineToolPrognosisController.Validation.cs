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
using static Opc.Ua.MachineTool.Server.MachineToolInventoryPublisher;

namespace Opc.Ua.MachineTool.Server
{
    public sealed partial class MachineToolPrognosisController
    {
        /// <summary>
        /// Rejects unbounded configuration and unsupported or implicit prediction capabilities.
        /// </summary>
        private void ValidateOptions()
        {
            if ((m_options.Features & ~MachineToolFeatures.All) != 0 ||
                m_options.MaximumPrognoses is < 1 or > 65536 ||
                m_options.MaximumReferencedEntities is < 1 or > 131072 ||
                m_options.MaximumTextLength is < 1 or > 65536)
            {
                throw new ArgumentOutOfRangeException(nameof(m_options), "Invalid prognosis bounds or features.");
            }
            foreach (MachineToolPrognosisKind kind in m_options.SupportedKinds)
            {
                if ((int)kind is < 0 or > 10 || !m_supported.Add(kind))
                {
                    throw Invalid("Prognosis kinds must be known and unique.");
                }
            }
            if (Has(MachineToolFeatures.Prognoses) && m_supported.Count == 0)
            {
                throw Invalid("An enabled prognosis surface requires explicitly selected provider capabilities.");
            }
            if (m_supported.Contains(MachineToolPrognosisKind.Companion) && m_options.CreatePrognosisAsync == null)
            {
                throw Invalid("A companion forecast requires a typed generated factory.");
            }
            if (Has(MachineToolFeatures.DynamicPrognoses) &&
                (!Has(MachineToolFeatures.Prognoses) || m_options.PublishFinalObservationsAsync == null))
            {
                throw Invalid("Dynamic prognoses require the Prognoses feature and a final-publication barrier.");
            }
        }

        /// <summary>
        /// Validates and resolves the full observation before any forecast or sibling value can change.
        /// </summary>
        private ArrayOf<ResolvedPrognosis> ValidateAndResolve(
            MachineToolSnapshot snapshot,
            Dictionary<string, MachineToolEquipmentController.ToolNodes>? tools,
            Func<string, ProductionJobState?>? resolveJob,
            Func<string, string, string, ProductionPartState?>? resolvePart)
        {
            if (!Has(MachineToolFeatures.Prognoses))
            {
                if (snapshot.Prognoses?.Entries.Count > 0)
                {
                    throw Invalid("Forecast observations require the explicitly selected Prognoses feature.");
                }
                return [];
            }
            MachineToolPrognosisObservation supplied = snapshot.Prognoses ??
                throw Invalid("The enabled prognosis surface requires an explicit provider observation.");
            if (supplied.Entries.Count > m_options.MaximumPrognoses)
            {
                throw Invalid("The prognosis inventory exceeds its configured bound.");
            }
            if (Observation != null && !Has(MachineToolFeatures.DynamicPrognoses) &&
                !SameOrder(Observation.Prognoses!.Entries, supplied.Entries))
            {
                throw Invalid("A static prognosis inventory cannot add, remove or reorder forecast slots.");
            }
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (MachineToolPrognosis entry in supplied.Entries)
            {
                if (entry == null)
                {
                    throw Invalid("A prognosis observation cannot be null.");
                }
                Text(entry.Key, true);
                Text(entry.NamespaceUri, false);
                if (!keys.Add(entry.Key) || !m_supported.Contains(entry.Kind))
                {
                    throw Invalid("Forecast keys must be unique and their kinds explicitly supported.");
                }
                PrognosisName(entry);
                ValidateFields(entry);
            }
            if (supplied.Entries.Count == 0)
            {
                return [];
            }
            List<Entity> entities = BuildEntities(snapshot, tools, resolveJob, resolvePart);
            var resolved = new ResolvedPrognosis[supplied.Entries.Count];
            for (int i = 0; i < resolved.Length; i++)
            {
                resolved[i] = Resolve(supplied.Entries[i], entities);
            }
            return resolved.ToArrayOf();
        }

        /// <summary>
        /// Validates only fields declared for the selected concrete standard type.
        /// </summary>
        private void ValidateFields(MachineToolPrognosis value)
        {
            MachineToolEquipmentController.ValidateSample(value.Sample);
            MachineToolEquipmentController.ValidateDataValue(value.PredictedTime);
            if (!value.PredictedTime.WrappedValue.IsNull &&
                !value.PredictedTime.WrappedValue.TryGetValue(out DateTimeUtc _))
            {
                throw Invalid("PredictedTime must retain an actual UtcTime value.");
            }
            if (value.PredictedTime.WrappedValue.TryGetValue(out DateTimeUtc prediction) &&
                prediction == default && !StatusCode.IsBad(value.PredictedTime.StatusCode))
            {
                throw Invalid("A usable forecast requires a provider-reported prediction, not an unspecified date.");
            }
            bool activity = value.Kind is MachineToolPrognosisKind.Maintenance or
                MachineToolPrognosisKind.ManualActivity or MachineToolPrognosisKind.ProcessChangeover;
            bool part = value.Kind is MachineToolPrognosisKind.PartLoad or MachineToolPrognosisKind.PartUnload;
            bool tool = value.Kind is MachineToolPrognosisKind.ToolChange or MachineToolPrognosisKind.ToolLoad or
                MachineToolPrognosisKind.ToolUnload;
            bool location = part || tool || value.Kind == MachineToolPrognosisKind.ProcessChangeover;
            bool job = value.Kind == MachineToolPrognosisKind.ProductionJobEnd;
            bool utility = value.Kind == MachineToolPrognosisKind.UtilityChange;
            Localized(value.Activity, activity);
            Localized(value.Location, location);
            if ((!activity && !value.Activity.IsNull) || (!location && !value.Location.IsNull) ||
                (!part && (value.PartName != null || value.PartIdentifier != null)) ||
                (!tool && (value.ToolName != null || value.ToolIdentifier != null)) ||
                (!job && value.SourceIdentifier != null) || (!utility && value.UtilityName != null))
            {
                throw Invalid("The observation supplies a component not declared by its selected prognosis type.");
            }
            Text(value.PartIdentifier, false);
            Text(value.PartName, false);
            Text(value.ToolIdentifier, false);
            Text(value.ToolName, false);
            Text(value.SourceIdentifier, false);
            Text(value.UtilityName, utility);
            if (value.Kind == MachineToolPrognosisKind.Companion)
            {
                NodeId type = ExpectedType(value);
                if (type.IsNull || !m_context.Context.TypeTable.IsTypeOf(type,
                    ExpandedNodeId.ToNodeId(ObjectTypeIds.PrognosisType, m_context.Context.NamespaceUris)))
                {
                    throw Invalid("The companion forecast must name its actual loaded PrognosisType subtype.");
                }
            }
            else if (!value.CompanionTypeId.IsNull)
            {
                throw Invalid("A standard forecast does not accept an unrelated companion type identity.");
            }
            ValidateTarget(value.Target, value.Kind);
        }

        /// <summary>
        /// Rejects incomplete, cross-kind or ambiguous key combinations before resolving an actual node.
        /// </summary>
        private void ValidateTarget(MachineToolPrognosisTarget? target, MachineToolPrognosisKind kind)
        {
            if (target == null)
            {
                return;
            }
            bool tool = kind is MachineToolPrognosisKind.ToolChange or MachineToolPrognosisKind.ToolUnload;
            bool part = kind is MachineToolPrognosisKind.PartLoad or MachineToolPrognosisKind.PartUnload;
            bool job = kind == MachineToolPrognosisKind.ProductionJobEnd;
            if (!tool && !part && !job)
            {
                throw Invalid("This prognosis type has no affected-entity NodeId component.");
            }
            Text(target.ToolKey, false);
            Text(target.JobKey, false);
            Text(target.PartSetKey, false);
            Text(target.PartKey, false);
            bool keys = target.ToolKey != null || target.JobKey != null || target.PartSetKey != null ||
                target.PartKey != null;
            if (!target.NodeId.IsNull)
            {
                if (keys)
                {
                    throw Invalid("Use either an actual NodeId or stable entity keys, not both.");
                }
                return;
            }
            if (!keys || (tool && (target.ToolKey == null || target.JobKey != null ||
                target.PartSetKey != null || target.PartKey != null)) ||
                (job && (target.JobKey == null || target.ToolKey != null ||
                    target.PartSetKey != null || target.PartKey != null)) ||
                (part && (target.JobKey == null || target.PartSetKey == null ||
                    target.PartKey == null || target.ToolKey != null)))
            {
                throw Invalid("The affected-entity key does not identify the kind required by this forecast.");
            }
        }

        /// <summary>
        /// Resolves only actually represented same-instance entities and reconciles their supplied descriptive fields.
        /// </summary>
        private ResolvedPrognosis Resolve(MachineToolPrognosis value, List<Entity> entities)
        {
            EntityKind? kind = value.Kind switch
            {
                MachineToolPrognosisKind.ToolChange or MachineToolPrognosisKind.ToolUnload or
                    MachineToolPrognosisKind.ToolLoad => EntityKind.Tool,
                MachineToolPrognosisKind.PartLoad or MachineToolPrognosisKind.PartUnload => EntityKind.Part,
                MachineToolPrognosisKind.ProductionJobEnd => EntityKind.Job,
                _ => null
            };
            string? identifier = value.Kind == MachineToolPrognosisKind.ProductionJobEnd ? value.SourceIdentifier :
                kind == EntityKind.Part ? value.PartIdentifier : value.ToolIdentifier;
            string? name = kind == EntityKind.Part ? value.PartName : value.ToolName;
            if (kind == null)
            {
                return new ResolvedPrognosis(value, NodeId.Null, identifier, name);
            }
            Entity? found = null;
            foreach (Entity entity in entities)
            {
                if (entity.Kind != kind)
                {
                    continue;
                }
                bool match = value.Target == null ?
                    identifier != null && string.Equals(identifier, entity.Identifier, StringComparison.Ordinal) :
                    Matches(entity, value.Target);
                if (match)
                {
                    if (found != null)
                    {
                        throw Invalid("An ambiguous affected identifier requires an explicit per-instance slot key.");
                    }
                    found = entity;
                }
            }
            if (value.Target != null && found == null)
            {
                throw Invalid("The affected entity is missing, has the wrong kind, or belongs to another instance.");
            }
            NodeId node = NodeId.Null;
            if (found != null)
            {
                if (found.State == null || !BelongsToMachine(found.State))
                {
                    throw Invalid("The affected entity must be staged on this actual machine before the forecast.");
                }
                if ((identifier != null && found.Identifier != identifier) ||
                    (name != null && found.Name != name))
                {
                    throw Invalid("The forecast's name or identifier differs from the actual affected entity.");
                }
                identifier ??= found.Identifier;
                name ??= found.Name;
                node = found.State.NodeId;
            }
            if (kind == EntityKind.Part)
            {
                Text(name, true);
            }
            if (kind == EntityKind.Job)
            {
                Text(identifier, true);
            }
            return new ResolvedPrognosis(value,
                value.Kind == MachineToolPrognosisKind.ToolLoad ? NodeId.Null : node, identifier, name);
        }

        /// <summary>
        /// Builds a bounded index from the same prospective sibling observations used by the containing transaction.
        /// </summary>
        private List<Entity> BuildEntities(
            MachineToolSnapshot snapshot,
            Dictionary<string, MachineToolEquipmentController.ToolNodes>? tools,
            Func<string, ProductionJobState?>? resolveJob,
            Func<string, string, string, ProductionPartState?>? resolvePart)
        {
            var entities = new List<Entity>();
            if (snapshot.Equipment != null)
            {
                AddTools(snapshot.Equipment.Tools, m_machine.Equipment?.Tools, 0);
            }
            if (snapshot.Production == null)
            {
                throw Invalid("A complete retained-production observation is required for entity resolution.");
            }
            foreach (MachineToolProductionJob job in snapshot.Production.Jobs)
            {
                if (job == null)
                {
                    throw Invalid("A prospective production job cannot be null.");
                }
                string jobKey = string.IsNullOrEmpty(job.Key) ? job.Identifier : job.Key;
                ProductionJobState? state = resolveJob == null ?
                    m_machine.Production?.ProductionPlan?.FindChild(
                        m_context.Context, InstanceName(jobKey)) as ProductionJobState : resolveJob(jobKey);
                Add(new Entity(EntityKind.Job, state, jobKey, null, null, job.Identifier, null));
                foreach (MachineToolProductionPartSet set in job.PartSets)
                {
                    if (set == null)
                    {
                        throw Invalid("A prospective production part set cannot be null.");
                    }
                    foreach (MachineToolProductionPart part in set.Parts)
                    {
                        if (part == null)
                        {
                            throw Invalid("A prospective production part cannot be null.");
                        }
                        ProductionPartState? partState;
                        if (resolvePart != null)
                        {
                            partState = resolvePart(jobKey, set.Key, part.Key);
                        }
                        else
                        {
                            var setState = state?.PartSets?.FindChild(m_context.Context, InstanceName(set.Key))
                                as ProductionPartSetState;
                            partState = setState?.PartsPerRun?.FindChild(m_context.Context, InstanceName(part.Key))
                                as ProductionPartState;
                        }
                        Add(new Entity(EntityKind.Part, partState, part.Key, jobKey, set.Key,
                            part.Identifier, part.Name));
                    }
                }
            }
            return entities;

            void Add(Entity entity)
            {
                if (entities.Count == m_options.MaximumReferencedEntities)
                {
                    throw Invalid("The prospective affected-entity inventory exceeds its configured bound.");
                }
                entities.Add(entity);
            }

            void AddTools(ArrayOf<MachineToolTool> values, BaseObjectState? parent, int depth)
            {
                if (depth > 1)
                {
                    throw Invalid("The prospective tool inventory contains nested multitools.");
                }
                foreach (MachineToolTool value in values)
                {
                    if (value == null)
                    {
                        throw Invalid("A prospective tool cannot be null.");
                    }
                    BaseToolState? state;
                    if (tools != null)
                    {
                        state = tools.TryGetValue(value.Key, out MachineToolEquipmentController.ToolNodes? item) ?
                            item.State : null;
                    }
                    else
                    {
                        state = parent?.FindChild(m_context.Context, InstanceName(value.Key)) as BaseToolState;
                    }
                    Add(new Entity(EntityKind.Tool, state, value.Key, null, null, value.Identifier, value.Name));
                    if (value.Tools.Count != 0)
                    {
                        AddTools(value.Tools, state, depth + 1);
                    }
                }
            }
        }

        /// <summary>
        /// Checks an actual NodeId or the full immutable path of the requested affected entity.
        /// </summary>
        private static bool Matches(Entity entity, MachineToolPrognosisTarget target)
        {
            if (!target.NodeId.IsNull)
            {
                return entity.State?.NodeId == target.NodeId;
            }
            return entity.Kind switch
            {
                EntityKind.Tool => entity.Key == target.ToolKey,
                EntityKind.Job => entity.Key == target.JobKey,
                _ => entity.Key == target.PartKey && entity.JobKey == target.JobKey &&
                    entity.PartSetKey == target.PartSetKey
            };
        }

        /// <summary>
        /// Uses actual generated parent identity, not a namespace or string-prefix heuristic.
        /// </summary>
        private bool BelongsToMachine(BaseInstanceState state)
        {
            NodeState? current = state;
            for (int depth = 0; depth < 16 && current is BaseInstanceState instance; depth++)
            {
                if (ReferenceEquals(current, m_machine))
                {
                    return true;
                }
                current = instance.Parent;
            }
            return false;
        }

        /// <summary>
        /// Keeps a forecast slot's selected type and optional shape stable while allowing its samples to change.
        /// </summary>
        private static void ValidateShape(ResolvedPrognosis before, ResolvedPrognosis after)
        {
            if (before.Observation.Kind != after.Observation.Kind ||
                before.Observation.NamespaceUri != after.Observation.NamespaceUri ||
                before.Observation.CompanionTypeId != after.Observation.CompanionTypeId ||
                before.NodeId.IsNull != after.NodeId.IsNull ||
                (before.Identifier == null) != (after.Identifier == null) ||
                (before.Name == null) != (after.Name == null))
            {
                throw Invalid("An existing forecast slot cannot change its selected type or optional schema.");
            }
        }

        /// <summary>
        /// Compares provider forecast order without inferring identity from its predicted time.
        /// </summary>
        private static bool SameOrder(ArrayOf<MachineToolPrognosis> before, ArrayOf<MachineToolPrognosis> after)
        {
            if (before.Count != after.Count)
            {
                return false;
            }
            for (int i = 0; i < before.Count; i++)
            {
                if (before[i] == null || after[i] == null || before[i].Key != after[i].Key)
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Bounds localized text while preserving the original locale.
        /// </summary>
        private void Localized(LocalizedText text, bool required)
        {
            Text(text.Text, required);
            Text(text.Locale, false);
        }

        /// <summary>
        /// Bounds all externally supplied names and rejects absent mandatory text.
        /// </summary>
        private void Text(string? text, bool required)
        {
            if ((required && string.IsNullOrWhiteSpace(text)) || text?.Length > m_options.MaximumTextLength)
            {
                throw Invalid("Prognosis text is absent or exceeds its configured bound.");
            }
        }

        /// <summary>
        /// Qualifies a forecast using the explicit loaded companion namespace, or the application namespace.
        /// </summary>
        private QualifiedName PrognosisName(MachineToolPrognosis value)
        {
            int index = value.NamespaceUri == null ? m_context.InstanceNamespaceIndex :
                m_context.Context.NamespaceUris.GetIndex(value.NamespaceUri);
            if (index < 0)
            {
                throw Invalid("The forecast's companion browse-name namespace is not loaded.");
            }
            return new QualifiedName(value.Key, (ushort)index);
        }

        /// <summary>
        /// The only standard affected-entity categories.
        /// </summary>
        private enum EntityKind
        {
            Tool,
            Job,
            Part
        }

        /// <summary>
        /// Identifies a real prospective entity and its provider-reported identifying fields.
        /// </summary>
        private sealed record Entity(
            EntityKind Kind, BaseInstanceState? State, string Key, string? JobKey, string? PartSetKey,
            string? Identifier, string? Name);
    }
}
