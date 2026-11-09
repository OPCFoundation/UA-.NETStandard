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

namespace Opc.Ua.PlasticsRubber.Server
{
    /// <summary>
    /// Validates generated chapter 20 values without substituting device-family variants.
    /// </summary>
    internal static class ProductionDatasetValidation
    {
        /// <summary>
        /// Checks a single read source and its source-dependent name.
        /// </summary>
        internal static void Read(ProductionDatasetReadOptionsType value, ProductionDatasetOptions limits)
        {
            if (value.Storage is not (StorageEnumeration.PRODUCTION or
                StorageEnumeration.PREPARATION or StorageEnumeration.FILE_SYSTEM))
            {
                throw Error(StatusCodes.BadInvalidArgument, "A read requires exactly one storage location.");
            }
            Text(value.Name, limits, value.Storage == StorageEnumeration.FILE_SYSTEM);
            if (value.Storage != StorageEnumeration.FILE_SYSTEM && !string.IsNullOrEmpty(value.Name))
            {
                throw Error(StatusCodes.BadInvalidArgument, "Control-layer reads require an empty name.");
            }
            Preparation(value.Storage, limits);
        }

        /// <summary>
        /// Checks a nonzero destination mask without dropping any selected destinations.
        /// </summary>
        internal static void Write(ProductionDatasetWriteOptionsType value, ProductionDatasetOptions limits)
        {
            const StorageEnumeration known =
                StorageEnumeration.PRODUCTION | StorageEnumeration.PREPARATION | StorageEnumeration.FILE_SYSTEM;
            if (value.Storage == 0 || (value.Storage & ~known) != 0)
            {
                throw Error(StatusCodes.BadInvalidArgument, "A write requires a nonzero supported storage mask.");
            }
            Text(value.Name, limits, true);
            Components(value.Components, limits);
            Preparation(value.Storage, limits);
            if (value.Storage == StorageEnumeration.FILE_SYSTEM && !value.Components.IsEmpty)
            {
                throw Error(
                    StatusCodes.BadInvalidArgument, "Component selection requires a control-layer destination.");
            }
        }

        /// <summary>
        /// Validates lengths and all published component codes, preserving their multiplicity.
        /// </summary>
        internal static void Components(ArrayOf<ushort> components, ProductionDatasetOptions limits)
        {
            if (components.Count > limits.MaximumComponents)
            {
                throw Error(StatusCodes.BadEncodingLimitsExceeded, "Too many dataset components.");
            }
            foreach (ushort component in components)
            {
                if (component > 15)
                {
                    throw Error(StatusCodes.BadOutOfRange, "Unknown OPC 40083 component code.");
                }
            }
        }

        /// <summary>
        /// Validates all bounded descriptive fields before copying or storing them.
        /// </summary>
        internal static void Information(ProductionDatasetInformationType information, ProductionDatasetOptions limits)
        {
            if (information == null)
            {
                throw Error(StatusCodes.BadInvalidArgument, "Dataset information is required.");
            }
            Text(information.Name, limits);
            Text(information.Description, limits);
            Text(information.MESId, limits);
            Text(information.UserName, limits);
            Text(information.Manufacturer, limits);
            Text(information.SerialNumber, limits);
            Text(information.Model, limits);
            Text(information.ControllerName, limits);
            Text(information.UserMachineName, limits);
            Text(information.LocationName, limits);
            Text(information.MouldId, limits);
            Components(information.Components, limits);
            if (information.ProductName.Count > limits.MaximumComponents)
            {
                throw Error(StatusCodes.BadEncodingLimitsExceeded, "Too many product names.");
            }
            foreach (string product in information.ProductName)
            {
                Text(product, limits);
            }
        }

        /// <summary>
        /// Validates names and filters without interpreting them as filesystem paths.
        /// </summary>
        internal static void Text(string? text, ProductionDatasetOptions limits, bool required = false)
        {
            if (required && string.IsNullOrEmpty(text))
            {
                throw Error(StatusCodes.BadInvalidArgument, "A dataset name is required.");
            }
            if (text != null && text.Length > limits.MaximumStringLength)
            {
                throw Error(StatusCodes.BadEncodingLimitsExceeded, "The dataset string limit was exceeded.");
            }
        }

        /// <summary>
        /// Prevents an unselected preparation feature from silently accepting a write.
        /// </summary>
        internal static void Preparation(StorageEnumeration storage, ProductionDatasetOptions limits)
        {
            if ((storage & StorageEnumeration.PREPARATION) != 0 &&
                (limits.Features & ProductionDatasetFeatures.Preparation) == 0)
            {
                throw Error(StatusCodes.BadNotSupported, "The preparation control layer is not enabled.");
            }
        }

        /// <summary>
        /// Makes a detached information snapshot using the generated wire type.
        /// </summary>
        internal static ProductionDatasetInformationType Copy(ProductionDatasetInformationType information)
        {
            return (ProductionDatasetInformationType)information.Clone();
        }

        /// <summary>
        /// Creates a precise service failure rather than a success-shaped unsupported response.
        /// </summary>
        internal static ServiceResultException Error(StatusCode code, string message)
        {
            return ServiceResultException.Create(code, message);
        }
    }
}
