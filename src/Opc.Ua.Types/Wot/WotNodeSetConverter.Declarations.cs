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
using System.Text.Json;

namespace Opc.Ua.Wot
{
    /// <summary>
    /// Derives the instance declarations a Thing Model states, so that a local
    /// context built over documents can answer
    /// <see cref="IWotTypeDeclarationResolver"/> by exactly the rules the
    /// conversion synthesizes by rather than by a copy that can drift.
    /// </summary>
    public static partial class WotNodeSetConverter
    {
        /// <summary>
        /// Describes the instance declarations a Thing Model states itself -
        /// its own declarations, without the ones it inherits.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Every affordance of a Thing Model becomes a Node the projected type
        /// declares: a property becomes a Variable reached by
        /// <c>HasProperty</c> when it binds itself to <c>PropertyType</c> and by
        /// <c>HasComponent</c> otherwise, an action becomes a Method reached by
        /// <c>HasComponent</c>, and an event becomes an EventType the type
        /// declares it raises, reached by <c>GeneratesEvent</c>.
        /// </para>
        /// <para>
        /// A Thing Description declares nothing: it projects an instance, and
        /// an instance's members are populated declarations rather than
        /// declarations of their own. The method returns <c>false</c> for one.
        /// </para>
        /// </remarks>
        /// <param name="document">The document to describe.</param>
        /// <param name="declarations">
        /// The declarations, ordered by NamespaceUri, BrowseName then kind.
        /// </param>
        /// <param name="supertypeReferences">
        /// The <c>tm:extends</c> hrefs the document names, in document order.
        /// They are what an effective closure follows.
        /// </param>
        /// <returns>
        /// <c>true</c> when <paramref name="document"/> is a Thing Model.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="document"/> is <c>null</c>.
        /// </exception>
        public static bool TryDescribeTypeDeclarations(
            WotDocument document,
            out ArrayOf<WotTypeDeclaration> declarations,
            out ArrayOf<string> supertypeReferences)
        {
            declarations = ArrayOf<WotTypeDeclaration>.Empty;
            supertypeReferences = ArrayOf<string>.Empty;
            if (!TryDescribeProjectedType(
                document, out string namespaceUri, out string rootLocal, out string typeNodeId))
            {
                return false;
            }

            var found = new List<WotTypeDeclaration>();
            foreach (KeyValuePair<string, JsonElement> property in document.Properties)
            {
                found.Add(DescribeVariableDeclaration(
                    document, property.Key, property.Value, namespaceUri, rootLocal, typeNodeId));
            }
            foreach (KeyValuePair<string, JsonElement> action in document.Actions)
            {
                found.Add(DescribeMethodDeclaration(
                    document, action.Key, action.Value, namespaceUri, rootLocal, typeNodeId));
            }
            foreach (KeyValuePair<string, JsonElement> eventAffordance in document.Events)
            {
                found.Add(DescribeEventDeclaration(
                    document,
                    eventAffordance.Key,
                    eventAffordance.Value,
                    namespaceUri,
                    rootLocal,
                    typeNodeId));
            }

            found.Sort(WotTypeDeclarations.Compare);
            declarations = found.ToArrayOf();
            supertypeReferences = ReadSupertypeReferences(document);
            return true;
        }

        /// <summary>
        /// Reads the <c>tm:extends</c> hrefs a document names, in document
        /// order, so an effective closure follows the same links the
        /// conversion's own type resolution does.
        /// </summary>
        private static ArrayOf<string> ReadSupertypeReferences(WotDocument document)
        {
            List<string>? hrefs = null;
            foreach (JsonElement link in document.Links)
            {
                if (string.Equals(
                        GetElementString(link, "rel"), "tm:extends", StringComparison.Ordinal) &&
                    GetElementString(link, "href") is { Length: > 0 } href)
                {
                    hrefs ??= [];
                    hrefs.Add(href);
                }
            }
            return hrefs is null ? ArrayOf<string>.Empty : hrefs.ToArrayOf();
        }

