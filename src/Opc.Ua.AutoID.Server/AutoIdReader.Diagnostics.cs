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
                    variable.OnReadValueAsync = ReadDiagnosticAsync;
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
        private ValueTask<AttributeReadResult> ReadDiagnosticAsync(
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
        private static BaseObjectState AddDiagnosticGroup(
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
        private void PublishPresence(ushort presence)
        {
            if (Device.Diagnostics is not { } diagnostics)
            {
                return;
            }
            lock (m_lock)
            {
                SetDiagnostic(diagnostics, "Presence", Variant.From(presence), DateTimeUtc.Now);
            }
            diagnostics.ClearChangeMasks(Runtime.SystemContext, true);
        }

        /// <summary>
        /// Projects the sink's single message column without maintaining another log.
        /// </summary>
        private void PublishLogEntry(string entry)
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
            logbook.ClearChangeMasks(Runtime.SystemContext, true);
        }

        /// <summary>
        /// Projects the first result of an access report, matching LastScanData's selection.
        /// </summary>
        private void PublishLastAccess(string client, string command, AccessResult result)
        {
            if (Device.Diagnostics == null)
            {
                return;
            }
            BaseObjectState access = DiagnosticGroup("LastAccess");
            bool hasTimestamp = (result.EncodingMask & (uint)AccessResultFields.Timestamp) != 0 &&
                !result.Timestamp.IsNull;
            DateTimeUtc timestamp = hasTimestamp ? result.Timestamp : DateTimeUtc.Now;
            bool hasIdentifier = (result.EncodingMask & (uint)AccessResultFields.Identifier) != 0;
            Variant identifier = hasIdentifier ? SnapshotDiagnosticData(result.Identifier) : Variant.Null;
            Variant data = Kind == AutoIdReaderKind.Rfid && result is RfidAccessResult rfidData &&
                (rfidData.EncodingMask & (uint)RfidAccessResultFields.RWData) != 0
                ? SnapshotDiagnosticData(rfidData.RWData)
                : Variant.Null;
            lock (m_lock)
            {
                SetDiagnostic(access, "Client", Variant.From(client), timestamp);
                SetDiagnostic(access, "Command", Variant.From(command), timestamp);
                SetDiagnostic(access, "Identifier", identifier, timestamp, hasIdentifier && !identifier.IsNull);
                SetDiagnostic(access, "Timestamp", Variant.From(result.Timestamp), timestamp, hasTimestamp);
                if (Kind == AutoIdReaderKind.Rfid && result is RfidAccessResult rfid)
                {
                    SetRfidDiagnostics(access, rfid, data, timestamp);
                }
            }
            access.ClearChangeMasks(Runtime.SystemContext, true);
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
            SetDiagnostic(access, "Polarization", Variant.From(result.Polarization), timestamp,
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
                    return Variant.FromStructure(new ScanDataEpc
                    {
                        PC = data.Epc.PC,
                        UId = SnapshotDiagnosticBytes(data.Epc.UId),
                        XPC_W1 = data.Epc.XPC_W1,
                        XPC_W2 = data.Epc.XPC_W2
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
                        return decoder.ReadVariant(null);
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
