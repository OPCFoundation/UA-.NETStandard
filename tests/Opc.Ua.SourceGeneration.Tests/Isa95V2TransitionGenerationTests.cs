// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT

using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using NUnit.Framework;

namespace Opc.Ua.SourceGeneration
{
    [TestFixture]
    [Category("SourceGeneration")]
    public sealed class Isa95V2TransitionGenerationTests
    {
        /// <summary>
        /// The effective receiver has method-triggered effects, while preparation and closing do not emit events.
        /// </summary>
        [Test]
        public void PublishedNodeSetEmitsScopedTransitionsWithTheirCausesAndEffects()
        {
            GeneratorRunResult result = Generate(EmbeddedText.From(kModelFile));
            Assert.That(result.Diagnostics, Is.Empty);
            string source = result.GeneratedSources.Single().SourceText.ToString();

            Assert.That(source, Does.Contain("new(Isa95V2Machine.Receiver, 1u, 2u, 2u, \"Start\", true)"));
            Assert.That(source, Does.Contain("new(Isa95V2Machine.Prepare, 1u, 2u, 1u, null, false)"));
            Assert.That(source, Does.Contain("new(Isa95V2Machine.Interrupted, 1u, 2u, 1u, null, false)"));
            Assert.That(source, Does.Contain("new(Isa95V2Machine.Ended, 1u, 2u, 1u, null, false)"));
            Assert.That(source.Split('\n').Count(
                line => line.IndexOf("new(Isa95V2Machine.Receiver, 1u, 2u,", StringComparison.Ordinal) >= 0),
                Is.EqualTo(1));
        }

        /// <summary>
        /// A transition number comes from the model declaration, not from its position in the table.
        /// </summary>
        [Test]
        public void ChangedTransitionNumberIsEmittedFromTheNodeSet()
        {
            XDocument model = LoadModel();
            Node(model, "ns=1;i=6107").Element(s_nodeset + "Value")!.Elements().Single().Value = "42";

            GeneratorRunResult result = GenerateModel(model);
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.GeneratedSources.Single().SourceText.ToString(),
                Does.Contain("new(Isa95V2Machine.Receiver, 1u, 2u, 42u, \"Start\", true)"));
        }

        /// <summary>
        /// Invalid transitions must not produce a partial runtime rule table.
        /// </summary>
        [TestCase("FromState", null, "FromState references")]
        [TestCase("ToState", "ns=1;i=5052", "must be states")]
        [TestCase("HasCause", "ns=1;i=5063", "cause")]
        [TestCase("HasEffect", "ns=1;i=5063", "effect")]
        [TestCase("HasProperty", "ns=1;i=999999", "HasProperty target")]
        public void InvalidTransitionReferenceProducesAnError(string referenceType, string? target, string diagnostic)
        {
            XDocument model = LoadModel();
            XElement reference = Node(model, "ns=1;i=5070").Element(s_nodeset + "References")!
                .Elements(s_nodeset + "Reference")
                .Single(item => (string?)item.Attribute("ReferenceType") == referenceType);
            if (target == null)
            {
                reference.Remove();
            }
            else
            {
                reference.Value = target;
            }
            AssertInvalid(GenerateModel(model), diagnostic);
        }

        [TestCase("2", "duplicates a transition number")]
        [TestCase("not a number", "UInt32 TransitionNumber")]
        public void InvalidTransitionNumberProducesAnError(string number, string diagnostic)
        {
            XDocument model = LoadModel();
            Node(model, "ns=1;i=6106").Element(s_nodeset + "Value")!.Elements().Single().Value = number;
            AssertInvalid(GenerateModel(model), diagnostic);
        }

        [TestCase("<UANodeSet>", "unexpected end")]
        [TestCase("<UANodeSet />", "must occupy namespace index one")]
        public void InvalidModelInputProducesAnError(string xml, string diagnostic)
        {
            AssertInvalid(Generate(EmbeddedText.Create(kModelFile, xml)), diagnostic);
        }

        [Test]
        public void UnrelatedInputDoesNotEmitTransitionSource()
        {
            GeneratorRunResult result = Generate(EmbeddedText.Create("Other.NodeSet2.xml", "<unrelated />"));
            Assert.That(result.Diagnostics, Is.Empty);
            Assert.That(result.GeneratedSources, Is.Empty);
        }

        [Test]
        public void AmbiguousInputsProduceAnErrorInsteadOfChoosingOne()
        {
            AdditionalText model = EmbeddedText.From(kModelFile);
            AdditionalText duplicate = EmbeddedText.Create($"duplicate/{kModelFile}", "<duplicate />");
            AssertInvalid(Generate(model, duplicate), "single AdditionalFile");
        }

        private static void AssertInvalid(GeneratorRunResult result, string message)
        {
            Assert.That(result.GeneratedSources, Is.Empty);
            Diagnostic error = result.Diagnostics.Single();
            Assert.That(error.Severity, Is.EqualTo(DiagnosticSeverity.Error));
            Assert.That(error.GetMessage(CultureInfo.InvariantCulture), Does.Contain(message).IgnoreCase);
        }

        private static XDocument LoadModel()
        {
            return XDocument.Parse(EmbeddedText.From(kModelFile).GetText()!.ToString());
        }

        private static XElement Node(XDocument model, string id)
        {
            return model.Root!.Elements().Single(element => (string?)element.Attribute("NodeId") == id);
        }

        private static GeneratorRunResult GenerateModel(XDocument model)
        {
            return Generate(EmbeddedText.Create(kModelFile, model.ToString()));
        }

        private static GeneratorRunResult Generate(params AdditionalText[] files)
        {
            GeneratorDriver driver = CSharpGeneratorDriver.Create(new TransitionGenerator())
                .WithUpdatedParseOptions(new CSharpParseOptions(LanguageVersion.CSharp13))
                .AddAdditionalTexts(ImmutableArray.CreateRange(files));
            return driver.RunGenerators(CSharpCompilation.Create("Isa95TransitionTest"))
                .GetRunResult().Results.Single();
        }

        private const string kModelFile = "Opc.Ua.ISA95.JobControl.V2.NodeSet2.xml";
        private static readonly XNamespace s_nodeset = "http://opcfoundation.org/UA/2011/03/UANodeSet.xsd";

        private sealed class TransitionGenerator : IIncrementalGenerator
        {
            public void Initialize(IncrementalGeneratorInitializationContext context)
            {
                Isa95V2TransitionGeneration.Register(context);
            }
        }
    }
}
