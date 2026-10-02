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
using System.Threading.Tasks;
using DiBrowseNames = Opc.Ua.Di.BrowseNames;
using MachineryBrowseNames = Opc.Ua.Machinery.BrowseNames;

namespace Opc.Ua.Pumps.Client
{
    public sealed partial class PumpsClient
    {
        /// <summary>
        /// Reads a pump's nameplate from its <c>Identification</c> add-in.
        /// </summary>
        /// <remarks>
        /// The nameplate straddles three namespaces - OPC 40223 adds eleven
        /// fields of its own to the four OPC 40001-1 contributes and the
        /// eleven it inherits from OPC 10000-100 - and each field has to be
        /// asked for under the namespace that declares it. Getting that wrong
        /// is the usual reason a nameplate read comes back empty against a
        /// conforming server.
        /// </remarks>
        /// <param name="pump">The pump to read.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        /// <returns>
        /// The nameplate, or <see langword="null"/> when the pump publishes no
        /// <c>Identification</c> add-in.
        /// </returns>
        public async ValueTask<PumpNameplate?> ReadNameplateAsync(
            NodeId pump,
            CancellationToken cancellationToken = default)
        {
            NodeId identification = await ResolveIdentificationAsync(pump, cancellationToken)
                .ConfigureAwait(false);
            if (identification.IsNull)
            {
                return null;
            }

            ushort di = NamespaceIndexOf(Opc.Ua.Di.Namespaces.OpcUaDi);
            ushort machinery = NamespaceIndexOf(Opc.Ua.Machinery.Namespaces.Machinery);
            ushort pumps = NamespaceIndexOf(Namespaces.Pumps);

            (string Key, QualifiedName Name)[] fields =
            [
                // OPC 10000-100
                (DiBrowseNames.Manufacturer, new QualifiedName(DiBrowseNames.Manufacturer, di)),
                (DiBrowseNames.SerialNumber, new QualifiedName(DiBrowseNames.SerialNumber, di)),
                (DiBrowseNames.ManufacturerUri, new QualifiedName(DiBrowseNames.ManufacturerUri, di)),
                (DiBrowseNames.Model, new QualifiedName(DiBrowseNames.Model, di)),
                (DiBrowseNames.ProductCode, new QualifiedName(DiBrowseNames.ProductCode, di)),
                (DiBrowseNames.HardwareRevision, new QualifiedName(DiBrowseNames.HardwareRevision, di)),
                (DiBrowseNames.SoftwareRevision, new QualifiedName(DiBrowseNames.SoftwareRevision, di)),
                (DiBrowseNames.DeviceClass, new QualifiedName(DiBrowseNames.DeviceClass, di)),
                (DiBrowseNames.ProductInstanceUri, new QualifiedName(DiBrowseNames.ProductInstanceUri, di)),
                (DiBrowseNames.AssetId, new QualifiedName(DiBrowseNames.AssetId, di)),
                (DiBrowseNames.ComponentName, new QualifiedName(DiBrowseNames.ComponentName, di)),
                // OPC 40001-1
                (MachineryBrowseNames.Location, new QualifiedName(MachineryBrowseNames.Location, machinery)),
                (MachineryBrowseNames.InitialOperationDate, new QualifiedName(MachineryBrowseNames.InitialOperationDate, machinery)),
                (MachineryBrowseNames.YearOfConstruction, new QualifiedName(MachineryBrowseNames.YearOfConstruction, machinery)),
                (MachineryBrowseNames.MonthOfConstruction, new QualifiedName(MachineryBrowseNames.MonthOfConstruction, machinery)),
                // OPC 40223
                (BrowseNames.DayOfConstruction, new QualifiedName(BrowseNames.DayOfConstruction, pumps)),
                (BrowseNames.ArticleNumber, new QualifiedName(BrowseNames.ArticleNumber, pumps)),
                (BrowseNames.OrderProductCode, new QualifiedName(BrowseNames.OrderProductCode, pumps)),
                (BrowseNames.TypeOfProduct, new QualifiedName(BrowseNames.TypeOfProduct, pumps)),
                (BrowseNames.Supplier, new QualifiedName(BrowseNames.Supplier, pumps)),
                (BrowseNames.CountryOfOrigin, new QualifiedName(BrowseNames.CountryOfOrigin, pumps)),
                (BrowseNames.FabricationNumber, new QualifiedName(BrowseNames.FabricationNumber, pumps)),
                (BrowseNames.GTINCode, new QualifiedName(BrowseNames.GTINCode, pumps)),
                (BrowseNames.NationalStockNumber, new QualifiedName(BrowseNames.NationalStockNumber, pumps)),
                (BrowseNames.PhysicalAddress, new QualifiedName(BrowseNames.PhysicalAddress, pumps)),
                // The Markings folder is an object, so it is resolved but not read.
                (BrowseNames.Markings, new QualifiedName(BrowseNames.Markings, pumps))
            ];

            var names = new QualifiedName[fields.Length];
            for (int ii = 0; ii < fields.Length; ii++)
            {
                names[ii] = fields[ii].Name;
            }

            ArrayOf<NodeId> resolved = await ResolveChildrenAsync(
                identification,
                names,
                cancellationToken).ConfigureAwait(false);

            NodeId markings = NodeId.Null;
            var reads = new List<ReadValueId>(fields.Length);
            var keys = new List<string>(fields.Length);
            for (int ii = 0; ii < fields.Length && ii < resolved.Count; ii++)
            {
                if (resolved[ii].IsNull)
                {
                    continue;
                }
                if (fields[ii].Key == BrowseNames.Markings)
                {
                    markings = resolved[ii];
                    continue;
                }
                reads.Add(new ReadValueId
                {
                    NodeId = resolved[ii],
                    AttributeId = Attributes.Value
                });
                keys.Add(fields[ii].Key);
            }

            var values = new Dictionary<string, DataValue>(StringComparer.Ordinal);
            if (reads.Count > 0)
            {
                ReadResponse response = await Session.ReadAsync(
                    requestHeader: null,
                    maxAge: 0,
                    timestampsToReturn: TimestampsToReturn.Neither,
                    nodesToRead: reads.ToArray().ToArrayOf(),
                    ct: cancellationToken).ConfigureAwait(false);
                for (int ii = 0; ii < response.Results.Count && ii < keys.Count; ii++)
                {
                    values[keys[ii]] = response.Results[ii];
                }
            }

            return new PumpNameplate
            {
                NodeId = identification,
                Manufacturer = Localized(values, DiBrowseNames.Manufacturer),
                SerialNumber = Text(values, DiBrowseNames.SerialNumber),
                ManufacturerUri = Text(values, DiBrowseNames.ManufacturerUri),
                Model = Localized(values, DiBrowseNames.Model),
                ProductCode = Text(values, DiBrowseNames.ProductCode),
                HardwareRevision = Text(values, DiBrowseNames.HardwareRevision),
                SoftwareRevision = Text(values, DiBrowseNames.SoftwareRevision),
                DeviceClass = Text(values, DiBrowseNames.DeviceClass),
                ProductInstanceUri = Text(values, DiBrowseNames.ProductInstanceUri),
                AssetId = Text(values, DiBrowseNames.AssetId),
                ComponentName = Localized(values, DiBrowseNames.ComponentName),
                Location = Text(values, MachineryBrowseNames.Location),
                InitialOperationDate = Date(
                    values, MachineryBrowseNames.InitialOperationDate),
                YearOfConstruction = UInt16(
                    values, MachineryBrowseNames.YearOfConstruction),
                MonthOfConstruction = Byte(
                    values, MachineryBrowseNames.MonthOfConstruction),
                DayOfConstruction = Int32(values, BrowseNames.DayOfConstruction),
                ArticleNumber = Text(values, BrowseNames.ArticleNumber),
                OrderProductCode = Text(values, BrowseNames.OrderProductCode),
                TypeOfProduct = Text(values, BrowseNames.TypeOfProduct),
                Supplier = Text(values, BrowseNames.Supplier),
                CountryOfOrigin = Text(values, BrowseNames.CountryOfOrigin),
                FabricationNumber = Text(values, BrowseNames.FabricationNumber),
                GTINCode = Text(values, BrowseNames.GTINCode),
                NationalStockNumber = Text(values, BrowseNames.NationalStockNumber),
                PhysicalAddress = Structured<PhysicalAddressDataType>(
                    values, BrowseNames.PhysicalAddress, Session.MessageContext),
                MarkingsFolderId = markings
            };
        }

