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
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Generates data types from a small ModelDesign, compiles the
    /// generated code and exercises the resulting classes.
    /// </summary>
    [TestFixture]
    [Category("Generator")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class DataTypeModelTests
    {
        private const string ModelUri = "http://test.org/UA/DT/";

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_generated = Generate(Model);
            m_dataTypes = m_generated
                .Where(f => f.Key.EndsWith("DataTypes.g.cs", StringComparison.Ordinal))
                .Select(f => f.Value)
                .Single();
            m_assembly = Compile(m_generated);
        }

        /// <summary>
        /// D-1: a structure without own fields still encodes every field it
        /// inherits, so its StructureDefinition has to list them. And a type
        /// deriving from it must not lose the fields above the field-less
        /// link of the chain.
        /// </summary>
        [Test]
        public void StructureDefinitionIncludesFieldsInheritedThroughFieldlessType()
        {
            StructureDefinition midB = CreateDefinition<StructureDefinition>("MidB");
            Assert.That(midB.Fields.ToArray()!.Select(f => f.Name), Is.EqualTo(s_midBFields));
            Assert.That(midB.FirstExplicitFieldIndex, Is.EqualTo(1));

            StructureDefinition leafC = CreateDefinition<StructureDefinition>("LeafC");
            Assert.That(
                leafC.Fields.ToArray()!.Select(f => f.Name),
                Is.EqualTo(s_leafCFields));
            Assert.That(leafC.FirstExplicitFieldIndex, Is.EqualTo(1));
        }

        /// <summary>
        /// D-2: a structure with optional fields deriving from a structure
        /// without optional fields compared the optional fields using only
        /// its own mask, so equality was asymmetric.
        /// </summary>
        [Test]
        public void DerivedOptionalFieldsEqualityComparesEncodingMask()
        {
            IEncodeable x = Create("OptDerived", ("A", 1));
            IEncodeable y = Create("OptDerived", ("A", 1), ("B", 7), ("EncodingMask", 1u));

            Assert.That(x.IsEqual(y), Is.False);
            Assert.That(y.IsEqual(x), Is.False);
        }

        /// <summary>
        /// D-3: equal instances must have equal hash codes. IsEqual ignores
        /// union members that are not selected and optional fields whose
        /// mask bit is clear; GetHashCode has to as well.
        /// </summary>
        [Test]
        public void HashCodeIsConsistentWithIsEqual()
        {
            IEncodeable u1 = Create("Choice", ("SwitchField", 1u), ("X", 1), ("Y", "stale"));
            IEncodeable u2 = Create("Choice", ("SwitchField", 1u), ("X", 1));
            Assert.That(u1.IsEqual(u2), Is.True);
            Assert.That(u1.GetHashCode(), Is.EqualTo(u2.GetHashCode()));

            IEncodeable o1 = Create("WithDefaults", ("Note", "stale"));
            IEncodeable o2 = Create("WithDefaults");
            Assert.That(o1.IsEqual(o2), Is.True);
            Assert.That(o1.GetHashCode(), Is.EqualTo(o2.GetHashCode()));
        }

        /// <summary>
        /// A3-7: IsEqual treats all NaNs as equal, so GetHashCode has to
        /// hash a canonical NaN for float and double fields (.NET Framework
        /// hashes the raw bits, i.e. the NaN payload).
        /// </summary>
        [Test]
        public void HashCodeCanonicalizesNaN()
        {
            Assert.That(m_dataTypes, Does.Contain("double.IsNaN(m_d) ? double.NaN : m_d"));
            Assert.That(m_dataTypes, Does.Contain("float.IsNaN(m_f) ? float.NaN : m_f"));
            Assert.That(m_dataTypes, Does.Contain("double.IsNaN(m_optD) ? double.NaN : m_optD"));

            double otherNaN = BitConverter.Int64BitsToDouble(0x7FF8000000000001);
            float otherNaNf = BitConverter.ToSingle(BitConverter.GetBytes(0x7FC00001), 0);
            IEncodeable a = Create("Samples", ("D", double.NaN), ("F", float.NaN),
                ("OptD", double.NaN), ("EncodingMask", 1u));
            IEncodeable b = Create("Samples", ("D", otherNaN), ("F", otherNaNf),
                ("OptD", otherNaN), ("EncodingMask", 1u));
            Assert.That(a.IsEqual(b), Is.True);
            Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));

            IEncodeable u1 = Create("NumberChoice", ("SwitchField", 1u), ("D", double.NaN));
            IEncodeable u2 = Create("NumberChoice", ("SwitchField", 1u), ("D", otherNaN));
            Assert.That(u1.IsEqual(u2), Is.True);
            Assert.That(u1.GetHashCode(), Is.EqualTo(u2.GetHashCode()));
        }

        /// <summary>
        /// D-4: a pooled instance handed out again must look like a newly
        /// constructed one: model default values, eagerly created structure
        /// fields, a cleared encoding mask and inherited fields restored.
        /// </summary>
        [Test]
        public void ReusedInstanceIsResetToConstructedState()
        {
            object first = Rent("WithDefaults");
            Set(first, "Level", 0);
            Set(first, "Child", null!);
            Set(first, "Note", "x");
            Set(first, "EncodingMask", 1u);
            ((IPooledEncodeable)first).Reuse();
            object second = Rent("WithDefaults");
            Assert.That(second, Is.SameAs(first), "the pool must hand the instance out again");
            Assert.That(Get(second, "Level"), Is.EqualTo(5));
            Assert.That(Get(second, "Child"), Is.Not.Null);
            Assert.That(Get(second, "Note"), Is.Null);
            Assert.That(Get(second, "EncodingMask"), Is.Zero);

            // Inherited field of an abstract (not pooled) base.
            object fromAbstract = Rent("FromAbstract");
            Set(fromAbstract, "Tag", 0);
            Set(fromAbstract, "Own", 3);
            ((IPooledEncodeable)fromAbstract).Reuse();
            object fromAbstractAgain = Rent("FromAbstract");
            Assert.That(fromAbstractAgain, Is.SameAs(fromAbstract));
            Assert.That(Get(fromAbstractAgain, "Tag"), Is.EqualTo(7));
            Assert.That(Get(fromAbstractAgain, "Own"), Is.Zero);

            // Inherited field of a pooled base.
            object fromConcrete = Rent("FromConcrete");
            Set(fromConcrete, "Level", 0);
            Set(fromConcrete, "Extra", 4);
            ((IPooledEncodeable)fromConcrete).Reuse();
            object fromConcreteAgain = Rent("FromConcrete");
            Assert.That(fromConcreteAgain, Is.SameAs(fromConcrete));
            Assert.That(Get(fromConcreteAgain, "Level"), Is.EqualTo(5));
            Assert.That(Get(fromConcreteAgain, "Extra"), Is.Zero);
        }

        /// <summary>
        /// E-2: Reuse() called through a reference typed as the (pooled)
        /// base class has to return a derived instance to the pool of its
        /// runtime type, not to the pool of the base.
        /// </summary>
        [Test]
        public void ReuseThroughBaseReferenceReturnsToRuntimeTypePool()
        {
            object derived = Rent("FromConcrete");
            Type baseType = m_assembly.GetType("Test.DT.WithDefaults", throwOnError: true)!;
            Assert.That(derived, Is.InstanceOf(baseType!));

            // A non-virtual call of the base Reuse(), as C# emits it for
            // ((WithDefaults)derived).Reuse().
            baseType.GetMethod("Reuse", Type.EmptyTypes)!.Invoke(derived, null);

            object rentedBase = Rent("WithDefaults");
            Assert.That(rentedBase, Is.Not.SameAs(derived));
            Assert.That(rentedBase.GetType(), Is.EqualTo(baseType));
            Assert.That(Rent("FromConcrete"), Is.SameAs(derived));
        }

        /// <summary>
        /// D-6: an OptionSet using bit 63 of a UInt64 threw an
        /// OverflowException in the generator. The EnumField value is the
        /// bit position.
        /// </summary>
        [Test]
        public void UInt64OptionSetUsingBit63Generates()
        {
            EnumDefinition flags = CreateDefinition<EnumDefinition>("Flags64");
            Assert.That(
                flags.Fields.ToArray()!.Select(f => (f.Name, f.Value)),
                Is.EqualTo(new[] { ("Low", 0L), ("Top", 63L) }));
        }

        /// <summary>
        /// A2-6: a subtype of the OptionSet structure may use bits beyond 63;
        /// the bit position was derived through a ulong conversion of the
        /// mask, which overflows for those.
        /// </summary>
        [Test]
        public void StructureOptionSetUsingBitsBeyond63Generates()
        {
            // Generation only: the test stack stub has no OptionSet class, so
            // the output is inspected rather than compiled.
            string model = Model.Replace(
                "</opc:ModelDesign>",
                """
                  <opc:DataType SymbolicName="BigOptions" BaseType="ua:OptionSet" IsOptionSet="true">
                    <opc:Fields>
                      <opc:Field Name="B0" Identifier="1" />
                      <opc:Field Name="B64" Identifier="18446744073709551616" />
                      <opc:Field Name="B95" Identifier="39614081257132168796771975168" />
                    </opc:Fields>
                  </opc:DataType>
                </opc:ModelDesign>
                """,
                StringComparison.Ordinal);
            string dataTypes = Generate(model)
                .Single(f => f.Key.EndsWith("DataTypes.g.cs", StringComparison.Ordinal))
                .Value;

            string[] values =
            [
                .. s_bigOptionsNames.Select(name =>
                {
                    int field = dataTypes.IndexOf($"Name = \"{name}\"", StringComparison.Ordinal);
                    Assert.That(field, Is.GreaterThanOrEqualTo(0), name);
                    int value = dataTypes.IndexOf("Value = ", field, StringComparison.Ordinal) + 8;
                    int end = dataTypes.IndexOf(',', value);
                    return name + "=" + dataTypes[value..end];
                })
            ];
            Assert.That(values, Is.EqualTo(s_bigOptionsValues));
        }

        /// <summary>
        /// A subtype of the OptionSet structure has no upper bit (OPC 10000-3
        /// 8.40): bit 130 is published as EnumField value 130.
        /// </summary>
        [Test]
        public void StructureOptionSetUsingBit130Generates()
        {
            string model = Model.Replace(
                "</opc:ModelDesign>",
                $"""
                  <opc:DataType SymbolicName="HugeOptions" BaseType="ua:OptionSet" IsOptionSet="true">
                    <opc:Fields>
                      <opc:Field Name="H0" Identifier="1" />
                      <opc:Field Name="H130" BitMask="4{new string('0', 32)}" />
                      <opc:Field Name="H131" />
                    </opc:Fields>
                  </opc:DataType>
                </opc:ModelDesign>
                """,
                StringComparison.Ordinal);
            string dataTypes = Generate(model)
                .Single(f => f.Key.EndsWith("DataTypes.g.cs", StringComparison.Ordinal))
                .Value;

            string[] values =
            [
                .. s_hugeOptionsNames.Select(name =>
                {
                    int field = dataTypes.IndexOf($"Name = \"{name}\"", StringComparison.Ordinal);
                    Assert.That(field, Is.GreaterThanOrEqualTo(0), name);
                    int value = dataTypes.IndexOf("Value = ", field, StringComparison.Ordinal) + 8;
                    int end = dataTypes.IndexOf(',', value);
                    return name + "=" + dataTypes[value..end];
                })
            ];
            Assert.That(values, Is.EqualTo(s_hugeOptionsValues));
        }

        private static readonly string[] s_hugeOptionsNames = ["H0", "H130", "H131"];
        private static readonly string[] s_hugeOptionsValues = ["H0=0", "H130=130", "H131=131"];
        private static readonly string[] s_bigOptionsNames = ["B0", "B64", "B95"];
        private static readonly string[] s_bigOptionsValues = ["B0=0", "B64=64", "B95=95"];

        private T CreateDefinition<T>(string typeName) where T : DataTypeDefinition
        {
            MethodInfo create = m_assembly.GetTypes()
                .Select(t => t.GetMethod(
                    "Create" + typeName,
                    BindingFlags.Public | BindingFlags.Static))
                .First(m => m != null)!;
            var namespaceUris = new NamespaceTable();
            namespaceUris.Append(ModelUri);
            return (T)create!.Invoke(null, [namespaceUris])!;
        }

        private IEncodeable Create(string typeName, params (string Name, object Value)[] values)
        {
            Type type = m_assembly.GetType("Test.DT." + typeName, throwOnError: true)!;
            object instance = Activator.CreateInstance(type!)!;
            foreach ((string name, object value) in values)
            {
                Set(instance!, name, value);
            }
            return (IEncodeable)instance!;
        }

        private object Rent(string typeName)
        {
            Type activator = m_assembly.GetType(
                "Test.DT." + typeName + "Activator",
                throwOnError: true)!;
            var instance = (IEncodeableType)activator!
                .GetField("Instance", BindingFlags.Public | BindingFlags.Static)!
                .GetValue(null)!;
            return instance!.CreateInstance();
        }

        private static void Set(object instance, string name, object value)
        {
            PropertyInfo property = instance.GetType().GetProperty(name)!;
            if (property!.PropertyType.IsEnum)
            {
                value = Enum.ToObject(property.PropertyType, value);
            }
            property.SetValue(instance, value);
        }

        private static object Get(object instance, string name)
        {
            return instance.GetType().GetProperty(name)!.GetValue(instance)!;
        }

        internal static Dictionary<string, string> Generate(
            string design,
            bool omitEventRecords = true)
        {
            string root = Path.Combine(
                Path.GetTempPath(),
                "UA-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                string path = Path.Combine(root, "Model.xml");
                File.WriteAllText(path, design);
                ITelemetryContext telemetry = NUnitTelemetryContext.Create(
                    logLevel: LogLevel.Error);
                using var fileSystem = new VirtualFileSystem();
                Generators.GenerateCode(
                    new DesignFileCollection
                    {
                        Targets = [path],
                        Dependencies = [path],
                        Options = new DesignFileOptions()
                    },
                    fileSystem,
                    string.Empty,
                    telemetry,
                    new GeneratorOptions
                    {
                        OmitFluentApi = true,
                        OmitEventRecords = omitEventRecords
                    },
                    useAllowSubtypes: false,
                    identifierFiles: null!,
                    referencedModels: null!,
                    nodeManagerBindings: null!,
                    reportBindingDiagnostic: null!,
                    sharedUsedBindings: null!,
                    bindingModelCount: 0,
                    reportFluentAccessorsOnlyDiagnostic: null!,
                    referencedModelProviders: null!,
                    referencedAccessorProviders: null!);
                return fileSystem.CreatedFiles
                    .ToDictionary(c => c, c => Encoding.UTF8.GetString(fileSystem.Get(c)));
            }
            finally
            {
                try
                {
                    Directory.Delete(root, true);
                }
                catch (IOException)
                {
                }
            }
        }

        internal static Assembly Compile(Dictionary<string, string> generated)
        {
            // The shared stack stub declares the event record base as a
            // class (for C# 8 tests); generated event records need the
            // production record shape.
            string stack = CompilerUtils.OpcUa
                .Replace(
                    "public abstract class EventRecord",
                    "public abstract record EventRecord",
                    StringComparison.Ordinal)
                .Replace(
                    "public partial class BaseEventTypeRecord : EventRecord",
                    "public partial record BaseEventTypeRecord : EventRecord",
                    StringComparison.Ordinal);
            IEnumerable<KeyValuePair<string, string>> sources = generated
                .Where(f => f.Key.EndsWith(".cs", StringComparison.Ordinal))
                .Append(new KeyValuePair<string, string>("OpcUa.cs", stack));
            using var peStream = new MemoryStream();
            EmitResult result = OptimizationLevel.Debug
                .CreateCompilation("DataTypeModel.Generated")
                .AddCode(sources, LanguageVersion.Latest)
                .Emit(peStream);
            string errors = string.Join(
                Environment.NewLine,
                result.Diagnostics
                    .Where(d => d.Severity == DiagnosticSeverity.Error)
                    .Take(10));
            Assert.That(result.Success, Is.True, "Generated code failed: " + errors);
            return Assembly.Load(peStream.ToArray());
        }

        private const string Model =
            """
            <?xml version="1.0" encoding="utf-8" ?>
            <opc:ModelDesign
              xmlns:opc="http://opcfoundation.org/UA/ModelDesign.xsd"
              xmlns:ua="http://opcfoundation.org/UA/"
              xmlns:uax="http://opcfoundation.org/UA/2008/02/Types.xsd"
              xmlns="http://test.org/UA/DT/"
              TargetNamespace="http://test.org/UA/DT/">
              <opc:Namespaces>
                <opc:Namespace Name="DT" Prefix="Test.DT">http://test.org/UA/DT/</opc:Namespace>
                <opc:Namespace Name="OpcUa" Prefix="Opc.Ua" XmlNamespace="http://opcfoundation.org/UA/2008/02/Types.xsd">http://opcfoundation.org/UA/</opc:Namespace>
              </opc:Namespaces>
              <opc:DataType SymbolicName="BaseA" BaseType="ua:Structure">
                <opc:Fields>
                  <opc:Field Name="A" DataType="ua:Int32" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="MidB" BaseType="BaseA" />
              <opc:DataType SymbolicName="LeafC" BaseType="MidB">
                <opc:Fields>
                  <opc:Field Name="C" DataType="ua:String" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="OptDerived" BaseType="BaseA">
                <opc:Fields>
                  <opc:Field Name="B" DataType="ua:Int32" IsOptional="true" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="Choice" BaseType="ua:Union" IsUnion="true">
                <opc:Fields>
                  <opc:Field Name="X" DataType="ua:Int32" />
                  <opc:Field Name="Y" DataType="ua:String" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="WithDefaults" BaseType="ua:Structure">
                <opc:Fields>
                  <opc:Field Name="Level" DataType="ua:Int32">
                    <opc:DefaultValue>
                      <uax:Int32>5</uax:Int32>
                    </opc:DefaultValue>
                  </opc:Field>
                  <opc:Field Name="Child" DataType="BaseA" />
                  <opc:Field Name="Note" DataType="ua:String" IsOptional="true" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="AbstractBase" BaseType="ua:Structure" IsAbstract="true">
                <opc:Fields>
                  <opc:Field Name="Tag" DataType="ua:Int32">
                    <opc:DefaultValue>
                      <uax:Int32>7</uax:Int32>
                    </opc:DefaultValue>
                  </opc:Field>
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="FromAbstract" BaseType="AbstractBase">
                <opc:Fields>
                  <opc:Field Name="Own" DataType="ua:Int32" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="FromConcrete" BaseType="WithDefaults">
                <opc:Fields>
                  <opc:Field Name="Extra" DataType="ua:Int32" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="Flags64" BaseType="ua:UInt64" IsOptionSet="true">
                <opc:Fields>
                  <opc:Field Name="Low" BitMask="0000000000000001" />
                  <opc:Field Name="Top" BitMask="8000000000000000" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="Samples" BaseType="ua:Structure">
                <opc:Fields>
                  <opc:Field Name="D" DataType="ua:Double" />
                  <opc:Field Name="F" DataType="ua:Float" />
                  <opc:Field Name="OptD" DataType="ua:Double" IsOptional="true" />
                </opc:Fields>
              </opc:DataType>
              <opc:DataType SymbolicName="NumberChoice" BaseType="ua:Union" IsUnion="true">
                <opc:Fields>
                  <opc:Field Name="D" DataType="ua:Double" />
                  <opc:Field Name="F" DataType="ua:Float" />
                </opc:Fields>
              </opc:DataType>
            </opc:ModelDesign>
            """;

        private static readonly string[] s_midBFields = ["A"];
        private static readonly string[] s_leafCFields = ["A", "C"];
        private Dictionary<string, string> m_generated;
        private string m_dataTypes;
        private Assembly m_assembly;
    }
}
