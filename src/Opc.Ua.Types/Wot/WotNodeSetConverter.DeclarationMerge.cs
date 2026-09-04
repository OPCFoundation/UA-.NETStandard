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
    /// WoT Binding Section 5.2.1 declaration merge, and Section 6.8's
    /// open-content rule, for the <see cref="WotNodeSetConverter"/>.
    /// </summary>
    public static partial class WotNodeSetConverter
    {
        /// <summary>
        /// Populates the instance declarations of the bound type with the
        /// members that name them, and applies the open-content rule.
        /// </summary>
        /// <remarks>
        /// <para>
        /// A document bound to an existing type does not invent the members the
        /// type already declares: a member whose qualified BrowseName is
        /// exactly a declaration's populates that declaration. The projected
        /// Node then carries the declaration's ReferenceType, type definition,
        /// DataType, ValueRank, ArrayDimensions and - for a Method - the
        /// declaration it is an instance of. Without the merge the Node would
        /// be a second, differently-reached Node under a name the type has
        /// already spoken for, which is exactly the duplicate sibling the
        /// clause forbids.
        /// </para>
        /// <para>
        /// The merge runs after every affordance has been synthesized, so it
        /// sees the Nodes as they were actually projected rather than the
        /// document's description of them, and it rewrites in place so that
        /// nothing is added and nothing is left behind.
        /// </para>
        /// </remarks>
        private static void MergeInstanceDeclarations(
            WotDocument document,
            UANodeSet nodeSet,
            List<UANode> items,
            List<Reference> rootReferences,
            string rootNodeId,
            WotDeclarationCatalog? declarations,
            List<WotDiagnostic> diagnostics)
        {
            bool closed = ReadAdditionalProperties(document) == false;
            bool bound = declarations is { TypeNodeId: not null };
            if (!bound)
            {
                // A document that binds to no type states its own members, so
                // there is no declared set to populate and none to close
                // against. The term is still preserved for a consumer.
                return;
            }

            // Every declaration that *was* read is applied, whole closure or
            // not. A declaration the local context answered for is a fact about
            // the bound type, and skipping it because some other part of the
            // closure could not be read is what produces the duplicate sibling
            // Section 5.2.1 forbids - under a name the type has already spoken
            // for, and with the wrong ReferenceType and type definition.
            if (declarations!.HasDeclarations)
            {
                MergeAgainstDeclarations(
                    document, nodeSet, items, rootReferences, rootNodeId, declarations,
                    // The closed-content rule is only decidable against a whole
                    // closure: a member the known part does not declare may well
                    // be declared by the part that would not answer.
                    closed && declarations.IsComplete,
                    diagnostics);
            }

            if (declarations.HasDeclarations && declarations.IsComplete)
            {
                return;
            }

            // The rule cannot be fully evaluated, and that is reported whether
            // or not the document closes its content. Section 6.8 is a
            // closed-content statement, so a closed document that cannot be
            // checked fails; an open document states no such rule, so the
            // populated members stand and the gap is a warning rather than a
            // refusal - but it is never silence, because silence is
            // indistinguishable from a type that declares nothing.
            diagnostics.Add(new WotDiagnostic(
                closed ? WotDiagnosticSeverity.Error : WotDiagnosticSeverity.Warning,
                WotDiagnosticCode.DeclarationsUnavailable,
                (closed
                    ? "The document states uav:additionalProperties: false and binds to "
                    : "The document binds to ") +
                $"'{declarations.TypeNodeId}', but the instance declarations of that type " +
                (declarations.HasDeclarations
                    ? "are incomplete, so a member the unread part declares cannot be told " +
                        "from one the type never declared."
                    : "are not available, so the rule cannot be evaluated.") +
                (closed
                    ? " The closed-content rule cannot be evaluated."
                    : " The declarations that were read were applied.") +
                (string.IsNullOrEmpty(declarations.Detail)
                    ? string.Empty
                    : " " + declarations.Detail),
                new WotLocation(jsonPointer: closed
                    ? "/uav:additionalProperties"
                    : "/links")));
        }

        private static void MergeAgainstDeclarations(
            WotDocument document,
            UANodeSet nodeSet,
            List<UANode> items,
            List<Reference> rootReferences,
            string rootNodeId,
            WotDeclarationCatalog declarations,
            bool closed,
            List<WotDiagnostic> diagnostics)
        {
            Dictionary<string, JsonElement> affordances = IndexAffordances(document);
            foreach (UANode node in items)
            {
                if (!IsDirectMember(node, rootNodeId, rootReferences))
                {
                    continue;
                }
                if (!TryResolveQualifiedName(
                    nodeSet, node.BrowseName, out string namespaceUri, out string browseName))
                {
                    continue;
                }

                IReadOnlyList<WotTypeDeclaration> matches =
                    declarations.Match(namespaceUri, browseName);
                if (matches.Count == 0)
                {
                    if (closed)
                    {
                        diagnostics.Add(new WotDiagnostic(
                            WotDiagnosticSeverity.Error,
                            WotDiagnosticCode.UndeclaredMember,
                            $"The member '{browseName}' is not declared by the bound type " +
                            $"'{declarations.TypeNodeId}', and the document states " +
                            "uav:additionalProperties: false (WoT Binding Section 6.8).",
                            WotLocation.FromNode(node.NodeId)));
                    }
                    continue;
                }
                if (matches.Count > 1)
                {
                    diagnostics.Add(new WotDiagnostic(
                        WotDiagnosticSeverity.Error,
                        WotDiagnosticCode.DeclarationAmbiguous,
                        $"The bound type '{declarations.TypeNodeId}' declares " +
                        $"{matches.Count} members named '{browseName}', so the document " +
                        "does not say which one it populates.",
                        WotLocation.FromNode(node.NodeId)));
                    continue;
                }

                affordances.TryGetValue(browseName, out JsonElement affordance);
                Populate(
                    nodeSet, node, affordance, matches[0], rootNodeId, rootReferences,
                    diagnostics);
            }
        }

        /// <summary>
        /// Maps the local name each affordance projects onto the affordance
        /// itself, so the merge can tell what the document actually
        /// <em>stated</em> from what the readable mapping inferred.
        /// </summary>
        /// <remarks>
        /// Section 9.1 gives the readable mapping one channel for a DataType -
        /// the DataSchema's json type - and that channel carries six OPC UA
        /// DataTypes, so a Node whose DataType came from it is not a statement
        /// the declaration can contradict. A definitive
        /// <c>uav:mapToType</c>, <c>uav:dataTypeId</c> or <c>uav:valueRank</c>
        /// is.
        /// </remarks>
        private static Dictionary<string, JsonElement> IndexAffordances(WotDocument document)
        {
            var index = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            AddAffordances(index, document.Properties);
            AddAffordances(index, document.Actions);
            AddAffordances(index, document.Events);
            return index;
        }

        private static void AddAffordances(
            Dictionary<string, JsonElement> index,
            IReadOnlyDictionary<string, JsonElement> affordances)
        {
            foreach (KeyValuePair<string, JsonElement> affordance in affordances)
            {
                string local =
                    LocalName(GetElementString(affordance.Value, "uav:browseName")) ??
                    affordance.Key;
                index[local] = affordance.Value;
            }
        }

        /// <summary>
        /// Gets whether a projected Node is a member of the projection root, as
        /// opposed to a nested Node of one of its members or the root itself.
        /// </summary>
        /// <remarks>
        /// The merge runs while the only Nodes that exist are the ones the
        /// affordance passes synthesized, so membership is decided by the
        /// root's own References alone. Whether the Node's NodeClass agrees
        /// with the declaration it matches is a separate question, and
        /// <see cref="KindMatches"/> answers it - with a report rather than
        /// with silence, because a member that names a declaration it cannot
        /// be is a mistake in the document rather than a Node to skip.
        /// </remarks>
        private static bool IsDirectMember(
            UANode node, string rootNodeId, List<Reference> rootReferences)
        {
            if (string.Equals(node.NodeId, rootNodeId, StringComparison.Ordinal))
            {
                return false;
            }
            foreach (Reference reference in rootReferences)
            {
                if (reference.IsForward &&
                    string.Equals(reference.Value, node.NodeId, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Applies one declaration to the Node that populates it.
        /// </summary>
        private static void Populate(
            UANodeSet nodeSet,
            UANode node,
            JsonElement affordance,
            WotTypeDeclaration declaration,
            string rootNodeId,
            List<Reference> rootReferences,
            List<WotDiagnostic> diagnostics)
        {
            if (!KindMatches(node, declaration.Kind))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.DeclarationMismatch,
                    $"The member '{declaration.BrowseName}' is projected as a " +
                    $"{DescribeNodeClass(node)} but the bound type declares it as a " +
                    $"{declaration.Kind}, so it cannot populate the declaration.",
                    WotLocation.FromNode(node.NodeId)));
                return;
            }

            if (node is UAVariable variable &&
                !PopulateVariable(nodeSet, variable, affordance, declaration, diagnostics))
            {
                return;
            }
            if (node is UAMethod method)
            {
                method.MethodDeclarationId =
                    ToNodeSetNodeId(declaration.MethodDeclarationNodeId, nodeSet, diagnostics);
            }

            ApplyDeclaredReferences(
                nodeSet, node, declaration, rootNodeId, rootReferences, diagnostics);

            diagnostics.Add(new WotDiagnostic(
                WotDiagnosticSeverity.Info,
                WotDiagnosticCode.DeclarationPopulated,
                $"The member '{declaration.BrowseName}' populates the " +
                (declaration.IsInherited ? "inherited " : string.Empty) +
                (declaration.IsMandatory ? "mandatory " : string.Empty) +
                $"{declaration.Kind} declaration '{declaration.NodeId}' of " +
                $"'{declaration.DeclaringTypeNodeId}' rather than adding a sibling.",
                WotLocation.FromNode(node.NodeId)));
        }

        /// <summary>
        /// Adopts the DataType, ValueRank and ArrayDimensions the declaration
        /// states, and reports a member that contradicts any of them.
        /// </summary>
        /// <remarks>
        /// A member that says nothing definitive about its DataType takes the
        /// declaration's, because the declaration is the more specific
        /// statement and the member is describing the same Node - Section 9.1's
        /// json type is one channel for six DataTypes and settles nothing. A
        /// member that states one of Section 5.4's definitive DataTypes, or an
        /// explicit <c>uav:valueRank</c>, and states something different is not
        /// describing the same Node at all, so it is reported rather than
        /// overwritten: silently adopting the declaration would discard what the
        /// document said, and silently keeping the member's own value would
        /// leave an instance that violates the type it claims to be an instance
        /// of.
        /// </remarks>
        private static bool PopulateVariable(
            UANodeSet nodeSet,
            UAVariable variable,
            JsonElement affordance,
            WotTypeDeclaration declaration,
            List<WotDiagnostic> diagnostics)
        {
            string? declared = declaration.DataType.Length == 0
                ? null
                : ToNodeSetNodeId(declaration.DataType, nodeSet, diagnostics);
            if (declared is not null &&
                StatesDefinitiveDataType(affordance) &&
                variable.DataType is { Length: > 0 } &&
                !string.Equals(variable.DataType, declared, StringComparison.Ordinal))
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.DeclarationMismatch,
                    $"The member '{declaration.BrowseName}' states DataType " +
                    $"'{variable.DataType}' but the declaration it names states " +
                    $"'{declared}'.",
                    WotLocation.FromNode(variable.NodeId)));
                return false;
            }
            if (StatesTerm(affordance, ValueRankTerm) &&
                declaration.ValueRank != variable.ValueRank)
            {
                diagnostics.Add(new WotDiagnostic(
                    WotDiagnosticSeverity.Error,
                    WotDiagnosticCode.DeclarationMismatch,
                    $"The member '{declaration.BrowseName}' states ValueRank " +
                    $"{variable.ValueRank.ToString(CultureInfo.InvariantCulture)} but the " +
                    "declaration it names states " +
                    declaration.ValueRank.ToString(CultureInfo.InvariantCulture) + ".",
                    WotLocation.FromNode(variable.NodeId)));
                return false;
            }

            if (declared is not null)
            {
                variable.DataType = declared;
            }
            variable.ValueRank = declaration.ValueRank;
            if (declaration.ArrayDimensions.Count > 0)
            {
                var parts = new List<string>(declaration.ArrayDimensions.Count);
                foreach (uint dimension in declaration.ArrayDimensions)
                {
                    parts.Add(dimension.ToString(CultureInfo.InvariantCulture));
                }
                variable.ArrayDimensions = string.Join(",", parts);
            }
            return true;
        }

        /// <summary>
        /// Gets whether a DataSchema states a DataType definitively, as opposed
        /// to letting the json type imply one.
        /// </summary>
        private static bool StatesDefinitiveDataType(JsonElement affordance)
        {
            return StatesTerm(affordance, "uav:mapToType") ||
                StatesTerm(affordance, "uav:dataTypeId") ||
                StatesTerm(affordance, "uav:dataTypeDefinition");
        }

        private static bool StatesTerm(JsonElement affordance, string term)
        {
            return affordance.ValueKind == JsonValueKind.Object &&
                affordance.TryGetProperty(term, out _);
        }

        /// <summary>
        /// Rewrites the two References that place a member so they are the ones
        /// the declaration states, replacing rather than adding.
        /// </summary>
        private static void ApplyDeclaredReferences(
            UANodeSet nodeSet,
            UANode node,
            WotTypeDeclaration declaration,
            string rootNodeId,
            List<Reference> rootReferences,
            List<WotDiagnostic> diagnostics)
        {
            string typeDefinition = declaration.TypeDefinitionNodeId.Length == 0
                ? string.Empty
                : ToNodeSetNodeId(declaration.TypeDefinitionNodeId, nodeSet, diagnostics);
            // Every Node the merge reaches is one the synthesis produced and
            // gave its placing References to, so there is always a list to
            // rewrite: the member was found through one of the References in
            // it.
            foreach (Reference reference in node.References!)
            {
                if (!reference.IsForward &&
                    IsOwnershipReference(reference.ReferenceType) &&
                    string.Equals(reference.Value, rootNodeId, StringComparison.Ordinal))
                {
                    reference.ReferenceType = declaration.ReferenceTypeName;
                }
                else if (reference.IsForward &&
                    typeDefinition.Length != 0 &&
                    string.Equals(
                        reference.ReferenceType,
                        "HasTypeDefinition",
                        StringComparison.Ordinal))
                {
                    reference.Value = typeDefinition;
                }
            }
            foreach (Reference reference in rootReferences)
            {
                if (reference.IsForward &&
                    IsOwnershipReference(reference.ReferenceType) &&
                    string.Equals(reference.Value, node.NodeId, StringComparison.Ordinal))
                {
                    reference.ReferenceType = declaration.ReferenceTypeName;
                }
            }
        }

        private static bool IsOwnershipReference(string? referenceType)
        {
            return referenceType is "HasComponent" or "HasProperty" or "HasOrderedComponent";
        }

        private static bool KindMatches(UANode node, WotDeclarationKind kind)
        {
            return kind switch
            {
                WotDeclarationKind.Variable => node is UAVariable,
                WotDeclarationKind.Method => node is UAMethod,
                WotDeclarationKind.Object => node is UAObject,
                WotDeclarationKind.Event => node is UAObjectType,
                _ => false
            };
        }

        /// <summary>
        /// Names the NodeClass a member was projected as, for a report that
        /// has to say which of the member and the declaration to change.
        /// </summary>
        /// <remarks>
        /// The synthesis projects an affordance as a Variable, a Method or an
        /// EventType and nothing else, so those are the three NodeClasses a
        /// member of the projection root can have.
        /// </remarks>
        private static string DescribeNodeClass(UANode node)
        {
            return node switch
            {
                UAMethod => "Method",
                UAObjectType => "ObjectType",
                _ => "Variable"
            };
        }

        /// <summary>
        /// Resolves a NodeSet BrowseName back into the NamespaceUri and local
        /// name a declaration is matched by.
        /// </summary>
        private static bool TryResolveQualifiedName(
            UANodeSet nodeSet,
            string? browseName,
            out string namespaceUri,
            out string localName)
        {
            namespaceUri = string.Empty;
            localName = string.Empty;
            if (string.IsNullOrEmpty(browseName))
            {
                return false;
            }
            int separator = browseName!.IndexOf(':', StringComparison.Ordinal);
            if (separator < 0)
            {
                namespaceUri = WotVocabulary.OpcUaNamespace;
                localName = browseName;
                return true;
            }
            if (!int.TryParse(
#if NETSTANDARD2_0 || NET472 || NET48
                    browseName.Substring(0, separator),
#else
                    browseName.AsSpan(0, separator),
#endif
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out int index))
            {
                return false;
            }
            localName = browseName.Substring(separator + 1);
            if (index == 0)
            {
                namespaceUri = WotVocabulary.OpcUaNamespace;
                return true;
            }

            // The synthesis seeds the table with the model's own namespace
            // before it writes any member, so there is always a table to read;
            // an index the table does not reach names a namespace the document
            // never declared.
            string[] uris = nodeSet.NamespaceUris!;
            if (index > uris.Length)
            {
                return false;
            }
            namespaceUri = uris[index - 1];
            return true;
        }

        /// <summary>
        /// Reads the <c>uav:includeInherited</c> flag of Section 6.8.
        /// </summary>
        /// <returns>
        /// The declared value, or <c>null</c> where the document states none.
        /// </returns>
        internal static bool? ReadIncludeInherited(WotDocument document)
        {
            return ReadRootFlag(document, "uav:includeInherited");
        }

        /// <summary>
        /// Reads the <c>uav:additionalProperties</c> flag of Section 6.8.
        /// </summary>
        /// <returns>
        /// The declared value, or <c>null</c> where the document states none.
        /// </returns>
        internal static bool? ReadAdditionalProperties(WotDocument document)
        {
            return ReadRootFlag(document, "uav:additionalProperties");
        }

        private static bool? ReadRootFlag(WotDocument document, string term)
        {
            if (!document.RootElement.TryGetProperty(
                term, out JsonElement value))
            {
                return null;
            }
            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                _ => null
            };
        }
    }
}
