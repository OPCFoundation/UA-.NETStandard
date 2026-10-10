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
 *
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

using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Wot;

namespace Opc.Ua.WotCon.Tests.Hosting
{
    [TestFixture]
    public sealed class WotProjectionFormProviderHostingTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task HostFormProviderRegisteredInDiWinsOverRegistryOptions(bool providerFirst)
        {
            var registered = new HostFormProvider("https://host.test/runtime/registered");
            var fallback = new HostFormProvider("https://host.test/runtime/fallback");
            var services = new ServiceCollection();
            if (providerFirst)
            {
                services.AddSingleton<IWotProjectionFormProvider>(registered);
            }
            services.AddOpcUa().AddWotRegistryServer(configuration => configuration.ProjectionFormProvider = fallback);
            if (!providerFirst)
            {
                services.AddSingleton<IWotProjectionFormProvider>(registered);
            }
            using ServiceProvider provider = services.BuildServiceProvider();
            WotNodeSetConverterOptions options = provider.GetRequiredService<WotNodeSetConverterOptions>();

            Assert.That(options.ProjectionFormProvider, Is.SameAs(registered));
            await AssertResolvedFormsAsync(options, "https://host.test/runtime/registered").ConfigureAwait(false);

            Assert.That(registered.Calls, Is.EqualTo(1));
            Assert.That(fallback.Calls, Is.Zero);
        }

        [Test]
        public async Task HostFormProviderRegistryOptionsReachDirectResolverWithoutAServiceRegistration()
        {
            var supplied = new HostFormProvider("https://host.test/runtime/options");
            var services = new ServiceCollection();
            services.AddOpcUa().AddWotRegistryServer(configuration => configuration.ProjectionFormProvider = supplied);
            using ServiceProvider provider = services.BuildServiceProvider();
            WotNodeSetConverterOptions options = provider.GetRequiredService<WotNodeSetConverterOptions>();

            Assert.That(provider.GetService<IWotProjectionFormProvider>(), Is.Null);
            Assert.That(options.ProjectionFormProvider, Is.SameAs(supplied));
            await AssertResolvedFormsAsync(options, "https://host.test/runtime/options").ConfigureAwait(false);

            Assert.That(supplied.Calls, Is.EqualTo(1));
        }

        [Test]
        public async Task HostFormProviderExplicitConverterOptionsKeepTheirDirectConstructionFallback()
        {
            var supplied = new HostFormProvider("https://host.test/runtime/direct");
            var other = new HostFormProvider("https://host.test/runtime/not-selected");
            var options = new WotNodeSetConverterOptions { ProjectionFormProvider = supplied };
            var services = new ServiceCollection();
            services.AddSingleton(options);
            services.AddSingleton<IWotProjectionFormProvider>(other);
            services.AddOpcUa().AddWotRegistryServer(configuration => configuration.ProjectionFormProvider = other);
            using ServiceProvider provider = services.BuildServiceProvider();

            Assert.That(provider.GetRequiredService<WotNodeSetConverterOptions>(), Is.SameAs(options));
            await AssertResolvedFormsAsync(options, "https://host.test/runtime/direct").ConfigureAwait(false);

            Assert.That(supplied.Calls, Is.EqualTo(1));
            Assert.That(other.Calls, Is.Zero);
        }

        private static async Task AssertResolvedFormsAsync(WotNodeSetConverterOptions options, string expectedHref)
        {
            using var plan = WotDocument.Parse(Encoding.UTF8.GetBytes(Plan));
            var resolver = new WotProjectionResolver(new SourceResolver(), options);

            WotConversionResult<WotDocument> result = await resolver.ResolveAsync(plan).ConfigureAwait(false);
            Assert.That(result.Success, Is.True, string.Join("; ", result.Diagnostics));
            using WotDocument view = result.Value ??
                throw new AssertionException("A successful host-form result must contain a document.");

            Assert.That(view.Kind, Is.EqualTo(WotDocumentKind.ThingDescription));
            JsonElement value = view.Properties["Value"];
            Assert.That(value.GetProperty("forms")[0].GetProperty("href").GetString(), Is.EqualTo(expectedHref));
            Assert.That(value.GetProperty("minimum").GetInt32(), Is.EqualTo(10));
            Assert.That(value.GetProperty("maximum").GetInt32(), Is.EqualTo(20));
            Assert.That(value.GetProperty("uav:resolvedFrom").GetString(),
                Is.EqualTo("https://source.test/model.json#/properties/Value"));
            Assert.That(value.GetProperty("forms")[0].GetProperty("security").EnumerateArray()
                .Select(scheme => scheme.GetString()), Is.EqualTo(s_hostSecurity));
        }

        private sealed class HostFormProvider(string href) : IWotProjectionFormProvider
        {
            public int Calls { get; private set; }

            public ValueTask<ArrayOf<JsonElement>> GetFormsAsync(
                WotProjectionFormContext context, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Calls++;
                Assert.That(context.ProjectionDocument.Id, Is.EqualTo("https://host.test/plan.json"));
                Assert.That(context.SourceLocation, Is.EqualTo("https://source.test/model.json"));
                return new ValueTask<ArrayOf<JsonElement>>([CreateForm(href)]);
            }

            private static JsonElement CreateForm(string href)
            {
                using JsonDocument document = JsonDocument.Parse(new JsonObject { ["href"] = href }.ToJsonString());
                return document.RootElement.Clone();
            }
        }

        private sealed class SourceResolver : IWotThingResolver
        {
            public ValueTask<WotResolverResult> ResolveThingAsync(
                string href, WotResolutionContext context, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Assert.That(href, Is.EqualTo("https://source.test/model.json"));
                return new ValueTask<WotResolverResult>(WotResolverResult.FromBytes(Encoding.UTF8.GetBytes(Source)));
            }
        }

        private const string Plan = /*lang=json,strict*/ """
            {
              "@context":"https://www.w3.org/2022/wot/td/v1.1",
              "@type":"uav:projection","uav:projectionKind":"ThingDescription",
              "id":"https://host.test/plan.json","base":"https://host.test/runtime/","title":"Host",
              "uav:scenario":"urn:scenario:host-forms",
              "security":"host","securityDefinitions":{"host":{"scheme":"basic"}},
              "uav:projects":[
                {
                  "uav:sourceName":"source","href":"https://source.test/model.json",
                  "type":"application/td+json","uav:selectAll":true,"uav:routing":"projection"
                }
              ]
            }
            """;
        private const string Source = /*lang=json,strict*/ """
            {
              "@context":"https://www.w3.org/2022/wot/td/v1.1",
              "@type":"Thing","id":"https://source.test/model.json","title":"Source",
              "security":"none","securityDefinitions":{"none":{"scheme":"nosec"}},
              "properties":{
                "Value":{
                  "type":"integer","minimum":10,"maximum":20,
                  "forms":[{"href":"https://source.test/runtime/value"}]
                }
              }
            }
            """;
        private static readonly string[] s_hostSecurity = ["q:p:aG9zdA"];
    }
}
