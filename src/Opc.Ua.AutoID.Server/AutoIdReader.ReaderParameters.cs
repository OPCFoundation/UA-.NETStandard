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

namespace Opc.Ua.AutoID.Server
{
    public sealed partial class AutoIdReader
    {
        /// <summary>
        /// Copies the complete snapshot without sharing provider-owned array storage.
        /// </summary>
        internal static AutoIdScanParameters SnapshotParameters(AutoIdScanParameters parameters)
        {
            return parameters with
            {
                CodeTypes = CopyParameterArray(parameters.CodeTypes),
                TagTypes = CopyParameterArray(parameters.TagTypes),
                CodeTypesRWData = CopyParameterArray(parameters.CodeTypesRWData)
            };
        }

        /// <summary>
        /// Freezes the provider's declared capabilities for the reader lifetime.
        /// </summary>
        internal static AutoIdReaderParameterCapabilities SnapshotCapabilities(
            AutoIdReaderParameterCapabilities capabilities)
        {
            if (capabilities == null)
            {
                throw new ArgumentNullException(nameof(capabilities));
            }
            return capabilities with
            {
                TagTypes = CopyParameterArray(capabilities.TagTypes),
                CodeTypesRWData = CopyParameterArray(capabilities.CodeTypesRWData),
                TemplateNames = CopyParameterArray(capabilities.TemplateNames)
            };
        }

