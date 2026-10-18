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
using Opc.Ua.GMS.Contracts;
using Opc.Ua.Machinery;
using Opc.Ua.Machinery.Server.Builders;

namespace Opc.Ua.GMS.Server
{
    internal sealed partial class GmsSystemBinding
    {
        /// <summary>
        /// Creates native accessory and additional-sensor slots, retaining their generated nameplates and type IDs.
        /// </summary>
        private void CreateEquipmentStructure(GMSState state, GmsSystemSnapshot snapshot)
        {
            if (Has(GmsFeatures.Accessories))
            {
                state.Equipment!.AddAccessories(m_context.Context);
                FolderState folder = state.Equipment.Accessories!;
                foreach (GmsAccessory item in snapshot.Accessories)
                {
                    var name = new QualifiedName(item.Key, m_context.InstanceNamespaceIndex);
                    BaseObjectState node = item.Kind switch
                    {
                        GmsAccessoryKind.RotaryTable => m_context.Context.CreateInstanceOfRotaryTableType(folder, name),
                        GmsAccessoryKind.SensorExchangeRack =>
                            m_context.Context.CreateInstanceOfSensorExchangeRackType(folder, name),
                        GmsAccessoryKind.TipExchangeRack =>
                            m_context.Context.CreateInstanceOfTipExchangeRackType(folder, name),
                        _ => throw Invalid("Unknown native accessory kind.")
                    };
                    node.ReferenceTypeId = ReferenceTypeIds.HasComponent;
                    folder.AddChild(node);
                    SelectFields(node, AccessoryFields(item));
                    m_accessories.Add(item.Key, node);
                }
            }
            if (Has(GmsFeatures.AdditionalSensors))
            {
                state.Equipment!.AddAdditionalSensor(m_context.Context);
                FolderState folder = state.Equipment.AdditionalSensor!;
                foreach (GmsAdditionalSensor value in snapshot.AdditionalSensors)
                {
                    AdditionalSensorState node = m_context.Context.CreateInstanceOfAdditionalSensorType(
                        folder, new QualifiedName(value.Key, m_context.InstanceNamespaceIndex));
                    node.ReferenceTypeId = ReferenceTypeIds.HasComponent;
                    folder.AddChild(node);
                    node.AccessLevel = AccessLevels.CurrentRead;
                    node.UserAccessLevel = AccessLevels.CurrentRead;
                    node.EngineeringUnits!.AccessLevel = AccessLevels.CurrentRead;
                    node.EngineeringUnits.UserAccessLevel = AccessLevels.CurrentRead;
                    SelectFields(node, AdditionalFields(value));
                    m_additionalSensors.Add(value.Key, node);
                }
            }
        }

        /// <summary>
        /// Reuses the shared nameplate writer on each native MachineryItemIdentification AddIn.
        /// </summary>
        private void ConfigureAccessoryIdentification(IMachineBuilder<GMSState> builder)
        {
            foreach (GmsAccessory value in m_input!.Accessories)
            {
                BaseObjectState node = m_accessories[value.Key];
                MachineryItemIdentificationState identity = node switch
                {
                    RotaryTableState rotary => rotary.Identification!,
                    SensorExchangeRackState sensorRack => sensorRack.Identification!,
                    TipExchangeRackState tipRack => tipRack.Identification!,
                    _ => throw Invalid("The accessory factory did not return its actual native type.")
                };
                GmsItemIdentification data = value.Identification!;
                builder.WithIdentification(identity, fields =>
                {
                    fields.Manufacturer = data.Manufacturer;
                    fields.SerialNumber = data.SerialNumber;
                    fields.ManufacturerUri = data.ManufacturerUri;
                    fields.Model = data.Model;
                    fields.ProductCode = data.ProductCode;
                });
            }
        }

        /// <summary>
        /// Plans native equipment values in the same transaction as monitoring, sensors and calibration.
        /// </summary>
        private void PlanEquipment(GmsSystemSnapshot snapshot, List<Action> writes)
        {
            foreach (GmsAccessory value in snapshot.Accessories)
            {
                PlanFields(m_accessories[value.Key], AccessoryFields(value), writes);
            }
            foreach (GmsAdditionalSensor value in snapshot.AdditionalSensors)
            {
                AdditionalSensorState node = m_additionalSensors[value.Key];
                PlanFields(node, AdditionalFields(value), writes);
                var unit = new DataValue(Variant.FromStructure(value.EngineeringUnits!),
                    value.Value.StatusCode, value.Value.SourceTimestamp);
                writes.Add(() =>
                {
                    Set(node, value.Value);
                    Set(node.EngineeringUnits!, unit);
                });
            }
        }

