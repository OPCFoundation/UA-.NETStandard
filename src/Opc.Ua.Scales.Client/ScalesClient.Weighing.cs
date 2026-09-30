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
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Opc.Ua.Client;
using Opc.Ua.Client.Subscriptions;
using Opc.Ua.Client.Subscriptions.Streaming;
using DiBrowseNames = Opc.Ua.Di.BrowseNames;
using MachineryBrowseNames = Opc.Ua.Machinery.BrowseNames;
using MonitoringOptions = Opc.Ua.Client.Subscriptions.MonitoredItems.MonitoredItemOptions;

namespace Opc.Ua.Scales.Client
{
    public sealed partial class ScalesClient
    {
        /// <summary>
        /// Reads a scale's <c>CurrentWeight</c> with every property it
        /// publishes.
        /// </summary>
        /// <param name="scale">The scale.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <exception cref="ServiceResultException">
        /// <c>Bad_NotFound</c>: the node publishes no <c>CurrentWeight</c> (a
        /// scale system or a module). <see cref="TryReadCurrentWeightAsync"/>
        /// returns null instead.
        /// </exception>
        public async ValueTask<ScaleReading> ReadCurrentWeightAsync(
            NodeId scale,
            CancellationToken cancellationToken = default)
        {
            return await ReadWeightItemAsync(scale, BrowseNames.CurrentWeight, cancellationToken).ConfigureAwait(false)
                ?? throw ServiceResultException.Create(
                    StatusCodes.BadNotFound,
                    "'{0}' publishes no CurrentWeight.",
                    scale);
        }

        /// <summary>
        /// Reads a node's <c>CurrentWeight</c>, or null when it publishes none
        /// (a scale system or a module does not). The counterpart of
        /// <see cref="ReadCurrentWeightAsync"/> with the null contract of
        /// <see cref="ReadRegisteredWeightAsync"/>.
        /// </summary>
        /// <param name="scale">The scale.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public ValueTask<ScaleReading?> TryReadCurrentWeightAsync(
            NodeId scale,
            CancellationToken cancellationToken = default)
        {
            return ReadWeightItemAsync(scale, BrowseNames.CurrentWeight, cancellationToken);
        }

