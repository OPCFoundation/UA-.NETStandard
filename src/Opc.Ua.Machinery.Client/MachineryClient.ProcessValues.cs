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
        /// than by browse path: every object below the item whose type is
        /// <c>ProcessValueType</c> or a subtype counts.
        /// </remarks>
        /// <param name="machineryItem">The machine or component to inspect.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async IAsyncEnumerable<MachineEntry> EnumerateProcessValuesAsync(
            NodeId machineryItem,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            int namespaceIndex = Session.NamespaceUris.GetIndex(
                Opc.Ua.Machinery.ProcessValues.Namespaces.MachineryProcessValues);
            if (namespaceIndex < 0)
            {
                yield break;
            }
            var processValueTypeId = new NodeId(
                Opc.Ua.Machinery.ProcessValues.ObjectTypes.ProcessValueType,
                (ushort)namespaceIndex);

            await foreach (MachineEntry entry in EnumerateChildObjectsAsync(
                    machineryItem,
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
        public async ValueTask<MachineryProcessValue?> ReadProcessValueAsync(
            NodeId processValue,
            CancellationToken cancellationToken = default)
        {
            if (processValue.IsNull)
            {
                return null;
            }
            ushort padimNamespaceIndex = NamespaceIndexOf(Opc.Ua.PADIM.Namespaces.PADIM);
            ushort processValueNamespaceIndex = NamespaceIndexOf(
                Opc.Ua.Machinery.ProcessValues.Namespaces.MachineryProcessValues);

            var signalName = new QualifiedName(
                PadimBrowseNames.AnalogSignal,
                padimNamespaceIndex);
            NodeId signal = await ResolveChildAsync(processValue, signalName, cancellationToken)
                .ConfigureAwait(false);
            if (signal.IsNull)
            {
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

            Dictionary<QualifiedName, Variant> objectValues = await ReadChildValuesAsync(
                processValue,
                [signalName, setpointName, statusName, suppressionName],
                Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                cancellationToken).ConfigureAwait(false);

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
            var unitsName = new QualifiedName(Opc.Ua.BrowseNames.EngineeringUnits);
            var rangeName = new QualifiedName(Opc.Ua.BrowseNames.EURange);

            Dictionary<QualifiedName, Variant> signalValues = await ReadChildValuesAsync(
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
                Opc.Ua.ReferenceTypeIds.HierarchicalReferences,
                cancellationToken).ConfigureAwait(false);

            return new MachineryProcessValue
            {
                NodeId = processValue,
                SignalNodeId = signal,
                Value = AsDouble(objectValues, signalName),
                Setpoint = AsDouble(objectValues, setpointName),
                Status = AsUInt16(objectValues, statusName),
                AlarmSuppression = AsUInt16(objectValues, suppressionName),
                LowLowLimit = AsDouble(signalValues, lowLowName),
                LowLimit = AsDouble(signalValues, lowName),
                HighLimit = AsDouble(signalValues, highName),
                HighHighLimit = AsDouble(signalValues, highHighName),
                PercentageValue = AsDouble(signalValues, percentageName),
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
            ushort padimNamespaceIndex = NamespaceIndexOf(Opc.Ua.PADIM.Namespaces.PADIM);
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

        private static ushort? AsUInt16(
            Dictionary<QualifiedName, Variant> values,
            QualifiedName name)
        {
            return values.TryGetValue(name, out Variant value) &&
                value.TryGetValue(out ushort number)
                ? number
                : null;
        }

        /// <summary>
        /// Decodes a structure read from the wire. The context-taking overload
        /// is the one that works for a value that arrived as a binary
        /// <see cref="ExtensionObject"/>, which is what a server sends unless
        /// the session already knows the type.
        /// </summary>
        private EUInformation? AsEngineeringUnits(
            Dictionary<QualifiedName, Variant> values,
            QualifiedName name)
        {
            return values.TryGetValue(name, out Variant value) &&
                value.TryGetStructure<EUInformation>(Session.MessageContext, out EUInformation? units)
                ? units
                : null;
        }

        private Opc.Ua.Range? AsRange(
            Dictionary<QualifiedName, Variant> values,
            QualifiedName name)
        {
            return values.TryGetValue(name, out Variant value) &&
                value.TryGetStructure<Opc.Ua.Range>(Session.MessageContext, out Opc.Ua.Range? range)
                ? range
                : null;
        }
    }
}
