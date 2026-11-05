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

namespace Opc.Ua.Scales.Client
{
    public sealed partial class ScalesClient
    {
        /// <summary>
        /// Reads a product's last item and price configuration, retaining each field's source quality and timestamp.
        /// Missing optional fields have HasValue false; service failures propagate.
        /// </summary>
        /// <param name="product">The product node, not the scale.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        public async ValueTask<ScaleLastItemSnapshot> ReadLastItemAsync(
            NodeId product,
            CancellationToken cancellationToken = default)
        {
            var proxy = new CatchweigherProductTypeClient(Session, product, Telemetry);
            WeighingItemTypeClient? item = await proxy.GetLastItemAsync(Telemetry, cancellationToken)
                .ConfigureAwait(false);
            QualifiedName weight = ScalesName(BrowseNames.MeasuredWeight);
            QualifiedName height = ScalesName(BrowseNames.MeasuredHeight);
            QualifiedName length = ScalesName(BrowseNames.MeasuredLength);
            QualifiedName width = ScalesName(BrowseNames.MeasuredWidth);
            QualifiedName volume = ScalesName(BrowseNames.MeasuredVolume);
            QualifiedName price = ScalesName(BrowseNames.ItemPrice);
            QualifiedName unitPrice = ScalesName(BrowseNames.UnitPrice);
            QualifiedName units = new(Ua.BrowseNames.EngineeringUnits);
            QualifiedName currency = ScalesName(BrowseNames.CurrencyUnit);
            ArrayOf<NodeId> itemIds = await ResolvePathsAsync(
                item is { } resolvedItem ? resolvedItem.ObjectId : NodeId.Null,
                [
                    [ScalesName(BrowseNames.ItemId)], [weight], [weight, units],
                    [height], [height, units], [length], [length, units],
                    [width], [width, units], [volume], [volume, units],
                    [ScalesName(BrowseNames.ZoneName)], [price], [price, currency],
                    [weight, ScalesName(BrowseNames.Overload)],
                    [weight, ScalesName(BrowseNames.Underload)],
                    [weight, ScalesName(BrowseNames.TareMode)],
                    [weight, ScalesName(BrowseNames.WeightStable)],
                    [weight, ScalesName(BrowseNames.InsideZero)],
                    [weight, ScalesName(BrowseNames.CurrentRangeId)],
                    [weight, ScalesName(BrowseNames.WeightId)]
                ], cancellationToken).ConfigureAwait(false);
            ArrayOf<NodeId> priceIds = await ResolvePathsAsync(product,
                [[unitPrice], [unitPrice, currency]], cancellationToken).ConfigureAwait(false);
            var combined = new NodeId[itemIds.Count + priceIds.Count];
            for (int index = 0; index < itemIds.Count; index++)
            {
                combined[index] = itemIds[index];
            }
            combined[itemIds.Count] = priceIds[0];
            combined[itemIds.Count + 1] = priceIds[1];
            ArrayOf<NodeId> ids = combined.ToArrayOf();
            DataValue[] values = await ReadDataValuesAsync(ids, cancellationToken).ConfigureAwait(false);
            bool hasWeight = values[1].WrappedValue.TryGetStructure<WeightType>(
                Session.MessageContext, out WeightType? _);
            ScaleReading reading = DecodeWeightItem(
                [values[1], values[14], values[15], values[16], values[17],
                    values[18], values[19], values[20], values[2]]);
            return new ScaleLastItemSnapshot
            {
                ProductNodeId = product,
                LastItemNodeId = item is { } lastItem ? lastItem.ObjectId : NodeId.Null,
                ItemId = TextField(values[0]),
                MeasuredWeight = Field(values[1], hasWeight, reading),
                MeasuredHeight = NumberField(values[3]),
                MeasuredHeightEngineeringUnits = StructureField<EUInformation>(values[4]),
                MeasuredLength = NumberField(values[5]),
                MeasuredLengthEngineeringUnits = StructureField<EUInformation>(values[6]),
                MeasuredWidth = NumberField(values[7]),
                MeasuredWidthEngineeringUnits = StructureField<EUInformation>(values[8]),
                MeasuredVolume = NumberField(values[9]),
                MeasuredVolumeEngineeringUnits = StructureField<EUInformation>(values[10]),
                ZoneName = TextField(values[11]),
                ItemPrice = NumberField(values[12]),
                ItemPriceCurrencyUnit = StructureField<CurrencyUnitType>(values[13]),
                UnitPrice = NumberField(values[21]),
                UnitPriceCurrencyUnit = StructureField<CurrencyUnitType>(values[22])
            };
        }

