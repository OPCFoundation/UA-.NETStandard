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
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using Opc.Ua.Mcp.Serialization;

namespace Opc.Ua.Mcp
{
    /// <summary>
    /// Versions to include in endpoint discovery.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<Isa95JobVersion>))]
    public enum Isa95JobVersion
    {
        /// <summary>
        /// Both supported wire models.
        /// </summary>
        All,

        /// <summary>
        /// ISA-95 Job Control V1.
        /// </summary>
        V1,

        /// <summary>
        /// ISA-95 Job Control V2.
        /// </summary>
        V2
    }

    /// <summary>
    /// The finite commands of the V1 ReceiveJobOrder method.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<Isa95V1Command>))]
    public enum Isa95V1Command
    {
        /// <summary>
        /// Store a job without starting it.
        /// </summary>
        Store = 1,

        /// <summary>
        /// Store and start a job.
        /// </summary>
        StoreAndStart = 2,

        /// <summary>
        /// Start a stored job.
        /// </summary>
        Start = 3,

        /// <summary>
        /// Update a stored job.
        /// </summary>
        Update = 4,

        /// <summary>
        /// Stop a job.
        /// </summary>
        Stop = 5,

        /// <summary>
        /// Cancel a job.
        /// </summary>
        Cancel = 6,

        /// <summary>
        /// Clear a job.
        /// </summary>
        Clear = 7
    }

    /// <summary>
    /// The states defined by the V1 wire model.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter<Isa95V1State>))]
    public enum Isa95V1State
    {
        /// <summary>
        /// No specific state selector.
        /// </summary>
        Undefined,

        /// <summary>
        /// Waiting for execution.
        /// </summary>
        Waiting,

        /// <summary>
        /// Ready to execute.
        /// </summary>
        Ready,

        /// <summary>
        /// Loaded.
        /// </summary>
        Loaded,

        /// <summary>
        /// Currently executing.
        /// </summary>
        Running,

        /// <summary>
        /// Execution completed.
        /// </summary>
        Completed,

        /// <summary>
        /// Execution aborted.
        /// </summary>
        Aborted,

        /// <summary>
        /// Held.
        /// </summary>
        Held,

        /// <summary>
        /// Suspended.
        /// </summary>
        Suspended,

        /// <summary>
        /// Closed.
        /// </summary>
        Closed,

        /// <summary>
        /// In an error state.
        /// </summary>
        Error
    }

    /// <summary>
    /// Localized text with an explicit locale rather than a structure string.
    /// </summary>
    public sealed class Isa95TextInput
    {
        /// <summary>
        /// Gets or sets the text.
        /// </summary>
        public required string Text { get; set; }

        /// <summary>
        /// Gets or sets the optional locale.
        /// </summary>
        public string? Locale { get; set; }
    }

    /// <summary>
    /// A UA Variant input with a concrete type and a JSON value in the UA JSON representation.
    /// </summary>
    public sealed class Isa95TypedValueInput
    {
        /// <summary>
        /// Gets or sets the concrete built-in type, for example UInt32, Double or ExtensionObject.
        /// </summary>
        [JsonConverter(typeof(JsonStringEnumConverter<BuiltInType>))]
        public required BuiltInType DataType { get; set; }

        /// <summary>
        /// Gets or sets whether Value is a one-dimensional array of the indicated type.
        /// </summary>
        public bool IsArray { get; set; }

        /// <summary>
        /// Gets or sets the typed UA JSON body. UInt64/Int64 values may be decimal strings.
        /// </summary>
        [Description("A UA JSON value for dataType, not a structure.ToString() value. " +
            "For example dataType=UInt32,value=0 or dataType=Boolean,value=false.")]
        public required JsonElement Value { get; set; }
    }

    /// <summary>
    /// Engineering units for the V2 model.
    /// </summary>
    public sealed class Isa95UnitInput
    {
        /// <summary>
        /// Gets or sets the units namespace URI.
        /// </summary>
        public required string NamespaceUri { get; set; }

        /// <summary>
        /// Gets or sets the unit identifier.
        /// </summary>
        public int UnitId { get; set; }

