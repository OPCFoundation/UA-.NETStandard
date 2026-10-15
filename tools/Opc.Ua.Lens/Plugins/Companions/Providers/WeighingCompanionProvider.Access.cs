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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua;
using Opc.Ua.Scales;
using Opc.Ua.Scales.Client;
using ReferenceTypeIds = Opc.Ua.ReferenceTypeIds;
using Scales = Opc.Ua.Scales;

namespace UaLens.Plugins.Companions.Providers
{
    internal sealed partial class WeighingCompanionProvider
    {
        private static ValueTask<NodeId> ResolveAsync(
            CompanionContext context, NodeId parent, string name, CancellationToken cancellationToken,
            string? namespaceUri = null)
        {
            return IndustrialCompanionAccess.ResolveChildAsync(context, parent,
                namespaceUri ?? Scales.Namespaces.Scales, name, true, cancellationToken);
        }

        private static async ValueTask<ArrayOf<DataValue>> ReadAsync(
            CompanionContext context, ArrayOf<ReadValueId> nodes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ReadResponse response = await context.Session.ReadAsync(
                null, 0, TimestampsToReturn.Source, nodes, cancellationToken).ConfigureAwait(false);
            RequireHeader(response.ResponseHeader);
            if (response.Results.Count != nodes.Count)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, "Incomplete weighing Read response.");
            }
            cancellationToken.ThrowIfCancellationRequested();
            return response.Results;
        }

        private static async ValueTask<ArrayOf<Field>> ReadFieldsAsync(
            CompanionContext context, NodeId parent, ArrayOf<FieldSpec> specifications,
            CancellationToken cancellationToken)
        {
            CellCompanionSupport.CheckCount(specifications.Count, context.MaxFields, "Weighing read fields");
            var fields = new Field[specifications.Count];
            var paths = new List<BrowsePath>();
            var pathPositions = new List<int>();
            var reads = new List<ReadValueId>();
            var readPositions = new List<int>();
            for (int index = 0; index < specifications.Count; index++)
            {
                FieldSpec specification = specifications[index];
                fields[index] = new Field(specification, default,
                    DataValue.FromStatusCode(StatusCodes.BadNotFound));
                if (specification.Name.Length == 0)
                {
                    reads.Add(new ReadValueId { NodeId = parent, AttributeId = Attributes.Value });
                    readPositions.Add(index);
                    continue;
                }
                string uri = specification.NamespaceUri ?? Scales.Namespaces.Scales;
                int namespaceIndex = context.Session.NamespaceUris.GetIndex(uri);
                if (namespaceIndex < 0)
                {
                    continue;
                }
                paths.Add(new BrowsePath
                {
                    StartingNode = parent,
                    RelativePath = new RelativePath
                    {
                        Elements =
                        [
                            new RelativePathElement
                            {
                                ReferenceTypeId = ReferenceTypeIds.HierarchicalReferences,
                                IncludeSubtypes = true,
                                TargetName = new QualifiedName(specification.Name, (ushort)namespaceIndex)
                            }
                        ]
                    }
                });
                pathPositions.Add(index);
            }
            if (paths.Count != 0)
            {
                TranslateBrowsePathsToNodeIdsResponse translated =
                    await context.Session.TranslateBrowsePathsToNodeIdsAsync(
                        null, [.. paths], cancellationToken).ConfigureAwait(false);
                RequireHeader(translated.ResponseHeader);
                if (translated.Results.Count != paths.Count)
                {
                    throw new ServiceResultException(StatusCodes.BadDecodingError, "Incomplete weighing field lookup.");
                }
                for (int index = 0; index < paths.Count; index++)
                {
                    int position = pathPositions[index];
                    BrowsePathResult result = translated.Results[index];
                    if (!StatusCode.IsGood(result.StatusCode))
                    {
                        fields[position] = new Field(specifications[position], default,
                            DataValue.FromStatusCode(result.StatusCode == StatusCodes.BadNoMatch
                                ? StatusCodes.BadNotFound : result.StatusCode));
                        continue;
                    }
                    if (result.Targets.Count != 1 || result.Targets[0].RemainingPathIndex != uint.MaxValue)
                    {
                        throw new ServiceResultException(StatusCodes.BadNoMatch, "Ambiguous weighing field lookup.");
                    }
                    NodeId id = IndustrialCompanionAccess.LocalId(context, result.Targets[0].TargetId);
                    if (id.IsNull)
                    {
                        throw new ServiceResultException(StatusCodes.BadNotSupported,
                            "Cross-server weighing fields are not supported.");
                    }
                    reads.Add(new ReadValueId { NodeId = id, AttributeId = Attributes.Value });
                    readPositions.Add(position);
                }
            }
            if (reads.Count != 0)
            {
                ArrayOf<DataValue> results = await ReadAsync(context, [.. reads], cancellationToken)
                    .ConfigureAwait(false);
                for (int index = 0; index < reads.Count; index++)
                {
                    int position = readPositions[index];
                    fields[position] = new Field(specifications[position], reads[index].NodeId, results[index]);
                }
            }
            return fields;
        }

        private static async ValueTask InspectWeightAsync(
            CompanionContext context, NodeId scale, string name, FieldBuffer values,
            CancellationToken cancellationToken)
        {
            NodeId weight = await ResolveAsync(context, scale, name, cancellationToken).ConfigureAwait(false);
            if (weight.IsNull)
            {
                values.Add(Absent(name));
                return;
            }
            ArrayOf<Field> fields = await ReadFieldsAsync(context, weight, s_weightFields, cancellationToken)
                .ConfigureAwait(false);
            string engineering = DescribeUnit(context, fields[8]) + "; " + DescribeRange(context, fields[9]);
            Field reading = fields[0];
            if (reading.Value.TryGetValue<WeightType>(out WeightType? typed, context.Session.MessageContext) &&
                typed is not null)
            {
                values.Add(reading.Present(name + " / Gross", Variant.From(typed.Gross),
                    engineering + FiniteNote(typed.Gross)));
                values.Add(reading.Present(name + " / Net", Variant.From(typed.Net),
                    engineering + FiniteNote(typed.Net)));
                values.Add(reading.Present(name + " / Tare", Variant.From(typed.Tare),
                    engineering + FiniteNote(typed.Tare)));
            }
            else
            {
                values.Add(reading.Present(name, DisplayValue(context, reading.Value),
                    engineering + "; expected WeightType; no usable gross/net/tare"));
            }
            for (int index = 1; index < fields.Count; index++)
            {
                Field field = fields[index];
                values.Add(field.Present(name + " / " + field.Specification.Name,
                    DisplayValue(context, field.Value), DescribeField(field)));
            }
        }

        private static async ValueTask InspectRangesAsync(
            CompanionContext context, NodeId scale, FieldBuffer values, CancellationToken cancellationToken)
        {
            ArrayOf<Field> units = await ReadFieldsAsync(context, scale,
                [new(Scales.BrowseNames.AllowedEngineeringUnits, BuiltInType.ExtensionObject, IsArray: true)],
                cancellationToken).ConfigureAwait(false);
            values.Add(units[0].Present("AllowedEngineeringUnits", DisplayValue(context, units[0].Value),
                DescribeField(units[0])));
            int count = 0;
            var budget = new IndustrialBrowseBudget(context);
            await foreach (ReferenceDescription reference in IndustrialCompanionAccess.BrowseAsync(
                context, scale, BrowseDirection.Forward, ReferenceTypeIds.HierarchicalReferences,
                NodeClass.Object, budget, cancellationToken).ConfigureAwait(false))
            {
                if (await IndustrialCompanionAccess.ClassifyAsync(context, reference.TypeDefinition,
                    [new(Scales.ObjectTypeIds.WeighingRangeElementType, "Range")], budget,
                    cancellationToken).ConfigureAwait(false) is null)
                {
                    continue;
                }
                CellCompanionSupport.CheckCount(++count, kMaxItems, "Weighing ranges");
                NodeId node = RequireLocal(context, reference.NodeId);
                ArrayOf<Field> fields = await ReadFieldsAsync(context, node,
                    [
                        new(Scales.BrowseNames.Range, BuiltInType.ExtensionObject),
                        new(Scales.BrowseNames.ActualScaleInterval, BuiltInType.Double),
                        new(Scales.BrowseNames.VerificationScaleInterval, BuiltInType.Double)
                    ], cancellationToken).ConfigureAwait(false);
                string prefix = "Range " + count.ToString(CultureInfo.InvariantCulture);
                for (int index = 0; index < fields.Count; index++)
                {
                    values.Add(fields[index].Present(prefix + " / " + fields[index].Specification.Name,
                        DisplayValue(context, fields[index].Value), DescribeField(fields[index])));
                }
            }
            if (count == 0)
            {
                values.Add(Absent("Weighing ranges"));
            }
        }

        private static async ValueTask InspectNotificationsAsync(
            CompanionContext context, NodeId scale, FieldBuffer values, CancellationToken cancellationToken)
        {
            ArrayOf<DataValue> attributes = await ReadAsync(context,
                [new ReadValueId { NodeId = scale, AttributeId = Attributes.EventNotifier }],
                cancellationToken).ConfigureAwait(false);
            DataValue notifier = attributes[0];
            values.Add(new CompanionValue("Event source", Variant.From(scale)));
            values.Add(new CompanionValue("EventNotifier", notifier.WrappedValue)
            {
                Quality = notifier.StatusCode,
                EngineeringMetadata = notifier.WrappedValue.TryGetValue(out byte _)
                    ? "Object attribute, not a measurement" : "Expected Byte EventNotifier attribute"
            });
            var client = new ScalesClient(context.Session, context.Telemetry);
            EventFilter filter = client.CreateNotificationFilter();
            values.Add(new CompanionValue("Scale event type",
                Variant.From(ExpandedNodeId.ToNodeId(Scales.ObjectTypeIds.ScaleEventType,
                    context.Session.NamespaceUris))));
            values.Add(new CompanionValue("Scale alarm type",
                Variant.From(ExpandedNodeId.ToNodeId(Scales.ObjectTypeIds.ScaleAlarmType,
                    context.Session.NamespaceUris))));
            foreach (SimpleAttributeOperand clause in filter.SelectClauses)
            {
                values.Add(new CompanionValue("Notification field",
                    Variant.From(string.Join(" / ", clause.BrowsePath))));
            }
        }

        private static async ValueTask InspectSectionAsync(
            CompanionContext context, ScaleEntry scale, string section, FieldBuffer values,
            CancellationToken cancellationToken)
        {
            NodeId group;
            switch (section)
            {
                case "identification":
                    group = await ResolveAsync(context, scale.NodeId, Opc.Ua.Di.BrowseNames.Identification,
                        cancellationToken, Opc.Ua.Di.Namespaces.OpcUaDi).ConfigureAwait(false);
                    break;
                case "products":
                    group = await ResolveAsync(context, scale.NodeId, Scales.BrowseNames.ProductionPreset,
                        cancellationToken).ConfigureAwait(false);
                    break;
                case "statistics":
                    group = await ResolveAsync(context, scale.NodeId, Scales.BrowseNames.ProductionOutput,
                        cancellationToken).ConfigureAwait(false);
                    break;
                case "recipes":
                    group = scale.Kind == ScaleKind.Recipe
                        ? await ResolveAsync(context, scale.NodeId, Scales.BrowseNames.Recipes, cancellationToken)
                            .ConfigureAwait(false)
                        : NodeId.Null;
                    break;
                default:
                    ArrayOf<Field> fields = await ReadFieldsAsync(context, scale.NodeId,
                        KindFields(scale), cancellationToken).ConfigureAwait(false);
                    foreach (Field field in fields)
                    {
                        values.Add(field.Present(field.Specification.Name, DisplayValue(context, field.Value),
                            DescribeField(field)));
                    }
                    return;
            }
            if (group.IsNull)
            {
                values.Add(Absent(section));
                return;
            }
            ExpandedNodeId expectedType = section switch
            {
                "products" => Scales.ObjectTypeIds.ProductionPresetType,
                "statistics" => Scales.ObjectTypeIds.StatisticType,
                "recipes" => Scales.ObjectTypeIds.RecipeManagementType,
                _ => default
            };
            if (!expectedType.IsNull)
            {
                await RequireObjectTypeAsync(context, group, expectedType, cancellationToken).ConfigureAwait(false);
            }
            await InspectTreeAsync(context, group, section, values, cancellationToken).ConfigureAwait(false);
        }

        private static async ValueTask InspectTreeAsync(
            CompanionContext context, NodeId root, string prefix, FieldBuffer values,
            CancellationToken cancellationToken)
        {
            var pending = new Queue<(NodeId Node, string Name, int Depth)>();
            pending.Enqueue((root, prefix, 0));
            var visited = new HashSet<NodeId> { root };
            var budget = new IndustrialBrowseBudget(context);
            int initialCount = values.Count;
            while (pending.TryDequeue(out (NodeId Node, string Name, int Depth) current))
            {
                await foreach (ReferenceDescription child in IndustrialCompanionAccess.BrowseAsync(
                    context, current.Node, BrowseDirection.Forward, ReferenceTypeIds.HierarchicalReferences,
                    NodeClass.Object | NodeClass.Variable, budget, cancellationToken).ConfigureAwait(false))
                {
                    NodeId node = RequireLocal(context, child.NodeId);
                    if (!visited.Add(node))
                    {
                        continue;
                    }
                    CellCompanionSupport.CheckCount(visited.Count, context.MaxFields, "Weighing section nodes");
                    string name = child.BrowseName.Name ?? child.DisplayName.Text ?? node.ToString();
                    RequireText(name, 256, "Weighing field name");
                    string path = current.Name + " / " + name;
                    if (child.NodeClass == NodeClass.Variable)
                    {
                        ArrayOf<DataValue> read = await ReadAsync(context,
                            [new ReadValueId { NodeId = node, AttributeId = Attributes.Value }],
                            cancellationToken).ConfigureAwait(false);
                        var field = new Field(new FieldSpec(name, ExpectedType(name)), node, read[0]);
                        values.Add(field.Present(path, DisplayValue(context, field.Value), DescribeField(field)));
                    }
                    else if (child.NodeClass == NodeClass.Object)
                    {
                        if (current.Depth >= 4)
                        {
                            throw new ServiceResultException(StatusCodes.BadEncodingLimitsExceeded,
                                "The weighing section exceeds four nested object levels.");
                        }
                        // File transfer and PackML observation belong to the existing shared tools.
                        if (name is not ("RecipeFile" or "State" or "SystemState" or "Lock"))
                        {
                            pending.Enqueue((node, path, current.Depth + 1));
                        }
                    }
                }
            }
            if (values.Count == initialCount)
            {
                values.Add(new CompanionValue(prefix, Variant.From("Exposed, with no readable child variables.")));
            }
        }

        private static ArrayOf<FieldSpec> KindFields(ScaleEntry scale)
        {
            var fields = new List<FieldSpec>
            {
                new(Scales.BrowseNames.ProcessStateId, BuiltInType.String),
                new(Scales.BrowseNames.ProcessStateMessage, BuiltInType.LocalizedText)
            };
            if (scale.Kind is ScaleKind.Continuous or ScaleKind.LossInWeight)
            {
                fields.AddRange(
                [
                    new(Scales.BrowseNames.FlowRate), new(Scales.BrowseNames.Load),
                    new(Scales.BrowseNames.Speed), new(Scales.BrowseNames.TargetFlowRate),
                    new(Scales.BrowseNames.MinFlowRate), new(Scales.BrowseNames.MaxFlowRate),
                    new(Scales.BrowseNames.RateControlMode, BuiltInType.Int32)
                ]);
            }
            if (scale.Kind == ScaleKind.PieceCounting)
            {
                fields.Add(new FieldSpec(Scales.BrowseNames.CurrentPieceCount));
                fields.Add(new FieldSpec(Scales.BrowseNames.ReferenceOptimisationRange));
            }
            return [.. fields];
        }

        private static BuiltInType ExpectedType(string name)
        {
            return name switch
            {
                "ProductId" or "RecipeId" or "BatchId" or "JobId" or "ResetCondition" => BuiltInType.String,
                "ProductName" or "RecipeName" or "BatchName" or "JobName" => BuiltInType.LocalizedText,
                "ProductMode" or "Weighed" => BuiltInType.Boolean,
                "StartTime" => BuiltInType.DateTime,
                _ => BuiltInType.Null
            };
        }

        private static async ValueTask RequireObjectTypeAsync(
            CompanionContext context, NodeId node, ExpandedNodeId expectedType, CancellationToken cancellationToken)
        {
            NodeId actual = await ReadTypeAsync(context, node, cancellationToken).ConfigureAwait(false);
            if (await IndustrialCompanionAccess.ClassifyAsync(context, new ExpandedNodeId(actual),
                [new(expectedType, "expected")], new IndustrialBrowseBudget(context), cancellationToken)
                .ConfigureAwait(false) is null)
            {
                throw new ServiceResultException(StatusCodes.BadTypeMismatch,
                    "The weighing child does not have the expected OPC 40200 type.");
            }
        }

        private static NodeId RequireLocal(CompanionContext context, ExpandedNodeId expanded)
        {
            NodeId id = IndustrialCompanionAccess.LocalId(context, expanded);
            if (id.IsNull)
            {
                throw new ServiceResultException(StatusCodes.BadNodeIdInvalid, "No local weighing node identity.");
            }
            return id;
        }

        private static Variant DisplayValue(CompanionContext context, Variant value)
        {
            if (value.IsNull)
            {
                return value;
            }
            if (value.TryGetValue(out string? text))
            {
                RequireText(text, 4096, "Weighing text", allowEmpty: true);
            }
            if (value.TryGetValue(out LocalizedText localized))
            {
                RequireText(localized.Text, 4096, "Weighing text", allowEmpty: true);
            }
            if (value.TryGetValue(out ByteString bytes))
            {
                CellCompanionSupport.CheckCount(bytes.Length, 4096, "Weighing byte string");
            }
            if (value.TryGetValue(out ArrayOf<string> strings))
            {
                CellCompanionSupport.CheckCount(strings.Count, kMaxItems, "Weighing string array");
                foreach (string item in strings)
                {
                    RequireText(item, 256, "Weighing identifier", allowEmpty: true);
                }
                return value;
            }
            if (value.TryGetValue(out ArrayOf<ExtensionObject> structures))
            {
                CellCompanionSupport.CheckCount(structures.Count, kMaxItems, "Weighing structure array");
                var descriptions = new List<string>();
                foreach (ExtensionObject structure in structures)
                {
                    if (Variant.From(structure).TryGetValue<EUInformation>(
                        out EUInformation? unit, context.Session.MessageContext) &&
                        unit is not null)
                    {
                        descriptions.Add(UnitText(unit));
                    }
                    else
                    {
                        descriptions.Add("Structured value; use the existing typed value editor to inspect");
                    }
                }
                ArrayOf<string> result = [.. descriptions];
                return Variant.From(result);
            }
            if (value.TypeInfo.ValueRank != ValueRanks.Scalar)
            {
                return Variant.From("Non-scalar value not materialized by this bounded section.");
            }
            if (value.TypeInfo.BuiltInType == BuiltInType.ExtensionObject)
            {
                if (value.TryGetValue<EUInformation>(out EUInformation? unit, context.Session.MessageContext) &&
                    unit is not null)
                {
                    return Variant.From(UnitText(unit));
                }
                if (value.TryGetValue<Opc.Ua.Range>(out Opc.Ua.Range? range, context.Session.MessageContext) &&
                    range is not null)
                {
                    return Variant.From(string.Create(CultureInfo.InvariantCulture, $"{range.Low} .. {range.High}"));
                }
                if (value.TryGetValue<WeightType>(out WeightType? weight, context.Session.MessageContext) &&
                    weight is not null)
                {
                    return Variant.From(string.Create(CultureInfo.InvariantCulture,
                        $"Gross {weight.Gross}; Net {weight.Net}; Tare {weight.Tare}"));
                }
                return Variant.From("Unsupported structured value; no default measurement substituted.");
            }
            return value;
        }

        private static string DescribeUnit(CompanionContext context, Field field)
        {
            if (field.IsAbsent)
            {
                return "Engineering units not exposed";
            }
            if (!StatusCode.IsGood(field.Status))
            {
                return "Engineering units: " + field.Status;
            }
            return field.Value.TryGetValue<EUInformation>(out EUInformation? unit, context.Session.MessageContext) &&
                unit is not null
                ? UnitText(unit)
                : "Engineering units have the wrong type";
        }

        private static string UnitText(EUInformation unit)
        {
            RequireText(unit.NamespaceUri, 2048, "Unit namespace", allowEmpty: true);
            RequireText(unit.DisplayName.Text, 256, "Unit display name", allowEmpty: true);
            return $"{unit.DisplayName.Text} ({unit.NamespaceUri}, {unit.UnitId})";
        }

        private static string DescribeRange(CompanionContext context, Field field)
        {
            if (field.IsAbsent)
            {
                return "Range not exposed";
            }
            if (!StatusCode.IsGood(field.Status))
            {
                return "Range: " + field.Status;
            }
            if (!field.Value.TryGetValue<Opc.Ua.Range>(
                out Opc.Ua.Range? range, context.Session.MessageContext) ||
                range is null)
            {
                return "Range has the wrong type";
            }
            return string.Create(CultureInfo.InvariantCulture, $"Range {range.Low} .. {range.High}") +
                (!double.IsFinite(range.Low) || !double.IsFinite(range.High) || range.High < range.Low
                    ? " (invalid; commands unavailable)" : string.Empty);
        }

        private static string DescribeField(Field field)
        {
            if (field.IsAbsent)
            {
                return "Not exposed";
            }
            BuiltInType expected = field.Specification.ExpectedType;
            bool wrongType = expected != BuiltInType.Null &&
                (field.Value.TypeInfo.BuiltInType != expected ||
                    field.Value.TypeInfo.ValueRank != (field.Specification.IsArray
                        ? ValueRanks.OneDimension : ValueRanks.Scalar));
            return wrongType ? "Expected " + expected + "; no default substituted" : string.Empty;
        }

        private static string FiniteNote(double value)
        {
            return double.IsFinite(value) ? string.Empty : "; non-finite server value; commands unavailable";
        }

        private static CompanionValue Absent(string name, string? description = null)
        {
            return new CompanionValue(name, Variant.Null)
            {
                Quality = StatusCodes.BadNotFound,
                EngineeringMetadata = description ?? "Not exposed"
            };
        }

        private static void RequireText(string? value, int maximum, string description, bool allowEmpty = false)
        {
            if ((!allowEmpty && string.IsNullOrWhiteSpace(value)) || (value is not null && value.Length > maximum))
            {
                throw new ServiceResultException(StatusCodes.BadInvalidArgument,
                    $"{description} must contain {(allowEmpty ? 0 : 1)}–{maximum} characters.");
            }
        }

        private static void RequireHeader(ResponseHeader? header)
        {
            if (header is null)
            {
                throw new ServiceResultException(StatusCodes.BadDecodingError, "Missing weighing response header.");
            }
            RequireGood(header.ServiceResult, "Weighing service");
        }

        private static void RequireGood(StatusCode status, string description)
        {
            if (!StatusCode.IsGood(status))
            {
                throw new ServiceResultException(status, description + " is not reliable.");
            }
        }

        private static readonly ArrayOf<FieldSpec> s_weightFields =
        [
            new(string.Empty, BuiltInType.ExtensionObject),
            new(Scales.BrowseNames.Overload, BuiltInType.Boolean),
            new(Scales.BrowseNames.Underload, BuiltInType.Boolean),
            new(Scales.BrowseNames.TareMode, BuiltInType.Int32),
            new(Scales.BrowseNames.WeightStable, BuiltInType.Boolean),
            new(Scales.BrowseNames.InsideZero, BuiltInType.Boolean),
            new(Scales.BrowseNames.CurrentRangeId, BuiltInType.UInt16),
            new(Scales.BrowseNames.WeightId, BuiltInType.String),
            new(Opc.Ua.BrowseNames.EngineeringUnits, BuiltInType.ExtensionObject, Opc.Ua.Namespaces.OpcUa),
            new(Opc.Ua.BrowseNames.EURange, BuiltInType.ExtensionObject, Opc.Ua.Namespaces.OpcUa)
        ];

        private sealed record FieldSpec(
            string Name, BuiltInType ExpectedType = BuiltInType.Null,
            string? NamespaceUri = null, bool IsArray = false);

        private sealed record Field(FieldSpec Specification, NodeId NodeId, DataValue Data)
        {
            public Variant Value => Data.WrappedValue;

            public StatusCode Status => Data.StatusCode;

            public bool IsAbsent => NodeId.IsNull && Status == StatusCodes.BadNotFound;

            public CompanionValue Present(string name, Variant value, string? metadata = null)
            {
                var timestamp = (DateTime)Data.SourceTimestamp;
                return new CompanionValue(name, value)
                {
                    SourceNodeId = NodeId,
                    Quality = Status,
                    SourceTimestamp = timestamp == DateTime.MinValue ? null : timestamp,
                    EngineeringMetadata = metadata
                };
            }
        }

        private sealed class FieldBuffer(int maximum)
        {
            public int Count => m_values.Count;

            public void Add(CompanionValue value)
            {
                CellCompanionSupport.CheckCount(m_values.Count + 1, maximum, "Weighing inspection fields");
                m_values.Add(value);
            }

            public ArrayOf<CompanionValue> ToArray()
            {
                return [.. m_values];
            }

            private readonly List<CompanionValue> m_values = [];
        }
    }
}