        /// <summary>
        /// Reads a totalizing-hopper product's UInteger tip counter without narrowing to UInt32.
        /// </summary>
        /// <param name="product">The product node.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        public async ValueTask<ScaleField<ulong>> ReadTipCounterAsync(
            NodeId product,
            CancellationToken cancellationToken = default)
        {
            NodeId id = await ResolveChildAsync(
                product, ScalesName(BrowseNames.TipCounter), cancellationToken).ConfigureAwait(false);
            DataValue[] values = await ReadDataValuesAsync([id], cancellationToken).ConfigureAwait(false);
            Variant value = values[0].WrappedValue;
            bool valid = value.TryGetValue(out ulong tips);
            if (!valid && value.TryGetValue(out uint u32))
            {
                tips = u32;
                valid = true;
            }
            if (!valid && value.TryGetValue(out ushort u16))
            {
                tips = u16;
                valid = true;
            }
            if (!valid && value.TryGetValue(out byte u8))
            {
                tips = u8;
                valid = true;
            }
            return Field(values[0], valid, tips);
        }

        /// <summary>
        /// Reads a vehicle's measurements and optional totals without substituting values for absent or bad data.
        /// </summary>
        /// <param name="product">The vehicle product node.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        public async ValueTask<ScaleVehicleWeightSnapshot> ReadVehicleWeightsAsync(
            NodeId product,
            CancellationToken cancellationToken = default)
        {
            ArrayOf<NodeId> ids = await ResolvePathsAsync(product,
                [
                    [ScalesName(BrowseNames.InboundWeight)], [ScalesName(BrowseNames.OutboundWeight)],
                    [ScalesName(BrowseNames.DeltaWeight)], [ScalesName(BrowseNames.TotalWeight)],
                    [ScalesName(BrowseNames.TotalWeightResetDate)]
                ], cancellationToken).ConfigureAwait(false);
            DataValue[] values = await ReadDataValuesAsync(ids, cancellationToken).ConfigureAwait(false);
            bool hasDate = values[4].WrappedValue.TryGetValue(out DateTimeUtc reset);
            return new ScaleVehicleWeightSnapshot
            {
                ProductNodeId = product,
                InboundWeight = StructureField<WeightType>(values[0]),
                OutboundWeight = StructureField<WeightType>(values[1]),
                DeltaWeight = StructureField<WeightType>(values[2]),
                TotalWeight = StructureField<WeightType>(values[3]),
                TotalWeightResetDate = Field(values[4], hasDate, (DateTime)reset)
            };
        }

        /// <summary>
        /// Reads the recipe selected on a product and its typed report entries.
        /// </summary>
        /// <param name="product">The recipe product node.</param>
        /// <param name="cancellationToken">Cancels the read.</param>
        public async ValueTask<ScaleRecipeStatus> ReadRecipeStatusAsync(
            NodeId product,
            CancellationToken cancellationToken = default)
        {
            ArrayOf<NodeId> ids = await ResolvePathsAsync(product,
                [[ScalesName(BrowseNames.RecipeNodeId)], [ScalesName(BrowseNames.Report)]],
                cancellationToken).ConfigureAwait(false);
            DataValue[] values = await ReadDataValuesAsync(ids, cancellationToken).ConfigureAwait(false);
            bool recipeValid = values[0].WrappedValue.TryGetValue(out NodeId recipe);
            bool reportValid = values[1].WrappedValue.TryGetStructure(
                Session.MessageContext, out ArrayOf<RecipeReportElementType> report);
            return new ScaleRecipeStatus
            {
                ProductNodeId = product,
                RecipeNodeId = Field(values[0], recipeValid, recipe),
                Report = Field(values[1], reportValid, report)
            };
        }

        /// <summary>
        /// Observes a recipe product's report changes with each notification's original quality and source time.
        /// A missing report member yields no observations.
        /// </summary>
        /// <param name="product">The recipe product node.</param>
        /// <param name="streaming">The explicit stream, or the managed session's default subscription.</param>
        /// <param name="cancellationToken">Ends observation.</param>
        public async IAsyncEnumerable<ScaleField<ArrayOf<RecipeReportElementType>>> ObserveRecipeReportsAsync(
            NodeId product,
            IStreamingSubscription? streaming = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            NodeId report = await ResolveChildAsync(
                product, ScalesName(BrowseNames.Report), cancellationToken).ConfigureAwait(false);
            if (report.IsNull)
            {
                yield break;
            }
            await foreach (DataValueChange change in (streaming ?? GetDefaultStreaming(Session))
                .SubscribeDataChangesAsync(report, null, cancellationToken).ConfigureAwait(false))
            {
                DataValue data = change.Value;
                bool valid = data.WrappedValue.TryGetStructure(
                    Session.MessageContext, out ArrayOf<RecipeReportElementType> entries);
                yield return Field(data, valid, entries);
            }
        }

        /// <summary>
        /// Preserves a field's service status and distinguishes malformed Good values from valid defaults.
        /// </summary>
        /// <typeparam name="T">The field's decoded value type.</typeparam>
        private static ScaleField<T> Field<T>(in DataValue data, bool valid, T value)
        {
            StatusCode status = !valid && StatusCode.IsGood(data.StatusCode)
                ? StatusCodes.BadTypeMismatch : data.StatusCode;
            return new ScaleField<T>(value, valid && StatusCode.IsNotBad(status), status, (DateTime)data.SourceTimestamp);
        }

        /// <summary>
        /// Decodes a structured field with the session's message context.
        /// </summary>
        /// <typeparam name="T">The generated structure type.</typeparam>
        private ScaleField<T> StructureField<T>(in DataValue data) where T : class, IEncodeable, new()
        {
            bool valid = data.WrappedValue.TryGetStructure<T>(Session.MessageContext, out T? value);
            return Field(data, valid, value!);
        }

        /// <summary>
        /// Reads a text field without substituting an empty string for missing data.
        /// </summary>
        private static ScaleField<string> TextField(in DataValue data)
        {
            bool valid = data.WrappedValue.TryGetValue(out string? value);
            return Field(data, valid, value!);
        }

        /// <summary>
        /// Reads the built-in scalar Number representations.
        /// </summary>
        private static ScaleField<double> NumberField(in DataValue data)
        {
            Variant value = data.WrappedValue;
            double number = value.TryGetValue(out double d) ? d
                : value.TryGetValue(out float f) ? f
                : value.TryGetValue(out sbyte i8) ? i8
                : value.TryGetValue(out byte u8) ? u8
                : value.TryGetValue(out short i16) ? i16
                : value.TryGetValue(out ushort u16) ? u16
                : value.TryGetValue(out int i32) ? i32
                : value.TryGetValue(out uint u32) ? u32
                : value.TryGetValue(out long i64) ? i64
                : value.TryGetValue(out ulong u64) ? u64 : double.NaN;
            return Field(data, !double.IsNaN(number), number);
        }
    }
}