        private static WotTypeDeclaration DescribeVariableDeclaration(
            WotDocument document,
            string key,
            JsonElement schema,
            string modelUri,
            string rootLocal,
            string typeNodeId)
        {
            (string declarationNamespace, string local) =
                ResolveDeclarationName(document, schema, key, modelUri);
            string? typeDefinition = ReadDeclaredTypeDefinition(schema);
            return new WotTypeDeclaration
            {
                NamespaceUri = declarationNamespace,
                BrowseName = local,
                Kind = WotDeclarationKind.Variable,
                DeclaringTypeNodeId = typeNodeId,
                NodeId = DeclarationNodeId(schema, modelUri, rootLocal, local),
                ReferenceTypeName = string.Equals(
                    typeDefinition, WotVocabulary.PropertyType, StringComparison.Ordinal)
                    ? "HasProperty"
                    : "HasComponent",
                TypeDefinitionNodeId = typeDefinition ?? WotVocabulary.BaseDataVariableType,
                DataType = ReadDeclaredDataType(schema),
                ValueRank = ReadValueRank(schema),
                ArrayDimensions = ReadDeclaredArrayDimensions(schema),
                ModellingRule = WotTypeDeclarations.ToModellingRule(
                    GetElementString(schema, ModellingRuleTerm))
            };
        }

        private static WotTypeDeclaration DescribeMethodDeclaration(
            WotDocument document,
            string key,
            JsonElement action,
            string modelUri,
            string rootLocal,
            string typeNodeId)
        {
            (string declarationNamespace, string local) =
                ResolveDeclarationName(document, action, key, modelUri);
            string nodeId = DeclarationNodeId(action, modelUri, rootLocal, local);
            return new WotTypeDeclaration
            {
                NamespaceUri = declarationNamespace,
                BrowseName = local,
                Kind = WotDeclarationKind.Method,
                DeclaringTypeNodeId = typeNodeId,
                NodeId = nodeId,
                ReferenceTypeName = "HasComponent",

                // A Method a type declares is itself the declaration every
                // instance of that type points its MethodDeclarationId at.
                MethodDeclarationNodeId = nodeId,
                ModellingRule = WotTypeDeclarations.ToModellingRule(
                    GetElementString(action, ModellingRuleTerm))
            };
        }

        private static WotTypeDeclaration DescribeEventDeclaration(
            WotDocument document,
            string key,
            JsonElement eventAffordance,
            string modelUri,
            string rootLocal,
            string typeNodeId)
        {
            (string declarationNamespace, string local) =
                ResolveDeclarationName(document, eventAffordance, key, modelUri);
            string nodeId = DeclarationNodeId(eventAffordance, modelUri, rootLocal, local);
            return new WotTypeDeclaration
            {
                NamespaceUri = declarationNamespace,
                BrowseName = local,
                Kind = WotDeclarationKind.Event,
                DeclaringTypeNodeId = typeNodeId,
                NodeId = nodeId,
                ReferenceTypeName = "GeneratesEvent",

                // The EventType an event affordance projects is the type of the
                // notification, so it is both the declaration and its own type
                // definition.
                TypeDefinitionNodeId = nodeId,
                ModellingRule = WotTypeDeclarations.ToModellingRule(
                    GetElementString(eventAffordance, ModellingRuleTerm))
            };
        }

