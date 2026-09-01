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

namespace Opc.Ua.Wot
{
    /// <summary>
    /// The single statement of what revision of the OPC UA WoT Binding this
    /// library implements, which conformance units and profiles it recognizes,
    /// and the closed value sets its validation rules are written against.
    /// </summary>
    /// <remarks>
    /// WoT Binding Section 4.1 separates three numbers that are easily
    /// confused: the vocabulary revision a document declares
    /// (<c>uav:bindingVersion</c>), the native projection grammar
    /// (<c>profileVersion</c> inside <c>uav:nodes</c>) and the conformance
    /// claim (<c>uav:profile</c>). Each is defined once here so no project
    /// spells a revision, a profile name, a security policy name or an opaque
    /// bound as a literal of its own.
    /// </remarks>
    public static class WotBindingConformance
    {
        /// <summary>
        /// The vocabulary namespace, which never changes across revisions
        /// (WoT Binding Section 4).
        /// </summary>
        public const string VocabularyNamespace = WotVocabulary.VocabularyNamespace;

        /// <summary>
        /// The OPC UA namespace URI (namespace 0), which is what a compact
        /// model name bound to the <c>ua</c> prefix resolves to.
        /// </summary>
        public const string OpcUaNamespace = WotVocabulary.OpcUaNamespace;

        /// <summary>
        /// The vocabulary revision this library implements
        /// (WoT Binding Section 4.1).
        /// </summary>
        public const string CurrentRevision = "1.1";

        /// <summary>
        /// The immutable versioned base IRI the artifacts of
        /// <see cref="CurrentRevision"/> are published at
        /// (WoT Binding Section 4.1).
        /// </summary>
        public const string CurrentRevisionArtifactBase =
            "http://opcfoundation.org/UA/WoT-Binding/v" + CurrentRevision + "/";

        /// <summary>
        /// The <c>uav:bindingVersion</c> term (WoT Binding Section 4.1).
        /// </summary>
        public const string BindingVersionTerm = "uav:bindingVersion";

        /// <summary>
        /// The <c>uav:profile</c> term (WoT Binding Sections 4.1 and 11).
        /// </summary>
        public const string ProfileTerm = "uav:profile";

        /// <summary>
        /// The <c>uav:minimumSecurity</c> term (WoT Binding Section 5.7.1).
        /// </summary>
        public const string MinimumSecurityTerm = "uav:minimumSecurity";

        /// <summary>
        /// The <c>uav:securityMode</c> term (WoT Binding Sections 5.7 and 5.7.1).
        /// </summary>
        public const string SecurityModeTerm = "uav:securityMode";

        /// <summary>
        /// The <c>uav:securityPolicy</c> term (WoT Binding Sections 5.7 and 5.7.1).
        /// </summary>
        public const string SecurityPolicyTerm = "uav:securityPolicy";

        /// <summary>
        /// The WoT security scheme that leaves endpoint selection to the client
        /// and is therefore the only scheme a security floor may constrain
        /// (WoT Binding Section 5.7.1).
        /// </summary>
        public const string AutoSecurityScheme = "auto";

        /// <summary>
        /// The maximum size, in octets, of an opaque object serialized as
        /// canonical UTF-8 JSON (WoT Binding Section 6.6).
        /// </summary>
        public const int OpaqueMaxOctets = 65536;

        /// <summary>
        /// The maximum nesting depth of an opaque object
        /// (WoT Binding Section 6.6).
        /// </summary>
        public const int OpaqueMaxDepth = 32;

        /// <summary>
        /// The maximum number of top-level keys of an opaque object
        /// (WoT Binding Section 6.6).
        /// </summary>
        public const int OpaqueMaxTopLevelKeys = 256;

