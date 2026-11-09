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
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Opc.Ua.AutoID.Server
{
    public sealed partial class AutoIdReader
    {
        /// <summary>
        /// Creates the optional read-only diagnostics of OPC 30010 Tables 8 and 15.
        /// </summary>
        internal static void ConfigureDiagnostics(ISystemContext context, AutoIdDeviceState device)
        {
            device.AddDiagnostics(context);
            BaseObjectState diagnostics = device.Diagnostics!;
            AddDiagnostic(context, diagnostics, "Presence", Opc.Ua.DataTypeIds.UInt16);
            BaseObjectState logbook = AddDiagnosticGroup(context, diagnostics, "Logbook");
            AddDiagnostic(context, logbook, "LogColumns", Opc.Ua.DataTypeIds.String);
            AddDiagnostic(context, logbook, "LastLogEntry", Opc.Ua.DataTypeIds.String);
            BaseVariableState columns = Diagnostic(context, logbook, "LogColumns");
            columns.Value = Variant.From("Message");
            columns.StatusCode = StatusCodes.Good;
            columns.Timestamp = DateTimeUtc.Now;
            BaseObjectState access = AddDiagnosticGroup(context, diagnostics, "LastAccess");
            AddDiagnostic(context, access, "Client", Opc.Ua.DataTypeIds.String);
            AddDiagnostic(context, access, "Command", Opc.Ua.DataTypeIds.String);
            AddDiagnostic(context, access, "Identifier", Opc.Ua.DataTypeIds.BaseDataType);
            AddDiagnostic(context, access, "Timestamp", Opc.Ua.DataTypeIds.UtcTime);
            if (device is RfidReaderDeviceState)
            {
                AddDiagnostic(context, access, "RWData", Opc.Ua.DataTypeIds.BaseDataType);
                AddDiagnostic(context, access, "Antenna", Opc.Ua.DataTypeIds.Int32);
                AddDiagnostic(context, access, "CurrentPowerLevel", Opc.Ua.DataTypeIds.Int32);
                AddDiagnostic(context, access, "PC", Opc.Ua.DataTypeIds.UInt16);
                AddDiagnostic(context, access, "Polarization", Opc.Ua.DataTypeIds.String);
                AddDiagnostic(context, access, "Strength", Opc.Ua.DataTypeIds.Int32);
            }
        }

        /// <summary>
        /// Binds reads to the reader's serialized projection, including each value's quality and timestamp.
        /// </summary>
        private void BindDiagnostics()
        {
            if (Device.Diagnostics == null)
            {
                return;
            }
            var pending = new Queue<BaseInstanceState>();
            pending.Enqueue(Device.Diagnostics);
            while (pending.Count > 0)
            {
                BaseInstanceState node = pending.Dequeue();
                if (node is BaseVariableState variable)
                {
                    variable.OnReadValueAsync = ReadCachedValueAsync;
                }
                var children = new List<BaseInstanceState>();
                node.GetChildren(Runtime.SystemContext, children);
                foreach (BaseInstanceState child in children)
                {
                    pending.Enqueue(child);
                }
            }
        }

        /// <summary>
        /// Reads a complete cached value under the same lock as its report update.
        /// </summary>
        private ValueTask<AttributeReadResult> ReadCachedValueAsync(
            ISystemContext context,
            NodeState node,
            NumericRange range,
            QualifiedName encoding,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var variable = (BaseVariableState)node;
            Variant value;
            StatusCode status;
            DateTimeUtc timestamp;
            lock (m_lock)
            {
                value = variable.Value;
                status = variable.StatusCode;
                timestamp = variable.Timestamp;
            }
            if (StatusCode.IsBad(status))
            {
                return new ValueTask<AttributeReadResult>(
                    new AttributeReadResult(ServiceResult.Good, value, status, timestamp));
            }
            ServiceResult result = BaseVariableState.ApplyIndexRangeAndDataEncoding(
                context, range, encoding, ref value);
            if (ServiceResult.IsGood(result) &&
                variable.CopyPolicy is VariableCopyPolicy.CopyOnRead or VariableCopyPolicy.Always)
            {
                value = CoreUtils.Clone(value);
            }
            return new ValueTask<AttributeReadResult>(new AttributeReadResult(result, value, status, timestamp));
        }

        /// <summary>
        /// Adds a DI FunctionalGroup using the AutoID-qualified declaration name.
        /// </summary>
        private static Di.FunctionalGroupState AddDiagnosticGroup(
            ISystemContext context, BaseObjectState parent, string name)
        {
            var group = new Di.FunctionalGroupState(parent);
            group.Create(context, NodeId.Null, DiagnosticName(context, name), new LocalizedText(name), true);
            group.ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasComponent;
            parent.AddChild(group);
            return group;
        }

        /// <summary>
        /// Adds a read-only scalar diagnostic with no invented initial measurement.
        /// </summary>
        private static void AddDiagnostic(
            ISystemContext context, BaseObjectState parent, string name, NodeId dataType)
        {
            var variable = new BaseDataVariableState(parent);
            variable.Create(context, NodeId.Null, DiagnosticName(context, name), new LocalizedText(name), true);
            variable.ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasComponent;
            variable.DataType = dataType;
            variable.ValueRank = ValueRanks.Scalar;
            variable.AccessLevel = variable.UserAccessLevel = AccessLevels.CurrentRead;
            variable.StatusCode = StatusCodes.UncertainInitialValue;
            parent.AddChild(variable);
        }

        /// <summary>
        /// Finds a configured diagnostic variable by its qualified name.
        /// </summary>
        private static BaseVariableState Diagnostic(ISystemContext context, BaseObjectState group, string name)
        {
            return (BaseVariableState)group.FindChildWithQualifiedName(context, DiagnosticName(context, name))!;
        }

        /// <summary>
        /// Gets the namespace-qualified name of a standard AutoID diagnostic.
        /// </summary>
        private static QualifiedName DiagnosticName(ISystemContext context, string name)
        {
            return new QualifiedName(name, context.NamespaceUris.GetIndexOrAppend(AutoIdModel.NamespaceUri));
        }

        /// <summary>
        /// Finds a configured diagnostic group by its qualified name.
        /// </summary>
        private BaseObjectState DiagnosticGroup(string name)
        {
            return (BaseObjectState)Device.Diagnostics!.FindChildWithQualifiedName(
                Runtime.SystemContext, DiagnosticName(Runtime.SystemContext, name))!;
        }

        /// <summary>
        /// Stores one diagnostic value; callers serialize the complete logical update.
        /// </summary>
        private void SetDiagnostic(
            BaseObjectState group, string name, Variant value, DateTimeUtc timestamp, bool available = true)
        {
            BaseVariableState variable = Diagnostic(Runtime.SystemContext, group, name);
            variable.Value = available ? value : Variant.Null;
            variable.StatusCode = available ? StatusCodes.Good : StatusCodes.BadNoData;
            variable.Timestamp = timestamp;
        }

        /// <summary>
        /// Projects the existing presence report without deriving a count from scan results.
        /// </summary>
        private async ValueTask PublishPresenceAsync(ushort presence, CancellationToken cancellationToken)
        {
            if (Device.Diagnostics is not { } diagnostics)
            {
                return;
            }
            lock (m_lock)
            {
                SetDiagnostic(diagnostics, "Presence", Variant.From(presence), DateTimeUtc.Now);
            }
            await diagnostics.ClearChangeMasksAsync(Runtime.SystemContext, true, cancellationToken)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Projects the sink's single message column without maintaining another log.
        /// </summary>
        private async ValueTask PublishLogEntryAsync(string entry, CancellationToken cancellationToken)
        {
            if (Device.Diagnostics == null)
            {
                return;
            }
            BaseObjectState logbook = DiagnosticGroup("Logbook");
            lock (m_lock)
            {
                SetDiagnostic(logbook, "LastLogEntry", Variant.From(entry), DateTimeUtc.Now);
            }
            await logbook.ClearChangeMasksAsync(Runtime.SystemContext, true, cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Publishes an access diagnostic update through asynchronous state-change sinks.
        /// </summary>
        private async ValueTask PublishLastAccessAsync(
            string client, string command, AccessResult result, bool tolerateDiagnosticCopyFailure = false,
            CancellationToken cancellationToken = default)
        {
            BaseObjectState? access = await PrepareLastAccessAsync(
                client, command, result, tolerateDiagnosticCopyFailure, cancellationToken).ConfigureAwait(false);
            if (access != null)
            {
                await access.ClearChangeMasksAsync(Runtime.SystemContext, true, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Updates the cached access diagnostics and returns their notification root.
        /// </summary>
        private async ValueTask<BaseObjectState?> PrepareLastAccessAsync(
            string client, string command, AccessResult result, bool tolerateDiagnosticCopyFailure,
            CancellationToken cancellationToken)
        {
            if (Device.Diagnostics == null)
            {
                return null;
            }
            BaseObjectState access = DiagnosticGroup("LastAccess");
            DateTimeUtc acquired = result.Timestamp;
            bool hasTimestamp = (result.EncodingMask & (uint)AccessResultFields.Timestamp) != 0 &&
                !acquired.IsNull;
            DateTimeUtc timestamp = hasTimestamp ? acquired : DateTimeUtc.Now;
            bool hasIdentifier = (result.EncodingMask & (uint)AccessResultFields.Identifier) != 0;
            RfidAccessResult? rfidSnapshot = result is RfidAccessResult rfid ? new RfidAccessResult
            {
                EncodingMask = rfid.EncodingMask,
                RWData = rfid.RWData,
                CodeTypeRWData = rfid.CodeTypeRWData,
                Antenna = rfid.Antenna,
                CurrentPowerLevel = rfid.CurrentPowerLevel,
                PC = rfid.PC,
                Polarization = rfid.Polarization,
                Strength = rfid.Strength
            } : null;
            Variant identifier;
            Variant data;
            try
            {
                identifier = hasIdentifier ? SnapshotDiagnosticData(result.Identifier) : Variant.Null;
                data = Kind == AutoIdReaderKind.Rfid && rfidSnapshot != null &&
                    (rfidSnapshot.EncodingMask & (uint)RfidAccessResultFields.RWData) != 0
                    ? await FormatDiagnosticDataAsync(rfidSnapshot, cancellationToken).ConfigureAwait(false)
                    : Variant.Null;
            }
            catch (ServiceResultException ex) when (tolerateDiagnosticCopyFailure &&
                (ex.StatusCode == StatusCodes.BadEncodingError ||
                    ex.StatusCode == StatusCodes.BadDecodingError ||
                    ex.StatusCode == StatusCodes.BadEncodingLimitsExceeded))
            {
                m_logger.ReaderShutdownStepFailed(ex, Name.Name, "copying the access diagnostics");
                return null;
            }
            cancellationToken.ThrowIfCancellationRequested();
            lock (m_lock)
            {
                SetDiagnostic(access, "Client", Variant.From(client), timestamp);
                SetDiagnostic(access, "Command", Variant.From(command), timestamp);
                SetDiagnostic(access, "Identifier", identifier, timestamp, hasIdentifier && !identifier.IsNull);
                SetDiagnostic(access, "Timestamp", Variant.From(acquired), timestamp, hasTimestamp);
                if (Kind == AutoIdReaderKind.Rfid && rfidSnapshot != null)
                {
                    SetRfidDiagnostics(access, rfidSnapshot, data, timestamp);
                }
            }
            return access;
        }

        /// <summary>
        /// Formats a copied user-data value without changing the access report retained by event consumers.
        /// </summary>
        private async ValueTask<Variant> FormatDiagnosticDataAsync(
            RfidAccessResult result, CancellationToken cancellationToken)
        {
            if (m_parameterCapabilities is not { CodeTypesRWData.IsNull: false })
            {
                return SnapshotDiagnosticData(result.RWData);
            }
            ArrayOf<string> selected;
            lock (m_lock)
            {
                ArrayOf<uint> indexes = m_parameters!.CodeTypesRWData;
                if (indexes.Count == 0)
                {
                    return Variant.Null;
                }
                var names = new string[indexes.Count];
                for (int i = 0; i < names.Length; i++)
                {
                    names[i] = m_parameterCapabilities.CodeTypesRWData[(int)indexes[i]];
                }
                selected = names.ToArrayOf();
            }
            try
            {
                ScanData input = SnapshotDiagnosticScanData(result.RWData);
                string source = (result.EncodingMask & (uint)RfidAccessResultFields.CodeTypeRWData) != 0
                    ? result.CodeTypeRWData ?? string.Empty : string.Empty;
                var formatter = (IAutoIdRfidDiagnosticFormatter)Provider;
                AutoIdRfidDiagnosticData formatted = await formatter.FormatRWDataAsync(
                    input, source, CopyParameterArray(selected), cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                ValidateFormattedDiagnostic(formatted, selected);
                Variant snapshot = SnapshotDiagnosticData(formatted.Data);
                if (snapshot.IsNull)
                {
                    throw new ServiceResultException(
                        StatusCodes.BadEncodingError, "The formatter returned a null diagnostic value.");
                }
                return snapshot;
            }
            catch (Exception ex) when (
                ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                m_logger.DiagnosticFormattingFailed(
                    ex is ServiceResultException service ? service.StatusCode : StatusCodes.BadUnexpectedError);
                return Variant.Null;
            }
        }

        /// <summary>
        /// Copies ScanData storage without trusting an externally overridden Clone implementation.
        /// </summary>
        private ScanData SnapshotDiagnosticScanData(ScanData? data)
        {
            if (data == null)
            {
                throw new ServiceResultException(StatusCodes.BadEncodingError, "The reported user data is null.");
            }
            return data.SwitchField switch
            {
                ScanDataFields.ByteString => new ScanData
                {
                    SwitchField = ScanDataFields.ByteString,
                    ByteString = SnapshotDiagnosticBytes(data.ByteString)
                },
                ScanDataFields.String => new ScanData { SwitchField = ScanDataFields.String, String = data.String },
                ScanDataFields.Epc when data.Epc is { } epc => new ScanData
                {
                    SwitchField = ScanDataFields.Epc,
                    Epc = new ScanDataEpc
                    {
                        PC = epc.PC,
                        UId = SnapshotDiagnosticBytes(epc.UId),
                        XPC_W1 = epc.XPC_W1,
                        XPC_W2 = epc.XPC_W2
                    }
                },
                ScanDataFields.Custom => new ScanData
                {
                    SwitchField = ScanDataFields.Custom,
                    Custom = SnapshotDiagnosticData(data)
                },
                _ => throw new ServiceResultException(
                    StatusCodes.BadEncodingError, "The reported diagnostic data has no valid union value.")
            };
        }

        /// <summary>
        /// Requires one selected code type and the union field defined by OPC 30010 Table 32.
        /// </summary>
        private static void ValidateFormattedDiagnostic(
            AutoIdRfidDiagnosticData formatted, ArrayOf<string> selected)
        {
            bool found = false;
            if (formatted != null)
            {
                foreach (string name in selected)
                {
                    found |= string.Equals(name, formatted.CodeType, StringComparison.Ordinal);
                }
            }
            if (!found || formatted == null)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingError, "The formatter returned an unselected diagnostic format.");
            }
            bool valid = formatted.CodeType switch
            {
                "RAW:BYTES" or "UID" or "GS1" or "ASC" =>
                    formatted.Data.SwitchField == ScanDataFields.ByteString && !formatted.Data.ByteString.IsNull,
                "RAW:STRING" or "URI" =>
                    formatted.Data.SwitchField == ScanDataFields.String && formatted.Data.String != null,
                "EPC" => formatted.Data.SwitchField == ScanDataFields.Epc && formatted.Data.Epc != null,
                _ => formatted.CodeType.StartsWith("CUSTOM:", StringComparison.Ordinal) &&
                    (formatted.Data.SwitchField switch
                    {
                        ScanDataFields.ByteString => !formatted.Data.ByteString.IsNull,
                        ScanDataFields.String => formatted.Data.String != null,
                        ScanDataFields.Custom => !formatted.Data.Custom.IsNull,
                        _ => false
                    })
            };
            if (!valid)
            {
                throw new ServiceResultException(
                    StatusCodes.BadEncodingError, "The formatter's code type does not match its union field.");
            }
        }

        /// <summary>
        /// Projects only present RFID fields, clearing previous data when a field is omitted.
        /// </summary>
        private void SetRfidDiagnostics(
            BaseObjectState access, RfidAccessResult result, Variant data, DateTimeUtc timestamp)
        {
            uint fields = result.EncodingMask;
            SetDiagnostic(access, "RWData", data, timestamp,
                (fields & (uint)RfidAccessResultFields.RWData) != 0 && !data.IsNull);
            SetDiagnostic(access, "Antenna", Variant.From(result.Antenna), timestamp,
                (fields & (uint)RfidAccessResultFields.Antenna) != 0);
            SetDiagnostic(access, "CurrentPowerLevel", Variant.From(result.CurrentPowerLevel), timestamp,
                (fields & (uint)RfidAccessResultFields.CurrentPowerLevel) != 0);
            SetDiagnostic(access, "PC", Variant.From(result.PC), timestamp,
                (fields & (uint)RfidAccessResultFields.PC) != 0);
            SetDiagnostic(access, "Polarization", Variant.From(result.Polarization ?? string.Empty), timestamp,
                (fields & (uint)RfidAccessResultFields.Polarization) != 0);
            SetDiagnostic(access, "Strength", Variant.From(result.Strength), timestamp,
                (fields & (uint)RfidAccessResultFields.Strength) != 0);
        }

        /// <summary>
        /// Copies mutable report storage before retaining it in the diagnostic projection.
        /// </summary>
        private Variant SnapshotDiagnosticData(ScanData? data)
        {
            if (data is null)
            {
                return Variant.Null;
            }
            switch (data.SwitchField)
            {
                case ScanDataFields.ByteString:
                    return Variant.From(SnapshotDiagnosticBytes(data.ByteString));
                case ScanDataFields.Epc:
                    if (data.Epc is not { } epc)
                    {
                        return VariantOf(data);
                    }
                    return Variant.FromStructure(new ScanDataEpc
                    {
                        PC = epc.PC,
                        UId = SnapshotDiagnosticBytes(epc.UId),
                        XPC_W1 = epc.XPC_W1,
                        XPC_W2 = epc.XPC_W2
                    });
                case ScanDataFields.Custom:
                    // Variant.Copy does not copy scalar ByteString or every nested mutable payload.
                    IServiceMessageContext context = Runtime.SystemContext.AsMessageContext();
                    using (var stream = new MemoryStream())
                    {
                        using (var encoder = new BinaryEncoder(stream, context, true))
                        {
                            encoder.WriteVariant(null, data.Custom);
                        }
                        stream.Position = 0;
                        using var decoder = new BinaryDecoder(stream, context, true);
                        Variant snapshot = decoder.ReadVariant(null);
                        if (!data.Custom.Equals(snapshot))
                        {
                            throw new ServiceResultException(
                                StatusCodes.BadEncodingError,
                                "The custom diagnostic value cannot be copied without changing its semantic value.");
                        }
                        return snapshot;
                    }
                default:
                    return VariantOf(data);
            }
        }

        /// <summary>
        /// Copies the bytes while preserving the distinction between null and empty.
        /// </summary>
        private static ByteString SnapshotDiagnosticBytes(ByteString value)
        {
            return value.IsNull ? default : ByteString.From(value.Span);
        }
    }
}
