// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using Opc.Ua.Di;
using Opc.Ua.Machinery;

namespace Opc.Ua.LADS.Server
{
    /// <summary>
    /// Writes <see cref="LadsIdentification"/> values to the DI properties of a component and to its
    /// Machinery identification, creating the optional properties a value is given for.
    /// </summary>
    internal static class LadsIdentificationWriter
    {
        /// <summary>
        /// Applies the identification values.
        /// </summary>
        /// <param name="context">The system context.</param>
        /// <param name="component">The DI component (device or component).</param>
        /// <param name="identification">The Machinery identification of the component.</param>
        /// <param name="values">The values.</param>
        public static void Apply(
            ISystemContext context,
            ComponentState component,
            MachineryItemIdentificationState? identification,
            LadsIdentification values)
        {
            if (!values.Manufacturer.IsNull)
            {
                Write(
                    component.Manufacturer,
                    () => component.AddManufacturer(context).Manufacturer,
                    values.Manufacturer);
                Write(identification?.Manufacturer, () => null, values.Manufacturer);
            }
            if (!values.Model.IsNull)
            {
                Write(component.Model, () => component.AddModel(context).Model, values.Model);
                Write(identification?.Model, () => identification?.AddModel(context).Model, values.Model);
            }
            if (!values.ComponentName.IsNull)
            {
                Write(
                    component.ComponentName,
                    () => component.AddComponentName(context).ComponentName,
                    values.ComponentName);
                Write(
                    identification?.ComponentName,
                    () => identification?.AddComponentName(context).ComponentName,
                    values.ComponentName);
            }
            if (values.ManufacturerUri != null)
            {
                Write(
                    component.ManufacturerUri,
                    () => component.AddManufacturerUri(context).ManufacturerUri,
                    values.ManufacturerUri);
                Write(
                    identification?.ManufacturerUri,
                    () => identification?.AddManufacturerUri(context).ManufacturerUri,
                    values.ManufacturerUri);
            }
            if (values.SerialNumber != null)
            {
                Write(
                    component.SerialNumber,
                    () => component.AddSerialNumber(context).SerialNumber,
                    values.SerialNumber);
                Write(identification?.SerialNumber, () => null, values.SerialNumber);
            }
            if (values.HardwareRevision != null)
            {
                Write(
                    component.HardwareRevision,
                    () => component.AddHardwareRevision(context).HardwareRevision,
                    values.HardwareRevision);
                Write(
                    identification?.HardwareRevision,
                    () => identification?.AddHardwareRevision(context).HardwareRevision,
                    values.HardwareRevision);
            }
            if (values.SoftwareRevision != null)
            {
                Write(
                    component.SoftwareRevision,
                    () => component.AddSoftwareRevision(context).SoftwareRevision,
                    values.SoftwareRevision);
                Write(
                    identification?.SoftwareRevision,
                    () => identification?.AddSoftwareRevision(context).SoftwareRevision,
                    values.SoftwareRevision);
            }
            if (values.DeviceRevision != null)
            {
                Write(
                    component.DeviceRevision,
                    () => component.AddDeviceRevision(context).DeviceRevision,
                    values.DeviceRevision);
                if (identification is MachineryComponentIdentificationState componentIdentification)
                {
                    Write(
                        componentIdentification.DeviceRevision,
                        () => componentIdentification.AddDeviceRevision(context).DeviceRevision,
                        values.DeviceRevision);
                }
            }
            if (values.DeviceManual != null)
            {
                Write(
                    component.DeviceManual,
                    () => component.AddDeviceManual(context).DeviceManual,
                    values.DeviceManual);
            }
            if (values.ProductInstanceUri != null)
            {
                Write(
                    component.ProductInstanceUri,
                    () => component.AddProductInstanceUri(context).ProductInstanceUri,
                    values.ProductInstanceUri);
                Write(
                    identification?.ProductInstanceUri,
                    () => identification?.AddProductInstanceUri(context).ProductInstanceUri,
                    values.ProductInstanceUri);
            }
            if (values.ProductCode != null)
            {
                Write(
                    component.ProductCode,
                    () => component.AddProductCode(context).ProductCode,
                    values.ProductCode);
                Write(
                    identification?.ProductCode,
                    () => identification?.AddProductCode(context).ProductCode,
                    values.ProductCode);
            }
            if (values.DeviceClass != null)
            {
                Write(
                    component.DeviceClass,
                    () => component.AddDeviceClass(context).DeviceClass,
                    values.DeviceClass);
                Write(
                    identification?.DeviceClass,
                    () => identification?.AddDeviceClass(context).DeviceClass,
                    values.DeviceClass);
            }
            if (values.AssetId != null)
            {
                Write(component.AssetId, () => component.AddAssetId(context).AssetId, values.AssetId);
                Write(
                    identification?.AssetId,
                    () => identification?.AddAssetId(context).AssetId,
                    values.AssetId);
            }
            if (!values.InitialOperationDate.IsNull)
            {
                Write(
                    identification?.InitialOperationDate,
                    () => identification?.AddInitialOperationDate(context).InitialOperationDate,
                    values.InitialOperationDate);
            }
        }

        /// <summary>
        /// Writes a property, creating it first when it does not exist.
        /// </summary>
        /// <typeparam name="T">The node type.</typeparam>
        private static void Write<T>(PropertyState<T>? property, Func<PropertyState<T>?> add, T value)
        {
            PropertyState<T>? target = property ?? add();
            if (target != null)
            {
                target.Value = value;
            }
        }
    }
}
