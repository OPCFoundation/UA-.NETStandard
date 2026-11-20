// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using Opc.Ua.Schema.Model;

namespace Opc.Ua.SourceGeneration.Generator.Tests
{
    /// <summary>
    /// Tests for <see cref="Generators.OverrideDependencyPrefixes"/> — the
    /// cross-namespace prefix override step used by the source generator
    /// to align dependency type references with the C# prefix published
    /// by referenced assemblies' [ModelDependencyAttribute] entries.
    /// </summary>
    [TestFixture]
    [Category("Api")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class CrossNamespacePrefixTests
    {
        private const string TargetUri = "http://example.org/UA/Target/";
        private const string DependencyUri = "http://opcfoundation.org/UA/DI/";
        private const string MachineryUri = "http://opcfoundation.org/UA/Machinery/";

        /// <summary>
        /// When the loaded ModelDesign declares a dependency namespace with
        /// the auto-generated prefix (e.g. <c>Opc.Ua.DI</c>) but a
        /// referenced assembly publishes that model under a different C#
        /// prefix (e.g. <c>Opc.Ua.Di</c>), the override rewrites the
        /// dependency namespace prefix to match.
        /// </summary>
        [Test]
        public void ModelWithDifferentPrefixAndXmlPrefix_EmitsCSharpPrefix()
        {
            var target = new Namespace { Value = TargetUri, Prefix = "Opc.Ua.Target" };
            var dep = new Namespace
            {
                Value = DependencyUri,
                Prefix = "Opc.Ua.DI",
                XmlPrefix = "DI"
            };
            IModelDesign md = BuildDesign(target, [target, dep]);
            var referenced = new Dictionary<string, ModelDependencyReference>
            {
                [DependencyUri] = new ModelDependencyReference(
                    "Opc.Ua.Di", DependencyUri, "Opc.Ua.Di", null!, null!)
            };

            Generators.OverrideDependencyPrefixes(md, referenced);

            Assert.That(dep.Prefix, Is.EqualTo("Opc.Ua.Di"));
            // XmlPrefix is untouched — only the C# Prefix is rewritten.
            Assert.That(dep.XmlPrefix, Is.EqualTo("DI"));
        }

        /// <summary>
        /// Looking up by URI (not by XmlPrefix or auto-generated name)
        /// matches the entry in <see cref="ModelDependencyReference"/>.
        /// </summary>
        [Test]
        public void CrossModelReference_ResolvesViaUriLookup()
        {
            var target = new Namespace { Value = TargetUri, Prefix = "Opc.Ua.Target" };
            var dep = new Namespace { Value = DependencyUri, Prefix = "wrong" };
            IModelDesign md = BuildDesign(target, [target, dep]);
            var referenced = new Dictionary<string, ModelDependencyReference>
            {
                [DependencyUri] = new ModelDependencyReference(
                    "Opc.Ua.Di", DependencyUri, "Opc.Ua.Di", null!, null!)
            };

            Generators.OverrideDependencyPrefixes(md, referenced);

            Assert.That(dep.Prefix, Is.EqualTo("Opc.Ua.Di"));
        }

        /// <summary>
        /// A namespace URI that is not in the referenced-models map is
        /// left untouched.
        /// </summary>
        [Test]
        public void UnknownNamespaceUri_NotRewritten()
        {
            var target = new Namespace { Value = TargetUri, Prefix = "Opc.Ua.Target" };
            var dep = new Namespace
            {
                Value = "http://unknown.example.org/X/",
                Prefix = "Original"
            };
            IModelDesign md = BuildDesign(target, [target, dep]);
            var referenced = new Dictionary<string, ModelDependencyReference>
            {
                [DependencyUri] = new ModelDependencyReference(
                    "Opc.Ua.Di", DependencyUri, "Opc.Ua.Di", null!, null!)
            };

            Generators.OverrideDependencyPrefixes(md, referenced);

            Assert.That(dep.Prefix, Is.EqualTo("Original"));
        }

        /// <summary>
        /// When multiple namespaces match referenced-models entries, all
        /// of them are rewritten.
        /// </summary>
        [Test]
        public void MultipleNamespaceImports_AllMapCorrectly()
        {
            var target = new Namespace { Value = TargetUri, Prefix = "Opc.Ua.Target" };
            var di = new Namespace { Value = DependencyUri, Prefix = "Opc.Ua.DI" };
            var machinery = new Namespace { Value = MachineryUri, Prefix = "Opc.Ua.Machinery" };
            IModelDesign md = BuildDesign(target, [target, di, machinery]);
            var referenced = new Dictionary<string, ModelDependencyReference>
            {
                [DependencyUri] = new ModelDependencyReference(
                    "Opc.Ua.Di", DependencyUri, "Opc.Ua.Di", null!, null!),
                [MachineryUri] = new ModelDependencyReference(
                    "Acme.Machinery", MachineryUri, "Acme.Machinery", null!, null!)
            };

            Generators.OverrideDependencyPrefixes(md, referenced);

            Assert.That(di.Prefix, Is.EqualTo("Opc.Ua.Di"));
            Assert.That(machinery.Prefix, Is.EqualTo("Acme.Machinery"));
        }

        /// <summary>
        /// The target namespace itself is never rewritten, even if a
        /// referenced-models entry happens to claim the same URI.
        /// Otherwise the generator would emit references into someone
        /// else's assembly.
        /// </summary>
        [Test]
        public void TargetNamespace_NeverRewritten()
        {
            var target = new Namespace { Value = TargetUri, Prefix = "Opc.Ua.Target" };
            IModelDesign md = BuildDesign(target, [target]);
            var referenced = new Dictionary<string, ModelDependencyReference>
            {
                [TargetUri] = new ModelDependencyReference(
                    "Foreign.Asm", TargetUri, "Foreign.Target", null!, null!)
            };

            Generators.OverrideDependencyPrefixes(md, referenced);

            Assert.That(target.Prefix, Is.EqualTo("Opc.Ua.Target"));
        }

        /// <summary>
        /// Null inputs do not throw.
        /// </summary>
        [Test]
        public void NullInputs_NoThrow()
        {
            // Null modelDesign
            Assert.DoesNotThrow(() =>
                Generators.OverrideDependencyPrefixes(
                    null!, new Dictionary<string, ModelDependencyReference>()));

            // Null referenced map
            var target = new Namespace { Value = TargetUri, Prefix = "Opc.Ua.Target" };
            IModelDesign md = BuildDesign(target, [target]);
            Assert.DoesNotThrow(() =>
                Generators.OverrideDependencyPrefixes(md, null!));
        }

        private static IModelDesign BuildDesign(Namespace target, Namespace[] all)
        {
            var mock = new Mock<IModelDesign>();
            mock.Setup(m => m.TargetNamespace).Returns(target);
            mock.Setup(m => m.Namespaces).Returns(all);
            return mock.Object;
        }
    }
}
