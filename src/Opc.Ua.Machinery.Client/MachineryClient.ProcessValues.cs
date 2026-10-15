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

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using MachineryBrowseNames = Opc.Ua.Machinery.BrowseNames;
using PadimBrowseNames = Opc.Ua.PADIM.BrowseNames;
using ProcessValueBrowseNames = Opc.Ua.Machinery.ProcessValues.BrowseNames;

namespace Opc.Ua.Machinery.Client
{
    public sealed partial class MachineryClient
    {
        /// <summary>
        /// Enumerates the OPC 40001-2 process values a machinery item
        /// publishes.
        /// </summary>
        /// <remarks>
        /// OPC 40001-2 does not fix where process values hang — the examples
        /// put them on a sensor component, on the machine, and below
        /// <c>Monitoring</c> — so they are found by type definition rather
        /// than by browse path: every object directly below the item, or
        /// directly below its <c>Monitoring</c> object, whose type is
        /// <c>ProcessValueType</c> or a subtype counts. The process values of
        /// a component are found by passing the component.
        /// </remarks>
        /// <param name="machineryItem">The machine or component to inspect.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async IAsyncEnumerable<MachineEntry> EnumerateProcessValuesAsync(
            NodeId machineryItem,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (!TryGetNamespaceIndex(
                ProcessValues.Namespaces.MachineryProcessValues,
                out ushort namespaceIndex))
            {
                yield break;
            }
            var processValueTypeId = new NodeId(
                ProcessValues.ObjectTypes.ProcessValueType,
                namespaceIndex);

            // A process value below the item may also be organized by or be
            // an event source of its Monitoring, so it is reported once.
            var seen = new HashSet<NodeId>();
            BrowseBudget? budget = m_options is null ? null : new BrowseBudget(m_options);
            await foreach (MachineEntry entry in EnumerateProcessValuesBelowAsync(
                    machineryItem,
                    processValueTypeId,
                    budget,
                    cancellationToken).ConfigureAwait(false))
            {
                seen.Add(entry.NodeId);
                yield return entry;
            }

            if (!TryGetNamespaceIndex(
                Namespaces.Machinery,
                out ushort machineryNamespaceIndex))
            {
                yield break;
            }
            NodeId monitoring = await ResolveChildAsync(
                machineryItem,
                new QualifiedName(MachineryBrowseNames.Monitoring, machineryNamespaceIndex),
                cancellationToken).ConfigureAwait(false);
            if (monitoring.IsNull)
            {
                yield break;
            }
            await foreach (MachineEntry entry in EnumerateProcessValuesBelowAsync(
                    monitoring,
                    processValueTypeId,
                    budget,
                    cancellationToken).ConfigureAwait(false))
            {
                if (seen.Add(entry.NodeId))
                {
                    yield return entry;
                }
            }
        }

        /// <summary>
        /// Reads one OPC 40001-2 process value with its metadata, limits,
        /// setpoint and status.
        /// </summary>
        /// <remarks>
        /// Everything but the signal itself is optional in the part, so a
        /// property the server does not publish comes back as
        /// <see langword="null"/> rather than as an error.
        /// </remarks>
        /// <param name="processValue">The process value to read.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <exception cref="MachineryReadException"></exception>
        public async ValueTask<MachineryProcessValue?> ReadProcessValueAsync(
            NodeId processValue,
            CancellationToken cancellationToken = default)
        {
            if (processValue.IsNull)
            {
                return null;
            }
            if (m_options is not null &&
                !TryGetNamespaceIndex(
                    ProcessValues.Namespaces.MachineryProcessValues, out _))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return null;
            }
            ushort padimNamespaceIndex = NamespaceIndexOf(PADIM.Namespaces.PADIM);
            ushort processValueNamespaceIndex = NamespaceIndexOf(
                ProcessValues.Namespaces.MachineryProcessValues);

            var signalName = new QualifiedName(
                PadimBrowseNames.AnalogSignal,
                padimNamespaceIndex);
            NodeId signal = await ResolveChildAsync(processValue, signalName, cancellationToken)
                .ConfigureAwait(false);
            if (signal.IsNull)
            {
                if (m_options is not null)
                {
                    throw ReadFailure(
                        StatusCodes.BadNoMatch,
                        UnresolvedField(processValue, [signalName], StatusCodes.BadNoMatch, isAbsent: true),
                        "A process value must publish AnalogSignal.");
                }
                return null;
            }