        /// <summary>
        /// Gets or sets the localized display name.
        /// </summary>
        public Isa95TextInput? DisplayName { get; set; }

        /// <summary>
        /// Gets or sets the localized description.
        /// </summary>
        public Isa95TextInput? Description { get; set; }
    }

    /// <summary>
    /// A typed parameter or resource property, including its recursive children.
    /// </summary>
    public sealed class Isa95ParameterInput
    {
        /// <summary>
        /// Gets or sets the parameter or property identifier.
        /// </summary>
        public required string Id { get; set; }

        /// <summary>
        /// Gets or sets the typed value.
        /// </summary>
        public required Isa95TypedValueInput Value { get; set; }

        /// <summary>
        /// Gets or sets descriptions. V1 permits at most one text without a locale.
        /// </summary>
        [JsonConverter(typeof(Isa95InputArrayJsonConverter<Isa95TextInput>))]
        public ArrayOf<Isa95TextInput> Description { get; set; }

        /// <summary>
        /// Gets or sets V2 engineering units.
        /// </summary>
        public Isa95UnitInput? EngineeringUnits { get; set; }

        /// <summary>
        /// Gets or sets the V1 unit-of-measure string. Not accepted by V2.
        /// </summary>
        public string? UnitOfMeasure { get; set; }

        /// <summary>
        /// Gets or sets subparameters, or subproperties when this input is used as a resource property.
        /// </summary>
        [JsonConverter(typeof(Isa95InputArrayJsonConverter<Isa95ParameterInput>))]
        public ArrayOf<Isa95ParameterInput> Children { get; set; }
    }

    /// <summary>
    /// A personnel, equipment or physical-asset requirement or actual.
    /// </summary>
    public sealed class Isa95ResourceInput
    {
        /// <summary>
        /// Gets or sets the resource identifier.
        /// </summary>
        public required string Id { get; set; }

        /// <summary>
        /// Gets or sets descriptions.
        /// </summary>
        [JsonConverter(typeof(Isa95InputArrayJsonConverter<Isa95TextInput>))]
        public ArrayOf<Isa95TextInput> Description { get; set; }

        /// <summary>
        /// Gets or sets the corresponding PersonnelUse, EquipmentUse or PhysicalAssetUse value.
        /// </summary>
        public string? Use { get; set; }

        /// <summary>
        /// Gets or sets the exact decimal quantity string.
        /// </summary>
        public string? Quantity { get; set; }

        /// <summary>
        /// Gets or sets V2 engineering units.
        /// </summary>
        public Isa95UnitInput? EngineeringUnits { get; set; }

        /// <summary>
        /// Gets or sets the V1 unit-of-measure string.
        /// </summary>
        public string? UnitOfMeasure { get; set; }

        /// <summary>
        /// Gets or sets typed resource properties.
        /// </summary>
        [JsonConverter(typeof(Isa95InputArrayJsonConverter<Isa95ParameterInput>))]
        public ArrayOf<Isa95ParameterInput> Properties { get; set; }
    }

    /// <summary>
    /// A material requirement or actual with independent classification and lot identifiers.
    /// </summary>
    public sealed class Isa95MaterialInput
    {
        /// <summary>
        /// Gets or sets the material class ID.
        /// </summary>
        public string? MaterialClassId { get; set; }

        /// <summary>
        /// Gets or sets the material definition ID.
        /// </summary>
        public string? MaterialDefinitionId { get; set; }

        /// <summary>
        /// Gets or sets the material lot ID.
        /// </summary>
        public string? MaterialLotId { get; set; }

        /// <summary>
        /// Gets or sets the material sublot ID.
        /// </summary>
        public string? MaterialSublotId { get; set; }

        /// <summary>
        /// Gets or sets descriptions.
        /// </summary>
        [JsonConverter(typeof(Isa95InputArrayJsonConverter<Isa95TextInput>))]
        public ArrayOf<Isa95TextInput> Description { get; set; }

        /// <summary>
        /// Gets or sets the material use.
        /// </summary>
        public string? Use { get; set; }

        /// <summary>
        /// Gets or sets the exact decimal quantity string.
        /// </summary>
        public string? Quantity { get; set; }