        /// <summary>
        /// Enumerates the vendor-defined markings below a pump's
        /// <c>Markings</c> folder. OPC 40223 leaves their contents open, so
        /// what comes back are the marking objects themselves.
        /// </summary>
        /// <param name="markingsFolder">
        /// The folder, from <see cref="PumpNameplate.MarkingsFolderId"/>.
        /// </param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask<ArrayOf<PumpEntry>> ReadMarkingsAsync(
            NodeId markingsFolder,
            CancellationToken cancellationToken = default)
        {
            var markings = new List<PumpEntry>();
            await foreach (ReferenceDescription reference in BrowseChildrenAsync(
                    markingsFolder,
                    NodeClass.Object | NodeClass.Variable,
                    cancellationToken).ConfigureAwait(false))
            {
                var nodeId = ExpandedNodeId.ToNodeId(reference.NodeId, Session.NamespaceUris);
                if (!nodeId.IsNull)
                {
                    markings.Add(new PumpEntry(
                        nodeId,
                        reference.BrowseName,
                        reference.DisplayName,
                        ExpandedNodeId.ToNodeId(
                            reference.TypeDefinition,
                            Session.NamespaceUris)));
                }
            }
            return markings.ToArrayOf();
        }

        private static bool TryGood(
            Dictionary<string, DataValue> values,
            string key,
            out DataValue value)
        {
            return values.TryGetValue(key, out value) &&
                !StatusCode.IsBad(value.StatusCode);
        }