        /// <summary>
        /// Validates finite native inventories and preserves the configured static accessory identities.
        /// </summary>
        private void ValidateEquipment(GmsSystemSnapshot snapshot)
        {
            if ((long)snapshot.Accessories.Count + snapshot.AdditionalSensors.Count > m_options.MaximumEquipment)
            {
                throw Invalid("The combined native accessory and additional-sensor inventory exceeds its bound.");
            }
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (GmsAccessory value in snapshot.Accessories)
            {
                if (value == null || value.Identification == null || !keys.Add(value.Key))
                {
                    throw Invalid("Accessories require distinct keys and their actual item nameplates.");
                }
                Text(value.Key, true);
                Text(value.Identification.Manufacturer.Text, true);
                Text(value.Identification.SerialNumber, true);
                Text(value.Identification.ManufacturerUri);
                Text(value.Identification.Model.Text);
                Text(value.Identification.ProductCode);
                switch (value.Kind)
                {
                    case GmsAccessoryKind.RotaryTable:
                        Raw(value.IsIntegrated, BuiltInType.Boolean, required: true);
                        Raw(value.NumberOfAxes, BuiltInType.Byte, required: true);
                        if (!value.IsAvailable.IsNull ||
                            (value.NumberOfAxes.WrappedValue.TryGetValue(out byte axes) && axes == 0))
                        {
                            throw Invalid("A rotary table has a nonzero Byte axis count and no IsAvailable property.");
                        }
                        break;
                    case GmsAccessoryKind.SensorExchangeRack:
                    case GmsAccessoryKind.TipExchangeRack:
                        Raw(value.IsAvailable, BuiltInType.Boolean, required: true);
                        if (!value.IsIntegrated.IsNull || !value.NumberOfAxes.IsNull)
                        {
                            throw Invalid("Exchange racks do not declare rotary-table properties.");
                        }
                        break;
                    default:
                        throw Invalid("Unknown native accessory kind.");
                }
            }
            keys.Clear();
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            foreach (GmsAdditionalSensor value in snapshot.AdditionalSensors)
            {
                if (value == null || !keys.Add(value.Key) || !identifiers.Add(value.Identifier))
                {
                    throw Invalid("Additional sensors require distinct keys and actual identifiers.");
                }
                Text(value.Key, true);
                Text(value.Identifier, true);
                Text(value.Name);
                Number(value.Value, required: true);
                Unit(value.EngineeringUnits);
                Unsigned(value.Class, 3);
            }
            if (Observation is { } previous)
            {
                if (previous.Accessories.Count != snapshot.Accessories.Count ||
                    previous.AdditionalSensors.Count != snapshot.AdditionalSensors.Count)
                {
                    throw Invalid("Static native equipment slots cannot appear or disappear during observation.");
                }
                for (int i = 0; i < previous.Accessories.Count; i++)
                {
                    GmsAccessory old = previous.Accessories[i];
                    GmsAccessory next = snapshot.Accessories[i];
                    if (old.Key != next.Key || old.Kind != next.Kind || old.Identification != next.Identification)
                    {
                        throw Invalid("A static accessory slot retains its native kind and actual nameplate.");
                    }
                }
                for (int i = 0; i < previous.AdditionalSensors.Count; i++)
                {
                    if (previous.AdditionalSensors[i].Key != snapshot.AdditionalSensors[i].Key ||
                        previous.AdditionalSensors[i].Identifier != snapshot.AdditionalSensors[i].Identifier)
                    {
                        throw Invalid("A static additional-sensor slot retains its physical identity.");
                    }
                }
            }
        }

        /// <summary>
        /// Validates the actual scalar Number representations used by native additional sensors and catalogs.
        /// </summary>
        private static void Number(in DataValue value, bool required = false)
        {
            BuiltInType type = value.WrappedValue.TypeInfo.BuiltInType;
            Raw(value, type, required);
            if (!value.WrappedValue.IsNull && type is < BuiltInType.SByte or > BuiltInType.Double)
            {
                throw Invalid("The native Number value must retain a numeric built-in representation.");
            }
        }

        /// <summary>
        /// Requires an explicitly described native engineering unit, without guessing a unit from sensor class.
        /// </summary>
        private void Unit(EUInformation? value)
        {
            if (value == null || !Uri.TryCreate(value.NamespaceUri, UriKind.Absolute, out _))
            {
                throw Invalid("An analog sensor requires an engineering unit with its actual unit namespace.");
            }
            Text(value.NamespaceUri, true);
            Text(value.DisplayName.Text, true);
            Text(value.Description.Text);
        }

        /// <summary>
        /// Lists only the properties declared by the actual concrete accessory kind.
        /// </summary>
        private static ArrayOf<(string Name, DataValue Value)> AccessoryFields(GmsAccessory value) =>
            value.Kind == GmsAccessoryKind.RotaryTable
                ? [(BrowseNames.IsIntegrated, value.IsIntegrated), (BrowseNames.NumberOfAxes, value.NumberOfAxes)]
                : [(BrowseNames.IsAvailable, value.IsAvailable)];

        /// <summary>
        /// Lists native additional-sensor metadata while keeping its primary analog sample separate.
        /// </summary>
        private static ArrayOf<(string Name, DataValue Value)> AdditionalFields(GmsAdditionalSensor value) =>
        [
            (BrowseNames.Identifier, new DataValue(Variant.From(value.Identifier),
                value.Value.StatusCode, value.Value.SourceTimestamp)),
            (BrowseNames.Name, value.Name == null ? DataValue.Null :
                new DataValue(Variant.From(value.Name), value.Value.StatusCode, value.Value.SourceTimestamp)),
            (BrowseNames.Class, value.Class)
        ];

        /// <summary>Static native accessory nodes owned by the containing root.</summary>
        private readonly Dictionary<string, BaseObjectState> m_accessories = new(StringComparer.Ordinal);

        /// <summary>Static native analog additional-sensor nodes owned by the containing root.</summary>
        private readonly Dictionary<string, AdditionalSensorState> m_additionalSensors = new(StringComparer.Ordinal);
    }
}