            var setpointName = new QualifiedName(
                ProcessValueBrowseNames.ProcessValueSetpoint,
                processValueNamespaceIndex);
            var statusName = new QualifiedName(
                ProcessValueBrowseNames.Status,
                processValueNamespaceIndex);
            var suppressionName = new QualifiedName(
                ProcessValueBrowseNames.AlarmSuppression,
                processValueNamespaceIndex);

            var samples = new Dictionary<QualifiedName, DataValue>();
            ReadValueSet objectValues = await ReadChildValuesAsync(
                processValue,
                [signalName, setpointName, statusName, suppressionName],
                ReferenceTypeIds.HierarchicalReferences,
                cancellationToken,
                samples).ConfigureAwait(false);
            objectValues.RequirePresent(signalName);
            if (!samples.TryGetValue(signalName, out DataValue sample))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotFound,
                    "The process value's AnalogSignal disappeared while reading its sample.");
            }

            var lowLowName = new QualifiedName(
                ProcessValueBrowseNames.LowLowLimit,
                processValueNamespaceIndex);
            var lowName = new QualifiedName(
                ProcessValueBrowseNames.LowLimit,
                processValueNamespaceIndex);
            var highName = new QualifiedName(
                ProcessValueBrowseNames.HighLimit,
                processValueNamespaceIndex);
            var highHighName = new QualifiedName(
                ProcessValueBrowseNames.HighHighLimit,
                processValueNamespaceIndex);
            var percentageName = new QualifiedName(
                ProcessValueBrowseNames.PercentageValue,
                processValueNamespaceIndex);
            var unitsName = new QualifiedName(Ua.BrowseNames.EngineeringUnits);
            var rangeName = new QualifiedName(Ua.BrowseNames.EURange);

            ReadValueSet signalValues = await ReadChildValuesAsync(
                signal,
                [
                    lowLowName,
                    lowName,
                    highName,
                    highHighName,
                    percentageName,
                    unitsName,
                    rangeName
                ],
                ReferenceTypeIds.HierarchicalReferences,
                cancellationToken).ConfigureAwait(false);

            return new MachineryProcessValue
            {
                Values = CombineEvidence(objectValues.Values, signalValues.Values),
                NodeId = processValue,
                SignalNodeId = signal,
                Sample = sample,
                Value = AsProcessNumber(objectValues, signalName, allowArray: true),
                Setpoint = AsProcessNumber(objectValues, setpointName, allowArray: true),
                Status = AsUInt16(objectValues, statusName),
                AlarmSuppression = AsUInt16(objectValues, suppressionName),
                LowLowLimit = AsProcessNumber(signalValues, lowLowName),
                LowLimit = AsProcessNumber(signalValues, lowName),
                HighLimit = AsProcessNumber(signalValues, highName),
                HighHighLimit = AsProcessNumber(signalValues, highHighName),
                PercentageValue = AsPercentage(signalValues, percentageName),
                EngineeringUnits = AsEngineeringUnits(signalValues, unitsName),
                EuRange = AsRange(signalValues, rangeName)
            };
        }

        /// <summary>
        /// Calls the OPC 40001-2 <c>ZeroPointAdjustment</c> method of a
        /// process value.
        /// </summary>
        /// <remarks>
        /// A conformant server reports a <c>ZeroPointAdjustmentEventType</c>
        /// event for every call; subscribe to the process value to see it.
        /// </remarks>
        /// <param name="processValue">The process value to adjust.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <returns>The service result of the call.</returns>
        /// <exception cref="ServiceResultException">
        /// The process value publishes no <c>ZeroPointAdjustment</c> method.
        /// </exception>
        public async ValueTask<StatusCode> ZeroPointAdjustmentAsync(
            NodeId processValue,
            CancellationToken cancellationToken = default)
        {
            ushort padimNamespaceIndex = NamespaceIndexOf(PADIM.Namespaces.PADIM);
            NodeId method = await ResolveChildAsync(
                processValue,
                new QualifiedName(
                    PadimBrowseNames.ZeroPointAdjustment,
                    padimNamespaceIndex),
                cancellationToken).ConfigureAwait(false);
            if (method.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadNotFound,
                    "The process value publishes no ZeroPointAdjustment method.");
            }

            CallResponse response = await Session.CallAsync(
                requestHeader: null,
                methodsToCall: new[]
                {
                    new CallMethodRequest
                    {
                        ObjectId = processValue,
                        MethodId = method,
                        InputArguments = []
                    }
                }.ToArrayOf(),
                ct: cancellationToken).ConfigureAwait(false);

            return response.Results.Count == 0
                ? StatusCodes.BadUnexpectedError
                : response.Results[0].StatusCode;
        }

        private async IAsyncEnumerable<MachineEntry> EnumerateProcessValuesBelowAsync(
            NodeId parent,
            NodeId processValueTypeId,
            BrowseBudget? budget,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (budget is not null)
            {
                ArrayOf<MachineEntry> entries = await BrowseBoundedAsync(
                    parent, NodeClass.Object, ReferenceTypeIds.HierarchicalReferences, budget, cancellationToken)
                    .ConfigureAwait(false);
                for (int ii = 0; ii < entries.Count; ii++)
                {
                    MachineEntry entry = entries[ii];
                    if (await Session.NodeCache.IsTypeOfAsync(
                        entry.TypeDefinitionId, processValueTypeId, cancellationToken).ConfigureAwait(false))
                    {
                        yield return entry;
                    }
                }
                yield break;
            }
            await foreach (MachineEntry entry in EnumerateChildObjectsAsync(
                    parent,
                    cancellationToken).ConfigureAwait(false))
            {
                // A vendor subtype of ProcessValueType is a process value too.
                if (await Session.NodeCache
                    .IsTypeOfAsync(entry.TypeDefinitionId, processValueTypeId, cancellationToken)
                    .ConfigureAwait(false))
                {
                    yield return entry;
                }
            }
        }

        private static ushort? AsUInt16(
            ReadValueSet values,
            QualifiedName name)
        {
            values.TryGetValue(name, out Variant value);
            bool valid = value.TryGetValue(out ushort number);
            values.RequireType(name, valid && HasType(value, BuiltInType.UInt16));
            return valid ? number : null;
        }

        private double? AsProcessNumber(ReadValueSet values, QualifiedName name, bool allowArray = false)
        {
            if (m_options is null)
            {
                return AsDouble(values, name);
            }
            values.TryGetValue(name, out Variant value);
            bool numeric = value.TypeInfo.BuiltInType is
                BuiltInType.SByte or BuiltInType.Byte or BuiltInType.Int16 or BuiltInType.UInt16 or
                BuiltInType.Int32 or BuiltInType.UInt32 or BuiltInType.Int64 or BuiltInType.UInt64 or
                BuiltInType.Float or BuiltInType.Double;
            values.RequireType(name, numeric && (allowArray || value.TypeInfo.ValueRank == ValueRanks.Scalar));
            if (value.TypeInfo.ValueRank >= ValueRanks.OneDimension)
            {
                ValidateVariantArrays(
                    value, new ReadArrayBudget(m_options, values.EvidenceOf(name)[0]), CancellationToken.None);
                // AnalogSignal and Setpoint allow Number arrays. Their
                // scalar convenience projection is null; Values retains the array.
                return null;
            }
            if (value.TryGetValue(out double number))
            {
                return number;
            }
            if (value.TryGetValue(out float single))
            {
                return single;
            }
            return value.TryGetDecimal(out decimal integer) ? (double)integer : null;
        }

        private static double? AsPercentage(ReadValueSet values, QualifiedName name)
        {
            values.TryGetValue(name, out Variant value);
            values.RequireType(name, HasType(value, BuiltInType.Double));
            return AsDouble(values, name);
        }

        /// <summary>
        /// Decodes a structure read from the wire. The context-taking overload
        /// is the one that works for a value that arrived as a binary
        /// <see cref="ExtensionObject"/>, which is what a server sends unless
        /// the session already knows the type.
        /// </summary>
        private EUInformation? AsEngineeringUnits(
            ReadValueSet values,
            QualifiedName name)
        {
            values.TryGetValue(name, out Variant value);
            if (m_options is not null)
            {
                return DecodeStructure<EUInformation>(value, values.EvidenceOf(name)[0]);
            }
            bool valid = value.TryGetStructure<EUInformation>(Session.MessageContext, out EUInformation? units);
            values.RequireType(name, valid);
            return valid ? units : null;
        }

        private Range? AsRange(
            ReadValueSet values,
            QualifiedName name)
        {
            values.TryGetValue(name, out Variant value);
            if (m_options is not null)
            {
                return DecodeStructure<Range>(value, values.EvidenceOf(name)[0]);
            }
            bool valid = value.TryGetStructure<Range>(Session.MessageContext, out Range? range);
            values.RequireType(name, valid);
            return valid ? range : null;
        }
    }
}
