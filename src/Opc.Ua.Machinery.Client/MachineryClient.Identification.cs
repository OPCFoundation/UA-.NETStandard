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

namespace Opc.Ua.Machinery.Client
{
    public sealed partial class MachineryClient
    {
        /// <summary>
        /// Reads the OPC 40001-1 identification of a machinery item. Returns
        /// <see langword="null"/> when the item publishes no
        /// <c>Identification</c> add-in.
        /// </summary>
        /// <param name="machineryItem">The machine or component to read.</param>
        /// <param name="cancellationToken">Cancels the operation.</param>
        public async ValueTask<MachineIdentification?> ReadIdentificationAsync(
            NodeId machineryItem,
            CancellationToken cancellationToken = default)
        {
            if (m_options is not null &&
                !TryGetNamespaceIndex(Namespaces.Machinery, out _))
            {
                cancellationToken.ThrowIfCancellationRequested();
                return null;
            }
            NodeId identification = await ResolveIdentificationAsync(
                machineryItem,
                cancellationToken).ConfigureAwait(false);
            if (identification.IsNull)
            {
                return null;
            }

            ushort diNamespaceIndex = NamespaceIndexOf(Di.Namespaces.OpcUaDi);
            ushort machineryNamespaceIndex = NamespaceIndexOf(
                Namespaces.Machinery);

            var properties = new (string Key, QualifiedName Name)[]
            {
                (nameof(MachineIdentification.Manufacturer),
                    new QualifiedName(DiBrowseNames.Manufacturer, diNamespaceIndex)),
                (nameof(MachineIdentification.SerialNumber),
                    new QualifiedName(DiBrowseNames.SerialNumber, diNamespaceIndex)),
                (nameof(MachineIdentification.ProductInstanceUri),
                    new QualifiedName(DiBrowseNames.ProductInstanceUri, diNamespaceIndex)),
                (nameof(MachineIdentification.Model),
                    new QualifiedName(DiBrowseNames.Model, diNamespaceIndex)),
                (nameof(MachineIdentification.ManufacturerUri),
                    new QualifiedName(DiBrowseNames.ManufacturerUri, diNamespaceIndex)),
                (nameof(MachineIdentification.ProductCode),
                    new QualifiedName(DiBrowseNames.ProductCode, diNamespaceIndex)),
                (nameof(MachineIdentification.HardwareRevision),
                    new QualifiedName(DiBrowseNames.HardwareRevision, diNamespaceIndex)),
                (nameof(MachineIdentification.SoftwareRevision),
                    new QualifiedName(DiBrowseNames.SoftwareRevision, diNamespaceIndex)),
                (nameof(MachineIdentification.DeviceRevision),
                    new QualifiedName(DiBrowseNames.DeviceRevision, diNamespaceIndex)),
                (nameof(MachineIdentification.DeviceClass),
                    new QualifiedName(DiBrowseNames.DeviceClass, diNamespaceIndex)),
                (nameof(MachineIdentification.AssetId),
                    new QualifiedName(DiBrowseNames.AssetId, diNamespaceIndex)),
                (nameof(MachineIdentification.ComponentName),
                    new QualifiedName(DiBrowseNames.ComponentName, diNamespaceIndex)),
                (nameof(MachineIdentification.Location),
                    new QualifiedName(MachineryBrowseNames.Location, machineryNamespaceIndex)),
                (nameof(MachineIdentification.InitialOperationDate),
                    new QualifiedName(
                        MachineryBrowseNames.InitialOperationDate,
                        machineryNamespaceIndex)),
                (nameof(MachineIdentification.YearOfConstruction),
                    new QualifiedName(
                        MachineryBrowseNames.YearOfConstruction,
                        machineryNamespaceIndex)),
                (nameof(MachineIdentification.MonthOfConstruction),
                    new QualifiedName(
                        MachineryBrowseNames.MonthOfConstruction,
                        machineryNamespaceIndex))
            };

            var names = new QualifiedName[properties.Length];
            for (int ii = 0; ii < properties.Length; ii++)
            {
                names[ii] = properties[ii].Name;
            }
            ReadValueSet fields = await ReadChildValuesAsync(
                identification, names.ToArrayOf(), ReferenceTypeIds.HasProperty, cancellationToken)
                .ConfigureAwait(false);
            fields.RequirePresent(names[0]);
            fields.RequirePresent(names[1]);
            var values = new Dictionary<string, Variant>(StringComparer.Ordinal);
            for (int ii = 0; ii < properties.Length; ii++)
            {
                if (fields.TryGetValue(names[ii], out Variant value))
                {
                    BuiltInType type = properties[ii].Key switch
                    {
                        nameof(MachineIdentification.Manufacturer) or
                        nameof(MachineIdentification.Model) or
                        nameof(MachineIdentification.ComponentName) => BuiltInType.LocalizedText,
                        nameof(MachineIdentification.InitialOperationDate) => BuiltInType.DateTime,
                        nameof(MachineIdentification.YearOfConstruction) => BuiltInType.UInt16,
                        nameof(MachineIdentification.MonthOfConstruction) => BuiltInType.Byte,
                        _ => BuiltInType.String
                    };
                    fields.RequireType(names[ii], HasType(value, type));
                    values[properties[ii].Key] = value;
                }
            }

            return new MachineIdentification
            {
                Values = fields.Values,
                Manufacturer = GetLocalizedText(values, nameof(MachineIdentification.Manufacturer)),
                SerialNumber = GetString(values, nameof(MachineIdentification.SerialNumber)),
                ProductInstanceUri = GetString(
                    values,
                    nameof(MachineIdentification.ProductInstanceUri)),
                Model = GetLocalizedText(values, nameof(MachineIdentification.Model)),
                ManufacturerUri = GetString(values, nameof(MachineIdentification.ManufacturerUri)),
                ProductCode = GetString(values, nameof(MachineIdentification.ProductCode)),
                HardwareRevision = GetString(
                    values,
                    nameof(MachineIdentification.HardwareRevision)),
                SoftwareRevision = GetString(
                    values,
                    nameof(MachineIdentification.SoftwareRevision)),
                DeviceRevision = GetString(values, nameof(MachineIdentification.DeviceRevision)),
                DeviceClass = GetString(values, nameof(MachineIdentification.DeviceClass)),
                AssetId = GetString(values, nameof(MachineIdentification.AssetId)),
                ComponentName = GetLocalizedText(
                    values,
                    nameof(MachineIdentification.ComponentName)),
                Location = GetString(values, nameof(MachineIdentification.Location)),
                InitialOperationDate = GetDateTime(
                    values,
                    nameof(MachineIdentification.InitialOperationDate)),
                YearOfConstruction = GetUInt16(
                    values,
                    nameof(MachineIdentification.YearOfConstruction)),
                MonthOfConstruction = GetByte(
                    values,
                    nameof(MachineIdentification.MonthOfConstruction))
            };
        }

        private static LocalizedText GetLocalizedText(
            Dictionary<string, Variant> values,
            string key)
        {
            return values.TryGetValue(key, out Variant value) &&
                value.TryGetValue(out LocalizedText text)
                ? text
                : default;
        }

        private static string? GetString(Dictionary<string, Variant> values, string key)
        {
            if (!values.TryGetValue(key, out Variant value))
            {
                return null;
            }
            if (value.TryGetValue(out string text))
            {
                return text;
            }
            return value.TryGetValue(out LocalizedText localized) ? localized.Text : null;
        }

        private static DateTime? GetDateTime(Dictionary<string, Variant> values, string key)
        {
            return values.TryGetValue(key, out Variant value) &&
                value.TryGetValue(out DateTimeUtc dateTime)
                ? dateTime.ToDateTime()
                : null;
        }

        private static ushort? GetUInt16(Dictionary<string, Variant> values, string key)
        {
            return values.TryGetValue(key, out Variant value) &&
                value.TryGetValue(out ushort number)
                ? number
                : null;
        }

        private static byte? GetByte(Dictionary<string, Variant> values, string key)
        {
            return values.TryGetValue(key, out Variant value) &&
                value.TryGetValue(out byte number)
                ? number
                : null;
        }
    }
}
