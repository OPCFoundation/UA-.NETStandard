/* ========================================================================
 * Copyright (c) 2005-2025 The OPC Foundation, Inc. All rights reserved.
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
#nullable enable

using System;
using System.Globalization;
using System.Xml;
using NUnit.Framework;

namespace Opc.Ua.Schema.Model.Tests
{
    /// <summary>
    /// Tests for the VariableTypeDesignJson class.
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ModelDesignSchemaTests
    {
        /// <summary>
        /// Tests ToNodeDesign when all nullable properties are null.
        /// Input: VariableTypeDesignJson with all nullable properties set to null.
        /// Expected: Returns VariableTypeDesign with non-nullable properties assigned and Specified flags false.
        /// </summary>
        [Test]
        public void ToNodeDesign_AllNullablePropertiesNull_ReturnsVariableTypeDesignWithDefaultValues()
        {
            // Arrange
            var json = new VariableTypeDesignJson(
                BrowseName: "TestVariable",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                DefaultValue: null,
                DataType: null,
                ValueRank: null,
                ArrayDimensions: null,
                AccessLevel: null,
                MinimumSamplingInterval: null,
                Historizing: null,
                ExposesItsChildren: false);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<VariableTypeDesign>());
            var variableType = (VariableTypeDesign)result;
            Assert.That(variableType.DefaultValue, Is.Null);
            Assert.That(variableType.DataType, Is.Null);
            Assert.That(variableType.ArrayDimensions, Is.Null);
            Assert.That(variableType.ValueRankSpecified, Is.False);
            Assert.That(variableType.AccessLevelSpecified, Is.False);
            Assert.That(variableType.MinimumSamplingIntervalSpecified, Is.False);
            Assert.That(variableType.HistorizingSpecified, Is.False);
            Assert.That(variableType.ExposesItsChildren, Is.False);
        }

        /// <summary>
        /// Tests ToNodeDesign with Historizing set to true and false.
        /// Input: VariableTypeDesignJson with Historizing true/false.
        /// Expected: Returns VariableTypeDesign with correct Historizing and HistorizingSpecified true.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void ToNodeDesign_HistorizingSetToTrueOrFalse_ReturnsVariableTypeDesignWithHistorizingSpecified(
            bool historizing)
        {
            // Arrange
            var json = new VariableTypeDesignJson(
                BrowseName: "TestVariable",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                DefaultValue: null,
                DataType: null,
                ValueRank: null,
                ArrayDimensions: null,
                AccessLevel: null,
                MinimumSamplingInterval: null,
                Historizing: historizing,
                ExposesItsChildren: false);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            var variableType = (VariableTypeDesign)result;
            Assert.That(variableType.Historizing, Is.EqualTo(historizing));
            Assert.That(variableType.HistorizingSpecified, Is.True);
        }

        /// <summary>
        /// Tests ToNodeDesign with ExposesItsChildren set to true and false.
        /// Input: VariableTypeDesignJson with ExposesItsChildren true/false.
        /// Expected: Returns VariableTypeDesign with correct ExposesItsChildren value.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void ToNodeDesign_ExposesItsChildrenSetToTrueOrFalse_ReturnsVariableTypeDesignWithCorrectValue(
            bool exposesItsChildren)
        {
            // Arrange
            var json = new VariableTypeDesignJson(
                BrowseName: "TestVariable",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                DefaultValue: null,
                DataType: null,
                ValueRank: null,
                ArrayDimensions: null,
                AccessLevel: null,
                MinimumSamplingInterval: null,
                Historizing: null,
                ExposesItsChildren: exposesItsChildren);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            var variableType = (VariableTypeDesign)result;
            Assert.That(variableType.ExposesItsChildren, Is.EqualTo(exposesItsChildren));
        }

        /// <summary>
        /// Tests ToNodeDesign with ArrayDimensions set to various string values.
        /// Input: VariableTypeDesignJson with different ArrayDimensions string values.
        /// Expected: Returns VariableTypeDesign with correct ArrayDimensions assigned.
        /// </summary>
        [TestCase("")]
        [TestCase("1")]
        [TestCase("1,2,3")]
        [TestCase("0,0,0")]
        [TestCase("10,20,30,40,50")]
        public void ToNodeDesign_ArrayDimensionsVariousStrings_ReturnsVariableTypeDesignWithCorrectArrayDimensions(
            string arrayDimensions)
        {
            // Arrange
            var json = new VariableTypeDesignJson(
                BrowseName: "TestVariable",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                DefaultValue: null,
                DataType: null,
                ValueRank: null,
                ArrayDimensions: arrayDimensions,
                AccessLevel: null,
                MinimumSamplingInterval: null,
                Historizing: null,
                ExposesItsChildren: false);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            var variableType = (VariableTypeDesign)result;
            Assert.That(variableType.ArrayDimensions, Is.EqualTo(arrayDimensions));
        }

        /// <summary>
        /// Tests ToNodeDesign with combination of ValueRank null and non-null.
        /// Input: VariableTypeDesignJson with ValueRank null.
        /// Expected: Returns VariableTypeDesign with ValueRankSpecified false.
        /// </summary>
        [Test]
        public void ToNodeDesign_ValueRankNull_ReturnsVariableTypeDesignWithValueRankSpecifiedFalse()
        {
            // Arrange
            var json = new VariableTypeDesignJson(
                BrowseName: "TestVariable",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                DefaultValue: null,
                DataType: null,
                ValueRank: null,
                ArrayDimensions: null,
                AccessLevel: null,
                MinimumSamplingInterval: null,
                Historizing: null,
                ExposesItsChildren: false);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            var variableType = (VariableTypeDesign)result;
            Assert.That(variableType.ValueRankSpecified, Is.False);
        }

        /// <summary>
        /// Tests ToNodeDesign with AccessLevel null.
        /// Input: VariableTypeDesignJson with AccessLevel null.
        /// Expected: Returns VariableTypeDesign with AccessLevelSpecified false.
        /// </summary>
        [Test]
        public void ToNodeDesign_AccessLevelNull_ReturnsVariableTypeDesignWithAccessLevelSpecifiedFalse()
        {
            // Arrange
            var json = new VariableTypeDesignJson(
                BrowseName: "TestVariable",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                DefaultValue: null,
                DataType: null,
                ValueRank: null,
                ArrayDimensions: null,
                AccessLevel: null,
                MinimumSamplingInterval: null,
                Historizing: null,
                ExposesItsChildren: false);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            var variableType = (VariableTypeDesign)result;
            Assert.That(variableType.AccessLevelSpecified, Is.False);
        }

        /// <summary>
        /// Tests ToNodeDesign with MinimumSamplingInterval null.
        /// Input: VariableTypeDesignJson with MinimumSamplingInterval null.
        /// Expected: Returns VariableTypeDesign with MinimumSamplingIntervalSpecified false.
        /// </summary>
        [Test]
        public void ToNodeDesign_MinimumSamplingIntervalNull_ReturnsVariableTypeDesignWithMinimumSamplingIntervalSpecifiedFalse()
        {
            // Arrange
            var json = new VariableTypeDesignJson(
                BrowseName: "TestVariable",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                DefaultValue: null,
                DataType: null,
                ValueRank: null,
                ArrayDimensions: null,
                AccessLevel: null,
                MinimumSamplingInterval: null,
                Historizing: null,
                ExposesItsChildren: false);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            var variableType = (VariableTypeDesign)result;
            Assert.That(variableType.MinimumSamplingIntervalSpecified, Is.False);
        }

        /// <summary>
        /// Tests ToNodeDesign with Historizing null.
        /// Input: VariableTypeDesignJson with Historizing null.
        /// Expected: Returns VariableTypeDesign with HistorizingSpecified false.
        /// </summary>
        [Test]
        public void ToNodeDesign_HistorizingNull_ReturnsVariableTypeDesignWithHistorizingSpecifiedFalse()
        {
            // Arrange
            var json = new VariableTypeDesignJson(
                BrowseName: "TestVariable",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                DefaultValue: null,
                DataType: null,
                ValueRank: null,
                ArrayDimensions: null,
                AccessLevel: null,
                MinimumSamplingInterval: null,
                Historizing: null,
                ExposesItsChildren: false);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            var variableType = (VariableTypeDesign)result;
            Assert.That(variableType.HistorizingSpecified, Is.False);
        }

        /// <summary>
        /// Tests ToNodeDesign with complex XmlElement DefaultValue.
        /// Input: VariableTypeDesignJson with complex XmlElement DefaultValue.
        /// Expected: Returns VariableTypeDesign with correct DefaultValue assigned.
        /// </summary>
        [Test]
        public void ToNodeDesign_ComplexXmlElementDefaultValue_ReturnsVariableTypeDesignWithCorrectDefaultValue()
        {
            // Arrange
            var xmlDoc = new XmlDocument();
            XmlElement defaultValue = xmlDoc.CreateElement("Value");
            defaultValue.SetAttribute("type", "String");
            defaultValue.InnerText = "TestValue";

            var json = new VariableTypeDesignJson(
                BrowseName: "TestVariable",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                DefaultValue: defaultValue,
                DataType: null,
                ValueRank: null,
                ArrayDimensions: null,
                AccessLevel: null,
                MinimumSamplingInterval: null,
                Historizing: null,
                ExposesItsChildren: false);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            var variableType = (VariableTypeDesign)result;
            Assert.That(variableType.DefaultValue, Is.EqualTo(defaultValue));
            Assert.That(variableType.DefaultValue.InnerText, Is.EqualTo("TestValue"));
        }

        /// <summary>
        /// Tests ToNodeDesign with minimal parameters (all nullable fields null).
        /// Verifies that a valid EncodingDesign object is created with default values.
        /// </summary>
        [Test]
        public void ToNodeDesign_MinimalParameters_ReturnsValidEncodingDesign()
        {
            // Arrange
            var encodingDesignJson = new EncodingDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                SupportsEvents: null);

            // Act
            var result = encodingDesignJson.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<EncodingDesign>());
            Assert.That(result.BrowseName, Is.Null);
            Assert.That(result.IsDeclaration, Is.False);
        }

        /// <summary>
        /// Tests ToNodeDesign with empty strings for string parameters.
        /// Verifies that empty strings are correctly handled and transferred.
        /// </summary>
        [Test]
        public void ToNodeDesign_EmptyStrings_HandlesCorrectly()
        {
            // Arrange
            var encodingDesignJson = new EncodingDesignJson(
                BrowseName: string.Empty,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: string.Empty,
                WriteAccess: 0,
                PartNo: 0,
                Category: string.Empty,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: string.Empty,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                SupportsEvents: null);

            // Act
            var result = encodingDesignJson.ToNodeDesign();

            // Assert
            Assert.That(result, Is.InstanceOf<EncodingDesign>());
            Assert.That(result.BrowseName, Is.EqualTo(string.Empty));
            Assert.That(result.StringId, Is.EqualTo(string.Empty));
            Assert.That(result.Category, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests ToNodeDesign with numeric boundary values (uint.MaxValue, uint.MinValue).
        /// Verifies that extreme numeric values are correctly handled.
        /// </summary>
        [TestCase(uint.MinValue, uint.MinValue, uint.MinValue, TestName = "MinValues")]
        [TestCase(uint.MaxValue, uint.MaxValue, uint.MaxValue, TestName = "MaxValues")]
        [TestCase(0u, 1u, 100u, TestName = "TypicalValues")]
        public void ToNodeDesign_NumericBoundaryValues_HandlesCorrectly(
            uint writeAccess,
            uint partNo,
            uint numericId)
        {
            // Arrange
            var encodingDesignJson = new EncodingDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: numericId,
                StringId: null,
                WriteAccess: writeAccess,
                PartNo: partNo,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                SupportsEvents: null);

            // Act
            var result = encodingDesignJson.ToNodeDesign();

            // Assert
            Assert.That(result, Is.InstanceOf<EncodingDesign>());
            Assert.That(result.WriteAccess, Is.EqualTo(writeAccess));
            Assert.That(result.PartNo, Is.EqualTo(partNo));
            Assert.That(result.NumericId, Is.EqualTo(numericId));
            Assert.That(result.NumericIdSpecified, Is.True);
        }

        /// <summary>
        /// Tests ToNodeDesign with null NumericId.
        /// Verifies that when NumericId is null, NumericIdSpecified is false.
        /// </summary>
        [Test]
        public void ToNodeDesign_NullNumericId_SetsSpecifiedToFalse()
        {
            // Arrange
            var encodingDesignJson = new EncodingDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                SupportsEvents: null);

            // Act
            var result = encodingDesignJson.ToNodeDesign();

            // Assert
            Assert.That(result, Is.InstanceOf<EncodingDesign>());
            Assert.That(result.NumericIdSpecified, Is.False);
        }

        /// <summary>
        /// Tests ToNodeDesign with all enum values for ReleaseStatus.
        /// Verifies that different ReleaseStatus values are correctly transferred.
        /// </summary>
        [TestCase(ReleaseStatus.Released)]
        [TestCase(ReleaseStatus.Draft)]
        [TestCase(ReleaseStatus.Deprecated)]
        public void ToNodeDesign_DifferentReleaseStatus_TransfersCorrectly(ReleaseStatus releaseStatus)
        {
            // Arrange
            var encodingDesignJson = new EncodingDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: releaseStatus,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                SupportsEvents: null);

            // Act
            var result = encodingDesignJson.ToNodeDesign();

            // Assert
            Assert.That(result, Is.InstanceOf<EncodingDesign>());
            Assert.That(result.ReleaseStatus, Is.EqualTo(releaseStatus));
        }

        /// <summary>
        /// Tests ToNodeDesign with all enum values for DataTypePurpose.
        /// Verifies that different DataTypePurpose values are correctly transferred.
        /// </summary>
        [TestCase(DataTypePurpose.Normal)]
        [TestCase(DataTypePurpose.ServicesOnly)]
        [TestCase(DataTypePurpose.CodeGenerator)]
        public void ToNodeDesign_DifferentDataTypePurpose_TransfersCorrectly(DataTypePurpose purpose)
        {
            // Arrange
            var encodingDesignJson = new EncodingDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: purpose,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                SupportsEvents: null);

            // Act
            var result = encodingDesignJson.ToNodeDesign();

            // Assert
            Assert.That(result, Is.InstanceOf<EncodingDesign>());
            Assert.That(result.Purpose, Is.EqualTo(purpose));
        }

        /// <summary>
        /// Tests ToNodeDesign with AccessRestrictions set.
        /// Verifies that AccessRestrictions and AccessRestrictionsSpecified are correctly set.
        /// </summary>
        [Test]
        public void ToNodeDesign_WithAccessRestrictions_SetsPropertyAndSpecified()
        {
            // Arrange
            const AccessRestrictions accessRestrictions = AccessRestrictions.SigningRequired;

            var encodingDesignJson = new EncodingDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: accessRestrictions,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                SupportsEvents: null);

            // Act
            var result = encodingDesignJson.ToNodeDesign();

            // Assert
            Assert.That(result, Is.InstanceOf<EncodingDesign>());
            Assert.That(result.AccessRestrictions, Is.EqualTo(accessRestrictions));
            Assert.That(result.AccessRestrictionsSpecified, Is.True);
        }

        /// <summary>
        /// Tests ToNodeDesign with DefaultAccessRestrictions set.
        /// Verifies that DefaultAccessRestrictions and DefaultAccessRestrictionsSpecified are correctly set.
        /// </summary>
        [Test]
        public void ToNodeDesign_WithDefaultAccessRestrictions_SetsPropertyAndSpecified()
        {
            // Arrange
            const AccessRestrictions defaultAccessRestrictions = AccessRestrictions.EncryptionRequired;

            var encodingDesignJson = new EncodingDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: defaultAccessRestrictions,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                SupportsEvents: null);

            // Act
            var result = encodingDesignJson.ToNodeDesign();

            // Assert
            Assert.That(result, Is.InstanceOf<EncodingDesign>());
            Assert.That(result.DefaultAccessRestrictions, Is.EqualTo(defaultAccessRestrictions));
            Assert.That(result.DefaultAccessRestrictionsSpecified, Is.True);
        }

        /// <summary>
        /// Tests ToNodeDesign with SupportsEvents set to true.
        /// Verifies that SupportsEvents property and SupportsEventsSpecified are correctly set.
        /// </summary>
        [Test]
        public void ToNodeDesign_SupportsEventsTrue_SetsSupportsEventsAndSpecified()
        {
            // Arrange
            var json = new ObjectTypeDesignJson(
                BrowseName: "TestObjectType",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                SupportsEvents: true);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<ObjectTypeDesign>());
            var objectType = (ObjectTypeDesign)result;
            Assert.That(objectType.SupportsEvents, Is.True);
            Assert.That(objectType.SupportsEventsSpecified, Is.True);
        }

        /// <summary>
        /// Tests ToNodeDesign with SupportsEvents set to false.
        /// Verifies that SupportsEvents property is false and SupportsEventsSpecified is true.
        /// </summary>
        [Test]
        public void ToNodeDesign_SupportsEventsFalse_SetsSupportsEventsFalseAndSpecified()
        {
            // Arrange
            var json = new ObjectTypeDesignJson(
                BrowseName: "TestObjectType",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                SupportsEvents: false);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<ObjectTypeDesign>());
            var objectType = (ObjectTypeDesign)result;
            Assert.That(objectType.SupportsEvents, Is.False);
            Assert.That(objectType.SupportsEventsSpecified, Is.True);
        }

        /// <summary>
        /// Tests ToNodeDesign with SupportsEvents set to null.
        /// Verifies that SupportsEventsSpecified remains false when SupportsEvents has no value.
        /// </summary>
        [Test]
        public void ToNodeDesign_SupportsEventsNull_DoesNotSetSpecified()
        {
            // Arrange
            var json = new ObjectTypeDesignJson(
                BrowseName: "TestObjectType",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                SupportsEvents: null);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<ObjectTypeDesign>());
            var objectType = (ObjectTypeDesign)result;
            Assert.That(objectType.SupportsEventsSpecified, Is.False);
        }

        /// <summary>
        /// Tests ToNodeDesign with all properties set to valid values.
        /// Verifies that all properties from the base class are correctly applied.
        /// </summary>
        [Test]
        public void ToNodeDesign_AllPropertiesSet_AppliesAllPropertiesCorrectly()
        {
            // Arrange
            const string browseName = "TestObjectType";
            var displayName = new LocalizedText { Value = "Test Object Type" };
            var description = new LocalizedText { Value = "Test Description" };
            var symbolicName = new XmlQualifiedName("TestSymbolicName", "http://test.com");
            var symbolicId = new XmlQualifiedName("TestSymbolicId", "http://test.com");
            var baseType = new XmlQualifiedName("BaseObjectType", "http://opcfoundation.org");
            const string className = "TestClassName";
            const uint numericId = 12345;
            const string stringId = "TestStringId";
            const uint writeAccess = 1;
            const uint partNo = 42;
            const string category = "TestCategory";

            var json = new ObjectTypeDesignJson(
                BrowseName: browseName,
                DisplayName: displayName,
                Description: description,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: AccessRestrictions.EncryptionRequired,
                DefaultAccessRestrictions: AccessRestrictions.SigningRequired,
                Extensions: null,
                SymbolicName: symbolicName,
                SymbolicId: symbolicId,
                IsDeclaration: true,
                NumericId: numericId,
                StringId: stringId,
                WriteAccess: writeAccess,
                PartNo: partNo,
                Category: category,
                NotInAddressSpace: true,
                ReleaseStatus: ReleaseStatus.Deprecated,
                Purpose: DataTypePurpose.ServicesOnly,
                IsDynamic: true,
                NodeType: "ObjectType",
                ClassName: className,
                BaseType: baseType,
                IsAbstract: true,
                NoClassGeneration: true,
                SupportsEvents: true);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<ObjectTypeDesign>());
            var objectType = (ObjectTypeDesign)result;

            // Verify NodeDesign properties
            Assert.That(objectType.BrowseName, Is.EqualTo(browseName));
            Assert.That(objectType.DisplayName, Is.EqualTo(displayName));
            Assert.That(objectType.Description, Is.EqualTo(description));
            Assert.That(objectType.SymbolicName, Is.EqualTo(symbolicName));
            Assert.That(objectType.SymbolicId, Is.EqualTo(symbolicId));
            Assert.That(objectType.NumericId, Is.EqualTo(numericId));
            Assert.That(objectType.NumericIdSpecified, Is.True);
            Assert.That(objectType.StringId, Is.EqualTo(stringId));
            Assert.That(objectType.WriteAccess, Is.EqualTo(writeAccess));
            Assert.That(objectType.PartNo, Is.EqualTo(partNo));
            Assert.That(objectType.Category, Is.EqualTo(category));
            Assert.That(objectType.IsDeclaration, Is.True);
            Assert.That(objectType.NotInAddressSpace, Is.True);
            Assert.That(objectType.ReleaseStatus, Is.EqualTo(ReleaseStatus.Deprecated));
            Assert.That(objectType.Purpose, Is.EqualTo(DataTypePurpose.ServicesOnly));
            Assert.That(objectType.IsDynamic, Is.True);
            Assert.That(objectType.AccessRestrictions, Is.EqualTo(AccessRestrictions.EncryptionRequired));
            Assert.That(objectType.AccessRestrictionsSpecified, Is.True);
            Assert.That(objectType.DefaultAccessRestrictions, Is.EqualTo(AccessRestrictions.SigningRequired));
            Assert.That(objectType.DefaultAccessRestrictionsSpecified, Is.True);

            // Verify TypeDesign properties
            Assert.That(objectType.ClassName, Is.EqualTo(className));
            Assert.That(objectType.BaseType, Is.EqualTo(baseType));
            Assert.That(objectType.IsAbstract, Is.True);
            Assert.That(objectType.NoClassGeneration, Is.True);

            // Verify ObjectTypeDesign specific properties
            Assert.That(objectType.SupportsEvents, Is.True);
            Assert.That(objectType.SupportsEventsSpecified, Is.True);
        }

        /// <summary>
        /// Tests ToNodeDesign with extreme boundary values for numeric properties.
        /// Verifies that extreme values are handled correctly.
        /// </summary>
        [Test]
        public void ToNodeDesign_ExtremeBoundaryValues_HandlesCorrectly()
        {
            // Arrange
            var json = new ObjectTypeDesignJson(
                BrowseName: "TestObjectType",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: uint.MaxValue,
                StringId: null,
                WriteAccess: uint.MaxValue,
                PartNo: uint.MaxValue,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                SupportsEvents: true);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<ObjectTypeDesign>());
            var objectType = (ObjectTypeDesign)result;
            Assert.That(objectType.NumericId, Is.EqualTo(uint.MaxValue));
            Assert.That(objectType.WriteAccess, Is.EqualTo(uint.MaxValue));
            Assert.That(objectType.PartNo, Is.EqualTo(uint.MaxValue));
        }

        /// <summary>
        /// Tests ToNodeDesign with all nullable properties set to null.
        /// Verifies that the method handles null values gracefully.
        /// </summary>
        [Test]
        public void ToNodeDesign_AllNullablePropertiesNull_CreatesValidObjectType()
        {
            // Arrange
            var json = new ObjectTypeDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                SupportsEvents: null);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<ObjectTypeDesign>());
            var objectType = (ObjectTypeDesign)result;
            Assert.That(objectType.BrowseName, Is.Null);
            Assert.That(objectType.DisplayName, Is.Null);
            Assert.That(objectType.Description, Is.Null);
            Assert.That(objectType.SymbolicName, Is.Null);
            Assert.That(objectType.SymbolicId, Is.Null);
            Assert.That(objectType.BaseType, Is.Null);
            Assert.That(objectType.ClassName, Is.Null);
            Assert.That(objectType.SupportsEventsSpecified, Is.False);
        }

        /// <summary>
        /// Tests ToNodeDesign with empty string values.
        /// Verifies that empty strings are preserved correctly.
        /// </summary>
        [Test]
        public void ToNodeDesign_EmptyStrings_PreservesEmptyValues()
        {
            // Arrange
            var json = new ObjectTypeDesignJson(
                BrowseName: string.Empty,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: string.Empty,
                WriteAccess: 0,
                PartNo: 0,
                Category: string.Empty,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: string.Empty,
                ClassName: string.Empty,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                SupportsEvents: null);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<ObjectTypeDesign>());
            var objectType = (ObjectTypeDesign)result;
            Assert.That(objectType.BrowseName, Is.EqualTo(string.Empty));
            Assert.That(objectType.StringId, Is.EqualTo(string.Empty));
            Assert.That(objectType.Category, Is.EqualTo(string.Empty));
            Assert.That(objectType.ClassName, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests ToNodeDesign with all enum values at boundaries.
        /// Verifies correct handling of enum boundary values.
        /// </summary>
        [TestCase(ReleaseStatus.Released, DataTypePurpose.Normal)]
        [TestCase(ReleaseStatus.Draft, DataTypePurpose.CodeGenerator)]
        [TestCase(ReleaseStatus.Deprecated, DataTypePurpose.ServicesOnly)]
        public void ToNodeDesign_EnumBoundaryValues_HandlesCorrectly(
            ReleaseStatus releaseStatus,
            DataTypePurpose purpose)
        {
            // Arrange
            var json = new ObjectTypeDesignJson(
                BrowseName: "TestObjectType",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: releaseStatus,
                Purpose: purpose,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                SupportsEvents: true);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            var objectType = (ObjectTypeDesign)result;
            Assert.That(objectType.ReleaseStatus, Is.EqualTo(releaseStatus));
            Assert.That(objectType.Purpose, Is.EqualTo(purpose));
        }

        /// <summary>
        /// Tests ToNodeDesign with zero values for numeric properties.
        /// Verifies that zero values are handled correctly.
        /// </summary>
        [Test]
        public void ToNodeDesign_ZeroNumericValues_HandlesCorrectly()
        {
            // Arrange
            var json = new ObjectTypeDesignJson(
                BrowseName: "TestObjectType",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: 0,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                SupportsEvents: false);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            var objectType = (ObjectTypeDesign)result;
            Assert.That(objectType.NumericId, Is.EqualTo(0));
            Assert.That(objectType.NumericIdSpecified, Is.True);
            Assert.That(objectType.WriteAccess, Is.EqualTo(0));
            Assert.That(objectType.PartNo, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests ToNodeDesign returns a ViewDesign instance with all boolean combinations.
        /// Input: ViewDesignJson with various SupportsEvents and ContainsNoLoops values.
        /// Expected: Returns ViewDesign with matching property values.
        /// </summary>
        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void ToNodeDesign_WithVariousBooleanCombinations_SetsPropertiesCorrectly(
            bool supportsEvents,
            bool containsNoLoops)
        {
            // Arrange
            var viewDesignJson = new ViewDesignJson(
                BrowseName: "TestView",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                SupportsEvents: supportsEvents,
                ContainsNoLoops: containsNoLoops);

            // Act
            var result = viewDesignJson.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.TypeOf<ViewDesign>());
            var viewDesign = (ViewDesign)result;
            Assert.That(viewDesign.SupportsEvents, Is.EqualTo(supportsEvents));
            Assert.That(viewDesign.ContainsNoLoops, Is.EqualTo(containsNoLoops));
        }

        /// <summary>
        /// Tests ToNodeDesign applies base properties through ApplyTo.
        /// Input: ViewDesignJson with BrowseName and other base properties set.
        /// Expected: Returns ViewDesign with BrowseName and base properties correctly applied.
        /// </summary>
        [Test]
        public void ToNodeDesign_WithBaseProperties_AppliesBasePropertiesCorrectly()
        {
            // Arrange
            var symbolicName = new XmlQualifiedName("TestSymbolicName", "http://test.org");
            var referenceType = new XmlQualifiedName("TestRefType", "http://test.org");
            var typeDefinition = new XmlQualifiedName("TestTypeDef", "http://test.org");

            var viewDesignJson = new ViewDesignJson(
                BrowseName: "TestBrowseName",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: symbolicName,
                SymbolicId: null,
                IsDeclaration: true,
                NumericId: 12345,
                StringId: "TestStringId",
                WriteAccess: 1,
                PartNo: 5,
                Category: "TestCategory",
                NotInAddressSpace: true,
                ReleaseStatus: ReleaseStatus.Draft,
                Purpose: DataTypePurpose.ServicesOnly,
                IsDynamic: true,
                NodeType: "TestNodeType",
                ReferenceType: referenceType,
                Declaration: null,
                TypeDefinition: typeDefinition,
                ModellingRule: ModellingRule.Mandatory,
                MinCardinality: 1,
                MaxCardinality: 10,
                PreserveDefaultAttributes: true,
                DesignToolOnly: true,
                SupportsEvents: true,
                ContainsNoLoops: false);

            // Act
            var result = viewDesignJson.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.TypeOf<ViewDesign>());
            var viewDesign = (ViewDesign)result;

            // Verify base properties from NodeDesign
            Assert.That(viewDesign.BrowseName, Is.EqualTo("TestBrowseName"));
            Assert.That(viewDesign.SymbolicName, Is.EqualTo(symbolicName));
            Assert.That(viewDesign.IsDeclaration, Is.EqualTo(true));
            Assert.That(viewDesign.NumericId, Is.EqualTo(12345));
            Assert.That(viewDesign.StringId, Is.EqualTo("TestStringId"));
            Assert.That(viewDesign.WriteAccess, Is.EqualTo((uint)1));
            Assert.That(viewDesign.PartNo, Is.EqualTo((uint)5));
            Assert.That(viewDesign.Category, Is.EqualTo("TestCategory"));
            Assert.That(viewDesign.NotInAddressSpace, Is.EqualTo(true));
            Assert.That(viewDesign.ReleaseStatus, Is.EqualTo(ReleaseStatus.Draft));
            Assert.That(viewDesign.Purpose, Is.EqualTo(DataTypePurpose.ServicesOnly));
            Assert.That(viewDesign.IsDynamic, Is.EqualTo(true));

            // Verify instance design properties
            Assert.That(viewDesign.ReferenceType, Is.EqualTo(referenceType));
            Assert.That(viewDesign.TypeDefinition, Is.EqualTo(typeDefinition));
            Assert.That(viewDesign.ModellingRule, Is.EqualTo(ModellingRule.Mandatory));
            Assert.That(viewDesign.ModellingRuleSpecified, Is.True);
            Assert.That(viewDesign.MinCardinality, Is.EqualTo((uint)1));
            Assert.That(viewDesign.MaxCardinality, Is.EqualTo((uint)10));
            Assert.That(viewDesign.PreserveDefaultAttributes, Is.EqualTo(true));
            Assert.That(viewDesign.DesignToolOnly, Is.EqualTo(true));

            // Verify view-specific properties
            Assert.That(viewDesign.SupportsEvents, Is.EqualTo(true));
            Assert.That(viewDesign.ContainsNoLoops, Is.EqualTo(false));
        }

        /// <summary>
        /// Tests ToNodeDesign with minimal required properties.
        /// Input: ViewDesignJson with only required properties (all nullable fields set to null).
        /// Expected: Returns ViewDesign without throwing exceptions.
        /// </summary>
        [Test]
        public void ToNodeDesign_WithMinimalProperties_ReturnsValidViewDesign()
        {
            // Arrange
            var viewDesignJson = new ViewDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                SupportsEvents: false,
                ContainsNoLoops: false);

            // Act
            var result = viewDesignJson.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.TypeOf<ViewDesign>());
            var viewDesign = (ViewDesign)result;
            Assert.That(viewDesign.SupportsEvents, Is.False);
            Assert.That(viewDesign.ContainsNoLoops, Is.False);
        }

        /// <summary>
        /// Tests ToNodeDesign with boundary values for uint properties.
        /// Input: ViewDesignJson with uint.MaxValue for MinCardinality and MaxCardinality.
        /// Expected: Returns ViewDesign with boundary values correctly set.
        /// </summary>
        [Test]
        public void ToNodeDesign_WithBoundaryUintValues_SetsBoundaryValuesCorrectly()
        {
            // Arrange
            var viewDesignJson = new ViewDesignJson(
                BrowseName: "TestView",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: uint.MaxValue,
                StringId: null,
                WriteAccess: uint.MaxValue,
                PartNo: uint.MaxValue,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: uint.MaxValue,
                MaxCardinality: uint.MaxValue,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                SupportsEvents: true,
                ContainsNoLoops: true);

            // Act
            var result = viewDesignJson.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            var viewDesign = (ViewDesign)result;
            Assert.That(viewDesign.NumericId, Is.EqualTo(uint.MaxValue));
            Assert.That(viewDesign.WriteAccess, Is.EqualTo(uint.MaxValue));
            Assert.That(viewDesign.PartNo, Is.EqualTo(uint.MaxValue));
            Assert.That(viewDesign.MinCardinality, Is.EqualTo(uint.MaxValue));
            Assert.That(viewDesign.MaxCardinality, Is.EqualTo(uint.MaxValue));
        }

        /// <summary>
        /// Tests ToNodeDesign with all ReleaseStatus enum values.
        /// Input: ViewDesignJson with each ReleaseStatus value.
        /// Expected: Returns ViewDesign with correct ReleaseStatus set.
        /// </summary>
        [TestCase(ReleaseStatus.Released)]
        [TestCase(ReleaseStatus.Draft)]
        [TestCase(ReleaseStatus.Deprecated)]
        public void ToNodeDesign_WithVariousReleaseStatusValues_SetsReleaseStatusCorrectly(
            ReleaseStatus releaseStatus)
        {
            // Arrange
            var viewDesignJson = new ViewDesignJson(
                BrowseName: "TestView",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: releaseStatus,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                SupportsEvents: false,
                ContainsNoLoops: false);

            // Act
            var result = viewDesignJson.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            var viewDesign = (ViewDesign)result;
            Assert.That(viewDesign.ReleaseStatus, Is.EqualTo(releaseStatus));
        }

        /// <summary>
        /// Tests ToNodeDesign with ModellingRule set vs null.
        /// Input: ViewDesignJson with ModellingRule specified and null.
        /// Expected: ViewDesign.ModellingRuleSpecified is true when set, false otherwise.
        /// </summary>
        [TestCase(ModellingRule.Mandatory, true)]
        [TestCase(ModellingRule.Optional, true)]
        [TestCase(ModellingRule.ExposesItsArray, true)]
        [TestCase(ModellingRule.MandatoryPlaceholder, true)]
        [TestCase(ModellingRule.OptionalPlaceholder, true)]
        [TestCase(null, false)]
        public void ToNodeDesign_WithModellingRule_SetsModellingRuleSpecifiedCorrectly(
            ModellingRule? modellingRule,
            bool expectedSpecified)
        {
            // Arrange
            var viewDesignJson = new ViewDesignJson(
                BrowseName: "TestView",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: modellingRule,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                SupportsEvents: false,
                ContainsNoLoops: false);

            // Act
            var result = viewDesignJson.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            var viewDesign = (ViewDesign)result;
            Assert.That(viewDesign.ModellingRuleSpecified, Is.EqualTo(expectedSpecified));
            if (modellingRule.HasValue)
            {
                Assert.That(viewDesign.ModellingRule, Is.EqualTo(modellingRule.Value));
            }
        }

        /// <summary>
        /// Tests ToInstanceDesign with valid data.
        /// Verifies that the method returns a ViewDesign instance that can be cast to InstanceDesign.
        /// </summary>
        [Test]
        public void ToInstanceDesign_ValidData_ReturnsViewDesignInstance()
        {
            // Arrange
            var viewDesignJson = new ViewDesignJson(
                BrowseName: "TestView",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: 1000u,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                SupportsEvents: true,
                ContainsNoLoops: true);

            // Act
            var result = viewDesignJson.ToInstanceDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<ViewDesign>());
            Assert.That(result, Is.InstanceOf<InstanceDesign>());
            var viewDesign = (ViewDesign)result;
            Assert.That(viewDesign.SupportsEvents, Is.True);
            Assert.That(viewDesign.ContainsNoLoops, Is.True);
        }

        /// <summary>
        /// Tests ToInstanceDesign with SupportsEvents set to false.
        /// Verifies that the property is correctly transferred to the ViewDesign instance.
        /// </summary>
        [Test]
        public void ToInstanceDesign_SupportsEventsFalse_ReturnsViewDesignWithCorrectProperty()
        {
            // Arrange
            var viewDesignJson = new ViewDesignJson(
                BrowseName: "TestView",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                SupportsEvents: false,
                ContainsNoLoops: false);

            // Act
            var result = viewDesignJson.ToInstanceDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<ViewDesign>());
            var viewDesign = (ViewDesign)result;
            Assert.That(viewDesign.SupportsEvents, Is.False);
            Assert.That(viewDesign.ContainsNoLoops, Is.False);
        }

        /// <summary>
        /// Tests ToInstanceDesign with all nullable properties set to null.
        /// Verifies that the method handles null values correctly and returns a valid ViewDesign instance.
        /// </summary>
        [Test]
        public void ToInstanceDesign_AllNullablePropertiesNull_ReturnsValidViewDesign()
        {
            // Arrange
            var viewDesignJson = new ViewDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                SupportsEvents: false,
                ContainsNoLoops: false);

            // Act
            var result = viewDesignJson.ToInstanceDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<ViewDesign>());
        }

        /// <summary>
        /// Tests ToInstanceDesign with complex properties set.
        /// Verifies that all properties are correctly transferred to the ViewDesign instance.
        /// </summary>
        [Test]
        public void ToInstanceDesign_WithComplexProperties_ReturnsViewDesignWithAllProperties()
        {
            // Arrange
            var displayName = new LocalizedText { Key = "DisplayName", Value = "Test Display Name" };
            var description = new LocalizedText { Key = "Description", Value = "Test Description" };
            var symbolicName = new XmlQualifiedName("TestSymbolicName", "http://test.com");
            var symbolicId = new XmlQualifiedName("TestSymbolicId", "http://test.com");
            var referenceType = new XmlQualifiedName("HasComponent", "http://opcfoundation.org/UA/");
            var typeDefinition = new XmlQualifiedName("BaseObjectType", "http://opcfoundation.org/UA/");

            var viewDesignJson = new ViewDesignJson(
                BrowseName: "ComplexView",
                DisplayName: displayName,
                Description: description,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: symbolicName,
                SymbolicId: symbolicId,
                IsDeclaration: true,
                NumericId: 5000u,
                StringId: "TestStringId",
                WriteAccess: 1,
                PartNo: 10,
                Category: "TestCategory",
                NotInAddressSpace: true,
                ReleaseStatus: ReleaseStatus.Deprecated,
                Purpose: DataTypePurpose.ServicesOnly,
                IsDynamic: true,
                NodeType: "ViewType",
                ReferenceType: referenceType,
                Declaration: null,
                TypeDefinition: typeDefinition,
                ModellingRule: ModellingRule.Mandatory,
                MinCardinality: 1,
                MaxCardinality: 10,
                PreserveDefaultAttributes: true,
                DesignToolOnly: true,
                SupportsEvents: true,
                ContainsNoLoops: false);

            // Act
            var result = viewDesignJson.ToInstanceDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<ViewDesign>());
            var viewDesign = (ViewDesign)result;
            Assert.That(viewDesign.SupportsEvents, Is.True);
            Assert.That(viewDesign.ContainsNoLoops, Is.False);
            Assert.That(viewDesign.BrowseName, Is.EqualTo("ComplexView"));
            Assert.That(viewDesign.IsDeclaration, Is.True);
            Assert.That(viewDesign.NumericId, Is.EqualTo(5000u));
            Assert.That(viewDesign.NumericIdSpecified, Is.True);
        }

        /// <summary>
        /// Tests ToInstanceDesign with minimum and maximum cardinality values.
        /// Verifies that edge case values for uint are handled correctly.
        /// </summary>
        [TestCase(0u, 0u)]
        [TestCase(1u, 1u)]
        [TestCase(uint.MaxValue, uint.MaxValue)]
        [TestCase(0u, uint.MaxValue)]
        public void ToInstanceDesign_WithCardinalityEdgeCases_ReturnsViewDesignWithCorrectValues(
            uint minCardinality,
            uint maxCardinality)
        {
            // Arrange
            var viewDesignJson = new ViewDesignJson(
                BrowseName: "TestView",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: minCardinality,
                MaxCardinality: maxCardinality,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                SupportsEvents: false,
                ContainsNoLoops: false);

            // Act
            var result = viewDesignJson.ToInstanceDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<ViewDesign>());
            var viewDesign = (ViewDesign)result;
            Assert.That(viewDesign.MinCardinality, Is.EqualTo(minCardinality));
            Assert.That(viewDesign.MaxCardinality, Is.EqualTo(maxCardinality));
        }

        /// <summary>
        /// Tests ToInstanceDesign with different ReleaseStatus enum values.
        /// Verifies that all enum values are correctly transferred to the ViewDesign instance.
        /// </summary>
        [TestCase(ReleaseStatus.Released)]
        [TestCase(ReleaseStatus.Draft)]
        [TestCase(ReleaseStatus.Deprecated)]
        public void ToInstanceDesign_WithDifferentReleaseStatus_ReturnsViewDesignWithCorrectStatus(
            ReleaseStatus releaseStatus)
        {
            // Arrange
            var viewDesignJson = new ViewDesignJson(
                BrowseName: "TestView",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: releaseStatus,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                SupportsEvents: false,
                ContainsNoLoops: false);

            // Act
            var result = viewDesignJson.ToInstanceDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<ViewDesign>());
            var viewDesign = (ViewDesign)result;
            Assert.That(viewDesign.ReleaseStatus, Is.EqualTo(releaseStatus));
        }

        /// <summary>
        /// Tests ToInstanceDesign with different DataTypePurpose enum values.
        /// Verifies that all enum values are correctly transferred to the ViewDesign instance.
        /// </summary>
        [TestCase(DataTypePurpose.Normal)]
        [TestCase(DataTypePurpose.ServicesOnly)]
        [TestCase(DataTypePurpose.CodeGenerator)]
        public void ToInstanceDesign_WithDifferentDataTypePurpose_ReturnsViewDesignWithCorrectPurpose(
            DataTypePurpose purpose)
        {
            // Arrange
            var viewDesignJson = new ViewDesignJson(
                BrowseName: "TestView",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: purpose,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                SupportsEvents: false,
                ContainsNoLoops: false);

            // Act
            var result = viewDesignJson.ToInstanceDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<ViewDesign>());
            var viewDesign = (ViewDesign)result;
            Assert.That(viewDesign.Purpose, Is.EqualTo(purpose));
        }

        /// <summary>
        /// Tests ToNodeDesign when Symmetric is null.
        /// Input: ReferenceTypeDesignJson with Symmetric = null.
        /// Expected: Returns ReferenceTypeDesign with SymmetricSpecified = false.
        /// </summary>
        [Test]
        public void ToNodeDesign_SymmetricIsNull_SymmetricSpecifiedIsFalse()
        {
            // Arrange
            var json = new ReferenceTypeDesignJson(
                BrowseName: "TestReference",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                InverseName: null,
                Symmetric: null);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<ReferenceTypeDesign>());
            var referenceType = (ReferenceTypeDesign)result;
            Assert.That(referenceType.SymmetricSpecified, Is.False);
        }

        /// <summary>
        /// Tests ToNodeDesign when Symmetric is true.
        /// Input: ReferenceTypeDesignJson with Symmetric = true.
        /// Expected: Returns ReferenceTypeDesign with Symmetric = true and SymmetricSpecified = true.
        /// </summary>
        [Test]
        public void ToNodeDesign_SymmetricIsTrue_SymmetricAndSpecifiedAreTrue()
        {
            // Arrange
            var json = new ReferenceTypeDesignJson(
                BrowseName: "TestReference",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                InverseName: null,
                Symmetric: true);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<ReferenceTypeDesign>());
            var referenceType = (ReferenceTypeDesign)result;
            Assert.That(referenceType.Symmetric, Is.True);
            Assert.That(referenceType.SymmetricSpecified, Is.True);
        }

        /// <summary>
        /// Tests ToNodeDesign when Symmetric is false.
        /// Input: ReferenceTypeDesignJson with Symmetric = false.
        /// Expected: Returns ReferenceTypeDesign with Symmetric = false and SymmetricSpecified = true.
        /// </summary>
        [Test]
        public void ToNodeDesign_SymmetricIsFalse_SymmetricIsFalseAndSpecifiedIsTrue()
        {
            // Arrange
            var json = new ReferenceTypeDesignJson(
                BrowseName: "TestReference",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                InverseName: null,
                Symmetric: false);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<ReferenceTypeDesign>());
            var referenceType = (ReferenceTypeDesign)result;
            Assert.That(referenceType.Symmetric, Is.False);
            Assert.That(referenceType.SymmetricSpecified, Is.True);
        }

        /// <summary>
        /// Tests ToNodeDesign when InverseName is null.
        /// Input: ReferenceTypeDesignJson with InverseName = null.
        /// Expected: Returns ReferenceTypeDesign with InverseName = null.
        /// </summary>
        [Test]
        public void ToNodeDesign_InverseNameIsNull_InverseNameIsNull()
        {
            // Arrange
            var json = new ReferenceTypeDesignJson(
                BrowseName: "TestReference",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                InverseName: null,
                Symmetric: null);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<ReferenceTypeDesign>());
            var referenceType = (ReferenceTypeDesign)result;
            Assert.That(referenceType.InverseName, Is.Null);
        }

        /// <summary>
        /// Tests ToNodeDesign when InverseName has a value.
        /// Input: ReferenceTypeDesignJson with valid InverseName.
        /// Expected: Returns ReferenceTypeDesign with InverseName correctly set.
        /// </summary>
        [Test]
        public void ToNodeDesign_InverseNameHasValue_InverseNameIsSet()
        {
            // Arrange
            var inverseName = new LocalizedText { Key = "InverseKey", Value = "Inverse Name" };
            var json = new ReferenceTypeDesignJson(
                BrowseName: "TestReference",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                InverseName: inverseName,
                Symmetric: null);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<ReferenceTypeDesign>());
            var referenceType = (ReferenceTypeDesign)result;
            Assert.That(referenceType.InverseName, Is.EqualTo(inverseName));
        }

        /// <summary>
        /// Tests ToNodeDesign with base properties applied.
        /// Input: ReferenceTypeDesignJson with various base properties set.
        /// Expected: Returns ReferenceTypeDesign with base properties correctly applied.
        /// </summary>
        [Test]
        public void ToNodeDesign_WithBaseProperties_BasePropertiesAreApplied()
        {
            // Arrange
            var symbolicName = new XmlQualifiedName("SymbolicName", "http://test.com");
            var baseType = new XmlQualifiedName("BaseType", "http://test.com");
            var displayName = new LocalizedText { Key = "DisplayKey", Value = "Display Name" };
            var json = new ReferenceTypeDesignJson(
                BrowseName: "TestReference",
                DisplayName: displayName,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: symbolicName,
                SymbolicId: null,
                IsDeclaration: true,
                NumericId: 123,
                StringId: "StringId123",
                WriteAccess: 1,
                PartNo: 5,
                Category: "TestCategory",
                NotInAddressSpace: true,
                ReleaseStatus: ReleaseStatus.Draft,
                Purpose: DataTypePurpose.ServicesOnly,
                IsDynamic: true,
                NodeType: "ReferenceType",
                ClassName: "TestClassName",
                BaseType: baseType,
                IsAbstract: true,
                NoClassGeneration: true,
                InverseName: null,
                Symmetric: true);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<ReferenceTypeDesign>());
            var referenceType = (ReferenceTypeDesign)result;
            Assert.That(referenceType.BrowseName, Is.EqualTo("TestReference"));
            Assert.That(referenceType.DisplayName, Is.EqualTo(displayName));
            Assert.That(referenceType.SymbolicName, Is.EqualTo(symbolicName));
            Assert.That(referenceType.IsDeclaration, Is.True);
            Assert.That(referenceType.NumericId, Is.EqualTo(123));
            Assert.That(referenceType.NumericIdSpecified, Is.True);
            Assert.That(referenceType.StringId, Is.EqualTo("StringId123"));
            Assert.That(referenceType.WriteAccess, Is.EqualTo(1));
            Assert.That(referenceType.PartNo, Is.EqualTo(5));
            Assert.That(referenceType.Category, Is.EqualTo("TestCategory"));
            Assert.That(referenceType.NotInAddressSpace, Is.True);
            Assert.That(referenceType.ReleaseStatus, Is.EqualTo(ReleaseStatus.Draft));
            Assert.That(referenceType.Purpose, Is.EqualTo(DataTypePurpose.ServicesOnly));
            Assert.That(referenceType.IsDynamic, Is.True);
            Assert.That(referenceType.ClassName, Is.EqualTo("TestClassName"));
            Assert.That(referenceType.BaseType, Is.EqualTo(baseType));
            Assert.That(referenceType.IsAbstract, Is.True);
            Assert.That(referenceType.NoClassGeneration, Is.True);
        }

        /// <summary>
        /// Tests ToNodeDesign with all ReleaseStatus enum values.
        /// Input: ReferenceTypeDesignJson with each ReleaseStatus value.
        /// Expected: Returns ReferenceTypeDesign with correct ReleaseStatus value.
        /// </summary>
        [TestCase(ReleaseStatus.Released)]
        [TestCase(ReleaseStatus.Draft)]
        [TestCase(ReleaseStatus.Deprecated)]
        public void ToNodeDesign_WithDifferentReleaseStatus_ReleaseStatusIsSet(
            ReleaseStatus releaseStatus)
        {
            // Arrange
            var json = new ReferenceTypeDesignJson(
                BrowseName: "TestReference",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: releaseStatus,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                InverseName: null,
                Symmetric: null);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<ReferenceTypeDesign>());
            var referenceType = (ReferenceTypeDesign)result;
            Assert.That(referenceType.ReleaseStatus, Is.EqualTo(releaseStatus));
        }

        /// <summary>
        /// Tests ToNodeDesign with boundary values for uint properties.
        /// Input: ReferenceTypeDesignJson with uint.MinValue, uint.MaxValue, and 0 for uint properties.
        /// Expected: Returns ReferenceTypeDesign with correct uint values set.
        /// </summary>
        [TestCase(0u, 0u)]
        [TestCase(uint.MaxValue, uint.MaxValue)]
        [TestCase(123u, 456u)]
        public void ToNodeDesign_WithUintBoundaryValues_ValuesAreSet(uint writeAccess, uint partNo)
        {
            // Arrange
            var json = new ReferenceTypeDesignJson(
                BrowseName: "TestReference",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: writeAccess,
                PartNo: partNo,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                InverseName: null,
                Symmetric: null);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<ReferenceTypeDesign>());
            var referenceType = (ReferenceTypeDesign)result;
            Assert.That(referenceType.WriteAccess, Is.EqualTo(writeAccess));
            Assert.That(referenceType.PartNo, Is.EqualTo(partNo));
        }

        /// <summary>
        /// Tests ToNodeDesign with string boundary values.
        /// Input: ReferenceTypeDesignJson with null, empty, and whitespace string values.
        /// Expected: Returns ReferenceTypeDesign with string values correctly set.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("ValidString")]
        public void ToNodeDesign_WithStringBoundaryValues_StringsAreSet(string stringValue)
        {
            // Arrange
            var json = new ReferenceTypeDesignJson(
                BrowseName: stringValue,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: stringValue,
                WriteAccess: 0,
                PartNo: 0,
                Category: stringValue,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: stringValue,
                ClassName: stringValue,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                InverseName: null,
                Symmetric: null);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<ReferenceTypeDesign>());
            var referenceType = (ReferenceTypeDesign)result;
            Assert.That(referenceType.BrowseName, Is.EqualTo(stringValue));
            Assert.That(referenceType.StringId, Is.EqualTo(stringValue));
            Assert.That(referenceType.Category, Is.EqualTo(stringValue));
            Assert.That(referenceType.ClassName, Is.EqualTo(stringValue));
        }

        /// <summary>
        /// Tests ToNodeDesign with all null/default values.
        /// Input: PropertyDesignJson with all parameters null or default.
        /// Expected: Returns non-null PropertyDesign instance.
        /// </summary>
        [Test]
        public void ToNodeDesign_AllNullParameters_ReturnsPropertyDesign()
        {
            // Arrange
            var json = new PropertyDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                DefaultValue: null,
                DataType: null,
                ValueRank: null,
                ArrayDimensions: null,
                AccessLevel: null,
                InstanceAccessLevel: null,
                MinimumSamplingInterval: null,
                Historizing: null);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<PropertyDesign>());
        }

        /// <summary>
        /// Tests ToNodeDesign with populated string parameters.
        /// Input: PropertyDesignJson with BrowseName and StringId set.
        /// Expected: Returns PropertyDesign with applied values.
        /// </summary>
        [Test]
        public void ToNodeDesign_WithStringParameters_ReturnsPropertyDesignWithValues()
        {
            // Arrange
            const string browseName = "TestProperty";
            const string stringId = "TestId";
            var json = new PropertyDesignJson(
                BrowseName: browseName,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: stringId,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                DefaultValue: null,
                DataType: null,
                ValueRank: null,
                ArrayDimensions: null,
                AccessLevel: null,
                InstanceAccessLevel: null,
                MinimumSamplingInterval: null,
                Historizing: null);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<PropertyDesign>());
            Assert.That(result.BrowseName, Is.EqualTo(browseName));
            Assert.That(result.StringId, Is.EqualTo(stringId));
        }

        /// <summary>
        /// Tests ToNodeDesign with numeric ID parameters.
        /// Input: PropertyDesignJson with NumericId, WriteAccess, and PartNo set.
        /// Expected: Returns PropertyDesign with applied numeric values.
        /// </summary>
        [Test]
        public void ToNodeDesign_WithNumericParameters_ReturnsPropertyDesignWithValues()
        {
            // Arrange
            const uint numericId = 12345;
            const uint writeAccess = 1;
            const uint partNo = 5;
            var json = new PropertyDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: numericId,
                StringId: null,
                WriteAccess: writeAccess,
                PartNo: partNo,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                DefaultValue: null,
                DataType: null,
                ValueRank: null,
                ArrayDimensions: null,
                AccessLevel: null,
                InstanceAccessLevel: null,
                MinimumSamplingInterval: null,
                Historizing: null);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<PropertyDesign>());
            Assert.That(result.NumericId, Is.EqualTo(numericId));
            Assert.That(result.WriteAccess, Is.EqualTo(writeAccess));
            Assert.That(result.PartNo, Is.EqualTo(partNo));
        }

        /// <summary>
        /// Tests ToNodeDesign with boundary numeric values.
        /// Input: PropertyDesignJson with uint.MaxValue for numeric parameters.
        /// Expected: Returns PropertyDesign with maximum values applied.
        /// </summary>
        [Test]
        public void ToNodeDesign_WithMaxNumericValues_ReturnsPropertyDesign()
        {
            // Arrange
            var json = new PropertyDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: uint.MaxValue,
                StringId: null,
                WriteAccess: uint.MaxValue,
                PartNo: uint.MaxValue,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: uint.MaxValue,
                MaxCardinality: uint.MaxValue,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                DefaultValue: null,
                DataType: null,
                ValueRank: null,
                ArrayDimensions: null,
                AccessLevel: null,
                InstanceAccessLevel: null,
                MinimumSamplingInterval: null,
                Historizing: null);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<PropertyDesign>());
            Assert.That(result.NumericId, Is.EqualTo(uint.MaxValue));
            Assert.That(result.WriteAccess, Is.EqualTo(uint.MaxValue));
        }

        /// <summary>
        /// Tests ToNodeDesign with boolean parameters set to true.
        /// Input: PropertyDesignJson with IsDeclaration, NotInAddressSpace, IsDynamic, PreserveDefaultAttributes, and DesignToolOnly set to true.
        /// Expected: Returns PropertyDesign with boolean values applied.
        /// </summary>
        [Test]
        public void ToNodeDesign_WithBooleanParametersTrue_ReturnsPropertyDesignWithValues()
        {
            // Arrange
            var json = new PropertyDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: true,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: true,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: true,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: true,
                DesignToolOnly: true,
                DefaultValue: null,
                DataType: null,
                ValueRank: null,
                ArrayDimensions: null,
                AccessLevel: null,
                InstanceAccessLevel: null,
                MinimumSamplingInterval: null,
                Historizing: null);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<PropertyDesign>());
            Assert.That(result.IsDeclaration, Is.True);
            Assert.That(result.NotInAddressSpace, Is.True);
            Assert.That(result.IsDynamic, Is.True);
        }

        /// <summary>
        /// Tests ToNodeDesign with enum parameters.
        /// Input: PropertyDesignJson with various ReleaseStatus and DataTypePurpose values.
        /// Expected: Returns PropertyDesign with enum values applied.
        /// </summary>
        [TestCase(ReleaseStatus.Released, DataTypePurpose.Normal)]
        [TestCase(ReleaseStatus.Draft, DataTypePurpose.ServicesOnly)]
        [TestCase(ReleaseStatus.Deprecated, DataTypePurpose.CodeGenerator)]
        public void ToNodeDesign_WithEnumParameters_ReturnsPropertyDesignWithValues(
            ReleaseStatus releaseStatus,
            DataTypePurpose purpose)
        {
            // Arrange
            var json = new PropertyDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: releaseStatus,
                Purpose: purpose,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                DefaultValue: null,
                DataType: null,
                ValueRank: null,
                ArrayDimensions: null,
                AccessLevel: null,
                InstanceAccessLevel: null,
                MinimumSamplingInterval: null,
                Historizing: null);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<PropertyDesign>());
            Assert.That(result.ReleaseStatus, Is.EqualTo(releaseStatus));
            Assert.That(result.Purpose, Is.EqualTo(purpose));
        }

        /// <summary>
        /// Tests ToNodeDesign with nullable MinimumSamplingInterval parameter.
        /// Input: PropertyDesignJson with MinimumSamplingInterval set to various values.
        /// Expected: Returns PropertyDesign with MinimumSamplingInterval applied.
        /// </summary>
        [TestCase(0)]
        [TestCase(100)]
        [TestCase(int.MaxValue)]
        [TestCase(-1)]
        public void ToNodeDesign_WithMinimumSamplingInterval_ReturnsPropertyDesignWithValue(
            int interval)
        {
            // Arrange
            var json = new PropertyDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                DefaultValue: null,
                DataType: null,
                ValueRank: null,
                ArrayDimensions: null,
                AccessLevel: null,
                InstanceAccessLevel: null,
                MinimumSamplingInterval: interval,
                Historizing: null);

            // Act
            var result = json.ToNodeDesign();
            var variable = (PropertyDesign)result;

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<PropertyDesign>());
            Assert.That(variable.MinimumSamplingInterval, Is.EqualTo(interval));
            Assert.That(variable.MinimumSamplingIntervalSpecified, Is.True);
        }

        /// <summary>
        /// Tests ToNodeDesign with nullable Historizing parameter.
        /// Input: PropertyDesignJson with Historizing set to true and false.
        /// Expected: Returns PropertyDesign with Historizing applied.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void ToNodeDesign_WithHistorizing_ReturnsPropertyDesignWithValue(bool historizing)
        {
            // Arrange
            var json = new PropertyDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                DefaultValue: null,
                DataType: null,
                ValueRank: null,
                ArrayDimensions: null,
                AccessLevel: null,
                InstanceAccessLevel: null,
                MinimumSamplingInterval: null,
                Historizing: historizing);

            // Act
            var result = json.ToNodeDesign();
            var variable = (PropertyDesign)result;

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<PropertyDesign>());
            Assert.That(variable.Historizing, Is.EqualTo(historizing));
            Assert.That(variable.HistorizingSpecified, Is.True);
        }

        /// <summary>
        /// Tests ToNodeDesign with empty string parameters.
        /// Input: PropertyDesignJson with empty string values for string parameters.
        /// Expected: Returns PropertyDesign with empty strings applied.
        /// </summary>
        [Test]
        public void ToNodeDesign_WithEmptyStrings_ReturnsPropertyDesignWithEmptyStrings()
        {
            // Arrange
            var json = new PropertyDesignJson(
                BrowseName: string.Empty,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: string.Empty,
                WriteAccess: 0,
                PartNo: 0,
                Category: string.Empty,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: string.Empty,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                DefaultValue: null,
                DataType: null,
                ValueRank: null,
                ArrayDimensions: string.Empty,
                AccessLevel: null,
                InstanceAccessLevel: null,
                MinimumSamplingInterval: null,
                Historizing: null);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<PropertyDesign>());
            Assert.That(result.BrowseName, Is.EqualTo(string.Empty));
            Assert.That(result.StringId, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests ToNodeDesign with XmlQualifiedName parameters.
        /// Input: PropertyDesignJson with SymbolicName, SymbolicId, DataType set.
        /// Expected: Returns PropertyDesign with qualified names applied.
        /// </summary>
        [Test]
        public void ToNodeDesign_WithXmlQualifiedNames_ReturnsPropertyDesignWithValues()
        {
            // Arrange
            var symbolicName = new XmlQualifiedName("SymbolicName", "http://test.org");
            var symbolicId = new XmlQualifiedName("SymbolicId", "http://test.org");
            var dataType = new XmlQualifiedName("Int32", "http://opcfoundation.org/UA/");

            var json = new PropertyDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: symbolicName,
                SymbolicId: symbolicId,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                DefaultValue: null,
                DataType: dataType,
                ValueRank: null,
                ArrayDimensions: null,
                AccessLevel: null,
                InstanceAccessLevel: null,
                MinimumSamplingInterval: null,
                Historizing: null);

            // Act
            var result = json.ToNodeDesign();
            var variable = (PropertyDesign)result;

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<PropertyDesign>());
            Assert.That(result.SymbolicName, Is.EqualTo(symbolicName));
            Assert.That(result.SymbolicId, Is.EqualTo(symbolicId));
            Assert.That(variable.DataType, Is.EqualTo(dataType));
        }

        /// <summary>
        /// Tests ToNodeDesign with LocalizedText parameters.
        /// Input: PropertyDesignJson with DisplayName and Description set.
        /// Expected: Returns PropertyDesign with localized text applied.
        /// </summary>
        [Test]
        public void ToNodeDesign_WithLocalizedText_ReturnsPropertyDesignWithValues()
        {
            // Arrange
            var displayName = new LocalizedText { Key = "DisplayName", Value = "Test Display" };
            var description = new LocalizedText { Key = "Description", Value = "Test Description" };

            var json = new PropertyDesignJson(
                BrowseName: null,
                DisplayName: displayName,
                Description: description,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                DefaultValue: null,
                DataType: null,
                ValueRank: null,
                ArrayDimensions: null,
                AccessLevel: null,
                InstanceAccessLevel: null,
                MinimumSamplingInterval: null,
                Historizing: null);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<PropertyDesign>());
            Assert.That(result.DisplayName, Is.EqualTo(displayName));
            Assert.That(result.Description, Is.EqualTo(description));
        }

        /// <summary>
        /// Tests ToNodeDesign with NumericId set to null.
        /// Verifies that NumericIdSpecified is false when NumericId is null.
        /// </summary>
        [Test]
        public void ToNodeDesign_NumericIdNull_NumericIdSpecifiedIsFalse()
        {
            // Arrange
            var json = new InstanceDesignJson(
                BrowseName: "Test",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result.NumericIdSpecified, Is.False);
        }

        /// <summary>
        /// Tests ToNodeDesign with AccessRestrictions not set (null).
        /// Verifies that AccessRestrictionsSpecified is false when AccessRestrictions is null.
        /// </summary>
        [Test]
        public void ToNodeDesign_AccessRestrictionsNull_AccessRestrictionsSpecifiedIsFalse()
        {
            // Arrange
            var json = new InstanceDesignJson(
                BrowseName: "Test",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result.AccessRestrictionsSpecified, Is.False);
            Assert.That(result.DefaultAccessRestrictionsSpecified, Is.False);
        }

        /// <summary>
        /// Tests ToNodeDesign with all ReleaseStatus enum values.
        /// Verifies correct transfer of ReleaseStatus values.
        /// </summary>
        [TestCase(ReleaseStatus.Released)]
        [TestCase(ReleaseStatus.Draft)]
        [TestCase(ReleaseStatus.Deprecated)]
        public void ToNodeDesign_DifferentReleaseStatusValues_SetsReleaseStatusCorrectly(
            ReleaseStatus status)
        {
            // Arrange
            var json = new InstanceDesignJson(
                BrowseName: "Test",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: status,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result.ReleaseStatus, Is.EqualTo(status));
        }

        /// <summary>
        /// Tests ToNodeDesign with all DataTypePurpose enum values.
        /// Verifies correct transfer of DataTypePurpose values.
        /// </summary>
        [TestCase(DataTypePurpose.Normal)]
        [TestCase(DataTypePurpose.ServicesOnly)]
        [TestCase(DataTypePurpose.CodeGenerator)]
        public void ToNodeDesign_DifferentDataTypePurposeValues_SetsPurposeCorrectly(
            DataTypePurpose purpose)
        {
            // Arrange
            var json = new InstanceDesignJson(
                BrowseName: "Test",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: purpose,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result.Purpose, Is.EqualTo(purpose));
        }

        /// <summary>
        /// Tests ToNodeDesign with various string property values including edge cases.
        /// Verifies correct handling of null, empty, and special character strings.
        /// </summary>
        [TestCase(null, null, null)]
        [TestCase("", "", "")]
        [TestCase("   ", "   ", "   ")]
        [TestCase("Normal", "String123", "Category1")]
        [TestCase("Special!@#$%", "ID<>[]", "Cat&*()")]
        public void ToNodeDesign_VariousStringValues_HandlesCorrectly(
            string browseName,
            string stringId,
            string category)
        {
            // Arrange
            var json = new InstanceDesignJson(
                BrowseName: browseName,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: stringId,
                WriteAccess: 0,
                PartNo: 0,
                Category: category,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result.BrowseName, Is.EqualTo(browseName));
            Assert.That(result.StringId, Is.EqualTo(stringId));
            Assert.That(result.Category, Is.EqualTo(category));
        }

        /// <summary>
        /// Tests ToNodeDesign returns the correct derived type.
        /// Verifies the return type is InstanceDesign and can be cast properly.
        /// </summary>
        [Test]
        public void ToNodeDesign_ReturnType_IsInstanceDesign()
        {
            // Arrange
            var json = new InstanceDesignJson(
                BrowseName: "Test",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.TypeOf<InstanceDesign>());
            Assert.That(result, Is.InstanceOf<NodeDesign>());
            var instanceDesign = result as InstanceDesign;
            Assert.That(instanceDesign, Is.Not.Null);
        }

        /// <summary>
        /// Tests that ToInstanceDesign returns a non-null InstanceDesign object.
        /// Input: Default InstanceDesignJson with minimal properties.
        /// Expected: Method returns a valid InstanceDesign object.
        /// </summary>
        [Test]
        public void ToInstanceDesign_MinimalProperties_ReturnsNonNullInstance()
        {
            // Arrange
            var jsonRecord = new InstanceDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false);

            // Act
            var result = jsonRecord.ToInstanceDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<InstanceDesign>());
        }

        /// <summary>
        /// Tests that ToInstanceDesign correctly transfers all basic properties from InstanceDesignJson to InstanceDesign.
        /// Input: InstanceDesignJson with all basic properties populated.
        /// Expected: All properties are correctly transferred to the InstanceDesign object.
        /// </summary>
        [Test]
        public void ToInstanceDesign_AllPropertiesPopulated_TransfersAllProperties()
        {
            // Arrange
            const string browseNameValue = "TestBrowseName";
            var displayNameValue = new LocalizedText { Value = "Test Display Name" };
            var descriptionValue = new LocalizedText { Value = "Test Description" };
            var symbolicNameValue = new XmlQualifiedName("TestSymbolic", "http://test.com");
            var symbolicIdValue = new XmlQualifiedName("TestId", "http://test.com");
            const string stringIdValue = "TestStringId";
            const string categoryValue = "TestCategory";
            var referenceTypeValue = new XmlQualifiedName("RefType", "http://test.com");
            var declarationValue = new XmlQualifiedName("Declaration", "http://test.com");
            var typeDefinitionValue = new XmlQualifiedName("TypeDef", "http://test.com");

            var jsonRecord = new InstanceDesignJson(
                BrowseName: browseNameValue,
                DisplayName: displayNameValue,
                Description: descriptionValue,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: symbolicNameValue,
                SymbolicId: symbolicIdValue,
                IsDeclaration: true,
                NumericId: 100,
                StringId: stringIdValue,
                WriteAccess: 15,
                PartNo: 5,
                Category: categoryValue,
                NotInAddressSpace: true,
                ReleaseStatus: ReleaseStatus.Draft,
                Purpose: DataTypePurpose.CodeGenerator,
                IsDynamic: true,
                NodeType: "TestNodeType",
                ReferenceType: referenceTypeValue,
                Declaration: declarationValue,
                TypeDefinition: typeDefinitionValue,
                ModellingRule: ModellingRule.Mandatory,
                MinCardinality: 1,
                MaxCardinality: 10,
                PreserveDefaultAttributes: true,
                DesignToolOnly: true);

            // Act
            var result = jsonRecord.ToInstanceDesign();

            // Assert
            Assert.That(result.BrowseName, Is.EqualTo(browseNameValue));
            Assert.That(result.DisplayName, Is.EqualTo(displayNameValue));
            Assert.That(result.Description, Is.EqualTo(descriptionValue));
            Assert.That(result.SymbolicName, Is.EqualTo(symbolicNameValue));
            Assert.That(result.SymbolicId, Is.EqualTo(symbolicIdValue));
            Assert.That(result.IsDeclaration, Is.True);
            Assert.That(result.NumericId, Is.EqualTo(100));
            Assert.That(result.NumericIdSpecified, Is.True);
            Assert.That(result.StringId, Is.EqualTo(stringIdValue));
            Assert.That(result.WriteAccess, Is.EqualTo(15));
            Assert.That(result.PartNo, Is.EqualTo(5));
            Assert.That(result.Category, Is.EqualTo(categoryValue));
            Assert.That(result.NotInAddressSpace, Is.True);
            Assert.That(result.ReleaseStatus, Is.EqualTo(ReleaseStatus.Draft));
            Assert.That(result.Purpose, Is.EqualTo(DataTypePurpose.CodeGenerator));
            Assert.That(result.IsDynamic, Is.True);
            Assert.That(result.ReferenceType, Is.EqualTo(referenceTypeValue));
            Assert.That(result.Declaration, Is.EqualTo(declarationValue));
            Assert.That(result.TypeDefinition, Is.EqualTo(typeDefinitionValue));
            Assert.That(result.ModellingRule, Is.EqualTo(ModellingRule.Mandatory));
            Assert.That(result.ModellingRuleSpecified, Is.True);
            Assert.That(result.MinCardinality, Is.EqualTo(1));
            Assert.That(result.MaxCardinality, Is.EqualTo(10));
            Assert.That(result.PreserveDefaultAttributes, Is.True);
            Assert.That(result.DesignToolOnly, Is.True);
        }

        /// <summary>
        /// Tests that ToInstanceDesign correctly handles null ModellingRule.
        /// Input: InstanceDesignJson with ModellingRule set to null.
        /// Expected: ModellingRuleSpecified is false in the resulting InstanceDesign.
        /// </summary>
        [Test]
        public void ToInstanceDesign_NullModellingRule_ModellingRuleSpecifiedIsFalse()
        {
            // Arrange
            var jsonRecord = new InstanceDesignJson(
                BrowseName: "TestNode",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false);

            // Act
            var result = jsonRecord.ToInstanceDesign();

            // Assert
            Assert.That(result.ModellingRuleSpecified, Is.False);
        }

        /// <summary>
        /// Tests that ToInstanceDesign correctly handles ModellingRule with a value.
        /// Input: InstanceDesignJson with ModellingRule set to a specific value.
        /// Expected: ModellingRuleSpecified is true and the value is correctly transferred.
        /// </summary>
        [TestCase(ModellingRule.Mandatory)]
        [TestCase(ModellingRule.Optional)]
        [TestCase(ModellingRule.MandatoryPlaceholder)]
        [TestCase(ModellingRule.OptionalPlaceholder)]
        [TestCase(ModellingRule.ExposesItsArray)]
        public void ToInstanceDesign_ModellingRuleWithValue_SetsModellingRuleSpecifiedTrue(
            ModellingRule modellingRule)
        {
            // Arrange
            var jsonRecord = new InstanceDesignJson(
                BrowseName: "TestNode",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: modellingRule,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false);

            // Act
            var result = jsonRecord.ToInstanceDesign();

            // Assert
            Assert.That(result.ModellingRuleSpecified, Is.True);
            Assert.That(result.ModellingRule, Is.EqualTo(modellingRule));
        }

        /// <summary>
        /// Tests that ToInstanceDesign correctly handles boundary values for MinCardinality and MaxCardinality.
        /// Input: InstanceDesignJson with MinCardinality and MaxCardinality set to boundary values.
        /// Expected: Values are correctly transferred to the InstanceDesign object.
        /// </summary>
        [TestCase(0u, 0u)]
        [TestCase(uint.MaxValue, uint.MaxValue)]
        [TestCase(0u, uint.MaxValue)]
        [TestCase(1u, 100u)]
        public void ToInstanceDesign_CardinalityBoundaryValues_TransfersCorrectly(
            uint minCardinality,
            uint maxCardinality)
        {
            // Arrange
            var jsonRecord = new InstanceDesignJson(
                BrowseName: "TestNode",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: minCardinality,
                MaxCardinality: maxCardinality,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false);

            // Act
            var result = jsonRecord.ToInstanceDesign();

            // Assert
            Assert.That(result.MinCardinality, Is.EqualTo(minCardinality));
            Assert.That(result.MaxCardinality, Is.EqualTo(maxCardinality));
        }

        /// <summary>
        /// Tests that ToInstanceDesign correctly handles boundary values for WriteAccess and PartNo.
        /// Input: InstanceDesignJson with WriteAccess and PartNo set to boundary values.
        /// Expected: Values are correctly transferred to the InstanceDesign object.
        /// </summary>
        [TestCase(0u, 0u)]
        [TestCase(uint.MaxValue, uint.MaxValue)]
        [TestCase(255u, 12345u)]
        public void ToInstanceDesign_WriteAccessAndPartNoBoundaryValues_TransfersCorrectly(
            uint writeAccess,
            uint partNo)
        {
            // Arrange
            var jsonRecord = new InstanceDesignJson(
                BrowseName: "TestNode",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: writeAccess,
                PartNo: partNo,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false);

            // Act
            var result = jsonRecord.ToInstanceDesign();

            // Assert
            Assert.That(result.WriteAccess, Is.EqualTo(writeAccess));
            Assert.That(result.PartNo, Is.EqualTo(partNo));
        }

        /// <summary>
        /// Tests that ToInstanceDesign correctly handles NumericId with and without a value.
        /// Input: InstanceDesignJson with NumericId set to null or specific values.
        /// Expected: NumericIdSpecified is set correctly based on whether NumericId has a value.
        /// </summary>
        [TestCase(null, false)]
        [TestCase(0u, true)]
        [TestCase(12345u, true)]
        [TestCase(uint.MaxValue, true)]
        public void ToInstanceDesign_NumericIdVariations_SetsNumericIdSpecifiedCorrectly(
            uint? numericId,
            bool expectedSpecified)
        {
            // Arrange
            var jsonRecord = new InstanceDesignJson(
                BrowseName: "TestNode",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: numericId,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false);

            // Act
            var result = jsonRecord.ToInstanceDesign();

            // Assert
            Assert.That(result.NumericIdSpecified, Is.EqualTo(expectedSpecified));
            if (numericId.HasValue)
            {
                Assert.That(result.NumericId, Is.EqualTo(numericId.Value));
            }
        }

        /// <summary>
        /// Tests that ToInstanceDesign correctly handles boolean properties with different combinations.
        /// Input: InstanceDesignJson with various boolean property values.
        /// Expected: Boolean properties are correctly transferred to the InstanceDesign object.
        /// </summary>
        [TestCase(true, true, true, true, true)]
        [TestCase(false, false, false, false, false)]
        [TestCase(true, false, true, false, true)]
        [TestCase(false, true, false, true, false)]
        public void ToInstanceDesign_BooleanPropertyCombinations_TransfersCorrectly(
            bool isDeclaration,
            bool notInAddressSpace,
            bool isDynamic,
            bool preserveDefaultAttributes,
            bool designToolOnly)
        {
            // Arrange
            var jsonRecord = new InstanceDesignJson(
                BrowseName: "TestNode",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: isDeclaration,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: notInAddressSpace,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: isDynamic,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: preserveDefaultAttributes,
                DesignToolOnly: designToolOnly);

            // Act
            var result = jsonRecord.ToInstanceDesign();

            // Assert
            Assert.That(result.IsDeclaration, Is.EqualTo(isDeclaration));
            Assert.That(result.NotInAddressSpace, Is.EqualTo(notInAddressSpace));
            Assert.That(result.IsDynamic, Is.EqualTo(isDynamic));
            Assert.That(result.PreserveDefaultAttributes, Is.EqualTo(preserveDefaultAttributes));
            Assert.That(result.DesignToolOnly, Is.EqualTo(designToolOnly));
        }

        /// <summary>
        /// Tests that ToInstanceDesign correctly handles all ReleaseStatus enum values.
        /// Input: InstanceDesignJson with different ReleaseStatus values.
        /// Expected: ReleaseStatus is correctly transferred to the InstanceDesign object.
        /// </summary>
        [TestCase(ReleaseStatus.Released)]
        [TestCase(ReleaseStatus.Draft)]
        [TestCase(ReleaseStatus.Deprecated)]
        public void ToInstanceDesign_ReleaseStatusValues_TransfersCorrectly(ReleaseStatus releaseStatus)
        {
            // Arrange
            var jsonRecord = new InstanceDesignJson(
                BrowseName: "TestNode",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: releaseStatus,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false);

            // Act
            var result = jsonRecord.ToInstanceDesign();

            // Assert
            Assert.That(result.ReleaseStatus, Is.EqualTo(releaseStatus));
        }

        /// <summary>
        /// Tests that ToInstanceDesign correctly handles all DataTypePurpose enum values.
        /// Input: InstanceDesignJson with different DataTypePurpose values.
        /// Expected: Purpose is correctly transferred to the InstanceDesign object.
        /// </summary>
        [TestCase(DataTypePurpose.Normal)]
        [TestCase(DataTypePurpose.ServicesOnly)]
        [TestCase(DataTypePurpose.CodeGenerator)]
        public void ToInstanceDesign_DataTypePurposeValues_TransfersCorrectly(DataTypePurpose purpose)
        {
            // Arrange
            var jsonRecord = new InstanceDesignJson(
                BrowseName: "TestNode",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: purpose,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false);

            // Act
            var result = jsonRecord.ToInstanceDesign();

            // Assert
            Assert.That(result.Purpose, Is.EqualTo(purpose));
        }

        /// <summary>
        /// Tests that ToInstanceDesign correctly handles null string properties.
        /// Input: InstanceDesignJson with null string properties.
        /// Expected: Null string properties are correctly transferred to the InstanceDesign object.
        /// </summary>
        [Test]
        public void ToInstanceDesign_NullStringProperties_TransfersCorrectly()
        {
            // Arrange
            var jsonRecord = new InstanceDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false);

            // Act
            var result = jsonRecord.ToInstanceDesign();

            // Assert
            Assert.That(result.BrowseName, Is.Null);
            Assert.That(result.StringId, Is.Null);
            Assert.That(result.Category, Is.Null);
        }

        /// <summary>
        /// Tests that ToInstanceDesign correctly handles empty and whitespace string properties.
        /// Input: InstanceDesignJson with empty and whitespace string properties.
        /// Expected: String properties are correctly transferred to the InstanceDesign object.
        /// </summary>
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("   ")]
        [TestCase("\t")]
        [TestCase("\n")]
        public void ToInstanceDesign_EmptyAndWhitespaceStrings_TransfersCorrectly(string value)
        {
            // Arrange
            var jsonRecord = new InstanceDesignJson(
                BrowseName: value,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: value,
                WriteAccess: 0,
                PartNo: 0,
                Category: value,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: value,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false);

            // Act
            var result = jsonRecord.ToInstanceDesign();

            // Assert
            Assert.That(result.BrowseName, Is.EqualTo(value));
            Assert.That(result.StringId, Is.EqualTo(value));
            Assert.That(result.Category, Is.EqualTo(value));
        }

        /// <summary>
        /// Tests that ToInstanceDesign correctly handles null XmlQualifiedName properties.
        /// Input: InstanceDesignJson with null XmlQualifiedName properties.
        /// Expected: Null XmlQualifiedName properties are correctly transferred to the InstanceDesign object.
        /// </summary>
        [Test]
        public void ToInstanceDesign_NullXmlQualifiedNameProperties_TransfersCorrectly()
        {
            // Arrange
            var jsonRecord = new InstanceDesignJson(
                BrowseName: "TestNode",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false);

            // Act
            var result = jsonRecord.ToInstanceDesign();

            // Assert
            Assert.That(result.SymbolicName, Is.Null);
            Assert.That(result.SymbolicId, Is.Null);
            Assert.That(result.ReferenceType, Is.Null);
            Assert.That(result.Declaration, Is.Null);
            Assert.That(result.TypeDefinition, Is.Null);
        }

        /// <summary>
        /// Tests that ToInstanceDesign correctly handles null array properties.
        /// Input: InstanceDesignJson with null array properties (References, Extensions).
        /// Expected: Null array properties are correctly transferred to the InstanceDesign object.
        /// </summary>
        [Test]
        public void ToInstanceDesign_NullArrayProperties_TransfersCorrectly()
        {
            // Arrange
            var jsonRecord = new InstanceDesignJson(
                BrowseName: "TestNode",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false);

            // Act
            var result = jsonRecord.ToInstanceDesign();

            // Assert
            Assert.That(result.References, Is.Null);
            Assert.That(result.Extensions, Is.Null);
        }

        /// <summary>
        /// Tests that ToInstanceDesign correctly handles empty arrays.
        /// Input: InstanceDesignJson with empty array properties.
        /// Expected: Empty arrays are correctly transferred to the InstanceDesign object.
        /// </summary>
        [Test]
        public void ToInstanceDesign_EmptyArrayProperties_TransfersCorrectly()
        {
            // Arrange
            Reference[] emptyReferences = [];
            XmlElement[] emptyExtensions = [];

            var jsonRecord = new InstanceDesignJson(
                BrowseName: "TestNode",
                DisplayName: null,
                Description: null,
                Children: null,
                References: emptyReferences,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: emptyExtensions,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false);

            // Act
            var result = jsonRecord.ToInstanceDesign();

            // Assert
            Assert.That(result.References, Is.Not.Null);
            Assert.That(result.References, Is.Empty);
            Assert.That(result.Extensions, Is.Not.Null);
            Assert.That(result.Extensions, Is.Empty);
        }

        /// <summary>
        /// Tests ToNodeDesign with minimal record containing only default values.
        /// Verifies that the method returns a valid ObjectDesign instance with proper type.
        /// Expected: Returns non-null ObjectDesign instance.
        /// </summary>
        [Test]
        public void ToNodeDesign_MinimalRecord_ReturnsObjectDesignInstance()
        {
            // Arrange
            var json = new ObjectDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                SupportsEvents: null);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.TypeOf<ObjectDesign>());
        }

        /// <summary>
        /// Tests ToNodeDesign with SupportsEvents set to true.
        /// Verifies that SupportsEvents and SupportsEventsSpecified are correctly set.
        /// Expected: Returns ObjectDesign with SupportsEvents = true and SupportsEventsSpecified = true.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void ToNodeDesign_WithSupportsEventsValue_SetsSupportsEventsCorrectly(bool supportsEventsValue)
        {
            // Arrange
            var json = new ObjectDesignJson(
                BrowseName: "TestObject",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                SupportsEvents: supportsEventsValue);

            // Act
            var result = json.ToNodeDesign();
            var objectDesign = (ObjectDesign)result;

            // Assert
            Assert.That(objectDesign.SupportsEvents, Is.EqualTo(supportsEventsValue));
            Assert.That(objectDesign.SupportsEventsSpecified, Is.True);
        }

        /// <summary>
        /// Tests ToNodeDesign with SupportsEvents set to null.
        /// Verifies that SupportsEventsSpecified is not set when SupportsEvents is null.
        /// Expected: Returns ObjectDesign with SupportsEventsSpecified = false.
        /// </summary>
        [Test]
        public void ToNodeDesign_WithSupportsEventsNull_DoesNotSetSupportsEventsSpecified()
        {
            // Arrange
            var json = new ObjectDesignJson(
                BrowseName: "TestObject",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                SupportsEvents: null);

            // Act
            var result = json.ToNodeDesign();
            var objectDesign = (ObjectDesign)result;

            // Assert
            Assert.That(objectDesign.SupportsEventsSpecified, Is.False);
        }

        /// <summary>
        /// Tests ToNodeDesign with fully populated record containing all properties.
        /// Verifies that all properties are correctly transferred to ObjectDesign instance.
        /// Expected: Returns ObjectDesign with all properties correctly set.
        /// </summary>
        [Test]
        public void ToNodeDesign_WithAllPropertiesPopulated_TransfersAllProperties()
        {
            // Arrange
            var displayName = new LocalizedText { Key = "DisplayKey", Value = "Display Value" };
            var description = new LocalizedText { Key = "DescKey", Value = "Description Value" };
            var symbolicName = new XmlQualifiedName("SymbolicName", "urn:test");
            var symbolicId = new XmlQualifiedName("SymbolicId", "urn:test");
            var referenceType = new XmlQualifiedName("RefType", "urn:test");
            var declaration = new XmlQualifiedName("Declaration", "urn:test");
            var typeDefinition = new XmlQualifiedName("TypeDef", "urn:test");
            XmlElement extension = new XmlDocument().CreateElement("Extension");
            var reference = new Reference { IsInverse = false, ReferenceType = referenceType, TargetId = symbolicName };

            var json = new ObjectDesignJson(
                BrowseName: "TestObject",
                DisplayName: displayName,
                Description: description,
                Children: null,
                References: [reference],
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: AccessRestrictions.EncryptionRequired,
                DefaultAccessRestrictions: AccessRestrictions.SigningRequired,
                Extensions: [extension],
                SymbolicName: symbolicName,
                SymbolicId: symbolicId,
                IsDeclaration: true,
                NumericId: 12345,
                StringId: "StringId123",
                WriteAccess: 1,
                PartNo: 2,
                Category: "TestCategory",
                NotInAddressSpace: true,
                ReleaseStatus: ReleaseStatus.Draft,
                Purpose: DataTypePurpose.ServicesOnly,
                IsDynamic: true,
                NodeType: "ObjectType",
                ReferenceType: referenceType,
                Declaration: declaration,
                TypeDefinition: typeDefinition,
                ModellingRule: ModellingRule.Mandatory,
                MinCardinality: 1,
                MaxCardinality: 10,
                PreserveDefaultAttributes: true,
                DesignToolOnly: true,
                SupportsEvents: true);

            // Act
            var result = json.ToNodeDesign();
            var objectDesign = (ObjectDesign)result;

            // Assert
            Assert.That(objectDesign.BrowseName, Is.EqualTo("TestObject"));
            Assert.That(objectDesign.DisplayName, Is.EqualTo(displayName));
            Assert.That(objectDesign.Description, Is.EqualTo(description));
            Assert.That(objectDesign.References, Is.Not.Null);
            Assert.That(objectDesign.References.Length, Is.EqualTo(1));
            Assert.That(objectDesign.References[0], Is.EqualTo(reference));
            Assert.That(objectDesign.AccessRestrictions, Is.EqualTo(AccessRestrictions.EncryptionRequired));
            Assert.That(objectDesign.AccessRestrictionsSpecified, Is.True);
            Assert.That(objectDesign.DefaultAccessRestrictions, Is.EqualTo(AccessRestrictions.SigningRequired));
            Assert.That(objectDesign.DefaultAccessRestrictionsSpecified, Is.True);
            Assert.That(objectDesign.Extensions, Is.Not.Null);
            Assert.That(objectDesign.Extensions.Length, Is.EqualTo(1));
            Assert.That(objectDesign.SymbolicName, Is.EqualTo(symbolicName));
            Assert.That(objectDesign.SymbolicId, Is.EqualTo(symbolicId));
            Assert.That(objectDesign.IsDeclaration, Is.True);
            Assert.That(objectDesign.NumericId, Is.EqualTo(12345));
            Assert.That(objectDesign.NumericIdSpecified, Is.True);
            Assert.That(objectDesign.StringId, Is.EqualTo("StringId123"));
            Assert.That(objectDesign.WriteAccess, Is.EqualTo(1));
            Assert.That(objectDesign.PartNo, Is.EqualTo(2));
            Assert.That(objectDesign.Category, Is.EqualTo("TestCategory"));
            Assert.That(objectDesign.NotInAddressSpace, Is.True);
            Assert.That(objectDesign.ReleaseStatus, Is.EqualTo(ReleaseStatus.Draft));
            Assert.That(objectDesign.Purpose, Is.EqualTo(DataTypePurpose.ServicesOnly));
            Assert.That(objectDesign.IsDynamic, Is.True);
            Assert.That(objectDesign.ReferenceType, Is.EqualTo(referenceType));
            Assert.That(objectDesign.Declaration, Is.EqualTo(declaration));
            Assert.That(objectDesign.TypeDefinition, Is.EqualTo(typeDefinition));
            Assert.That(objectDesign.ModellingRule, Is.EqualTo(ModellingRule.Mandatory));
            Assert.That(objectDesign.ModellingRuleSpecified, Is.True);
            Assert.That(objectDesign.MinCardinality, Is.EqualTo(1));
            Assert.That(objectDesign.MaxCardinality, Is.EqualTo(10));
            Assert.That(objectDesign.PreserveDefaultAttributes, Is.True);
            Assert.That(objectDesign.DesignToolOnly, Is.True);
            Assert.That(objectDesign.SupportsEvents, Is.True);
            Assert.That(objectDesign.SupportsEventsSpecified, Is.True);
        }

        /// <summary>
        /// Tests ToNodeDesign with boundary values for uint properties.
        /// Verifies that maximum and minimum uint values are handled correctly.
        /// Expected: Returns ObjectDesign with correct boundary values.
        /// </summary>
        [TestCase(uint.MinValue, uint.MinValue)]
        [TestCase(uint.MaxValue, uint.MaxValue)]
        [TestCase(0u, uint.MaxValue)]
        [TestCase(uint.MaxValue, 0u)]
        public void ToNodeDesign_WithBoundaryUintValues_HandlesCorrectly(uint writeAccess, uint partNo)
        {
            // Arrange
            var json = new ObjectDesignJson(
                BrowseName: "TestObject",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: writeAccess,
                PartNo: partNo,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: uint.MinValue,
                MaxCardinality: uint.MaxValue,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                SupportsEvents: null);

            // Act
            var result = json.ToNodeDesign();
            var objectDesign = (ObjectDesign)result;

            // Assert
            Assert.That(objectDesign.WriteAccess, Is.EqualTo(writeAccess));
            Assert.That(objectDesign.PartNo, Is.EqualTo(partNo));
            Assert.That(objectDesign.MinCardinality, Is.EqualTo(uint.MinValue));
            Assert.That(objectDesign.MaxCardinality, Is.EqualTo(uint.MaxValue));
        }

        /// <summary>
        /// Tests ToNodeDesign with empty and whitespace strings.
        /// Verifies that empty and whitespace string values are handled correctly.
        /// Expected: Returns ObjectDesign with empty/whitespace strings preserved.
        /// </summary>
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("\t\n")]
        public void ToNodeDesign_WithEmptyOrWhitespaceStrings_HandlesCorrectly(string stringValue)
        {
            // Arrange
            var json = new ObjectDesignJson(
                BrowseName: stringValue,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: stringValue,
                WriteAccess: 0,
                PartNo: 0,
                Category: stringValue,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: stringValue,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                SupportsEvents: null);

            // Act
            var result = json.ToNodeDesign();
            var objectDesign = (ObjectDesign)result;

            // Assert
            Assert.That(objectDesign.BrowseName, Is.EqualTo(stringValue));
            Assert.That(objectDesign.StringId, Is.EqualTo(stringValue));
            Assert.That(objectDesign.Category, Is.EqualTo(stringValue));
        }

        /// <summary>
        /// Tests ToNodeDesign with all enum values for ReleaseStatus.
        /// Verifies that all ReleaseStatus enum values are handled correctly.
        /// Expected: Returns ObjectDesign with correct ReleaseStatus value.
        /// </summary>
        [TestCase(ReleaseStatus.Released)]
        [TestCase(ReleaseStatus.Draft)]
        [TestCase(ReleaseStatus.Deprecated)]
        public void ToNodeDesign_WithAllReleaseStatusValues_HandlesCorrectly(ReleaseStatus releaseStatus)
        {
            // Arrange
            var json = new ObjectDesignJson(
                BrowseName: "TestObject",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: releaseStatus,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                SupportsEvents: null);

            // Act
            var result = json.ToNodeDesign();
            var objectDesign = (ObjectDesign)result;

            // Assert
            Assert.That(objectDesign.ReleaseStatus, Is.EqualTo(releaseStatus));
        }

        /// <summary>
        /// Tests ToNodeDesign with all enum values for DataTypePurpose.
        /// Verifies that all DataTypePurpose enum values are handled correctly.
        /// Expected: Returns ObjectDesign with correct DataTypePurpose value.
        /// </summary>
        [TestCase(DataTypePurpose.Normal)]
        [TestCase(DataTypePurpose.ServicesOnly)]
        [TestCase(DataTypePurpose.CodeGenerator)]
        public void ToNodeDesign_WithAllDataTypePurposeValues_HandlesCorrectly(DataTypePurpose purpose)
        {
            // Arrange
            var json = new ObjectDesignJson(
                BrowseName: "TestObject",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: purpose,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                SupportsEvents: null);

            // Act
            var result = json.ToNodeDesign();
            var objectDesign = (ObjectDesign)result;

            // Assert
            Assert.That(objectDesign.Purpose, Is.EqualTo(purpose));
        }

        /// <summary>
        /// Tests ToNodeDesign with all enum values for ModellingRule.
        /// Verifies that all ModellingRule enum values are handled correctly including null.
        /// Expected: Returns ObjectDesign with correct ModellingRule value and specification flag.
        /// </summary>
        [TestCase(ModellingRule.Mandatory)]
        [TestCase(ModellingRule.Optional)]
        [TestCase(ModellingRule.ExposesItsArray)]
        [TestCase(ModellingRule.MandatoryPlaceholder)]
        [TestCase(ModellingRule.OptionalPlaceholder)]
        public void ToNodeDesign_WithAllModellingRuleValues_HandlesCorrectly(ModellingRule modellingRule)
        {
            // Arrange
            var json = new ObjectDesignJson(
                BrowseName: "TestObject",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: modellingRule,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                SupportsEvents: null);

            // Act
            var result = json.ToNodeDesign();
            var objectDesign = (ObjectDesign)result;

            // Assert
            Assert.That(objectDesign.ModellingRule, Is.EqualTo(modellingRule));
            Assert.That(objectDesign.ModellingRuleSpecified, Is.True);
        }

        /// <summary>
        /// Tests ToNodeDesign with empty arrays for collection properties.
        /// Verifies that empty arrays are correctly transferred to ObjectDesign.
        /// Expected: Returns ObjectDesign with empty arrays preserved.
        /// </summary>
        [Test]
        public void ToNodeDesign_WithEmptyArrays_HandlesCorrectly()
        {
            // Arrange
            var json = new ObjectDesignJson(
                BrowseName: "TestObject",
                DisplayName: null,
                Description: null,
                Children: null,
                References: [],
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: [],
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                SupportsEvents: null);

            // Act
            var result = json.ToNodeDesign();
            var objectDesign = (ObjectDesign)result;

            // Assert
            Assert.That(objectDesign.References, Is.Not.Null);
            Assert.That(objectDesign.References, Is.Empty);
            Assert.That(objectDesign.Extensions, Is.Not.Null);
            Assert.That(objectDesign.Extensions, Is.Empty);
        }

        /// <summary>
        /// Tests that ToInstanceDesign returns an ObjectDesign instance that is also an InstanceDesign.
        /// Input: ObjectDesignJson with all null/default values.
        /// Expected: Returns a valid ObjectDesign instance that is also an InstanceDesign.
        /// </summary>
        [Test]
        public void ToInstanceDesign_AllNullValues_ReturnsObjectDesignInstance()
        {
            // Arrange
            var objectDesignJson = new ObjectDesignJson(
                null, null, null, null, null, null, null, null, null, null, null, null,
                false, null, null, 0, 0, null, false, ReleaseStatus.Released, DataTypePurpose.Normal,
                false, null, null, null, null, null, 0, 0, false, false, null);

            // Act
            var result = objectDesignJson.ToInstanceDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<ObjectDesign>());
            Assert.That(result, Is.InstanceOf<InstanceDesign>());
        }

        /// <summary>
        /// Tests that ToInstanceDesign returns the same instance as ToNodeDesign.
        /// Input: ObjectDesignJson with minimal values.
        /// Expected: ToInstanceDesign and ToNodeDesign return the same instance.
        /// </summary>
        [Test]
        public void ToInstanceDesign_MinimalValues_ReturnsSameInstanceAsToNodeDesign()
        {
            // Arrange
            var objectDesignJson = new ObjectDesignJson(
                null, null, null, null, null, null, null, null, null, null, null, null,
                false, null, null, 0, 0, null, false, ReleaseStatus.Released, DataTypePurpose.Normal,
                false, null, null, null, null, null, 0, 0, false, false, null);

            // Act
            var resultFromToInstanceDesign = objectDesignJson.ToInstanceDesign();
            var resultFromToNodeDesign = objectDesignJson.ToNodeDesign();

            // Assert
            Assert.That(resultFromToNodeDesign, Is.EqualTo(resultFromToInstanceDesign));
        }

        /// <summary>
        /// Tests that ToInstanceDesign correctly handles SupportsEvents set to true.
        /// Input: ObjectDesignJson with SupportsEvents set to true.
        /// Expected: Returns ObjectDesign with SupportsEvents property set to true.
        /// </summary>
        [Test]
        public void ToInstanceDesign_SupportsEventsTrue_SetsPropertyCorrectly()
        {
            // Arrange
            var objectDesignJson = new ObjectDesignJson(
                "TestObject", null, null, null, null, null, null, null, null, null, null, null,
                false, null, null, 0, 0, null, false, ReleaseStatus.Released, DataTypePurpose.Normal,
                false, null, null, null, null, null, 0, 0, false, false, true);

            // Act
            var result = (ObjectDesign)objectDesignJson.ToInstanceDesign();

            // Assert
            Assert.That(result.SupportsEvents, Is.True);
            Assert.That(result.SupportsEventsSpecified, Is.True);
        }

        /// <summary>
        /// Tests that ToInstanceDesign correctly handles SupportsEvents set to false.
        /// Input: ObjectDesignJson with SupportsEvents set to false.
        /// Expected: Returns ObjectDesign with SupportsEvents property set to false.
        /// </summary>
        [Test]
        public void ToInstanceDesign_SupportsEventsFalse_SetsPropertyCorrectly()
        {
            // Arrange
            var objectDesignJson = new ObjectDesignJson(
                "TestObject", null, null, null, null, null, null, null, null, null, null, null,
                false, null, null, 0, 0, null, false, ReleaseStatus.Released, DataTypePurpose.Normal,
                false, null, null, null, null, null, 0, 0, false, false, false);

            // Act
            var result = (ObjectDesign)objectDesignJson.ToInstanceDesign();

            // Assert
            Assert.That(result.SupportsEvents, Is.False);
            Assert.That(result.SupportsEventsSpecified, Is.True);
        }

        /// <summary>
        /// Tests that ToInstanceDesign correctly handles SupportsEvents set to null.
        /// Input: ObjectDesignJson with SupportsEvents set to null.
        /// Expected: Returns ObjectDesign with SupportsEventsSpecified set to false.
        /// </summary>
        [Test]
        public void ToInstanceDesign_SupportsEventsNull_DoesNotSetProperty()
        {
            // Arrange
            var objectDesignJson = new ObjectDesignJson(
                "TestObject", null, null, null, null, null, null, null, null, null, null, null,
                false, null, null, 0, 0, null, false, ReleaseStatus.Released, DataTypePurpose.Normal,
                false, null, null, null, null, null, 0, 0, false, false, null);

            // Act
            var result = (ObjectDesign)objectDesignJson.ToInstanceDesign();

            // Assert
            Assert.That(result.SupportsEventsSpecified, Is.False);
        }

        /// <summary>
        /// Tests that ToInstanceDesign correctly handles all non-null values.
        /// Input: ObjectDesignJson with all values populated.
        /// Expected: Returns ObjectDesign with BrowseName correctly set.
        /// </summary>
        [Test]
        public void ToInstanceDesign_AllValuesPopulated_ReturnsProperlyConfiguredObjectDesign()
        {
            // Arrange
            var displayName = new LocalizedText { Value = "Display Name", Key = "en" };
            var description = new LocalizedText { Value = "Description", Key = "en" };
            var symbolicName = new XmlQualifiedName("SymbolicName", "http://test.com");
            var symbolicId = new XmlQualifiedName("SymbolicId", "http://test.com");
            var referenceType = new XmlQualifiedName("ReferenceType", "http://test.com");
            var declaration = new XmlQualifiedName("Declaration", "http://test.com");
            var typeDefinition = new XmlQualifiedName("TypeDefinition", "http://test.com");

            var objectDesignJson = new ObjectDesignJson(
                "TestBrowseName", displayName, description, null, null, null, null, null, null, null,
                symbolicName, symbolicId, true, 123, "StringId123", 1, 2, "Category1", true,
                ReleaseStatus.Draft, DataTypePurpose.ServicesOnly, true, "ObjectType",
                referenceType, declaration, typeDefinition, ModellingRule.Mandatory, 1, 5, true, true, true);

            // Act
            var result = (ObjectDesign)objectDesignJson.ToInstanceDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<ObjectDesign>());
            Assert.That(result, Is.InstanceOf<InstanceDesign>());
            Assert.That(result.BrowseName, Is.EqualTo("TestBrowseName"));
            Assert.That(result.SupportsEvents, Is.True);
            Assert.That(result.SupportsEventsSpecified, Is.True);
        }

        /// <summary>
        /// Tests that ToInstanceDesign with boundary values for numeric fields.
        /// Input: ObjectDesignJson with uint.MaxValue and uint.MinValue for cardinality fields.
        /// Expected: Returns ObjectDesign with correct cardinality values.
        /// </summary>
        [Test]
        public void ToInstanceDesign_BoundaryNumericValues_HandlesCorrectly()
        {
            // Arrange
            var objectDesignJson = new ObjectDesignJson(
                "Test", null, null, null, null, null, null, null, null, null, null, null,
                false, uint.MaxValue, null, uint.MaxValue, uint.MaxValue, null, false,
                ReleaseStatus.Released, DataTypePurpose.Normal, false, null, null, null, null, null,
                uint.MinValue, uint.MaxValue, false, false, null);

            // Act
            var result = (ObjectDesign)objectDesignJson.ToInstanceDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.MinCardinality, Is.EqualTo(uint.MinValue));
            Assert.That(result.MaxCardinality, Is.EqualTo(uint.MaxValue));
        }

        /// <summary>
        /// Tests that ToInstanceDesign handles empty string values correctly.
        /// Input: ObjectDesignJson with empty strings for string fields.
        /// Expected: Returns ObjectDesign with empty string values preserved.
        /// </summary>
        [Test]
        public void ToInstanceDesign_EmptyStringValues_HandlesCorrectly()
        {
            // Arrange
            var objectDesignJson = new ObjectDesignJson(
                string.Empty, null, null, null, null, null, null, null, null, null, null, null,
                false, null, string.Empty, 0, 0, string.Empty, false, ReleaseStatus.Released,
                DataTypePurpose.Normal, false, string.Empty, null, null, null, null, 0, 0, false, false, null);

            // Act
            var result = (ObjectDesign)objectDesignJson.ToInstanceDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.BrowseName, Is.EqualTo(string.Empty));
            Assert.That(result.StringId, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests that ToInstanceDesign handles all enum values correctly.
        /// Input: ObjectDesignJson with all possible ModellingRule enum values.
        /// Expected: Returns ObjectDesign with correct ModellingRule value.
        /// </summary>
        [TestCase(ModellingRule.Mandatory)]
        [TestCase(ModellingRule.Optional)]
        [TestCase(ModellingRule.ExposesItsArray)]
        [TestCase(ModellingRule.MandatoryPlaceholder)]
        [TestCase(ModellingRule.OptionalPlaceholder)]
        public void ToInstanceDesign_DifferentModellingRules_HandlesCorrectly(ModellingRule modellingRule)
        {
            // Arrange
            var objectDesignJson = new ObjectDesignJson(
                "Test", null, null, null, null, null, null, null, null, null, null, null,
                false, null, null, 0, 0, null, false, ReleaseStatus.Released, DataTypePurpose.Normal,
                false, null, null, null, null, modellingRule, 0, 0, false, false, null);

            // Act
            var result = (ObjectDesign)objectDesignJson.ToInstanceDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.ModellingRule, Is.EqualTo(modellingRule));
            Assert.That(result.ModellingRuleSpecified, Is.True);
        }

        /// <summary>
        /// Tests ToNodeDesign with all null properties.
        /// Verifies that a NodeDesign object is created with default values when all record properties are null.
        /// Expected: Returns a non-null NodeDesign with default constructor values.
        /// </summary>
        [Test]
        public void ToNodeDesign_AllNullProperties_ReturnsNodeDesignWithDefaults()
        {
            // Arrange
            var json = new NodeDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.BrowseName, Is.Null);
            Assert.That(result.DisplayName, Is.Null);
            Assert.That(result.Description, Is.Null);
            Assert.That(result.Children, Is.Null);
            Assert.That(result.References, Is.Null);
            Assert.That(result.IsDeclaration, Is.False);
            Assert.That(result.NumericIdSpecified, Is.False);
            Assert.That(result.AccessRestrictionsSpecified, Is.False);
            Assert.That(result.DefaultAccessRestrictionsSpecified, Is.False);
        }

        /// <summary>
        /// Tests ToNodeDesign with NumericId set to boundary values.
        /// Verifies correct handling of minimum, maximum, and zero values for NumericId.
        /// Expected: NumericId is correctly set and NumericIdSpecified is true.
        /// </summary>
        [TestCase(0u)]
        [TestCase(1u)]
        [TestCase(uint.MaxValue)]
        public void ToNodeDesign_NumericIdBoundaryValues_SetsNumericIdCorrectly(uint numericId)
        {
            // Arrange
            var json = new NodeDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: numericId,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result.NumericId, Is.EqualTo(numericId));
            Assert.That(result.NumericIdSpecified, Is.True);
        }

        /// <summary>
        /// Tests ToNodeDesign with NumericId as null.
        /// Verifies that when NumericId is null, NumericIdSpecified is false.
        /// Expected: NumericIdSpecified is false and NumericId has default value.
        /// </summary>
        [Test]
        public void NodeDesignJsonToNodeDesign_NumericIdNull_NumericIdSpecifiedIsFalse()
        {
            // Arrange
            var json = new NodeDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result.NumericIdSpecified, Is.False);
        }

        /// <summary>
        /// Tests ToNodeDesign with AccessRestrictions as null.
        /// Verifies that when AccessRestrictions is null, AccessRestrictionsSpecified is false.
        /// Expected: AccessRestrictionsSpecified is false.
        /// </summary>
        [Test]
        public void NodeDesignJsonToNodeDesign_AccessRestrictionsNull_AccessRestrictionsSpecifiedIsFalse()
        {
            // Arrange
            var json = new NodeDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result.AccessRestrictionsSpecified, Is.False);
        }

        /// <summary>
        /// Tests ToNodeDesign with various ReleaseStatus values.
        /// Verifies that ReleaseStatus enum values are correctly mapped.
        /// Expected: ReleaseStatus is set correctly.
        /// </summary>
        [TestCase(ReleaseStatus.Released)]
        [TestCase(ReleaseStatus.Draft)]
        [TestCase(ReleaseStatus.Deprecated)]
        public void NodeDesignJsonToNodeDesign_ReleaseStatusValues_SetsReleaseStatusCorrectly(
            ReleaseStatus releaseStatus)
        {
            // Arrange
            var json = new NodeDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: releaseStatus,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result.ReleaseStatus, Is.EqualTo(releaseStatus));
        }

        /// <summary>
        /// Tests ToNodeDesign with various DataTypePurpose values.
        /// Verifies that DataTypePurpose enum values are correctly mapped.
        /// Expected: Purpose is set correctly.
        /// </summary>
        [TestCase(DataTypePurpose.Normal)]
        [TestCase(DataTypePurpose.ServicesOnly)]
        [TestCase(DataTypePurpose.CodeGenerator)]
        public void NodeDesignJsonToNodeDesign_DataTypePurposeValues_SetsPurposeCorrectly(
            DataTypePurpose purpose)
        {
            // Arrange
            var json = new NodeDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: purpose,
                IsDynamic: false,
                NodeType: null);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result.Purpose, Is.EqualTo(purpose));
        }

        /// <summary>
        /// Tests ToNodeDesign with various string properties.
        /// Verifies that string properties including null, empty, and whitespace are correctly mapped.
        /// Expected: String properties are set correctly including null and empty values.
        /// </summary>
        [TestCase(null, null, null)]
        [TestCase("", "", "")]
        [TestCase("   ", "   ", "   ")]
        [TestCase("ValidBrowseName", "ValidStringId", "ValidCategory")]
        public void NodeDesignJsonToNodeDesign_StringProperties_SetsStringPropertiesCorrectly(
            string browseName,
            string stringId,
            string category)
        {
            // Arrange
            var json = new NodeDesignJson(
                BrowseName: browseName,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: stringId,
                WriteAccess: 0,
                PartNo: 0,
                Category: category,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result.BrowseName, Is.EqualTo(browseName));
            Assert.That(result.StringId, Is.EqualTo(stringId));
            Assert.That(result.Category, Is.EqualTo(category));
        }

        /// <summary>
        /// Tests ToNodeDesign with WriteAccess and PartNo boundary values.
        /// Verifies correct handling of minimum, maximum, and zero values for uint properties.
        /// Expected: WriteAccess and PartNo are correctly set.
        /// </summary>
        [TestCase(0u, 0u)]
        [TestCase(1u, 1u)]
        [TestCase(uint.MaxValue, uint.MaxValue)]
        public void NodeDesignJsonToNodeDesign_WriteAccessAndPartNoBoundaryValues_SetsCorrectly(
            uint writeAccess,
            uint partNo)
        {
            // Arrange
            var json = new NodeDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: writeAccess,
                PartNo: partNo,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result.WriteAccess, Is.EqualTo(writeAccess));
            Assert.That(result.PartNo, Is.EqualTo(partNo));
        }

        /// <summary>
        /// Tests ToNodeDesign with empty collections.
        /// Verifies that empty arrays are correctly handled.
        /// Expected: Empty arrays are set without throwing exceptions.
        /// </summary>
        [Test]
        public void NodeDesignJsonToNodeDesign_EmptyCollections_SetsEmptyCollectionsCorrectly()
        {
            // Arrange
            var json = new NodeDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: [],
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: [],
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result.References, Is.Not.Null);
            Assert.That(result.References, Is.Empty);
            Assert.That(result.Extensions, Is.Not.Null);
            Assert.That(result.Extensions, Is.Empty);
        }

        /// <summary>
        /// Tests ToModelDesign with all nullable properties set to null.
        /// Expected: Returns ModelDesign with null/default properties and TargetPublicationDateSpecified = false.
        /// </summary>
        [Test]
        public void ModelDesignJsonToModelDesign_AllPropertiesNull_ReturnsModelDesignWithDefaultValues()
        {
            // Arrange
            var json = new ModelDesignJson(
                Namespaces: null,
                PermissionSets: null,
                Items: null,
                Extensions: null,
                TargetNamespace: null,
                TargetVersion: null,
                TargetPublicationDate: null,
                TargetXmlNamespace: null,
                DefaultLocale: null);

            // Act
            var result = json.ToModelDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.TargetNamespace, Is.Null);
            Assert.That(result.TargetVersion, Is.Null);
            Assert.That(result.TargetXmlNamespace, Is.Null);
            Assert.That(result.DefaultLocale, Is.Null);
            Assert.That(result.PermissionSets, Is.Null);
            Assert.That(result.Extensions, Is.Null);
            Assert.That(result.TargetPublicationDateSpecified, Is.False);
            Assert.That(result.Namespaces, Is.Null);
            Assert.That(result.Items, Is.Null);
        }

        /// <summary>
        /// Tests ToModelDesign when TargetPublicationDate is null.
        /// Expected: TargetPublicationDateSpecified is false and TargetPublicationDate is default.
        /// </summary>
        [Test]
        public void ModelDesignJsonToModelDesign_TargetPublicationDateNull_DoesNotSetSpecifiedFlag()
        {
            // Arrange
            var json = new ModelDesignJson(
                Namespaces: null,
                PermissionSets: null,
                Items: null,
                Extensions: null,
                TargetNamespace: "http://test.org",
                TargetVersion: "1.0.0",
                TargetPublicationDate: null,
                TargetXmlNamespace: null,
                DefaultLocale: null);

            // Act
            var result = json.ToModelDesign();

            // Assert
            Assert.That(result.TargetPublicationDateSpecified, Is.False);
            Assert.That(result.TargetPublicationDate, Is.EqualTo(default(DateTime)));
        }

        /// <summary>
        /// Tests ToModelDesign with various DateTime values for TargetPublicationDate.
        /// Expected: TargetPublicationDate and TargetPublicationDateSpecified are correctly set.
        /// </summary>
        [TestCase("2024-01-15")]
        [TestCase("0001-01-01")]
        [TestCase("9999-12-31")]
        public void ModelDesignJsonToModelDesign_TargetPublicationDateHasValue_SetsDateAndSpecifiedFlag(
            string dateString)
        {
            // Arrange
            var targetDate = DateTime.Parse(dateString, CultureInfo.InvariantCulture);
            var json = new ModelDesignJson(
                Namespaces: null,
                PermissionSets: null,
                Items: null,
                Extensions: null,
                TargetNamespace: null,
                TargetVersion: null,
                TargetPublicationDate: targetDate,
                TargetXmlNamespace: null,
                DefaultLocale: null);

            // Act
            var result = json.ToModelDesign();

            // Assert
            Assert.That(result.TargetPublicationDate, Is.EqualTo(targetDate));
            Assert.That(result.TargetPublicationDateSpecified, Is.True);
        }

        /// <summary>
        /// Tests ToModelDesign with DateTime.MinValue for TargetPublicationDate.
        /// Expected: DateTime.MinValue is correctly set along with TargetPublicationDateSpecified.
        /// </summary>
        [Test]
        public void ModelDesignJsonToModelDesign_TargetPublicationDateMinValue_SetsCorrectly()
        {
            // Arrange
            var json = new ModelDesignJson(
                Namespaces: null,
                PermissionSets: null,
                Items: null,
                Extensions: null,
                TargetNamespace: null,
                TargetVersion: null,
                TargetPublicationDate: DateTime.MinValue,
                TargetXmlNamespace: null,
                DefaultLocale: null);

            // Act
            var result = json.ToModelDesign();

            // Assert
            Assert.That(result.TargetPublicationDate, Is.EqualTo(DateTime.MinValue));
            Assert.That(result.TargetPublicationDateSpecified, Is.True);
        }

        /// <summary>
        /// Tests ToModelDesign with DateTime.MaxValue for TargetPublicationDate.
        /// Expected: DateTime.MaxValue is correctly set along with TargetPublicationDateSpecified.
        /// </summary>
        [Test]
        public void ModelDesignJsonToModelDesign_TargetPublicationDateMaxValue_SetsCorrectly()
        {
            // Arrange
            var json = new ModelDesignJson(
                Namespaces: null,
                PermissionSets: null,
                Items: null,
                Extensions: null,
                TargetNamespace: null,
                TargetVersion: null,
                TargetPublicationDate: DateTime.MaxValue,
                TargetXmlNamespace: null,
                DefaultLocale: null);

            // Act
            var result = json.ToModelDesign();

            // Assert
            Assert.That(result.TargetPublicationDate, Is.EqualTo(DateTime.MaxValue));
            Assert.That(result.TargetPublicationDateSpecified, Is.True);
        }

        /// <summary>
        /// Tests ToModelDesign when Namespaces is null.
        /// Expected: ModelDesign.Namespaces remains null.
        /// </summary>
        [Test]
        public void ModelDesignJsonToModelDesign_NamespacesNull_DoesNotSetNamespaces()
        {
            // Arrange
            var json = new ModelDesignJson(
                Namespaces: null,
                PermissionSets: null,
                Items: null,
                Extensions: null,
                TargetNamespace: null,
                TargetVersion: null,
                TargetPublicationDate: null,
                TargetXmlNamespace: null,
                DefaultLocale: null);

            // Act
            var result = json.ToModelDesign();

            // Assert
            Assert.That(result.Namespaces, Is.Null);
        }

        /// <summary>
        /// Tests ToModelDesign when Namespaces is an empty array.
        /// Expected: ModelDesign.Namespaces is set to the empty array.
        /// </summary>
        [Test]
        public void ModelDesignJsonToModelDesign_NamespacesEmpty_SetsEmptyArray()
        {
            // Arrange
            Namespace[] emptyNamespaces = [];
            var json = new ModelDesignJson(
                Namespaces: emptyNamespaces,
                PermissionSets: null,
                Items: null,
                Extensions: null,
                TargetNamespace: null,
                TargetVersion: null,
                TargetPublicationDate: null,
                TargetXmlNamespace: null,
                DefaultLocale: null);

            // Act
            var result = json.ToModelDesign();

            // Assert
            Assert.That(result.Namespaces, Is.Not.Null);
            Assert.That(result.Namespaces.Length, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests ToModelDesign when Namespaces contains multiple elements.
        /// Expected: ModelDesign.Namespaces is correctly set with all elements.
        /// </summary>
        [Test]
        public void ModelDesignJsonToModelDesign_NamespacesPopulated_SetsNamespacesArray()
        {
            // Arrange
            Namespace[] namespaces =
            [
                CreateNamespace("http://test1.org", "Test1"),
                CreateNamespace("http://test2.org", "Test2")
            ];
            var json = new ModelDesignJson(
                Namespaces: namespaces,
                PermissionSets: null,
                Items: null,
                Extensions: null,
                TargetNamespace: null,
                TargetVersion: null,
                TargetPublicationDate: null,
                TargetXmlNamespace: null,
                DefaultLocale: null);

            // Act
            var result = json.ToModelDesign();

            // Assert
            Assert.That(result.Namespaces, Is.EqualTo(namespaces));
            Assert.That(result.Namespaces.Length, Is.EqualTo(2));
        }

        /// <summary>
        /// Tests ToModelDesign when Items is null.
        /// Expected: ModelDesign.Items remains null.
        /// </summary>
        [Test]
        public void ModelDesignJsonToModelDesign_ItemsNull_DoesNotSetItems()
        {
            // Arrange
            var json = new ModelDesignJson(
                Namespaces: null,
                PermissionSets: null,
                Items: null,
                Extensions: null,
                TargetNamespace: null,
                TargetVersion: null,
                TargetPublicationDate: null,
                TargetXmlNamespace: null,
                DefaultLocale: null);

            // Act
            var result = json.ToModelDesign();

            // Assert
            Assert.That(result.Items, Is.Null);
        }

        /// <summary>
        /// Tests ToModelDesign when Items is an empty array.
        /// Expected: ModelDesign.Items is set to an empty array.
        /// </summary>
        [Test]
        public void ModelDesignJsonToModelDesign_ItemsEmpty_SetsEmptyArray()
        {
            // Arrange
            NodeDesignJson[] emptyItems = [];
            var json = new ModelDesignJson(
                Namespaces: null,
                PermissionSets: null,
                Items: emptyItems,
                Extensions: null,
                TargetNamespace: null,
                TargetVersion: null,
                TargetPublicationDate: null,
                TargetXmlNamespace: null,
                DefaultLocale: null);

            // Act
            var result = json.ToModelDesign();

            // Assert
            Assert.That(result.Items, Is.Not.Null);
            Assert.That(result.Items.Length, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests ToModelDesign when Items contains a single element.
        /// Expected: ModelDesign.Items contains one converted NodeDesign element.
        /// </summary>
        [Test]
        public void ModelDesignJsonToModelDesign_ItemsSingleElement_ConvertsAndSetsItems()
        {
            // Arrange
            NodeDesignJson[] items = [CreateNodeDesignJson("Node1")];
            var json = new ModelDesignJson(
                Namespaces: null,
                PermissionSets: null,
                Items: items,
                Extensions: null,
                TargetNamespace: null,
                TargetVersion: null,
                TargetPublicationDate: null,
                TargetXmlNamespace: null,
                DefaultLocale: null);

            // Act
            var result = json.ToModelDesign();

            // Assert
            Assert.That(result.Items, Is.Not.Null);
            Assert.That(result.Items.Length, Is.EqualTo(1));
            Assert.That(result.Items[0], Is.InstanceOf<NodeDesign>());
            Assert.That(result.Items[0].BrowseName, Is.EqualTo("Node1"));
        }

        /// <summary>
        /// Tests ToModelDesign when Items contains multiple elements.
        /// Expected: ModelDesign.Items contains all converted NodeDesign elements.
        /// </summary>
        [Test]
        public void ModelDesignJsonToModelDesign_ItemsMultipleElements_ConvertsAndSetsAllItems()
        {
            // Arrange
            NodeDesignJson[] items =
            [
                CreateNodeDesignJson("Node1"),
                CreateNodeDesignJson("Node2"),
                CreateNodeDesignJson("Node3")
            ];
            var json = new ModelDesignJson(
                Namespaces: null,
                PermissionSets: null,
                Items: items,
                Extensions: null,
                TargetNamespace: null,
                TargetVersion: null,
                TargetPublicationDate: null,
                TargetXmlNamespace: null,
                DefaultLocale: null);

            // Act
            var result = json.ToModelDesign();

            // Assert
            Assert.That(result.Items, Is.Not.Null);
            Assert.That(result.Items.Length, Is.EqualTo(3));
            Assert.That(result.Items[0].BrowseName, Is.EqualTo("Node1"));
            Assert.That(result.Items[1].BrowseName, Is.EqualTo("Node2"));
            Assert.That(result.Items[2].BrowseName, Is.EqualTo("Node3"));
        }

        /// <summary>
        /// Tests ToModelDesign with empty string for string properties.
        /// Expected: Empty strings are correctly assigned to ModelDesign properties.
        /// </summary>
        [Test]
        public void ModelDesignJsonToModelDesign_EmptyStringProperties_SetsEmptyStrings()
        {
            // Arrange
            var json = new ModelDesignJson(
                Namespaces: null,
                PermissionSets: null,
                Items: null,
                Extensions: null,
                TargetNamespace: string.Empty,
                TargetVersion: string.Empty,
                TargetPublicationDate: null,
                TargetXmlNamespace: string.Empty,
                DefaultLocale: string.Empty);

            // Act
            var result = json.ToModelDesign();

            // Assert
            Assert.That(result.TargetNamespace, Is.EqualTo(string.Empty));
            Assert.That(result.TargetVersion, Is.EqualTo(string.Empty));
            Assert.That(result.TargetXmlNamespace, Is.EqualTo(string.Empty));
            Assert.That(result.DefaultLocale, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests ToModelDesign with whitespace-only strings for string properties.
        /// Expected: Whitespace strings are correctly assigned to ModelDesign properties.
        /// </summary>
        [Test]
        public void ModelDesignJsonToModelDesign_WhitespaceStringProperties_SetsWhitespaceStrings()
        {
            // Arrange
            var json = new ModelDesignJson(
                Namespaces: null,
                PermissionSets: null,
                Items: null,
                Extensions: null,
                TargetNamespace: "   ",
                TargetVersion: "\t",
                TargetPublicationDate: null,
                TargetXmlNamespace: " \n ",
                DefaultLocale: "  \r\n  ");

            // Act
            var result = json.ToModelDesign();

            // Assert
            Assert.That(result.TargetNamespace, Is.EqualTo("   "));
            Assert.That(result.TargetVersion, Is.EqualTo("\t"));
            Assert.That(result.TargetXmlNamespace, Is.EqualTo(" \n "));
            Assert.That(result.DefaultLocale, Is.EqualTo("  \r\n  "));
        }

        /// <summary>
        /// Tests ToModelDesign with very long strings for string properties.
        /// Expected: Long strings are correctly assigned to ModelDesign properties.
        /// </summary>
        [Test]
        public void ModelDesignJsonToModelDesign_VeryLongStrings_SetsLongStrings()
        {
            // Arrange
            string longString = new('x', 10000);
            var json = new ModelDesignJson(
                Namespaces: null,
                PermissionSets: null,
                Items: null,
                Extensions: null,
                TargetNamespace: longString,
                TargetVersion: longString,
                TargetPublicationDate: null,
                TargetXmlNamespace: longString,
                DefaultLocale: longString);

            // Act
            var result = json.ToModelDesign();

            // Assert
            Assert.That(result.TargetNamespace, Is.EqualTo(longString));
            Assert.That(result.TargetVersion, Is.EqualTo(longString));
            Assert.That(result.TargetXmlNamespace, Is.EqualTo(longString));
            Assert.That(result.DefaultLocale, Is.EqualTo(longString));
        }

        /// <summary>
        /// Tests ToModelDesign with strings containing special characters.
        /// Expected: Special characters are correctly assigned to ModelDesign properties.
        /// </summary>
        [Test]
        public void ModelDesignJsonToModelDesign_StringsWithSpecialCharacters_SetsSpecialCharacters()
        {
            // Arrange
            const string specialString = "Test<>&\"'@#$%^&*()";
            var json = new ModelDesignJson(
                Namespaces: null,
                PermissionSets: null,
                Items: null,
                Extensions: null,
                TargetNamespace: specialString,
                TargetVersion: specialString,
                TargetPublicationDate: null,
                TargetXmlNamespace: specialString,
                DefaultLocale: specialString);

            // Act
            var result = json.ToModelDesign();

            // Assert
            Assert.That(result.TargetNamespace, Is.EqualTo(specialString));
            Assert.That(result.TargetVersion, Is.EqualTo(specialString));
            Assert.That(result.TargetXmlNamespace, Is.EqualTo(specialString));
            Assert.That(result.DefaultLocale, Is.EqualTo(specialString));
        }

        /// <summary>
        /// Tests ToModelDesign when PermissionSets is null.
        /// Expected: ModelDesign.PermissionSets is null.
        /// </summary>
        [Test]
        public void ModelDesignJsonToModelDesign_PermissionSetsNull_DoesNotSetPermissionSets()
        {
            // Arrange
            var json = new ModelDesignJson(
                Namespaces: null,
                PermissionSets: null,
                Items: null,
                Extensions: null,
                TargetNamespace: null,
                TargetVersion: null,
                TargetPublicationDate: null,
                TargetXmlNamespace: null,
                DefaultLocale: null);

            // Act
            var result = json.ToModelDesign();

            // Assert
            Assert.That(result.PermissionSets, Is.Null);
        }

        /// <summary>
        /// Tests ToModelDesign when PermissionSets is an empty array.
        /// Expected: ModelDesign.PermissionSets is set to the empty array.
        /// </summary>
        [Test]
        public void ModelDesignJsonToModelDesign_PermissionSetsEmpty_SetsEmptyArray()
        {
            // Arrange
            RolePermissionSet[] emptyPermissions = [];
            var json = new ModelDesignJson(
                Namespaces: null,
                PermissionSets: emptyPermissions,
                Items: null,
                Extensions: null,
                TargetNamespace: null,
                TargetVersion: null,
                TargetPublicationDate: null,
                TargetXmlNamespace: null,
                DefaultLocale: null);

            // Act
            var result = json.ToModelDesign();

            // Assert
            Assert.That(result.PermissionSets, Is.Not.Null);
            Assert.That(result.PermissionSets.Length, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests ToModelDesign when Extensions is null.
        /// Expected: ModelDesign.Extensions is null.
        /// </summary>
        [Test]
        public void ModelDesignJsonToModelDesign_ExtensionsNull_DoesNotSetExtensions()
        {
            // Arrange
            var json = new ModelDesignJson(
                Namespaces: null,
                PermissionSets: null,
                Items: null,
                Extensions: null,
                TargetNamespace: null,
                TargetVersion: null,
                TargetPublicationDate: null,
                TargetXmlNamespace: null,
                DefaultLocale: null);

            // Act
            var result = json.ToModelDesign();

            // Assert
            Assert.That(result.Extensions, Is.Null);
        }

        /// <summary>
        /// Tests ToModelDesign when Extensions is an empty array.
        /// Expected: ModelDesign.Extensions is set to the empty array.
        /// </summary>
        [Test]
        public void ModelDesignJsonToModelDesign_ExtensionsEmpty_SetsEmptyArray()
        {
            // Arrange
            XmlElement[] emptyExtensions = [];
            var json = new ModelDesignJson(
                Namespaces: null,
                PermissionSets: null,
                Items: null,
                Extensions: emptyExtensions,
                TargetNamespace: null,
                TargetVersion: null,
                TargetPublicationDate: null,
                TargetXmlNamespace: null,
                DefaultLocale: null);

            // Act
            var result = json.ToModelDesign();

            // Assert
            Assert.That(result.Extensions, Is.Not.Null);
            Assert.That(result.Extensions.Length, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests ToModelDesign when Extensions contains elements.
        /// Expected: ModelDesign.Extensions is correctly set with all elements.
        /// </summary>
        [Test]
        public void ModelDesignJsonToModelDesign_ExtensionsPopulated_SetsExtensionsArray()
        {
            // Arrange
            XmlElement[] extensions = [CreateXmlElement("Ext1"), CreateXmlElement("Ext2")];
            var json = new ModelDesignJson(
                Namespaces: null,
                PermissionSets: null,
                Items: null,
                Extensions: extensions,
                TargetNamespace: null,
                TargetVersion: null,
                TargetPublicationDate: null,
                TargetXmlNamespace: null,
                DefaultLocale: null);

            // Act
            var result = json.ToModelDesign();

            // Assert
            Assert.That(result.Extensions, Is.EqualTo(extensions));
            Assert.That(result.Extensions.Length, Is.EqualTo(2));
        }

        /// <summary>
        /// Tests ToNodeDesign with all properties null or default.
        /// Input: A VariableDesignJson with all nullable properties set to null and default values.
        /// Expected: Returns a VariableDesign with all properties unset/default and Specified flags false.
        /// </summary>
        [Test]
        public void VariableDesignJsonToNodeDesign_AllPropertiesNull_ReturnsVariableDesignWithDefaults()
        {
            // Arrange
            var json = new VariableDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                DefaultValue: null,
                DataType: null,
                ValueRank: null,
                ArrayDimensions: null,
                AccessLevel: null,
                InstanceAccessLevel: null,
                MinimumSamplingInterval: null,
                Historizing: null);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<VariableDesign>());
            var variable = (VariableDesign)result;
            Assert.That(variable.DefaultValue, Is.Null);
            Assert.That(variable.DataType, Is.Null);
            Assert.That(variable.ArrayDimensions, Is.Null);
            Assert.That(variable.ValueRankSpecified, Is.False);
            Assert.That(variable.AccessLevelSpecified, Is.False);
            Assert.That(variable.InstanceAccessLevelSpecified, Is.False);
            Assert.That(variable.MinimumSamplingIntervalSpecified, Is.False);
            Assert.That(variable.HistorizingSpecified, Is.False);
        }

        /// <summary>
        /// Tests ToNodeDesign with MinimumSamplingInterval set to boundary values.
        /// Input: VariableDesignJson with int.MinValue, 0, int.MaxValue, and negative values.
        /// Expected: Returns VariableDesign with correct MinimumSamplingInterval and Specified flag set.
        /// </summary>
        [TestCase(int.MinValue)]
        [TestCase(-1000)]
        [TestCase(0)]
        [TestCase(1000)]
        [TestCase(int.MaxValue)]
        public void VariableDesignJsonToNodeDesign_MinimumSamplingIntervalBoundaries_SetsValueCorrectly(
            int interval)
        {
            // Arrange
            VariableDesignJson json = CreateMinimalVariableDesignJson();
            json = json with { MinimumSamplingInterval = interval };

            // Act
            var result = json.ToNodeDesign();

            // Assert
            var variable = (VariableDesign)result;
            Assert.That(variable.MinimumSamplingInterval, Is.EqualTo(interval));
            Assert.That(variable.MinimumSamplingIntervalSpecified, Is.True);
        }

        /// <summary>
        /// Tests ToNodeDesign with Historizing set to true and false.
        /// Input: VariableDesignJson with Historizing set to true and false.
        /// Expected: Returns VariableDesign with correct Historizing value and Specified flag set.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void VariableDesignJsonToNodeDesign_HistorizingValues_SetsValueCorrectly(bool historizing)
        {
            // Arrange
            VariableDesignJson json = CreateMinimalVariableDesignJson();
            json = json with { Historizing = historizing };

            // Act
            var result = json.ToNodeDesign();

            // Assert
            var variable = (VariableDesign)result;
            Assert.That(variable.Historizing, Is.EqualTo(historizing));
            Assert.That(variable.HistorizingSpecified, Is.True);
        }

        /// <summary>
        /// Tests ToNodeDesign with empty string ArrayDimensions.
        /// Input: VariableDesignJson with empty string for ArrayDimensions.
        /// Expected: Returns VariableDesign with empty string ArrayDimensions.
        /// </summary>
        [Test]
        public void VariableDesignJsonToNodeDesign_EmptyArrayDimensions_SetsEmptyString()
        {
            // Arrange
            VariableDesignJson json = CreateMinimalVariableDesignJson();
            json = json with { ArrayDimensions = string.Empty };

            // Act
            var result = json.ToNodeDesign();

            // Assert
            var variable = (VariableDesign)result;
            Assert.That(variable.ArrayDimensions, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests ToNodeDesign with whitespace-only ArrayDimensions.
        /// Input: VariableDesignJson with whitespace string for ArrayDimensions.
        /// Expected: Returns VariableDesign with the whitespace string preserved.
        /// </summary>
        [Test]
        public void VariableDesignJsonToNodeDesign_WhitespaceArrayDimensions_PreservesWhitespace()
        {
            // Arrange
            VariableDesignJson json = CreateMinimalVariableDesignJson();
            json = json with { ArrayDimensions = "   " };

            // Act
            var result = json.ToNodeDesign();

            // Assert
            var variable = (VariableDesign)result;
            Assert.That(variable.ArrayDimensions, Is.EqualTo("   "));
        }

        /// <summary>
        /// Tests ToNodeDesign with complex ArrayDimensions string.
        /// Input: VariableDesignJson with complex dimension string.
        /// Expected: Returns VariableDesign with the exact string preserved.
        /// </summary>
        [TestCase("1")]
        [TestCase("1,2")]
        [TestCase("10,20,30")]
        [TestCase("0,0,0")]
        public void VariableDesignJsonToNodeDesign_ComplexArrayDimensions_PreservesString(string dimensions)
        {
            // Arrange
            VariableDesignJson json = CreateMinimalVariableDesignJson();
            json = json with { ArrayDimensions = dimensions };

            // Act
            var result = json.ToNodeDesign();

            // Assert
            var variable = (VariableDesign)result;
            Assert.That(variable.ArrayDimensions, Is.EqualTo(dimensions));
        }

        /// <summary>
        /// Tests ToNodeDesign with null DataType.
        /// Input: VariableDesignJson with null DataType.
        /// Expected: Returns VariableDesign with null DataType.
        /// </summary>
        [Test]
        public void VariableDesignJsonToNodeDesign_NullDataType_SetsNullDataType()
        {
            // Arrange
            VariableDesignJson json = CreateMinimalVariableDesignJson();
            json = json with { DataType = null };

            // Act
            var result = json.ToNodeDesign();

            // Assert
            var variable = (VariableDesign)result;
            Assert.That(variable.DataType, Is.Null);
        }

        /// <summary>
        /// Tests ToNodeDesign with various XmlQualifiedName DataType values.
        /// Input: VariableDesignJson with different XmlQualifiedName DataType values.
        /// Expected: Returns VariableDesign with correct DataType.
        /// </summary>
        [TestCase("Int32", "http://opcfoundation.org/UA/")]
        [TestCase("String", "http://opcfoundation.org/UA/")]
        [TestCase("Boolean", "http://opcfoundation.org/UA/")]
        [TestCase("CustomType", "http://custom.namespace/")]
        public void VariableDesignJsonToNodeDesign_VariousDataTypes_SetsDataTypeCorrectly(
            string name,
            string ns)
        {
            // Arrange
            var dataType = new XmlQualifiedName(name, ns);
            VariableDesignJson json = CreateMinimalVariableDesignJson();
            json = json with { DataType = dataType };

            // Act
            var result = json.ToNodeDesign();

            // Assert
            var variable = (VariableDesign)result;
            Assert.That(variable.DataType, Is.Not.Null);
            Assert.That(variable.DataType.Name, Is.EqualTo(name));
            Assert.That(variable.DataType.Namespace, Is.EqualTo(ns));
        }

        /// <summary>
        /// Tests ToNodeDesign with null DefaultValue.
        /// Input: VariableDesignJson with null DefaultValue.
        /// Expected: Returns VariableDesign with null DefaultValue.
        /// </summary>
        [Test]
        public void VariableDesignJsonToNodeDesign_NullDefaultValue_SetsNullDefaultValue()
        {
            // Arrange
            VariableDesignJson json = CreateMinimalVariableDesignJson();
            json = json with { DefaultValue = null };

            // Act
            var result = json.ToNodeDesign();

            // Assert
            var variable = (VariableDesign)result;
            Assert.That(variable.DefaultValue, Is.Null);
        }

        /// <summary>
        /// Tests ToNodeDesign with valid XmlElement DefaultValue.
        /// Input: VariableDesignJson with an XmlElement DefaultValue.
        /// Expected: Returns VariableDesign with the same XmlElement reference.
        /// </summary>
        [Test]
        public void VariableDesignJsonToNodeDesign_ValidDefaultValue_SetsDefaultValue()
        {
            // Arrange
            var xmlDoc = new XmlDocument();
            XmlElement defaultValue = xmlDoc.CreateElement("Value");
            defaultValue.InnerText = "TestValue";

            VariableDesignJson json = CreateMinimalVariableDesignJson();
            json = json with { DefaultValue = defaultValue };

            // Act
            var result = json.ToNodeDesign();

            // Assert
            var variable = (VariableDesign)result;
            Assert.That(variable.DefaultValue, Is.SameAs(defaultValue));
            Assert.That(variable.DefaultValue.InnerText, Is.EqualTo("TestValue"));
        }

        /// <summary>
        /// Tests ToNodeDesign verifies return type is NodeDesign but actual instance is VariableDesign.
        /// Input: VariableDesignJson instance.
        /// Expected: Returns object of type NodeDesign that is actually a VariableDesign instance.
        /// </summary>
        [Test]
        public void VariableDesignJsonToNodeDesign_ReturnType_IsNodeDesignButInstanceIsVariableDesign()
        {
            // Arrange
            VariableDesignJson json = CreateMinimalVariableDesignJson();

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<NodeDesign>());
            Assert.That(result, Is.InstanceOf<InstanceDesign>());
            Assert.That(result, Is.InstanceOf<VariableDesign>());
        }

        /// <summary>
        /// Tests ToInstanceDesign with minimal properties set.
        /// Verifies that the method returns a VariableDesign instance.
        /// </summary>
        [Test]
        public void VariableDesignJsonToInstanceDesign_MinimalProperties_ReturnsVariableDesign()
        {
            // Arrange
            var variableDesignJson = new VariableDesignJson(
                BrowseName: "TestVariable",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                DefaultValue: null,
                DataType: null,
                ValueRank: null,
                ArrayDimensions: null,
                AccessLevel: null,
                InstanceAccessLevel: null,
                MinimumSamplingInterval: null,
                Historizing: null);

            // Act
            var result = variableDesignJson.ToInstanceDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<VariableDesign>());
            Assert.That(result, Is.InstanceOf<InstanceDesign>());
        }

        /// <summary>
        /// Tests ToInstanceDesign with null BrowseName.
        /// Verifies that the method handles null values correctly.
        /// </summary>
        [Test]
        public void VariableDesignJsonToInstanceDesign_NullBrowseName_ReturnsVariableDesign()
        {
            // Arrange
            var variableDesignJson = new VariableDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                DefaultValue: null,
                DataType: null,
                ValueRank: null,
                ArrayDimensions: null,
                AccessLevel: null,
                InstanceAccessLevel: null,
                MinimumSamplingInterval: null,
                Historizing: null);

            // Act
            var result = variableDesignJson.ToInstanceDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<VariableDesign>());
        }

        /// <summary>
        /// Tests ToInstanceDesign with various MinimumSamplingInterval values.
        /// Verifies boundary values and nullable handling.
        /// </summary>
        [TestCase(-1)]
        [TestCase(0)]
        [TestCase(100)]
        [TestCase(int.MaxValue)]
        [TestCase(null)]
        public void VariableDesignJsonToInstanceDesign_VariousMinimumSamplingIntervals_ReturnsVariableDesign(
            int? minimumSamplingInterval)
        {
            // Arrange
            var variableDesignJson = new VariableDesignJson(
                BrowseName: "TestVariable",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                DefaultValue: null,
                DataType: null,
                ValueRank: null,
                ArrayDimensions: null,
                AccessLevel: null,
                InstanceAccessLevel: null,
                MinimumSamplingInterval: minimumSamplingInterval,
                Historizing: null);

            // Act
            var result = variableDesignJson.ToInstanceDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<VariableDesign>());
            var variableDesign = (VariableDesign)result;
            if (minimumSamplingInterval.HasValue)
            {
                Assert.That(variableDesign.MinimumSamplingIntervalSpecified, Is.True);
                Assert.That(variableDesign.MinimumSamplingInterval, Is.EqualTo(minimumSamplingInterval.Value));
            }
            else
            {
                Assert.That(variableDesign.MinimumSamplingIntervalSpecified, Is.False);
            }
        }

        /// <summary>
        /// Tests ToInstanceDesign with various Historizing values.
        /// Verifies that nullable boolean is handled correctly.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        [TestCase(null)]
        public void VariableDesignJsonToInstanceDesign_VariousHistorizingValues_ReturnsVariableDesign(
            bool? historizing)
        {
            // Arrange
            var variableDesignJson = new VariableDesignJson(
                BrowseName: "TestVariable",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                DefaultValue: null,
                DataType: null,
                ValueRank: null,
                ArrayDimensions: null,
                AccessLevel: null,
                InstanceAccessLevel: null,
                MinimumSamplingInterval: null,
                Historizing: historizing);

            // Act
            var result = variableDesignJson.ToInstanceDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<VariableDesign>());
            var variableDesign = (VariableDesign)result;
            if (historizing.HasValue)
            {
                Assert.That(variableDesign.HistorizingSpecified, Is.True);
                Assert.That(variableDesign.Historizing, Is.EqualTo(historizing.Value));
            }
            else
            {
                Assert.That(variableDesign.HistorizingSpecified, Is.False);
            }
        }

        /// <summary>
        /// Tests ToInstanceDesign with boundary values for cardinality.
        /// Verifies that MinCardinality and MaxCardinality are handled correctly.
        /// </summary>
        [TestCase(0u, 0u)]
        [TestCase(1u, 1u)]
        [TestCase(0u, uint.MaxValue)]
        [TestCase(uint.MaxValue, uint.MaxValue)]
        public void VariableDesignJsonToInstanceDesign_BoundaryCardinalityValues_ReturnsVariableDesign(
            uint minCardinality,
            uint maxCardinality)
        {
            // Arrange
            var variableDesignJson = new VariableDesignJson(
                BrowseName: "TestVariable",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: minCardinality,
                MaxCardinality: maxCardinality,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                DefaultValue: null,
                DataType: null,
                ValueRank: null,
                ArrayDimensions: null,
                AccessLevel: null,
                InstanceAccessLevel: null,
                MinimumSamplingInterval: null,
                Historizing: null);

            // Act
            var result = variableDesignJson.ToInstanceDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<VariableDesign>());
            var variableDesign = (VariableDesign)result;
            Assert.That(variableDesign.MinCardinality, Is.EqualTo(minCardinality));
            Assert.That(variableDesign.MaxCardinality, Is.EqualTo(maxCardinality));
        }

        /// <summary>
        /// Tests ToNodeDesign with null EncodingName.
        /// Input: DictionaryDesignJson with null EncodingName and minimal properties.
        /// Expected: Returns a DictionaryDesign with null EncodingName.
        /// </summary>
        [Test]
        public void DictionaryDesignJsonToNodeDesign_WithNullEncodingName_ReturnsDictionaryDesignWithNullEncodingName()
        {
            // Arrange
            var json = new DictionaryDesignJson(
                BrowseName: "TestDictionary",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                DefaultValue: null,
                DataType: null,
                ValueRank: null,
                ArrayDimensions: null,
                AccessLevel: null,
                InstanceAccessLevel: null,
                MinimumSamplingInterval: null,
                Historizing: null,
                EncodingName: null);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<DictionaryDesign>());
            var dictionary = (DictionaryDesign)result;
            Assert.That(dictionary.EncodingName, Is.Null);
        }

        /// <summary>
        /// Tests ToNodeDesign with non-null EncodingName.
        /// Input: DictionaryDesignJson with a valid XmlQualifiedName for EncodingName.
        /// Expected: Returns a DictionaryDesign with the same EncodingName.
        /// </summary>
        [Test]
        public void DictionaryDesignJsonToNodeDesign_WithNonNullEncodingName_ReturnsDictionaryDesignWithEncodingName()
        {
            // Arrange
            var encodingName = new XmlQualifiedName("TestEncoding", "http://test.namespace");
            var json = new DictionaryDesignJson(
                BrowseName: "TestDictionary",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                DefaultValue: null,
                DataType: null,
                ValueRank: null,
                ArrayDimensions: null,
                AccessLevel: null,
                InstanceAccessLevel: null,
                MinimumSamplingInterval: null,
                Historizing: null,
                EncodingName: encodingName);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<DictionaryDesign>());
            var dictionary = (DictionaryDesign)result;
            Assert.That(dictionary.EncodingName, Is.EqualTo(encodingName));
        }

        /// <summary>
        /// Tests ToNodeDesign applies BrowseName correctly.
        /// Input: DictionaryDesignJson with a specific BrowseName.
        /// Expected: Returns a DictionaryDesign with the same BrowseName.
        /// </summary>
        [Test]
        public void DictionaryDesignJsonToNodeDesign_WithBrowseName_AppliesBrowseNameToDictionary()
        {
            // Arrange
            const string browseName = "TestBrowseName";
            var json = new DictionaryDesignJson(
                BrowseName: browseName,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                DefaultValue: null,
                DataType: null,
                ValueRank: null,
                ArrayDimensions: null,
                AccessLevel: null,
                InstanceAccessLevel: null,
                MinimumSamplingInterval: null,
                Historizing: null,
                EncodingName: null);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.BrowseName, Is.EqualTo(browseName));
        }

        /// <summary>
        /// Tests ToNodeDesign with DataType property.
        /// Input: DictionaryDesignJson with a valid DataType XmlQualifiedName.
        /// Expected: Returns a DictionaryDesign with the same DataType.
        /// </summary>
        [Test]
        public void DictionaryDesignJsonToNodeDesign_WithDataType_AppliesDataTypeToDictionary()
        {
            // Arrange
            var dataType = new XmlQualifiedName("Int32", "http://opcfoundation.org/UA/");
            var json = new DictionaryDesignJson(
                BrowseName: "TestDictionary",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                DefaultValue: null,
                DataType: dataType,
                ValueRank: null,
                ArrayDimensions: null,
                AccessLevel: null,
                InstanceAccessLevel: null,
                MinimumSamplingInterval: null,
                Historizing: null,
                EncodingName: null);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            var dictionary = (DictionaryDesign)result;
            Assert.That(dictionary.DataType, Is.EqualTo(dataType));
        }

        /// <summary>
        /// Tests ToNodeDesign with MinimumSamplingInterval property.
        /// Input: DictionaryDesignJson with a specific MinimumSamplingInterval value.
        /// Expected: Returns a DictionaryDesign with the same MinimumSamplingInterval and MinimumSamplingIntervalSpecified set to true.
        /// </summary>
        [TestCase(0)]
        [TestCase(100)]
        [TestCase(-1)]
        [TestCase(int.MaxValue)]
        public void DictionaryDesignJsonToNodeDesign_WithMinimumSamplingInterval_AppliesMinimumSamplingIntervalToDictionary(
            int samplingInterval)
        {
            // Arrange
            var json = new DictionaryDesignJson(
                BrowseName: "TestDictionary",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                DefaultValue: null,
                DataType: null,
                ValueRank: null,
                ArrayDimensions: null,
                AccessLevel: null,
                InstanceAccessLevel: null,
                MinimumSamplingInterval: samplingInterval,
                Historizing: null,
                EncodingName: null);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            var dictionary = (DictionaryDesign)result;
            Assert.That(dictionary.MinimumSamplingInterval, Is.EqualTo(samplingInterval));
            Assert.That(dictionary.MinimumSamplingIntervalSpecified, Is.True);
        }

        /// <summary>
        /// Tests ToNodeDesign with Historizing property.
        /// Input: DictionaryDesignJson with Historizing set to true and false.
        /// Expected: Returns a DictionaryDesign with the same Historizing value and HistorizingSpecified set to true.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void DictionaryDesignJsonToNodeDesign_WithHistorizing_AppliesHistorizingToDictionary(
            bool historizing)
        {
            // Arrange
            var json = new DictionaryDesignJson(
                BrowseName: "TestDictionary",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                DefaultValue: null,
                DataType: null,
                ValueRank: null,
                ArrayDimensions: null,
                AccessLevel: null,
                InstanceAccessLevel: null,
                MinimumSamplingInterval: null,
                Historizing: historizing,
                EncodingName: null);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            var dictionary = (DictionaryDesign)result;
            Assert.That(dictionary.Historizing, Is.EqualTo(historizing));
            Assert.That(dictionary.HistorizingSpecified, Is.True);
        }

        /// <summary>
        /// Tests ToNodeDesign with complex EncodingName including namespace.
        /// Input: DictionaryDesignJson with EncodingName having both name and namespace.
        /// Expected: Returns a DictionaryDesign with the same EncodingName preserving both name and namespace.
        /// </summary>
        [Test]
        public void DictionaryDesignJsonToNodeDesign_WithComplexEncodingName_PreservesNameAndNamespace()
        {
            // Arrange
            var encodingName = new XmlQualifiedName("ComplexType_Encoding_DefaultBinary", "http://opcfoundation.org/UA/MyTypes/");
            var json = new DictionaryDesignJson(
                BrowseName: "MyDictionary",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                DefaultValue: null,
                DataType: null,
                ValueRank: null,
                ArrayDimensions: null,
                AccessLevel: null,
                InstanceAccessLevel: null,
                MinimumSamplingInterval: null,
                Historizing: null,
                EncodingName: encodingName);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            var dictionary = (DictionaryDesign)result;
            Assert.That(dictionary.EncodingName, Is.Not.Null);
            Assert.That(dictionary.EncodingName.Name, Is.EqualTo("ComplexType_Encoding_DefaultBinary"));
            Assert.That(dictionary.EncodingName.Namespace, Is.EqualTo("http://opcfoundation.org/UA/MyTypes/"));
        }

        /// <summary>
        /// Tests ToInstanceDesign returns a MethodDesign instance cast to InstanceDesign.
        /// Input: Valid MethodDesignJson with all null/default values.
        /// Expected: Returns non-null InstanceDesign that is actually a MethodDesign.
        /// </summary>
        [Test]
        public void MethodDesignJsonToInstanceDesign_WithDefaultValues_ReturnsMethodDesignAsInstanceDesign()
        {
            // Arrange
            var methodJson = new MethodDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                InputArguments: null,
                OutputArguments: null,
                NonExecutable: null);

            // Act
            var result = methodJson.ToInstanceDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<MethodDesign>());
        }

        /// <summary>
        /// Tests ToInstanceDesign returns the same object as ToNodeDesign.
        /// Input: Valid MethodDesignJson instance.
        /// Expected: ToInstanceDesign returns the same instance as ToNodeDesign.
        /// </summary>
        [Test]
        public void MethodDesignJsonToInstanceDesign_WithValidData_ReturnsSameAsToNodeDesign()
        {
            // Arrange
            var methodJson = new MethodDesignJson(
                BrowseName: "TestMethod",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: new XmlQualifiedName("TestMethod", "http://test.com"),
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: 1000,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                InputArguments: null,
                OutputArguments: null,
                NonExecutable: false);

            // Act
            var instanceResult = methodJson.ToInstanceDesign();
            var nodeResult = methodJson.ToNodeDesign();

            // Assert
            Assert.That(nodeResult, Is.EqualTo(instanceResult));
        }

        /// <summary>
        /// Tests ToInstanceDesign with InputArguments populated.
        /// Input: MethodDesignJson with input arguments.
        /// Expected: Returns MethodDesign with InputArguments populated.
        /// </summary>
        [Test]
        public void MethodDesignJsonToInstanceDesign_WithInputArguments_ReturnsMethodDesignWithArguments()
        {
            // Arrange
            ParameterJson[] inputArgs =
            [
                new ParameterJson(
                    Description: null,
                    DefaultValue: null,
                    DisplayName: null,
                    Name: "arg1",
                    Identifier: 1,
                    BitMask: null,
                    DataType: new XmlQualifiedName("Int32", "http://opcfoundation.org/UA/"),
                    ValueRank: ValueRank.Scalar,
                    ArrayDimensions: null,
                    AllowSubTypes: false,
                    IsOptional: false,
                    ReleaseStatus: ReleaseStatus.Released)
            ];

            var methodJson = new MethodDesignJson(
                BrowseName: "TestMethod",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                InputArguments: inputArgs,
                OutputArguments: null,
                NonExecutable: null);

            // Act
            var result = methodJson.ToInstanceDesign();

            // Assert
            Assert.That(result, Is.InstanceOf<MethodDesign>());
            var methodDesign = (MethodDesign)result;
            Assert.That(methodDesign.InputArguments, Is.Not.Null);
            Assert.That(methodDesign.InputArguments.Length, Is.EqualTo(1));
            Assert.That(methodDesign.InputArguments[0].Name, Is.EqualTo("arg1"));
        }

        /// <summary>
        /// Tests ToInstanceDesign with OutputArguments populated.
        /// Input: MethodDesignJson with output arguments.
        /// Expected: Returns MethodDesign with OutputArguments populated.
        /// </summary>
        [Test]
        public void MethodDesignJsonToInstanceDesign_WithOutputArguments_ReturnsMethodDesignWithArguments()
        {
            // Arrange
            ParameterJson[] outputArgs =
            [
                new ParameterJson(
                    Description: null,
                    DefaultValue: null,
                    DisplayName: null,
                    Name: "result",
                    Identifier: 1,
                    BitMask: null,
                    DataType: new XmlQualifiedName("String", "http://opcfoundation.org/UA/"),
                    ValueRank: ValueRank.Scalar,
                    ArrayDimensions: null,
                    AllowSubTypes: false,
                    IsOptional: false,
                    ReleaseStatus: ReleaseStatus.Released)
            ];

            var methodJson = new MethodDesignJson(
                BrowseName: "TestMethod",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                InputArguments: null,
                OutputArguments: outputArgs,
                NonExecutable: null);

            // Act
            var result = methodJson.ToInstanceDesign();

            // Assert
            Assert.That(result, Is.InstanceOf<MethodDesign>());
            var methodDesign = (MethodDesign)result;
            Assert.That(methodDesign.OutputArguments, Is.Not.Null);
            Assert.That(methodDesign.OutputArguments.Length, Is.EqualTo(1));
            Assert.That(methodDesign.OutputArguments[0].Name, Is.EqualTo("result"));
        }

        /// <summary>
        /// Tests ToInstanceDesign with NonExecutable set to true.
        /// Input: MethodDesignJson with NonExecutable = true.
        /// Expected: Returns MethodDesign with NonExecutable = true and NonExecutableSpecified = true.
        /// </summary>
        [Test]
        public void MethodDesignJsonToInstanceDesign_WithNonExecutableTrue_ReturnsMethodDesignWithNonExecutableSet()
        {
            // Arrange
            var methodJson = new MethodDesignJson(
                BrowseName: "TestMethod",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                InputArguments: null,
                OutputArguments: null,
                NonExecutable: true);

            // Act
            var result = methodJson.ToInstanceDesign();

            // Assert
            Assert.That(result, Is.InstanceOf<MethodDesign>());
            var methodDesign = (MethodDesign)result;
            Assert.That(methodDesign.NonExecutable, Is.True);
            Assert.That(methodDesign.NonExecutableSpecified, Is.True);
        }

        /// <summary>
        /// Tests ToInstanceDesign with NonExecutable set to false.
        /// Input: MethodDesignJson with NonExecutable = false.
        /// Expected: Returns MethodDesign with NonExecutable = false and NonExecutableSpecified = true.
        /// </summary>
        [Test]
        public void MethodDesignJsonToInstanceDesign_WithNonExecutableFalse_ReturnsMethodDesignWithNonExecutableSet()
        {
            // Arrange
            var methodJson = new MethodDesignJson(
                BrowseName: "TestMethod",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                InputArguments: null,
                OutputArguments: null,
                NonExecutable: false);

            // Act
            var result = methodJson.ToInstanceDesign();

            // Assert
            Assert.That(result, Is.InstanceOf<MethodDesign>());
            var methodDesign = (MethodDesign)result;
            Assert.That(methodDesign.NonExecutable, Is.False);
            Assert.That(methodDesign.NonExecutableSpecified, Is.True);
        }

        /// <summary>
        /// Tests ToInstanceDesign with both input and output arguments.
        /// Input: MethodDesignJson with both input and output arguments.
        /// Expected: Returns MethodDesign with both argument arrays populated.
        /// </summary>
        [Test]
        public void MethodDesignJsonToInstanceDesign_WithBothArguments_ReturnsMethodDesignWithBothArgumentArrays()
        {
            // Arrange
            ParameterJson[] inputArgs =
            [
                new ParameterJson(
                    Description: null,
                    DefaultValue: null,
                    DisplayName: null,
                    Name: "input1",
                    Identifier: 1,
                    BitMask: null,
                    DataType: new XmlQualifiedName("Int32", "http://opcfoundation.org/UA/"),
                    ValueRank: ValueRank.Scalar,
                    ArrayDimensions: null,
                    AllowSubTypes: false,
                    IsOptional: false,
                    ReleaseStatus: ReleaseStatus.Released),
                new ParameterJson(
                    Description: null,
                    DefaultValue: null,
                    DisplayName: null,
                    Name: "input2",
                    Identifier: 2,
                    BitMask: null,
                    DataType: new XmlQualifiedName("String", "http://opcfoundation.org/UA/"),
                    ValueRank: ValueRank.Scalar,
                    ArrayDimensions: null,
                    AllowSubTypes: false,
                    IsOptional: false,
                    ReleaseStatus: ReleaseStatus.Released)
            ];

            ParameterJson[] outputArgs =
            [
                new ParameterJson(
                    Description: null,
                    DefaultValue: null,
                    DisplayName: null,
                    Name: "output1",
                    Identifier: 1,
                    BitMask: null,
                    DataType: new XmlQualifiedName("Boolean", "http://opcfoundation.org/UA/"),
                    ValueRank: ValueRank.Scalar,
                    ArrayDimensions: null,
                    AllowSubTypes: false,
                    IsOptional: false,
                    ReleaseStatus: ReleaseStatus.Released)
            ];

            var methodJson = new MethodDesignJson(
                BrowseName: "TestMethod",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                InputArguments: inputArgs,
                OutputArguments: outputArgs,
                NonExecutable: false);

            // Act
            var result = methodJson.ToInstanceDesign();

            // Assert
            Assert.That(result, Is.InstanceOf<MethodDesign>());
            var methodDesign = (MethodDesign)result;
            Assert.That(methodDesign.InputArguments, Is.Not.Null);
            Assert.That(methodDesign.InputArguments.Length, Is.EqualTo(2));
            Assert.That(methodDesign.OutputArguments, Is.Not.Null);
            Assert.That(methodDesign.OutputArguments.Length, Is.EqualTo(1));
        }

        /// <summary>
        /// Tests ToInstanceDesign with empty argument arrays.
        /// Input: MethodDesignJson with empty input and output argument arrays.
        /// Expected: Returns MethodDesign with empty argument arrays.
        /// </summary>
        [Test]
        public void MethodDesignJsonToInstanceDesign_WithEmptyArgumentArrays_ReturnsMethodDesignWithEmptyArrays()
        {
            // Arrange
            var methodJson = new MethodDesignJson(
                BrowseName: "TestMethod",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                InputArguments: [],
                OutputArguments: [],
                NonExecutable: null);

            // Act
            var result = methodJson.ToInstanceDesign();

            // Assert
            Assert.That(result, Is.InstanceOf<MethodDesign>());
            var methodDesign = (MethodDesign)result;
            Assert.That(methodDesign.InputArguments, Is.Not.Null);
            Assert.That(methodDesign.InputArguments.Length, Is.EqualTo(0));
            Assert.That(methodDesign.OutputArguments, Is.Not.Null);
            Assert.That(methodDesign.OutputArguments.Length, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests ToInstanceDesign with all ModellingRule values.
        /// Input: MethodDesignJson with different ModellingRule values.
        /// Expected: Returns MethodDesign with correctly set properties.
        /// </summary>
        [TestCase(ModellingRule.Mandatory)]
        [TestCase(ModellingRule.Optional)]
        [TestCase(ModellingRule.ExposesItsArray)]
        [TestCase(ModellingRule.MandatoryPlaceholder)]
        [TestCase(ModellingRule.OptionalPlaceholder)]
        public void MethodDesignJsonToInstanceDesign_WithModellingRule_ReturnsMethodDesignWithCorrectRule(
            ModellingRule modellingRule)
        {
            // Arrange
            var methodJson = new MethodDesignJson(
                BrowseName: "TestMethod",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: modellingRule,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                InputArguments: null,
                OutputArguments: null,
                NonExecutable: null);

            // Act
            var result = methodJson.ToInstanceDesign();

            // Assert
            Assert.That(result, Is.InstanceOf<MethodDesign>());
            Assert.That(result.ModellingRuleSpecified, Is.True);
            Assert.That(result.ModellingRule, Is.EqualTo(modellingRule));
        }

        /// <summary>
        /// Tests ToNodeDesign with null InputArguments.
        /// Input: MethodDesignJson with InputArguments set to null.
        /// Expected: Returns a MethodDesign with InputArguments not assigned (null or default).
        /// </summary>
        [Test]
        public void MethodDesignJsonToNodeDesign_NullInputArguments_DoesNotSetInputArguments()
        {
            // Arrange
            var methodJson = new MethodDesignJson(
                BrowseName: "TestMethod",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                InputArguments: null,
                OutputArguments: null,
                NonExecutable: null
            );

            // Act
            var result = methodJson.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<MethodDesign>());
            var method = (MethodDesign)result;
            Assert.That(method.InputArguments, Is.Null);
        }

        /// <summary>
        /// Tests ToNodeDesign with null OutputArguments.
        /// Input: MethodDesignJson with OutputArguments set to null.
        /// Expected: Returns a MethodDesign with OutputArguments not assigned (null or default).
        /// </summary>
        [Test]
        public void MethodDesignJsonToNodeDesign_NullOutputArguments_DoesNotSetOutputArguments()
        {
            // Arrange
            var methodJson = new MethodDesignJson(
                BrowseName: "TestMethod",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                InputArguments: null,
                OutputArguments: null,
                NonExecutable: null
            );

            // Act
            var result = methodJson.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<MethodDesign>());
            var method = (MethodDesign)result;
            Assert.That(method.OutputArguments, Is.Null);
        }

        /// <summary>
        /// Tests ToNodeDesign with empty InputArguments array.
        /// Input: MethodDesignJson with InputArguments set to an empty array.
        /// Expected: Returns a MethodDesign with an empty InputArguments array.
        /// </summary>
        [Test]
        public void MethodDesignJsonToNodeDesign_EmptyInputArguments_ReturnsEmptyArray()
        {
            // Arrange
            var methodJson = new MethodDesignJson(
                BrowseName: "TestMethod",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                InputArguments: [],
                OutputArguments: null,
                NonExecutable: null
            );

            // Act
            var result = methodJson.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<MethodDesign>());
            var method = (MethodDesign)result;
            Assert.That(method.InputArguments, Is.Not.Null);
            Assert.That(method.InputArguments.Length, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests ToNodeDesign with empty OutputArguments array.
        /// Input: MethodDesignJson with OutputArguments set to an empty array.
        /// Expected: Returns a MethodDesign with an empty OutputArguments array.
        /// </summary>
        [Test]
        public void MethodDesignJsonToNodeDesign_EmptyOutputArguments_ReturnsEmptyArray()
        {
            // Arrange
            var methodJson = new MethodDesignJson(
                BrowseName: "TestMethod",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                InputArguments: null,
                OutputArguments: [],
                NonExecutable: null
            );

            // Act
            var result = methodJson.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<MethodDesign>());
            var method = (MethodDesign)result;
            Assert.That(method.OutputArguments, Is.Not.Null);
            Assert.That(method.OutputArguments.Length, Is.EqualTo(0));
        }

        /// <summary>
        /// Tests ToNodeDesign with NonExecutable set to null.
        /// Input: MethodDesignJson with NonExecutable set to null.
        /// Expected: Returns a MethodDesign with NonExecutableSpecified set to false.
        /// </summary>
        [Test]
        public void MethodDesignJsonToNodeDesign_NullNonExecutable_DoesNotSetNonExecutableSpecified()
        {
            // Arrange
            var methodJson = new MethodDesignJson(
                BrowseName: "TestMethod",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                InputArguments: null,
                OutputArguments: null,
                NonExecutable: null
            );

            // Act
            var result = methodJson.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<MethodDesign>());
            var method = (MethodDesign)result;
            Assert.That(method.NonExecutableSpecified, Is.False);
        }

        /// <summary>
        /// Tests ToNodeDesign with NonExecutable set to true.
        /// Input: MethodDesignJson with NonExecutable set to true.
        /// Expected: Returns a MethodDesign with NonExecutable true and NonExecutableSpecified true.
        /// </summary>
        [Test]
        public void MethodDesignJsonToNodeDesign_NonExecutableTrue_SetsNonExecutableTrueAndSpecified()
        {
            // Arrange
            var methodJson = new MethodDesignJson(
                BrowseName: "TestMethod",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                InputArguments: null,
                OutputArguments: null,
                NonExecutable: true
            );

            // Act
            var result = methodJson.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<MethodDesign>());
            var method = (MethodDesign)result;
            Assert.That(method.NonExecutable, Is.True);
            Assert.That(method.NonExecutableSpecified, Is.True);
        }

        /// <summary>
        /// Tests ToNodeDesign with NonExecutable set to false.
        /// Input: MethodDesignJson with NonExecutable set to false.
        /// Expected: Returns a MethodDesign with NonExecutable false and NonExecutableSpecified true.
        /// </summary>
        [Test]
        public void MethodDesignJsonToNodeDesign_NonExecutableFalse_SetsNonExecutableFalseAndSpecified()
        {
            // Arrange
            var methodJson = new MethodDesignJson(
                BrowseName: "TestMethod",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                InputArguments: null,
                OutputArguments: null,
                NonExecutable: false
            );

            // Act
            var result = methodJson.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<MethodDesign>());
            var method = (MethodDesign)result;
            Assert.That(method.NonExecutable, Is.False);
            Assert.That(method.NonExecutableSpecified, Is.True);
        }

        /// <summary>
        /// Tests ToNodeDesign with a single input argument.
        /// Input: MethodDesignJson with one input argument.
        /// Expected: Returns a MethodDesign with InputArguments containing one parameter.
        /// </summary>
        [Test]
        public void MethodDesignJsonToNodeDesign_SingleInputArgument_ReturnsSingleParameter()
        {
            // Arrange
            ParameterJson[] inputArgs =
            [
                new ParameterJson(
                    Description: new LocalizedText { Value = "Test parameter" },
                    DefaultValue: null,
                    DisplayName: new LocalizedText { Value = "Test Display" },
                    Name: "TestParam",
                    Identifier: 42m,
                    BitMask: null,
                    DataType: new XmlQualifiedName("Int32"),
                    ValueRank: ValueRank.Scalar,
                    ArrayDimensions: null,
                    AllowSubTypes: true,
                    IsOptional: false,
                    ReleaseStatus: ReleaseStatus.Released
                )
            ];
            var methodJson = new MethodDesignJson(
                BrowseName: "TestMethod",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                InputArguments: inputArgs,
                OutputArguments: null,
                NonExecutable: null
            );

            // Act
            var result = methodJson.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<MethodDesign>());
            var method = (MethodDesign)result;
            Assert.That(method.InputArguments, Is.Not.Null);
            Assert.That(method.InputArguments.Length, Is.EqualTo(1));
            Assert.That(method.InputArguments[0].Name, Is.EqualTo("TestParam"));
            Assert.That(method.InputArguments[0].DataType, Is.EqualTo(new XmlQualifiedName("Int32")));
            Assert.That(method.InputArguments[0].AllowSubTypes, Is.True);
        }

        /// <summary>
        /// Tests ToNodeDesign applies base properties from ApplyTo.
        /// Input: MethodDesignJson with various base properties set.
        /// Expected: Returns a MethodDesign with base properties correctly applied.
        /// </summary>
        [Test]
        public void MethodDesignJsonToNodeDesign_WithBaseProperties_AppliesBasePropertiesToMethodDesign()
        {
            // Arrange
            var methodJson = new MethodDesignJson(
                BrowseName: "BaseTestMethod",
                DisplayName: new LocalizedText { Value = "Base Display Name" },
                Description: new LocalizedText { Value = "Base Description" },
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: AccessRestrictions.SigningRequired,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: new XmlQualifiedName("BaseSymbol"),
                SymbolicId: new XmlQualifiedName("BaseId"),
                IsDeclaration: true,
                NumericId: 999,
                StringId: "StringId123",
                WriteAccess: 1,
                PartNo: 2,
                Category: "BaseCategory",
                NotInAddressSpace: true,
                ReleaseStatus: ReleaseStatus.Deprecated,
                Purpose: DataTypePurpose.ServicesOnly,
                IsDynamic: true,
                NodeType: "BaseNode",
                ReferenceType: new XmlQualifiedName("RefType"),
                Declaration: new XmlQualifiedName("Decl"),
                TypeDefinition: new XmlQualifiedName("TypeDef"),
                ModellingRule: ModellingRule.Optional,
                MinCardinality: 1,
                MaxCardinality: 10,
                PreserveDefaultAttributes: true,
                DesignToolOnly: true,
                InputArguments: null,
                OutputArguments: null,
                NonExecutable: null
            );

            // Act
            var result = methodJson.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<MethodDesign>());
            var method = (MethodDesign)result;
            Assert.That(method.BrowseName, Is.EqualTo("BaseTestMethod"));
            Assert.That(method.DisplayName, Is.Not.Null);
            Assert.That(method.DisplayName.Value, Is.EqualTo("Base Display Name"));
            Assert.That(method.Description, Is.Not.Null);
            Assert.That(method.Description.Value, Is.EqualTo("Base Description"));
            Assert.That(method.SymbolicName, Is.EqualTo(new XmlQualifiedName("BaseSymbol")));
            Assert.That(method.SymbolicId, Is.EqualTo(new XmlQualifiedName("BaseId")));
            Assert.That(method.IsDeclaration, Is.True);
            Assert.That(method.NumericId, Is.EqualTo(999));
            Assert.That(method.NumericIdSpecified, Is.True);
            Assert.That(method.StringId, Is.EqualTo("StringId123"));
            Assert.That(method.WriteAccess, Is.EqualTo(1));
            Assert.That(method.PartNo, Is.EqualTo(2));
            Assert.That(method.Category, Is.EqualTo("BaseCategory"));
            Assert.That(method.NotInAddressSpace, Is.True);
            Assert.That(method.ReleaseStatus, Is.EqualTo(ReleaseStatus.Deprecated));
            Assert.That(method.Purpose, Is.EqualTo(DataTypePurpose.ServicesOnly));
            Assert.That(method.IsDynamic, Is.True);
            Assert.That(method.ReferenceType, Is.EqualTo(new XmlQualifiedName("RefType")));
            Assert.That(method.Declaration, Is.EqualTo(new XmlQualifiedName("Decl")));
            Assert.That(method.TypeDefinition, Is.EqualTo(new XmlQualifiedName("TypeDef")));
            Assert.That(method.ModellingRule, Is.EqualTo(ModellingRule.Optional));
            Assert.That(method.ModellingRuleSpecified, Is.True);
            Assert.That(method.MinCardinality, Is.EqualTo(1));
            Assert.That(method.MaxCardinality, Is.EqualTo(10));
            Assert.That(method.PreserveDefaultAttributes, Is.True);
            Assert.That(method.DesignToolOnly, Is.True);
        }

        /// <summary>
        /// Tests ToNodeDesign with minimum required properties.
        /// Input: MethodDesignJson with only required properties (non-nullable) set.
        /// Expected: Returns a valid MethodDesign with default or null values for optional properties.
        /// </summary>
        [Test]
        public void MethodDesignJsonToNodeDesign_MinimalProperties_ReturnsValidMethodDesign()
        {
            // Arrange
            var methodJson = new MethodDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                InputArguments: null,
                OutputArguments: null,
                NonExecutable: null
            );

            // Act
            var result = methodJson.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<MethodDesign>());
            var method = (MethodDesign)result;
            Assert.That(method.InputArguments, Is.Null);
            Assert.That(method.OutputArguments, Is.Null);
            Assert.That(method.NonExecutableSpecified, Is.False);
        }

        /// <summary>
        /// Tests ToNodeDesign with boundary values for cardinality properties.
        /// Input: MethodDesignJson with MinCardinality and MaxCardinality set to boundary values.
        /// Expected: Returns a MethodDesign with cardinality values correctly assigned.
        /// </summary>
        [Test]
        public void MethodDesignJsonToNodeDesign_BoundaryCardinality_SetsCardinalityCorrectly()
        {
            // Arrange
            var methodJson = new MethodDesignJson(
                BrowseName: "TestMethod",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: uint.MinValue,
                MaxCardinality: uint.MaxValue,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                InputArguments: null,
                OutputArguments: null,
                NonExecutable: null
            );

            // Act
            var result = methodJson.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<MethodDesign>());
            var method = (MethodDesign)result;
            Assert.That(method.MinCardinality, Is.EqualTo(uint.MinValue));
            Assert.That(method.MaxCardinality, Is.EqualTo(uint.MaxValue));
        }

        /// <summary>
        /// Tests ToNodeDesign with all null/default properties.
        /// Verifies that a valid DataTypeDesign is created with default values.
        /// Expected: DataTypeDesign is returned with all boolean flags set to their record values.
        /// </summary>
        [Test]
        public void DataTypeDesignJsonToNodeDesign_AllNullProperties_ReturnsDataTypeDesignWithDefaults()
        {
            // Arrange
            var json = new DataTypeDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                Fields: null,
                Encodings: null,
                IsOptionSet: false,
                IsUnion: false,
                NoArraysAllowed: false,
                ForceEnumValues: false,
                NoEncodings: false);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.TypeOf<DataTypeDesign>());
            var dataType = (DataTypeDesign)result;
            Assert.That(dataType.IsOptionSet, Is.False);
            Assert.That(dataType.IsUnion, Is.False);
            Assert.That(dataType.NoArraysAllowed, Is.False);
            Assert.That(dataType.ForceEnumValues, Is.False);
            Assert.That(dataType.NoEncodings, Is.False);
        }

        /// <summary>
        /// Tests ToNodeDesign with Fields being null.
        /// Input: Fields property is null.
        /// Expected: DataTypeDesign.Fields is not assigned (remains default).
        /// </summary>
        [Test]
        public void DataTypeDesignJsonToNodeDesign_FieldsNull_DoesNotAssignFields()
        {
            // Arrange
            var json = new DataTypeDesignJson(
                BrowseName: "TestType",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                Fields: null,
                Encodings: null,
                IsOptionSet: false,
                IsUnion: false,
                NoArraysAllowed: false,
                ForceEnumValues: false,
                NoEncodings: false);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            var dataType = (DataTypeDesign)result;
            Assert.That(dataType.Fields, Is.Null.Or.Empty);
        }

        /// <summary>
        /// Tests ToNodeDesign with Fields being an empty array.
        /// Input: Fields property is an empty array.
        /// Expected: DataTypeDesign.Fields is assigned as an empty array.
        /// </summary>
        [Test]
        public void DataTypeDesignJsonToNodeDesign_FieldsEmpty_AssignsEmptyFieldsArray()
        {
            // Arrange
            var json = new DataTypeDesignJson(
                BrowseName: "TestType",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                Fields: [],
                Encodings: null,
                IsOptionSet: false,
                IsUnion: false,
                NoArraysAllowed: false,
                ForceEnumValues: false,
                NoEncodings: false);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            var dataType = (DataTypeDesign)result;
            Assert.That(dataType.Fields, Is.Not.Null);
            Assert.That(dataType.Fields, Is.Empty);
        }

        /// <summary>
        /// Tests ToNodeDesign with a single Field element.
        /// Input: Fields property contains one ParameterJson.
        /// Expected: DataTypeDesign.Fields contains one converted Parameter.
        /// </summary>
        [Test]
        public void DataTypeDesignJsonToNodeDesign_SingleField_AssignsOneParameter()
        {
            // Arrange
            var fieldJson = new ParameterJson(
                Description: null,
                DefaultValue: null,
                DisplayName: null,
                Name: "TestField",
                Identifier: 1,
                BitMask: null,
                DataType: null,
                ValueRank: ValueRank.Scalar,
                ArrayDimensions: null,
                AllowSubTypes: false,
                IsOptional: false,
                ReleaseStatus: ReleaseStatus.Released);

            var json = new DataTypeDesignJson(
                BrowseName: "TestType",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                Fields: [fieldJson],
                Encodings: null,
                IsOptionSet: false,
                IsUnion: false,
                NoArraysAllowed: false,
                ForceEnumValues: false,
                NoEncodings: false);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            var dataType = (DataTypeDesign)result;
            Assert.That(dataType.Fields, Is.Not.Null);
            Assert.That(dataType.Fields.Length, Is.EqualTo(1));
            Assert.That(dataType.Fields[0].Name, Is.EqualTo("TestField"));
        }

        /// <summary>
        /// Tests ToNodeDesign with multiple Field elements.
        /// Input: Fields property contains multiple ParameterJson elements.
        /// Expected: DataTypeDesign.Fields contains all converted Parameters in order.
        /// </summary>
        [Test]
        public void DataTypeDesignJsonToNodeDesign_MultipleFields_AssignsAllParameters()
        {
            // Arrange
            var field1 = new ParameterJson(
                Description: null,
                DefaultValue: null,
                DisplayName: null,
                Name: "Field1",
                Identifier: 1,
                BitMask: null,
                DataType: null,
                ValueRank: ValueRank.Scalar,
                ArrayDimensions: null,
                AllowSubTypes: false,
                IsOptional: false,
                ReleaseStatus: ReleaseStatus.Released);

            var field2 = new ParameterJson(
                Description: null,
                DefaultValue: null,
                DisplayName: null,
                Name: "Field2",
                Identifier: 2,
                BitMask: null,
                DataType: null,
                ValueRank: ValueRank.Scalar,
                ArrayDimensions: null,
                AllowSubTypes: false,
                IsOptional: false,
                ReleaseStatus: ReleaseStatus.Released);

            var json = new DataTypeDesignJson(
                BrowseName: "TestType",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                Fields: [field1, field2],
                Encodings: null,
                IsOptionSet: false,
                IsUnion: false,
                NoArraysAllowed: false,
                ForceEnumValues: false,
                NoEncodings: false);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            var dataType = (DataTypeDesign)result;
            Assert.That(dataType.Fields, Is.Not.Null);
            Assert.That(dataType.Fields.Length, Is.EqualTo(2));
            Assert.That(dataType.Fields[0].Name, Is.EqualTo("Field1"));
            Assert.That(dataType.Fields[1].Name, Is.EqualTo("Field2"));
        }

        /// <summary>
        /// Tests ToNodeDesign with Encodings being null.
        /// Input: Encodings property is null.
        /// Expected: DataTypeDesign.Encodings is not assigned (remains default).
        /// </summary>
        [Test]
        public void DataTypeDesignJsonToNodeDesign_EncodingsNull_DoesNotAssignEncodings()
        {
            // Arrange
            var json = new DataTypeDesignJson(
                BrowseName: "TestType",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                Fields: null,
                Encodings: null,
                IsOptionSet: false,
                IsUnion: false,
                NoArraysAllowed: false,
                ForceEnumValues: false,
                NoEncodings: false);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            var dataType = (DataTypeDesign)result;
            Assert.That(dataType.Encodings, Is.Null.Or.Empty);
        }

        /// <summary>
        /// Tests ToNodeDesign with Encodings being an empty array.
        /// Input: Encodings property is an empty array.
        /// Expected: DataTypeDesign.Encodings is assigned as an empty array.
        /// </summary>
        [Test]
        public void DataTypeDesignJsonToNodeDesign_EncodingsEmpty_AssignsEmptyEncodingsArray()
        {
            // Arrange
            var json = new DataTypeDesignJson(
                BrowseName: "TestType",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                Fields: null,
                Encodings: [],
                IsOptionSet: false,
                IsUnion: false,
                NoArraysAllowed: false,
                ForceEnumValues: false,
                NoEncodings: false);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            var dataType = (DataTypeDesign)result;
            Assert.That(dataType.Encodings, Is.Not.Null);
            Assert.That(dataType.Encodings, Is.Empty);
        }

        /// <summary>
        /// Tests ToNodeDesign with a single Encoding element.
        /// Input: Encodings property contains one EncodingDesignJson.
        /// Expected: DataTypeDesign.Encodings contains one converted EncodingDesign.
        /// </summary>
        [Test]
        public void DataTypeDesignJsonToNodeDesign_SingleEncoding_AssignsOneEncodingDesign()
        {
            // Arrange
            var encodingJson = new EncodingDesignJson(
                BrowseName: "TestEncoding",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                SupportsEvents: null);

            var json = new DataTypeDesignJson(
                BrowseName: "TestType",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                Fields: null,
                Encodings: [encodingJson],
                IsOptionSet: false,
                IsUnion: false,
                NoArraysAllowed: false,
                ForceEnumValues: false,
                NoEncodings: false);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            var dataType = (DataTypeDesign)result;
            Assert.That(dataType.Encodings, Is.Not.Null);
            Assert.That(dataType.Encodings.Length, Is.EqualTo(1));
            Assert.That(dataType.Encodings[0], Is.TypeOf<EncodingDesign>());
            Assert.That(dataType.Encodings[0].BrowseName, Is.EqualTo("TestEncoding"));
        }

        /// <summary>
        /// Tests ToNodeDesign with multiple Encoding elements.
        /// Input: Encodings property contains multiple EncodingDesignJson elements.
        /// Expected: DataTypeDesign.Encodings contains all converted EncodingDesign objects in order.
        /// </summary>
        [Test]
        public void DataTypeDesignJsonToNodeDesign_MultipleEncodings_AssignsAllEncodingDesigns()
        {
            // Arrange
            var encoding1 = new EncodingDesignJson(
                BrowseName: "Encoding1",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                SupportsEvents: null);

            var encoding2 = new EncodingDesignJson(
                BrowseName: "Encoding2",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                SupportsEvents: null);

            var json = new DataTypeDesignJson(
                BrowseName: "TestType",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                Fields: null,
                Encodings: [encoding1, encoding2],
                IsOptionSet: false,
                IsUnion: false,
                NoArraysAllowed: false,
                ForceEnumValues: false,
                NoEncodings: false);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            var dataType = (DataTypeDesign)result;
            Assert.That(dataType.Encodings, Is.Not.Null);
            Assert.That(dataType.Encodings.Length, Is.EqualTo(2));
            Assert.That(dataType.Encodings[0].BrowseName, Is.EqualTo("Encoding1"));
            Assert.That(dataType.Encodings[1].BrowseName, Is.EqualTo("Encoding2"));
        }

        /// <summary>
        /// Tests ToNodeDesign with all boolean flags set to true.
        /// Input: IsOptionSet, IsUnion, NoArraysAllowed, ForceEnumValues, NoEncodings all set to true.
        /// Expected: DataTypeDesign has all corresponding boolean properties set to true.
        /// </summary>
        [Test]
        public void DataTypeDesignJsonToNodeDesign_AllBooleanFlagsTrue_SetsBooleanPropertiesCorrectly()
        {
            // Arrange
            var json = new DataTypeDesignJson(
                BrowseName: "TestType",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                Fields: null,
                Encodings: null,
                IsOptionSet: true,
                IsUnion: true,
                NoArraysAllowed: true,
                ForceEnumValues: true,
                NoEncodings: true);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            var dataType = (DataTypeDesign)result;
            Assert.That(dataType.IsOptionSet, Is.True);
            Assert.That(dataType.IsUnion, Is.True);
            Assert.That(dataType.NoArraysAllowed, Is.True);
            Assert.That(dataType.ForceEnumValues, Is.True);
            Assert.That(dataType.NoEncodings, Is.True);
        }

        /// <summary>
        /// Tests ToNodeDesign with mixed boolean flag values.
        /// Input: Mixed true/false values for boolean properties.
        /// Expected: DataTypeDesign has boolean properties set according to input values.
        /// </summary>
        [TestCase(true, false, true, false, true)]
        [TestCase(false, true, false, true, false)]
        [TestCase(true, true, false, false, false)]
        [TestCase(false, false, true, true, true)]
        public void DataTypeDesignJsonToNodeDesign_MixedBooleanFlags_SetsBooleanPropertiesCorrectly(
            bool isOptionSet,
            bool isUnion,
            bool noArraysAllowed,
            bool forceEnumValues,
            bool noEncodings)
        {
            // Arrange
            var json = new DataTypeDesignJson(
                BrowseName: "TestType",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                Fields: null,
                Encodings: null,
                IsOptionSet: isOptionSet,
                IsUnion: isUnion,
                NoArraysAllowed: noArraysAllowed,
                ForceEnumValues: forceEnumValues,
                NoEncodings: noEncodings);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            var dataType = (DataTypeDesign)result;
            Assert.That(dataType.IsOptionSet, Is.EqualTo(isOptionSet));
            Assert.That(dataType.IsUnion, Is.EqualTo(isUnion));
            Assert.That(dataType.NoArraysAllowed, Is.EqualTo(noArraysAllowed));
            Assert.That(dataType.ForceEnumValues, Is.EqualTo(forceEnumValues));
            Assert.That(dataType.NoEncodings, Is.EqualTo(noEncodings));
        }

        /// <summary>
        /// Tests ToNodeDesign with both Fields and Encodings populated.
        /// Input: Both Fields and Encodings arrays contain elements.
        /// Expected: DataTypeDesign has both Fields and Encodings arrays populated correctly.
        /// </summary>
        [Test]
        public void DataTypeDesignJsonToNodeDesign_WithFieldsAndEncodings_AssignsBothCorrectly()
        {
            // Arrange
            var field = new ParameterJson(
                Description: null,
                DefaultValue: null,
                DisplayName: null,
                Name: "TestField",
                Identifier: 1,
                BitMask: null,
                DataType: null,
                ValueRank: ValueRank.Scalar,
                ArrayDimensions: null,
                AllowSubTypes: false,
                IsOptional: false,
                ReleaseStatus: ReleaseStatus.Released);

            var encoding = new EncodingDesignJson(
                BrowseName: "TestEncoding",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                SupportsEvents: null);

            var json = new DataTypeDesignJson(
                BrowseName: "TestType",
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false,
                Fields: [field],
                Encodings: [encoding],
                IsOptionSet: true,
                IsUnion: false,
                NoArraysAllowed: true,
                ForceEnumValues: false,
                NoEncodings: false);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            var dataType = (DataTypeDesign)result;
            Assert.That(dataType.Fields, Is.Not.Null);
            Assert.That(dataType.Fields.Length, Is.EqualTo(1));
            Assert.That(dataType.Encodings, Is.Not.Null);
            Assert.That(dataType.Encodings.Length, Is.EqualTo(1));
            Assert.That(dataType.IsOptionSet, Is.True);
            Assert.That(dataType.NoArraysAllowed, Is.True);
        }

        /// <summary>
        /// Tests ToNodeDesign verifies that ApplyTo is called to set parent properties.
        /// Input: DataTypeDesignJson with specific parent properties set (BrowseName, ClassName, etc.).
        /// Expected: DataTypeDesign has parent properties correctly set via ApplyTo.
        /// </summary>
        [Test]
        public void DataTypeDesignJsonToNodeDesign_CallsApplyToSetParentProperties_SetsPropertiesCorrectly()
        {
            // Arrange
            const string browseName = "MyDataType";
            const string className = "MyClassName";
            var baseType = new XmlQualifiedName("BaseType", "http://test.com");
            var json = new DataTypeDesignJson(
                BrowseName: browseName,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: className,
                BaseType: baseType,
                IsAbstract: true,
                NoClassGeneration: true,
                Fields: null,
                Encodings: null,
                IsOptionSet: false,
                IsUnion: false,
                NoArraysAllowed: false,
                ForceEnumValues: false,
                NoEncodings: false);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            var dataType = (DataTypeDesign)result;
            Assert.That(dataType.BrowseName, Is.EqualTo(browseName));
            Assert.That(dataType.ClassName, Is.EqualTo(className));
            Assert.That(dataType.BaseType, Is.EqualTo(baseType));
            Assert.That(dataType.IsAbstract, Is.True);
            Assert.That(dataType.NoClassGeneration, Is.True);
        }

        /// <summary>
        /// Tests ToParameter with all nullable properties set to null and default values for non-nullable properties.
        /// Input: ParameterJson with all nullable properties null, ValueRank.Scalar, AllowSubTypes=false, IsOptional=false, ReleaseStatus.Released.
        /// Expected: Returns a Parameter with all corresponding properties null/default and IdentifierSpecified=false.
        /// </summary>
        [Test]
        public void ParameterJsonToParameter_AllNullablePropertiesNull_ReturnsParameterWithNullProperties()
        {
            // Arrange
            var json = new ParameterJson(
                Description: null,
                DefaultValue: null,
                DisplayName: null,
                Name: null,
                Identifier: null,
                BitMask: null,
                DataType: null,
                ValueRank: ValueRank.Scalar,
                ArrayDimensions: null,
                AllowSubTypes: false,
                IsOptional: false,
                ReleaseStatus: ReleaseStatus.Released
            );

            // Act
            var result = json.ToParameter();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Name, Is.Null);
            Assert.That(result.Description, Is.Null);
            Assert.That(result.DefaultValue, Is.Null);
            Assert.That(result.DisplayName, Is.Null);
            Assert.That(result.BitMask, Is.Null);
            Assert.That(result.DataType, Is.Null);
            Assert.That(result.ArrayDimensions, Is.Null);
            Assert.That(result.IdentifierSpecified, Is.False);
            Assert.That(result.ValueRank, Is.EqualTo(ValueRank.Scalar));
            Assert.That(result.AllowSubTypes, Is.False);
            Assert.That(result.IsOptional, Is.False);
            Assert.That(result.ReleaseStatus, Is.EqualTo(ReleaseStatus.Released));
        }

        /// <summary>
        /// Tests ToParameter with all properties set to valid values.
        /// Input: ParameterJson with all properties populated with valid values.
        /// Expected: Returns a Parameter with all properties correctly copied.
        /// </summary>
        [Test]
        public void ParameterJsonToParameter_AllPropertiesSet_ReturnsParameterWithAllPropertiesSet()
        {
            // Arrange
            var description = new LocalizedText { Key = "desc", Value = "Description" };
            var displayName = new LocalizedText { Key = "display", Value = "Display Name" };
            XmlElement defaultValue = new XmlDocument().CreateElement("Value");
            var dataType = new XmlQualifiedName("Int32", "http://opcfoundation.org/UA/");
            const string name = "TestParameter";
            const decimal identifier = 123.456m;
            const string bitMask = "0xFF";
            const string arrayDimensions = "10,20";

            var json = new ParameterJson(
                Description: description,
                DefaultValue: defaultValue,
                DisplayName: displayName,
                Name: name,
                Identifier: identifier,
                BitMask: bitMask,
                DataType: dataType,
                ValueRank: ValueRank.Array,
                ArrayDimensions: arrayDimensions,
                AllowSubTypes: true,
                IsOptional: true,
                ReleaseStatus: ReleaseStatus.Draft
            );

            // Act
            var result = json.ToParameter();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result.Name, Is.EqualTo(name));
            Assert.That(result.Description, Is.EqualTo(description));
            Assert.That(result.DefaultValue, Is.EqualTo(defaultValue));
            Assert.That(result.DisplayName, Is.EqualTo(displayName));
            Assert.That(result.BitMask, Is.EqualTo(bitMask));
            Assert.That(result.DataType, Is.EqualTo(dataType));
            Assert.That(result.ArrayDimensions, Is.EqualTo(arrayDimensions));
            Assert.That(result.Identifier, Is.EqualTo(identifier));
            Assert.That(result.IdentifierSpecified, Is.True);
            Assert.That(result.ValueRank, Is.EqualTo(ValueRank.Array));
            Assert.That(result.AllowSubTypes, Is.True);
            Assert.That(result.IsOptional, Is.True);
            Assert.That(result.ReleaseStatus, Is.EqualTo(ReleaseStatus.Draft));
        }

        /// <summary>
        /// Tests ToParameter with Identifier set to null.
        /// Input: ParameterJson with Identifier = null.
        /// Expected: Returns a Parameter with IdentifierSpecified = false and Identifier not set.
        /// </summary>
        [Test]
        public void ParameterJsonToParameter_IdentifierNull_IdentifierSpecifiedIsFalse()
        {
            // Arrange
            var json = new ParameterJson(
                Description: null,
                DefaultValue: null,
                DisplayName: null,
                Name: "Test",
                Identifier: null,
                BitMask: null,
                DataType: null,
                ValueRank: ValueRank.Scalar,
                ArrayDimensions: null,
                AllowSubTypes: false,
                IsOptional: false,
                ReleaseStatus: ReleaseStatus.Released
            );

            // Act
            var result = json.ToParameter();

            // Assert
            Assert.That(result.IdentifierSpecified, Is.False);
        }

        /// <summary>
        /// Tests ToParameter with Identifier set to a value.
        /// Input: ParameterJson with Identifier = 42.5m.
        /// Expected: Returns a Parameter with IdentifierSpecified = true and Identifier = 42.5m.
        /// </summary>
        [Test]
        public void ParameterJsonToParameter_IdentifierHasValue_IdentifierSpecifiedIsTrue()
        {
            // Arrange
            const decimal identifierValue = 42.5m;
            var json = new ParameterJson(
                Description: null,
                DefaultValue: null,
                DisplayName: null,
                Name: "Test",
                Identifier: identifierValue,
                BitMask: null,
                DataType: null,
                ValueRank: ValueRank.Scalar,
                ArrayDimensions: null,
                AllowSubTypes: false,
                IsOptional: false,
                ReleaseStatus: ReleaseStatus.Released
            );

            // Act
            var result = json.ToParameter();

            // Assert
            Assert.That(result.IdentifierSpecified, Is.True);
            Assert.That(result.Identifier, Is.EqualTo(identifierValue));
        }

        /// <summary>
        /// Tests ToParameter with Identifier set to decimal.MinValue.
        /// Input: ParameterJson with Identifier = decimal.MinValue.
        /// Expected: Returns a Parameter with Identifier = decimal.MinValue and IdentifierSpecified = true.
        /// </summary>
        [Test]
        public void ParameterJsonToParameter_IdentifierMinValue_ReturnsParameterWithMinValue()
        {
            // Arrange
            var json = new ParameterJson(
                Description: null,
                DefaultValue: null,
                DisplayName: null,
                Name: null,
                Identifier: decimal.MinValue,
                BitMask: null,
                DataType: null,
                ValueRank: ValueRank.Scalar,
                ArrayDimensions: null,
                AllowSubTypes: false,
                IsOptional: false,
                ReleaseStatus: ReleaseStatus.Released
            );

            // Act
            var result = json.ToParameter();

            // Assert
            Assert.That(result.Identifier, Is.EqualTo(decimal.MinValue));
            Assert.That(result.IdentifierSpecified, Is.True);
        }

        /// <summary>
        /// Tests ToParameter with Identifier set to decimal.MaxValue.
        /// Input: ParameterJson with Identifier = decimal.MaxValue.
        /// Expected: Returns a Parameter with Identifier = decimal.MaxValue and IdentifierSpecified = true.
        /// </summary>
        [Test]
        public void ParameterJsonToParameter_IdentifierMaxValue_ReturnsParameterWithMaxValue()
        {
            // Arrange
            var json = new ParameterJson(
                Description: null,
                DefaultValue: null,
                DisplayName: null,
                Name: null,
                Identifier: decimal.MaxValue,
                BitMask: null,
                DataType: null,
                ValueRank: ValueRank.Scalar,
                ArrayDimensions: null,
                AllowSubTypes: false,
                IsOptional: false,
                ReleaseStatus: ReleaseStatus.Released
            );

            // Act
            var result = json.ToParameter();

            // Assert
            Assert.That(result.Identifier, Is.EqualTo(decimal.MaxValue));
            Assert.That(result.IdentifierSpecified, Is.True);
        }

        /// <summary>
        /// Tests ToParameter with Identifier set to zero.
        /// Input: ParameterJson with Identifier = 0.
        /// Expected: Returns a Parameter with Identifier = 0 and IdentifierSpecified = true.
        /// </summary>
        [Test]
        public void ParameterJsonToParameter_IdentifierZero_ReturnsParameterWithZero()
        {
            // Arrange
            var json = new ParameterJson(
                Description: null,
                DefaultValue: null,
                DisplayName: null,
                Name: null,
                Identifier: 0m,
                BitMask: null,
                DataType: null,
                ValueRank: ValueRank.Scalar,
                ArrayDimensions: null,
                AllowSubTypes: false,
                IsOptional: false,
                ReleaseStatus: ReleaseStatus.Released
            );

            // Act
            var result = json.ToParameter();

            // Assert
            Assert.That(result.Identifier, Is.EqualTo(0m));
            Assert.That(result.IdentifierSpecified, Is.True);
        }

        /// <summary>
        /// Tests ToParameter with Identifier set to a negative value.
        /// Input: ParameterJson with Identifier = -123.45m.
        /// Expected: Returns a Parameter with Identifier = -123.45m and IdentifierSpecified = true.
        /// </summary>
        [Test]
        public void ParameterJsonToParameter_IdentifierNegative_ReturnsParameterWithNegativeValue()
        {
            // Arrange
            const decimal negativeValue = -123.45m;
            var json = new ParameterJson(
                Description: null,
                DefaultValue: null,
                DisplayName: null,
                Name: null,
                Identifier: negativeValue,
                BitMask: null,
                DataType: null,
                ValueRank: ValueRank.Scalar,
                ArrayDimensions: null,
                AllowSubTypes: false,
                IsOptional: false,
                ReleaseStatus: ReleaseStatus.Released
            );

            // Act
            var result = json.ToParameter();

            // Assert
            Assert.That(result.Identifier, Is.EqualTo(negativeValue));
            Assert.That(result.IdentifierSpecified, Is.True);
        }

        /// <summary>
        /// Tests ToParameter with Name set to an empty string.
        /// Input: ParameterJson with Name = string.Empty.
        /// Expected: Returns a Parameter with Name = string.Empty.
        /// </summary>
        [Test]
        public void ParameterJsonToParameter_NameEmpty_ReturnsParameterWithEmptyName()
        {
            // Arrange
            var json = new ParameterJson(
                Description: null,
                DefaultValue: null,
                DisplayName: null,
                Name: string.Empty,
                Identifier: null,
                BitMask: null,
                DataType: null,
                ValueRank: ValueRank.Scalar,
                ArrayDimensions: null,
                AllowSubTypes: false,
                IsOptional: false,
                ReleaseStatus: ReleaseStatus.Released
            );

            // Act
            var result = json.ToParameter();

            // Assert
            Assert.That(result.Name, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests ToParameter with Name set to a whitespace-only string.
        /// Input: ParameterJson with Name = "   ".
        /// Expected: Returns a Parameter with Name = "   ".
        /// </summary>
        [Test]
        public void ParameterJsonToParameter_NameWhitespace_ReturnsParameterWithWhitespaceName()
        {
            // Arrange
            const string whitespaceName = "   ";
            var json = new ParameterJson(
                Description: null,
                DefaultValue: null,
                DisplayName: null,
                Name: whitespaceName,
                Identifier: null,
                BitMask: null,
                DataType: null,
                ValueRank: ValueRank.Scalar,
                ArrayDimensions: null,
                AllowSubTypes: false,
                IsOptional: false,
                ReleaseStatus: ReleaseStatus.Released
            );

            // Act
            var result = json.ToParameter();

            // Assert
            Assert.That(result.Name, Is.EqualTo(whitespaceName));
        }

        /// <summary>
        /// Tests ToParameter with BitMask set to an empty string.
        /// Input: ParameterJson with BitMask = string.Empty.
        /// Expected: Returns a Parameter with BitMask = string.Empty.
        /// </summary>
        [Test]
        public void ParameterJsonToParameter_BitMaskEmpty_ReturnsParameterWithEmptyBitMask()
        {
            // Arrange
            var json = new ParameterJson(
                Description: null,
                DefaultValue: null,
                DisplayName: null,
                Name: null,
                Identifier: null,
                BitMask: string.Empty,
                DataType: null,
                ValueRank: ValueRank.Scalar,
                ArrayDimensions: null,
                AllowSubTypes: false,
                IsOptional: false,
                ReleaseStatus: ReleaseStatus.Released
            );

            // Act
            var result = json.ToParameter();

            // Assert
            Assert.That(result.BitMask, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests ToParameter with ArrayDimensions set to an empty string.
        /// Input: ParameterJson with ArrayDimensions = string.Empty.
        /// Expected: Returns a Parameter with ArrayDimensions = string.Empty.
        /// </summary>
        [Test]
        public void ParameterJsonToParameter_ArrayDimensionsEmpty_ReturnsParameterWithEmptyArrayDimensions()
        {
            // Arrange
            var json = new ParameterJson(
                Description: null,
                DefaultValue: null,
                DisplayName: null,
                Name: null,
                Identifier: null,
                BitMask: null,
                DataType: null,
                ValueRank: ValueRank.Scalar,
                ArrayDimensions: string.Empty,
                AllowSubTypes: false,
                IsOptional: false,
                ReleaseStatus: ReleaseStatus.Released
            );

            // Act
            var result = json.ToParameter();

            // Assert
            Assert.That(result.ArrayDimensions, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// Tests ToParameter with all ValueRank enum values.
        /// Input: ParameterJson with each ValueRank enum value.
        /// Expected: Returns a Parameter with the correct ValueRank value.
        /// </summary>
        [TestCase(ValueRank.Scalar)]
        [TestCase(ValueRank.Array)]
        [TestCase(ValueRank.ScalarOrArray)]
        [TestCase(ValueRank.OneOrMoreDimensions)]
        [TestCase(ValueRank.ScalarOrOneDimension)]
        [TestCase(ValueRank.Any)]
        public void ParameterJsonToParameter_ValueRankVariousValues_ReturnsParameterWithCorrectValueRank(
            ValueRank valueRank)
        {
            // Arrange
            var json = new ParameterJson(
                Description: null,
                DefaultValue: null,
                DisplayName: null,
                Name: null,
                Identifier: null,
                BitMask: null,
                DataType: null,
                ValueRank: valueRank,
                ArrayDimensions: null,
                AllowSubTypes: false,
                IsOptional: false,
                ReleaseStatus: ReleaseStatus.Released
            );

            // Act
            var result = json.ToParameter();

            // Assert
            Assert.That(result.ValueRank, Is.EqualTo(valueRank));
        }

        /// <summary>
        /// Tests ToParameter with all ReleaseStatus enum values.
        /// Input: ParameterJson with each ReleaseStatus enum value.
        /// Expected: Returns a Parameter with the correct ReleaseStatus value.
        /// </summary>
        [TestCase(ReleaseStatus.Released)]
        [TestCase(ReleaseStatus.RC)]
        [TestCase(ReleaseStatus.Draft)]
        [TestCase(ReleaseStatus.Deprecated)]
        public void ParameterJsonToParameter_ReleaseStatusVariousValues_ReturnsParameterWithCorrectReleaseStatus(
            ReleaseStatus releaseStatus)
        {
            // Arrange
            var json = new ParameterJson(
                Description: null,
                DefaultValue: null,
                DisplayName: null,
                Name: null,
                Identifier: null,
                BitMask: null,
                DataType: null,
                ValueRank: ValueRank.Scalar,
                ArrayDimensions: null,
                AllowSubTypes: false,
                IsOptional: false,
                ReleaseStatus: releaseStatus
            );

            // Act
            var result = json.ToParameter();

            // Assert
            Assert.That(result.ReleaseStatus, Is.EqualTo(releaseStatus));
        }

        /// <summary>
        /// Tests ToParameter with AllowSubTypes set to true.
        /// Input: ParameterJson with AllowSubTypes = true.
        /// Expected: Returns a Parameter with AllowSubTypes = true.
        /// </summary>
        [Test]
        public void ParameterJsonToParameter_AllowSubTypesTrue_ReturnsParameterWithAllowSubTypesTrue()
        {
            // Arrange
            var json = new ParameterJson(
                Description: null,
                DefaultValue: null,
                DisplayName: null,
                Name: null,
                Identifier: null,
                BitMask: null,
                DataType: null,
                ValueRank: ValueRank.Scalar,
                ArrayDimensions: null,
                AllowSubTypes: true,
                IsOptional: false,
                ReleaseStatus: ReleaseStatus.Released
            );

            // Act
            var result = json.ToParameter();

            // Assert
            Assert.That(result.AllowSubTypes, Is.True);
        }

        /// <summary>
        /// Tests ToParameter with AllowSubTypes set to false.
        /// Input: ParameterJson with AllowSubTypes = false.
        /// Expected: Returns a Parameter with AllowSubTypes = false.
        /// </summary>
        [Test]
        public void ParameterJsonToParameter_AllowSubTypesFalse_ReturnsParameterWithAllowSubTypesFalse()
        {
            // Arrange
            var json = new ParameterJson(
                Description: null,
                DefaultValue: null,
                DisplayName: null,
                Name: null,
                Identifier: null,
                BitMask: null,
                DataType: null,
                ValueRank: ValueRank.Scalar,
                ArrayDimensions: null,
                AllowSubTypes: false,
                IsOptional: false,
                ReleaseStatus: ReleaseStatus.Released
            );

            // Act
            var result = json.ToParameter();

            // Assert
            Assert.That(result.AllowSubTypes, Is.False);
        }

        /// <summary>
        /// Tests ToParameter with IsOptional set to true.
        /// Input: ParameterJson with IsOptional = true.
        /// Expected: Returns a Parameter with IsOptional = true.
        /// </summary>
        [Test]
        public void ParameterJsonToParameter_IsOptionalTrue_ReturnsParameterWithIsOptionalTrue()
        {
            // Arrange
            var json = new ParameterJson(
                Description: null,
                DefaultValue: null,
                DisplayName: null,
                Name: null,
                Identifier: null,
                BitMask: null,
                DataType: null,
                ValueRank: ValueRank.Scalar,
                ArrayDimensions: null,
                AllowSubTypes: false,
                IsOptional: true,
                ReleaseStatus: ReleaseStatus.Released
            );

            // Act
            var result = json.ToParameter();

            // Assert
            Assert.That(result.IsOptional, Is.True);
        }

        /// <summary>
        /// Tests ToParameter with IsOptional set to false.
        /// Input: ParameterJson with IsOptional = false.
        /// Expected: Returns a Parameter with IsOptional = false.
        /// </summary>
        [Test]
        public void ParameterJsonToParameter_IsOptionalFalse_ReturnsParameterWithIsOptionalFalse()
        {
            // Arrange
            var json = new ParameterJson(
                Description: null,
                DefaultValue: null,
                DisplayName: null,
                Name: null,
                Identifier: null,
                BitMask: null,
                DataType: null,
                ValueRank: ValueRank.Scalar,
                ArrayDimensions: null,
                AllowSubTypes: false,
                IsOptional: false,
                ReleaseStatus: ReleaseStatus.Released
            );

            // Act
            var result = json.ToParameter();

            // Assert
            Assert.That(result.IsOptional, Is.False);
        }

        /// <summary>
        /// Tests ToParameter with a complex string value for Name containing special characters.
        /// Input: ParameterJson with Name containing special characters.
        /// Expected: Returns a Parameter with Name containing the same special characters.
        /// </summary>
        [Test]
        public void ParameterJsonToParameter_NameWithSpecialCharacters_ReturnsParameterWithSpecialCharactersInName()
        {
            // Arrange
            const string specialName = "Test!@#$%^&*()_+-=[]{}|;:',.<>?/~`";
            var json = new ParameterJson(
                Description: null,
                DefaultValue: null,
                DisplayName: null,
                Name: specialName,
                Identifier: null,
                BitMask: null,
                DataType: null,
                ValueRank: ValueRank.Scalar,
                ArrayDimensions: null,
                AllowSubTypes: false,
                IsOptional: false,
                ReleaseStatus: ReleaseStatus.Released
            );

            // Act
            var result = json.ToParameter();

            // Assert
            Assert.That(result.Name, Is.EqualTo(specialName));
        }

        /// <summary>
        /// Tests ToParameter with a very long string for ArrayDimensions.
        /// Input: ParameterJson with ArrayDimensions containing a very long string.
        /// Expected: Returns a Parameter with ArrayDimensions containing the same long string.
        /// </summary>
        [Test]
        public void ParameterJsonToParameter_ArrayDimensionsVeryLong_ReturnsParameterWithLongArrayDimensions()
        {
            // Arrange
            string longArrayDimensions = new('1', 10000);
            var json = new ParameterJson(
                Description: null,
                DefaultValue: null,
                DisplayName: null,
                Name: null,
                Identifier: null,
                BitMask: null,
                DataType: null,
                ValueRank: ValueRank.Scalar,
                ArrayDimensions: longArrayDimensions,
                AllowSubTypes: false,
                IsOptional: false,
                ReleaseStatus: ReleaseStatus.Released
            );

            // Act
            var result = json.ToParameter();

            // Assert
            Assert.That(result.ArrayDimensions, Is.EqualTo(longArrayDimensions));
        }

        /// <summary>
        /// Tests ToListOfChildren when Items property is null.
        /// Input: ListOfChildrenJson with null Items.
        /// Expected: Returns null.
        /// </summary>
        [Test]
        public void ListOfChildrenJsonToListOfChildren_ItemsIsNull_ReturnsNull()
        {
            // Arrange
            var json = new ListOfChildrenJson(Items: null);

            // Act
            var result = json.ToListOfChildren();

            // Assert
            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Tests that ToNodeDesign returns a non-null TypeDesign instance.
        /// Input: TypeDesignJson with default values.
        /// Expected: Non-null TypeDesign object is returned.
        /// </summary>
        [Test]
        public void TypeDesignJsonToNodeDesign_DefaultValues_ReturnsNonNullTypeDesign()
        {
            // Arrange
            var json = new TypeDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false);

            // Act
            var result = json.ToNodeDesign();

            // Assert
            Assert.That(result, Is.Not.Null);
            Assert.That(result, Is.InstanceOf<TypeDesign>());
        }

        /// <summary>
        /// Tests that ToNodeDesign correctly maps ClassName property.
        /// Input: TypeDesignJson with various ClassName values.
        /// Expected: TypeDesign.ClassName matches the input value.
        /// </summary>
        [TestCase(null)]
        [TestCase("")]
        [TestCase("MyCustomType")]
        [TestCase("Very.Long.Qualified.ClassName.With.Multiple.Parts")]
        public void TypeDesignJsonToNodeDesign_VariousClassNames_CorrectlyMapsClassName(
            string className)
        {
            // Arrange
            var json = new TypeDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: className,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false);

            // Act
            var result = (TypeDesign)json.ToNodeDesign();

            // Assert
            Assert.That(result.ClassName, Is.EqualTo(className));
        }

        /// <summary>
        /// Tests that ToNodeDesign correctly maps BaseType property.
        /// Input: TypeDesignJson with null and valid BaseType values.
        /// Expected: TypeDesign.BaseType matches the input value.
        /// </summary>
        [Test]
        public void TypeDesignJsonToNodeDesign_NullBaseType_CorrectlyMapsBaseType()
        {
            // Arrange
            var json = new TypeDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false);

            // Act
            var result = (TypeDesign)json.ToNodeDesign();

            // Assert
            Assert.That(result.BaseType, Is.Null);
        }

        /// <summary>
        /// Tests that ToNodeDesign correctly maps BaseType property.
        /// Input: TypeDesignJson with a valid XmlQualifiedName BaseType.
        /// Expected: TypeDesign.BaseType matches the input value.
        /// </summary>
        [Test]
        public void TypeDesignJsonToNodeDesign_ValidBaseType_CorrectlyMapsBaseType()
        {
            // Arrange
            var baseType = new XmlQualifiedName("BaseObjectType", "http://opcfoundation.org/UA/");
            var json = new TypeDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: baseType,
                IsAbstract: false,
                NoClassGeneration: false);

            // Act
            var result = (TypeDesign)json.ToNodeDesign();

            // Assert
            Assert.That(result.BaseType, Is.EqualTo(baseType));
        }

        /// <summary>
        /// Tests that ToNodeDesign correctly maps IsAbstract property.
        /// Input: TypeDesignJson with IsAbstract set to true and false.
        /// Expected: TypeDesign.IsAbstract matches the input value.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void TypeDesignJsonToNodeDesign_VariousIsAbstractValues_CorrectlyMapsIsAbstract(
            bool isAbstract)
        {
            // Arrange
            var json = new TypeDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: isAbstract,
                NoClassGeneration: false);

            // Act
            var result = (TypeDesign)json.ToNodeDesign();

            // Assert
            Assert.That(result.IsAbstract, Is.EqualTo(isAbstract));
        }

        /// <summary>
        /// Tests that ToNodeDesign correctly maps NoClassGeneration property.
        /// Input: TypeDesignJson with NoClassGeneration set to true and false.
        /// Expected: TypeDesign.NoClassGeneration matches the input value.
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void TypeDesignJsonToNodeDesign_VariousNoClassGenerationValues_CorrectlyMapsNoClassGeneration(
            bool noClassGeneration)
        {
            // Arrange
            var json = new TypeDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: noClassGeneration);

            // Act
            var result = (TypeDesign)json.ToNodeDesign();

            // Assert
            Assert.That(result.NoClassGeneration, Is.EqualTo(noClassGeneration));
        }

        /// <summary>
        /// Tests that ToNodeDesign correctly maps all TypeDesign-specific properties.
        /// Input: TypeDesignJson with all TypeDesign properties set.
        /// Expected: All properties are correctly mapped to TypeDesign.
        /// </summary>
        [Test]
        public void TypeDesignJsonToNodeDesign_AllTypeDesignPropertiesSet_CorrectlyMapsAllProperties()
        {
            // Arrange
            var baseType = new XmlQualifiedName("MyBaseType", "http://test.org/");
            var json = new TypeDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: "CustomTypeClass",
                BaseType: baseType,
                IsAbstract: true,
                NoClassGeneration: true);

            // Act
            var result = (TypeDesign)json.ToNodeDesign();

            // Assert
            Assert.That(result.ClassName, Is.EqualTo("CustomTypeClass"));
            Assert.That(result.BaseType, Is.EqualTo(baseType));
            Assert.That(result.IsAbstract, Is.True);
            Assert.That(result.NoClassGeneration, Is.True);
        }

        /// <summary>
        /// Tests that ToNodeDesign correctly maps base NodeDesign properties.
        /// Input: TypeDesignJson with BrowseName, DisplayName, and StringId set.
        /// Expected: Base properties are correctly mapped through ApplyTo.
        /// </summary>
        [Test]
        public void TypeDesignJsonToNodeDesign_BasePropertiesSet_CorrectlyMapsBaseProperties()
        {
            // Arrange
            var displayName = new LocalizedText { Value = "Test Display Name" };
            var json = new TypeDesignJson(
                BrowseName: "TestBrowseName",
                DisplayName: displayName,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: true,
                NumericId: null,
                StringId: "TestStringId",
                WriteAccess: 0,
                PartNo: 0,
                Category: "TestCategory",
                NotInAddressSpace: true,
                ReleaseStatus: ReleaseStatus.Draft,
                Purpose: DataTypePurpose.ServicesOnly,
                IsDynamic: true,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false);

            // Act
            var result = (TypeDesign)json.ToNodeDesign();

            // Assert
            Assert.That(result.BrowseName, Is.EqualTo("TestBrowseName"));
            Assert.That(result.DisplayName, Is.EqualTo(displayName));
            Assert.That(result.StringId, Is.EqualTo("TestStringId"));
            Assert.That(result.Category, Is.EqualTo("TestCategory"));
            Assert.That(result.IsDeclaration, Is.True);
            Assert.That(result.NotInAddressSpace, Is.True);
            Assert.That(result.ReleaseStatus, Is.EqualTo(ReleaseStatus.Draft));
            Assert.That(result.Purpose, Is.EqualTo(DataTypePurpose.ServicesOnly));
            Assert.That(result.IsDynamic, Is.True);
        }

        /// <summary>
        /// Tests that ToNodeDesign correctly maps NumericId when it has a value.
        /// Input: TypeDesignJson with NumericId set to a valid value.
        /// Expected: TypeDesign.NumericId and NumericIdSpecified are correctly set.
        /// </summary>
        [TestCase(0u)]
        [TestCase(1u)]
        [TestCase(uint.MaxValue)]
        public void TypeDesignJsonToNodeDesign_NumericIdSet_CorrectlyMapsNumericId(uint numericId)
        {
            // Arrange
            var json = new TypeDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: numericId,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false);

            // Act
            var result = (TypeDesign)json.ToNodeDesign();

            // Assert
            Assert.That(result.NumericId, Is.EqualTo(numericId));
            Assert.That(result.NumericIdSpecified, Is.True);
        }

        /// <summary>
        /// Tests that ToNodeDesign correctly handles null NumericId.
        /// Input: TypeDesignJson with NumericId set to null.
        /// Expected: TypeDesign.NumericIdSpecified is false.
        /// </summary>
        [Test]
        public void TypeDesignJsonToNodeDesign_NumericIdNull_NumericIdSpecifiedIsFalse()
        {
            // Arrange
            var json = new TypeDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false);

            // Act
            var result = (TypeDesign)json.ToNodeDesign();

            // Assert
            Assert.That(result.NumericIdSpecified, Is.False);
        }

        /// <summary>
        /// Tests that ToNodeDesign correctly maps AccessRestrictions when it has a value.
        /// Input: TypeDesignJson with AccessRestrictions set.
        /// Expected: TypeDesign.AccessRestrictions and AccessRestrictionsSpecified are correctly set.
        /// </summary>
        [Test]
        public void TypeDesignJsonToNodeDesign_AccessRestrictionsSet_CorrectlyMapsAccessRestrictions()
        {
            // Arrange
            const AccessRestrictions accessRestrictions = AccessRestrictions.EncryptionRequired;
            var json = new TypeDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: accessRestrictions,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false);

            // Act
            var result = (TypeDesign)json.ToNodeDesign();

            // Assert
            Assert.That(result.AccessRestrictions, Is.EqualTo(accessRestrictions));
            Assert.That(result.AccessRestrictionsSpecified, Is.True);
        }

        /// <summary>
        /// Tests that ToNodeDesign correctly handles null AccessRestrictions.
        /// Input: TypeDesignJson with AccessRestrictions set to null.
        /// Expected: TypeDesign.AccessRestrictionsSpecified is false.
        /// </summary>
        [Test]
        public void TypeDesignJsonToNodeDesign_AccessRestrictionsNull_AccessRestrictionsSpecifiedIsFalse()
        {
            // Arrange
            var json = new TypeDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false);

            // Act
            var result = (TypeDesign)json.ToNodeDesign();

            // Assert
            Assert.That(result.AccessRestrictionsSpecified, Is.False);
        }

        /// <summary>
        /// Tests that ToNodeDesign correctly maps all ReleaseStatus enum values.
        /// Input: TypeDesignJson with different ReleaseStatus values.
        /// Expected: TypeDesign.ReleaseStatus matches the input value.
        /// </summary>
        [TestCase(ReleaseStatus.Released)]
        [TestCase(ReleaseStatus.Draft)]
        [TestCase(ReleaseStatus.Deprecated)]
        public void TypeDesignJsonToNodeDesign_VariousReleaseStatusValues_CorrectlyMapsReleaseStatus(
            ReleaseStatus releaseStatus)
        {
            // Arrange
            var json = new TypeDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: releaseStatus,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false);

            // Act
            var result = (TypeDesign)json.ToNodeDesign();

            // Assert
            Assert.That(result.ReleaseStatus, Is.EqualTo(releaseStatus));
        }

        /// <summary>
        /// Tests that ToNodeDesign correctly maps all DataTypePurpose enum values.
        /// Input: TypeDesignJson with different DataTypePurpose values.
        /// Expected: TypeDesign.Purpose matches the input value.
        /// </summary>
        [TestCase(DataTypePurpose.Normal)]
        [TestCase(DataTypePurpose.ServicesOnly)]
        [TestCase(DataTypePurpose.CodeGenerator)]
        public void TypeDesignJsonToNodeDesign_VariousDataTypePurposeValues_CorrectlyMapsPurpose(
            DataTypePurpose purpose)
        {
            // Arrange
            var json = new TypeDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: purpose,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false);

            // Act
            var result = (TypeDesign)json.ToNodeDesign();

            // Assert
            Assert.That(result.Purpose, Is.EqualTo(purpose));
        }

        /// <summary>
        /// Tests that ToNodeDesign correctly maps extreme uint values for WriteAccess and PartNo.
        /// Input: TypeDesignJson with boundary uint values.
        /// Expected: TypeDesign properties match the input values.
        /// </summary>
        [TestCase(0u, 0u)]
        [TestCase(uint.MaxValue, uint.MaxValue)]
        [TestCase(1u, 42u)]
        public void TypeDesignJsonToNodeDesign_ExtremeBoundaryUIntValues_CorrectlyMapsValues(
            uint writeAccess,
            uint partNo)
        {
            // Arrange
            var json = new TypeDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: writeAccess,
                PartNo: partNo,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ClassName: null,
                BaseType: null,
                IsAbstract: false,
                NoClassGeneration: false);

            // Act
            var result = (TypeDesign)json.ToNodeDesign();

            // Assert
            Assert.That(result.WriteAccess, Is.EqualTo(writeAccess));
            Assert.That(result.PartNo, Is.EqualTo(partNo));
        }

        /// <summary>
        /// Helper method to create a minimal VariableDesignJson with default values.
        /// </summary>
        private static VariableDesignJson CreateMinimalVariableDesignJson()
        {
            return new VariableDesignJson(
                BrowseName: null,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null,
                ReferenceType: null,
                Declaration: null,
                TypeDefinition: null,
                ModellingRule: null,
                MinCardinality: 0,
                MaxCardinality: 0,
                PreserveDefaultAttributes: false,
                DesignToolOnly: false,
                DefaultValue: null,
                DataType: null,
                ValueRank: null,
                ArrayDimensions: null,
                AccessLevel: null,
                InstanceAccessLevel: null,
                MinimumSamplingInterval: null,
                Historizing: null);
        }

        private static Namespace CreateNamespace(string value, string prefix)
        {
            return new Namespace
            {
                Value = value,
                Prefix = prefix
            };
        }

        private static XmlElement CreateXmlElement(string name)
        {
            var doc = new XmlDocument();
            return doc.CreateElement(name);
        }

        private static NodeDesignJson CreateNodeDesignJson(string browseName)
        {
            return new NodeDesignJson(
                BrowseName: browseName,
                DisplayName: null,
                Description: null,
                Children: null,
                References: null,
                RolePermissions: null,
                DefaultRolePermissions: null,
                AccessRestrictions: null,
                DefaultAccessRestrictions: null,
                Extensions: null,
                SymbolicName: null,
                SymbolicId: null,
                IsDeclaration: false,
                NumericId: null,
                StringId: null,
                WriteAccess: 0,
                PartNo: 0,
                Category: null,
                NotInAddressSpace: false,
                ReleaseStatus: ReleaseStatus.Released,
                Purpose: DataTypePurpose.Normal,
                IsDynamic: false,
                NodeType: null);
        }
    }
}