        /// <summary>
        /// Gets or sets V2 engineering units.
        /// </summary>
        public Isa95UnitInput? EngineeringUnits { get; set; }

        /// <summary>
        /// Gets or sets the V1 unit-of-measure string.
        /// </summary>
        public string? UnitOfMeasure { get; set; }

        /// <summary>
        /// Gets or sets typed material properties.
        /// </summary>
        [JsonConverter(typeof(Isa95InputArrayJsonConverter<Isa95ParameterInput>))]
        public ArrayOf<Isa95ParameterInput> Properties { get; set; }
    }

    /// <summary>
    /// A work master and its typed parameter list.
    /// </summary>
    public sealed class Isa95WorkMasterInput
    {
        /// <summary>
        /// Gets or sets the work-master ID.
        /// </summary>
        public required string Id { get; set; }

        /// <summary>
        /// Gets or sets the single description.
        /// </summary>
        public Isa95TextInput? Description { get; set; }

        /// <summary>
        /// Gets or sets work-master parameters.
        /// </summary>
        [JsonConverter(typeof(Isa95InputArrayJsonConverter<Isa95ParameterInput>))]
        public ArrayOf<Isa95ParameterInput> Parameters { get; set; }
    }

    /// <summary>
    /// A V2 state number scoped by its relative state-machine browse path.
    /// </summary>
    public sealed class Isa95StateInput
    {
        /// <summary>
        /// Gets or sets qualified browse-name segments; an empty path selects the root state machine.
        /// </summary>
        [JsonConverter(typeof(McpStringArrayJsonConverter))]
        public ArrayOf<string> BrowsePath { get; set; }

        /// <summary>
        /// Gets or sets the state number within the selected state machine.
        /// </summary>
        public required uint StateNumber { get; set; }

        /// <summary>
        /// Gets or sets the state's optional localized label.
        /// </summary>
        public Isa95TextInput? StateText { get; set; }
    }

    /// <summary>
    /// An explicit comment list for a V2 command.
    /// </summary>
    public sealed class Isa95CommentInput
    {
        /// <summary>
        /// Gets or sets localized comment texts.
        /// </summary>
        [JsonConverter(typeof(Isa95InputArrayJsonConverter<Isa95TextInput>))]
        public ArrayOf<Isa95TextInput> Texts { get; set; }
    }

    /// <summary>
    /// A complete job-order input. Omitted V2 optionals remain absent on the wire.
    /// </summary>
    public sealed class Isa95JobOrderInput
    {
        /// <summary>
        /// Gets or sets the job-order identifier.
        /// </summary>
        public required string JobOrderId { get; set; }

        /// <summary>
        /// Gets or sets descriptions. V1 permits a single unlocalized text.
        /// </summary>
        [JsonConverter(typeof(Isa95InputArrayJsonConverter<Isa95TextInput>))]
        public ArrayOf<Isa95TextInput> Description { get; set; }

        /// <summary>
        /// Gets or sets work masters.
        /// </summary>
        [JsonConverter(typeof(Isa95InputArrayJsonConverter<Isa95WorkMasterInput>))]
        public ArrayOf<Isa95WorkMasterInput> WorkMasters { get; set; }

        /// <summary>
        /// Gets or sets the requested UTC start time.
        /// </summary>
        public DateTime? StartTime { get; set; }

        /// <summary>
        /// Gets or sets the requested UTC end time.
        /// </summary>
        public DateTime? EndTime { get; set; }

        /// <summary>
        /// Gets or sets priority. Explicit zero is distinct from an omitted V2 priority.
        /// </summary>
        public short? Priority { get; set; }

        /// <summary>
        /// Gets or sets typed job-order parameters.
        /// </summary>
        [JsonConverter(typeof(Isa95InputArrayJsonConverter<Isa95ParameterInput>))]
        public ArrayOf<Isa95ParameterInput> Parameters { get; set; }

        /// <summary>
        /// Gets or sets personnel requirements.
        /// </summary>
        [JsonConverter(typeof(Isa95InputArrayJsonConverter<Isa95ResourceInput>))]
        public ArrayOf<Isa95ResourceInput> Personnel { get; set; }

