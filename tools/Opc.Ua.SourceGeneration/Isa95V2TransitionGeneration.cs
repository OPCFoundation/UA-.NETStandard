// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Opc.Ua.SourceGeneration
{
    /// <summary>
    /// Projects the published ISA95 Job Control V2 state machines into a build-time transition table.
    /// </summary>
    internal static class Isa95V2TransitionGeneration
    {
        /// <summary>
        /// Registers generation only for the exact ISA95 Job Control V2 NodeSet filename.
        /// </summary>
        internal static void Register(IncrementalGeneratorInitializationContext context)
        {
            context.RegisterSourceOutput(
                context.AdditionalTextsProvider
                    .Where(static file => string.Equals(
                        Path.GetFileName(file.Path), kFileName, StringComparison.Ordinal))
                    .Collect(),
                static (production, inputs) =>
                    SourceGenerator.Guard(production, () => Emit(production, inputs)));
        }

        private static void Emit(SourceProductionContext context, ImmutableArray<AdditionalText> inputs)
        {
            if (inputs.IsEmpty)
            {
                return;
            }

            try
            {
                if (inputs.Length != 1)
                {
                    throw new FormatException("Expected a single AdditionalFile with this name.");
                }

                string text = inputs[0].GetText(context.CancellationToken)?.ToString()
                    ?? throw new FormatException("AdditionalFile could not be read.");
                using var input = new StringReader(text);
                using var reader = XmlReader.Create(input, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null
                });
                XElement? root = XDocument.Load(reader).Root;
                if (root == null || root.Name != s_ns + "UANodeSet" ||
                    root.Element(s_ns + "NamespaceUris")?.Elements(s_ns + "Uri").FirstOrDefault()?.Value !=
                        kModelUri ||
                    root.Element(s_ns + "Models")?.Elements(s_ns + "Model")
                        .Any(static model => (string?)model.Attribute("ModelUri") == kModelUri) != true)
                {
                    throw new FormatException("The ISA95 Job Control V2 model must occupy namespace index one.");
                }

                var model = new Model(root);
                context.AddSource("Opc.Ua.ISA95.JobControl.Isa95V2TransitionTable.g.cs",
                    Render(model.ReadTransitions()));
            }
            catch (Exception exception) when (exception is FormatException or XmlException)
            {
                context.ReportDiagnostic(Diagnostic.Create(SourceGenerator.GenericError, Location.None,
                    $"ISA95 Job Control V2 transition table from '{inputs[0].Path}': {exception.Message}"));
            }
        }

        private static string Render(IEnumerable<Transition> transitions)
        {
            var source = new StringBuilder(CodeTemplates.CodeHeader).AppendLine().AppendLine(
                """

                namespace Opc.Ua.ISA95.JobControl
                {
                    /// <summary>
                    /// Identifies a state machine whose transition numbers are scoped to that machine.
                    /// </summary>
                    internal enum Isa95V2Machine
                    {
                        Receiver,
                        Prepare,
                        Interrupted,
                        Ended
                    }

                    /// <summary>
                    /// A directed edge declared by the Job Control V2 model.
                    /// </summary>
                    internal readonly record struct Isa95V2Transition(
                        Isa95V2Machine Machine, uint From, uint To, uint Number, string? Cause, bool HasStatusEffect);

                    /// <summary>
                    /// Transitions declared by the Job Control V2 NodeSet.
                    /// </summary>
                    internal static class Isa95V2TransitionTable
                    {
                        /// <summary>
                        /// Gets the transitions in machine, source state, and target state order.
                        /// </summary>
                        internal static global::System.ReadOnlySpan<Isa95V2Transition> Entries => s_entries;

                        /// <summary>
                        /// Looks up a transition by machine and the model's source and target state numbers.
                        /// </summary>
                        internal static bool TryGet(
                            Isa95V2Machine machine, uint from, uint to, out Isa95V2Transition edge)
                        {
                            foreach (Isa95V2Transition entry in Entries)
                            {
                                if (entry.Machine == machine && entry.From == from && entry.To == to)
                                {
                                    edge = entry;
                                    return true;
                                }
                            }
                            edge = default;
                            return false;
                        }

                        private static readonly Isa95V2Transition[] s_entries =
                        [
                """);
            foreach (Transition edge in transitions)
            {
                string cause = edge.Cause == null ? "null" : SymbolDisplay.FormatLiteral(edge.Cause, quote: true);
                string effect = edge.HasStatusEffect ? "true" : "false";
                string key = FormattableString.Invariant($"Isa95V2Machine.{edge.Machine}, {edge.From}u, {edge.To}u");
                string details = FormattableString.Invariant($"{edge.Number}u, {cause}, {effect}");
                source.AppendLine($"            new({key}, {details}),");
            }
            return source.AppendLine(
                """
                        ];
                    }
                }
                """).ToString();
        }

        private const string kFileName = "Opc.Ua.ISA95.JobControl.V2.NodeSet2.xml";
        private const string kModelUri = "http://opcfoundation.org/UA/ISA95-JOBCONTROL_V2/";
        private static readonly XNamespace s_ns = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd";
        private static readonly XNamespace s_types = "http://opcfoundation.org/UA/2008/02/Types.xsd";
        private enum ReferenceType : uint
        {
            HasTypeDefinition = 40,
            HasSubtype = 45,
            HasProperty = 46,
            HasComponent = 47,
            FromState = 51,
            ToState = 52,
            HasCause = 53,
            HasEffect = 54
        }

        private sealed class Model
        {
            /// <summary>
            /// Indexes model declarations and aliases using normalized NodeIds.
            /// </summary>
            internal Model(XElement root)
            {
                foreach (XElement alias in root.Element(s_ns + "Aliases")?.Elements(s_ns + "Alias") ?? [])
                {
                    string name = (string?)alias.Attribute("Alias") ?? string.Empty;
                    if (name.Length == 0 || m_aliases.ContainsKey(name))
                    {
                        throw new FormatException($"Missing or duplicate alias '{name}'.");
                    }
                    m_aliases.Add(name, ParseId(alias.Value.Trim()));
                }

                foreach (XElement element in root.Elements().Where(static node => node.Attribute("NodeId") != null))
                {
                    NodeId id = ParseId((string)element.Attribute("NodeId")!);
                    if (m_nodes.ContainsKey(id))
                    {
                        throw new FormatException($"Duplicate NodeId '{id}'.");
                    }
                    m_nodes.Add(id, element);
                }
            }

            /// <summary>
            /// Reads the scoped transitions, rejecting declarations that cannot be represented unambiguously.
            /// </summary>
            internal IEnumerable<Transition> ReadTransitions()
            {
                NodeId baseReceiver = FindType("ISA95JobOrderReceiverObjectType");
                NodeId receiver = FindType("ISA95JobOrderReceiverSubStatesType");
                NodeId statusEvent = FindType("ISA95JobOrderStatusEventType");
                if (ReadReference(m_nodes[receiver], ReferenceType.HasSubtype, required: true, forward: false) !=
                    baseReceiver)
                {
                    throw new FormatException("The receiver sub-states type must derive from the base receiver type.");
                }

                // Use the effective receiver's declarations only; the base type contributes inherited methods.
                (string Name, NodeId Owner)[] machines =
                [
                    ("Receiver", receiver),
                    ("Prepare", FindType("ISA95PrepareStateMachineType")),
                    ("Interrupted", FindType("ISA95InterruptedStateMachineType")),
                    ("Ended", FindType("ISA95EndedStateMachineType"))
                ];
                foreach ((string name, NodeId owner) in machines)
                {
                    NodeId[] components = ReadComponents(owner).ToArray();
                    var methodIds = new HashSet<NodeId>(components.Where(IsMethod));
                    if (owner == receiver)
                    {
                        methodIds.UnionWith(ReadComponents(baseReceiver).Where(IsMethod));
                    }

                    var states = new Dictionary<NodeId, uint>();
                    var stateNumbers = new HashSet<uint>();
                    var transitionNumbers = new HashSet<uint>();
                    var endpoints = new HashSet<(uint, uint)>();
                    var transitions = new List<Transition>();
                    var declarations = new List<XElement>();
                    foreach (XElement node in components.Select(id => m_nodes[id])
                        .Where(static node => node.Name == s_ns + "UAObject"))
                    {
                        NodeId type = ReadReference(node, ReferenceType.HasTypeDefinition, required: true);
                        if (type == s_stateType || type == s_initialStateType)
                        {
                            uint number = ReadNumber(node, "StateNumber");
                            if (!stateNumbers.Add(number))
                            {
                                throw Invalid(node, $"duplicates state number {number} in {name}.");
                            }
                            states.Add(ParseId((string)node.Attribute("NodeId")!), number);
                        }
                        else if (type == s_transitionType)
                        {
                            declarations.Add(node);
                        }
                        else if (References(node, ReferenceType.FromState, forward: true).Any() ||
                            References(node, ReferenceType.ToState, forward: true).Any())
                        {
                            throw Invalid(node, "declares FromState or ToState without TransitionType.");
                        }
                    }

                    foreach (XElement declaration in declarations)
                    {
                        NodeId fromId = ReadReference(declaration, ReferenceType.FromState, required: true);
                        NodeId toId = ReadReference(declaration, ReferenceType.ToState, required: true);
                        if (!states.TryGetValue(fromId, out uint from) || !states.TryGetValue(toId, out uint to))
                        {
                            throw Invalid(declaration, $"endpoints '{fromId}' and '{toId}' must be states of {name}.");
                        }
                        uint number = ReadNumber(declaration, "TransitionNumber");
                        if (!transitionNumbers.Add(number) || !endpoints.Add((from, to)))
                        {
                            throw Invalid(declaration, $"duplicates a transition number or state pair in {name}.");
                        }
                        NodeId causeId = ReadReference(declaration, ReferenceType.HasCause, required: false);
                        string? cause = null;
                        if (!causeId.IsNull)
                        {
                            if (!methodIds.Contains(causeId))
                            {
                                throw Invalid(declaration, $"cause '{causeId}' is not a method of {name}.");
                            }
                            QualifiedName methodName = BrowseName(m_nodes[causeId]);
                            cause = methodName.Name;
                            if (string.IsNullOrEmpty(cause) || methodName.NamespaceIndex != 1)
                            {
                                throw Invalid(m_nodes[causeId], "cause method needs a V2 BrowseName.");
                            }
                        }
                        NodeId effect = ReadReference(declaration, ReferenceType.HasEffect, required: false);
                        if (!effect.IsNull && effect != statusEvent)
                        {
                            throw Invalid(declaration,
                                $"effect '{effect}' must reference ISA95JobOrderStatusEventType.");
                        }
                        transitions.Add(new Transition(name, from, to, number, cause, effect == statusEvent));
                    }
                    if (transitions.Count == 0)
                    {
                        throw Invalid(m_nodes[owner], "declares no transitions.");
                    }
                    foreach (Transition transition in transitions.OrderBy(edge => edge.From).ThenBy(edge => edge.To))
                    {
                        yield return transition;
                    }
                }
            }

            private NodeId FindType(string name)
            {
                var browseName = new QualifiedName(name, 1);
                NodeId[] matches = m_nodes.Where(pair => pair.Value.Name == s_ns + "UAObjectType" &&
                    BrowseName(pair.Value) == browseName).Select(static pair => pair.Key).ToArray();
                return matches.Length == 1
                    ? matches[0]
                    : throw new FormatException($"Expected exactly one ObjectType named '{browseName}'.");
            }

            private IEnumerable<NodeId> ReadComponents(NodeId owner)
            {
                XElement node = m_nodes[owner];
                var seen = new HashSet<NodeId>();
                foreach (NodeId id in References(node, ReferenceType.HasComponent, forward: true))
                {
                    if (!seen.Add(id) || !m_nodes.TryGetValue(id, out XElement? component))
                    {
                        throw Invalid(node, $"HasComponent target '{id}' is missing or duplicated.");
                    }
                    NodeId inverse = ReadReference(component, ReferenceType.HasComponent,
                        required: false, forward: false);
                    if (!inverse.IsNull && inverse != owner)
                    {
                        throw Invalid(component, $"HasComponent owner '{inverse}' does not match '{owner}'.");
                    }
                    yield return id;
                }

                // A failed owner reference must not silently drop a state, edge, or method.
                foreach ((NodeId id, XElement component) in m_nodes)
                {
                    if (!seen.Contains(id) &&
                        (component.Name == s_ns + "UAObject" || component.Name == s_ns + "UAMethod") &&
                        (string?)component.Attribute("ParentNodeId") is string parent && ParseId(parent) == owner)
                    {
                        throw Invalid(component, $"is missing from '{owner}' HasComponent references.");
                    }
                }
            }

            private uint ReadNumber(XElement node, string propertyName)
            {
                var browseName = new QualifiedName(propertyName);
                var properties = new List<XElement>();
                foreach (NodeId id in References(node, ReferenceType.HasProperty, forward: true))
                {
                    if (!m_nodes.TryGetValue(id, out XElement? property))
                    {
                        throw Invalid(node, $"HasProperty target '{id}' does not exist.");
                    }
                    if (BrowseName(property) == browseName)
                    {
                        properties.Add(property);
                    }
                }
                XElement? propertyNode = properties.Count == 1 ? properties[0] : null;
                if (propertyNode == null || propertyNode.Name != s_ns + "UAVariable" ||
                    ResolveId((string?)propertyNode.Attribute("DataType") ?? string.Empty) != s_uint32 ||
                    !uint.TryParse(propertyNode.Element(s_ns + "Value")?.Element(s_types + "UInt32")?.Value.Trim(),
                        NumberStyles.None, CultureInfo.InvariantCulture, out uint number))
                {
                    throw Invalid(node, $"must declare exactly one UInt32 {propertyName} property with a value.");
                }
                NodeId parent = ReadReference(propertyNode, ReferenceType.HasProperty, required: false, forward: false);
                if (!parent.IsNull && parent != ParseId((string)node.Attribute("NodeId")!))
                {
                    throw Invalid(node, $"{propertyName} belongs to '{parent}'.");
                }
                return number;
            }

            private NodeId ReadReference(XElement node, ReferenceType type, bool required, bool forward = true)
            {
                NodeId[] targets = References(node, type, forward).ToArray();
                return targets.Length switch
                {
                    0 when !required => NodeId.Null,
                    1 => targets[0],
                    _ => throw Invalid(node, $"has {targets.Length} {type} references; expected " +
                        (required ? "one." : "at most one."))
                };
            }

            private IEnumerable<NodeId> References(XElement node, ReferenceType type, bool forward)
            {
                var referenceType = new NodeId((uint)type);
                foreach (XElement reference in node.Element(s_ns + "References")?.Elements(s_ns + "Reference") ?? [])
                {
                    if (ResolveId((string?)reference.Attribute("ReferenceType") ?? string.Empty) == referenceType &&
                        ((bool?)reference.Attribute("IsForward") ?? true) == forward)
                    {
                        yield return ResolveId(reference.Value.Trim());
                    }
                }
            }

            private bool IsMethod(NodeId id) => m_nodes[id].Name == s_ns + "UAMethod";

            private NodeId ResolveId(string value) =>
                m_aliases.TryGetValue(value, out NodeId id) ? id : ParseId(value);

            private static NodeId ParseId(string value) =>
                NodeId.TryParse(value, out NodeId id) && !id.IsNull
                    ? id
                    : throw new FormatException($"Invalid NodeId '{value}'.");

            private static QualifiedName BrowseName(XElement node) =>
                QualifiedName.Parse((string?)node.Attribute("BrowseName") ?? string.Empty);

            private static FormatException Invalid(XElement node, string message) =>
                new($"Node '{node.Attribute("NodeId")?.Value}' ({node.Attribute("BrowseName")?.Value}) {message}");

            private static readonly NodeId s_stateType = new(2307u);
            private static readonly NodeId s_initialStateType = new(2309u);
            private static readonly NodeId s_transitionType = new(2310u);
            private static readonly NodeId s_uint32 = new(7u);
            private readonly Dictionary<string, NodeId> m_aliases = new(StringComparer.Ordinal);
            private readonly Dictionary<NodeId, XElement> m_nodes = [];
        }

        private readonly record struct Transition(
            string Machine, uint From, uint To, uint Number, string? Cause, bool HasStatusEffect);
    }
}
