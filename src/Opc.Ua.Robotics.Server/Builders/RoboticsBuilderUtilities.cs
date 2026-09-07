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
using Opc.Ua.Di;
using Opc.Ua.Di.Server.Builders;

namespace Opc.Ua.Robotics.Server.Builders
{
    internal static class RoboticsBuilderUtilities
    {
        public static QualifiedName NormalizeBrowseName(
            IRoboticsBuildContext context,
            string browseName)
        {
            if (string.IsNullOrWhiteSpace(browseName))
            {
                throw new ArgumentException(
                    "A non-empty browse name is required.",
                    nameof(browseName));
            }
            return new QualifiedName(browseName, context.InstanceNamespaceIndex);
        }

        public static QualifiedName NormalizeBrowseName(
            IRoboticsBuildContext context,
            QualifiedName browseName)
        {
            if (browseName.IsNull || string.IsNullOrWhiteSpace(browseName.Name))
            {
                throw new ArgumentException(
                    "A non-empty browse name is required.",
                    nameof(browseName));
            }
            return new QualifiedName(browseName.Name, context.InstanceNamespaceIndex);
        }

        public static void EnsureBrowseNameAvailable(
            ISystemContext context,
            NodeState parent,
            QualifiedName browseName)
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(context, children);
            for (int ii = 0; ii < children.Count; ii++)
            {
                if (children[ii].BrowseName == browseName)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadBrowseNameDuplicated,
                        "Parent '{0}' already contains a child named '{1}'.",
                        parent.BrowseName,
                        browseName);
                }
            }
        }

        public static TState AddContained<TState>(
            ISystemContext context,
            NodeState parent,
            QualifiedName browseName,
            Func<NodeState, QualifiedName, TState> factory)
            where TState : BaseInstanceState
        {
            return AddContained(
                context,
                parent,
                browseName,
                global::Opc.Ua.ReferenceTypeIds.HasComponent,
                factory);
        }

        public static TState AddContained<TState>(
            ISystemContext context,
            NodeState parent,
            QualifiedName browseName,
            NodeId referenceTypeId,
            Func<NodeState, QualifiedName, TState> factory)
            where TState : BaseInstanceState
        {
            EnsureBrowseNameAvailable(context, parent, browseName);
            TState state = factory(parent, browseName) ??
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError,
                    "The generated factory returned null for '{0}'.",
                    browseName);
            state.ReferenceTypeId = referenceTypeId;
            parent.AddChild(state);
            return state;
        }

        public static TState AddGeneratedChild<TState>(
            ISystemContext context,
            NodeState parent,
            Func<NodeState, TState> factory)
            where TState : BaseInstanceState
        {
            TState state = factory(parent) ??
                throw ServiceResultException.Create(
                    StatusCodes.BadUnexpectedError,
                    "The generated child factory returned null below '{0}'.",
                    parent.BrowseName);
            parent.AddChild(state);
            NodeId previousNodeId = context.AssignInstanceNodeId(state);
            context.AssignInstanceChildNodeIds(state, previousNodeId, parent);
            return state;
        }

        public static TChild FindRequiredChild<TChild>(
            ISystemContext context,
            NodeState parent,
            string browseName)
            where TChild : BaseInstanceState
        {
            TChild? child = FindChild<TChild>(context, parent, browseName);
            if (child == null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Generated mandatory child '{0}' is missing below '{1}'.",
                    browseName,
                    parent.BrowseName);
            }
            return child;
        }

        public static TChild? FindChild<TChild>(
            ISystemContext context,
            NodeState parent,
            string browseName)
            where TChild : BaseInstanceState
        {
            var children = new List<BaseInstanceState>();
            parent.GetChildren(context, children);
            for (int ii = 0; ii < children.Count; ii++)
            {
                if (children[ii] is TChild typed &&
                    string.Equals(
                        children[ii].BrowseName.Name,
                        browseName,
                        StringComparison.Ordinal))
                {
                    return typed;
                }
            }
            return null;
        }

        public static void SetComponentName(
            ComponentState state,
            ISystemContext context,
            LocalizedText componentName)
        {
            if (componentName.IsNull)
            {
                throw new ArgumentException(
                    "A non-null component name is required.",
                    nameof(componentName));
            }
            state.AddComponentName(context);
            SetValue(state.ComponentName!, componentName);
        }

        public static void SetProductCode(
            ComponentState state,
            ISystemContext context,
            string productCode)
        {
            if (productCode == null)
            {
                throw new ArgumentNullException(nameof(productCode));
            }
            state.AddProductCode(context);
            SetValue(state.ProductCode!, productCode);
        }

        public static void SetAssetId(
            ComponentState state,
            ISystemContext context,
            string assetId)
        {
            if (assetId == null)
            {
                throw new ArgumentNullException(nameof(assetId));
            }
            state.AddAssetId(context);
            SetValue(state.AssetId!, assetId);
        }

        public static void ApplyIdentification(
            ComponentState state,
            ISystemContext context,
            Action<DeviceIdentificationData> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }
            var data = new DeviceIdentificationData();
            configure(data);

            if (!data.Manufacturer.IsNull)
            {
                state.AddManufacturer(context);
                SetValue(state.Manufacturer!, data.Manufacturer);
            }
            if (data.ManufacturerUri != null)
            {
                state.AddManufacturerUri(context);
                SetValue(state.ManufacturerUri!, data.ManufacturerUri);
            }
            if (!data.Model.IsNull)
            {
                state.AddModel(context);
                SetValue(state.Model!, data.Model);
            }
            if (data.HardwareRevision != null)
            {
                state.AddHardwareRevision(context);
                SetValue(state.HardwareRevision!, data.HardwareRevision);
            }
            if (data.SoftwareRevision != null)
            {
                state.AddSoftwareRevision(context);
                SetValue(state.SoftwareRevision!, data.SoftwareRevision);
            }
            if (data.DeviceRevision != null)
            {
                state.AddDeviceRevision(context);
                SetValue(state.DeviceRevision!, data.DeviceRevision);
            }
            if (data.ProductCode != null)
            {
                state.AddProductCode(context);
                SetValue(state.ProductCode!, data.ProductCode);
            }
            if (data.DeviceManual != null)
            {
                state.AddDeviceManual(context);
                SetValue(state.DeviceManual!, data.DeviceManual);
            }
            if (data.DeviceClass != null)
            {
                state.AddDeviceClass(context);
                SetValue(state.DeviceClass!, data.DeviceClass);
            }
            if (data.SerialNumber != null)
            {
                state.AddSerialNumber(context);
                SetValue(state.SerialNumber!, data.SerialNumber);
            }
            if (data.ProductInstanceUri != null)
            {
                state.AddProductInstanceUri(context);
                SetValue(state.ProductInstanceUri!, data.ProductInstanceUri);
            }
            if (data.RevisionCounter.HasValue)
            {
                state.AddRevisionCounter(context);
                SetValue(state.RevisionCounter!, data.RevisionCounter.Value);
            }
        }

        public static void SetValue<T>(
            BaseDataVariableState<T> variable,
            T value,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default)
        {
            variable.Value = value;
            SetValueAttributes(variable, statusCode, timestamp);
        }

        public static void SetValue<T>(
            PropertyState<T> variable,
            T value,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default)
        {
            variable.Value = value;
            SetValueAttributes(variable, statusCode, timestamp);
        }

        public static void SetValue<T>(
            AnalogUnitState<T> variable,
            T value,
            StatusCode statusCode = default,
            DateTimeUtc timestamp = default)
        {
            variable.Value = value;
            SetValueAttributes(variable, statusCode, timestamp);
        }

        private static void SetValueAttributes(
            BaseVariableState variable,
            StatusCode statusCode,
            DateTimeUtc timestamp)
        {
            variable.StatusCode = statusCode;
            variable.Timestamp = NormalizeTimestamp(timestamp);
        }

        public static void SetAnalogValue(
            AnalogUnitState<double> variable,
            ISystemContext context,
            double value,
            EUInformation? engineeringUnits,
            global::Opc.Ua.Range? range,
            StatusCode statusCode,
            DateTimeUtc timestamp)
        {
            SetValue(variable, value, statusCode, timestamp);
            if (engineeringUnits != null)
            {
                PropertyState<EUInformation> property =
                    variable.EngineeringUnits ??
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "Generated mandatory EngineeringUnits is missing below '{0}'.",
                        variable.BrowseName);
                SetValue(property, engineeringUnits);
            }
            if (range != null)
            {
                variable.AddEURange(context);
                SetValue(variable.EURange!, range);
            }
        }

        public static void BindRead(
            BaseVariableState variable,
            Func<CancellationToken, ValueTask<DataValue>> read)
        {
            if (read == null)
            {
                throw new ArgumentNullException(nameof(read));
            }
            if (variable.OnReadValueAsync != null ||
                variable.OnSimpleReadValueAsync != null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "An asynchronous read callback is already attached to '{0}'.",
                    variable.BrowseName);
            }

            variable.AccessLevel |= AccessLevels.CurrentRead;
            variable.UserAccessLevel |= AccessLevels.CurrentRead;
            variable.OnReadValueAsync = async (
                context,
                _,
                indexRange,
                dataEncoding,
                cancellationToken) =>
            {
                DataValue dataValue = await read(cancellationToken).ConfigureAwait(false);
                if (dataValue.IsNull)
                {
                    return new AttributeReadResult(
                        StatusCodes.BadNoDataAvailable,
                        Variant.Null,
                        StatusCodes.BadNoDataAvailable,
                        DateTimeUtc.MinValue);
                }

                Variant value = dataValue.WrappedValue;
                if (!StatusCode.IsBad(dataValue.StatusCode))
                {
                    ServiceResult result = BaseVariableState.ApplyIndexRangeAndDataEncoding(
                        context,
                        indexRange,
                        dataEncoding,
                        ref value);
                    if (ServiceResult.IsBad(result))
                    {
                        return new AttributeReadResult(
                            result,
                            Variant.Null,
                            result.StatusCode,
                            dataValue.SourceTimestamp);
                    }

                    if (variable.CopyPolicy is
                        VariableCopyPolicy.CopyOnRead or VariableCopyPolicy.Always)
                    {
                        value = CoreUtils.Clone(value);
                    }
                }

                return new AttributeReadResult(
                    ServiceResult.Good,
                    value,
                    dataValue.StatusCode,
                    dataValue.SourceTimestamp);
            };
        }

        public static void BindWrite(
            BaseVariableState variable,
            Func<Variant, CancellationToken, ValueTask<ServiceResult>> write)
        {
            if (write == null)
            {
                throw new ArgumentNullException(nameof(write));
            }
            if (variable.OnWriteValueAsync != null ||
                variable.OnSimpleWriteValueAsync != null)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadInvalidState,
                    "An asynchronous write callback is already attached to '{0}'.",
                    variable.BrowseName);
            }

            variable.AccessLevel |= AccessLevels.CurrentWrite;
            variable.UserAccessLevel |= AccessLevels.CurrentWrite;
            variable.OnSimpleWriteValueAsync = async (_, _, value, cancellationToken) =>
            {
                ServiceResult result = await write(value, cancellationToken)
                    .ConfigureAwait(false);
                return new AttributeWriteResult(result ?? ServiceResult.Good);
            };
        }

        private static DateTimeUtc NormalizeTimestamp(DateTimeUtc timestamp)
        {
            return timestamp == DateTimeUtc.MinValue ? DateTimeUtc.Now : timestamp;
        }
    }
}
