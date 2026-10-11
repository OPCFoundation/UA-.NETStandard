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
using System.Reflection;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Generates and compiles the event records of a companion model.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class EventRecordModelTests
    {
        private const string ModelUri = "http://test.org/UA/Ev/";

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            Dictionary<string, string> generated = DataTypeModelTests.Generate(
                Model,
                omitEventRecords: false);
            m_records = generated
                .Single(f => f.Key.EndsWith("EventRecords.g.cs", StringComparison.Ordinal))
                .Value;
            m_assembly = DataTypeModelTests.Compile(generated);
        }

        /// <summary>
        /// D-15: a select clause is matched including the namespace index
        /// of the browse names, so the browse names a companion model
        /// declares must be in the model's namespace of the session. The
        /// standard fields stay in namespace 0.
        /// </summary>
        [Test]
        public void VendorFieldBrowsePathsUseTheModelNamespace()
        {
            Type decoder = m_assembly.GetType(
                "Test.Ev.MachineEventTypeRecord+Decoder",
                throwOnError: true)!;
            MethodInfo getStandardFields = decoder!.GetMethod(
                "GetStandardFields",
                BindingFlags.Public | BindingFlags.Static)!;
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append("urn:other");
            namespaceUris.Append(ModelUri);

            var paths = (QualifiedName[][])getStandardFields!.Invoke(null, [namespaceUris])!;

            QualifiedName machineId = paths!.Single(p => p[0].Name == "MachineId")[0];
            Assert.That(machineId.NamespaceIndex, Is.EqualTo(2));
            QualifiedName eventId = paths.Single(p => p[0].Name == "EventId")[0];
            Assert.That(eventId.NamespaceIndex, Is.Zero);

            // The positional layout without a session keeps its positions.
            var standardFields = (QualifiedName[][])decoder
                .GetField("StandardFields", BindingFlags.Public | BindingFlags.Static)!
                .GetValue(null)!;
            Assert.That(
                standardFields!.Select(p => p[0].Name),
                Is.EqualTo(paths.Select(p => p[0].Name)));

            Assert.That(
                m_records,
                Does.Contain("MachineEventTypeRecord.Decoder.GetStandardFields(namespaceUris)"));
        }

        /// <summary>
        /// S2 (OPC 10000-4 7.7.4.5, 7.22.3): when the session does not know
        /// the model's namespace the browse name used to fall back to
        /// namespace 0, where it could match a standard field of the same
        /// name. It now gets an index no server table holds, so the server
        /// returns null for the field. The companion StandardFields table,
        /// all in namespace 0, is marked obsolete.
        /// </summary>
        [Test]
        public void VendorFieldOfAnUnknownNamespaceCannotMatchNamespaceZero()
        {
            Type decoder = m_assembly.GetType(
                "Test.Ev.MachineEventTypeRecord+Decoder",
                throwOnError: true)!;
            MethodInfo getStandardFields = decoder!.GetMethod(
                "GetStandardFields",
                BindingFlags.Public | BindingFlags.Static)!;
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append("urn:other");

            var paths = (QualifiedName[][])getStandardFields!.Invoke(null, [namespaceUris])!;

            QualifiedName machineId = paths!.Single(p => p[0].Name == "MachineId")[0];
            Assert.That(machineId.NamespaceIndex, Is.EqualTo(ushort.MaxValue));
            QualifiedName eventId = paths.Single(p => p[0].Name == "EventId")[0];
            Assert.That(eventId.NamespaceIndex, Is.Zero);

            FieldInfo standardFields = decoder.GetField(
                "StandardFields",
                BindingFlags.Public | BindingFlags.Static)!;
            Assert.That(
                standardFields!.GetCustomAttribute<ObsoleteAttribute>(),
                Is.Not.Null,
                "the all-namespace-0 companion table must point callers to GetStandardFields");
        }

        /// <summary>
        /// D-16: a property whose data type is a simple subtype of a
        /// built-in type (no class is generated for it) is represented by
        /// the built-in type; a model data type named like a standard one
        /// is not mistaken for it.
        /// </summary>
        [Test]
        public void VendorDataTypesMapToGeneratedOrBuiltInTypes()
        {
            Type record = m_assembly.GetType(
                "Test.Ev.MachineEventTypeRecord",
                throwOnError: true)!;
            Assert.That(record!.GetProperty("Load")!.PropertyType, Is.EqualTo(typeof(double?)));
            Assert.That(record.GetProperty("Span")!.PropertyType.FullName, Is.EqualTo("Test.Ev.Duration"));
            Assert.That(record.GetProperty("Elapsed")!.PropertyType, Is.EqualTo(typeof(double?)));
        }

        private const string Model =
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <opc:ModelDesign
              xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
              xmlns:ua="http://opcfoundation.org/UA/"
              xmlns="http://test.org/UA/Ev/"
              TargetNamespace="http://test.org/UA/Ev/">
              <opc:Namespaces>
                <opc:Namespace Name="Ev" Prefix="Test.Ev">http://test.org/UA/Ev/</opc:Namespace>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua" XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd">http://opcfoundation.org/UA/</opc:Namespace>
              </opc:Namespaces>
              <opc:DataType SymbolicName="Percent" BaseType="ua:Double" />
              <opc:DataType SymbolicName="Duration" BaseType="ua:Structure">
                <opc:Fields>
                  <opc:Field Name="Ms" DataType="ua:Int64" />
                </opc:Fields>
              </opc:DataType>
              <opc:ObjectType SymbolicName="MachineEventType" BaseType="ua:BaseEventType">
                <opc:Children>
                  <opc:Property SymbolicName="MachineId" DataType="ua:String" />
                  <opc:Property SymbolicName="Load" DataType="Percent" />
                  <opc:Property SymbolicName="Span" DataType="Duration" />
                  <opc:Property SymbolicName="Elapsed" DataType="ua:Duration" />
                </opc:Children>
              </opc:ObjectType>
            </opc:ModelDesign>
            """;

        private string m_records;
        private Assembly m_assembly;
    }
}
