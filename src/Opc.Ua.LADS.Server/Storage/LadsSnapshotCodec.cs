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
using System.Text;
using System.Xml;

namespace Opc.Ua.LADS.Server
{
    /// <summary>
    /// Reflection-free, strict schema-one snapshot encoding shared by both store providers.
    /// </summary>
    internal static class LadsSnapshotCodec
    {
        /// <summary>
        /// Encodes a validated device without allowing the output buffer to exceed the byte limit.
        /// </summary>
        internal static byte[] Encode(LadsStoredDeviceData data, string identity, int maxBytes)
        {
            ValidateLimit(maxBytes);
            ValidateIdentity(identity);
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }
            Validate(data, identity);
            using var buffer = new LadsSnapshotBuffer(maxBytes);
            using (XmlWriter writer = XmlWriter.Create(buffer, new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false, true),
                CloseOutput = false,
                CheckCharacters = true,
                NewLineHandling = NewLineHandling.Entitize
            }))
            {
                writer.WriteStartElement("LadsDevice");
                WriteString(writer, "SchemaVersion", XmlConvert.ToString(data.SchemaVersion));
                WriteString(writer, "Identity", data.Identity);
                WriteString(writer, "OperationCycles", XmlConvert.ToString(data.OperationCycles));
                WriteDouble(writer, "OperationDurationMilliseconds", data.OperationDurationMilliseconds);
                WriteDouble(writer, "PowerOnDurationMilliseconds", data.PowerOnDurationMilliseconds);
                WriteArray(writer, "FunctionalUnits", data.FunctionalUnits, WriteUnit);
                WriteArray(writer, "MaintenanceTasks", data.MaintenanceTasks, WriteMaintenance);
                writer.WriteEndElement();
            }
            return buffer.ToArray();
        }

        /// <summary>
        /// Decodes one bounded document, rejecting unsupported versions, unknown elements and trailing content.
        /// </summary>
        internal static LadsStoredDeviceData Decode(byte[] content, string identity, int maxBytes)
        {
            ValidateLimit(maxBytes);
            ValidateIdentity(identity);
            if (content.Length > maxBytes)
            {
                throw new InvalidDataException("The LADS snapshot exceeds the configured byte limit.");
            }
            using var buffer = new MemoryStream(content, writable: false);
            using XmlReader reader = XmlReader.Create(buffer, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = maxBytes,
                MaxCharactersFromEntities = maxBytes,
                CheckCharacters = true,
                CloseInput = false
            });
            ReadStart(reader, "LadsDevice");
            int version = XmlConvert.ToInt32(ReadText(reader, "SchemaVersion"));
            if (version != 1)
            {
                throw new InvalidDataException("Unsupported LADS snapshot schema version.");
            }
            var data = new LadsStoredDeviceData
            {
                SchemaVersion = version,
                Identity = ReadText(reader, "Identity"),
                OperationCycles = XmlConvert.ToUInt32(ReadText(reader, "OperationCycles")),
                OperationDurationMilliseconds = ReadDouble(reader, "OperationDurationMilliseconds"),
                PowerOnDurationMilliseconds = ReadDouble(reader, "PowerOnDurationMilliseconds"),
                FunctionalUnits = ReadArray(reader, "FunctionalUnits", ReadUnit),
                MaintenanceTasks = ReadArray(reader, "MaintenanceTasks", ReadMaintenance)
            };
            reader.ReadEndElement();
            if (reader.MoveToContent() != XmlNodeType.None)
            {
                throw new InvalidDataException("Unexpected content after the LADS snapshot.");
            }
            Validate(data, identity);
            return data;
        }

        /// <summary>
        /// Rejects empty device identities before accessing a provider.
        /// </summary>
        internal static void ValidateIdentity(string identity)
        {
            if (string.IsNullOrWhiteSpace(identity))
            {
                throw new ArgumentException("A stable device identity is required.", nameof(identity));
            }
        }

        /// <summary>
        /// Rejects invalid byte limits.
        /// </summary>
        internal static void ValidateLimit(int maxBytes)
        {
            if (maxBytes <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxBytes));
            }
        }

        /// <summary>
        /// Checks identities, unique collection keys, enums and nonnegative finite durations.
        /// </summary>
        private static void Validate(LadsStoredDeviceData data, string identity)
        {
            if (data.SchemaVersion != 1)
            {
                throw new InvalidDataException("Unsupported LADS snapshot schema version.");
            }
            if (!string.Equals(data.Identity, identity, StringComparison.Ordinal))
            {
                throw new InvalidDataException("The snapshot identity does not match the device storage key.");
            }
            ValidateDuration(data.OperationDurationMilliseconds);
            ValidateDuration(data.PowerOnDurationMilliseconds);
            ValidateUnique(data.FunctionalUnits, static unit => unit.Name);
            ValidateUnique(data.MaintenanceTasks, static task => task.Name);
            foreach (LadsStoredFunctionalUnitData unit in data.FunctionalUnits)
            {
                ValidateUnique(unit.Templates, static template => template.Id);
                ValidateUnique(unit.Results, static result => result.RunId);
                ValidateUnique(unit.RemovedTemplateIds, static id => id);
                var removed = new HashSet<string>(StringComparer.Ordinal);
                foreach (string id in unit.RemovedTemplateIds)
                {
                    removed.Add(id);
                }
                foreach (LadsStoredTemplateData template in unit.Templates)
                {
                    ValidateTemplate(template);
                    if (removed.Contains(template.Id))
                    {
                        throw new InvalidDataException("A stored template is also marked as deleted.");
                    }
                }
                foreach (LadsStoredResultData result in unit.Results)
                {
                    ValidateTemplate(result.Template);
                    if (result.Outcome is not (LadsProgramOutcome.Completed or
                        LadsProgramOutcome.Stopped or LadsProgramOutcome.Aborted))
                    {
                        throw new InvalidDataException("The stored program outcome is invalid.");
                    }
                    ValidateDuration(result.TotalRuntimeMilliseconds);
                    ValidateDuration(result.TotalPauseTimeMilliseconds);
                    if (result.TotalPauseTimeMilliseconds > result.TotalRuntimeMilliseconds ||
                        result.Stopped.CompareTo(result.Started) < 0)
                    {
                        throw new InvalidDataException("Stored result timing is inconsistent.");
                    }
                    ValidateUnique(result.Files, static file => file.Name);
                    if (result.SupervisoryJobId == null || result.SupervisoryTaskId == null)
                    {
                        throw new InvalidDataException("Supervisory identifiers must not be null.");
                    }
                }
            }
            foreach (LadsStoredMaintenanceData task in data.MaintenanceTasks)
            {
                if (task.State is not (LadsMaintenanceStateValue.Planned or
                    LadsMaintenanceStateValue.Executing or LadsMaintenanceStateValue.Finished))
                {
                    throw new InvalidDataException("The stored maintenance state is invalid.");
                }
                if (task.LastResult is not (MaintenanceTaskResultEnum.Success or
                    MaintenanceTaskResultEnum.Failure or MaintenanceTaskResultEnum.Undetermined))
                {
                    throw new InvalidDataException("The stored maintenance result is invalid.");
                }
            }
        }

        /// <summary>
        /// Validates required template metadata without interpreting executable program content.
        /// </summary>
        private static void ValidateTemplate(LadsStoredTemplateData template)
        {
            if (template == null || string.IsNullOrWhiteSpace(template.Id) ||
                template.Author == null || template.Version == null)
            {
                throw new InvalidDataException("Required template metadata is missing.");
            }
        }

        /// <summary>
        /// Validates collection keys and rejects null elements and duplicate identifiers.
        /// </summary>
        /// <typeparam name="T">The type of collection element.</typeparam>
        private static void ValidateUnique<T>(ArrayOf<T> values, Func<T, string> keySelector)
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (T value in values)
            {
                if (value is null)
                {
                    throw new InvalidDataException("A snapshot collection contains a null element.");
                }
                string key = keySelector(value);
                if (string.IsNullOrWhiteSpace(key) || !keys.Add(key))
                {
                    throw new InvalidDataException("A snapshot collection contains an empty or duplicate identifier.");
                }
            }
        }

        /// <summary>
        /// Rejects negative, infinite, not-a-number and out-of-range durations.
        /// </summary>
        private static void ValidateDuration(double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) ||
                value < 0 || value > TimeSpan.MaxValue.TotalMilliseconds)
            {
                throw new InvalidDataException("Snapshot durations must be finite, nonnegative and fit in a TimeSpan.");
            }
        }

        /// <summary>
        /// Writes a functional unit body.
        /// </summary>
        private static void WriteUnit(XmlWriter writer, LadsStoredFunctionalUnitData unit)
        {
            WriteString(writer, "Name", unit.Name);
            WriteString(writer, "InterruptedRunId", unit.InterruptedRunId);
            WriteString(writer, "LastInterruptedRunId", unit.LastInterruptedRunId);
            WriteArray(writer, "Templates", unit.Templates, WriteTemplate);
            WriteArray(writer, "RemovedTemplateIds", unit.RemovedTemplateIds,
                static (xml, id) => WriteString(xml, "Id", id));
            WriteArray(writer, "Results", unit.Results, WriteResult);
        }

        /// <summary>
        /// Reads a functional unit body.
        /// </summary>
        private static LadsStoredFunctionalUnitData ReadUnit(XmlReader reader)
        {
            return new LadsStoredFunctionalUnitData
            {
                Name = ReadText(reader, "Name"),
                InterruptedRunId = ReadString(reader, "InterruptedRunId"),
                LastInterruptedRunId = ReadString(reader, "LastInterruptedRunId"),
                Templates = ReadArray(reader, "Templates", ReadTemplate),
                RemovedTemplateIds = ReadArray(reader, "RemovedTemplateIds", static xml => ReadText(xml, "Id")),
                Results = ReadArray(reader, "Results", ReadResult)
            };
        }

        /// <summary>
        /// Writes template metadata and serialized program bytes.
        /// </summary>
        private static void WriteTemplate(XmlWriter writer, LadsStoredTemplateData template)
        {
            WriteString(writer, "Id", template.Id);
            WriteString(writer, "Author", template.Author);
            WriteString(writer, "Version", template.Version);
            WriteLocalizedText(writer, "Description", template.Description);
            WriteString(writer, "SupervisoryTemplateId", template.SupervisoryTemplateId);
            WriteDate(writer, "Created", template.Created);
            WriteDate(writer, "Modified", template.Modified);
            WriteBytes(writer, "Content", template.Content);
        }

        /// <summary>
        /// Reads template metadata and serialized program bytes.
        /// </summary>
        private static LadsStoredTemplateData ReadTemplate(XmlReader reader)
        {
            return new LadsStoredTemplateData
            {
                Id = ReadText(reader, "Id"),
                Author = ReadText(reader, "Author"),
                Version = ReadText(reader, "Version"),
                Description = ReadLocalizedText(reader, "Description"),
                SupervisoryTemplateId = ReadString(reader, "SupervisoryTemplateId"),
                Created = ReadDate(reader, "Created"),
                Modified = ReadDate(reader, "Modified"),
                Content = ReadBytes(reader, "Content")
            };
        }

        /// <summary>
        /// Writes a result with an independent template snapshot and embedded result files.
        /// </summary>
        private static void WriteResult(XmlWriter writer, LadsStoredResultData result)
        {
            WriteString(writer, "RunId", result.RunId);
            writer.WriteStartElement("Template");
            WriteTemplate(writer, result.Template);
            writer.WriteEndElement();
            WriteString(writer, "Outcome", XmlConvert.ToString((int)result.Outcome));
            WriteDate(writer, "Started", result.Started);
            WriteDate(writer, "Stopped", result.Stopped);
            WriteArray(writer, "Properties", result.Properties, WriteKeyValue);
            WriteArray(writer, "Samples", result.Samples, WriteSample);
            WriteString(writer, "SupervisoryJobId", result.SupervisoryJobId);
            WriteString(writer, "SupervisoryTaskId", result.SupervisoryTaskId);
            WriteArray(writer, "Variables", result.Variables, WriteKeyValue);
            WriteArray(writer, "Files", result.Files, WriteFile);
            WriteDouble(writer, "TotalRuntimeMilliseconds", result.TotalRuntimeMilliseconds);
            WriteDouble(writer, "TotalPauseTimeMilliseconds", result.TotalPauseTimeMilliseconds);
        }

        /// <summary>
        /// Reads a complete result body.
        /// </summary>
        private static LadsStoredResultData ReadResult(XmlReader reader)
        {
            string runId = ReadText(reader, "RunId");
            ReadStart(reader, "Template");
            LadsStoredTemplateData template = ReadTemplate(reader);
            reader.ReadEndElement();
            return new LadsStoredResultData
            {
                RunId = runId,
                Template = template,
                Outcome = (LadsProgramOutcome)XmlConvert.ToInt32(ReadText(reader, "Outcome")),
                Started = ReadDate(reader, "Started"),
                Stopped = ReadDate(reader, "Stopped"),
                Properties = ReadArray(reader, "Properties", ReadKeyValue),
                Samples = ReadArray(reader, "Samples", ReadSample),
                SupervisoryJobId = ReadText(reader, "SupervisoryJobId"),
                SupervisoryTaskId = ReadText(reader, "SupervisoryTaskId"),
                Variables = ReadArray(reader, "Variables", ReadKeyValue),
                Files = ReadArray(reader, "Files", ReadFile),
                TotalRuntimeMilliseconds = ReadDouble(reader, "TotalRuntimeMilliseconds"),
                TotalPauseTimeMilliseconds = ReadDouble(reader, "TotalPauseTimeMilliseconds")
            };
        }

        /// <summary>
        /// Writes the LADS string-valued key-value structure without reflection.
        /// </summary>
        private static void WriteKeyValue(XmlWriter writer, KeyValueType value)
        {
            WriteString(writer, "Key", value.Key);
            WriteString(writer, "Value", value.Value);
        }

        /// <summary>
        /// Reads the LADS string-valued key-value structure.
        /// </summary>
        private static KeyValueType ReadKeyValue(XmlReader reader)
        {
            return new KeyValueType
            {
                Key = ReadString(reader, "Key"),
                Value = ReadString(reader, "Value")
            };
        }

        /// <summary>
        /// Writes all fields of the LADS sample information structure.
        /// </summary>
        private static void WriteSample(XmlWriter writer, SampleInfoType value)
        {
            WriteString(writer, "ContainerId", value.ContainerId);
            WriteString(writer, "SampleId", value.SampleId);
            WriteString(writer, "Position", value.Position);
            WriteString(writer, "CustomData", value.CustomData);
        }

        /// <summary>
        /// Reads all fields of the LADS sample information structure.
        /// </summary>
        private static SampleInfoType ReadSample(XmlReader reader)
        {
            return new SampleInfoType
            {
                ContainerId = ReadString(reader, "ContainerId"),
                SampleId = ReadString(reader, "SampleId"),
                Position = ReadString(reader, "Position"),
                CustomData = ReadString(reader, "CustomData")
            };
        }

        /// <summary>
        /// Writes file metadata and its bytes in the same snapshot as the result.
        /// </summary>
        private static void WriteFile(XmlWriter writer, LadsResultFile file)
        {
            WriteString(writer, "Name", file.Name);
            WriteString(writer, "MimeType", file.MimeType);
            WriteBytes(writer, "Content", file.Content);
        }

        /// <summary>
        /// Reads a result file without consulting any file paths from the document.
        /// </summary>
        private static LadsResultFile ReadFile(XmlReader reader)
        {
            return new LadsResultFile(
                ReadText(reader, "Name"),
                ReadText(reader, "MimeType"),
                ReadBytes(reader, "Content"));
        }

        /// <summary>
        /// Writes durable maintenance state without storing an execution continuation.
        /// </summary>
        private static void WriteMaintenance(XmlWriter writer, LadsStoredMaintenanceData task)
        {
            WriteString(writer, "Name", task.Name);
            WriteString(writer, "State", XmlConvert.ToString((int)task.State));
            WriteDate(writer, "LastExecutionDate", task.LastExecutionDate);
            WriteString(writer, "LastResult", XmlConvert.ToString((int)task.LastResult));
            WriteDate(writer, "PlannedDate", task.PlannedDate);
            WriteLocalizedText(writer, "Comment", task.Comment);
        }

        /// <summary>
        /// Reads durable maintenance state; callers decide how to report interrupted execution.
        /// </summary>
        private static LadsStoredMaintenanceData ReadMaintenance(XmlReader reader)
        {
            return new LadsStoredMaintenanceData
            {
                Name = ReadText(reader, "Name"),
                State = (LadsMaintenanceStateValue)XmlConvert.ToInt32(ReadText(reader, "State")),
                LastExecutionDate = ReadDate(reader, "LastExecutionDate"),
                LastResult = (MaintenanceTaskResultEnum)XmlConvert.ToInt32(ReadText(reader, "LastResult")),
                PlannedDate = ReadDate(reader, "PlannedDate"),
                Comment = ReadLocalizedText(reader, "Comment")
            };
        }

        /// <summary>
        /// Writes an array while preserving the distinction between null and empty.
        /// </summary>
        /// <typeparam name="T">The type of array element.</typeparam>
        private static void WriteArray<T>(
            XmlWriter writer,
            string name,
            ArrayOf<T> values,
            Action<XmlWriter, T> write)
        {
            writer.WriteStartElement(name);
            if (values.IsNull)
            {
                writer.WriteAttributeString("Null", "true");
            }
            foreach (T value in values)
            {
                if (value is null)
                {
                    throw new InvalidDataException("A snapshot collection contains a null element.");
                }
                writer.WriteStartElement("Item");
                write(writer, value);
                writer.WriteEndElement();
            }
            writer.WriteEndElement();
        }

        /// <summary>
        /// Reads a strictly typed array without reflection or untrusted preallocated element counts.
        /// </summary>
        /// <typeparam name="T">The type of array element.</typeparam>
        private static ArrayOf<T> ReadArray<T>(XmlReader reader, string name, Func<XmlReader, T> read)
        {
            RequireElement(reader, name);
            bool isNull = ReadNullFlag(reader);
            bool empty = reader.IsEmptyElement;
            reader.ReadStartElement(name);
            if (empty)
            {
                return isNull ? default : ArrayOf<T>.Empty;
            }
            var values = new List<T>();
            while (reader.MoveToContent() == XmlNodeType.Element)
            {
                if (isNull)
                {
                    throw new InvalidDataException("A null collection contains elements.");
                }
                ReadStart(reader, "Item");
                values.Add(read(reader));
                reader.ReadEndElement();
            }
            reader.ReadEndElement();
            return isNull ? ArrayOf<T>.Null : new ArrayOf<T>(values.ToArray());
        }

        /// <summary>
        /// Writes a nullable string, escaping text and preserving carriage returns.
        /// </summary>
        private static void WriteString(XmlWriter writer, string name, string? value)
        {
            writer.WriteStartElement(name);
            if (value == null)
            {
                writer.WriteAttributeString("Null", "true");
            }
            else
            {
                writer.WriteString(value);
            }
            writer.WriteEndElement();
        }

        /// <summary>
        /// Reads a nullable string and rejects content attached to a null marker.
        /// </summary>
        private static string? ReadString(XmlReader reader, string name)
        {
            RequireElement(reader, name);
            bool isNull = ReadNullFlag(reader);
            string text = reader.ReadElementContentAsString();
            if (isNull && text.Length != 0)
            {
                throw new InvalidDataException("A null string contains text.");
            }
            return isNull ? null : text;
        }

        /// <summary>
        /// Reads a required, possibly empty string.
        /// </summary>
        private static string ReadText(XmlReader reader, string name)
        {
            return ReadString(reader, name) ??
                throw new InvalidDataException("A required snapshot field is null: " + name);
        }

        /// <summary>
        /// Writes a finite, nonnegative duration.
        /// </summary>
        private static void WriteDouble(XmlWriter writer, string name, double value)
        {
            ValidateDuration(value);
            WriteString(writer, name, XmlConvert.ToString(value));
        }

        /// <summary>
        /// Reads and validates a finite, nonnegative duration.
        /// </summary>
        private static double ReadDouble(XmlReader reader, string name)
        {
            double value = XmlConvert.ToDouble(ReadText(reader, name));
            ValidateDuration(value);
            return value;
        }

        /// <summary>
        /// Writes the native UTC file-time value, including its null value, without precision loss.
        /// </summary>
        private static void WriteDate(XmlWriter writer, string name, DateTimeUtc value)
        {
            WriteString(writer, name, XmlConvert.ToString(value.Value));
        }

        /// <summary>
        /// Reads a UTC file-time value and rejects values that would be silently clamped.
        /// </summary>
        private static DateTimeUtc ReadDate(XmlReader reader, string name)
        {
            long value = XmlConvert.ToInt64(ReadText(reader, name));
            var date = new DateTimeUtc(value);
            if (date.Value != value)
            {
                throw new InvalidDataException("A stored UTC timestamp is outside its supported range.");
            }
            return date;
        }

        /// <summary>
        /// Writes localized text in its portable locale-and-text representation.
        /// </summary>
        private static void WriteLocalizedText(XmlWriter writer, string name, LocalizedText value)
        {
            writer.WriteStartElement(name);
            WriteString(writer, "Locale", value.Locale);
            WriteString(writer, "Text", value.Text);
            writer.WriteEndElement();
        }

        /// <summary>
        /// Reads localized text without reflection.
        /// </summary>
        private static LocalizedText ReadLocalizedText(XmlReader reader, string name)
        {
            ReadStart(reader, name);
            var value = new LocalizedText(ReadString(reader, "Locale"), ReadString(reader, "Text"));
            reader.ReadEndElement();
            return value;
        }

        /// <summary>
        /// Writes base64 in fixed-size chunks rather than allocating another complete file buffer.
        /// </summary>
        private static void WriteBytes(XmlWriter writer, string name, ByteString value)
        {
            writer.WriteStartElement(name);
            if (value.IsNull)
            {
                writer.WriteAttributeString("Null", "true");
            }
            else
            {
                byte[] chunk = new byte[Math.Min(value.Length, 8192)];
                int offset = 0;
                while (offset < value.Length)
                {
                    int count = Math.Min(chunk.Length, value.Length - offset);
                    value.Span.Slice(offset, count).CopyTo(chunk);
                    writer.WriteBase64(chunk, 0, count);
                    offset += count;
                }
            }
            writer.WriteEndElement();
        }

        /// <summary>
        /// Reads embedded bytes from the already bounded XML document.
        /// </summary>
        private static ByteString ReadBytes(XmlReader reader, string name)
        {
            RequireElement(reader, name);
            if (ReadNullFlag(reader))
            {
                if (reader.ReadElementContentAsString().Length != 0)
                {
                    throw new InvalidDataException("A null byte string contains text.");
                }
                return default;
            }
            using var buffer = new MemoryStream();
            byte[] chunk = new byte[8192];
            int count;
            while ((count = reader.ReadElementContentAsBase64(chunk, 0, chunk.Length)) != 0)
            {
                buffer.Write(chunk, 0, count);
            }
            return ByteString.From(buffer.ToArray());
        }

        /// <summary>
        /// Checks the only optional scalar or array attribute.
        /// </summary>
        private static bool ReadNullFlag(XmlReader reader)
        {
            string? flag = reader.GetAttribute("Null");
            if (reader.AttributeCount != (flag == null ? 0 : 1) || (flag != null && flag != "true"))
            {
                throw new InvalidDataException("Unexpected LADS snapshot field attributes.");
            }
            return flag != null;
        }

        /// <summary>
        /// Opens a required complex element and rejects unexpected attributes.
        /// </summary>
        private static void ReadStart(XmlReader reader, string name)
        {
            RequireElement(reader, name);
            if (reader.HasAttributes || reader.IsEmptyElement)
            {
                throw new InvalidDataException("A required snapshot element is empty or has unexpected attributes.");
            }
            reader.ReadStartElement(name);
        }

        /// <summary>
        /// Enforces the exact element name and empty XML namespace for schema one.
        /// </summary>
        private static void RequireElement(XmlReader reader, string name)
        {
            if (reader.MoveToContent() != XmlNodeType.Element ||
                reader.LocalName != name || reader.NamespaceURI.Length != 0)
            {
                throw new InvalidDataException("Missing or unexpected LADS snapshot element: " + name);
            }
        }
    }

    /// <summary>
    /// A write-only memory stream that fails before growing past the configured snapshot byte limit.
    /// </summary>
    internal sealed class LadsSnapshotBuffer : Stream
    {
        /// <summary>
        /// Creates an empty bounded snapshot buffer.
        /// </summary>
        internal LadsSnapshotBuffer(int maxBytes)
        {
            m_maxBytes = maxBytes;
        }

        /// <inheritdoc/>
        public override bool CanRead => false;

        /// <inheritdoc/>
        public override bool CanSeek => false;

        /// <inheritdoc/>
        public override bool CanWrite => true;

        /// <inheritdoc/>
        public override long Length => m_buffer.Length;

        /// <inheritdoc/>
        public override long Position
        {
            get => m_buffer.Position;
            set => throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override void Flush()
        {
            m_buffer.Flush();
        }

        /// <inheritdoc/>
        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count)
        {
            if (count < 0 || m_buffer.Length > m_maxBytes - (long)count)
            {
                throw new InvalidDataException("The LADS snapshot exceeds the configured byte limit.");
            }
            m_buffer.Write(buffer, offset, count);
        }

        /// <inheritdoc/>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                m_buffer.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>
        /// Copies the complete encoded snapshot.
        /// </summary>
        internal byte[] ToArray()
        {
            return m_buffer.ToArray();
        }

        /// <summary>The complete snapshot byte limit.</summary>
        private readonly int m_maxBytes;

        /// <summary>The bounded output's backing memory.</summary>
        private readonly MemoryStream m_buffer = new();
    }
}
