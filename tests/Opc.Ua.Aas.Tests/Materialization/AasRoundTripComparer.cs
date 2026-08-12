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

using Opc.Ua.Aas.V3;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Opc.Ua.Aas.Tests.Materialization
{
    /// <summary>
    /// Compares AAS environments with the equivalence relation from clause 6.4.
    /// </summary>
    internal static class AasRoundTripComparer
    {
        public static AasRoundTripComparison Compare(AasEnvironment expected, AasEnvironment actual)
        {
            var differences = new List<string>();
            CompareEnvironment(expected, actual, "$", differences);
            return new AasRoundTripComparison(differences);
        }

        private static void CompareEnvironment(
            AasEnvironment expected,
            AasEnvironment actual,
            string path,
            List<string> differences)
        {
            CompareOptionalArray(expected.Submodels, actual.Submodels, path + ".submodels", CompareSubmodel, differences);
            CompareOptionalArray(
                expected.AssetAdministrationShells,
                actual.AssetAdministrationShells,
                path + ".assetAdministrationShells",
                CompareShell,
                differences);
            CompareOptionalArray(
                expected.ConceptDescriptions,
                actual.ConceptDescriptions,
                path + ".conceptDescriptions",
                CompareConcept,
                differences);
        }

        private static void CompareShell(AasShell expected, AasShell actual, string path, List<string> differences)
        {
            CompareReferable(expected, actual, path, differences);
            CompareString(expected.Id, actual.Id, path + ".id", differences);
            CompareOptionalString(
                expected.AssetInformation.GlobalAssetId,
                actual.AssetInformation.GlobalAssetId,
                path + ".assetInformation.globalAssetId",
                differences);
            CompareOptionalArray(
                expected.SubmodelReferences,
                actual.SubmodelReferences,
                path + ".submodelReferences",
                CompareReference,
                differences);
        }

        private static void CompareSubmodel(
            AasSubmodel expected,
            AasSubmodel actual,
            string path,
            List<string> differences)
        {
            CompareReferable(expected, actual, path, differences);
            CompareString(expected.Id, actual.Id, path + ".id", differences);
            CompareOptionalArray(expected.Qualifiers, actual.Qualifiers, path + ".qualifiers", CompareQualifier, differences);
            CompareOptionalArray(
                expected.SubmodelElements,
                actual.SubmodelElements,
                path + ".submodelElements",
                CompareElement,
                differences);
        }

        private static void CompareConcept(
            AasConceptDescription expected,
            AasConceptDescription actual,
            string path,
            List<string> differences)
        {
            CompareReferable(expected, actual, path, differences);
            CompareString(expected.Id, actual.Id, path + ".id", differences);
        }

        private static void CompareElement(
            AasSubmodelElement expected,
            AasSubmodelElement actual,
            string path,
            List<string> differences)
        {
            if (expected.GetType() != actual.GetType())
            {
                differences.Add(path + " modelType differs.");
                return;
            }

            CompareReferable(expected, actual, path, differences);
            CompareOptionalArray(expected.Qualifiers, actual.Qualifiers, path + ".qualifiers", CompareQualifier, differences);
            CompareReferenceOptional(expected.SemanticId, actual.SemanticId, path + ".semanticId", differences);
            switch (expected)
            {
                case AasProperty left:
                    var property = (AasProperty)actual;
                    CompareValueType(left.ValueType, property.ValueType, path + ".valueType", differences);
                    CompareValue(left.Value, property.Value, left.ValueType, property.ValueType,
                        path + ".value", differences);
                    CompareReferenceOptional(left.ValueId, property.ValueId, path + ".valueId", differences);
                    break;
                case AasRange left:
                    var range = (AasRange)actual;
                    CompareValueType(left.ValueType, range.ValueType, path + ".valueType", differences);
                    CompareValue(left.Min, range.Min, left.ValueType, range.ValueType,
                        path + ".min", differences);
                    CompareValue(left.Max, range.Max, left.ValueType, range.ValueType,
                        path + ".max", differences);
                    break;
                case AasBlob left:
                    var blob = (AasBlob)actual;
                    CompareString(left.ContentType, blob.ContentType, path + ".contentType", differences);
                    CompareOptionalByteString(left.Value, blob.Value, path + ".value", differences);
                    break;
                case AasFile left:
                    var file = (AasFile)actual;
                    CompareString(left.ContentType, file.ContentType, path + ".contentType", differences);
                    CompareOptionalString(left.Value, file.Value, path + ".value", differences);
                    break;
                case AasReferenceElement left:
                    var referenceElement = (AasReferenceElement)actual;
                    CompareReferenceOptional(left.Value, referenceElement.Value, path + ".value", differences);
                    break;
                case AasBasicEventElement left:
                    CompareBasicEvent(left, (AasBasicEventElement)actual, path, differences);
                    break;
                case AasMultiLanguageProperty left:
                    var multiLanguage = (AasMultiLanguageProperty)actual;
                    CompareOptionalArray(left.Value, multiLanguage.Value, path + ".value", CompareLangString, differences);
                    break;
                case AasSubmodelElementCollection left:
                    var collection = (AasSubmodelElementCollection)actual;
                    CompareOptionalArray(left.Value, collection.Value, path + ".value", CompareElement, differences);
                    break;
                case AasSubmodelElementList left:
                    CompareList(left, (AasSubmodelElementList)actual, path, differences);
                    break;
                case AasOperation left:
                    var operation = (AasOperation)actual;
                    CompareOptionalArray(
                        left.InputVariables,
                        operation.InputVariables,
                        path + ".inputVariables",
                        CompareElement,
                        differences);
                    CompareOptionalArray(
                        left.OutputVariables,
                        operation.OutputVariables,
                        path + ".outputVariables",
                        CompareElement,
                        differences);
                    CompareOptionalArray(
                        left.InoutputVariables,
                        operation.InoutputVariables,
                        path + ".inoutputVariables",
                        CompareElement,
                        differences);
                    break;
                case AasEntity left:
                    var entity = (AasEntity)actual;
                    if (left.EntityType != entity.EntityType)
                    {
                        differences.Add(path + ".entityType differs.");
                    }
                    CompareOptionalString(left.GlobalAssetId, entity.GlobalAssetId,
                        path + ".globalAssetId", differences);
                    CompareOptionalArray(left.Statements, entity.Statements, path + ".statements", CompareElement, differences);
                    break;
                case AasAnnotatedRelationshipElement left:
                    var annotatedRelationship = (AasAnnotatedRelationshipElement)actual;
                    CompareReference(left.First, annotatedRelationship.First, path + ".first", differences);
                    CompareReference(left.Second, annotatedRelationship.Second, path + ".second", differences);
                    CompareOptionalArray(
                        left.Annotations,
                        annotatedRelationship.Annotations,
                        path + ".annotations",
                        CompareElement,
                        differences);
                    break;
                case AasRelationshipElement left:
                    var relationshipElement = (AasRelationshipElement)actual;
                    CompareReference(left.First, relationshipElement.First, path + ".first", differences);
                    CompareReference(left.Second, relationshipElement.Second, path + ".second", differences);
                    break;
            }
        }

        private static void CompareList(
            AasSubmodelElementList expected,
            AasSubmodelElementList actual,
            string path,
            List<string> differences)
        {
            if (expected.EffectiveOrderRelevant != actual.EffectiveOrderRelevant)
            {
                differences.Add(path + ".orderRelevant differs.");
            }

            if (expected.EffectiveOrderRelevant)
            {
                CompareOptionalArray(expected.Value, actual.Value, path + ".value", CompareElement, differences);
                return;
            }

            if (!SamePresence(expected.Value.IsPresent, actual.Value.IsPresent, path + ".value", differences) ||
                !expected.Value.IsPresent)
            {
                return;
            }

            var unmatched = actual.Value.Value.Span.ToArray().ToList();
            foreach (AasSubmodelElement member in expected.Value.Value.Span)
            {
                int match = unmatched.FindIndex(candidate => CompareElementOnly(member, candidate).IsEquivalent);
                if (match < 0)
                {
                    differences.Add(path + ".value unordered member is missing.");
                }
                else
                {
                    unmatched.RemoveAt(match);
                }
            }

            if (unmatched.Count > 0)
            {
                differences.Add(path + ".value has unexpected unordered members.");
            }
        }

        private static void CompareReferable(
            AasReferable expected,
            AasReferable actual,
            string path,
            List<string> differences)
        {
            CompareOptionalString(expected.IdShort, actual.IdShort, path + ".idShort", differences);
            CompareOptionalString(expected.Category, actual.Category, path + ".category", differences);
            CompareOptionalArray(expected.DisplayName, actual.DisplayName, path + ".displayName", CompareLangString, differences);
            CompareOptionalArray(expected.Description, actual.Description, path + ".description", CompareLangString, differences);
        }

        private static void CompareValue(
            AasOptional<Variant> expected,
            AasOptional<Variant> actual,
            AASDataTypeDefXsdDataType expectedValueType,
            AASDataTypeDefXsdDataType actualValueType,
            string path,
            List<string> differences)
        {
            if (!SamePresence(expected.IsPresent, actual.IsPresent, path, differences) || !expected.IsPresent)
            {
                return;
            }

            // Each side is canonicalized under its own declared type. Reusing
            // the expected type for both would hide a lost or altered
            // valueType, which is a field the round trip has to preserve.
            string? left = Lexical(expected.Value, expectedValueType);
            string? right = Lexical(actual.Value, actualValueType);
            if (left is null || right is null)
            {
                // A value that cannot be canonicalized has no place in the
                // value space, so it cannot be equivalent to anything -
                // including another value that also failed.
                differences.Add(path + " could not be canonicalized on at least one side.");
                return;
            }

            if (!AasValueSpaceComparer.AreEquivalent(left, right, expectedValueType))
            {
                differences.Add(path + " differs in the xs value space.");
            }
        }

        private static void CompareValueType(
            AASDataTypeDefXsdDataType expected,
            AASDataTypeDefXsdDataType actual,
            string path,
            List<string> differences)
        {
            if (expected != actual)
            {
                differences.Add(path + " differs.");
            }
        }

        private static void CompareOptionalByteString(
            AasOptional<ByteString> expected,
            AasOptional<ByteString> actual,
            string path,
            List<string> differences)
        {
            if (!SamePresence(expected.IsPresent, actual.IsPresent, path, differences) || !expected.IsPresent)
            {
                return;
            }

            if (!expected.Value.Span.SequenceEqual(actual.Value.Span))
            {
                differences.Add(path + " differs.");
            }
        }

        private static void CompareBasicEvent(
            AasBasicEventElement expected,
            AasBasicEventElement actual,
            string path,
            List<string> differences)
        {
            CompareReference(expected.Observed, actual.Observed, path + ".observed", differences);
            if (expected.Direction != actual.Direction)
            {
                differences.Add(path + ".direction differs.");
            }
            if (expected.State != actual.State)
            {
                differences.Add(path + ".state differs.");
            }
            CompareOptionalString(expected.MessageTopic, actual.MessageTopic, path + ".messageTopic", differences);
            CompareReferenceOptional(
                expected.MessageBroker, actual.MessageBroker, path + ".messageBroker", differences);

            // The timing fields are DateTime and DurationString on the wire, so
            // they are compared in their own value spaces rather than as text.
            CompareValue(expected.LastUpdate, actual.LastUpdate,
                AASDataTypeDefXsdDataType.DateTime, AASDataTypeDefXsdDataType.DateTime,
                path + ".lastUpdate", differences);
            CompareValue(expected.MinInterval, actual.MinInterval,
                AASDataTypeDefXsdDataType.Duration, AASDataTypeDefXsdDataType.Duration,
                path + ".minInterval", differences);
            CompareValue(expected.MaxInterval, actual.MaxInterval,
                AASDataTypeDefXsdDataType.Duration, AASDataTypeDefXsdDataType.Duration,
                path + ".maxInterval", differences);
        }

        private static string? Lexical(in Variant value, AASDataTypeDefXsdDataType valueType)
        {
            if (value.TryGetValue(out string? text))
            {
                return text;
            }

            return AasLexicalCanonicalizer.TryCanonicalize(value, valueType, out string? lexical, out _)
                ? lexical
                : null;
        }

        private static void CompareOptionalString(
            AasOptional<string> expected,
            AasOptional<string> actual,
            string path,
            List<string> differences)
        {
            if (SamePresence(expected.IsPresent, actual.IsPresent, path, differences) &&
                expected.IsPresent)
            {
                CompareString(expected.Value, actual.Value, path, differences);
            }
        }

        private static void CompareReferenceOptional(
            AasOptional<AASReferenceDataType> expected,
            AasOptional<AASReferenceDataType> actual,
            string path,
            List<string> differences)
        {
            if (SamePresence(expected.IsPresent, actual.IsPresent, path, differences) &&
                expected.IsPresent)
            {
                CompareReference(expected.Value, actual.Value, path, differences);
            }
        }

        private static void CompareOptionalArray<T>(
            AasOptional<ArrayOf<T>> expected,
            AasOptional<ArrayOf<T>> actual,
            string path,
            Action<T, T, string, List<string>> compare,
            List<string> differences)
            where T : class
        {
            if (!SamePresence(expected.IsPresent, actual.IsPresent, path, differences) || !expected.IsPresent)
            {
                return;
            }

            if (expected.Value.Count != actual.Value.Count)
            {
                differences.Add(path + " count differs.");
                return;
            }

            for (int ii = 0; ii < expected.Value.Count; ii++)
            {
                compare(
                    expected.Value[ii],
                    actual.Value[ii],
                    path + "[" + ii.ToString(CultureInfo.InvariantCulture) + "]",
                    differences);
            }
        }

        private static void CompareReference(
            AASReferenceDataType expected,
            AASReferenceDataType actual,
            string path,
            List<string> differences)
        {
            if (expected.Type != actual.Type)
            {
                differences.Add(path + ".type differs.");
            }

            if (expected.Keys.Count != actual.Keys.Count)
            {
                differences.Add(path + ".keys count differs.");
                return;
            }

            for (int ii = 0; ii < expected.Keys.Count; ii++)
            {
                string key = path + ".keys[" + ii.ToString(CultureInfo.InvariantCulture) + "]";
                if (expected.Keys[ii].Type != actual.Keys[ii].Type)
                {
                    differences.Add(key + ".type differs.");
                }

                CompareString(
                    expected.Keys[ii].Value,
                    actual.Keys[ii].Value,
                    key + ".value",
                    differences);
            }
        }

        private static void CompareQualifier(
            AASQualifierDataType expected,
            AASQualifierDataType actual,
            string path,
            List<string> differences)
        {
            CompareString(expected.Type, actual.Type, path + ".type", differences);
            if (!AasValueSpaceComparer.AreEquivalent(expected.Value, actual.Value, expected.ValueType))
            {
                differences.Add(path + ".value differs in the xs value space.");
            }
        }

        private static void CompareLangString(
            AASLangStringDataType expected,
            AASLangStringDataType actual,
            string path,
            List<string> differences)
        {
            CompareString(expected.Language, actual.Language, path + ".language", differences);
            CompareString(expected.Text, actual.Text, path + ".text", differences);
        }

        private static void CompareString(string? expected, string? actual, string path, List<string> differences)
        {
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                differences.Add(path + " differs.");
            }
        }

        private static bool SamePresence(bool expected, bool actual, string path, List<string> differences)
        {
            if (expected == actual)
            {
                return true;
            }

            differences.Add(path + " presence differs.");
            return false;
        }

        private static AasRoundTripComparison CompareElementOnly(AasSubmodelElement expected, AasSubmodelElement actual)
        {
            var differences = new List<string>();
            CompareElement(expected, actual, "$", differences);
            return new AasRoundTripComparison(differences);
        }
    }

    internal sealed class AasRoundTripComparison
    {
        public AasRoundTripComparison(IReadOnlyList<string> differences)
        {
            Differences = differences;
        }

        public IReadOnlyList<string> Differences { get; }

        public bool IsEquivalent => Differences.Count == 0;
    }
}
