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
using System.Collections.Immutable;

namespace Opc.Ua.WotCon.Server.Materialization
{
    /// <summary>
    /// One non-leaf segment of a validated <c>uav:mapByFieldPath</c> path: the
    /// field name to descend through and the encodeable type of the nested
    /// structure it holds.
    /// </summary>
    internal sealed class WotFieldPathSegment
    {
        public WotFieldPathSegment(string name, IEncodeableType nestedType)
        {
            Name = name;
            NestedType = nestedType;
        }

        public string Name { get; }

        public IEncodeableType NestedType { get; }
    }

    /// <summary>
    /// A validated, pre-resolved walk from a structured target's root down to
    /// one leaf field, built once during activation so repeated reads/writes
    /// never re-walk <see cref="StructureDefinition"/> metadata.
    /// </summary>
    internal sealed class WotFieldPathPlan
    {
        public WotFieldPathPlan(ImmutableArray<WotFieldPathSegment> intermediateSegments, string leafFieldName)
        {
            IntermediateSegments = intermediateSegments;
            LeafFieldName = leafFieldName;
        }

        public ImmutableArray<WotFieldPathSegment> IntermediateSegments { get; }

        public string LeafFieldName { get; }
    }

    /// <summary>
    /// Validates <c>uav:mapByFieldPath</c> paths against
    /// <see cref="StructureDefinition"/> metadata and navigates
    /// <see cref="IStructure"/> instances while composing (read direction) or
    /// extracting (write direction) a structured target value. No reflection
    /// and no public <see cref="object"/> API are used: nested structures are
    /// read and written exclusively through <see cref="IStructure"/> and
    /// <see cref="Variant.TryGetValue(out ExtensionObject)"/>.
    /// </summary>
    internal static class WotStructuredFieldNavigator
    {
        /// <summary>
        /// Validates a slash-separated field path against the root type's
        /// structure definition tree and returns the pre-resolved plan.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// The path is empty, contains an empty segment, references an unknown
        /// field, traverses an array-valued intermediate field, or traverses an
        /// intermediate field whose DataType is not a structure type.
        /// </exception>
        /// <exception cref="InvalidOperationException"></exception>
        public static WotFieldPathPlan BuildPlan(
            IEncodeableFactory factory,
            NamespaceTable namespaceUris,
            IEncodeableType rootType,
            string fieldPath,
            NodeId targetNodeId)
        {
            if (string.IsNullOrWhiteSpace(fieldPath))
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "'uav:mapByFieldPath' for target '{0}' must not be empty.",
                    targetNodeId);
            }

