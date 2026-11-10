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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// IsValueType="true" emits a struct, and only for a concrete structure
    /// deriving from Structure without optional fields.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ValueTypeDesignTests
    {
        [Test]
        public void PlainStructureIsGeneratedAsStruct()
        {
            Dictionary<string, string> generated = DataTypeModelTests.Generate(Model(
                """
                <opc:DataType SymbolicName="Point" BaseType="ua:Structure" IsValueType="true">
                  <opc:Fields>
                    <opc:Field Name="X" DataType="ua:Int32" />
                    <opc:Field Name="Y" DataType="ua:Int32" />
                  </opc:Fields>
                </opc:DataType>
                """));
            string code = string.Join("\n", generated
                .Where(f => f.Key.EndsWith(".cs", StringComparison.Ordinal))
                .Select(f => f.Value));
            Assert.That(code, Does.Contain("partial struct Point"));
        }

        [Test]
        public void StructureHoldingValueTypeFieldsCompiles()
        {
            Dictionary<string, string> generated = DataTypeModelTests.Generate(Model(
                """
                <opc:DataType SymbolicName="Point" BaseType="ua:Structure" IsValueType="true">
                  <opc:Fields>
                    <opc:Field Name="X" DataType="ua:Int32" />
                  </opc:Fields>
                </opc:DataType>
                <opc:DataType SymbolicName="Holder" BaseType="ua:Structure">
                  <opc:Fields>
                    <opc:Field Name="One" DataType="Point" />
                    <opc:Field Name="Many" DataType="Point" ValueRank="Array" />
                  </opc:Fields>
                </opc:DataType>
                <opc:DataType SymbolicName="Pair" BaseType="ua:Structure" IsValueType="true">
                  <opc:Fields>
                    <opc:Field Name="First" DataType="Point" />
                    <opc:Field Name="Rest" DataType="Pair" ValueRank="Array" />
                    <opc:Field Name="Owner" DataType="Holder" />
                  </opc:Fields>
                </opc:DataType>
                """));

            string code = string.Join("\n", generated
                .Where(f => f.Key.EndsWith(".cs", StringComparison.Ordinal))
                .Select(f => f.Value));

            // a scalar value type field has a plain setter: comparing it with
            // null is always false.
            Assert.That(code, Does.Contain("set => m_one = value;"));
            Assert.That(code, Does.Contain("set => m_first = value;"));
            Assert.That(CompileProblems(generated), Is.Empty);
        }

        [TestCase(
            """
            <opc:DataType SymbolicName="Bad" BaseType="ua:Structure" IsValueType="true">
              <opc:Fields><opc:Field Name="Self" DataType="Bad" /></opc:Fields>
            </opc:DataType>
            """,
            "contains itself",
            TestName = "DirectSelfContainmentIsRejected")]
        [TestCase(
            """
            <opc:DataType SymbolicName="Bad" BaseType="ua:Structure" IsValueType="true">
              <opc:Fields><opc:Field Name="Next" DataType="Other" /></opc:Fields>
            </opc:DataType>
            <opc:DataType SymbolicName="Other" BaseType="ua:Structure" IsValueType="true">
              <opc:Fields><opc:Field Name="Back" DataType="Bad" /></opc:Fields>
            </opc:DataType>
            """,
            "contains itself",
            TestName = "IndirectSelfContainmentIsRejected")]
        [TestCase(
            """
            <opc:DataType SymbolicName="Base" BaseType="ua:Structure" IsValueType="true">
              <opc:Fields><opc:Field Name="X" DataType="ua:Int32" /></opc:Fields>
            </opc:DataType>
            <opc:DataType SymbolicName="Bad" BaseType="Base">
              <opc:Fields><opc:Field Name="Y" DataType="ua:Int32" /></opc:Fields>
            </opc:DataType>
            """,
            "cannot have subtypes",
            TestName = "SubtypeOfValueTypeIsRejected")]
        public void InvalidValueTypeUseIsRejected(string dataTypes, string reason)
        {
            Assert.That(
                () => DataTypeModelTests.Generate(Model(dataTypes)),
                Throws.Exception.With.Message.Contains("Bad").And.Message.Contains(reason));
        }

        private static List<string> CompileProblems(Dictionary<string, string> generated)
        {
            IEnumerable<KeyValuePair<string, string>> sources = generated
                .Where(f => f.Key.EndsWith(".cs", StringComparison.Ordinal))
                .Append(new KeyValuePair<string, string>("OpcUa.cs", CompilerUtils.OpcUa));
            using var peStream = new MemoryStream();
            EmitResult result = OptimizationLevel.Debug
                .CreateCompilation("ValueTypeDesign.Generated")
                .AddCode(sources, LanguageVersion.Latest)
                .Emit(peStream);

            // CS0472: comparing a value type with null, always false.
            return [.. result.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error || d.Id == "CS0472")
                .Select(d => d.ToString())];
        }

        [TestCase("BaseType=\"ua:Structure\" IsAbstract=\"true\"", "", TestName = "AbstractStructureIsRejected")]
        [TestCase("BaseType=\"ua:Structure\"", "IsOptional=\"true\"", TestName = "OptionalFieldIsRejected")]
        [TestCase("BaseType=\"ua:Union\" IsUnion=\"true\"", "", TestName = "UnionIsRejected")]
        public void InvalidValueTypeIsRejected(string typeAttributes, string fieldAttributes)
        {
            string dataType =
                $"""
                <opc:DataType SymbolicName="Bad" {typeAttributes} IsValueType="true">
                  <opc:Fields>
                    <opc:Field Name="A" DataType="ua:Int32" {fieldAttributes} />
                  </opc:Fields>
                </opc:DataType>
                """;
            Assert.That(
                () => DataTypeModelTests.Generate(Model(dataType)),
                Throws.Exception.With.Message.Contains("Bad").And.Message.Contains("cannot be a value type"));
        }

        private static string Model(string dataTypes)
        {
            return
                $"""
                <?xml version="1.0" encoding="utf-8" ?>
                <opc:ModelDesign
                  xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
                  xmlns:ua="http://opcfoundation.org/UA/"
                  xmlns="http://test.org/UA/VT/"
                  TargetNamespace="http://test.org/UA/VT/">
                  <opc:Namespaces>
                    <opc:Namespace Name="VT" Prefix="Test.VT">http://test.org/UA/VT/</opc:Namespace>
                    <opc:Namespace Name="OpcUa" Prefix="Opc.Ua" XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd">http://opcfoundation.org/UA/</opc:Namespace>
                  </opc:Namespaces>
                {dataTypes}
                </opc:ModelDesign>
                """;
        }
    }
}
