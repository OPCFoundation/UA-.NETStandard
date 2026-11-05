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

namespace Opc.Ua.AutoID.Server
{
    public sealed partial class AutoIdReader
    {
        /// <summary>
        /// Creates the optional parameter subtree before registration.
        /// </summary>
        internal static void ConfigureParameters(
            ISystemContext context,
            AutoIdDeviceState device,
            AutoIdScanParameters parameters,
            ArrayOf<string> codes,
            bool control,
            ArrayOf<RolePermissionType> permissions,
            AutoIdReaderParameterCapabilities? capabilities = null)
        {
            device.AddRuntimeParameters(context);
            BaseObjectState runtime = device.RuntimeParameters!;
            ushort ns = context.NamespaceUris.GetIndexOrAppend(AutoIdModel.NamespaceUri);
            var group = new Di.FunctionalGroupState(runtime);
            group.Create(context, NodeId.Null, new QualifiedName("ScanSettings", ns),
                new LocalizedText("ScanSettings"), true);
            group.ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasComponent;
            runtime.AddChild(group);
            AddParameter(context, group, "Duration", Opc.Ua.DataTypeIds.Duration, Variant.From(parameters.Duration));
            AddParameter(context, group, "Cycles", Opc.Ua.DataTypeIds.Int32, Variant.From(parameters.Cycles));
            AddParameter(context, group, "DataAvailable",
                Opc.Ua.DataTypeIds.Boolean, Variant.From(parameters.DataAvailable));
            AddParameter(context, group, "CodeType",
                ExpandedNodeId.ToNodeId(DataTypeIds.CodeTypeDataType, context.NamespaceUris),
                Variant.From(parameters.CodeType));

            AddParameterSelection(context, runtime, "CodeTypes", codes, parameters.CodeTypes);
            ConfigureReaderParameters(context, runtime, parameters, capabilities);
            if (control)
            {
                device.AddScanActive(context);
                device.ScanActive!.Value = false;
                device.ScanActive.StatusCode = StatusCodes.Good;
                device.ScanActive.Timestamp = DateTimeUtc.Now;
                device.ScanActive.RolePermissions = permissions;
            }
            var children = new List<BaseInstanceState>();
            runtime.GetChildren(context, children);
            group.GetChildren(context, children);
            foreach (BaseInstanceState child in children)
            {
                if (child is BaseVariableState variable)
                {
                    variable.RolePermissions = permissions;
                }
            }
        }

        /// <summary>
        /// Adds one scalar parameter using the published model's metadata.
        /// </summary>
        private static void AddParameter(
            ISystemContext context, BaseObjectState parent, string name, NodeId dataType, Variant value)
        {
            var variable = new BaseDataVariableState(parent);
            variable.Create(context, NodeId.Null,
                new QualifiedName(name, context.NamespaceUris.GetIndexOrAppend(AutoIdModel.NamespaceUri)),
                new LocalizedText(name), true);
            variable.DataType = dataType;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.Value = value;
            variable.StatusCode = StatusCodes.Good;
            variable.Timestamp = DateTimeUtc.Now;
            parent.AddChild(variable);
        }

