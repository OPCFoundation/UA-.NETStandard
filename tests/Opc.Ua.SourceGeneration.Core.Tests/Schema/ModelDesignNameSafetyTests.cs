// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Xml;
using NUnit.Framework;
using Opc.Ua.SourceGeneration;

namespace Opc.Ua.Schema.Model.Tests
{
    /// <summary>
    /// Model names (SymbolicName, SymbolicId, ClassName, namespace Prefix and
    /// Name) are emitted verbatim as C# identifiers, inside comments and inside
    /// string literals. A model file must not be able to inject code through
    /// them, so anything but a plain identifier is rejected.
    /// </summary>
    [TestFixture]
    [Category("ModelDesign")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [Parallelizable]
    public class ModelDesignNameSafetyTests
    {
        [TestCase("PumpType")]
        [TestCase("_Pump2")]
        [TestCase("Server_ServerCapabilities")]
        [TestCase("Ventil\u00F6ffnung")]
        public void IsCodeSafeIdentifierAcceptsIdentifiers(string name)
        {
            Assert.That(name.IsCodeSafeIdentifier(), Is.True);
        }

        [TestCase("")]
        [TestCase("1Pump")]
        [TestCase("Pump Type")]
        [TestCase("Pump{}public class X{}")]
        [TestCase("A/*x*/")]
        [TestCase("A.B")]
        [TestCase("A\nB")]
        [TestCase("A\u2028B")]
        [TestCase("A\"B")]
        public void IsCodeSafeIdentifierRejectsAnythingElse(string name)
        {
            Assert.That(name.IsCodeSafeIdentifier(), Is.False);
        }

        [TestCase("Opc.Ua")]
        [TestCase("Acme.Opc.Ua.Pumps")]
        [TestCase("Pumps")]
        public void IsCodeSafeNamespaceAcceptsDottedIdentifiers(string name)
        {
            Assert.That(name.IsCodeSafeNamespace(), Is.True);
        }

        [TestCase("Acme.Inj{}namespace X")]
        [TestCase("..\\..\\x")]
        [TestCase("Acme..Pumps")]
        [TestCase("Acme.")]
        [TestCase("Opc.Ua/*x*/")]
        public void IsCodeSafeNamespaceRejectsAnythingElse(string name)
        {
            Assert.That(name.IsCodeSafeNamespace(), Is.False);
        }

        [TestCase("Opc.Ua.Di", true, "Opc.Ua.Di")]
        [TestCase("Opc.Ua.ISA95-JOBCONTROL_V2", true, "Opc.Ua.ISA95_JOBCONTROL_V2")]
        [TestCase("192.168.0.1.Models", true, "_192._168._0._1.Models")]
        [TestCase("x{}namespace y..z", true, "x__namespace_y.z")]
        [TestCase("OpcUaDi", false, "OpcUaDi")]
        [TestCase("a\"b", false, "a_b")]
        [TestCase("1x", false, "_1x")]
        public void ToCodeSafeNameKeepsValidNamesAndRepairsOthers(
            string name,
            bool dotted,
            string expected)
        {
            string result = name.ToCodeSafeName(dotted);

            Assert.That(result, Is.EqualTo(expected));
            Assert.That(dotted ? result.IsCodeSafeNamespace() : result.IsCodeSafeIdentifier(), Is.True);
        }

        [TestCase("a\rb", "a b")]
        [TestCase("a\nb", "a b")]
        [TestCase("a\u0085b", "a b")]
        [TestCase("a\u2028b", "a b")]
        [TestCase("a\u2029b", "a b")]
        [TestCase("plain", "plain")]
        public void AsSingleLineCommentTextReplacesLineTerminators(string text, string expected)
        {
            Assert.That(text.AsSingleLineCommentText(), Is.EqualTo(expected));
        }

        [Test]
        public void EnsureNamesAreCodeSafeAcceptsPlainNames()
        {
            var type = new ObjectTypeDesign
            {
                SymbolicName = new XmlQualifiedName("PumpType", "urn:x"),
                SymbolicId = new XmlQualifiedName("PumpType", "urn:x"),
                ClassName = "Pump"
            };

            Assert.DoesNotThrow(() => ModelDesignValidator.EnsureNamesAreCodeSafe([type]));
        }

        [TestCase("Sym{}\u2028PWN", "PumpType", "Pump")]
        [TestCase("PumpType", "A/*\" + Run() + \"*/", "Pump")]
        [TestCase("PumpType", "PumpType", "Pump{} public class Injected{} public partial class Q")]
        public void EnsureNamesAreCodeSafeRejectsInjectedNames(
            string symbolicName,
            string symbolicId,
            string className)
        {
            var type = new ObjectTypeDesign
            {
                SymbolicName = new XmlQualifiedName(symbolicName, "urn:x"),
                SymbolicId = new XmlQualifiedName(symbolicId, "urn:x"),
                ClassName = className,
                BrowseName = "Pump"
            };

            InvalidOperationException ex = Assert.Throws<InvalidOperationException>(
                () => ModelDesignValidator.EnsureNamesAreCodeSafe([type]));
            Assert.That(ex.Message, Does.Contain("not a valid C# identifier"));
            Assert.That(ex.Message, Does.Not.Contain("\u2028"));
        }

        [Test]
        public void EnsureNamesAreCodeSafeChecksChildren()
        {
            var child = new VariableDesign
            {
                SymbolicName = new XmlQualifiedName("Flow\n[ModuleInitializer] static void P() { } //", "urn:x"),
                BrowseName = "Flow"
            };
            var type = new ObjectTypeDesign
            {
                SymbolicName = new XmlQualifiedName("PumpType", "urn:x"),
                SymbolicId = new XmlQualifiedName("PumpType", "urn:x"),
                Children = new ListOfChildren { Items = [child] }
            };

            Assert.Throws<InvalidOperationException>(
                () => ModelDesignValidator.EnsureNamesAreCodeSafe([type]));
        }

        [TestCase("Acme.Inj{}namespace PWN", "Inj")]
        [TestCase("Opc.Ua/*INJ*/", "OpcUa")]
        [TestCase("Opc.Ua", "OpcUa/*INJ2*/")]
        [TestCase("Opc.Ua", "OpcUa = \"\"; public const string X")]
        public void EnsureNamespacesAreCodeSafeRejectsInjectedNames(string prefix, string name)
        {
            var ns = new Namespace { Prefix = prefix, Name = name, Value = "http://opcfoundation.org/UA/" };

            Assert.Throws<InvalidOperationException>(
                () => ModelDesignValidator.EnsureNamespacesAreCodeSafe([ns]));
        }

        [Test]
        public void EnsureNamespacesAreCodeSafeAcceptsPlainNames()
        {
            var ns = new Namespace { Prefix = "Opc.Ua.Di", Name = "OpcUaDi", Value = "http://opcfoundation.org/UA/DI/" };

            Assert.DoesNotThrow(() => ModelDesignValidator.EnsureNamespacesAreCodeSafe([ns]));
        }
    }
}
