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
