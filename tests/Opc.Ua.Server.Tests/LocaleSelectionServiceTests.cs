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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Server.TestFramework;
using Quickstarts.ReferenceServer;

namespace Opc.Ua.Server.Tests
{
    /// <summary>
    /// The Read and Browse services return the translation of a localized text
    /// the session prefers (OPC 10000-4 5.4, OPC 10000-3 5.2.5 and 8.5).
    /// </summary>
    [TestFixture]
    [Category("Server")]
    [SetCulture("en-us")]
    [SetUICulture("en-us")]
    [NonParallelizable]
    public class LocaleSelectionServiceTests
    {
        private const string kReferenceServerNamespace = "http://opcfoundation.org/Quickstarts/ReferenceServer";

        private ServerFixture<ReferenceServer> m_fixture;
        private ReferenceServer m_server;
        private NodeId m_folderId;
        private NodeId m_scalarId;
        private NodeId m_arrayId;
        private BaseVariableState m_scalar;
        private BaseVariableState m_array;
        private ReferenceTypeState m_hasComponent;
        private LocalizedText m_scalarDisplayName;
        private LocalizedText m_scalarDescription;
        private Variant m_scalarValue;
        private Variant m_arrayValue;
        private LocalizedText m_hasComponentInverseName;

        [OneTimeSetUp]
        public async Task OneTimeSetUpAsync()
        {
            m_fixture = new ServerFixture<ReferenceServer>(t => new ReferenceServer(t))
            {
                AutoAccept = true
            };
            m_server = await m_fixture.StartAsync().ConfigureAwait(false);

            IServerInternal server = m_server.CurrentInstance;
            ushort ns = (ushort)server.NamespaceUris.GetIndex(kReferenceServerNamespace);
            m_folderId = new NodeId("Scalar_Static", ns);
            m_scalarId = new NodeId("Scalar_Static_LocalizedText", ns);
            m_arrayId = new NodeId("Scalar_Static_Arrays_LocalizedText", ns);

            m_scalar = (BaseVariableState)(await server.NodeManager
                .FindNodeInAddressSpaceAsync(m_scalarId).ConfigureAwait(false))!;
            m_array = (BaseVariableState)(await server.NodeManager
                .FindNodeInAddressSpaceAsync(m_arrayId).ConfigureAwait(false))!;
            m_hasComponent = (ReferenceTypeState)(await server.NodeManager
                .FindNodeInAddressSpaceAsync(ReferenceTypeIds.HasComponent).ConfigureAwait(false))!;
            Assert.That(m_scalar, Is.Not.Null);
            Assert.That(m_array, Is.Not.Null);
            Assert.That(m_hasComponent, Is.Not.Null);

            m_scalarDisplayName = m_scalar.DisplayName;
            m_scalarDescription = m_scalar.Description;
            m_scalarValue = m_scalar.Value;
            m_arrayValue = m_array.Value;
            m_hasComponentInverseName = m_hasComponent.InverseName;

            m_scalar.DisplayName = CreateText("Text", "Text");
            m_scalar.Description = CreateText("Description", "Beschreibung");
            m_scalar.Value = Variant.From(CreateText("Hello", "Hallo"));
            m_array.Value = Variant.From(new[] { CreateText("One", "Eins"), CreateText("Two", "Zwei") }.ToArrayOf());
            m_hasComponent.InverseName = CreateText("ComponentOf", "KomponenteVon");
        }

        [OneTimeTearDown]
        public async Task OneTimeTearDownAsync()
        {
            if (m_scalar != null)
            {
                m_scalar.DisplayName = m_scalarDisplayName;
                m_scalar.Description = m_scalarDescription;
                m_scalar.Value = m_scalarValue;
            }
            if (m_array != null)
            {
                m_array.Value = m_arrayValue;
            }
            if (m_hasComponent != null)
            {
                m_hasComponent.InverseName = m_hasComponentInverseName;
            }
            if (m_fixture != null)
            {
                await m_fixture.StopAsync().ConfigureAwait(false);
            }
        }

        [Test]
        public async Task ReadReturnsGermanTranslationForGermanSessionAsync()
        {
            ArrayOf<DataValue> values = await ReadAsync(["de-DE", "en-US"]).ConfigureAwait(false);

            AssertText(values[0], "de-DE", "Text");
            AssertText(values[1], "de-DE", "Beschreibung");
            AssertText(values[2], "de-DE", "Hallo");
            AssertText(values[3], "de-DE", "KomponenteVon");
            Assert.That(values[4].WrappedValue.TryGetValue(out ArrayOf<LocalizedText> texts), Is.True);
            Assert.That(texts.Count, Is.EqualTo(2));
            AssertText(texts[0], "de-DE", "Eins");
            AssertText(texts[1], "de-DE", "Zwei");
        }

        [Test]
        public async Task ReadReturnsEnglishTranslationForEnglishSessionAsync()
        {
            ArrayOf<DataValue> values = await ReadAsync(["en-US"]).ConfigureAwait(false);

            AssertText(values[1], "en-US", "Description");
            AssertText(values[2], "en-US", "Hello");
            AssertText(values[3], "en-US", "ComponentOf");
        }

        [Test]
        public async Task ReadSelectsSecondPreferenceAndMatchesCaseInsensitivelyAsync()
        {
            ArrayOf<DataValue> values = await ReadAsync(["fr-FR", "DE-de"]).ConfigureAwait(false);

            AssertText(values[2], "de-DE", "Hallo");
        }

        [Test]
        public async Task ReadReturnsDefaultTranslationForUnknownLocaleAsync()
        {
            ArrayOf<DataValue> values = await ReadAsync(["xx-XX"]).ConfigureAwait(false);

            AssertText(values[1], "en-US", "Description");
            AssertText(values[2], "en-US", "Hello");
        }

