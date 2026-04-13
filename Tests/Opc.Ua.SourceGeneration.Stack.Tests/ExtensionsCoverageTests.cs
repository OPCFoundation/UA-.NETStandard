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

using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration.Tests
{
    [TestFixture]
    [Category("SourceGeneration")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class ExtensionsCoverageTests
    {
        [Test]
        public void ToNodeSetOptionsWithNullReturnsDefaults()
        {
            NodesetFileOptions result = Extensions.ToNodeSetOptions(null);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.Ignore, Is.False);
            Assert.That(result.Prefix, Is.Null);
            Assert.That(result.Version, Is.Null);
            Assert.That(result.Name, Is.Null);
            Assert.That(result.ModelUri, Is.Null);
        }

        [Test]
        public void ToNodeSetOptionsWithValidOptionsPopulatesAllProperties()
        {
            string prefix = $"build_metadata.AdditionalFiles.{SourceGenerator.Name}";
            var options = new AnalyzerOptions(new Dictionary<string, string>
            {
                [$"{prefix}Ignore".ToLowerInvariant()] = "true",
                [$"{prefix}Prefix".ToLowerInvariant()] = "MyPrefix",
                [$"{prefix}Version".ToLowerInvariant()] = "1.0",
                [$"{prefix}Name".ToLowerInvariant()] = "MyName",
                [$"{prefix}ModelUri".ToLowerInvariant()] = "http://test.org"
            });

            NodesetFileOptions result = options.ToNodeSetOptions();

            Assert.That(result.Ignore, Is.True);
            Assert.That(result.Prefix, Is.EqualTo("MyPrefix"));
            Assert.That(result.Version, Is.EqualTo("1.0"));
            Assert.That(result.Name, Is.EqualTo("MyName"));
            Assert.That(result.ModelUri, Is.EqualTo("http://test.org"));
        }

        [Test]
        public void ToNodeSetOptionsWithEmptyOptionsSetsDefaults()
        {
            var options = new AnalyzerOptions(new Dictionary<string, string>());

            NodesetFileOptions result = options.ToNodeSetOptions();

            Assert.That(result.Ignore, Is.False);
            Assert.That(result.Prefix, Is.EqualTo(string.Empty));
            Assert.That(result.Version, Is.EqualTo(string.Empty));
            Assert.That(result.Name, Is.EqualTo(string.Empty));
            Assert.That(result.ModelUri, Is.EqualTo(string.Empty));
        }

        [Test]
        public void GetFullNamespaceReturnsNamespaceForType()
        {
            string code = @"
namespace My.Test.Namespace
{
    public class TestClass { }
}
";
            CSharpCompilation compilation = CSharpCompilation.Create("TestAssembly")
                .AddReferences(CompilerUtils.TrustedReferences)
                .AddSyntaxTrees(CSharpSyntaxTree.ParseText(code));

            INamedTypeSymbol symbol = compilation.GetTypeByMetadataName("My.Test.Namespace.TestClass");

            Assert.That(symbol, Is.Not.Null);
            Assert.That(symbol.GetFullNamespace(), Is.EqualTo("My.Test.Namespace"));
        }

        [Test]
        public void GetFullNamespaceReturnsEmptyForGlobalNamespace()
        {
            string code = @"public class GlobalClass { }";
            CSharpCompilation compilation = CSharpCompilation.Create("TestAssembly")
                .AddReferences(CompilerUtils.TrustedReferences)
                .AddSyntaxTrees(CSharpSyntaxTree.ParseText(code));

            INamedTypeSymbol symbol = compilation.GetTypeByMetadataName("GlobalClass");

            Assert.That(symbol, Is.Not.Null);
            Assert.That(symbol.GetFullNamespace(), Is.EqualTo(string.Empty));
        }

        [Test]
        public void GetFullNamespaceReturnsSingleSegmentNamespace()
        {
            string code = @"
namespace Single
{
    public class TestClass { }
}
";
            CSharpCompilation compilation = CSharpCompilation.Create("TestAssembly")
                .AddReferences(CompilerUtils.TrustedReferences)
                .AddSyntaxTrees(CSharpSyntaxTree.ParseText(code));

            INamedTypeSymbol symbol = compilation.GetTypeByMetadataName("Single.TestClass");

            Assert.That(symbol, Is.Not.Null);
            Assert.That(symbol.GetFullNamespace(), Is.EqualTo("Single"));
        }

        [Test]
        public void GetFullyQualifiedTypeNameReturnsGlobalPrefixed()
        {
            string code = @"
namespace My.Namespace
{
    public class MyType { }
}
";
            CSharpCompilation compilation = CSharpCompilation.Create("TestAssembly")
                .AddReferences(CompilerUtils.TrustedReferences)
                .AddSyntaxTrees(CSharpSyntaxTree.ParseText(code));

            INamedTypeSymbol symbol = compilation.GetTypeByMetadataName("My.Namespace.MyType");

            Assert.That(symbol, Is.Not.Null);
            string result = symbol.GetFullyQualifiedTypeName();
            Assert.That(result, Does.StartWith("global::"));
            Assert.That(result, Does.Contain("My.Namespace.MyType"));
        }

        [Test]
        public void ImplementsInterfaceReturnsTrueWhenImplemented()
        {
            string code = @"
namespace Test
{
    public interface IMyInterface { }
    public class MyClass : IMyInterface { }
}
";
            CSharpCompilation compilation = CSharpCompilation.Create("TestAssembly")
                .AddReferences(CompilerUtils.TrustedReferences)
                .AddSyntaxTrees(CSharpSyntaxTree.ParseText(code));

            INamedTypeSymbol symbol = compilation.GetTypeByMetadataName("Test.MyClass");

            Assert.That(symbol, Is.Not.Null);
            Assert.That(symbol.ImplementsInterface("IMyInterface"), Is.True);
        }

        [Test]
        public void ImplementsInterfaceReturnsFalseWhenNotImplemented()
        {
            string code = @"
namespace Test
{
    public interface IMyInterface { }
    public class MyClass { }
}
";
            CSharpCompilation compilation = CSharpCompilation.Create("TestAssembly")
                .AddReferences(CompilerUtils.TrustedReferences)
                .AddSyntaxTrees(CSharpSyntaxTree.ParseText(code));

            INamedTypeSymbol symbol = compilation.GetTypeByMetadataName("Test.MyClass");

            Assert.That(symbol, Is.Not.Null);
            Assert.That(symbol.ImplementsInterface("IMyInterface"), Is.False);
        }

        [Test]
        public void HasAttributeReturnsTrueWhenPresent()
        {
            string code = @"
using System;
namespace Test
{
    [Obsolete]
    public class MyClass { }
}
";
            CSharpCompilation compilation = CSharpCompilation.Create("TestAssembly")
                .AddReferences(CompilerUtils.TrustedReferences)
                .AddSyntaxTrees(CSharpSyntaxTree.ParseText(code));

            INamedTypeSymbol symbol = compilation.GetTypeByMetadataName("Test.MyClass");

            Assert.That(symbol, Is.Not.Null);
            Assert.That(symbol.HasAttribute("ObsoleteAttribute"), Is.True);
        }

        [Test]
        public void HasAttributeReturnsFalseWhenAbsent()
        {
            string code = @"
namespace Test
{
    public class MyClass { }
}
";
            CSharpCompilation compilation = CSharpCompilation.Create("TestAssembly")
                .AddReferences(CompilerUtils.TrustedReferences)
                .AddSyntaxTrees(CSharpSyntaxTree.ParseText(code));

            INamedTypeSymbol symbol = compilation.GetTypeByMetadataName("Test.MyClass");

            Assert.That(symbol, Is.Not.Null);
            Assert.That(symbol.HasAttribute("ObsoleteAttribute"), Is.False);
        }

        [Test]
        public void AttributeDataGetValueReturnsNullForNullAttribute()
        {
            AttributeData attr = null;

            string result = attr.GetValue("SomeName");

            Assert.That(result, Is.Null);
        }

        [Test]
        public void AttributeDataGetValueReturnsNullWhenNameNotFound()
        {
            string code = @"
using System;
namespace Test
{
    [Obsolete(""reason"")]
    public class MyClass { }
}
";
            CSharpCompilation compilation = CSharpCompilation.Create("TestAssembly")
                .AddReferences(CompilerUtils.TrustedReferences)
                .AddSyntaxTrees(CSharpSyntaxTree.ParseText(code));

            INamedTypeSymbol symbol = compilation.GetTypeByMetadataName("Test.MyClass");
            AttributeData attr = symbol.GetAttributes().First();

            string result = attr.GetValue("NonExistentName");

            Assert.That(result, Is.Null);
        }

        [Test]
        public void AttributeDataGetValueReturnsStringValueWhenFound()
        {
            string code = @"
using System;
using System.ComponentModel;
namespace Test
{
    [DefaultValue(Name = ""hello"")]
    public class MyClass { }

    [AttributeUsage(AttributeTargets.Class)]
    public class DefaultValueAttribute : Attribute
    {
        public string Name { get; set; }
    }
}
";
            CSharpCompilation compilation = CSharpCompilation.Create("TestAssembly")
                .AddReferences(CompilerUtils.TrustedReferences)
                .AddSyntaxTrees(CSharpSyntaxTree.ParseText(code));

            INamedTypeSymbol symbol = compilation.GetTypeByMetadataName("Test.MyClass");
            AttributeData attr = symbol.GetAttributes()
                .First(a => a.AttributeClass?.Name == "DefaultValueAttribute");

            string result = attr.GetValue("Name");

            Assert.That(result, Is.EqualTo("hello"));
        }

        [Test]
        public void AttributeDataGetIntegerReturnsDefaultForNullAttribute()
        {
            AttributeData attr = null;

            int result = attr.GetInteger("SomeName", 42);

            Assert.That(result, Is.EqualTo(42));
        }

        [Test]
        public void AttributeDataGetIntegerReturnsDefaultWhenNameNotFound()
        {
            string code = @"
using System;
namespace Test
{
    [Obsolete(""reason"")]
    public class MyClass { }
}
";
            CSharpCompilation compilation = CSharpCompilation.Create("TestAssembly")
                .AddReferences(CompilerUtils.TrustedReferences)
                .AddSyntaxTrees(CSharpSyntaxTree.ParseText(code));

            INamedTypeSymbol symbol = compilation.GetTypeByMetadataName("Test.MyClass");
            AttributeData attr = symbol.GetAttributes().First();

            int result = attr.GetInteger("NonExistent", 99);

            Assert.That(result, Is.EqualTo(99));
        }

        [Test]
        public void AttributeDataGetIntegerReturnsValueWhenFound()
        {
            string code = @"
using System;
namespace Test
{
    [MyAttr(Count = 7)]
    public class MyClass { }

    [AttributeUsage(AttributeTargets.Class)]
    public class MyAttrAttribute : Attribute
    {
        public int Count { get; set; }
    }
}
";
            CSharpCompilation compilation = CSharpCompilation.Create("TestAssembly")
                .AddReferences(CompilerUtils.TrustedReferences)
                .AddSyntaxTrees(CSharpSyntaxTree.ParseText(code));

            INamedTypeSymbol symbol = compilation.GetTypeByMetadataName("Test.MyClass");
            AttributeData attr = symbol.GetAttributes()
                .First(a => a.AttributeClass?.Name == "MyAttrAttribute");

            int result = attr.GetInteger("Count");

            Assert.That(result, Is.EqualTo(7));
        }

        [Test]
        public void AttributeDataGetIntegerReturnsDefaultValueOfZero()
        {
            AttributeData attr = null;

            int result = attr.GetInteger("SomeName");

            Assert.That(result, Is.EqualTo(0));
        }

        [Test]
        public void GetValueWithCustomConverterAppliesConverter()
        {
            var options = new AnalyzerOptions(new Dictionary<string, string>
            {
                [$"build_property.{SourceGenerator.Name}MyProp".ToLowerInvariant()] = "42"
            });

            int result = options.GetValue("MyProp", s => int.Parse(s, System.Globalization.CultureInfo.InvariantCulture));

            Assert.That(result, Is.EqualTo(42));
        }

        [Test]
        public void GetValueWithMissingKeyPassesNullToConverter()
        {
            var options = new AnalyzerOptions(new Dictionary<string, string>());

            string result = options.GetValue("Missing", s => s ?? "default");

            Assert.That(result, Is.EqualTo("default"));
        }

        [Test]
        public void GetValueWithBuildMetadataPrefixUsesCorrectKey()
        {
            var options = new AnalyzerOptions(new Dictionary<string, string>
            {
                [$"build_metadata.AdditionalFiles.{SourceGenerator.Name}Prop".ToLowerInvariant()] = "val"
            });

            string result = options.GetValue("Prop", s => s, buildProperty: false);

            Assert.That(result, Is.EqualTo("val"));
        }

        [Test]
        public void GetStringsMixedDelimitersParseCorrectly()
        {
            var options = new AnalyzerOptions(new Dictionary<string, string>
            {
                [$"build_property.{SourceGenerator.Name}Items".ToLowerInvariant()] = "a;b,c+d"
            });

            List<string> result = options.GetStrings("Items");

            Assert.That(result, Has.Count.EqualTo(4));
            Assert.That(result, Does.Contain("a"));
            Assert.That(result, Does.Contain("b"));
            Assert.That(result, Does.Contain("c"));
            Assert.That(result, Does.Contain("d"));
        }

        [Test]
        public void ImplementsInterfaceWithInheritedInterfaceReturnsTrue()
        {
            string code = @"
namespace Test
{
    public interface IBase { }
    public interface IDerived : IBase { }
    public class MyClass : IDerived { }
}
";
            CSharpCompilation compilation = CSharpCompilation.Create("TestAssembly")
                .AddReferences(CompilerUtils.TrustedReferences)
                .AddSyntaxTrees(CSharpSyntaxTree.ParseText(code));

            INamedTypeSymbol symbol = compilation.GetTypeByMetadataName("Test.MyClass");

            Assert.That(symbol, Is.Not.Null);
            Assert.That(symbol.ImplementsInterface("IBase"), Is.True);
            Assert.That(symbol.ImplementsInterface("IDerived"), Is.True);
        }

        [Test]
        public void GetFullNamespaceWithDeeplyNestedNamespace()
        {
            string code = @"
namespace A.B.C.D.E
{
    public class DeepClass { }
}
";
            CSharpCompilation compilation = CSharpCompilation.Create("TestAssembly")
                .AddReferences(CompilerUtils.TrustedReferences)
                .AddSyntaxTrees(CSharpSyntaxTree.ParseText(code));

            INamedTypeSymbol symbol = compilation.GetTypeByMetadataName("A.B.C.D.E.DeepClass");

            Assert.That(symbol, Is.Not.Null);
            Assert.That(symbol.GetFullNamespace(), Is.EqualTo("A.B.C.D.E"));
        }

        [Test]
        public void GetFullyQualifiedTypeNameOmitsGlobalNamespace()
        {
            string code = @"
namespace Test
{
    public class SimpleType { }
}
";
            CSharpCompilation compilation = CSharpCompilation.Create("TestAssembly")
                .AddReferences(CompilerUtils.TrustedReferences)
                .AddSyntaxTrees(CSharpSyntaxTree.ParseText(code));

            INamedTypeSymbol symbol = compilation.GetTypeByMetadataName("Test.SimpleType");

            Assert.That(symbol, Is.Not.Null);
            string result = symbol.GetFullyQualifiedTypeName();
            Assert.That(result, Is.EqualTo("global::Test.SimpleType"));
        }

        [Test]
        public void AttributeDataGetValueReturnsNullForNonStringNamedArgument()
        {
            string code = @"
using System;
namespace Test
{
    [MyAttr(Count = 5)]
    public class MyClass { }

    [AttributeUsage(AttributeTargets.Class)]
    public class MyAttrAttribute : Attribute
    {
        public int Count { get; set; }
    }
}
";
            CSharpCompilation compilation = CSharpCompilation.Create("TestAssembly")
                .AddReferences(CompilerUtils.TrustedReferences)
                .AddSyntaxTrees(CSharpSyntaxTree.ParseText(code));

            INamedTypeSymbol symbol = compilation.GetTypeByMetadataName("Test.MyClass");
            AttributeData attr = symbol.GetAttributes()
                .First(a => a.AttributeClass?.Name == "MyAttrAttribute");

            string result = attr.GetValue("Count");

            Assert.That(result, Is.Null);
        }

        [Test]
        public void AttributeDataGetIntegerReturnsDefaultForNonIntegerNamedArgument()
        {
            string code = @"
using System;
namespace Test
{
    [MyAttr(Name = ""hello"")]
    public class MyClass { }

    [AttributeUsage(AttributeTargets.Class)]
    public class MyAttrAttribute : Attribute
    {
        public string Name { get; set; }
    }
}
";
            CSharpCompilation compilation = CSharpCompilation.Create("TestAssembly")
                .AddReferences(CompilerUtils.TrustedReferences)
                .AddSyntaxTrees(CSharpSyntaxTree.ParseText(code));

            INamedTypeSymbol symbol = compilation.GetTypeByMetadataName("Test.MyClass");
            AttributeData attr = symbol.GetAttributes()
                .First(a => a.AttributeClass?.Name == "MyAttrAttribute");

            int result = attr.GetInteger("Name", 99);

            Assert.That(result, Is.EqualTo(99));
        }

        [Test]
        public void ToNodeSetOptionsIgnoreFieldFromBuildMetadata()
        {
            string prefix = $"build_metadata.AdditionalFiles.{SourceGenerator.Name}";
            var options = new AnalyzerOptions(new Dictionary<string, string>
            {
                [$"{prefix}Ignore".ToLowerInvariant()] = "false"
            });

            NodesetFileOptions result = options.ToNodeSetOptions();

            Assert.That(result.Ignore, Is.False);
        }
    }
}
