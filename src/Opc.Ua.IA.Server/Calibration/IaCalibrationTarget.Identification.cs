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
using Opc.Ua.Di;

namespace Opc.Ua.IA.Server
{
    public sealed partial class IaCalibrationTarget
    {
        /// <summary>
        /// Reuses generated DI nameplate properties under the IA Identification functional group.
        /// </summary>
        private void ConfigureIdentification()
        {
            IaCalibrationIdentification identity = Configuration.Identification;
            FunctionalGroupState group = State.Identification ??
                throw new InvalidOperationException("The generated identification group was removed.");

            // These unregistered interface views reuse DI's generated optional-property factories.
            // Their instance identity matches the real group so the owner's NodeId factory sees
            // the actual parent path. Only their configured properties enter the address space.
            var vendor = new IVendorNameplateState(State)
            {
                NodeId = group.NodeId,
                BrowseName = group.BrowseName,
                DisplayName = group.DisplayName
            };
            if (!identity.Manufacturer.IsNull)
            {
                vendor.AddManufacturer(m_context);
                vendor.Manufacturer!.Value = identity.Manufacturer;
            }
            if (identity.ManufacturerUri != null)
            {
                vendor.AddManufacturerUri(m_context);
                vendor.ManufacturerUri!.Value = identity.ManufacturerUri;
            }
            if (!identity.Model.IsNull)
            {
                vendor.AddModel(m_context);
                vendor.Model!.Value = identity.Model;
            }
            if (identity.ProductCode != null)
            {
                vendor.AddProductCode(m_context);
                vendor.ProductCode!.Value = identity.ProductCode;
            }
            if (identity.HardwareRevision != null)
            {
                vendor.AddHardwareRevision(m_context);
                vendor.HardwareRevision!.Value = identity.HardwareRevision;
            }
            if (identity.SoftwareRevision != null)
            {
                vendor.AddSoftwareRevision(m_context);
                vendor.SoftwareRevision!.Value = identity.SoftwareRevision;
            }
            if (identity.DeviceRevision != null)
            {
                vendor.AddDeviceRevision(m_context);
                vendor.DeviceRevision!.Value = identity.DeviceRevision;
            }
            if (identity.DeviceManual != null)
            {
                vendor.AddDeviceManual(m_context);
                vendor.DeviceManual!.Value = identity.DeviceManual;
            }
            if (identity.DeviceClass != null)
            {
                vendor.AddDeviceClass(m_context);
                vendor.DeviceClass!.Value = identity.DeviceClass;
            }
            if (identity.SerialNumber != null)
            {
                vendor.AddSerialNumber(m_context);
                vendor.SerialNumber!.Value = identity.SerialNumber;
            }
            if (identity.ProductInstanceUri != null)
            {
                vendor.AddProductInstanceUri(m_context);
                vendor.ProductInstanceUri!.Value = identity.ProductInstanceUri;
            }
            if (identity.RevisionCounter is int revision)
            {
                vendor.AddRevisionCounter(m_context);
                vendor.RevisionCounter!.Value = revision;
            }
            AttachNameplateProperties(vendor, group);

            var tag = new ITagNameplateState(State)
            {
                NodeId = group.NodeId,
                BrowseName = group.BrowseName,
                DisplayName = group.DisplayName
            };
            if (identity.AssetId != null)
            {
                tag.AddAssetId(m_context);
                tag.AssetId!.Value = identity.AssetId;
            }
            if (!identity.ComponentName.IsNull)
            {
                tag.AddComponentName(m_context);
                tag.ComponentName!.Value = identity.ComponentName;
            }
            AttachNameplateProperties(tag, group);
        }

        /// <summary>
        /// Moves only configured generated properties into the actual Identification group.
        /// </summary>
        private void AttachNameplateProperties(NodeState view, FunctionalGroupState group)
        {
            var properties = new List<BaseInstanceState>();
            view.GetChildren(m_context, properties);
            foreach (BaseInstanceState property in properties)
            {
                view.RemoveChild(property);
                group.AddChild(property);
            }
        }
    }
}