        [Test]
        public async Task ReadReturnsAllTranslationsForMulAsync()
        {
            ArrayOf<DataValue> values = await ReadAsync(["mul"]).ConfigureAwait(false);

            Assert.That(values[2].WrappedValue.TryGetValue(out LocalizedText text), Is.True);
            Assert.That(text.Locale, Is.EqualTo("mul"));
            // the mul text decodes into all translations (Part 3 8.5.2.2).
            var decoded = new LocalizedText(text.Locale, text.Text);
            Assert.That(decoded.Translations, Is.EquivalentTo(new Dictionary<string, string>
            {
                { "en-US", "Hello" },
                { "de-DE", "Hallo" }
            }));
        }

        [Test]
        public async Task ReadDoesNotChangeTheStoredValueAsync()
        {
            await ReadAsync(["de-DE"]).ConfigureAwait(false);

            Assert.That(m_scalar.Value.TryGetValue(out LocalizedText stored), Is.True);
            Assert.That(stored.Locale, Is.EqualTo("en-US"));
            Assert.That(stored.Translations, Has.Count.EqualTo(2));
            Assert.That(m_scalar.Description.Locale, Is.EqualTo("en-US"));
        }

        [Test]
        public async Task BrowseReturnsDisplayNameInSessionLocaleAsync()
        {
            m_scalar.DisplayName = CreateText("Localized text", "Lokalisierter Text");
            try
            {
                Assert.That(await BrowseDisplayNameAsync(["de-DE"]).ConfigureAwait(false),
                    Is.EqualTo(new LocalizedText("de-DE", "Lokalisierter Text")));
                Assert.That(await BrowseDisplayNameAsync(["en-US"]).ConfigureAwait(false),
                    Is.EqualTo(new LocalizedText("en-US", "Localized text")));
                Assert.That(await BrowseDisplayNameAsync(["xx-XX"]).ConfigureAwait(false),
                    Is.EqualTo(new LocalizedText("en-US", "Localized text")));

                LocalizedText mul = await BrowseDisplayNameAsync(["mul"]).ConfigureAwait(false);
                Assert.That(mul.Locale, Is.EqualTo("mul"));
                Assert.That(new LocalizedText(mul.Locale, mul.Text).Translations, Has.Count.EqualTo(2));
            }
            finally
            {
                m_scalar.DisplayName = CreateText("Text", "Text");
            }
        }

        private static LocalizedText CreateText(string english, string german)
        {
            return new LocalizedText(new Dictionary<string, string>
            {
                { "en-US", english },
                { "de-DE", german }
            });
        }

        private static void AssertText(DataValue value, string locale, string text)
        {
            Assert.That(StatusCode.IsGood(value.StatusCode), Is.True, value.StatusCode.ToString());
            Assert.That(value.WrappedValue.TryGetValue(out LocalizedText result), Is.True);
            AssertText(result, locale, text);
        }

        private static void AssertText(LocalizedText result, string locale, string text)
        {
            Assert.That(result.Locale, Is.EqualTo(locale));
            Assert.That(result.Text, Is.EqualTo(text));
        }

        private async Task<ArrayOf<DataValue>> ReadAsync(ArrayOf<string> localeIds)
        {
            (RequestHeader requestHeader, SecureChannelContext secureChannelContext) =
                await m_server.CreateAndActivateSessionAsync(
                    TestContext.CurrentContext.Test.Name,
                    localeIds: localeIds).ConfigureAwait(false);
            try
            {
                ReadResponse response = await m_server.ReadAsync(
                    secureChannelContext,
                    requestHeader,
                    0,
                    TimestampsToReturn.Neither,
                    [
                        new ReadValueId { NodeId = m_scalarId, AttributeId = Attributes.DisplayName },
                        new ReadValueId { NodeId = m_scalarId, AttributeId = Attributes.Description },
                        new ReadValueId { NodeId = m_scalarId, AttributeId = Attributes.Value },
                        new ReadValueId { NodeId = ReferenceTypeIds.HasComponent, AttributeId = Attributes.InverseName },
                        new ReadValueId { NodeId = m_arrayId, AttributeId = Attributes.Value }
                    ],
                    RequestLifetime.None).ConfigureAwait(false);
                return response.Results;
            }
            finally
            {
                await m_server.CloseSessionAsync(secureChannelContext, requestHeader, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }

        private async Task<LocalizedText> BrowseDisplayNameAsync(ArrayOf<string> localeIds)
        {
            (RequestHeader requestHeader, SecureChannelContext secureChannelContext) =
                await m_server.CreateAndActivateSessionAsync(
                    TestContext.CurrentContext.Test.Name,
                    localeIds: localeIds).ConfigureAwait(false);
            try
            {
                BrowseResponse response = await m_server.BrowseAsync(
                    secureChannelContext,
                    requestHeader,
                    null,
                    0,
                    [
                        new BrowseDescription
                        {
                            NodeId = m_folderId,
                            BrowseDirection = BrowseDirection.Forward,
                            ReferenceTypeId = ReferenceTypeIds.Organizes,
                            IncludeSubtypes = true,
                            ResultMask = (uint)BrowseResultMask.All
                        }
                    ],
                    RequestLifetime.None).ConfigureAwait(false);
                ReferenceDescription reference = response.Results[0].References.ToArray()!
                    .Single(r => (NodeId)r.NodeId == m_scalarId);
                return reference.DisplayName;
            }
            finally
            {
                await m_server.CloseSessionAsync(secureChannelContext, requestHeader, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
    }
}