        /// <summary>
        /// Checks optional support, reader kind, enum selections and the general antenna configuration.
        /// </summary>
        internal static void ValidateReaderParameters(
            AutoIdScanParameters parameters,
            AutoIdReaderKind kind,
            AutoIdReaderParameterCapabilities? capabilities)
        {
            AutoIdReaderParameterCapabilities supported = capabilities ?? new AutoIdReaderParameterCapabilities();
            bool rfid = kind == AutoIdReaderKind.Rfid;
            bool optical = kind is AutoIdReaderKind.Ocr or
                AutoIdReaderKind.Optical or AutoIdReaderKind.OpticalVerifier;
            if ((!rfid && (!supported.TagTypes.IsNull || !supported.CodeTypesRWData.IsNull ||
                supported.SupportsRfPower || supported.SupportsMinRssi || supported.ConfiguredAntennas.HasValue)) ||
                (!optical && (!supported.TemplateNames.IsNull || supported.SupportsMatchCode)) ||
                parameters.RfPower.HasValue != supported.SupportsRfPower ||
                parameters.MinRssi.HasValue != supported.SupportsMinRssi ||
                parameters.EnableAntennas.HasValue != supported.ConfiguredAntennas.HasValue ||
                (parameters.TemplateName != null) != !supported.TemplateNames.IsNull ||
                (parameters.MatchCode != null) != supported.SupportsMatchCode)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "Reader parameters must match the declared capabilities and kind.");
            }
            ValidateParameterSelection(parameters.TagTypes, supported.TagTypes);
            ValidateParameterSelection(parameters.CodeTypesRWData, supported.CodeTypesRWData);
            foreach (string codeType in supported.CodeTypesRWData)
            {
                if (codeType is not ("RAW:BYTES" or "RAW:STRING" or "EPC" or "UID" or "GS1" or "ASC" or "URI") &&
                    !codeType.StartsWith("CUSTOM:", StringComparison.Ordinal))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError, "Unknown diagnostic format name.");
                }
            }
            ValidateParameterNames(supported.TemplateNames);
            if (parameters.TemplateName != null)
            {
                bool found = false;
                foreach (string template in supported.TemplateNames)
                {
                    found |= string.Equals(template, parameters.TemplateName, StringComparison.Ordinal);
                }
                if (!found)
                {
                    throw ServiceResultException.Create(StatusCodes.BadOutOfRange, "Unknown configured template.");
                }
            }
            if (parameters.EnableAntennas is uint selected &&
                supported.ConfiguredAntennas is uint configured && (selected & ~configured) != 0)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadOutOfRange, "The selection cannot enable unconfigured antennas.");
            }
        }

        /// <summary>
        /// Adds only the provider-supported standard reader parameters before registration.
        /// </summary>
        private static void ConfigureReaderParameters(
            ISystemContext context,
            BaseObjectState runtime,
            AutoIdScanParameters parameters,
            AutoIdReaderParameterCapabilities? capabilities)
        {
            if (capabilities == null)
            {
                return;
            }
            if (!capabilities.TagTypes.IsNull)
            {
                AddParameterSelection(context, runtime, "TagTypes", capabilities.TagTypes, parameters.TagTypes);
            }
            if (!capabilities.CodeTypesRWData.IsNull)
            {
                AddParameterSelection(
                    context, runtime, "CodeTypesRWData", capabilities.CodeTypesRWData, parameters.CodeTypesRWData);
            }
            if (parameters.RfPower is sbyte power)
            {
                AddParameter(context, runtime, "RfPower", Opc.Ua.DataTypeIds.SByte, Variant.From(power));
            }
            if (parameters.MinRssi is int rssi)
            {
                AddParameter(context, runtime, "MinRssi", Opc.Ua.DataTypeIds.Int32, Variant.From(rssi));
            }
            if (parameters.EnableAntennas is uint antennas)
            {
                AddParameter(context, runtime, "EnableAntennas", Opc.Ua.DataTypeIds.UInt32, Variant.From(antennas));
            }
            if (parameters.TemplateName is string template)
            {
                AddParameter(context, runtime, "TemplateName", Opc.Ua.DataTypeIds.String, Variant.From(template));
            }
            if (parameters.MatchCode is string match)
            {
                AddParameter(context, runtime, "MatchCode", Opc.Ua.DataTypeIds.String, Variant.From(match));
            }
        }

        /// <summary>
        /// Instantiates the published UInt32 array override and its mandatory EnumStrings property.
        /// </summary>
        private static void AddParameterSelection(
            ISystemContext context, BaseObjectState runtime, string name, ArrayOf<string> names, ArrayOf<uint> values)
        {
            var variable = new MultiStateDiscreteState(runtime);
            variable.Create(context, NodeId.Null,
                new QualifiedName(name, context.NamespaceUris.GetIndexOrAppend(AutoIdModel.NamespaceUri)),
                new LocalizedText(name), true);
            variable.ReferenceTypeId = Opc.Ua.Types.ReferenceTypeIds.HasComponent;
            variable.DataType = Opc.Ua.DataTypeIds.UInt32;
            variable.ValueRank = ValueRanks.OneDimension;
            variable.ArrayDimensions = [0];
            variable.AccessLevel = variable.UserAccessLevel = AccessLevels.CurrentReadOrWrite;
            variable.Value = Variant.From(CopyParameterArray(values));
            variable.StatusCode = StatusCodes.Good;
            variable.Timestamp = DateTimeUtc.Now;
            var labels = new LocalizedText[names.Count];
            for (int index = 0; index < labels.Length; index++)
            {
                labels[index] = new LocalizedText(names[index]);
            }
            variable.EnumStrings!.Value = labels.ToArrayOf();
            variable.EnumStrings.AccessLevel = variable.EnumStrings.UserAccessLevel = AccessLevels.CurrentRead;
            variable.EnumStrings.ArrayDimensions = [0];
            variable.EnumStrings.StatusCode = StatusCodes.Good;
            variable.EnumStrings.Timestamp = variable.Timestamp;
            runtime.AddChild(variable);
        }

        /// <summary>
        /// Validates zero or more unique supported indexes without imposing an unspecified minimum selection.
        /// </summary>
        private static void ValidateParameterSelection(ArrayOf<uint> selected, ArrayOf<string> names)
        {
            if (selected.IsNull != names.IsNull)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError, "Parameter values and capability declarations must agree.");
            }
            ValidateParameterNames(names);
            var indexes = new HashSet<uint>();
            foreach (uint index in selected)
            {
                if (index >= names.Count || !indexes.Add(index))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadOutOfRange, "Selected indexes must be supported and unique.");
                }
            }
        }

        /// <summary>
        /// Rejects empty and duplicate names in a provider's stable option catalogue.
        /// </summary>
        private static void ValidateParameterNames(ArrayOf<string> names)
        {
            var unique = new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in names)
            {
                if (string.IsNullOrEmpty(name) || !unique.Add(name))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError, "Parameter option names must be nonempty and unique.");
                }
            }
        }

        /// <summary>
        /// Copies array storage while preserving the distinction between absent and empty parameters.
        /// </summary>
        /// <typeparam name="T">The parameter array element type.</typeparam>
        private static ArrayOf<T> CopyParameterArray<T>(ArrayOf<T> values)
        {
            return values.IsNull ? default : values.Span.ToArray().ToArrayOf();
        }

        /// <summary>
        /// Checks declared support before binding a standard optional child.
        /// </summary>
        private bool SupportsReaderParameter(string? name)
        {
            return m_parameterCapabilities != null && (name switch
            {
                "TagTypes" => !m_parameterCapabilities.TagTypes.IsNull,
                "CodeTypesRWData" => !m_parameterCapabilities.CodeTypesRWData.IsNull,
                "RfPower" => m_parameterCapabilities.SupportsRfPower,
                "MinRssi" => m_parameterCapabilities.SupportsMinRssi,
                "EnableAntennas" => m_parameterCapabilities.ConfiguredAntennas.HasValue,
                "TemplateName" => !m_parameterCapabilities.TemplateNames.IsNull,
                "MatchCode" => m_parameterCapabilities.SupportsMatchCode,
                _ => false
            });
        }

        /// <summary>The frozen reader-specific capability declaration.</summary>
        private readonly AutoIdReaderParameterCapabilities? m_parameterCapabilities;
    }
}
