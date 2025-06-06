using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Core.Tests.Types.BuiltIn
{
    /// <summary>
    /// Tests for the LocalizedText Class.
    /// </summary>
    [TestFixture, Category("BuiltIn")]
    [SetCulture("en-us"), SetUICulture("en-us")]
    [Parallelizable]
    public class LocalizedTextTests
    {
        [Test]
        public void MultiLanguageDictionaryCreatesCorretMulLocale()
        {
            // Arrange
            var translations = new Dictionary<string, string>
            {
                { "en-US", "Hello" },
                { "de-DE", "Hallo" }
            };

            // Act
            var localizedText = new LocalizedText(translations);

            // Assert
            Assert.IsTrue(localizedText.IsMultiLanguage, "Should be mul locale");
            Assert.AreEqual("mul", localizedText.Locale, "Locale should be 'mul'");
            Assert.IsNotNull(localizedText.Text, "Text should not be null");
            Assert.AreEqual(2, localizedText.Translations.Count, "Translations should have 2 entries");
            Assert.AreEqual("Hello", localizedText.Translations["en-US"]);
            Assert.AreEqual("Hallo", localizedText.Translations["de-DE"]);

            const string expectedJson = "{\"t\":[[\"en-US\",\"Hello\"],[\"de-DE\",\"Hallo\"]]}";
            Assert.AreEqual(expectedJson, localizedText.Text);
        }

        [Test]
        public void EmptyDictionaryCreatesNullText()
        {
            // Arrange
            var translations = new Dictionary<string, string>();

            // Act
            var localizedText = new LocalizedText(translations);

            // Assert
            Assert.IsFalse(localizedText.IsMultiLanguage, "Should not be mul locale");
            Assert.IsNull(localizedText.Translations, "Translations should be null for empty dictionary");
            Assert.IsNull(localizedText.Text, "Text should be null for empty dictionary");
            Assert.IsNull(localizedText.Locale, "Locale should be null for empty dictionary");
        }

        [Test]
        public void SingleLocaleDictionaryCreatesLocaleAndTextDirectly()
        {
            // Arrange
            var translations = new Dictionary<string, string>
            {
                { "fr-FR", "Bonjour" }
            };

            // Act
            var localizedText = new LocalizedText(translations);

            // Assert
            Assert.IsFalse(localizedText.IsMultiLanguage, "Should not be mul locale for single entry");
            Assert.AreEqual("fr-FR", localizedText.Locale, "Locale should be 'fr-FR'");
            Assert.AreEqual("Bonjour", localizedText.Text, "Text should be 'Bonjour'");
            Assert.AreEqual(1, localizedText.Translations.Count, "Translations should have 1 entry");
            Assert.AreEqual("Bonjour", localizedText.Translations["fr-FR"]);
        }

        [Test]
        public void LocalizedText_MulLocale_WithThreeTranslations_ParsesCorrectly()
        {
            // Arrange
            const string mulLocale = "mul";
            const string jsonText = "{\"t\":[[\"en-US\",\"Hello\"],[\"de-DE\",\"Hallo\"],[\"fr-FR\",\"Bonjour\"]]}";

            // Act
            var localizedText = new LocalizedText(mulLocale, jsonText);

            // Assert
            Assert.IsTrue(localizedText.IsMultiLanguage, "Should be mul locale");
            Assert.AreEqual("mul", localizedText.Locale, "Locale should be 'mul'");
            Assert.AreEqual(jsonText, localizedText.Text, "Text should match the input JSON exactly");
            Assert.IsNotNull(localizedText.Translations, "Translations should not be null");
            Assert.AreEqual(3, localizedText.Translations.Count, "Translations should have 3 entries");
            Assert.AreEqual("Hello", localizedText.Translations["en-US"]);
            Assert.AreEqual("Hallo", localizedText.Translations["de-DE"]);
            Assert.AreEqual("Bonjour", localizedText.Translations["fr-FR"]);
        }

        [Test]
        public void LocalizedText_MulLocale_ReturnsPreferredTranslations()
        {
            // Arrange
            const string mulLocale = "mul";
            const string jsonText = "{\"t\":[[\"en-US\",\"Hello\"],[\"de-DE\",\"Hallo\"],[\"fr-FR\",\"Bonjour\"]]}";

            // Act
            var localizedText = new LocalizedText(mulLocale, jsonText);

            // Assert
            Assert.IsTrue(localizedText.IsMultiLanguage, "Should be mul locale");

            //found locale returned
            var singleUS = localizedText.FilterByPreferredLocales(new[] { "en-US", "de-DE" });
            Assert.AreEqual("en-US", singleUS.Locale, "Locale should be 'en-US'");
            Assert.AreEqual("Hello", singleUS.Text, "Text should be 'Hello'");

            //found locale returned
            var singleDE = localizedText.FilterByPreferredLocales(new[] { "en-GB", "de-DE" });
            Assert.AreEqual("de-DE", singleDE.Locale, "Locale should be 'de-DE'");
            Assert.AreEqual("Hallo", singleDE.Text, "Text should be 'Hallo'");

            //first locale returned
            var singleFR = localizedText.FilterByPreferredLocales(new[] { "en-GB" });
            Assert.AreEqual("en-US", singleFR.Locale, "Locale should be 'en-US'");
            Assert.AreEqual("Hello", singleFR.Text, "Text should be 'Hello'");

            // Default locale returned
            var mulGB = localizedText.FilterByPreferredLocales(new[] { "mul", "en-GB" });
            Assert.AreEqual("en-US", mulGB.Locale, "Locale should be 'en-US'");
            Assert.AreEqual("Hello", mulGB.Text, "Text should be 'Hello'");

            // All locales returned
            var mul = localizedText.FilterByPreferredLocales(new[] { "mul" });
            Assert.IsTrue(mul.IsMultiLanguage, "Should be mul locale");
            Assert.AreEqual(3, mul.Translations.Count, "Translations should have 3 entries");
            Assert.AreEqual("Hello", mul.Translations["en-US"]);
            Assert.AreEqual("Hallo", mul.Translations["de-DE"]);
            Assert.AreEqual("Bonjour", mul.Translations["fr-FR"]);

            //matching locale returned
            var mulFr = localizedText.FilterByPreferredLocales(new[] { "mul", "fr-FR" });
            Assert.AreEqual("fr-FR", mulFr.Locale, "Locale should be 'fr-FR'");
            Assert.AreEqual("Bonjour", mulFr.Text, "Text should be 'Bonjour'");
        }

        [Test]
        public void LocalizedText_SingleLocale_ReturnsPreferredTranslations()
        {
            // Act
            var localizedText = new LocalizedText("de-DE", "Hallo");

            // Assert
            Assert.IsFalse(localizedText.IsMultiLanguage, "Should be mul locale");

            //found locale returned
            var singleDE = localizedText.FilterByPreferredLocales(new[] { "en-US", "de-DE" });
            Assert.AreEqual("de-DE", singleDE.Locale, "Locale should be 'de-DE'");
            Assert.AreEqual("Hallo", singleDE.Text, "Text should be 'Hallo'");

            //nonexisting locale, default locale returned
            var singleUS = localizedText.FilterByPreferredLocales(new[] { "en-GB", "en-US" });
            Assert.AreEqual("de-DE", singleUS.Locale, "Locale should be 'de-DE'");
            Assert.AreEqual("Hallo", singleUS.Text, "Text should be 'Hallo'");

            // Default locale returned
            var mulGB = localizedText.FilterByPreferredLocales(new[] { "mul", "en-GB" });
            Assert.AreEqual("de-DE", mulGB.Locale, "Locale should be 'de-DE'");
            Assert.AreEqual("Hallo", mulGB.Text, "Text should be 'Hallo'");
        }

        [Test]
        public void LocalizedText_MulLocale_InvalidJson()
        {
            // Arrange
            const string mulLocale = "mul";
            const string jsonText = "{\"t\":[[\"en-US\"],[\"de-DE\",\"Hallo\", \"fr-FR\"],[]]}";

            // Act
            var localizedText = new LocalizedText(mulLocale, jsonText);

            // Assert
            Assert.IsTrue(localizedText.IsMultiLanguage, "Should be mul locale");
            Assert.AreEqual(jsonText, localizedText.Text);
            Assert.IsNotNull(localizedText.Translations, "Translations should not be null");
        }

        [Test]
        public void LocalizedText_MulLocale_DeepCopy()
        {
            // Arrange
            const string mulLocale = "mul";
            const string jsonText = "{\"t\":[[\"en-US\",\"Hello\"],[\"de-DE\",\"Hallo\"],[\"fr-FR\",\"Bonjour\"]]}";

            // Act
            var localizedText = new LocalizedText(mulLocale, jsonText);
            var deepCopy = new LocalizedText(localizedText);

            //Assert
            Assert.IsTrue(localizedText.IsMultiLanguage, "Should be mul locale");
            Assert.IsTrue(deepCopy.IsMultiLanguage, "Should be mul locale");
            Assert.AreEqual(localizedText.Locale, deepCopy.Locale, "Locale should be the same");
            Assert.AreEqual(localizedText.Text, deepCopy.Text, "Text should be the same");
            Assert.AreEqual(localizedText.Translations.Count, deepCopy.Translations.Count, "Translations count should be the same");
            Assert.AreEqual(localizedText.Translations["en-US"], deepCopy.Translations["en-US"], "English translation should be the same");
            Assert.AreEqual(localizedText.Translations["de-DE"], deepCopy.Translations["de-DE"], "German translation should be the same");
            Assert.AreEqual(localizedText.Translations["fr-FR"], deepCopy.Translations["fr-FR"], "French translation should be the same");
        }
    }
}