        private static string? Text(Dictionary<string, DataValue> values, string key)
        {
            if (!TryGood(values, key, out DataValue value))
            {
                return null;
            }
            if (value.WrappedValue.TryGetValue(out string text))
            {
                return text;
            }
            return value.WrappedValue.TryGetValue(out LocalizedText localized)
                ? localized.Text
                : null;
        }

        private static LocalizedText Localized(
            Dictionary<string, DataValue> values,
            string key)
        {
            if (!TryGood(values, key, out DataValue value))
            {
                return LocalizedText.Null;
            }
            if (value.WrappedValue.TryGetValue(out LocalizedText localized))
            {
                return localized;
            }
            return value.WrappedValue.TryGetValue(out string text)
                ? new LocalizedText(text)
                : LocalizedText.Null;
        }

        private static ushort? UInt16(Dictionary<string, DataValue> values, string key)
        {
            return TryGood(values, key, out DataValue value) &&
                value.WrappedValue.TryGetValue(out ushort number)
                    ? number
                    : null;
        }

        private static byte? Byte(Dictionary<string, DataValue> values, string key)
        {
            return TryGood(values, key, out DataValue value) &&
                value.WrappedValue.TryGetValue(out byte number)
                    ? number
                    : null;
        }

        private static int? Int32(Dictionary<string, DataValue> values, string key)
        {
            return TryGood(values, key, out DataValue value) &&
                value.WrappedValue.TryGetValue(out int number)
                    ? number
                    : null;
        }

        private static DateTime? Date(Dictionary<string, DataValue> values, string key)
        {
            return TryGood(values, key, out DataValue value) &&
                value.WrappedValue.TryGetValue(out DateTimeUtc date)
                    ? date.ToDateTime()
                    : null;
        }

        private static T? Structured<T>(
            Dictionary<string, DataValue> values,
            string key,
            IServiceMessageContext context)
            where T : class, IEncodeable
        {
            if (!TryGood(values, key, out DataValue value))
            {
                return null;
            }
#pragma warning disable CS8600 // TryGetValue is [MaybeNullWhen(false)]
            bool decodedOk = value.WrappedValue.TryGetValue(out T decoded, context);
#pragma warning restore CS8600
            return decodedOk ? decoded : null;
        }
    }
}
