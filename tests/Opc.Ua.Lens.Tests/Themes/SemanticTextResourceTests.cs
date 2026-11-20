// ========================================================================
// SPDX-FileCopyrightText: Copyright (c) 2005-2026, OPC Federation AISBL
// SPDX-License-Identifier: MIT
// ========================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using NUnit.Framework;

namespace UaLens.Tests.Themes
{
    [TestFixture]
    public sealed class SemanticTextResourceTests
    {
        [Test]
        public async Task MarkupForegroundsUseSemanticTextResourcesInsteadOfStatusAccents()
        {
            Assembly assembly = typeof(SemanticTextResourceTests).Assembly;
            string[] names = [.. assembly.GetManifestResourceNames()
                .Where(name => name.StartsWith("LensMarkup/", StringComparison.Ordinal))
                .Order(StringComparer.Ordinal)];
            Assert.That(names, Is.Not.Empty, "The regression must inspect the application's embedded AXAML sources.");
            var violations = new List<string>();
            foreach (string name in names)
            {
                await using Stream source = assembly.GetManifestResourceStream(name)
                    ?? throw new InvalidOperationException($"Missing embedded markup: {name}");
                XDocument document = await XDocument.LoadAsync(
                    source, LoadOptions.None, CancellationToken.None).ConfigureAwait(false);
                foreach (XElement element in document.Descendants())
                {
                    foreach (XAttribute attribute in element.Attributes())
                    {
                        if (IsForeground(attribute.Name.LocalName) &&
                            IsStatusAccentResource(attribute.Value))
                        {
                            violations.Add(
                                $"{name}: {element.Name.LocalName}.{attribute.Name.LocalName}={attribute.Value}");
                        }
                    }
                    if (element.Name.LocalName == "Setter" &&
                        IsForeground(element.Attribute("Property")?.Value) &&
                        IsStatusAccentResource(element.Attribute("Value")?.Value))
                    {
                        violations.Add($"{name}: Foreground style setter uses {element.Attribute("Value")?.Value}");
                    }
                }
            }
            Assert.That(violations, Is.Empty,
                "Use ErrorText, WarningText, SuccessText or InfoText for semantic text; keep accents for decoration.");
        }

        [TestCase("Foreground", true)]
        [TestCase("TextElement.Foreground", true)]
        [TestCase("Background", false)]
        [TestCase("BorderBrush", false)]
        [TestCase("Fill", false)]
        public void TextPolicyLeavesDecorativePropertiesAlone(string property, bool expected)
        {
            Assert.That(IsForeground(property), Is.EqualTo(expected));
        }

        [TestCase("{DynamicResource AccentRed}", true)]
        [TestCase("{StaticResource AccentYellowLight}", true)]
        [TestCase("{DynamicResource ResourceKey=AccentGreen}", true)]
        [TestCase("{DynamicResource ErrorText}", false)]
        [TestCase("{DynamicResource WarningText}", false)]
        [TestCase("{DynamicResource SuccessText}", false)]
        [TestCase("{DynamicResource InfoText}", false)]
        public void TextPolicyRecognizesStatusAccentResourceBindings(string value, bool expected)
        {
            Assert.That(IsStatusAccentResource(value), Is.EqualTo(expected));
        }

        private static bool IsForeground(string? property)
        {
            return property is not null &&
                (property == "Foreground" || property.EndsWith(".Foreground", StringComparison.Ordinal));
        }

        private static bool IsStatusAccentResource(string? value)
        {
            if (value is null ||
                !value.EndsWith('}', StringComparison.Ordinal) ||
                (!value.StartsWith("{DynamicResource ", StringComparison.Ordinal) &&
                    !value.StartsWith("{StaticResource ", StringComparison.Ordinal)))
            {
                return false;
            }
            string key = value[(value.IndexOf(' ', StringComparison.Ordinal) + 1)..^1].Trim();
            const string resourceKey = "ResourceKey=";
            if (key.StartsWith(resourceKey, StringComparison.Ordinal))
            {
                key = key[resourceKey.Length..].Trim();
            }
            return key is "AccentRed" or "AccentRedLight" or "AccentYellow" or
                "AccentYellowLight" or "AccentGreen" or "AccentCyan";
        }
    }
}
