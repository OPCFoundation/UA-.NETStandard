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
    }
}
