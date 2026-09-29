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
using System.Linq;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// D2: the StructureType of a structure follows its fields (own and
    /// inherited). Optional fields together with fields that allow subtypes
    /// have no StructureType (OPC 10000-3 8.49, OPC 10000-6 F.13) and are
    /// rejected; IsOptional is ignored for a union (OPC 10000-3 8.51).
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class StructureTypeKindTests
    {
        [Test]
        public void OptionalAndSubtypedFieldsAreRejected()
        {
            Assert.That(
                () => DataTypeModelTests.Generate(Model(
                    """
                    <opc:DataType SymbolicName="Mixed" BaseType="ua:Structure">
                      <opc:Fields>
                        <opc:Field Name="Maybe" DataType="ua:Int32" IsOptional="true" />
                        <opc:Field Name="Any" DataType="Shape" AllowSubTypes="true" />
                      </opc:Fields>
                    </opc:DataType>
                    """)),
                Throws.Exception.With.Message.Contains("Mixed").And.Message.Contains("F.13"));
        }

        [Test]
        public void InheritedOptionalAndSubtypedFieldsAreRejected()
        {
            Assert.That(
                () => DataTypeModelTests.Generate(Model(
                    """
                    <opc:DataType SymbolicName="WithAny" BaseType="ua:Structure">
                      <opc:Fields>
                        <opc:Field Name="Any" DataType="Shape" AllowSubTypes="true" />
                      </opc:Fields>
                    </opc:DataType>
                    <opc:DataType SymbolicName="WithAnyAndMaybe" BaseType="WithAny">
                      <opc:Fields>
                        <opc:Field Name="Maybe" DataType="ua:Int32" IsOptional="true" />
                      </opc:Fields>
                    </opc:DataType>
                    """)),
                Throws.Exception.With.Message.Contains("WithAnyAndMaybe"));
        }

        [Test]
        public void UnionIgnoresIsOptional()
        {
            Dictionary<string, string> generated = DataTypeModelTests.Generate(Model(
                """
                <opc:DataType SymbolicName="Pick" BaseType="ua:Union" IsUnion="true">
                  <opc:Fields>
                    <opc:Field Name="A" DataType="ua:Int32" IsOptional="true" />
                    <opc:Field Name="B" DataType="ua:String" IsOptional="true" />
                  </opc:Fields>
                </opc:DataType>
                <opc:DataType SymbolicName="PickAny" BaseType="ua:Union" IsUnion="true">
                  <opc:Fields>
                    <opc:Field Name="A" DataType="ua:Int32" IsOptional="true" />
                    <opc:Field Name="S" DataType="Shape" AllowSubTypes="true" />
                  </opc:Fields>
                </opc:DataType>
                """));
            string code = string.Join("\n", generated
                .Where(f => f.Key.EndsWith(".cs", StringComparison.Ordinal))
                .Select(f => f.Value));
            string pick = Definition(code, "Pick");
            Assert.That(pick, Does.Contain("StructureType = global::Opc.Ua.StructureType.Union,"));
            Assert.That(pick, Does.Not.Contain("IsOptional = true").IgnoreCase);
            Assert.That(
                Definition(code, "PickAny"),
                Does.Contain("StructureType = global::Opc.Ua.StructureType.UnionWithSubtypedValues,"));
        }

        private static string Definition(string code, string typeName)
        {
            string start = "global::Opc.Ua.StructureDefinition Create" + typeName + "(";
            int begin = code.IndexOf(start, StringComparison.Ordinal);
            Assert.That(begin, Is.GreaterThanOrEqualTo(0), typeName + " definition");
            int end = code.IndexOf("public static", begin + start.Length, StringComparison.Ordinal);
            return end < 0 ? code.Substring(begin) : code.Substring(begin, end - begin);
        }

        private static string Model(string dataTypes)
        {
            return
                $"""
                <?xml version="1.0" encoding="utf-8" ?>
                <opc:ModelDesign
                  xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
                  xmlns:ua="http://opcfoundation.org/UA/"
                  xmlns="http://test.org/UA/SK/"
                  TargetNamespace="http://test.org/UA/SK/">
                  <opc:Namespaces>
                    <opc:Namespace Name="SK" Prefix="Test.SK">http://test.org/UA/SK/</opc:Namespace>
                    <opc:Namespace Name="OpcUa" Prefix="Opc.Ua" XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd">http://opcfoundation.org/UA/</opc:Namespace>
                  </opc:Namespaces>
                  <opc:DataType SymbolicName="Shape" BaseType="ua:Structure" IsAbstract="true">
                    <opc:Fields>
                      <opc:Field Name="Id" DataType="ua:Int32" />
                    </opc:Fields>
                  </opc:DataType>
                {dataTypes}
                </opc:ModelDesign>
                """;
        }
    }
}
