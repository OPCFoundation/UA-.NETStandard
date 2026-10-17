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
using Opc.Ua.Machinery.Jobs;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.Machinery.Client
{
    public sealed partial class MachineryClient
    {
        private ArrayOf<T> DecodeStructureArray<T>(MachineryReadValue field, ReadArrayBudget budget)
            where T : class, IEncodeable, new()
        {
            return field.IsAbsent ? [] : DecodeStructureArray<T>(field.Value.WrappedValue, budget);
        }

        private ArrayOf<T> DecodeStructureArray<T>(Variant value, ReadArrayBudget budget)
            where T : class, IEncodeable, new()
        {
            if (value.IsNull)
            {
                return default;
            }
            if (!HasType(value, BuiltInType.ExtensionObject, ValueRanks.OneDimension) ||
                !value.TryGetValue(out ArrayOf<ExtensionObject> encoded))
            {
                throw ReadFailure(
                    StatusCodes.BadTypeMismatch, budget.Evidence, "A one-dimensional structure array is required.");
            }
            budget.Add(encoded.Count);
            if (encoded.IsNull)
            {
                return default;
            }
            var decoded = new T[encoded.Count];
            for (int ii = 0; ii < decoded.Length; ii++)
            {
                decoded[ii] = DecodeStructure<T>(Variant.From(encoded[ii]), budget.Evidence) ??
                    throw ReadFailure(
                        StatusCodes.BadDecodingError, budget.Evidence, "A structure-array element is null.");
            }
            return decoded.ToArrayOf();
        }

        private T? DecodeStructure<T>(Variant value, MachineryReadValue evidence)
            where T : class, IEncodeable, new()
        {
            if (value.IsNull || (value.TryGetValue(out ExtensionObject encoded) && encoded.IsNull))
            {
                return null;
            }
            if (value.TryGetValue(out ExtensionObject extension) &&
                extension.TryGetValue(out IEncodeable? body) &&
                !MatchesStructureIdentifier(extension.TypeId, body))
            {
                throw ReadFailure(
                    StatusCodes.BadTypeMismatch, evidence, "The structure identifier does not match its body.");
            }
            try
            {
                if (HasType(value, BuiltInType.ExtensionObject) &&
                    extension.TryGetValue(out IEncodeable? decoded, Session.MessageContext) &&
                    decoded is T typed)
                {
                    return typed;
                }
            }
            catch (ServiceResultException exception)
            {
                throw ReadFailure(exception.StatusCode, evidence, exception.Message, exception);
            }
            if (value.TryGetValue(out extension) && MatchesStructureIdentifier(extension.TypeId, new T()))
            {
                // The shared Try decoder deliberately hides decoding exceptions.
                // A known type with an undecodable body is malformed, not absent.
                throw ReadFailure(
                    StatusCodes.BadDecodingError, evidence, "The known structure body could not be decoded.");
            }
            throw ReadFailure(StatusCodes.BadTypeMismatch, evidence, "The structure has an incompatible wire type.");
        }

        private bool MatchesStructureIdentifier(ExpandedNodeId actual, IEncodeable expected)
        {
            return SameStructureIdentifier(actual, expected.TypeId) ||
                SameStructureIdentifier(actual, expected.BinaryEncodingId) ||
                SameStructureIdentifier(actual, expected.XmlEncodingId);
        }

        private bool SameStructureIdentifier(ExpandedNodeId actual, ExpandedNodeId expected)
        {
            if (actual.ServerIndex != 0 || actual.IsNull)
            {
                return false;
            }
            if (actual == expected)
            {
                return true;
            }
            var local = ExpandedNodeId.ToNodeId(actual, Session.NamespaceUris);
            return !local.IsNull && local == ExpandedNodeId.ToNodeId(expected, Session.NamespaceUris);
        }

        private void ValidateJobOrders(
            ArrayOf<V2.ISA95JobOrderAndStateDataType> orders,
            ReadArrayBudget budget,
            CancellationToken cancellationToken)
        {
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            foreach (V2.ISA95JobOrderAndStateDataType entry in orders)
            {
                cancellationToken.ThrowIfCancellationRequested();
                V2.ISA95JobOrderDataType order = entry.JobOrder ??
                    throw ReadFailure(StatusCodes.BadDecodingError, budget.Evidence, "A job order is missing.");
                RequireIdentifier(order.JobOrderID, identifiers, budget);
                budget.Add(order.Description.Count);
                ValidateStates(entry.State, budget);
                ValidateParameters(order.JobOrderParameters, budget, cancellationToken);
                budget.Add(order.WorkMasterID.Count);
                foreach (V2.ISA95WorkMasterDataType master in order.WorkMasterID)
                {
                    RequireElement(master, budget);
                    ValidateParameters(master.Parameters, budget, cancellationToken);
                }
                ValidateJobResources(
                    order.PersonnelRequirements, order.EquipmentRequirements,
                    order.PhysicalAssetRequirements, order.MaterialRequirements, budget, cancellationToken);
            }
        }

        private void ValidateJobResponses(
            ArrayOf<V2.ISA95JobResponseDataType> responses,
            ReadArrayBudget budget,
            CancellationToken cancellationToken)
        {
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            foreach (V2.ISA95JobResponseDataType response in responses)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RequireIdentifier(response.JobResponseID, identifiers, budget);
                if (string.IsNullOrEmpty(response.JobOrderID))
                {
                    throw ReadFailure(
                        StatusCodes.BadDecodingError, budget.Evidence, "A response's job order ID is missing.");
                }
                ValidateStates(response.JobState, budget);
                ValidateParameters(response.JobResponseData, budget, cancellationToken);
                ValidateJobResources(
                    response.PersonnelActuals, response.EquipmentActuals,
                    response.PhysicalAssetActuals, response.MaterialActuals, budget, cancellationToken);
            }
        }

        private static void ValidateStates(ArrayOf<V2.ISA95StateDataType> states, ReadArrayBudget budget)
        {
            budget.Add(states.Count);
            foreach (V2.ISA95StateDataType state in states)
            {
                RequireElement(state, budget);
                RequireElement(state.BrowsePath, budget);
                budget.Add(state.BrowsePath.Elements.Count);
                foreach (RelativePathElement element in state.BrowsePath.Elements)
                {
                    RequireElement(element, budget);
                }
            }
        }

        private void ValidateParameters(
            ArrayOf<V2.ISA95ParameterDataType> parameters,
            ReadArrayBudget budget,
            CancellationToken cancellationToken)
        {
            var pending = new Stack<ArrayOf<V2.ISA95ParameterDataType>>();
            budget.Add(parameters.Count);
            pending.Push(parameters);
            while (pending.Count != 0)
            {
                var identifiers = new HashSet<string>(StringComparer.Ordinal);
                foreach (V2.ISA95ParameterDataType parameter in pending.Pop())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    RequireElement(parameter, budget);
                    RequireIdentifier(parameter.ID, identifiers, budget);
                    budget.Add(parameter.Description.Count);
                    Variant value = parameter.Value;
                    bool valid = parameter.ID switch
                    {
                        "JobName" or "JobAnnotation" =>
                            HasType(value, BuiltInType.LocalizedText, ValueRanks.OneDimension),
                        "OrderNumbers" or "Customers" or "CustomerOrderNumbers" =>
                            HasType(value, BuiltInType.String, ValueRanks.OneDimension),
                        "JobExecutionMode" or "JobResult" =>
                            HasType(value, BuiltInType.Int32) || HasType(value, BuiltInType.Enumeration),
                        "ReasonForStateChange" => HasType(value, BuiltInType.LocalizedText),
                        "RunsPlanned" or "RunsCompleted" or "RunsStarted" => HasType(value, BuiltInType.UInt32),
                        "Overproduction" => HasType(value, BuiltInType.Boolean),
                        "PlannedProductionTime" or "PlannedSetupTime" or "PlannedTimePerRun" or
                        "PlannedQuantityPerRun" or "PlannedOrderQuantity" or "PlannedDuration" or
                        "ActualQuantityCurrentRun" or "ActualUnitBusyTime" or "ActualUnitSetupTime" or
                        "ActualUnitDelayTime" or "ActualProductionTime" or "ProducedQuantity" or
                        "EstimatedRemainingTime" or "GoodQuantity" => HasType(value, BuiltInType.Double),
                        _ => true
                    };
                    if (!value.IsNull && !valid)
                    {
                        throw ReadFailure(
                            StatusCodes.BadTypeMismatch, budget.Evidence,
                            $"The job parameter '{parameter.ID}' has an incompatible wire type.");
                    }
                    if (parameter.ID == "AsBuiltBOM")
                    {
                        foreach (BOMInformationDataType bom in
                            DecodeStructureArray<BOMInformationDataType>(value, budget))
                        {
                            RequireElement(bom.Identification, budget);
                            budget.Add(bom.ComponentInformation.Count);
                            foreach (BOMComponentInformationDataType component in bom.ComponentInformation)
                            {
                                RequireElement(component, budget);
                                RequireElement(component.Identification, budget);
                            }
                        }
                    }
                    else if (parameter.ID == "OutputPerformanceInfo")
                    {
                        foreach (OutputPerformanceInfoDataType performance in
                            DecodeStructureArray<OutputPerformanceInfoDataType>(value, budget))
                        {
                            RequireElement(performance.Identification, budget);
                            budget.Add(performance.Parameters.Count);
                            pending.Push(performance.Parameters);
                        }
                    }
                    else
                    {
                        ValidateVariantArrays(value, budget, cancellationToken);
                    }
                    budget.Add(parameter.Subparameters.Count);
                    if (parameter.Subparameters.Count != 0)
                    {
                        pending.Push(parameter.Subparameters);
                    }
                }
            }
        }

        private static void ValidateJobResources(
            ArrayOf<V2.ISA95PersonnelDataType> personnel,
            ArrayOf<V2.ISA95EquipmentDataType> equipment,
            ArrayOf<V2.ISA95PhysicalAssetDataType> assets,
            ArrayOf<V2.ISA95MaterialDataType> materials,
            ReadArrayBudget budget,
            CancellationToken cancellationToken)
        {
            ValidateResources(
                personnel, static p => p.Description, static p => p.Properties, budget, cancellationToken);
            ValidateResources(
                equipment, static p => p.Description, static p => p.Properties, budget, cancellationToken);
            ValidateResources(assets, static p => p.Description, static p => p.Properties, budget, cancellationToken);
            ValidateResources(
                materials, static p => p.Description, static p => p.Properties, budget, cancellationToken);
        }

        private static void ValidateResources<T>(
            ArrayOf<T> resources,
            Func<T, ArrayOf<LocalizedText>> descriptions,
            Func<T, ArrayOf<V2.ISA95PropertyDataType>> properties,
            ReadArrayBudget budget,
            CancellationToken cancellationToken) where T : class
        {
            budget.Add(resources.Count);
            var pending = new Stack<ArrayOf<V2.ISA95PropertyDataType>>();
            foreach (T resource in resources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                RequireElement(resource, budget);
                budget.Add(descriptions(resource).Count);
                ArrayOf<V2.ISA95PropertyDataType> fields = properties(resource);
                budget.Add(fields.Count);
                pending.Push(fields);
            }
            while (pending.Count != 0)
            {
                var identifiers = new HashSet<string>(StringComparer.Ordinal);
                foreach (V2.ISA95PropertyDataType property in pending.Pop())
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    RequireElement(property, budget);
                    RequireIdentifier(property.ID, identifiers, budget);
                    budget.Add(property.Description.Count);
                    ValidateVariantArrays(property.Value, budget, cancellationToken);
                    budget.Add(property.Subproperties.Count);
                    if (property.Subproperties.Count != 0)
                    {
                        pending.Push(property.Subproperties);
                    }
                }
            }
        }

        private static void ValidateVariantArrays(
            Variant value,
            ReadArrayBudget budget,
            CancellationToken cancellationToken)
        {
            var pending = new Stack<Variant>();
            pending.Push(value);
            while (pending.Count != 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Variant current = pending.Pop();
                if (current.TypeInfo.ValueRank < ValueRanks.OneDimension)
                {
                    if (current.TryGetValue(out DataValue dataValue))
                    {
                        budget.Add(1);
                        pending.Push(dataValue.WrappedValue);
                    }
                    continue;
                }
                int count = current.TypeInfo.BuiltInType switch
                {
                    BuiltInType.Boolean => ArrayCount<bool>(current),
                    BuiltInType.SByte => ArrayCount<sbyte>(current),
                    BuiltInType.Byte => ArrayCount<byte>(current),
                    BuiltInType.Int16 => ArrayCount<short>(current),
                    BuiltInType.UInt16 => ArrayCount<ushort>(current),
                    BuiltInType.Int32 => ArrayCount<int>(current),
                    BuiltInType.UInt32 => ArrayCount<uint>(current),
                    BuiltInType.Int64 => ArrayCount<long>(current),
                    BuiltInType.UInt64 => ArrayCount<ulong>(current),
                    BuiltInType.Float => ArrayCount<float>(current),
                    BuiltInType.Double => ArrayCount<double>(current),
                    BuiltInType.String => ArrayCount<string>(current),
                    BuiltInType.DateTime => ArrayCount<DateTimeUtc>(current),
                    BuiltInType.Guid => ArrayCount<Uuid>(current),
                    BuiltInType.ByteString => ArrayCount<ByteString>(current),
                    BuiltInType.XmlElement => ArrayCount<XmlElement>(current),
                    BuiltInType.NodeId => ArrayCount<NodeId>(current),
                    BuiltInType.ExpandedNodeId => ArrayCount<ExpandedNodeId>(current),
                    BuiltInType.StatusCode => ArrayCount<StatusCode>(current),
                    BuiltInType.QualifiedName => ArrayCount<QualifiedName>(current),
                    BuiltInType.LocalizedText => ArrayCount<LocalizedText>(current),
                    BuiltInType.ExtensionObject => ArrayCount<ExtensionObject>(current),
                    BuiltInType.DataValue => ArrayCount<DataValue>(current),
                    BuiltInType.Variant => ArrayCount<Variant>(current),
                    BuiltInType.Enumeration => ArrayCount<EnumValue>(current),
                    _ => -1
                };
                if (count < 0)
                {
                    throw ReadFailure(StatusCodes.BadDecodingError, budget.Evidence, "A value array is malformed.");
                }
                budget.Add(count);
                if (current.TryGetValue(out MatrixOf<Variant> children))
                {
                    foreach (Variant child in children)
                    {
                        pending.Push(child);
                    }
                }
                else if (current.TryGetValue(out MatrixOf<DataValue> dataValues))
                {
                    foreach (DataValue dataValue in dataValues)
                    {
                        pending.Push(dataValue.WrappedValue);
                    }
                }
            }
        }

        private static int ArrayCount<T>(Variant value)
        {
            if (value.TryGetArray(out ArrayOf<T> array, value.TypeInfo.BuiltInType))
            {
                return array.Count;
            }
            return value.TryGetMatrix(out MatrixOf<T> matrix, value.TypeInfo.BuiltInType) ? matrix.Count : -1;
        }

        private static void RequireElement<T>(T? element, ReadArrayBudget budget) where T : class
        {
            if (element is null)
            {
                throw ReadFailure(
                    StatusCodes.BadDecodingError, budget.Evidence, "A modeled structure element is null.");
            }
        }

        private static void RequireIdentifier(string? id, HashSet<string> identifiers, ReadArrayBudget budget)
        {
            if (string.IsNullOrEmpty(id) || !identifiers.Add(id))
            {
                throw ReadFailure(
                    StatusCodes.BadDecodingError, budget.Evidence, "A modeled identifier is missing or repeated.");
            }
        }

        /// <summary>
        /// Bounds modeled array items before copying or walking a structured read.
        /// Opaque vendor ExtensionObject bodies and transport decoding use session quotas.
        /// </summary>
        private sealed class ReadArrayBudget(MachineryClientOptions options, MachineryReadValue evidence)
        {
            /// <summary>
            /// Gets the original source value used for any structural failure.
            /// </summary>
            public MachineryReadValue Evidence { get; } = evidence;

            /// <summary>
            /// Reserves modeled items, including nested arrays, before traversing them.
            /// </summary>
            /// <exception cref="MachineryReadException"></exception>
            public void Add(int count)
            {
                if (count > options.MaximumReadArrayItems - m_count)
                {
                    throw ReadFailure(
                        StatusCodes.BadEncodingLimitsExceeded, Evidence,
                        "The Machinery structured read limit was exceeded.");
                }
                m_count += count;
            }

            private int m_count;
        }
    }
}
