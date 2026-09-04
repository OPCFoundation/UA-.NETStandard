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
using System.Globalization;
using System.Text.Json;
using Opc.Ua.Export;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// The Method argument mapping of WoT Binding Sections 9.1 and 6.11.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A UA Method becomes a WoT action, and its input and output arguments
    /// become that action's <c>input</c> and <c>output</c> DataSchemas. In the
    /// address space those arguments are not attributes of the Method: they are
    /// the <c>Argument</c> structures held by its <c>InputArguments</c> and
    /// <c>OutputArguments</c> Properties. Mapping the affordance therefore
    /// means decoding and rebuilding those values, which is what this file
    /// does in both directions.
    /// </para>
    /// <para>
    /// OPC UA Method arguments are positional, and RFC 8259 gives JSON object
    /// members no order, so the order is carried explicitly in
    /// <c>uav:fieldOrder</c> (Section 6.11.4) rather than taken from JSON
    /// member order. Where a document states no order and none follows from the
    /// Condition Method the action invokes, the arguments are reported and left
    /// to preservation instead of being invented.
    /// </para>
    /// </remarks>
    public static partial class WotNodeSetConverter
    {
        /// <summary>
        /// The BrowseName of the Property holding a Method's input arguments,
        /// in the base OPC UA namespace (OPC 10000-5).
        /// </summary>
        internal const string InputArgumentsBrowseName = "InputArguments";

        /// <inheritdoc cref="InputArgumentsBrowseName"/>
        internal const string OutputArgumentsBrowseName = "OutputArguments";

        /// <summary>
        /// The WoT member carrying an action's input DataSchema.
        /// </summary>
        internal const string InputMember = "input";

        /// <inheritdoc cref="InputMember"/>
        internal const string OutputMember = "output";

        /// <summary>
        /// The <c>Argument</c> DataType and its default XML encoding
        /// (OPC 10000-5).
        /// </summary>
        private const string ArgumentDataType = "i=296";

        /// <inheritdoc cref="ArgumentDataType"/>
        private const string ArgumentXmlEncoding = "i=297";

        /// <summary>
        /// The default name given to the one argument a bare DataSchema
        /// denotes, where the document supplies no name for it.
        /// </summary>
        /// <remarks>
        /// An <c>Argument</c> is identified by position in an OPC 10000-4
        /// <c>Call</c>; its <c>Name</c> is descriptive. A schema that is one
        /// value rather than a member map - the shape a Union-typed input takes
        /// - therefore still yields a complete Argument, and only its
        /// descriptive name has to be supplied.
        /// </remarks>
        private const string DefaultInputArgumentName = "Input";

        /// <inheritdoc cref="DefaultInputArgumentName"/>
        private const string DefaultOutputArgumentName = "Output";

        /// <summary>
        /// One decoded or authored <c>Argument</c>.
        /// </summary>
        private readonly record struct WotMethodArgument(
            string Name,
            string? DataType,
            int ValueRank,
            string? ArrayDimensions,
            Opc.Ua.Export.LocalizedText[]? Description);

        /// <summary>
        /// The arguments a Method holds, in declaration order.
        /// </summary>
        private readonly record struct WotMethodArguments(
            List<WotMethodArgument>? Input,
            List<WotMethodArgument>? Output);

        /// <summary>
        /// How an authored <c>input</c> or <c>output</c> DataSchema maps onto an
        /// <c>Argument</c> list.
        /// </summary>
        private enum WotArgumentShapeKind
        {
            /// <summary>The member is absent or declares no argument.</summary>
            None,

            /// <summary>The schema is one value, so it is one argument.</summary>
            Single,

            /// <summary>The schema's members are the arguments, in order.</summary>
            Members,

            /// <summary>The schema is not one this Binding can map.</summary>
            Invalid,

            /// <summary>The member order is neither stated nor derivable.</summary>
            AmbiguousOrder
        }

        /// <summary>
        /// The result of reading an <c>input</c> or <c>output</c> DataSchema.
        /// </summary>
        private readonly record struct WotArgumentShape(
            WotArgumentShapeKind Kind,
            IReadOnlyList<string> Members);

        /// <summary>
        /// Gets whether the converter maps an action member onto Argument
        /// values, which is what decides whether preservation must also carry
        /// it.
        /// </summary>
        /// <remarks>
        /// A member the converter materializes must not also be captured as
        /// residue, or the same fact would be stated twice - once as the
        /// argument Variable the NodeSet gained and once as an Extension
        /// re-applied over the document generated from it. A member the
        /// converter cannot map is the opposite case: it is reported and kept
        /// verbatim, so nothing is silently dropped.
        /// </remarks>
        internal static bool MapsArgumentSchema(JsonElement action, string member)
        {
            WotArgumentShapeKind kind = AnalyzeArgumentSchema(action, member).Kind;
            return kind is WotArgumentShapeKind.Single or WotArgumentShapeKind.Members;
        }

        /// <summary>
        /// Collects the arguments of every Method the Thing holds and records
        /// the argument Variables that the action schemas fully represent.
        /// </summary>
        /// <remarks>
        /// An argument Variable whose value decodes is represented by the
        /// action's own <c>input</c> or <c>output</c> schema, so emitting it a
        /// second time as an unrelated sibling property would state the same
        /// Node twice. One whose value does not decode is not represented, and
        /// stays a property naming the Method it belongs to, exactly as before.
        /// </remarks>
        private static Dictionary<string, WotMethodArguments> CollectMethodArguments(
            List<UAMethod> actions,
            Dictionary<string, UANode> index,
            HashSet<string> represented)
        {
            var collected = new Dictionary<string, WotMethodArguments>(StringComparer.Ordinal);
            foreach (UAMethod method in actions)
            {
                if (method.NodeId is null)
                {
                    continue;
                }
                List<WotMethodArgument>? input = ReadArgumentVariable(
                    method, index, InputArgumentsBrowseName, represented);
                List<WotMethodArgument>? output = ReadArgumentVariable(
                    method, index, OutputArgumentsBrowseName, represented);
                if (input is null && output is null)
                {
                    continue;
                }
                collected[method.NodeId] = new WotMethodArguments(input, output);
            }
            return collected;
        }

        /// <summary>
        /// Reads one argument Property of a Method, when it holds a decodable
        /// <c>Argument</c> list.
        /// </summary>
        private static List<WotMethodArgument>? ReadArgumentVariable(
            UAMethod method,
            Dictionary<string, UANode> index,
            string browseName,
            HashSet<string> represented)
        {
            foreach (Reference reference in method.References ?? [])
            {
                if (!reference.IsForward ||
                    reference.Value is null ||
                    !IsComponentReference(reference.ReferenceType) ||
                    !index.TryGetValue(reference.Value, out UANode? target) ||
                    target is not UAVariable variable ||
                    variable.NodeId is null ||
                    !IsBaseNamespaceBrowseName(variable.BrowseName, browseName))
                {
                    continue;
                }
                if (!TryDecodeArguments(variable.Value, out List<WotMethodArgument> arguments))
                {
                    return null;
                }
                represented.Add(variable.NodeId);
                return arguments;
            }
            return null;
        }

        /// <summary>
        /// Decodes the <c>ListOfExtensionObject</c> value an argument Property
        /// holds.
        /// </summary>
        /// <remarks>
        /// Only the standard shape is accepted: a list of ExtensionObjects each
        /// carrying the <c>Argument</c> default XML encoding. Anything else -
        /// another encoding, a foreign body, a missing name - decodes to
        /// nothing, and the Variable then stays a property in its own right so
        /// that a value this direction could not read is never re-stated as an
        /// argument list it is not.
        /// </remarks>
        private static bool TryDecodeArguments(
            System.Xml.XmlElement? value,
            out List<WotMethodArgument> arguments)
        {
            arguments = [];
            if (value is null ||
                !string.Equals(
                    value.LocalName, "ListOfExtensionObject", StringComparison.Ordinal) ||
                !string.Equals(value.NamespaceURI, UaXmlNamespace, StringComparison.Ordinal))
            {
                return false;
            }
            foreach (System.Xml.XmlNode node in value.ChildNodes)
            {
                if (node is not System.Xml.XmlElement extension ||
                    !string.Equals(
                        extension.LocalName, "ExtensionObject", StringComparison.Ordinal))
                {
                    return false;
                }
                System.Xml.XmlElement? typeId = FindChild(extension, "TypeId");
                System.Xml.XmlElement? identifier = typeId is null ? null : FindChild(typeId, "Identifier");
                if (identifier is null ||
                    !string.Equals(
                        identifier.InnerText.Trim(),
                        ArgumentXmlEncoding,
                        StringComparison.Ordinal))
                {
                    return false;
                }
                System.Xml.XmlElement? body = FindChild(extension, "Body");
                System.Xml.XmlElement? argument = body is null ? null : FindChild(body, "Argument");
                if (argument is null ||
                    !TryDecodeArgument(argument, out WotMethodArgument decoded))
                {
                    return false;
                }
                arguments.Add(decoded);
            }
            return arguments.Count > 0;
        }

        /// <summary>
        /// Decodes one <c>Argument</c> body element.
        /// </summary>
        private static bool TryDecodeArgument(
            System.Xml.XmlElement argument,
            out WotMethodArgument decoded)
        {
            decoded = default;
            System.Xml.XmlElement? name = FindChild(argument, "Name");
            if (name is null || name.InnerText.Length == 0)
            {
                return false;
            }
            System.Xml.XmlElement? dataType = FindChild(argument, "DataType");
            System.Xml.XmlElement? dataTypeId = dataType is null ? null : FindChild(dataType, "Identifier");
            System.Xml.XmlElement? valueRank = FindChild(argument, "ValueRank");
            int rank = -1;
            if (valueRank is not null &&
                valueRank.InnerText.Length > 0 &&
                !int.TryParse(
                    valueRank.InnerText.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out rank))
            {
                return false;
            }
            decoded = new WotMethodArgument(
                name.InnerText,
                dataTypeId?.InnerText.Trim(),
                rank,
                ReadArgumentDimensions(FindChild(argument, "ArrayDimensions")),
                ReadArgumentDescription(FindChild(argument, "Description")));
            return true;
        }

        /// <summary>
        /// Reads an <c>Argument</c>'s ArrayDimensions as the comma-separated
        /// NodeSet attribute form.
        /// </summary>
        private static string? ReadArgumentDimensions(System.Xml.XmlElement? dimensions)
        {
            if (dimensions is null)
            {
                return null;
            }
            var parts = new List<string>();
            foreach (System.Xml.XmlNode node in dimensions.ChildNodes)
            {
                if (node is System.Xml.XmlElement dimension &&
                    uint.TryParse(
                        dimension.InnerText.Trim(),
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out uint parsed))
                {
                    parts.Add(parsed.ToString(CultureInfo.InvariantCulture));
                }
            }
            return parts.Count == 0 ? null : string.Join(",", parts);
        }

        /// <summary>
        /// Reads an <c>Argument</c>'s Description text, keeping the locale it
        /// states (WoT Binding Section 9.1.1).
        /// </summary>
        private static Opc.Ua.Export.LocalizedText[]? ReadArgumentDescription(
            System.Xml.XmlElement? description)
        {
            if (description is null)
            {
                return null;
            }
            System.Xml.XmlElement? text = FindChild(description, "Text");
            if (text is null || text.InnerText.Length == 0)
            {
                return null;
            }
            System.Xml.XmlElement? locale = FindChild(description, "Locale");
            return
            [
                new Opc.Ua.Export.LocalizedText
                {
                    Locale = locale?.InnerText ?? string.Empty,
                    Value = text.InnerText
                }
            ];
        }

        private static System.Xml.XmlElement? FindChild(System.Xml.XmlElement parent, string localName)
        {
            foreach (System.Xml.XmlNode node in parent.ChildNodes)
            {
                if (node is System.Xml.XmlElement element &&
                    string.Equals(element.LocalName, localName, StringComparison.Ordinal) &&
                    string.Equals(element.NamespaceURI, UaXmlNamespace, StringComparison.Ordinal))
                {
                    return element;
                }
            }
            return null;
        }

        /// <summary>
        /// Writes an action's <c>input</c> or <c>output</c> DataSchema from the
        /// arguments the Method declares.
        /// </summary>
        /// <remarks>
        /// The schema is an object whose members are the arguments and whose
        /// <c>uav:fieldOrder</c> states their declaration order, because that
        /// order is what an OPC 10000-4 <c>Call</c> is positional over. Every
        /// argument is required: a Call supplies all of them.
        /// </remarks>
        private static void WriteArgumentSchema(
            Utf8JsonWriter writer,
            string member,
            List<WotMethodArgument>? arguments,
            UANodeSet nodeSet,
            string defaultLocale)
        {
            if (arguments is not { Count: > 0 })
            {
                return;
            }
            writer.WritePropertyName(member);
            writer.WriteStartObject();
            writer.WriteString("type", "object");

            writer.WritePropertyName("uav:fieldOrder");
            writer.WriteStartArray();
            foreach (WotMethodArgument argument in arguments)
            {
                writer.WriteStringValue(argument.Name);
            }
            writer.WriteEndArray();

            writer.WritePropertyName("properties");
            writer.WriteStartObject();
            foreach (WotMethodArgument argument in arguments)
            {
                writer.WritePropertyName(argument.Name);
                WriteArgument(writer, argument, nodeSet, defaultLocale);
            }
            writer.WriteEndObject();

            writer.WritePropertyName("required");
            writer.WriteStartArray();
            foreach (WotMethodArgument argument in arguments)
            {
                writer.WriteStringValue(argument.Name);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        /// <summary>
        /// Writes one argument as a DataSchema.
        /// </summary>
        /// <remarks>
        /// The json type is the readable claim and <c>uav:mapToType</c> is the
        /// definitive one, exactly as for a property affordance: the json type
        /// carries six OPC UA types, so a LocalizedText and a String read back
        /// the same without it. <c>uav:valueRank</c> is always written, because
        /// a scalar and a one-dimensional array of the same DataType differ
        /// only in it.
        /// </remarks>
        private static void WriteArgument(
            Utf8JsonWriter writer,
            WotMethodArgument argument,
            UANodeSet nodeSet,
            string defaultLocale)
        {
            writer.WriteStartObject();
            WriteArgumentJsonType(writer, argument.DataType);
            WriteLocalizedDescription(writer, argument.Description, defaultLocale);
            WriteOptional(
                writer,
                "uav:mapToType",
                ToPortableDataTypeId(argument.DataType, nodeSet));
            writer.WriteNumber("uav:valueRank", argument.ValueRank);
            WriteFieldArrayDimensions(writer, argument.ArrayDimensions);
            writer.WriteEndObject();
        }

        /// <summary>
        /// Writes the WoT type members that stand for a built-in DataType.
        /// </summary>
        /// <remarks>
        /// The refinements <c>contentEncoding</c> and <c>format</c> are what
        /// separate a ByteString, DateTime, Guid or UriString from a plain
        /// string in Section 6.11.4's table, so writing them keeps the readable
        /// schema idiomatic WoT rather than leaving every one of them a bare
        /// string that only <c>uav:mapToType</c> distinguishes.
        /// </remarks>
        private static void WriteArgumentJsonType(Utf8JsonWriter writer, string? dataType)
        {
            switch (dataType)
            {
                case WotVocabulary.ByteString:
                    writer.WriteString("type", "string");
                    writer.WriteString("contentEncoding", WotVocabulary.Base64Encoding);
                    return;
                case "i=13":
                    writer.WriteString("type", "string");
                    writer.WriteString("format", "date-time");
                    return;
                case "i=14":
                    writer.WriteString("type", "string");
                    writer.WriteString("format", "uuid");
                    return;
                case WotVocabulary.UriString:
                    writer.WriteString("type", "string");
                    writer.WriteString("format", "uri");
                    return;
                default:
                    WriteOptional(writer, "type", MapDataTypeToJson(dataType));
                    return;
            }
        }

        /// <summary>
        /// Materializes an action's authored <c>input</c> and <c>output</c>
        /// schemas as the Method's argument Properties.
        /// </summary>
        private static void SynthesizeMethodArguments(
            WotDocument document,
            UANodeSet nodeSet,
            JsonElement action,
            string affordanceKey,
            string methodNodeId,
            string methodLocal,
            string rootLocal,
            List<UANode> items,
            List<Reference> methodReferences,
            List<WotDiagnostic> diagnostics)
        {
            SynthesizeArgumentVariable(
                document, nodeSet, action, InputMember, InputArgumentsBrowseName,
                DefaultInputArgumentName, affordanceKey, methodNodeId, methodLocal,
                rootLocal, items, methodReferences, diagnostics);
            SynthesizeArgumentVariable(
                document, nodeSet, action, OutputMember, OutputArgumentsBrowseName,
                DefaultOutputArgumentName, affordanceKey, methodNodeId, methodLocal,
                rootLocal, items, methodReferences, diagnostics);
        }

        /// <summary>
        /// Materializes one argument Property from the DataSchema an action
        /// authors for it.
        /// </summary>
        private static void SynthesizeArgumentVariable(
            WotDocument document,
            UANodeSet nodeSet,
            JsonElement action,
            string member,
            string browseName,
            string defaultName,
            string affordanceKey,
            string methodNodeId,
            string methodLocal,
            string rootLocal,
            List<UANode> items,
            List<Reference> methodReferences,
            List<WotDiagnostic> diagnostics)
        {
            WotArgumentShape shape = AnalyzeArgumentSchema(action, member);
            string pointer = "/actions/" + EscapeJsonPointerToken(affordanceKey) + "/" + member;
            switch (shape.Kind)
            {
                case WotArgumentShapeKind.None:
                    return;
                case WotArgumentShapeKind.Invalid:
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.MethodArgumentSchemaInvalid,
                        $"The '{member}' DataSchema of an action does not map onto an " +
                        "OPC UA Argument list. It is carried unchanged by preservation " +
                        "rather than dropped.",
                        WotLocation.FromPointer(pointer)));
                    return;
                case WotArgumentShapeKind.AmbiguousOrder:
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.MethodArgumentOrderAmbiguous,
                        $"The '{member}' DataSchema of an action declares " +
                        $"{shape.Members.Count} members but states no uav:fieldOrder. " +
                        "OPC UA Method arguments are positional and JSON member order " +
                        "carries no meaning, so the order shall be stated " +
                        "(WoT Binding Section 6.11.4).",
                        WotLocation.FromPointer(pointer)));
                    return;
            }

            JsonElement schema = action.GetProperty(member);
            var arguments = new List<WotMethodArgument>();
            if (shape.Kind == WotArgumentShapeKind.Single)
            {
                arguments.Add(ReadArgument(
                    document, schema, ReadArgumentName(schema, defaultName),
                    nodeSet, diagnostics));
            }
            else
            {
                JsonElement properties = schema.GetProperty("properties");
                foreach (string name in shape.Members)
                {
                    arguments.Add(ReadArgument(
                        document, properties.GetProperty(name), name, nodeSet, diagnostics));
                }
            }

            string nodeId = GenerateBaseChildNodeId(
                nodeSet, rootLocal, methodLocal, browseName);
            items.Add(new UAVariable
            {
                NodeId = nodeId,
                BrowseName = browseName,
                DisplayName = MakeText(browseName),
                ParentNodeId = methodNodeId,
                DataType = ArgumentDataType,
                ValueRank = 1,
                ArrayDimensions = arguments.Count.ToString(CultureInfo.InvariantCulture),
                AccessLevel = AccessLevelCurrentRead,
                Value = BuildArgumentValue(arguments),
                References =
                [
                    new Reference
                    {
                        ReferenceType = "HasTypeDefinition",
                        IsForward = true,
                        Value = WotVocabulary.PropertyType
                    },
                    new Reference
                    {
                        ReferenceType = "HasModellingRule",
                        IsForward = true,
                        Value = WotVocabulary.ModellingRuleMandatory
                    },
                    new Reference
                    {
                        ReferenceType = "HasProperty",
                        IsForward = false,
                        Value = methodNodeId
                    }
                ]
            });

            methodReferences.Add(new Reference
            {
                ReferenceType = "HasProperty",
                IsForward = true,
                Value = nodeId
            });
        }

        /// <summary>
        /// Determines how an authored <c>input</c> or <c>output</c> member maps
        /// onto an Argument list, and in which order.
        /// </summary>
        /// <remarks>
        /// A schema that names a DataType - through <c>uav:mapToType</c>, a
        /// DataType identity or an inline definition - is one value and
        /// therefore one argument, whatever members it also states: those
        /// members are the fields of that DataType, which is exactly the shape
        /// a Union-typed input takes in Section 6.11.4.
        /// </remarks>
        private static WotArgumentShape AnalyzeArgumentSchema(
            JsonElement action,
            string member)
        {
            if (action.ValueKind != JsonValueKind.Object ||
                !action.TryGetProperty(member, out JsonElement schema))
            {
                return new WotArgumentShape(WotArgumentShapeKind.None, []);
            }
            if (schema.ValueKind != JsonValueKind.Object)
            {
                return new WotArgumentShape(WotArgumentShapeKind.Invalid, []);
            }
            if (NamesDataType(schema))
            {
                return new WotArgumentShape(WotArgumentShapeKind.Single, []);
            }
            if (!schema.TryGetProperty("properties", out JsonElement properties) ||
                properties.ValueKind != JsonValueKind.Object)
            {
                // A bare DataSchema is one argument; an object that states
                // neither members nor a type declares no argument at all and is
                // left to preservation rather than turned into one.
                string? type = GetElementString(schema, "type");
                return new WotArgumentShape(
                    type is null ||
                    string.Equals(type, "object", StringComparison.Ordinal)
                        ? WotArgumentShapeKind.None
                        : WotArgumentShapeKind.Single,
                    []);
            }

            var declared = new List<string>();
            foreach (JsonProperty property in properties.EnumerateObject())
            {
                declared.Add(property.Name);
            }
            if (declared.Count == 0)
            {
                return new WotArgumentShape(WotArgumentShapeKind.None, []);
            }
            if (schema.TryGetProperty("uav:fieldOrder", out JsonElement order))
            {
                return AnalyzeFieldOrder(order, properties, declared);
            }
            if (declared.Count == 1)
            {
                return new WotArgumentShape(WotArgumentShapeKind.Members, declared);
            }
            if (TryGetConditionArgumentOrder(action, declared, out List<string> conditionOrder))
            {
                return new WotArgumentShape(WotArgumentShapeKind.Members, conditionOrder);
            }
            return new WotArgumentShape(WotArgumentShapeKind.AmbiguousOrder, declared);
        }

        /// <summary>
        /// Reads an authored <c>uav:fieldOrder</c> and checks it against the
        /// members it orders.
        /// </summary>
        /// <remarks>
        /// Section 6.11.4 requires the order to list every member exactly once.
        /// An order that names something the schema does not define, or that
        /// leaves a member out, states an argument list that disagrees with the
        /// schema it orders, so it is rejected rather than partially applied.
        /// </remarks>
        private static WotArgumentShape AnalyzeFieldOrder(
            JsonElement order,
            JsonElement properties,
            List<string> declared)
        {
            if (order.ValueKind != JsonValueKind.Array)
            {
                return new WotArgumentShape(WotArgumentShapeKind.Invalid, declared);
            }
            var ordered = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (JsonElement entry in order.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.String ||
                    entry.GetString() is not { Length: > 0 } name ||
                    !properties.TryGetProperty(name, out _) ||
                    !seen.Add(name))
                {
                    return new WotArgumentShape(WotArgumentShapeKind.Invalid, declared);
                }
                ordered.Add(name);
            }
            return ordered.Count == declared.Count
                ? new WotArgumentShape(WotArgumentShapeKind.Members, ordered)
                : new WotArgumentShape(WotArgumentShapeKind.Invalid, declared);
        }

        /// <summary>
        /// Gets whether a DataSchema names the DataType of one value.
        /// </summary>
        private static bool NamesDataType(JsonElement schema)
        {
            return schema.TryGetProperty("uav:mapToType", out _) ||
                schema.TryGetProperty("uav:dataTypeId", out _) ||
                schema.TryGetProperty("uav:dataTypeName", out _) ||
                schema.TryGetProperty("uav:dataTypeDefinition", out _);
        }

        /// <summary>
        /// Names the one argument a bare DataSchema denotes.
        /// </summary>
        private static string ReadArgumentName(JsonElement schema, string defaultName)
        {
            return LocalName(GetElementString(schema, "uav:browseName")) ??
                SanitizeName(GetElementString(schema, "title")) ??
                defaultName;
        }

        /// <summary>
        /// Reads one argument from the DataSchema that declares it.
        /// </summary>
        /// <remarks>
        /// The DataType is resolved by the same rules a property affordance
        /// uses, so a definitive <c>uav:mapToType</c>, an inline DataType
        /// definition and a plain json type all resolve exactly as they do
        /// elsewhere rather than through a second, parallel reading.
        /// </remarks>
        private static WotMethodArgument ReadArgument(
            WotDocument document,
            JsonElement schema,
            string name,
            UANodeSet nodeSet,
            List<WotDiagnostic> diagnostics)
        {
            return new WotMethodArgument(
                name,
                MapJsonSchemaToDataType(document, schema, nodeSet, diagnostics),
                GetElementInt32(schema, "uav:valueRank") ?? -1,
                ReadArrayDimensions(schema, name, diagnostics),
                ReadDescription(schema, GetDeclaredLocale(document)));
        }

        /// <summary>
        /// Builds the <c>ListOfExtensionObject</c> value an argument Property
        /// holds.
        /// </summary>
        private static System.Xml.XmlElement BuildArgumentValue(List<WotMethodArgument> arguments)
        {
            var document = new System.Xml.XmlDocument { XmlResolver = null };
            System.Xml.XmlElement list = document.CreateElement(
                "uax", "ListOfExtensionObject", UaXmlNamespace);
            foreach (WotMethodArgument argument in arguments)
            {
                System.Xml.XmlElement extension = document.CreateElement(
                    "uax", "ExtensionObject", UaXmlNamespace);
                System.Xml.XmlElement typeId = document.CreateElement("uax", "TypeId", UaXmlNamespace);
                System.Xml.XmlElement identifier = document.CreateElement(
                    "uax", "Identifier", UaXmlNamespace);
                identifier.InnerText = ArgumentXmlEncoding;
                typeId.AppendChild(identifier);
                extension.AppendChild(typeId);

                System.Xml.XmlElement body = document.CreateElement("uax", "Body", UaXmlNamespace);
                body.AppendChild(BuildArgument(document, argument));
                extension.AppendChild(body);
                list.AppendChild(extension);
            }
            return list;
        }

        /// <summary>
        /// Builds one <c>Argument</c> body element.
        /// </summary>
        private static System.Xml.XmlElement BuildArgument(
            System.Xml.XmlDocument document,
            WotMethodArgument argument)
        {
            System.Xml.XmlElement element = document.CreateElement("uax", "Argument", UaXmlNamespace);
            System.Xml.XmlElement name = document.CreateElement("uax", "Name", UaXmlNamespace);
            name.InnerText = argument.Name;
            element.AppendChild(name);

            System.Xml.XmlElement dataType = document.CreateElement("uax", "DataType", UaXmlNamespace);
            System.Xml.XmlElement identifier = document.CreateElement("uax", "Identifier", UaXmlNamespace);
            identifier.InnerText = argument.DataType ?? WotVocabulary.BaseDataType;
            dataType.AppendChild(identifier);
            element.AppendChild(dataType);

            System.Xml.XmlElement valueRank = document.CreateElement("uax", "ValueRank", UaXmlNamespace);
            valueRank.InnerText = argument.ValueRank.ToString(CultureInfo.InvariantCulture);
            element.AppendChild(valueRank);

            System.Xml.XmlElement dimensions = document.CreateElement(
                "uax", "ArrayDimensions", UaXmlNamespace);
            foreach (string part in (argument.ArrayDimensions ?? string.Empty).Split(','))
            {
                if (uint.TryParse(
                    part.Trim(),
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out uint value))
                {
                    System.Xml.XmlElement dimension = document.CreateElement(
                        "uax", "UInt32", UaXmlNamespace);
                    dimension.InnerText = value.ToString(CultureInfo.InvariantCulture);
                    dimensions.AppendChild(dimension);
                }
            }
            element.AppendChild(dimensions);

            System.Xml.XmlElement description = document.CreateElement(
                "uax", "Description", UaXmlNamespace);
            if (FirstText(argument.Description) is { Length: > 0 } text)
            {
                if (FirstLocale(argument.Description) is { Length: > 0 } locale)
                {
                    System.Xml.XmlElement localeElement = document.CreateElement(
                        "uax", "Locale", UaXmlNamespace);
                    localeElement.InnerText = locale;
                    description.AppendChild(localeElement);
                }
                System.Xml.XmlElement value = document.CreateElement("uax", "Text", UaXmlNamespace);
                value.InnerText = text;
                description.AppendChild(value);
            }
            element.AppendChild(description);
            return element;
        }
    }
}
