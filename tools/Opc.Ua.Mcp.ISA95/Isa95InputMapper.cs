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
using System.Text.Json;
using V1 = Opc.Ua.ISA95.JobControl.V1;
using V2 = Opc.Ua.ISA95.JobControl.V2;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Converts explicit job inputs to generated wire types without reflection or fabricated optional values.
    /// </summary>
    internal static class Isa95InputMapper
    {
        /// <summary>
        /// Validates a required job or resource identifier.
        /// </summary>
        public static string Identifier(string value)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(value);
            return value;
        }

        /// <summary>
        /// Converts localized command comments.
        /// </summary>
        public static ArrayOf<LocalizedText> Comment(Isa95CommentInput? value)
        {
            return value is null ? default : Texts(value.Texts);
        }

        /// <summary>
        /// Converts an explicitly typed JSON body through the existing strict UA decoder.
        /// </summary>
        public static Variant Value(Isa95TypedValueInput input, IServiceMessageContext context)
        {
            ArgumentNullException.ThrowIfNull(input);
            ArgumentNullException.ThrowIfNull(context);
            if (!Enum.IsDefined(input.DataType) || (uint)input.DataType > (uint)BuiltInType.DiagnosticInfo)
            {
                throw new ArgumentException("A concrete UA built-in type is required.", nameof(input));
            }
            if (input.Value.ValueKind == JsonValueKind.Undefined)
            {
                throw new ArgumentException("A typed value must explicitly supply its JSON value.", nameof(input));
            }
            if (input.DataType == BuiltInType.Null)
            {
                if (input.IsArray || input.Value.ValueKind != JsonValueKind.Null)
                {
                    throw new ArgumentException("The Null type requires a scalar null value.", nameof(input));
                }
                return Variant.Null;
            }
            if (input.IsArray && input.Value.ValueKind != JsonValueKind.Array)
            {
                throw new ArgumentException("An array type requires a JSON array.", nameof(input));
            }
            if (input.IsArray && input.Value.GetArrayLength() > 500)
            {
                throw new ArgumentException("Typed arrays are limited to 500 elements.", nameof(input));
            }
            string json = input.Value.GetRawText();
            if (json.Length > 262_144)
            {
                throw new ArgumentException("A typed parameter exceeds the 262144-character limit.", nameof(input));
            }
            using var decoder = new JsonDecoder("{\"value\":" + json + "}", context);
            return decoder.ReadVariantValue(
                "value", new TypeInfo(input.DataType, input.IsArray ? ValueRanks.OneDimension : ValueRanks.Scalar));
        }

        /// <summary>
        /// Converts a complete V1 order, rejecting V2-only metadata rather than silently discarding it.
        /// </summary>
        public static V1.ISA95JobOrderDataType OrderV1(Isa95JobOrderInput input, IServiceMessageContext context)
        {
            ArgumentNullException.ThrowIfNull(input);
            return new V1.ISA95JobOrderDataType
            {
                ID = Identifier(input.JobOrderId),
                Description = V1Description(input.Description),
                WorkMasterID = Map(input.WorkMasters, value => WorkMasterV1(value, context)),
                StartTime = Timestamp(input.StartTime),
                EndTime = Timestamp(input.EndTime),
                Priority = input.Priority.GetValueOrDefault(),
                JobOrderParameters = Map(input.Parameters, value => ParameterV1(value, context, 0)),
                PersonnelRequirements = Map(input.Personnel, value => PersonnelV1(value, context)),
                EquipmentRequirements = Map(input.Equipment, value => EquipmentV1(value, context)),
                PhysicalAssetRequirements = Map(input.PhysicalAssets, value => PhysicalAssetV1(value, context)),
                MaterialRequirements = Map(input.Materials, value => MaterialV1(value, context))
            };
        }

        /// <summary>
        /// Converts a V2 order, including optional-field masks for explicit zero and empty values.
        /// </summary>
        public static V2.ISA95JobOrderDataType OrderV2(Isa95JobOrderInput input, IServiceMessageContext context)
        {
            ArgumentNullException.ThrowIfNull(input);
            return new V2.ISA95JobOrderDataType
            {
                JobOrderID = Identifier(input.JobOrderId),
                Description = Texts(input.Description),
                WorkMasterID = Map(input.WorkMasters, value => WorkMasterV2(value, context)),
                StartTime = Timestamp(input.StartTime),
                EndTime = Timestamp(input.EndTime),
                Priority = input.Priority.GetValueOrDefault(),
                JobOrderParameters = Map(input.Parameters, value => ParameterV2(value, context, 0)),
                PersonnelRequirements = Map(input.Personnel, value => PersonnelV2(value, context)),
                EquipmentRequirements = Map(input.Equipment, value => EquipmentV2(value, context)),
                PhysicalAssetRequirements = Map(input.PhysicalAssets, value => PhysicalAssetV2(value, context)),
                MaterialRequirements = Map(input.Materials, value => MaterialV2(value, context)),
                EncodingMask =
                    Flag(!input.Description.IsNull, (uint)V2.ISA95JobOrderDataTypeFields.Description) |
                    Flag(!input.WorkMasters.IsNull, (uint)V2.ISA95JobOrderDataTypeFields.WorkMasterID) |
                    Flag(input.StartTime.HasValue, (uint)V2.ISA95JobOrderDataTypeFields.StartTime) |
                    Flag(input.EndTime.HasValue, (uint)V2.ISA95JobOrderDataTypeFields.EndTime) |
                    Flag(input.Priority.HasValue, (uint)V2.ISA95JobOrderDataTypeFields.Priority) |
                    Flag(!input.Parameters.IsNull, (uint)V2.ISA95JobOrderDataTypeFields.JobOrderParameters) |
                    Flag(!input.Personnel.IsNull, (uint)V2.ISA95JobOrderDataTypeFields.PersonnelRequirements) |
                    Flag(!input.Equipment.IsNull, (uint)V2.ISA95JobOrderDataTypeFields.EquipmentRequirements) |
                    Flag(!input.PhysicalAssets.IsNull, (uint)V2.ISA95JobOrderDataTypeFields.PhysicalAssetRequirements) |
                    Flag(!input.Materials.IsNull, (uint)V2.ISA95JobOrderDataTypeFields.MaterialRequirements)
            };
        }

        /// <summary>
        /// Converts a complete V1 response and its mandatory enum state.
        /// </summary>
        public static V1.ISA95JobResponseDataType ResponseV1(
            Isa95JobResponseInput input,
            IServiceMessageContext context)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (!Enum.IsDefined(input.V1State) || !input.States.IsNull)
            {
                throw new ArgumentException("V1 responses require a V1 state, not V2 state paths.", nameof(input));
            }
            return new V1.ISA95JobResponseDataType
            {
                ID = Identifier(input.JobResponseId),
                JobOrderID = Identifier(input.JobOrderId),
                Description = V1Text(input.Description),
                StartTime = Timestamp(input.StartTime),
                EndTime = Timestamp(input.EndTime),
                JobState = (V1.ISA95JobOrderStateEnum)input.V1State,
                JobResponseData = Map(input.Parameters, value => ParameterV1(value, context, 0)),
                PersonnelActuals = Map(input.Personnel, value => PersonnelV1(value, context)),
                EquipmentActuals = Map(input.Equipment, value => EquipmentV1(value, context)),
                PhysicalAssetActuals = Map(input.PhysicalAssets, value => PhysicalAssetV1(value, context)),
                MaterialActuals = Map(input.Materials, value => MaterialV1(value, context))
            };
        }

        /// <summary>
        /// Converts a complete V2 response with mandatory state paths and exact optional presence.
        /// </summary>
        public static V2.ISA95JobResponseDataType ResponseV2(
            Isa95JobResponseInput input,
            IServiceMessageContext context)
        {
            ArgumentNullException.ThrowIfNull(input);
            if (input.States.IsNull || input.V1State != Isa95V1State.Undefined)
            {
                throw new ArgumentException(
                    "V2 responses require an explicit states array, not a V1 state.", nameof(input));
            }
            return new V2.ISA95JobResponseDataType
            {
                JobResponseID = Identifier(input.JobResponseId),
                JobOrderID = Identifier(input.JobOrderId),
                Description = Text(input.Description),
                StartTime = Timestamp(input.StartTime),
                EndTime = Timestamp(input.EndTime),
                JobState = States(input.States),
                JobResponseData = Map(input.Parameters, value => ParameterV2(value, context, 0)),
                PersonnelActuals = Map(input.Personnel, value => PersonnelV2(value, context)),
                EquipmentActuals = Map(input.Equipment, value => EquipmentV2(value, context)),
                PhysicalAssetActuals = Map(input.PhysicalAssets, value => PhysicalAssetV2(value, context)),
                MaterialActuals = Map(input.Materials, value => MaterialV2(value, context)),
                EncodingMask =
                    Flag(input.Description is not null, (uint)V2.ISA95JobResponseDataTypeFields.Description) |
                    Flag(input.StartTime.HasValue, (uint)V2.ISA95JobResponseDataTypeFields.StartTime) |
                    Flag(input.EndTime.HasValue, (uint)V2.ISA95JobResponseDataTypeFields.EndTime) |
                    Flag(!input.Parameters.IsNull, (uint)V2.ISA95JobResponseDataTypeFields.JobResponseData) |
                    Flag(!input.Personnel.IsNull, (uint)V2.ISA95JobResponseDataTypeFields.PersonnelActuals) |
                    Flag(!input.Equipment.IsNull, (uint)V2.ISA95JobResponseDataTypeFields.EquipmentActuals) |
                    Flag(!input.PhysicalAssets.IsNull, (uint)V2.ISA95JobResponseDataTypeFields.PhysicalAssetActuals) |
                    Flag(!input.Materials.IsNull, (uint)V2.ISA95JobResponseDataTypeFields.MaterialActuals)
            };
        }

        /// <summary>
        /// Converts V2 state selectors while preserving each state-machine scope.
        /// </summary>
        public static ArrayOf<V2.ISA95StateDataType> States(ArrayOf<Isa95StateInput> values)
        {
            return Map(values, value => new V2.ISA95StateDataType
            {
                StateNumber = value.StateNumber,
                StateText = Text(value.StateText),
                BrowsePath = new RelativePath
                {
                    Elements = Map(value.BrowsePath, segment => new RelativePathElement
                    {
                        ReferenceTypeId = ReferenceTypeIds.HasSubStateMachine,
                        IncludeSubtypes = true,
                        TargetName = QualifiedName.Parse(Identifier(segment))
                    })
                }
            });
        }

        /// <summary>
        /// Converts a recursive V1 parameter.
        /// </summary>
        private static V1.ISA95ParameterDataType ParameterV1(
            Isa95ParameterInput input,
            IServiceMessageContext context,
            int depth)
        {
            CheckDepth(depth);
            return new V1.ISA95ParameterDataType
            {
                ID = Identifier(input.Id),
                Value = Value(input.Value, context),
                Description = V1Description(input.Description),
                UoM = UnitV1(input.UnitOfMeasure, input.EngineeringUnits),
                Subparameters = Map(input.Children, value => ParameterV1(value, context, depth + 1))
            };
        }

        /// <summary>
        /// Converts a recursive V2 parameter with optional metadata.
        /// </summary>
        private static V2.ISA95ParameterDataType ParameterV2(
            Isa95ParameterInput input,
            IServiceMessageContext context,
            int depth)
        {
            CheckDepth(depth);
            return new V2.ISA95ParameterDataType
            {
                ID = Identifier(input.Id),
                Value = Value(input.Value, context),
                Description = Texts(input.Description),
                EngineeringUnits = UnitV2(input.EngineeringUnits, input.UnitOfMeasure),
                Subparameters = Map(input.Children, value => ParameterV2(value, context, depth + 1)),
                EncodingMask =
                    Flag(!input.Description.IsNull, (uint)V2.ISA95ParameterDataTypeFields.Description) |
                    Flag(input.EngineeringUnits is not null, (uint)V2.ISA95ParameterDataTypeFields.EngineeringUnits) |
                    Flag(!input.Children.IsNull, (uint)V2.ISA95ParameterDataTypeFields.Subparameters)
            };
        }

        /// <summary>
        /// Converts a recursive V1 resource property.
        /// </summary>
        private static V1.ISA95PropertyDataType PropertyV1(
            Isa95ParameterInput input,
            IServiceMessageContext context,
            int depth)
        {
            CheckDepth(depth);
            return new V1.ISA95PropertyDataType
            {
                ID = Identifier(input.Id),
                Value = Value(input.Value, context),
                Description = V1Description(input.Description),
                UoM = UnitV1(input.UnitOfMeasure, input.EngineeringUnits),
                Subproperties = Map(input.Children, value => PropertyV1(value, context, depth + 1))
            };
        }

        /// <summary>
        /// Converts a recursive V2 resource property.
        /// </summary>
        private static V2.ISA95PropertyDataType PropertyV2(
            Isa95ParameterInput input,
            IServiceMessageContext context,
            int depth)
        {
            CheckDepth(depth);
            return new V2.ISA95PropertyDataType
            {
                ID = Identifier(input.Id),
                Value = Value(input.Value, context),
                Description = Texts(input.Description),
                EngineeringUnits = UnitV2(input.EngineeringUnits, input.UnitOfMeasure),
                Subproperties = Map(input.Children, value => PropertyV2(value, context, depth + 1)),
                EncodingMask =
                    Flag(!input.Description.IsNull, (uint)V2.ISA95PropertyDataTypeFields.Description) |
                    Flag(input.EngineeringUnits is not null, (uint)V2.ISA95PropertyDataTypeFields.EngineeringUnits) |
                    Flag(!input.Children.IsNull, (uint)V2.ISA95PropertyDataTypeFields.Subproperties)
            };
        }

        /// <summary>
        /// Converts a V1 work master.
        /// </summary>
        private static V1.ISA95WorkMasterDataType WorkMasterV1(
            Isa95WorkMasterInput input,
            IServiceMessageContext context)
        {
            return new V1.ISA95WorkMasterDataType
            {
                ID = Identifier(input.Id),
                Description = V1Text(input.Description),
                Parameters = Map(input.Parameters, value => ParameterV1(value, context, 0))
            };
        }

        /// <summary>
        /// Converts a V2 work master and optional parameter-list presence.
        /// </summary>
        private static V2.ISA95WorkMasterDataType WorkMasterV2(
            Isa95WorkMasterInput input,
            IServiceMessageContext context)
        {
            return new V2.ISA95WorkMasterDataType
            {
                ID = Identifier(input.Id),
                Description = Text(input.Description),
                Parameters = Map(input.Parameters, value => ParameterV2(value, context, 0)),
                EncodingMask =
                    Flag(input.Description is not null, (uint)V2.ISA95WorkMasterDataTypeFields.Description) |
                    Flag(!input.Parameters.IsNull, (uint)V2.ISA95WorkMasterDataTypeFields.Parameters)
            };
        }

        /// <summary>
        /// Converts V1 personnel requirements or actuals.
        /// </summary>
        private static V1.ISA95PersonnelDataType PersonnelV1(
            Isa95ResourceInput input,
            IServiceMessageContext context)
        {
            return new V1.ISA95PersonnelDataType
            {
                ID = Identifier(input.Id),
                Description = V1Description(input.Description),
                PersonnelUse = input.Use ?? string.Empty,
                Quantity = input.Quantity ?? string.Empty,
                UoM = UnitV1(input.UnitOfMeasure, input.EngineeringUnits),
                Properties = Map(input.Properties, value => PropertyV1(value, context, 0))
            };
        }

        /// <summary>
        /// Converts V1 equipment requirements or actuals.
        /// </summary>
        private static V1.ISA95EquipmentDataType EquipmentV1(
            Isa95ResourceInput input,
            IServiceMessageContext context)
        {
            return new V1.ISA95EquipmentDataType
            {
                ID = Identifier(input.Id),
                Description = V1Description(input.Description),
                EquipmentUse = input.Use ?? string.Empty,
                Quantity = input.Quantity ?? string.Empty,
                UoM = UnitV1(input.UnitOfMeasure, input.EngineeringUnits),
                Properties = Map(input.Properties, value => PropertyV1(value, context, 0))
            };
        }

        /// <summary>
        /// Converts V1 physical-asset requirements or actuals.
        /// </summary>
        private static V1.ISA95PhysicalAssetDataType PhysicalAssetV1(
            Isa95ResourceInput input,
            IServiceMessageContext context)
        {
            return new V1.ISA95PhysicalAssetDataType
            {
                ID = Identifier(input.Id),
                Description = V1Description(input.Description),
                PhysicalAssetUse = input.Use ?? string.Empty,
                Quantity = input.Quantity ?? string.Empty,
                UoM = UnitV1(input.UnitOfMeasure, input.EngineeringUnits),
                Properties = Map(input.Properties, value => PropertyV1(value, context, 0))
            };
        }

        /// <summary>
        /// Converts V2 personnel requirements or actuals.
        /// </summary>
        private static V2.ISA95PersonnelDataType PersonnelV2(
            Isa95ResourceInput input,
            IServiceMessageContext context)
        {
            return new V2.ISA95PersonnelDataType
            {
                ID = Identifier(input.Id),
                Description = Texts(input.Description),
                PersonnelUse = input.Use ?? string.Empty,
                Quantity = input.Quantity ?? string.Empty,
                EngineeringUnits = UnitV2(input.EngineeringUnits, input.UnitOfMeasure),
                Properties = Map(input.Properties, value => PropertyV2(value, context, 0)),
                EncodingMask =
                    Flag(!input.Description.IsNull, (uint)V2.ISA95PersonnelDataTypeFields.Description) |
                    Flag(input.Use is not null, (uint)V2.ISA95PersonnelDataTypeFields.PersonnelUse) |
                    Flag(input.Quantity is not null, (uint)V2.ISA95PersonnelDataTypeFields.Quantity) |
                    Flag(input.EngineeringUnits is not null, (uint)V2.ISA95PersonnelDataTypeFields.EngineeringUnits) |
                    Flag(!input.Properties.IsNull, (uint)V2.ISA95PersonnelDataTypeFields.Properties)
            };
        }

        /// <summary>
        /// Converts V2 equipment requirements or actuals.
        /// </summary>
        private static V2.ISA95EquipmentDataType EquipmentV2(
            Isa95ResourceInput input,
            IServiceMessageContext context)
        {
            return new V2.ISA95EquipmentDataType
            {
                ID = Identifier(input.Id),
                Description = Texts(input.Description),
                EquipmentUse = input.Use ?? string.Empty,
                Quantity = input.Quantity ?? string.Empty,
                EngineeringUnits = UnitV2(input.EngineeringUnits, input.UnitOfMeasure),
                Properties = Map(input.Properties, value => PropertyV2(value, context, 0)),
                EncodingMask =
                    Flag(!input.Description.IsNull, (uint)V2.ISA95EquipmentDataTypeFields.Description) |
                    Flag(input.Use is not null, (uint)V2.ISA95EquipmentDataTypeFields.EquipmentUse) |
                    Flag(input.Quantity is not null, (uint)V2.ISA95EquipmentDataTypeFields.Quantity) |
                    Flag(input.EngineeringUnits is not null, (uint)V2.ISA95EquipmentDataTypeFields.EngineeringUnits) |
                    Flag(!input.Properties.IsNull, (uint)V2.ISA95EquipmentDataTypeFields.Properties)
            };
        }

        /// <summary>
        /// Converts V2 physical-asset requirements or actuals.
        /// </summary>
        private static V2.ISA95PhysicalAssetDataType PhysicalAssetV2(
            Isa95ResourceInput input,
            IServiceMessageContext context)
        {
            return new V2.ISA95PhysicalAssetDataType
            {
                ID = Identifier(input.Id),
                Description = Texts(input.Description),
                PhysicalAssetUse = input.Use ?? string.Empty,
                Quantity = input.Quantity ?? string.Empty,
                EngineeringUnits = UnitV2(input.EngineeringUnits, input.UnitOfMeasure),
                Properties = Map(input.Properties, value => PropertyV2(value, context, 0)),
                EncodingMask =
                    Flag(!input.Description.IsNull, (uint)V2.ISA95PhysicalAssetDataTypeFields.Description) |
                    Flag(input.Use is not null, (uint)V2.ISA95PhysicalAssetDataTypeFields.PhysicalAssetUse) |
                    Flag(input.Quantity is not null, (uint)V2.ISA95PhysicalAssetDataTypeFields.Quantity) |
                    Flag(input.EngineeringUnits is not null,
                        (uint)V2.ISA95PhysicalAssetDataTypeFields.EngineeringUnits) |
                    Flag(!input.Properties.IsNull, (uint)V2.ISA95PhysicalAssetDataTypeFields.Properties)
            };
        }

        /// <summary>
        /// Converts V1 material requirements or actuals without numeric quantity coercion.
        /// </summary>
        private static V1.ISA95MaterialDataType MaterialV1(
            Isa95MaterialInput input,
            IServiceMessageContext context)
        {
            return new V1.ISA95MaterialDataType
            {
                MaterialClassID = input.MaterialClassId ?? string.Empty,
                MaterialDefinitionID = input.MaterialDefinitionId ?? string.Empty,
                MaterialLotID = input.MaterialLotId ?? string.Empty,
                MaterialSublotID = input.MaterialSublotId ?? string.Empty,
                Description = V1Description(input.Description),
                MaterialUse = input.Use ?? string.Empty,
                Quantity = input.Quantity ?? string.Empty,
                UoM = UnitV1(input.UnitOfMeasure, input.EngineeringUnits),
                Properties = Map(input.Properties, value => PropertyV1(value, context, 0))
            };
        }

        /// <summary>
        /// Converts V2 materials with independent optional ID and metadata masks.
        /// </summary>
        private static V2.ISA95MaterialDataType MaterialV2(
            Isa95MaterialInput input,
            IServiceMessageContext context)
        {
            return new V2.ISA95MaterialDataType
            {
                MaterialClassID = input.MaterialClassId ?? string.Empty,
                MaterialDefinitionID = input.MaterialDefinitionId ?? string.Empty,
                MaterialLotID = input.MaterialLotId ?? string.Empty,
                MaterialSublotID = input.MaterialSublotId ?? string.Empty,
                Description = Texts(input.Description),
                MaterialUse = input.Use ?? string.Empty,
                Quantity = input.Quantity ?? string.Empty,
                EngineeringUnits = UnitV2(input.EngineeringUnits, input.UnitOfMeasure),
                Properties = Map(input.Properties, value => PropertyV2(value, context, 0)),
                EncodingMask =
                    Flag(input.MaterialClassId is not null, (uint)V2.ISA95MaterialDataTypeFields.MaterialClassID) |
                    Flag(input.MaterialDefinitionId is not null,
                        (uint)V2.ISA95MaterialDataTypeFields.MaterialDefinitionID) |
                    Flag(input.MaterialLotId is not null, (uint)V2.ISA95MaterialDataTypeFields.MaterialLotID) |
                    Flag(input.MaterialSublotId is not null, (uint)V2.ISA95MaterialDataTypeFields.MaterialSublotID) |
                    Flag(!input.Description.IsNull, (uint)V2.ISA95MaterialDataTypeFields.Description) |
                    Flag(input.Use is not null, (uint)V2.ISA95MaterialDataTypeFields.MaterialUse) |
                    Flag(input.Quantity is not null, (uint)V2.ISA95MaterialDataTypeFields.Quantity) |
                    Flag(input.EngineeringUnits is not null, (uint)V2.ISA95MaterialDataTypeFields.EngineeringUnits) |
                    Flag(!input.Properties.IsNull, (uint)V2.ISA95MaterialDataTypeFields.Properties)
            };
        }

        /// <summary>
        /// Converts a V1 unit only when no incompatible V2 unit metadata was supplied.
        /// </summary>
        private static string UnitV1(string? value, Isa95UnitInput? units)
        {
            if (units is not null)
            {
                throw new ArgumentException("V1 requires unitOfMeasure rather than engineeringUnits.", nameof(units));
            }
            return value ?? string.Empty;
        }

        /// <summary>
        /// Converts V2 engineering units, leaving optional presence to the caller's encoding mask.
        /// </summary>
        private static EUInformation UnitV2(Isa95UnitInput? value, string? unitOfMeasure)
        {
            if (unitOfMeasure is not null)
            {
                throw new ArgumentException(
                    "V2 requires engineeringUnits rather than unitOfMeasure.", nameof(unitOfMeasure));
            }
            return value is null ? new EUInformation() : new EUInformation
            {
                NamespaceUri = Identifier(value.NamespaceUri),
                UnitId = value.UnitId,
                DisplayName = Text(value.DisplayName),
                Description = Text(value.Description)
            };
        }

        /// <summary>
        /// Converts a localized text without using Nullable on UA null-sentinel structs.
        /// </summary>
        private static LocalizedText Text(Isa95TextInput? value)
        {
            if (value is null)
            {
                return LocalizedText.Null;
            }
            ArgumentNullException.ThrowIfNull(value.Text);
            return new LocalizedText(value.Locale, value.Text);
        }

        /// <summary>
        /// Converts a list of localized descriptions.
        /// </summary>
        private static ArrayOf<LocalizedText> Texts(ArrayOf<Isa95TextInput> values)
        {
            return Map(values, Text);
        }

        /// <summary>
        /// Rejects unsupported V1 description multiplicity instead of selecting an arbitrary first entry.
        /// </summary>
        private static string V1Description(ArrayOf<Isa95TextInput> values)
        {
            if (values.Count > 1)
            {
                throw new ArgumentException("V1 supports only one unlocalized description.", nameof(values));
            }
            return values.Count == 0 ? string.Empty : V1Text(values[0]);
        }

        /// <summary>
        /// Rejects localization that cannot be represented by a V1 String field.
        /// </summary>
        private static string V1Text(Isa95TextInput? value)
        {
            if (value is not null && !string.IsNullOrEmpty(value.Locale))
            {
                throw new ArgumentException("V1 descriptions cannot carry a locale.", nameof(value));
            }
            return value is null ? string.Empty : value.Text
                ?? throw new ArgumentException("A description text cannot be null.", nameof(value));
        }

        /// <summary>
        /// Converts timestamps without interpreting unspecified local wall-clock times.
        /// </summary>
        private static DateTimeUtc Timestamp(DateTime? value)
        {
            if (value.HasValue && value.Value.Kind == DateTimeKind.Unspecified)
            {
                throw new ArgumentException("A timestamp must include a UTC or offset designation.", nameof(value));
            }
            return value.HasValue ? new DateTimeUtc(value.Value) : DateTimeUtc.MinValue;
        }

        /// <summary>
        /// Includes a flag only when the input field is present, independent of its value.
        /// </summary>
        private static uint Flag(bool present, uint flag)
        {
            return present ? flag : 0;
        }

        /// <summary>
        /// Bounds recursive parameters and properties.
        /// </summary>
        private static void CheckDepth(int depth)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(depth, 8);
        }

        /// <summary>
        /// Converts a bounded immutable array while preserving omitted versus explicit empty arrays.
        /// </summary>
        /// <typeparam name="T">The input type.</typeparam>
        /// <typeparam name="TResult">The generated wire type.</typeparam>
        private static ArrayOf<TResult> Map<T, TResult>(ArrayOf<T> values, Func<T, TResult> convert)
            where T : class
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(values.Count, 500);
            if (values.IsNull)
            {
                return ArrayOf<TResult>.Null;
            }
            var result = new TResult[values.Count];
            for (int index = 0; index < values.Count; index++)
            {
                T value = values[index]
                    ?? throw new ArgumentException("Null array entries are not allowed.", nameof(values));
                result[index] = convert(value);
            }
            return new ArrayOf<TResult>(result);
        }
    }
}
