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
    }
}