        /// <summary>
        /// Rejects invalid settings and unknown code indexes without calling the provider.
        /// </summary>
        internal static void ValidateParameters(AutoIdScanParameters parameters, ArrayOf<string> codes)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }
            if (double.IsNaN(parameters.Duration) || double.IsInfinity(parameters.Duration) ||
                parameters.Duration < 0 || parameters.Cycles < 0)
            {
                throw ServiceResultException.Create(StatusCodes.BadOutOfRange, "Invalid scan duration or cycles.");
            }
            var names = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < codes.Count; i++)
            {
                if (string.IsNullOrEmpty(codes[i]) || !names.Add(codes[i]))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError, "Code names must be nonempty and unique.");
                }
            }
            if (!names.Contains(parameters.CodeType) || parameters.CodeTypes.Count == 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadOutOfRange, "Select a supported code format and at least one code.");
            }
            var indexes = new HashSet<uint>();
            for (int i = 0; i < parameters.CodeTypes.Count; i++)
            {
                uint index = parameters.CodeTypes[i];
                if (index >= codes.Count || !indexes.Add(index))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadOutOfRange, "Code indexes must be supported and unique.");
                }
            }
        }

        /// <summary>
        /// Binds writes without allowing the service's post-handler cache update to overwrite a
        /// concurrent scan completion.
        /// </summary>
        private void BindParameters()
        {
            if (m_parameters == null)
            {
                return;
            }
            var variables = new List<BaseInstanceState>();
            Device.RuntimeParameters!.GetChildren(Runtime.SystemContext, variables);
            foreach (BaseInstanceState child in variables.ToArray())
            {
                if (child is BaseObjectState group)
                {
                    group.GetChildren(Runtime.SystemContext, variables);
                }
            }
            foreach (BaseInstanceState child in variables)
            {
                if (child is BaseVariableState variable &&
                    variable.BrowseName.NamespaceIndex ==
                        Runtime.SystemContext.NamespaceUris.GetIndex(AutoIdModel.NamespaceUri) &&
                    (variable.BrowseName.Name is
                        "Duration" or "Cycles" or "DataAvailable" or "CodeType" or "CodeTypes" ||
                        (variable.Parent == Device.RuntimeParameters &&
                            SupportsReaderParameter(variable.BrowseName.Name))))
                {
                    variable.OnWriteValueAsync = async (context, node, range, value, ct) =>
                    {
                        if (!range.IsNull)
                        {
                            return new AttributeWriteResult(StatusCodes.BadIndexRangeInvalid);
                        }
                        ServiceResult result = await WriteParameterAsync(variable, value, ct).ConfigureAwait(false);
                        return new AttributeWriteResult(result) { SuppressCacheUpdate = true };
                    };
                }
            }
            Device.ScanActive?.OnWriteValueAsync = async (context, node, range, value, ct) =>
            {
                if (!range.IsNull || !value.TryGetValue(out bool active))
                {
                    return new AttributeWriteResult(range.IsNull
                        ? StatusCodes.BadTypeMismatch : StatusCodes.BadIndexRangeInvalid);
                }
                if (!m_operations.TryEnter())
                {
                    return new AttributeWriteResult(StatusCodes.BadInvalidState);
                }
                try
                {
                    ServiceResult result;
                    if (active)
                    {
                        (ServiceResult service, AutoIdOperationStatusEnumeration status) =
                            await ScanStartCoreAsync(context, new ScanSettings(), ct, variableControl: true)
                                .ConfigureAwait(false);
                        result = ServiceResult.IsBad(service) ? service :
                            status == AutoIdOperationStatusEnumeration.SUCCESS ? ServiceResult.Good :
                            BadResult(StatusCodes.BadDeviceFailure, "The provider rejected the scan start: " + status);
                    }
                    else
                    {
                        await StopAsynchronousScanAsync(NodeId.Null, ct).ConfigureAwait(false);
                        lock (m_lock)
                        {
                            result = m_activity == ScanActivity.Idle ? ServiceResult.Good :
                                BadResult(StatusCodes.BadInvalidState,
                                    "A synchronous scan cannot be stopped through ScanActive.");
                        }
                    }
                    return new AttributeWriteResult(result) { SuppressCacheUpdate = true };
                }
                finally
                {
                    m_operations.Exit();
                }
            };
        }

        /// <summary>
        /// Serializes configuration commits with scan starts and stops.
        /// </summary>
        private async ValueTask<ServiceResult> WriteParameterAsync(
            BaseVariableState variable, Variant value, CancellationToken cancellationToken)
        {
            if (!m_operations.TryEnter())
            {
                return BadResult(StatusCodes.BadInvalidState, "The reader is shut down.");
            }
            try
            {
                return await CommitParameterAsync(variable, value, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                m_operations.Exit();
            }
        }

        /// <summary>
        /// Commits a validated parameter while the operation tracker keeps control resources alive.
        /// </summary>
        private async ValueTask<ServiceResult> CommitParameterAsync(
            BaseVariableState variable, Variant value, CancellationToken cancellationToken)
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, m_stopping.Token);
            await m_controlGate.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                if (!m_operations.TryEnter())
                {
                    return BadResult(StatusCodes.BadInvalidState, "The reader is shut down.");
                }
                try
                {
                    lock (m_lock)
                    {
                        if (m_activity != ScanActivity.Idle || m_parameterWrite)
                        {
                            return BadResult(StatusCodes.BadInvalidState,
                                "Scan parameters cannot change during a scan.");
                        }
                        m_parameterWrite = true;
                    }
                    try
                    {
                        if (variable.BrowseName.Name == "EnableAntennas" &&
                            value.TryGetValue(out uint antennaMask) && antennaMask == 0)
                        {
                            return ServiceResult.Good;
                        }
                        AutoIdScanParameters next = variable.BrowseName.Name switch
                        {
                            "Duration" when value.TryGetValue(out double duration) =>
                                m_parameters! with { Duration = duration },
                            "Cycles" when value.TryGetValue(out int cycles) => m_parameters! with { Cycles = cycles },
                            "DataAvailable" when value.TryGetValue(out bool available) =>
                                m_parameters! with { DataAvailable = available },
                            "CodeType" when value.TryGetValue(out string code) =>
                                m_parameters! with { CodeType = code },
                            "CodeTypes" when value.TryGetValue(out ArrayOf<uint> codes) =>
                                m_parameters! with { CodeTypes = codes },
                            "TagTypes" when value.TryGetValue(out ArrayOf<uint> tags) =>
                                m_parameters! with { TagTypes = tags },
                            "CodeTypesRWData" when value.TryGetValue(out ArrayOf<uint> formats) =>
                                m_parameters! with { CodeTypesRWData = formats },
                            "RfPower" when value.TryGetValue(out sbyte power) =>
                                m_parameters! with { RfPower = power },
                            "MinRssi" when value.TryGetValue(out int rssi) =>
                                m_parameters! with { MinRssi = rssi },
                            "EnableAntennas" when value.TryGetValue(out uint antennas) =>
                                m_parameters! with { EnableAntennas = antennas },
                            "TemplateName" when value.TryGetValue(out string template) =>
                                m_parameters! with { TemplateName = template },
                            "MatchCode" when value.TryGetValue(out string match) =>
                                m_parameters! with { MatchCode = match },
                            _ => throw ServiceResultException.Create(
                                StatusCodes.BadTypeMismatch, "Invalid parameter type.")
                        };
                        var provider = (IAutoIdScanParametersProvider)Provider;
                        ValidateParameters(next, provider.SupportedCodeTypes);
                        ValidateReaderParameters(next, Kind, m_parameterCapabilities);
                        next = SnapshotParameters(next);
                        await provider.WriteParametersAsync(SnapshotParameters(next), linked.Token)
                            .ConfigureAwait(false);
                        lock (m_lock)
                        {
                            m_parameters = next;
                        }
                        variable.WrappedValue = variable.BrowseName.Name switch
                        {
                            "CodeTypes" => Variant.From(CopyParameterArray(next.CodeTypes)),
                            "TagTypes" => Variant.From(CopyParameterArray(next.TagTypes)),
                            "CodeTypesRWData" => Variant.From(CopyParameterArray(next.CodeTypesRWData)),
                            _ => value
                        };
                        variable.Timestamp = DateTimeUtc.Now;
                        variable.StatusCode = StatusCodes.Good;
                        await variable
                            .ClearChangeMasksAsync(Runtime.SystemContext, false, CancellationToken.None)
                            .ConfigureAwait(false);
                        return ServiceResult.Good;
                    }
                    catch (ServiceResultException ex)
                    {
                        return ex.Result;
                    }
                    finally
                    {
                        lock (m_lock)
                        {
                            m_parameterWrite = false;
                        }
                    }
                }
                finally
                {
                    m_operations.Exit();
                }
            }
            finally
            {
                m_controlGate.Release();
            }
        }

        /// <summary>The last provider-committed standard parameter configuration.</summary>
        private AutoIdScanParameters? m_parameters;

        /// <summary>Prevents synchronous scan entry while a provider commit is pending.</summary>
        private bool m_parameterWrite;
    }
}
