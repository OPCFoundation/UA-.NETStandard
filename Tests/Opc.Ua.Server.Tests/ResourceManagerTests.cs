using System.Collections.Generic;
using System.Xml;
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
        public void TranslateServiceResultNullReturnsNull()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var appConfig = new ApplicationConfiguration(telemetry);
            using var resourceManager = new ResourceManager(appConfig);

            ServiceResult result = resourceManager.Translate(["en-US"], (ServiceResult)null);

            Assert.That(result, Is.Null);
        }

        [Test]
        public void TranslateServiceResultWithSymbolicIdTranslates()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var appConfig = new ApplicationConfiguration(telemetry);
            using var resourceManager = new ResourceManager(appConfig);
            var error = new ServiceResult(new StatusCode(StatusCodes.BadNodeIdInvalid.Code, "BadNodeIdInvalid"));

            ServiceResult translated = resourceManager.Translate(["en-US"], error);

            Assert.That(translated, Is.Not.Null);
            Assert.That(translated.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public void TranslateServiceResultWithLocalizedTextNoPreferredLocalesReturnsOriginal()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var appConfig = new ApplicationConfiguration(telemetry);
            using var resourceManager = new ResourceManager(appConfig);
            var localizedText = new LocalizedText("en-US", "Node ID is invalid");
            var error = new ServiceResult(StatusCodes.BadNodeIdInvalid, localizedText);

            ServiceResult translated = resourceManager.Translate(default, error);

            Assert.That(translated, Is.EqualTo(error));
        }

        [Test]
        public void TranslateServiceResultWithLocalizedTextAndPreferredLocalesTranslates()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var appConfig = new ApplicationConfiguration(telemetry);
            using var resourceManager = new ResourceManager(appConfig);
            var localizedText = new LocalizedText("en-US", "Node ID is invalid");
            var error = new ServiceResult(StatusCodes.BadNodeIdInvalid, localizedText);

            ServiceResult translated = resourceManager.Translate(["en-US"], error);

            Assert.That(translated, Is.Not.Null);
            Assert.That(translated.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public void TranslateServiceResultWithEmptyLocalizedTextUsesStatusCode()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var appConfig = new ApplicationConfiguration(telemetry);
            using var resourceManager = new ResourceManager(appConfig);
            var error = new ServiceResult(StatusCodes.BadNodeIdInvalid);

            ServiceResult translated = resourceManager.Translate(["en-US"], error);

            Assert.That(translated, Is.Not.Null);
            Assert.That(translated.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
        }

        [Test]
        public void GetAvailableLocalesReturnsEmptyWhenNoTranslationsAdded()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var appConfig = new ApplicationConfiguration(telemetry);
            using var resourceManager = new ResourceManager(appConfig);

            string[] locales = resourceManager.GetAvailableLocales();

            Assert.That(locales, Is.Empty);
        }

        [Test]
        public void GetAvailableLocalesReturnsAddedLocales()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();

            var appConfig = new ApplicationConfiguration(telemetry);
            using var resourceManager = new ResourceManager(appConfig);
            resourceManager.Add("key1", "en-US", "Hello");
            resourceManager.Add("key1", "de-DE", "Hallo");

            string[] locales = resourceManager.GetAvailableLocales();

            Assert.That(locales, Has.Length.EqualTo(2));
            Assert.That(locales, Does.Contain("en-US"));
            Assert.That(locales, Does.Contain("de-DE"));
        }

        [Test]
        public void AddThrowsWhenKeyNull()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appConfig = new ApplicationConfiguration(telemetry);
            using var resourceManager = new ResourceManager(appConfig);

            Assert.That(() => resourceManager.Add((string)null!, "en-US", "text"), Throws.ArgumentNullException);
        }

        [Test]
        public void AddThrowsWhenLocaleNull()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appConfig = new ApplicationConfiguration(telemetry);
            using var resourceManager = new ResourceManager(appConfig);

            Assert.That(() => resourceManager.Add("key", null!, "text"), Throws.ArgumentNullException);
        }

        [Test]
        public void AddThrowsWhenTextNull()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appConfig = new ApplicationConfiguration(telemetry);
            using var resourceManager = new ResourceManager(appConfig);

            Assert.That(() => resourceManager.Add("key", "en-US", null!), Throws.ArgumentNullException);
        }

        [Test]
        public void AddThrowsWhenNeutralLocale()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appConfig = new ApplicationConfiguration(telemetry);
            using var resourceManager = new ResourceManager(appConfig);

            Assert.That(() => resourceManager.Add("key", "en", "text"), Throws.ArgumentException);
        }

        [Test]
        public void AddDictionaryThrowsWhenLocaleNull()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appConfig = new ApplicationConfiguration(telemetry);
            using var resourceManager = new ResourceManager(appConfig);

            Assert.That(
                () => resourceManager.Add(null!, new Dictionary<string, string>()),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddDictionaryThrowsWhenTranslationsNull()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appConfig = new ApplicationConfiguration(telemetry);
            using var resourceManager = new ResourceManager(appConfig);

            Assert.That(
                () => resourceManager.Add("en-US", (IDictionary<string, string>)null!),
                Throws.ArgumentNullException);
        }

        [Test]
        public void AddDictionaryThrowsWhenNeutralLocale()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appConfig = new ApplicationConfiguration(telemetry);
            using var resourceManager = new ResourceManager(appConfig);

            Assert.That(
                () => resourceManager.Add("de", new Dictionary<string, string> { { "k", "v" } }),
                Throws.ArgumentException);
        }

        [Test]
        public void AddDictionaryAddsMultipleTranslations()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appConfig = new ApplicationConfiguration(telemetry);
            using var resourceManager = new ResourceManager(appConfig);

            var translations = new Dictionary<string, string>
            {
                { "hello", "Hallo" },
                { "goodbye", "Tschüss" }
            };
            resourceManager.Add("de-DE", translations);

            LocalizedText result = resourceManager.Translate(["de-DE"], "hello", null);
            Assert.That(result.Text, Is.EqualTo("Hallo"));

            result = resourceManager.Translate(["de-DE"], "goodbye", null);
            Assert.That(result.Text, Is.EqualTo("Tschüss"));
        }

        [Test]
        public void AddStatusCodeAddsTranslation()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appConfig = new ApplicationConfiguration(telemetry);
            using var resourceManager = new ResourceManager(appConfig);

            resourceManager.Add(StatusCodes.BadNodeIdInvalid, "en-US", "The node id is invalid");

            // Translate a ServiceResult and verify the status code round-trips
            var error = new ServiceResult(StatusCodes.BadNodeIdInvalid);
            ServiceResult translated = resourceManager.Translate(["en-US"], error);

            Assert.That(translated, Is.Not.Null);
            Assert.That(translated.StatusCode, Is.EqualTo(StatusCodes.BadNodeIdInvalid));
            // The translation is applied; the localized text should not be empty
            Assert.That(translated.LocalizedText.IsNullOrEmpty, Is.False);
        }

        [Test]
        public void AddSymbolicIdAddsTranslation()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appConfig = new ApplicationConfiguration(telemetry);
            using var resourceManager = new ResourceManager(appConfig);

            var symbolicId = new XmlQualifiedName("MyError", "urn:test");
            resourceManager.Add(symbolicId, "en-US", "My custom error");

            var error = new ServiceResult(StatusCodes.Bad, new XmlQualifiedName("MyError", "urn:test"), default(LocalizedText));
            ServiceResult translated = resourceManager.Translate(["en-US"], error);

            Assert.That(translated.LocalizedText.Text, Is.EqualTo("My custom error"));
        }

        [Test]
        public void LoadDefaultTextAddsStatusCodeTranslations()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appConfig = new ApplicationConfiguration(telemetry);
            using var resourceManager = new ResourceManager(appConfig);

            resourceManager.LoadDefaultText();

            string[] locales = resourceManager.GetAvailableLocales();
            Assert.That(locales, Does.Contain("en-US"));
        }

        [Test]
        public void TranslateLanguageFallbackMatchesRegionVariant()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appConfig = new ApplicationConfiguration(telemetry);
            using var resourceManager = new ResourceManager(appConfig);
            resourceManager.Add("greeting", "en-US", "Hello");

            // Request en-GB, should fall back to en-US
            LocalizedText result = resourceManager.Translate(["en-GB"], "greeting", null);

            Assert.That(result.Text, Is.EqualTo("Hello"));
            Assert.That(result.Locale, Is.EqualTo("en-US"));
        }

        [Test]
        public void TranslateReturnsDefaultWhenNoTranslationFound()
        {
            ITelemetryContext telemetry = NUnitTelemetryContext.Create();
            var appConfig = new ApplicationConfiguration(telemetry);
            using var resourceManager = new ResourceManager(appConfig);

            var defaultText = new LocalizedText("nonexistent_key", "en-US", "Fallback");

            LocalizedText result = resourceManager.Translate(["ja-JP"], defaultText);

            Assert.That(result.Text, Is.EqualTo("Fallback"));
        }

        [Test]
        public void ConstructorThrowsWhenConfigurationNull()
        {
            Assert.That(() => new ResourceManager(null!), Throws.ArgumentNullException);
        }
    }
}

