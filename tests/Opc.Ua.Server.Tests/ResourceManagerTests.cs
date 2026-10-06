/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
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
using NUnit.Framework;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Test ResourceManager
    /// </summary>
    [TestFixture]
    [Category("ResourceManager")]
    [Parallelizable]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    public class ResourceManagerTests
    {
        [Test]
        public void TranslateSingleLanguageExactMatch()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var appConfig = new ApplicationConfiguration(telemetry);
            using var resourceManager = new ResourceManager(appConfig);
            var defaultText = new LocalizedText("en-US", "Hello");

            //Act
            LocalizedText resultText = resourceManager.Translate(["en-US", "de-DE"], defaultText);

            // Assert
            Assert.That(resultText, Is.EqualTo(defaultText));
        }

        [Test]
        public void TranslateSingleLanguageWithInfoExactMatch()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var appConfig = new ApplicationConfiguration(telemetry);
            using var resourceManager = new ResourceManager(appConfig);
            var defaultText = new LocalizedText("greeting", "en-US", "Hello");

            //Act
            LocalizedText resultText = resourceManager.Translate(["en-US", "de-DE"], defaultText);

            // Assert
            Assert.That(resultText, Is.EqualTo(defaultText));
        }

        [Test]
        public void TranslateSingleLanguageWithArguments()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var appConfig = new ApplicationConfiguration(telemetry);
            using var resourceManager = new ResourceManager(appConfig);
            resourceManager.Add("greeting", "en-US", "Hello {0}");

            //Act
            LocalizedText resultText = resourceManager.Translate(
                ["en-US", "de-DE"],
                "greeting",
                "Hello {0}",
                "User");

            // Assert
            Assert.That(resultText.Text, Is.EqualTo("Hello User"));
            Assert.That(resultText.Locale, Is.EqualTo("en-US"));
        }

        [Test]
        public void TranslateMultiLanguageExactMatchMulRequested()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var appConfig = new ApplicationConfiguration(telemetry);
            using var resourceManager = new ResourceManager(appConfig);
            var translations = new Dictionary<string, string> {
                { "en-US", "Hello" },
                { "de-DE", "Hallo" } };
            LocalizedText defaultText = new LocalizedText("greeting", translations).AsMultiLanguage();

            //Act
            LocalizedText resultText = resourceManager.Translate(
                ["mul", "de-DE", "en-US"],
                defaultText);

            // Assert
            Assert.That(resultText, Is.EqualTo(defaultText));
        }

        [Test]
        public void TranslateMultiLanguageMulRequested()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var appConfig = new ApplicationConfiguration(telemetry);
            using var resourceManager = new ResourceManager(appConfig);
            var translations = new Dictionary<string, string>
            {
                { "en-US", "Hello" },
                { "de-DE", "Hallo" },
                { "fr-FR", "Bonjour" }
            };
            var defaultText = new LocalizedText("greeting", translations);

            //Act
            LocalizedText resultText = resourceManager.Translate(
                ["mul", "de-DE", "en-US"],
                defaultText);

            // Assert
            Assert.That(
                resultText.Text,
                Is.EqualTo(/*lang=json,strict*/ "{\"t\":[[\"de-DE\",\"Hallo\"],[\"en-US\",\"Hello\"]]}"));
            Assert.That(resultText.Locale, Is.EqualTo("mul"));
        }

        [Test]
        public void TranslateSingleLanguageMulRequested()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var appConfig = new ApplicationConfiguration(telemetry);
            using var resourceManager = new ResourceManager(appConfig);
            var defaultText = new LocalizedText("greeting", "en-US", "Hello");

            //Act
            LocalizedText resultText = resourceManager.Translate(
                ["mul", "de-DE", "en-US"],
                defaultText);

            // Assert
            Assert.That(resultText, Is.EqualTo(defaultText));
        }

        [Test]
        public void TranslateNoLocalesRequestedDefaultTextReturned()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var appConfig = new ApplicationConfiguration(telemetry);
            using var resourceManager = new ResourceManager(appConfig);
            var defaultText = new LocalizedText("greeting", "en-US", "Hello");

            //Act
            LocalizedText resultText = resourceManager.Translate(default, defaultText);

            // Assert
            Assert.That(resultText, Is.EqualTo(defaultText));
        }

        [Test]
        public void TranslateSingleLanguageMulRequestedWithTranslation()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var appConfig = new ApplicationConfiguration(telemetry);
            using var resourceManager = new ResourceManager(appConfig);
            var defaultText = new LocalizedText("greeting", "en-US", "Hello");
            resourceManager.Add("greeting", "de-DE", "Hallo");
            resourceManager.Add("greeting", "fr-FR", "Bonjour");

            //Act
            LocalizedText resultText = resourceManager.Translate(
                ["mul", "de-DE", "en-US"],
                defaultText);

            // Assert
            Assert.That(
                resultText.Text,
                Is.EqualTo(/*lang=json,strict*/ "{\"t\":[[\"de-DE\",\"Hallo\"],[\"en-US\",\"Hello\"]]}"));
            Assert.That(resultText.Locale, Is.EqualTo("mul"));
        }

        [Test]
        public void TranslateKeyMulRequestedWithTranslation()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var appConfig = new ApplicationConfiguration(telemetry);
            using var resourceManager = new ResourceManager(appConfig);
            resourceManager.Add("greeting", "de-DE", "Hallo");
            resourceManager.Add("greeting", "en-US", "Hello");

            //Act
            LocalizedText resultText = resourceManager.Translate(
                ["mul", "de-DE", "en-US"],
                "greeting",
                null);

            // Assert
            Assert.That(
                resultText.Text,
                Is.EqualTo(/*lang=json,strict*/ "{\"t\":[[\"de-DE\",\"Hallo\"],[\"en-US\",\"Hello\"]]}"));
            Assert.That(resultText.Locale, Is.EqualTo("mul"));
        }

        [Test]
        public void TranslateKeyMulRequestedAllLanguagesWithTranslation()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var appConfig = new ApplicationConfiguration(telemetry);
            using var resourceManager = new ResourceManager(appConfig);
            resourceManager.Add("greeting", "de-DE", "Hallo");
            resourceManager.Add("greeting", "en-US", "Hello");

            //Act
            LocalizedText resultText = resourceManager.Translate(["mul"], "greeting", null);

            // Assert
            Assert.That(
                resultText.Text,
                Is.EqualTo(/*lang=json,strict*/ "{\"t\":[[\"de-DE\",\"Hallo\"],[\"en-US\",\"Hello\"]]}"));
            Assert.That(resultText.Locale, Is.EqualTo("mul"));
        }

        [Test]
        public void TranslateKeyMulRequestedTranslationWithParameters()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            // Arrange
            var appConfig = new ApplicationConfiguration(telemetry);
            using var resourceManager = new ResourceManager(appConfig);
            resourceManager.Add("greeting", "de-DE", "Hallo {0}");
            resourceManager.Add("greeting", "en-US", "Hello {0}");

            //Act
            LocalizedText resultText = resourceManager.Translate(["mul"], "greeting", null, "User");

            // Assert
            Assert.That(
                resultText.Text,
                Is.EqualTo(/*lang=json,strict*/ "{\"t\":[[\"de-DE\",\"Hallo User\"],[\"en-US\",\"Hello User\"]]}"));
            Assert.That(resultText.Locale, Is.EqualTo("mul"));
        }

        [Test]
        public void TranslateUsesPartialLocaleWhenExactLocaleHasNoTranslation()
        {
            var configuration = new ApplicationConfiguration(NUnitTelemetryContext.Create());
            using var resources = new ResourceManager(configuration);
            resources.Add("greeting", "de-AT", "Servus");
            resources.Add("other", "de-DE", "Andere");

            LocalizedText translated = resources.Translate(
                ["de-DE"],
                "greeting",
                "Hello");

            Assert.That(translated.Locale, Is.EqualTo("de-AT"));
            Assert.That(translated.Text, Is.EqualTo("Servus"));
        }

        [Test]
        public void TranslateRetainsOriginalFallbackForAnotherLocale()
        {
            var configuration = new ApplicationConfiguration(NUnitTelemetryContext.Create());
            using var resources = new ResourceManager(configuration);
            resources.Add("greeting", "de-DE", "Hallo {0}");
            var original = new LocalizedText("greeting", "en-US", "Hello {0}", "User");

            LocalizedText german = resources.Translate(["de-DE"], original);
            LocalizedText english = resources.Translate(["en-US"], german);

            Assert.Multiple(() =>
            {
                Assert.That(german.Locale, Is.EqualTo("de-DE"));
                Assert.That(german.Text, Is.EqualTo("Hallo User"));
                Assert.That(german.TranslationInfo, Is.EqualTo(original.TranslationInfo));
                Assert.That(english.Locale, Is.EqualTo("en-US"));
                Assert.That(english.Text, Is.EqualTo("Hello User"));
                Assert.That(english.TranslationInfo, Is.EqualTo(original.TranslationInfo));
                Assert.That(original.Locale, Is.EqualTo("en-US"));
                Assert.That(original.Text, Is.EqualTo("Hello User"));
            });
        }

        [Test]
        public void MissingLocaleUsesOriginalFallbackAfterTranslation()
        {
            var configuration = new ApplicationConfiguration(NUnitTelemetryContext.Create());
            using var resources = new ResourceManager(configuration);
            resources.Add("greeting", "de-DE", "Hallo");
            var original = new LocalizedText("greeting", "en-US", "Hello");
            LocalizedText german = resources.Translate(["de-DE"], original);

            LocalizedText result = resources.Translate(["fr-FR"], german);

            Assert.That(result.Locale, Is.EqualTo("en-US"));
            Assert.That(result.Text, Is.EqualTo("Hello"));
            Assert.That(result.TranslationInfo, Is.EqualTo(original.TranslationInfo));
        }

        [Test]
        public void NoLocalePreferencePreservesTheSelectedTranslation()
        {
            var configuration = new ApplicationConfiguration(NUnitTelemetryContext.Create());
            using var resources = new ResourceManager(configuration);
            resources.Add("greeting", "de-DE", "Hallo");
            var original = new LocalizedText("greeting", "en-US", "Hello");
            LocalizedText german = resources.Translate(["de-DE"], original);

            LocalizedText result = resources.Translate([], german);

            Assert.That(result.Locale, Is.EqualTo("de-DE"));
            Assert.That(result.Text, Is.EqualTo("Hallo"));
            Assert.That(result.TranslationInfo, Is.EqualTo(original.TranslationInfo));
        }

        [Test]
        public void RetainedServiceResultCanBeTranslatedForAnotherSession()
        {
            var configuration = new ApplicationConfiguration(NUnitTelemetryContext.Create());
            using var resources = new ResourceManager(configuration);
            resources.Add("timeout", "de-DE", "Zeitlimit {0}");
            var original = new ServiceResult(
                StatusCodes.BadTimeout,
                new LocalizedText("timeout", "en-US", "Timeout {0}", "Pump"));

            ServiceResult german = resources.Translate(["de-DE"], original);
            ServiceResult english = resources.Translate(["en-US"], german);

            Assert.That(german.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(german.LocalizedText.Text, Is.EqualTo("Zeitlimit Pump"));
            Assert.That(german.LocalizedText.Locale, Is.EqualTo("de-DE"));
            Assert.That(english.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(english.LocalizedText.Text, Is.EqualTo("Timeout Pump"));
            Assert.That(english.LocalizedText.Locale, Is.EqualTo("en-US"));
            Assert.That(english.LocalizedText.TranslationInfo, Is.EqualTo(original.LocalizedText.TranslationInfo));
            Assert.That(original.LocalizedText.Text, Is.EqualTo("Timeout Pump"));
        }

        [Test]
        public void TranslateUsesRegisteredTextAndLocaleInsteadOfFallback()
        {
            var configuration = new ApplicationConfiguration(NUnitTelemetryContext.Create());
            using var resources = new ResourceManager(configuration);
            resources.Add("greeting", "de-DE", "Hallo {0}");

            LocalizedText translated = resources.Translate(["de-DE"], "greeting", "Hello {0}", "User");

            Assert.That(translated.Text, Is.EqualTo("Hallo User"));
            Assert.That(translated.Locale, Is.EqualTo("de-DE"));
            Assert.That(translated.TranslationInfo.Key, Is.EqualTo("greeting"));
            Assert.That(translated.TranslationInfo.Text, Is.EqualTo("Hello {0}"));
        }

        [TestCase(null)]
        [TestCase(Ua.Namespaces.OpcUa)]
        public void TranslateStandardStatusUsesRegisteredTranslation(string namespaceUri)
        {
            var configuration = new ApplicationConfiguration(NUnitTelemetryContext.Create());
            using var resources = new ResourceManager(configuration);
            resources.LoadDefaultText();
            resources.Add(StatusCodes.BadTimeout, "de-DE", "Zeitlimit abgelaufen");
            var input = new ServiceResult(
                StatusCodes.BadTimeout,
                new System.Xml.XmlQualifiedName("BadTimeout", namespaceUri),
                LocalizedText.Null);

            ServiceResult translated = resources.Translate(["de-DE"], input);

            Assert.That(translated.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
            Assert.That(translated.LocalizedText.Text, Is.EqualTo("Zeitlimit abgelaufen"));
            Assert.That(translated.LocalizedText.Locale, Is.EqualTo("de-DE"));
            Assert.That(resources.Translate(["en-US"], input).LocalizedText.Text, Is.EqualTo("BadTimeout"));
        }

        [Test]
        public void TranslateStandardStatusWithoutMappingRetainsSymbolicId()
        {
            var configuration = new ApplicationConfiguration(NUnitTelemetryContext.Create());
            using var resources = new ResourceManager(configuration);

            ServiceResult translated = resources.Translate(["de-DE"], new ServiceResult(StatusCodes.BadTimeout));

            Assert.That(translated.LocalizedText.Text, Is.EqualTo("BadTimeout"));
            Assert.That(translated.StatusCode, Is.EqualTo(StatusCodes.BadTimeout));
        }

        [TestCase("BadTimeout", "urn:custom-errors")]
        [TestCase("CustomTimeout", Ua.Namespaces.OpcUa)]
        public void TranslateCustomSymbolicIdDoesNotUseStandardStatusTranslation(string name, string namespaceUri)
        {
            var configuration = new ApplicationConfiguration(NUnitTelemetryContext.Create());
            using var resources = new ResourceManager(configuration);
            resources.LoadDefaultText();
            resources.Add(StatusCodes.BadTimeout, "de-DE", "Zeitlimit abgelaufen");
            var input = new ServiceResult(
                StatusCodes.BadTimeout,
                new System.Xml.XmlQualifiedName(name, namespaceUri),
                LocalizedText.Null);

            ServiceResult translated = resources.Translate(["de-DE"], input);

            Assert.That(translated.LocalizedText.Text, Is.EqualTo(name));
            Assert.That(translated.NamespaceUri, Is.EqualTo(namespaceUri));
            Assert.That(translated.SymbolicId, Is.EqualTo(name));
        }

        [TestCase("de-DE", "de-DE", "Hallo")]
        [TestCase("en-US", "en-US", "Hello")]
        [TestCase("DE-de", "de-DE", "Hallo")]
        [TestCase("de-CH", "de-DE", "Hallo")]
        [TestCase("xx-XX", "en-US", "Hello")]
        public void TranslateSelectsTheTextsOwnTranslation(
            string requested,
            string expectedLocale,
            string expectedText)
        {
            using var resourceManager = new ResourceManager(
                new ApplicationConfiguration(NUnitTelemetryContext.Create()));

            LocalizedText result = resourceManager.Translate([requested], CreateHelloText());

            Assert.That(result.Locale, Is.EqualTo(expectedLocale));
            Assert.That(result.Text, Is.EqualTo(expectedText));
        }

        [Test]
        public void TranslateMulReturnsAllOwnTranslations()
        {
            using var resourceManager = new ResourceManager(
                new ApplicationConfiguration(NUnitTelemetryContext.Create()));

            LocalizedText result = resourceManager.Translate(["mul"], CreateHelloText());

            Assert.That(result.Locale, Is.EqualTo("mul"));
            Assert.That(result.Translations, Is.EquivalentTo(new Dictionary<string, string>
            {
                { "en-US", "Hello" },
                { "de-DE", "Hallo" }
            }));
        }

        [Test]
        public void TranslateMulWithLocalesReturnsOnlyRequestedTranslations()
        {
            using var resourceManager = new ResourceManager(
                new ApplicationConfiguration(NUnitTelemetryContext.Create()));

            // as many locales requested as the text holds, but not the same ones.
            LocalizedText result = resourceManager.Translate(
                ["mul", "fr-FR", "de-DE"],
                CreateHelloText());

            Assert.That(result.Locale, Is.EqualTo("de-DE"));
            Assert.That(result.Text, Is.EqualTo("Hallo"));
        }

        [Test]
        public void TranslateMatchesTranslationTableCaseInsensitively()
        {
            using var resourceManager = new ResourceManager(
                new ApplicationConfiguration(NUnitTelemetryContext.Create()));
            resourceManager.Add("greeting", "de-DE", "Hallo");

            LocalizedText result = resourceManager.Translate(
                ["DE-de"],
                new LocalizedText("greeting", "en-US", "Hello"));

            Assert.That(result.Locale, Is.EqualTo("de-DE"));
            Assert.That(result.Text, Is.EqualTo("Hallo"));
        }

        [Test]
        public void TranslateMulLabelsRegionFallbackWithItsOwnLocale()
        {
            using var resourceManager = new ResourceManager(
                new ApplicationConfiguration(NUnitTelemetryContext.Create()));
            resourceManager.Add("greeting", "de-DE", "Hallo");

            LocalizedText result = resourceManager.Translate(
                ["mul", "de-AT"],
                new LocalizedText("greeting", "en-US", "Hello"));

            // the de-DE text must not be labelled as de-AT.
            Assert.That(result.Locale, Is.EqualTo("de-DE"));
            Assert.That(result.Text, Is.EqualTo("Hallo"));
        }

        [Test]
        public void TranslateValueSelectsTranslationOfScalarAndArray()
        {
            using var resourceManager = new ResourceManager(
                new ApplicationConfiguration(NUnitTelemetryContext.Create()));
            LocalizedText[] stored = [CreateHelloText(), CreateHelloText()];

            Variant scalar = resourceManager.TranslateValue(["de-DE"], Variant.From(CreateHelloText()));
            Variant array = resourceManager.TranslateValue(["de-DE"], Variant.From(stored.ToArrayOf()));

            Assert.That(scalar.TryGetValue(out LocalizedText text), Is.True);
            Assert.That(text.Text, Is.EqualTo("Hallo"));
            Assert.That(array.TryGetValue(out ArrayOf<LocalizedText> texts), Is.True);
            Assert.That(texts.Count, Is.EqualTo(2));
            Assert.That(texts[0].Text, Is.EqualTo("Hallo"));
            Assert.That(texts[1].Locale, Is.EqualTo("de-DE"));
            // the stored value keeps all its translations.
            Assert.That(stored[0].Translations, Has.Count.EqualTo(2));
            Assert.That(stored[0].Locale, Is.EqualTo("en-US"));
        }

        [Test]
        public void TranslateValueLeavesOtherValuesUnchanged()
        {
            using var resourceManager = new ResourceManager(
                new ApplicationConfiguration(NUnitTelemetryContext.Create()));
            Variant number = Variant.From(42);
            Variant text = Variant.From(CreateHelloText());

            Assert.That(resourceManager.TranslateValue(["de-DE"], number), Is.EqualTo(number));
            Assert.That(resourceManager.TranslateValue(["de-DE"], Variant.Null), Is.EqualTo(Variant.Null));
            Variant untranslated = resourceManager.TranslateValue([], text);
            Assert.That(untranslated.TryGetValue(out LocalizedText value), Is.True);
            Assert.That(value.Locale, Is.EqualTo("en-US"));
        }

        private static LocalizedText CreateHelloText()
        {
            return new LocalizedText(new Dictionary<string, string>
            {
                { "en-US", "Hello" },
                { "de-DE", "Hallo" }
            });
        }
    }
}
