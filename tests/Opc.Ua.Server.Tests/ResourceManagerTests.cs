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
        public void TranslateUsesRegisteredTextAndLocaleInsteadOfFallback()
        {
            var configuration = new ApplicationConfiguration(NUnitTelemetryContext.Create());
            using var resources = new ResourceManager(configuration);
            resources.Add("greeting", "de-DE", "Hallo {0}");

            LocalizedText translated = resources.Translate(["de-DE"], "greeting", "Hello {0}", "User");

            Assert.That(translated.Text, Is.EqualTo("Hallo User"));
            Assert.That(translated.Locale, Is.EqualTo("de-DE"));
            Assert.That(translated.TranslationInfo.Key, Is.EqualTo("greeting"));
            Assert.That(translated.TranslationInfo.Text, Is.EqualTo("Hallo {0}"));
        }

        [TestCase(null)]
        [TestCase(Opc.Ua.Namespaces.OpcUa)]
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
        [TestCase("CustomTimeout", Opc.Ua.Namespaces.OpcUa)]
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
    }
}