        /// <summary>
        /// Reads a scale's <c>RegisteredWeight</c>, or null when it publishes
        /// none.
        /// </summary>
        /// <param name="scale">The scale.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public ValueTask<ScaleReading?> ReadRegisteredWeightAsync(
            NodeId scale,
            CancellationToken cancellationToken = default)
        {
            return ReadWeightItemAsync(scale, BrowseNames.RegisteredWeight, cancellationToken);
        }

        /// <summary>
        /// Reads the identification add-in of a scale, scale system or
        /// module.
        /// </summary>
        /// <param name="component">The scale, scale system or module.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask<ScaleIdentification> ReadIdentificationAsync(
            NodeId component,
            CancellationToken cancellationToken = default)
        {
            QualifiedName identification = DiName(DiBrowseNames.Identification);
            QualifiedName[][] paths =
            [
                [identification, DiName(DiBrowseNames.Manufacturer)],
                [identification, DiName(DiBrowseNames.SerialNumber)],
                [identification, DiName(DiBrowseNames.ProductInstanceUri)],
                [identification, DiName(DiBrowseNames.ManufacturerUri)],
                [identification, DiName(DiBrowseNames.Model)],
                [identification, DiName(DiBrowseNames.ProductCode)],
                [identification, DiName(DiBrowseNames.HardwareRevision)],
                [identification, DiName(DiBrowseNames.SoftwareRevision)],
                [identification, DiName(DiBrowseNames.DeviceClass)],
                [identification, DiName(DiBrowseNames.AssetId)],
                [identification, DiName(DiBrowseNames.ComponentName)],
                [identification, MachineryName(MachineryBrowseNames.Location)],
                [identification, MachineryName(MachineryBrowseNames.YearOfConstruction)],
                [identification, MachineryName(MachineryBrowseNames.MonthOfConstruction)],
                [identification, MachineryName(MachineryBrowseNames.InitialOperationDate)]
            ];
            ArrayOf<NodeId> ids = await ResolvePathsAsync(component, paths, cancellationToken).ConfigureAwait(false);
            Variant[] v = await ReadValuesAsync(ids, cancellationToken).ConfigureAwait(false);
            return new ScaleIdentification
            {
                Manufacturer = AsText(v[0]),
                SerialNumber = AsString(v[1]),
                ProductInstanceUri = AsString(v[2]),
                ManufacturerUri = AsString(v[3]),
                Model = AsText(v[4]),
                ProductCode = AsString(v[5]),
                HardwareRevision = AsString(v[6]),
                SoftwareRevision = AsString(v[7]),
                DeviceClass = AsString(v[8]),
                AssetId = AsString(v[9]),
                ComponentName = AsText(v[10]),
                Location = AsString(v[11]),
                YearOfConstruction = v[12].TryGetValue(out ushort year) ? year : null,
                MonthOfConstruction = v[13].TryGetValue(out byte month) ? month : null,
                InitialOperationDate = v[14].TryGetValue(out DateTimeUtc date) ? (DateTime)date : null
            };
        }

        /// <summary>
        /// Reads the weighing ranges of a scale (its
        /// <c>WeighingRangeElementType</c> children), ordered by capacity.
        /// </summary>
        /// <param name="scale">The scale.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask<ArrayOf<WeighingRangeDefinition>> ReadWeighingRangesAsync(
            NodeId scale,
            CancellationToken cancellationToken = default)
        {
            NodeId rangeType = ScalesTypeId(ObjectTypes.WeighingRangeElementType);
            var ranges = new List<WeighingRangeDefinition>();
            if (rangeType.IsNull)
            {
                return [];
            }
            await foreach (ReferenceDescription reference in BrowseChildrenAsync(scale, NodeClass.Object, cancellationToken)
                .ConfigureAwait(false))
            {
                var typeDefinition = ExpandedNodeId.ToNodeId(reference.TypeDefinition, Session.NamespaceUris);
                if (typeDefinition != rangeType)
                {
                    continue;
                }
                var element = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                ArrayOf<NodeId> ids = await ResolvePathsAsync(
                    element,
                    [
                        [ScalesName(BrowseNames.Range)],
                        [ScalesName(BrowseNames.ActualScaleInterval)],
                        [ScalesName(BrowseNames.VerificationScaleInterval)],
                        [ScalesName(BrowseNames.ActualScaleInterval), new QualifiedName(Opc.Ua.BrowseNames.EngineeringUnits)]
                    ],
                    cancellationToken).ConfigureAwait(false);
                Variant[] v = await ReadValuesAsync(ids, cancellationToken).ConfigureAwait(false);
                if (!v[0].TryGetStructure<Opc.Ua.Range>(Session.MessageContext, out Opc.Ua.Range? range) || range == null)
                {
                    continue;
                }
                ranges.Add(new WeighingRangeDefinition(range.Low, range.High, AsDouble(v[1]), AsDouble(v[2]))
                {
                    EngineeringUnits = v[3].TryGetStructure<EUInformation>(Session.MessageContext, out EUInformation? units) ? units : null
                });
            }
            ranges.Sort((a, b) => a.High.CompareTo(b.High));
            return ranges.ToArrayOf();
        }

        /// <summary>
        /// Reads the engineering units the scale's methods accept
        /// (<c>AllowedEngineeringUnits</c>); empty when the scale does not
        /// restrict them.
        /// </summary>
        /// <param name="scale">The scale.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask<ArrayOf<EUInformation>> ReadAllowedEngineeringUnitsAsync(
            NodeId scale,
            CancellationToken cancellationToken = default)
        {
            NodeId id = await ResolveChildAsync(scale, ScalesName(BrowseNames.AllowedEngineeringUnits), cancellationToken)
                .ConfigureAwait(false);
            Variant[] v = await ReadValuesAsync(new[] { id }.ToArrayOf(), cancellationToken).ConfigureAwait(false);
            var units = new List<EUInformation>();
            if (v[0].TryGetValue(out ArrayOf<ExtensionObject> extensions))
            {
                foreach (ExtensionObject extension in extensions)
                {
                    if (Variant.From(extension).TryGetStructure<EUInformation>(Session.MessageContext, out EUInformation? unit) && unit != null)
                    {
                        units.Add(unit);
                    }
                }
            }
            return units.ToArrayOf();
        }

        /// <summary>
        /// Calls <c>SetZero</c> (§7.4.4).
        /// </summary>
        /// <param name="scale">The scale.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask SetZeroAsync(NodeId scale, CancellationToken cancellationToken = default)
        {
            await CallAsync(scale, ScalesName(BrowseNames.SetZero), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Calls <c>SetTare</c> (§7.4.5).
        /// </summary>
        /// <param name="scale">The scale.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask SetTareAsync(NodeId scale, CancellationToken cancellationToken = default)
        {
            await CallAsync(scale, ScalesName(BrowseNames.SetTare), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Calls <c>ClearTare</c> (§7.4.6).
        /// </summary>
        /// <param name="scale">The scale.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask ClearTareAsync(NodeId scale, CancellationToken cancellationToken = default)
        {
            await CallAsync(scale, ScalesName(BrowseNames.ClearTare), cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Calls <c>SetPresetTare</c> (§7.4.7).
        /// </summary>
        /// <param name="scale">The scale.</param>
        /// <param name="presetTare">The preset tare.</param>
        /// <param name="engineeringUnits">The unit of <paramref name="presetTare"/>.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask SetPresetTareAsync(
            NodeId scale,
            double presetTare,
            EUInformation engineeringUnits,
            CancellationToken cancellationToken = default)
        {
            await CallAsync(
                scale,
                ScalesName(BrowseNames.SetPresetTare),
                cancellationToken,
                Variant.From(presetTare),
                Variant.From(new ExtensionObject(engineeringUnits ?? throw new ArgumentNullException(nameof(engineeringUnits)))))
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Calls <c>RegisterWeight</c> (§7.4.8) and reads back the registered
        /// weight.
        /// </summary>
        /// <param name="scale">The scale.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <returns>The registered weight, or null when the scale does not publish it.</returns>
        public async ValueTask<ScaleReading?> RegisterWeightAsync(NodeId scale, CancellationToken cancellationToken = default)
        {
            await CallAsync(scale, ScalesName(BrowseNames.RegisterWeight), cancellationToken).ConfigureAwait(false);
            return await ReadRegisteredWeightAsync(scale, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Streams a scale's <c>CurrentWeight</c> as it changes.
        /// </summary>
        /// <remarks>
        /// The stream carries the gross, net and tare of the <c>WeightType</c>
        /// value; the properties (overload, tare mode, ...) are read with
        /// <see cref="ReadCurrentWeightAsync"/> when needed.
        /// </remarks>
        /// <param name="scale">The scale.</param>
        /// <param name="streaming">
        /// The streaming subscription, or null for the session's default
        /// (which requires a <see cref="ManagedSession"/>).
        /// </param>
        /// <param name="options">Optional monitoring options.</param>
        /// <param name="cancellationToken">Ends the observation.</param>
        public async IAsyncEnumerable<ScaleReading> ObserveWeightAsync(
            NodeId scale,
            IStreamingSubscription? streaming = null,
            MonitoringOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            NodeId weight = await ResolveChildAsync(scale, ScalesName(BrowseNames.CurrentWeight), cancellationToken)
                .ConfigureAwait(false);
            if (weight.IsNull)
            {
                yield break;
            }
            await foreach (DataValueChange change in (streaming ?? GetDefaultStreaming(Session))
                .SubscribeDataChangesAsync(weight, options, cancellationToken)
                .ConfigureAwait(false))
            {
                DataValue data = change.Value;
                if (data.WrappedValue.TryGetStructure<WeightType>(Session.MessageContext, out WeightType? value) &&
                    value != null)
                {
                    yield return new ScaleReading
                    {
                        Gross = value.Gross,
                        Net = value.Net,
                        Tare = value.Tare,
                        Timestamp = (DateTime)data.SourceTimestamp,
                        StatusCode = data.StatusCode
                    };
                }
            }
        }

        private async ValueTask<ScaleReading?> ReadWeightItemAsync(NodeId scale, string item, CancellationToken ct)
        {
            QualifiedName weight = ScalesName(item);
            QualifiedName[][] paths =
            [
                [weight],
                [weight, ScalesName(BrowseNames.Overload)],
                [weight, ScalesName(BrowseNames.Underload)],
                [weight, ScalesName(BrowseNames.TareMode)],
                [weight, ScalesName(BrowseNames.WeightStable)],
                [weight, ScalesName(BrowseNames.InsideZero)],
                [weight, ScalesName(BrowseNames.CurrentRangeId)],
                [weight, ScalesName(BrowseNames.WeightId)],
                [weight, new QualifiedName(Opc.Ua.BrowseNames.EngineeringUnits)]
            ];
            ArrayOf<NodeId> ids = await ResolvePathsAsync(scale, paths, ct).ConfigureAwait(false);
            if (ids[0].IsNull)
            {
                return null;
            }
            // One Read for the weight and its properties, so the reading and
            // its flags describe the same moment.
            DataValue[] results = await ReadDataValuesAsync(ids, ct).ConfigureAwait(false);
            DataValue data = results[0];
            var v = new Variant[results.Length];
            for (int ii = 1; ii < results.Length; ii++)
            {
                if (StatusCode.IsNotBad(results[ii].StatusCode))
                {
                    v[ii] = results[ii].WrappedValue;
                }
            }
            data.WrappedValue.TryGetStructure<WeightType>(Session.MessageContext, out WeightType? value);
            return new ScaleReading
            {
                Gross = value?.Gross ?? double.NaN,
                Net = value?.Net ?? double.NaN,
                Tare = value?.Tare ?? double.NaN,
                Overload = v[1].TryGetValue(out bool overload) && overload,
                Underload = v[2].TryGetValue(out bool underload) && underload,
                TareMode = v[3].TryGetValue(out int tareMode) ? (TareMode)tareMode : TareMode.None_0,
                Stable = v[4].TryGetValue(out bool stable) ? stable : null,
                InsideZero = v[5].TryGetValue(out bool insideZero) ? insideZero : null,
                CurrentRangeId = v[6].TryGetValue(out ushort rangeId) ? rangeId : null,
                WeightId = AsString(v[7]),
                EngineeringUnits = v[8].TryGetStructure<EUInformation>(Session.MessageContext, out EUInformation? units) ? units : null,
                Timestamp = (DateTime)data.SourceTimestamp,
                StatusCode = data.StatusCode
            };
        }

        internal static IStreamingSubscription GetDefaultStreaming(ISession session)
        {
            if (session is ManagedSession managedSession)
            {
                return managedSession.DefaultStreaming;
            }
            throw ServiceResultException.Create(
                StatusCodes.BadInvalidState,
                "Observation without an explicit IStreamingSubscription requires a ManagedSession.");
        }

        internal static string? AsString(Variant value)
        {
            return value.TryGetValue(out string? text) ? text : null;
        }

        internal static LocalizedText AsText(Variant value)
        {
            return value.TryGetValue(out LocalizedText text) ? text : LocalizedText.Null;
        }

        internal static double AsDouble(Variant value)
        {
            return value.TryGetValue(out double d) ? d
                : value.TryGetValue(out float f) ? f
                : value.TryGetValue(out int i) ? i
                : value.TryGetValue(out uint u) ? u
                : double.NaN;
        }
    }
}