        /// <summary>
        /// The revisions of the vocabulary this library implements. A document
        /// declaring another revision is still processed - Section 4.1 forbids
        /// rejecting it for that reason alone - but strict conformance reports
        /// it, because a term added by a revision this library does not
        /// implement is indistinguishable from a member the author invented.
        /// </summary>
        public static ArrayOf<string> SupportedRevisions { get; } = new[] { CurrentRevision };

        /// <summary>
        /// The native projection grammars (<c>profileVersion</c> inside
        /// <c>uav:nodes</c>) this library parses (WoT Binding Sections 4.1 and
        /// 10.1). An unsupported grammar is rejected rather than parsed by
        /// guesswork.
        /// </summary>
        public static ArrayOf<string> SupportedProjectionGrammars { get; } =
            new[] { WotVocabulary.ProfileVersion };

        /// <summary>
        /// The closed conformance-unit and profile names of WoT Binding
        /// Section 11. A claim a test suite cannot name is not a claim, so the
        /// set is closed rather than open.
        /// </summary>
        public static ArrayOf<string> ConformanceNames { get; } =
        [
            "WoT-ProtocolBinding",
            "WoT-NativeMapping",
            "WoT-StructuredFallback",
            "WoT-JsonResidue",
            "WoT-NodeSetPreservation",
            "WoT-ExactRoundtrip",
            "WoT-EventMapping",
            "WoT-ConditionMapping",
            "WoT-ModelVocabulary",
            "WoT-DataTypeDefinition",
            "WoT-ExternalResolver",
            "WoT-Projection",
            "WoT-Reader",
            "WoT-Modeller",
            "WoT-Converter",
            "WoT-ArchivalConverter"
        ];

        /// <summary>
        /// The <c>MessageSecurityMode</c> strength order of WoT Binding
        /// Section 5.7.1, weakest first.
        /// </summary>
        public static ArrayOf<string> SecurityModeOrder { get; } =
        [
            "None",
            "Sign",
            "SignAndEncrypt"
        ];

        /// <summary>
        /// The security-policy strength order of WoT Binding Section 5.7.1,
        /// weakest first. The two deprecated policies order below the
        /// recommended ones, so a floor of <c>Basic256Sha256</c> excludes them
        /// without naming them.
        /// </summary>
        public static ArrayOf<string> SecurityPolicyOrder { get; } =
        [
            "None",
            "Basic128Rsa15",
            "Basic256",
            "Basic256Sha256",
            "Aes128_Sha256_RsaOaep",
            "Aes256_Sha256_RsaPss"
        ];

        /// <summary>
        /// The opaque members of WoT Binding Section 6.6, whose contents stay
        /// opaque and whose shape does not.
        /// </summary>
        public static ArrayOf<string> OpaqueMembers { get; } =
        [
            "uav:metadata",
            "uav:propertyConfiguration",
            "uav:actionConfiguration",
            "uav:eventConfiguration"
        ];