            string[] segments = fieldPath.Split('/');
            ImmutableArray<WotFieldPathSegment>.Builder intermediate = ImmutableArray.CreateBuilder<WotFieldPathSegment>();
            IEncodeableType currentType = rootType;

            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];
                if (string.IsNullOrEmpty(segment))
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "'uav:mapByFieldPath' value '{0}' for target '{1}' contains an empty segment.",
                        fieldPath,
                        targetNodeId);
                }

                StructureDefinition definition = GetStructureDefinition(
                    currentType, namespaceUris, fieldPath, targetNodeId);
                StructureField? field = FindField(definition, segment) ??
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "'uav:mapByFieldPath' value '{0}' for target '{1}' references unknown field '{2}'.",
                        fieldPath,
                        targetNodeId,
                        segment);

                bool isLast = i == segments.Length - 1;
                if (isLast)
                {
                    return new WotFieldPathPlan(intermediate.ToImmutable(), segment);
                }

                if (field.ValueRank != ValueRanks.Scalar)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "'uav:mapByFieldPath' value '{0}' for target '{1}' traverses array-valued field " +
                        "'{2}' (ValueRank {3}); only scalar structures can be traversed.",
                        fieldPath,
                        targetNodeId,
                        segment,
                        field.ValueRank);
                }

                var nestedTypeId = NodeId.ToExpandedNodeId(field.DataType, namespaceUris);
                if (!factory.TryGetEncodeableType(nestedTypeId, out IEncodeableType? nestedType) ||
                    nestedType.CreateInstance() is not IStructure)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "'uav:mapByFieldPath' value '{0}' for target '{1}' traverses field '{2}' whose " +
                        "DataType '{3}' is not a registered structure type.",
                        fieldPath,
                        targetNodeId,
                        segment,
                        field.DataType);
                }

                intermediate.Add(new WotFieldPathSegment(segment, nestedType));
                currentType = nestedType;
            }

            // Unreachable: segments.Length > 0 is guaranteed by the empty-path
            // check above, so the loop always returns via the isLast branch.
            throw new InvalidOperationException("Field path resolution did not reach a leaf segment.");
        }

        /// <summary>
        /// Navigates from <paramref name="root"/> to the parent of the leaf
        /// field, creating any missing nested structure instance along the
        /// way. Used when composing a fresh structured value for a read.
        /// </summary>
        /// <exception cref="ServiceResultException"></exception>
        public static IStructure CreateOrGetChild(
            IStructure root, ImmutableArray<WotFieldPathSegment> intermediateSegments)
        {
            IStructure current = root;
            foreach (WotFieldPathSegment segment in intermediateSegments)
            {
                Variant existing = current[segment.Name];
                if (existing.TryGetValue(out ExtensionObject extensionObject) &&
                    extensionObject.TryGetValue(out IEncodeable? existingEncodeable) &&
                    existingEncodeable is IStructure existingChild)
                {
                    current = existingChild;
                    continue;
                }

                IEncodeable createdChild = segment.NestedType.CreateInstance();
                if (createdChild is not IStructure child)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadConfigurationError,
                        "Field '{0}' could not be instantiated as a structure.",
                        segment.Name);
                }
                current[segment.Name] = new Variant(new ExtensionObject(createdChild));
                current = child;
            }
            return current;
        }

        /// <summary>
        /// Navigates from <paramref name="root"/> to the parent of the leaf
        /// field without creating anything. Used when extracting a field from
        /// an incoming structured value for a write.
        /// </summary>
        /// <exception cref="ServiceResultException">
        /// An intermediate field is missing, null, or not the expected
        /// structure type.
        /// </exception>
        public static IStructure GetExistingChild(
            IStructure root,
            ImmutableArray<WotFieldPathSegment> intermediateSegments,
            NodeId targetNodeId,
            IServiceMessageContext messageContext)
        {
            IStructure current = root;
            foreach (WotFieldPathSegment segment in intermediateSegments)
            {
                Variant existing = current[segment.Name];
                if (!existing.TryGetValue(out ExtensionObject extensionObject) ||
                    !extensionObject.TryGetValue(out IEncodeable? existingEncodeable, messageContext) ||
                    existingEncodeable is not IStructure existingChild)
                {
                    throw ServiceResultException.Create(
                        StatusCodes.BadStructureMissing,
                        "Target '{0}' is missing the nested structure at field '{1}'.",
                        targetNodeId,
                        segment.Name);
                }
                current = existingChild;
            }
            return current;
        }

        private static StructureDefinition GetStructureDefinition(
            IEncodeableType type, NamespaceTable namespaceUris, string fieldPath, NodeId targetNodeId)
        {
            if (type is not IDataTypeDefinitionSource source ||
                source.GetDataTypeDefinition(namespaceUris) is not StructureDefinition definition)
            {
                throw ServiceResultException.Create(
                    StatusCodes.BadConfigurationError,
                    "'uav:mapByFieldPath' value '{0}' for target '{1}' traverses type '{2}', which does " +
                    "not expose a structure definition.",
                    fieldPath,
                    targetNodeId,
                    type.XmlName);
            }
            return definition;
        }

        private static StructureField? FindField(StructureDefinition definition, string name)
        {
            ArrayOf<StructureField> fields = definition.Fields;
            for (int i = 0; i < fields.Count; i++)
            {
                StructureField field = fields[i];
                if (string.Equals(field.Name, name, StringComparison.Ordinal))
                {
                    return field;
                }
            }
            return null;
        }
    }
}