        /// <summary>
        /// Derives the qualified name an affordance declares, by the same rules
        /// the synthesis writes the BrowseName with: an explicit
        /// <c>uav:browseName</c> names its own namespace, and an affordance
        /// without one takes its key in the model's own namespace.
        /// </summary>
        private static (string NamespaceUri, string BrowseName) ResolveDeclarationName(
            WotDocument document,
            JsonElement affordance,
            string key,
            string modelUri)
        {
            string? raw = GetElementString(affordance, "uav:browseName");
            if (raw is null)
            {
                return (modelUri, key);
            }
            if (raw.StartsWith("nsu=", StringComparison.Ordinal))
            {
                int delimiter = raw.IndexOf(';', 4);
                if (delimiter > 4 && delimiter + 1 < raw.Length)
                {
                    return (
                        CoreUtils.UnescapeUri(raw.AsSpan(4, delimiter - 4)),
                        raw.Substring(delimiter + 1));
                }
                return (modelUri, raw);
            }
            int separator = raw.IndexOf(':', StringComparison.Ordinal);
            if (separator <= 0 || separator + 1 >= raw.Length)
            {
                return (modelUri, raw);
            }
            string prefix = raw.Substring(0, separator);
            string local = raw.Substring(separator + 1);
            return TryGetContextNamespace(document, prefix, out string namespaceUri)
                ? (namespaceUri, local)
                : (modelUri, local);
        }

        /// <summary>
        /// Derives the identity a declaration's Node takes, matching the
        /// synthesis: an authored <c>uav:id</c> wins, otherwise the identifier
        /// is generated from the root and the declaration's own name, in the
        /// model's own namespace and by the Annex G.1 formula the synthesis
        /// uses.
        /// </summary>
        private static string DeclarationNodeId(
            JsonElement affordance,
            string modelUri,
            string rootLocal,
            string local)
        {
            string? authored = GetElementString(affordance, "uav:id");
            return authored ?? WotPortableIdentity.GenerateNodeId(
                modelUri,
                new ArrayOf<WotBrowsePathElement>(
                [
                    new WotBrowsePathElement(modelUri, rootLocal),
                    new WotBrowsePathElement(modelUri, local)
                ]));
        }

        /// <summary>
        /// Reads an affordance's declared type definition without needing a
        /// NodeSet namespace table, which the declaration view has none of.
        /// </summary>
        private static string? ReadDeclaredTypeDefinition(JsonElement affordance)
        {
            if (affordance.ValueKind != JsonValueKind.Object ||
                !affordance.TryGetProperty("links", out JsonElement links) ||
                links.ValueKind != JsonValueKind.Array)
            {
                return null;
            }
            foreach (JsonElement link in links.EnumerateArray())
            {
                if (link.ValueKind == JsonValueKind.Object &&
                    string.Equals(
                        GetElementString(link, "rel"),
                        TypeBindingRel,
                        StringComparison.Ordinal) &&
                    GetElementString(link, "href") is { Length: > 0 } href)
                {
                    return href;
                }
            }
            return null;
        }

        /// <summary>
        /// Reads the DataType a DataSchema states, by the same precedence the
        /// synthesis uses: the definitive <c>uav:mapToType</c>, then
        /// <c>uav:dataTypeId</c>, then what the json type implies.
        /// </summary>
        private static string ReadDeclaredDataType(JsonElement schema)
        {
            if (GetElementString(schema, "uav:mapToType") is { Length: > 0 } mapped)
            {
                return mapped;
            }
            if (GetElementString(schema, "uav:dataTypeId") is { Length: > 0 } annotated)
            {
                return annotated;
            }
            return WotVocabulary.MapJsonTypeToDataType(
                GetElementString(schema, "type"),
                GetElementString(schema, "contentEncoding"),
                GetElementString(schema, "format"));
        }

        private static ArrayOf<uint> ReadDeclaredArrayDimensions(JsonElement schema)
        {
            if (schema.ValueKind != JsonValueKind.Object ||
                !schema.TryGetProperty(ArrayDimensionsTerm, out JsonElement dimensions) ||
                dimensions.ValueKind != JsonValueKind.Array)
            {
                return ArrayOf<uint>.Empty;
            }
            var values = new List<uint>();
            foreach (JsonElement dimension in dimensions.EnumerateArray())
            {
                if (dimension.ValueKind != JsonValueKind.Number ||
                    !dimension.TryGetUInt32(out uint value))
                {
                    return ArrayOf<uint>.Empty;
                }
                values.Add(value);
            }
            return values.ToArrayOf();
        }

        private const string ModellingRuleTerm = "uav:modellingRule";
    }
}