        /// <summary>
        /// Determines whether a value is a syntactically well-formed vocabulary
        /// revision, that is <c>&lt;major&gt;.&lt;minor&gt;</c>
        /// (WoT Binding Section 4.1).
        /// </summary>
        /// <param name="revision">The declared revision.</param>
        /// <returns><c>true</c> when the value has the required form.</returns>
        public static bool IsWellFormedRevision(string? revision)
        {
            if (string.IsNullOrEmpty(revision))
            {
                return false;
            }
            int separator = revision!.IndexOf('.', 0);
            if (separator <= 0 || separator == revision.Length - 1)
            {
                return false;
            }
            for (int ii = 0; ii < revision.Length; ii++)
            {
                if (ii == separator)
                {
                    continue;
                }
                if (revision[ii] is < '0' or > '9')
                {
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// Determines whether a revision is one this library implements.
        /// </summary>
        /// <param name="revision">The declared revision.</param>
        /// <returns><c>true</c> when the revision is implemented here.</returns>
        public static bool IsSupportedRevision(string? revision)
        {
            foreach (string supported in SupportedRevisions)
            {
                if (string.Equals(supported, revision, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Determines whether a name is a conformance unit or profile of
        /// WoT Binding Section 11.
        /// </summary>
        /// <param name="name">The claimed name.</param>
        /// <returns><c>true</c> when Section 11 defines the name.</returns>
        public static bool IsConformanceName(string? name)
        {
            return name is not null && s_conformanceNames.Contains(name);
        }

        /// <summary>
        /// Determines whether a set of claims covers a required conformance
        /// unit or profile.
        /// </summary>
        /// <remarks>
        /// WoT Binding Section 11 states that claiming a profile claims every
        /// unit that profile names, and the recommended profiles nest, so
        /// <c>WoT-ArchivalConverter</c> covers <c>WoT-Converter</c>, which
        /// covers <c>WoT-Modeller</c>, and so on. Expanding a claim here is
        /// what keeps that nesting stated once.
        /// </remarks>
        /// <param name="claims">The names a document claims.</param>
        /// <param name="required">The name that has to be covered.</param>
        /// <returns><c>true</c> when the claims cover the required name.</returns>
        public static bool ClaimsSatisfy(IReadOnlyList<string> claims, string required)
        {
            if (claims is null)
            {
                throw new ArgumentNullException(nameof(claims));
            }
            for (int ii = 0; ii < claims.Count; ii++)
            {
                if (string.Equals(claims[ii], required, StringComparison.Ordinal) ||
                    (s_profileUnits.TryGetValue(claims[ii], out HashSet<string>? units) &&
                        units.Contains(required)))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Expands a conformance claim into the units it names, which is the
        /// claim itself for a unit and every named unit for a profile
        /// (WoT Binding Section 11).
        /// </summary>
        /// <param name="name">The claimed unit or profile name.</param>
        /// <returns>The units the claim covers, in Section 11 order.</returns>
        public static ArrayOf<string> Expand(string? name)
        {
            if (name is null || !IsConformanceName(name))
            {
                return ArrayOf<string>.Empty;
            }
            if (!s_profileUnits.TryGetValue(name, out HashSet<string>? units))
            {
                return new[] { name };
            }
            var expanded = new List<string>(units.Count + 1) { name };
            foreach (string unit in ConformanceNames)
            {
                if (units.Contains(unit) &&
                    !string.Equals(unit, name, StringComparison.Ordinal))
                {
                    expanded.Add(unit);
                }
            }
            return expanded.ToArray();
        }

        /// <summary>
        /// Determines whether a value is one of the three
        /// <c>MessageSecurityMode</c> names this Binding uses.
        /// </summary>
        /// <param name="securityMode">The mode name.</param>
        /// <returns><c>true</c> when the name is defined.</returns>
        public static bool IsSecurityMode(string? securityMode)
        {
            return TryGetSecurityModeRank(securityMode, out _);
        }

        /// <summary>
        /// Determines whether a value is one of the security-policy names
        /// WoT Binding Section 5.7 lists.
        /// </summary>
        /// <param name="securityPolicy">The policy name.</param>
        /// <returns><c>true</c> when the name is defined.</returns>
        public static bool IsSecurityPolicy(string? securityPolicy)
        {
            return TryGetSecurityPolicyRank(securityPolicy, out _);
        }

        /// <summary>
        /// Resolves the strength rank of a <c>MessageSecurityMode</c>, where a
        /// larger rank is stronger (WoT Binding Section 5.7.1).
        /// </summary>
        /// <param name="securityMode">The mode name.</param>
        /// <param name="rank">The resolved rank.</param>
        /// <returns><c>true</c> when the name is one this Binding orders.</returns>
        public static bool TryGetSecurityModeRank(string? securityMode, out int rank)
        {
            return TryGetRank(SecurityModeOrder, securityMode, out rank);
        }

        /// <summary>
        /// Resolves the strength rank of a security policy, where a larger rank
        /// is stronger (WoT Binding Section 5.7.1). A policy this Binding does
        /// not name has no rank and ranks below every policy it names.
        /// </summary>
        /// <param name="securityPolicy">The policy name.</param>
        /// <param name="rank">The resolved rank.</param>
        /// <returns><c>true</c> when the name is one this Binding orders.</returns>
        public static bool TryGetSecurityPolicyRank(string? securityPolicy, out int rank)
        {
            return TryGetRank(SecurityPolicyOrder, securityPolicy, out rank);
        }

        /// <summary>
        /// Determines whether a member name is a term of this Binding revision.
        /// </summary>
        /// <remarks>
        /// Strict conformance uses this to tell a term added by a later
        /// revision, or misspelled by an author, from a term this library
        /// knows. Permissive processing never consults it: an unknown
        /// <c>uav:</c> member is carried unchanged as residue, which is what
        /// Sections 9.4 and 10.2 require.
        /// </remarks>
        /// <param name="term">The member name, including the <c>uav:</c> prefix.</param>
        /// <returns><c>true</c> when this revision defines the term.</returns>
        public static bool IsKnownTerm(string? term)
        {
            return term is not null && s_terms.Contains(term);
        }

        private static bool TryGetRank(ArrayOf<string> order, string? value, out int rank)
        {
            if (value is not null)
            {
                for (int ii = 0; ii < order.Count; ii++)
                {
                    if (string.Equals(order[ii], value, StringComparison.Ordinal))
                    {
                        rank = ii;
                        return true;
                    }
                }
            }
            rank = -1;
            return false;
        }

        private static HashSet<string> BuildSet(ArrayOf<string> values)
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (string value in values)
            {
                set.Add(value);
            }
            return set;
        }

        private static readonly HashSet<string> s_conformanceNames = BuildSet(ConformanceNames);

        /// <summary>
        /// The units each recommended profile of WoT Binding Section 11 names,
        /// including the units its nested profile names.
        /// </summary>
        private static readonly Dictionary<string, HashSet<string>> s_profileUnits = BuildProfileUnits();

        private static Dictionary<string, HashSet<string>> BuildProfileUnits()
        {
            var reader = new HashSet<string>(StringComparer.Ordinal)
            {
                "WoT-ProtocolBinding",
                "WoT-NativeMapping"
            };
            var modeller = new HashSet<string>(reader, StringComparer.Ordinal)
            {
                "WoT-Reader",
                "WoT-ModelVocabulary",
                "WoT-DataTypeDefinition",
                "WoT-EventMapping",
                "WoT-Projection"
            };
            var converter = new HashSet<string>(modeller, StringComparer.Ordinal)
            {
                "WoT-Modeller",
                "WoT-StructuredFallback",
                "WoT-JsonResidue",
                "WoT-ExactRoundtrip"
            };
            var archival = new HashSet<string>(converter, StringComparer.Ordinal)
            {
                "WoT-Converter",
                "WoT-NodeSetPreservation"
            };
            return new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
            {
                ["WoT-Reader"] = reader,
                ["WoT-Modeller"] = modeller,
                ["WoT-Converter"] = converter,
                ["WoT-ArchivalConverter"] = archival
            };
        }

        /// <summary>
        /// Every <c>uav:</c> term this revision defines, taken from the
        /// per-term domain and range table of WoT Binding Section 7 together
        /// with the class terms Section 6.11 and Sections 10.1 / 12 define.
        /// </summary>
        private static readonly HashSet<string> s_terms =
            new(StringComparer.Ordinal)
            {
                // Section 4.1 - revision and conformance claims.
                BindingVersionTerm,
                ProfileTerm,
                // Section 5 - identity, names, paths and type annotations.
                "uav:id",
                "uav:browsePath",
                "uav:browsePathAnchor",
                "uav:browseName",
                "uav:object",
                "uav:objectType",
                "uav:variable",
                "uav:variableType",
                "uav:method",
                "uav:eventType",
                "uav:hasComponent",
                "uav:componentOf",
                "uav:mapToNodeId",
                "uav:mapToType",
                "uav:mapToTypeName",
                "uav:mapByFieldPath",
                // Section 5.7 - security schemes.
                "uav:channelsec",
                "uav:authentication",
                SecurityModeTerm,
                SecurityPolicyTerm,
                MinimumSecurityTerm,
                "uav:userIdentityToken",
                "uav:issueToken",
                // Section 6.1 - composition, events and select clauses.
                "uav:isComposite",
                "uav:contains",
                "uav:containedIn",
                "uav:isEvent",
                WotNodeSetConverter.SeverityTerm,
                WotEventSelectClauses.Term,
                WotEventSelectClauses.TypeDefinitionIdTerm,
                // Section 6.2 - links and references.
                "uav:refName",
                "uav:refId",
                WotNodeSetConverter.InverseNameTerm,
                WotNodeSetConverter.SymmetricTerm,
                // Type annotations this implementation emits for a projected
                // ReferenceType or DataType root. Section 5.2 names an
                // annotation per NodeClass and the published context declares
                // the five instance and type ones; these two complete the set
                // for the two NodeClasses a NodeSet conversion also projects,
                // and are recognized here so a document this library produces
                // passes its own strict conformance.
                "uav:referenceType",
                "uav:dataType",
                // Sections 6.4 and 6.4.1 - scaling, units and ranges.
                "uav:scaleFactor",
                "uav:decimalPlaces",
                WotNodeSetConverter.UnitPropertyTerm,
                "uav:engineeringUnits",
                "uav:instrumentRange",
                // Sections 6.5 to 6.10 - grouping, opaque members, inheritance,
                // generic mapping and device identity.
                "uav:metadata",
                "uav:semanticId",
                "uav:propertyConfiguration",
                "uav:actionConfiguration",
                "uav:eventConfiguration",
                "uav:includeInherited",
                "uav:additionalProperties",
                "uav:externalSchema",
                "uav:modellingRule",
                WotNodeSetConverter.ValueRankTerm,
                WotNodeSetConverter.ArrayDimensionsTerm,
                // Section 6.11 - DataType definitions.
                "uav:dataTypeDefinitions",
                "uav:dataTypeDefinition",
                "uav:dataTypeName",
                "uav:dataTypeId",
                "uav:dataTypeSubtypeOf",
                "uav:isAbstract",
                "uav:StructureDefinition",
                "uav:EnumDefinition",
                "uav:SimpleDataType",
                "uav:StructureField",
                "uav:EnumField",
                "uav:structureType",
                "uav:fields",
                "uav:fieldOrder",
                "uav:fieldName",
                "uav:fieldDataTypeDefinition",
                "uav:fieldDataTypeName",
                "uav:fieldDataTypeId",
                "uav:maxStringLength",
                "uav:isOptional",
                "uav:allowSubtypes",
                "uav:enumFields",
                "uav:enumName",
                "uav:enumValue",
                "uav:isOptionSet",
                "uav:hasDefaultEncoding",
                "uav:defaultEncodingId",
                "uav:binaryEncodingId",
                "uav:xmlEncodingId",
                "uav:jsonEncodingId",
                // Sections 10.1 and 10.3 - native projection and envelope.
                "uav:NodeModel",
                "uav:nodes",
                "uav:nodeSet",
                "uav:sourceDigest",
                // Section 12 - projections.
                "uav:projection",
                "uav:scenario",
                "uav:projects",
                "uav:sourceName",
                "uav:routing",
                "uav:namePrefix",
                "uav:selectAll",
                "uav:select",
                "uav:affordanceKind",
                "uav:resolvedFrom",
                // Section 13 - Alarms and Conditions.
                "uav:conditionType",
                "uav:conditionTypeId",
                "uav:conditionAction",
                "uav:actsOn"
            };
    }
}
