using System.Collections.Generic;
using Moq;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// Test ResourceManager
    /// </summary>
    [TestFixture, Category("ResourceManager")]
    [Parallelizable]
    [SetCulture("en-us"), SetUICulture("en-us")]
    public class ResourceManagerTests
    {
        [Test]
        public void TranslateSingleLanguageExactMatch()
        {
            // Arrange
            var resourceManager = new ResourceManager(
                new Mock<IServerInternal>().Object,
                new Mock<ApplicationConfiguration>().Object
            );
            var defaultText = new LocalizedText("en-US", "Hello");

            //Act
            LocalizedText resultText = resourceManager.Translate(["en-US", "de-DE"], defaultText);

            // Assert
            Assert.AreEqual(defaultText, resultText);
        }

        [Test]
        public void TranslateSingleLanguageWithInfoExactMatch()
        {
            // Arrange
            var resourceManager = new ResourceManager(
                new Mock<IServerInternal>().Object,
                new Mock<ApplicationConfiguration>().Object
            );
            var defaultText = new LocalizedText("greeting", "en-US", "Hello");

            //Act
            LocalizedText resultText = resourceManager.Translate(["en-US", "de-DE"], defaultText);

            // Assert
            Assert.AreEqual(defaultText, resultText);
        }

        [Test]
        public void TranslateSingleLanguageWithArguments()
        {
            // Arrange
            var resourceManager = new ResourceManager(
                new Mock<IServerInternal>().Object,
                new Mock<ApplicationConfiguration>().Object
            );
            resourceManager.Add("greeting", "en-US", "Hello {0}");

            //Act
            LocalizedText resultText = resourceManager.Translate(["en-US", "de-DE"], "greeting", "Hello {0}", "User");

            // Assert
            Assert.AreEqual("Hello User", resultText.Text);
            Assert.AreEqual("en-US", resultText.Locale);
        }

        [Test]
        public void TranslateMultiLanguageExactMatchMulRequested()
        {
            // Arrange
            var resourceManager = new ResourceManager(
                new Mock<IServerInternal>().Object,
                new Mock<ApplicationConfiguration>().Object
            );
            var translations = new Dictionary<string, string> { { "en-US", "Hello" }, { "de-DE", "Hallo" } };
            var defaultText = new LocalizedText("greeting", translations);

            //Act
            LocalizedText resultText = resourceManager.Translate(["mul", "de-DE", "en-US"], defaultText);

            // Assert
            Assert.AreEqual(defaultText, resultText);
        }

        [Test]
        public void TranslateMultiLanguageMulRequested()
        {
            // Arrange
            var resourceManager = new ResourceManager(
                new Mock<IServerInternal>().Object,
                new Mock<ApplicationConfiguration>().Object
            );
            var translations = new Dictionary<string, string>
            {
                { "en-US", "Hello" },
                { "de-DE", "Hallo" },
                { "fr-FR", "Bonjour" },
            };
            var defaultText = new LocalizedText("greeting", translations);

            //Act
            LocalizedText resultText = resourceManager.Translate(["mul", "de-DE", "en-US"], defaultText);

            // Assert
            Assert.AreEqual( /*lang=json,strict*/
                "{\"t\":[[\"en-US\",\"Hello\"],[\"de-DE\",\"Hallo\"]]}",
                resultText.Text
            );
            Assert.AreEqual("mul", resultText.Locale);
        }

        [Test]
        public void TranslateSingleLanguageMulRequested()
        {
            // Arrange
            var resourceManager = new ResourceManager(
                new Mock<IServerInternal>().Object,
                new Mock<ApplicationConfiguration>().Object
            );
            var defaultText = new LocalizedText("greeting", "en-US", "Hello");

            //Act
            LocalizedText resultText = resourceManager.Translate(["mul", "de-DE", "en-US"], defaultText);

            // Assert
            Assert.AreEqual(defaultText, resultText);
        }

        [Test]
        public void TranslateNoLocalesRequestedDefaultTextReturned()
        {
            // Arrange
            var resourceManager = new ResourceManager(
                new Mock<IServerInternal>().Object,
                new Mock<ApplicationConfiguration>().Object
            );
            var defaultText = new LocalizedText("greeting", "en-US", "Hello");

            //Act
            LocalizedText resultText = resourceManager.Translate(null, defaultText);

            // Assert
            Assert.AreEqual(defaultText, resultText);
        }

        [Test]
        public void TranslateSingleLanguageMulRequestedWithTranslation()
        {
            // Arrange
            var resourceManager = new ResourceManager(
                new Mock<IServerInternal>().Object,
                new Mock<ApplicationConfiguration>().Object
            );
            var defaultText = new LocalizedText("greeting", "en-US", "Hello");
            resourceManager.Add("greeting", "de-DE", "Hallo");
            resourceManager.Add("greeting", "fr-FR", "Bonjour");

            //Act
            LocalizedText resultText = resourceManager.Translate(["mul", "de-DE", "en-US"], defaultText);

            // Assert
            Assert.AreEqual( /*lang=json,strict*/
                "{\"t\":[[\"en-US\",\"Hello\"],[\"de-DE\",\"Hallo\"]]}",
                resultText.Text
            );
            Assert.AreEqual("mul", resultText.Locale);
        }

        [Test]
        public void TranslateKeyMulRequestedWithTranslation()
        {
            // Arrange
            var resourceManager = new ResourceManager(
                new Mock<IServerInternal>().Object,
                new Mock<ApplicationConfiguration>().Object
            );
            resourceManager.Add("greeting", "de-DE", "Hallo");
            resourceManager.Add("greeting", "en-US", "Hello");

            //Act
            LocalizedText resultText = resourceManager.Translate(["mul", "de-DE", "en-US"], "greeting", null);

            // Assert
            Assert.AreEqual( /*lang=json,strict*/
                "{\"t\":[[\"de-DE\",\"Hallo\"],[\"en-US\",\"Hello\"]]}",
                resultText.Text
            );
            Assert.AreEqual("mul", resultText.Locale);
        }

        [Test]
        public void TranslateKeyMulRequestedAllLanguagesWithTranslation()
        {
            // Arrange
            var resourceManager = new ResourceManager(
                new Mock<IServerInternal>().Object,
                new Mock<ApplicationConfiguration>().Object
            );
            resourceManager.Add("greeting", "de-DE", "Hallo");
            resourceManager.Add("greeting", "en-US", "Hello");

            //Act
            LocalizedText resultText = resourceManager.Translate(["mul"], "greeting", null);

            // Assert
            Assert.AreEqual( /*lang=json,strict*/
                "{\"t\":[[\"de-DE\",\"Hallo\"],[\"en-US\",\"Hello\"]]}",
                resultText.Text
            );
            Assert.AreEqual("mul", resultText.Locale);
        }

        [Test]
        public void TranslateKeyMulRequestedTranslationWithParameters()
        {
            // Arrange
            var resourceManager = new ResourceManager(
                new Mock<IServerInternal>().Object,
                new Mock<ApplicationConfiguration>().Object
            );
            resourceManager.Add("greeting", "de-DE", "Hallo {0}");
            resourceManager.Add("greeting", "en-US", "Hello {0}");

            //Act
            LocalizedText resultText = resourceManager.Translate(["mul"], "greeting", null, "User");

            // Assert
            Assert.AreEqual( /*lang=json,strict*/
                "{\"t\":[[\"de-DE\",\"Hallo User\"],[\"en-US\",\"Hello User\"]]}",
                resultText.Text
            );
            Assert.AreEqual("mul", resultText.Locale);
        }
    }
}
