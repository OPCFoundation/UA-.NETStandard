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
using System.Text;
using System.Xml;
using Microsoft.Extensions.Logging;
using NUnit.Framework;
using Opc.Ua.Tests;
using SchemaTypes = Opc.Ua.Schema.Types;

namespace Opc.Ua.Schema.Model.Tests
{
    /// <summary>
    /// Regression tests for defects in <see cref="ModelDesignValidator"/>
    /// (identifier allocation, dependency load order, method argument
    /// properties, enum identifiers and dependency value ranks).
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class ModelDesignValidatorRegressionTests
    {
        private VirtualFileSystem m_fileSystem;

        [SetUp]
        public void SetUp()
        {
            m_fileSystem = new VirtualFileSystem();
        }

        [TearDown]
        public void TearDown()
        {
            m_fileSystem?.Dispose();
            m_fileSystem = null;
        }

        /// <summary>
        /// An explicit NumericId in the design must be reserved before
        /// identifiers are allocated, otherwise another node of the same
        /// namespace is handed the same number and validation fails with
        /// "Duplicate identifiers".
        /// </summary>
        [TestCase(true)]
        [TestCase(false)]
        public void ExplicitNumericIdIsNotHandedOutToAnotherNode(bool pinnedFirst)
        {
            const string pinned =
                """<opc:ObjectType SymbolicName="PinnedType" BaseType="ua:BaseObjectType" NumericId="1" />""";
            const string auto =
                """<opc:ObjectType SymbolicName="AutoType" BaseType="ua:BaseObjectType" />""";
            string path = AddDesign("Pinned", pinnedFirst ? pinned + auto : auto + pinned);
            ModelDesignValidator validator = CreateValidator(startId: 1);

            Assert.DoesNotThrow(() => validator.Validate([path], [], null));

            Assert.Multiple(() =>
            {
                Assert.That(FindNode(validator, "PinnedType", "Pinned").NumericId, Is.EqualTo(1u));
                Assert.That(FindNode(validator, "AutoType", "Pinned").NumericId, Is.EqualTo(2u));
            });
        }

        /// <summary>
        /// The dependency load order must be a topological order of the
        /// imports independent of the order the dependency files are passed
        /// in: Z imports X which imports Y (which the target imports too).
        /// </summary>
        [TestCase("Z", "X", "Y")]
        [TestCase("X", "Z", "Y")]
        [TestCase("Y", "Z", "X")]
        [TestCase("Z", "Y", "X")]
        public void DependenciesLoadInImportOrderRegardlessOfFileOrder(
            string first,
            string second,
            string third)
        {
            var files = new Dictionary<string, string>
            {
                ["Y"] = AddDesign(
                    "Y",
                    """<opc:ObjectType SymbolicName="YType" BaseType="ua:BaseObjectType" />"""),
                ["X"] = AddDesign(
                    "X",
                    """<opc:ObjectType SymbolicName="XType" BaseType="y:YType" />""",
                    "Y"),
                ["Z"] = AddDesign(
                    "Z",
                    """<opc:ObjectType SymbolicName="ZType" BaseType="x:XType" />""",
                    "X")
            };
            string target = AddDesign(
                "T",
                """<opc:ObjectType SymbolicName="TType" BaseType="y:YType" />""",
                "Y");
            ModelDesignValidator validator = CreateValidator();

            Assert.DoesNotThrow(() => validator.Validate(
                [target],
                [files[first], files[second], files[third]],
                null));

            var zType = (TypeDesign)FindNode(validator, "ZType", "Z");
            Assert.That(zType.BaseTypeNode?.SymbolicName?.Name, Is.EqualTo("XType"));
        }

        /// <summary>
        /// A dependency that imports the primary target must still be loaded
        /// after it.
        /// </summary>
        [Test]
        public void DependencyImportingTheTargetLoadsAfterTheTarget()
        {
            string downstream = AddDesign(
                "D",
                """<opc:ObjectType SymbolicName="DType" BaseType="t:TType" />""",
                "T");
            string target = AddDesign(
                "T",
                """<opc:ObjectType SymbolicName="TType" BaseType="ua:BaseObjectType" />""");
            ModelDesignValidator validator = CreateValidator();

            Assert.DoesNotThrow(() => validator.Validate([target], [downstream], null));

            var dType = (TypeDesign)FindNode(validator, "DType", "D");
            Assert.That(dType.BaseTypeNode?.SymbolicName?.Name, Is.EqualTo("TType"));
        }

        /// <summary>
        /// OPC 10000-3 only defines the InputArguments / OutputArguments
        /// properties of a method when it has arguments of that direction.
        /// A child method without arguments must not grow empty argument
        /// properties (which consume NodeIds and are emitted as nodes).
        /// </summary>
        [Test]
        public void ChildMethodWithoutArgumentsHasNoArgumentProperties()
        {
            string path = AddDesign("Methods", MachineTypeWithMethods("MachineType", "ua:BaseObjectType"));
            ModelDesignValidator validator = CreateValidator();

            validator.Validate([path], [], null);

            var type = (TypeDesign)FindNode(validator, "MachineType", "Methods");
            List<string> paths = type.Hierarchy.NodeList.ConvertAll(n => n.RelativePath);

            Assert.Multiple(() =>
            {
                Assert.That(paths, Does.Contain("Reset"));
                Assert.That(paths, Does.Not.Contain("Reset_InputArguments"));
                Assert.That(paths, Does.Not.Contain("Reset_OutputArguments"));
                Assert.That(paths, Does.Contain("Start_InputArguments"));
                Assert.That(paths, Does.Not.Contain("Start_OutputArguments"));
                Assert.That(paths, Does.Contain("Stop_OutputArguments"));
                Assert.That(paths, Does.Not.Contain("Stop_InputArguments"));
            });
        }

        /// <summary>
        /// Same as <see cref="ChildMethodWithoutArgumentsHasNoArgumentProperties"/>
        /// for methods inherited from a type declared in a dependency design
        /// (linked by LinkDependencyInstances instead of ValidateInstance).
        /// </summary>
        [Test]
        public void InheritedDependencyMethodWithoutArgumentsHasNoArgumentProperties()
        {
            string dependency = AddDesign("Dep", MachineTypeWithMethods("DepMachineType", "ua:BaseObjectType"));
            string target = AddDesign(
                "Tgt",
                """<opc:ObjectType SymbolicName="SubMachineType" BaseType="dep:DepMachineType" />""",
                "Dep");
            ModelDesignValidator validator = CreateValidator();

            validator.Validate([target], [dependency], null);

            var type = (TypeDesign)FindNode(validator, "SubMachineType", "Tgt");
            List<string> paths = type.Hierarchy.NodeList.ConvertAll(n => n.RelativePath);

            Assert.Multiple(() =>
            {
                Assert.That(paths, Does.Contain("Reset"));
                Assert.That(paths, Does.Not.Contain("Reset_InputArguments"));
                Assert.That(paths, Does.Not.Contain("Reset_OutputArguments"));
                Assert.That(paths, Does.Contain("Start_InputArguments"));
                Assert.That(paths, Does.Not.Contain("Start_OutputArguments"));
                Assert.That(paths, Does.Contain("Stop_OutputArguments"));
                Assert.That(paths, Does.Not.Contain("Stop_InputArguments"));
            });
        }

        /// <summary>
        /// The trailing "_&lt;digits&gt;" naming convention only supplies an
        /// enum value when none is authored: an explicit Identifier wins.
        /// </summary>
        [Test]
        public void ExplicitEnumIdentifierWinsOverTheNameSuffix()
        {
            string path = AddDesign(
                "Enums",
                """
                <opc:DataType SymbolicName="TextEncoding" BaseType="ua:Enumeration">
                  <opc:Fields>
                    <opc:Field Name="UTF_8" Identifier="0" />
                    <opc:Field Name="UTF_16" Identifier="1" />
                    <opc:Field Name="Other" />
                    <opc:Field Name="Legacy_7" />
                    <opc:Field Name="Next" />
                  </opc:Fields>
                </opc:DataType>
                """);
            ModelDesignValidator validator = CreateValidator();

            validator.Validate([path], [], null);

            var dataType = (DataTypeDesign)FindNode(validator, "TextEncoding", "Enums");
            Assert.That(
                dataType.Fields.Select(f => f.Identifier),
                Is.EqualTo(new decimal[] { 0, 1, 2, 7, 8 }));
        }

        /// <summary>
        /// A child variable of a dependency type keeps its authored ValueRank
        /// instead of taking the rank of its default value.
        /// </summary>
        [Test]
        public void DependencyChildVariableKeepsItsAuthoredValueRank()
        {
            string dependency = AddDesign(
                "Dep",
                """
                <opc:ObjectType SymbolicName="DeviceType" BaseType="ua:BaseObjectType">
                  <opc:Children>
                    <opc:Variable SymbolicName="Limits" DataType="ua:Double" ValueRank="ScalarOrArray"
                        TypeDefinition="ua:BaseDataVariableType">
                      <opc:DefaultValue>
                        <uax:Double>0</uax:Double>
                      </opc:DefaultValue>
                    </opc:Variable>
                    <opc:Variable SymbolicName="Level" DataType="ua:Double"
                        TypeDefinition="ua:BaseDataVariableType">
                      <opc:DefaultValue>
                        <uax:Double>0</uax:Double>
                      </opc:DefaultValue>
                    </opc:Variable>
                  </opc:Children>
                </opc:ObjectType>
                """);
            string target = AddDesign(
                "Tgt",
                """<opc:ObjectType SymbolicName="SubDeviceType" BaseType="dep:DeviceType" />""",
                "Dep");
            ModelDesignValidator validator = CreateValidator();

            validator.Validate([target], [dependency], null);

            var type = (TypeDesign)FindNode(validator, "SubDeviceType", "Tgt");
            var limits = (VariableDesign)type.Hierarchy.NodeList
                .Single(n => n.RelativePath == "Limits").Instance;
            var level = (VariableDesign)type.Hierarchy.NodeList
                .Single(n => n.RelativePath == "Level").Instance;

            Assert.Multiple(() =>
            {
                Assert.That(limits.ValueRank, Is.EqualTo(ValueRank.ScalarOrArray));
                Assert.That(level.ValueRank, Is.EqualTo(ValueRank.Scalar));
            });
        }

        /// <summary>
        /// A method that references a method declaration through its
        /// TypeDefinition keeps its own authored description.
        /// </summary>
        [Test]
        public void MethodWithTypeDefinitionKeepsItsOwnDescription()
        {
            string path = AddDesign(
                "Pumps",
                """
                <opc:Method SymbolicName="StartMethodType">
                  <opc:Description>Starts the device.</opc:Description>
                </opc:Method>
                <opc:ObjectType SymbolicName="PumpType" BaseType="ua:BaseObjectType">
                  <opc:Children>
                    <opc:Method SymbolicName="Start" TypeDefinition="StartMethodType">
                      <opc:Description>Starts the pump.</opc:Description>
                    </opc:Method>
                    <opc:Method SymbolicName="Restart" TypeDefinition="StartMethodType" />
                  </opc:Children>
                </opc:ObjectType>
                """);
            ModelDesignValidator validator = CreateValidator();

            validator.Validate([path], [], null);

            var type = (TypeDesign)FindNode(validator, "PumpType", "Pumps");
            InstanceDesign start = type.Children.Items.Single(c => c.SymbolicName.Name == "Start");
            InstanceDesign restart = type.Children.Items.Single(c => c.SymbolicName.Name == "Restart");

            Assert.Multiple(() =>
            {
                Assert.That(start.Description?.Value, Is.EqualTo("Starts the pump."));
                Assert.That(restart.Description?.Value, Is.EqualTo("Starts the device."));
            });
        }

        /// <summary>
        /// An inherited child whose name merely starts with the name of an
        /// explicitly declared child (StatusCode vs Status) is not explicitly
        /// defined and takes its identifier from the implicit range.
        /// </summary>
        [Test]
        public void InheritedChildSharingANamePrefixIsNotExplicitlyDefined()
        {
            string path = AddDesign(
                "Prefix",
                """
                <opc:ObjectType SymbolicName="BaseMachineType" BaseType="ua:BaseObjectType">
                  <opc:Children>
                    <opc:Property SymbolicName="StatusCode" DataType="ua:Int32" ModellingRule="Optional" />
                  </opc:Children>
                </opc:ObjectType>
                <opc:ObjectType SymbolicName="MachineType" BaseType="BaseMachineType">
                  <opc:Children>
                    <opc:Property SymbolicName="Status" DataType="ua:Int32" />
                  </opc:Children>
                </opc:ObjectType>
                """);
            ModelDesignValidator validator = CreateValidator(startId: 1);

            validator.Validate([path], [], null);

            var type = (TypeDesign)FindNode(validator, "MachineType", "Prefix");
            HierarchyNode status = type.Hierarchy.NodeList.Single(n => n.RelativePath == "Status");
            HierarchyNode statusCode = type.Hierarchy.NodeList.Single(n => n.RelativePath == "StatusCode");

            Assert.Multiple(() =>
            {
                Assert.That((uint)status.Identifier, Is.LessThan(1000000u));
                Assert.That((uint)statusCode.Identifier, Is.GreaterThanOrEqualTo(1000000u));
            });
        }

        /// <summary>
        /// The reference type of a child is validated: an unknown reference
        /// type is reported instead of silently flowing into generated code.
        /// </summary>
        [TestCase("ua:HasComponnet", true)]
        [TestCase("ua:HasOrderedComponent", false)]
        public void UnknownChildReferenceTypeIsReported(string referenceType, bool reported)
        {
            string path = AddDesign(
                "Refs",
                $"""
                <opc:ObjectType SymbolicName="HolderType" BaseType="ua:BaseObjectType">
                  <opc:Children>
                    <opc:Object SymbolicName="Part" TypeDefinition="ua:BaseObjectType">
                      <opc:ReferenceType>{referenceType}</opc:ReferenceType>
                    </opc:Object>
                  </opc:Children>
                </opc:ObjectType>
                """);
            var entries = new List<string>();
            ModelDesignValidator validator = CreateValidator(
                telemetry: new CapturingTelemetryContext(entries));

            try
            {
                validator.Validate([path], [], null);
            }
            catch (InvalidOperationException)
            {
                // a later stage may reject the unknown reference type too.
            }

            Assert.That(
                entries.Exists(e => e.Contains(referenceType[3..], StringComparison.Ordinal)),
                Is.EqualTo(reported));
        }

        /// <summary>
        /// A matrix field of the built-in type dictionary keeps one
        /// dimension per rank.
        /// </summary>
        [TestCase(-1, ValueRank.Scalar, null)]
        [TestCase(1, ValueRank.Array, null)]
        [TestCase(2, ValueRank.OneOrMoreDimensions, "0,0")]
        [TestCase(3, ValueRank.OneOrMoreDimensions, "0,0,0")]
        public void ImportFieldKeepsTheRankOfMatrixFields(
            int rank,
            ValueRank expectedRank,
            string expectedDimensions)
        {
            var field = new SchemaTypes.FieldType
            {
                Name = "Field",
                DataType = new XmlQualifiedName("Double"),
                ValueRank = rank
            };

            Parameter parameter = ModelDesignValidator.ImportField(field);

            Assert.Multiple(() =>
            {
                Assert.That(parameter.ValueRank, Is.EqualTo(expectedRank));
                Assert.That(parameter.ArrayDimensions, Is.EqualTo(expectedDimensions));
            });
        }

        private static string MachineTypeWithMethods(string name, string baseType)
        {
            return $"""
                <opc:ObjectType SymbolicName="{name}" BaseType="{baseType}">
                  <opc:Children>
                    <opc:Method SymbolicName="Reset" ModellingRule="Mandatory" />
                    <opc:Method SymbolicName="Start" ModellingRule="Mandatory">
                      <opc:InputArguments>
                        <opc:Argument Name="Speed" DataType="ua:Double" />
                      </opc:InputArguments>
                    </opc:Method>
                    <opc:Method SymbolicName="Stop" ModellingRule="Mandatory">
                      <opc:OutputArguments>
                        <opc:Argument Name="Duration" DataType="ua:Double" />
                      </opc:OutputArguments>
                    </opc:Method>
                  </opc:Children>
                </opc:ObjectType>
                """;
        }

        private static string NamespaceUri(string name)
        {
            return $"http://test.org/UA/{name}/";
        }

        private string AddDesign(string name, string body, params string[] imports)
        {
            var design = new StringBuilder();
            design.Append("<?xml version=\"1.0\" encoding=\"utf-8\" ?>\n")
                .Append("<opc:ModelDesign\n")
                .Append("    xmlns:opc=\"http://opcfoundation.org/UA/ModelDesign.xsd\"\n")
                .Append("    xmlns:ua=\"http://opcfoundation.org/UA/\"\n")
                .Append("    xmlns:uax=\"http://opcfoundation.org/UA/2008/02/Types.xsd\"\n");
            foreach (string import in imports)
            {
                design.Append("    xmlns:").Append(import.ToLowerInvariant())
                    .Append("=\"").Append(NamespaceUri(import)).Append("\"\n");
            }
            design.Append("    xmlns=\"").Append(NamespaceUri(name)).Append("\"\n")
                .Append("    TargetNamespace=\"").Append(NamespaceUri(name)).Append("\">\n")
                .Append("  <opc:Namespaces>\n")
                .Append("    <opc:Namespace Name=\"OpcUa\" Prefix=\"Opc.Ua\" ")
                .Append("XmlNamespace=\"http://opcfoundation.org/UA/2008/02/Types.xsd\">")
                .Append("http://opcfoundation.org/UA/</opc:Namespace>\n");
            foreach (string ns in imports.Prepend(name))
            {
                design.Append("    <opc:Namespace Name=\"").Append(ns)
                    .Append("\" Prefix=\"Test.").Append(ns).Append("\">")
                    .Append(NamespaceUri(ns)).Append("</opc:Namespace>\n");
            }
            design.Append("  </opc:Namespaces>\n")
                .Append(body).Append('\n')
                .Append("</opc:ModelDesign>\n");

            string path = $"memory://{name}.design.xml";
            m_fileSystem.Add(path, Encoding.UTF8.GetBytes(design.ToString()));
            return path;
        }

        private static NodeDesign FindNode(ModelDesignValidator validator, string name, string ns)
        {
            Assert.That(
                validator.TryFindNode(
                    new XmlQualifiedName(name, NamespaceUri(ns)),
                    "Test",
                    "Node",
                    out NodeDesign node),
                Is.True,
                name);
            return node;
        }

        private ModelDesignValidator CreateValidator(
            uint startId = 1000,
            ITelemetryContext telemetry = null)
        {
            telemetry ??= NUnitTelemetryContext.Create(logLevel: LogLevel.Error);
            IFileSystem fileSystem = typeof(ModelDesignValidator).Assembly
                .AsFileSystem("Opc.Ua.SourceGeneration.Design")
                .WithFallback(m_fileSystem);
            return new ModelDesignValidator(
                fileSystem,
                startId,
                null,
                telemetry,
                SpecificationVersion.V105);
        }

        private sealed class CapturingTelemetryContext : TelemetryContextBase
        {
            public CapturingTelemetryContext(List<string> entries)
                : base(new CapturingLoggerFactory(entries))
            {
            }
        }

        private sealed class CapturingLoggerFactory : ILoggerFactory
        {
            public CapturingLoggerFactory(List<string> entries)
            {
                m_logger = new CapturingLogger(entries);
            }

            public void AddProvider(ILoggerProvider provider)
            {
            }

            public ILogger CreateLogger(string categoryName)
            {
                return m_logger;
            }

            public void Dispose()
            {
            }

            private readonly CapturingLogger m_logger;
        }

        private sealed class CapturingLogger : ILogger
        {
            public CapturingLogger(List<string> entries)
            {
                m_entries = entries;
            }

            public IDisposable BeginScope<TState>(TState state) where TState : notnull
            {
                return null;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return logLevel >= LogLevel.Warning;
            }

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception exception,
                Func<TState, Exception, string> formatter)
            {
                if (IsEnabled(logLevel))
                {
                    m_entries.Add(formatter(state, exception));
                }
            }

            private readonly List<string> m_entries;
        }
    }
}
