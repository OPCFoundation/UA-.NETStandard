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

using System.Collections.Generic;

namespace Opc.Ua.Server.Tests.CoverageNodeSet
{
    /// <summary>
    /// The expected node / attribute / reference catalogue authored in
    /// <c>Opc.Ua.CoverageTest.NodeSet2.xml</c>. The same catalogue drives the
    /// data-driven assertion battery for both the source-generation server and
    /// the runtime-import server so that any attribute or reference dropped by
    /// either pipeline fails a test.
    /// </summary>
    public static class CoverageTestCatalogue
    {
        /// <summary>
        /// The model namespace URI.
        /// </summary>
        public const string NamespaceUri = "http://opcfoundation.org/UA/CoverageTest/";

        /// <summary>
        /// The secondary (dependent) model namespace URI.
        /// </summary>
        public const string SecondaryNamespaceUri =
            "http://opcfoundation.org/UA/CoverageTest/Secondary/";

        /// <summary>
        /// An expected node: its numeric identifier in the model namespace,
        /// its BrowseName, and its NodeClass.
        /// </summary>
        public sealed record ExpectedNode(uint Id, string BrowseName, NodeClass NodeClass);

        /// <summary>
        /// An expected reference edge. <paramref name="Target"/> is either a
        /// model-namespace identifier (when <paramref name="TargetIsOwned"/>)
        /// or an absolute standard <see cref="NodeId"/> numeric identifier in
        /// namespace 0.
        /// </summary>
        public sealed record ExpectedReference(
            uint Source,
            uint ReferenceType,
            uint Target,
            bool TargetIsOwned,
            bool IsForward);

        /// <summary>
        /// Every node authored in the model namespace.
        /// </summary>
        public static IReadOnlyList<ExpectedNode> Nodes { get; } =
        [
            // Reference types.
            new(5001, "CoverageHierarchicalReference", NodeClass.ReferenceType),
            new(5002, "CoverageSymmetricReference", NodeClass.ReferenceType),
            new(5003, "CoverageAbstractReference", NodeClass.ReferenceType),
            new(5004, "CoverageConcreteReference", NodeClass.ReferenceType),

            // Data types + members + encodings.
            new(5100, "CoverageEnumeration", NodeClass.DataType),
            new(5101, "EnumValues", NodeClass.Variable),
            new(5110, "CoverageOptionSet", NodeClass.DataType),
            new(5111, "OptionSetValues", NodeClass.Variable),
            new(5120, "CoverageAbstractStructure", NodeClass.DataType),
            new(5130, "CoverageStructure", NodeClass.DataType),
            new(5131, "Default Binary", NodeClass.Object),
            new(5132, "Default XML", NodeClass.Object),
            new(5133, "Default JSON", NodeClass.Object),
            new(5140, "CoverageOptionalStructure", NodeClass.DataType),
            new(5141, "Default Binary", NodeClass.Object),
            new(5150, "CoverageUnion", NodeClass.DataType),
            new(5151, "Default Binary", NodeClass.Object),
            new(5160, "CoveragePlainStructure", NodeClass.DataType),
            new(5161, "Default Binary", NodeClass.Object),

            // Object / variable / event / interface types.
            new(5200, "CoverageAbstractObjectType", NodeClass.ObjectType),
            new(5210, "CoverageObjectType", NodeClass.ObjectType),
            new(5211, "MandatoryVariable", NodeClass.Variable),
            new(5212, "OptionalVariable", NodeClass.Variable),
            new(5213, "MandatoryPlaceholder", NodeClass.Object),
            new(5214, "OptionalPlaceholder", NodeClass.Object),
            new(5215, "ExposesArrayVariable", NodeClass.Variable),
            new(5216, "MandatoryMethod", NodeClass.Method),
            new(5217, "InputArguments", NodeClass.Variable),
            new(5218, "OutputArguments", NodeClass.Variable),
            new(5230, "CoverageEventType", NodeClass.ObjectType),
            new(5240, "CoverageInterfaceType", NodeClass.ObjectType),
            new(5300, "CoverageVariableType", NodeClass.VariableType),

            // Instances.
            new(5400, "CoverageRoot", NodeClass.Object),
            new(5402, "TypedObject", NodeClass.Object),
            new(5403, "MandatoryVariable", NodeClass.Variable),
            new(5404, "MandatoryMethod", NodeClass.Method),
            new(5405, "InputArguments", NodeClass.Variable),
            new(5406, "OutputArguments", NodeClass.Variable),
            new(5410, "EventSource", NodeClass.Object),
            new(5411, "SubNotifier", NodeClass.Object),
            new(5412, "CoverageCondition", NodeClass.Object),
            new(5420, "BooleanValue", NodeClass.Variable),
            new(5421, "Int32Value", NodeClass.Variable),
            new(5422, "StringArrayValue", NodeClass.Variable),
            new(5423, "DoubleMatrixValue", NodeClass.Variable),
            new(5424, "DateTimeValue", NodeClass.Variable),
            new(5425, "EnumerationValue", NodeClass.Variable),
            new(5426, "OrderedValue", NodeClass.Variable),
            new(5450, "AddNumbers", NodeClass.Method),
            new(5451, "LockedMethod", NodeClass.Method),
            new(5452, "InputArguments", NodeClass.Variable),
            new(5453, "OutputArguments", NodeClass.Variable),
            new(5460, "CoverageView", NodeClass.View),
            new(5470, "CoverageStateMachine", NodeClass.Object),
            new(5471, "StateA", NodeClass.Object),
            new(5472, "StateB", NodeClass.Object),
            new(5473, "TransitionAB", NodeClass.Object),

            // Deep, branching instance tree.
            new(5490, "TreeRoot", NodeClass.Object),
            new(5491, "BranchA", NodeClass.Object),
            new(5492, "LeafA1", NodeClass.Variable),
            new(5493, "SubBranchA", NodeClass.Object),
            new(5494, "LeafA2", NodeClass.Variable),
            new(5495, "BranchB", NodeClass.Object),
            new(5496, "LeafB1", NodeClass.Variable),
        ];