        /// <summary>
        /// Gets or sets equipment requirements.
        /// </summary>
        [JsonConverter(typeof(Isa95InputArrayJsonConverter<Isa95ResourceInput>))]
        public ArrayOf<Isa95ResourceInput> Equipment { get; set; }

        /// <summary>
        /// Gets or sets physical-asset requirements.
        /// </summary>
        [JsonConverter(typeof(Isa95InputArrayJsonConverter<Isa95ResourceInput>))]
        public ArrayOf<Isa95ResourceInput> PhysicalAssets { get; set; }

        /// <summary>
        /// Gets or sets material requirements.
        /// </summary>
        [JsonConverter(typeof(Isa95InputArrayJsonConverter<Isa95MaterialInput>))]
        public ArrayOf<Isa95MaterialInput> Materials { get; set; }
    }

    /// <summary>
    /// A complete job-response input for the version-specific receive method.
    /// </summary>
    public sealed class Isa95JobResponseInput
    {
        /// <summary>
        /// Gets or sets the job-response ID.
        /// </summary>
        public required string JobResponseId { get; set; }

        /// <summary>
        /// Gets or sets the related job-order ID.
        /// </summary>
        public required string JobOrderId { get; set; }

        /// <summary>
        /// Gets or sets the response description.
        /// </summary>
        public Isa95TextInput? Description { get; set; }

        /// <summary>
        /// Gets or sets the actual UTC start time.
        /// </summary>
        public DateTime? StartTime { get; set; }

        /// <summary>
        /// Gets or sets the actual UTC end time.
        /// </summary>
        public DateTime? EndTime { get; set; }

        /// <summary>
        /// Gets or sets the V1 response state. Leave Undefined when sending V2.
        /// </summary>
        public Isa95V1State V1State { get; set; }

        /// <summary>
        /// Gets or sets V2 states. Required for V2; leave absent when sending V1.
        /// </summary>
        [JsonConverter(typeof(Isa95InputArrayJsonConverter<Isa95StateInput>))]
        public ArrayOf<Isa95StateInput> States { get; set; }

        /// <summary>
        /// Gets or sets typed response data.
        /// </summary>
        [JsonConverter(typeof(Isa95InputArrayJsonConverter<Isa95ParameterInput>))]
        public ArrayOf<Isa95ParameterInput> Parameters { get; set; }

        /// <summary>
        /// Gets or sets personnel actuals.
        /// </summary>
        [JsonConverter(typeof(Isa95InputArrayJsonConverter<Isa95ResourceInput>))]
        public ArrayOf<Isa95ResourceInput> Personnel { get; set; }

        /// <summary>
        /// Gets or sets equipment actuals.
        /// </summary>
        [JsonConverter(typeof(Isa95InputArrayJsonConverter<Isa95ResourceInput>))]
        public ArrayOf<Isa95ResourceInput> Equipment { get; set; }

        /// <summary>
        /// Gets or sets physical-asset actuals.
        /// </summary>
        [JsonConverter(typeof(Isa95InputArrayJsonConverter<Isa95ResourceInput>))]
        public ArrayOf<Isa95ResourceInput> PhysicalAssets { get; set; }

        /// <summary>
        /// Gets or sets material actuals.
        /// </summary>
        [JsonConverter(typeof(Isa95InputArrayJsonConverter<Isa95MaterialInput>))]
        public ArrayOf<Isa95MaterialInput> Materials { get; set; }
    }

    /// <summary>
    /// Selects a V2 response query by exactly one identifier or state list.
    /// </summary>
    public sealed class Isa95ResponseQueryInput
    {
        /// <summary>
        /// Gets or sets the job-order identifier for an ID query.
        /// </summary>
        public string? JobOrderId { get; set; }

        /// <summary>
        /// Gets or sets state selectors for a state query, including their state-machine paths.
        /// </summary>
        [JsonConverter(typeof(Isa95InputArrayJsonConverter<Isa95StateInput>))]
        public ArrayOf<Isa95StateInput> States { get; set; }
    }
}