        /// <summary>
        /// Every node authored in the secondary (dependent) model namespace.
        /// </summary>
        public static IReadOnlyList<ExpectedNode> SecondaryNodes { get; } =
        [
            new(6001, "SecondaryObjectType", NodeClass.ObjectType),
            new(6010, "SecondaryInstance", NodeClass.Object),
        ];

        /// <summary>
        /// At least one forward (or explicitly authored inverse) occurrence of
        /// every reference relation the model exercises.
        /// </summary>
        public static IReadOnlyList<ExpectedReference> References { get; } =
        [
            // Hierarchical.
            new(5400, ReferenceTypes.Organizes, Objects.ObjectsFolder, false, false),
            new(5400, ReferenceTypes.HasComponent, 5424, true, true),
            new(5400, ReferenceTypes.HasOrderedComponent, 5426, true, true),
            new(5100, ReferenceTypes.HasProperty, 5101, true, true),
            new(5200, ReferenceTypes.HasSubtype, 5210, true, true),
            new(5410, ReferenceTypes.HasEventSource, 5411, true, true),

            // Non-hierarchical / instance wiring.
            new(5420, ReferenceTypes.HasTypeDefinition, VariableTypes.BaseDataVariableType, false, true),
            new(5211, ReferenceTypes.HasModellingRule, ObjectIds_ModellingRule_Mandatory, false, true),
            new(5212, ReferenceTypes.HasModellingRule, ObjectIds_ModellingRule_Optional, false, true),
            new(5213, ReferenceTypes.HasModellingRule, ObjectIds_ModellingRule_MandatoryPlaceholder, false, true),
            new(5214, ReferenceTypes.HasModellingRule, ObjectIds_ModellingRule_OptionalPlaceholder, false, true),
            new(5215, ReferenceTypes.HasModellingRule, ObjectIds_ModellingRule_ExposesItsArray, false, true),
            new(5130, ReferenceTypes.HasEncoding, 5131, true, true),
            new(5210, ReferenceTypes.HasInterface, 5240, true, true),
            new(5210, ReferenceTypes.GeneratesEvent, 5230, true, true),
            new(5216, ReferenceTypes.AlwaysGeneratesEvent, 5230, true, true),
            new(5410, ReferenceTypes.HasCondition, 5412, true, true),

            // State machine.
            new(5473, ReferenceTypes.FromState, 5471, true, true),
            new(5473, ReferenceTypes.ToState, 5472, true, true),
            new(5473, ReferenceTypes.HasCause, 5450, true, true),
            new(5473, ReferenceTypes.HasEffect, 5230, true, true),
            new(5471, ReferenceTypes.HasTrueSubState, 5472, true, true),
            new(5471, ReferenceTypes.HasFalseSubState, 5472, true, true),

            // Custom reference types + inverse authoring.
            new(5400, 5001, 5420, true, true),
            new(5400, 5002, 5421, true, true),
            new(5400, 5004, 5422, true, true),

            // Deep instance tree (nested HasComponent chain).
            new(5400, ReferenceTypes.HasComponent, 5490, true, true),
            new(5490, ReferenceTypes.HasComponent, 5491, true, true),
            new(5491, ReferenceTypes.HasComponent, 5493, true, true),
            new(5493, ReferenceTypes.HasComponent, 5494, true, true),
        ];

        // Standard modelling-rule object identifiers (namespace 0).
        private const uint ObjectIds_ModellingRule_Mandatory = 78;
        private const uint ObjectIds_ModellingRule_Optional = 80;
        private const uint ObjectIds_ModellingRule_ExposesItsArray = 83;
        private const uint ObjectIds_ModellingRule_MandatoryPlaceholder = 11508;
        private const uint ObjectIds_ModellingRule_OptionalPlaceholder = 11510;

        /// <summary>
        /// Builds the model-namespace <see cref="NodeId"/> for an owned node.
        /// </summary>
        public static NodeId NodeId(uint id, ushort namespaceIndex)
        {
            return new NodeId(id, namespaceIndex);
        }
    }
}
