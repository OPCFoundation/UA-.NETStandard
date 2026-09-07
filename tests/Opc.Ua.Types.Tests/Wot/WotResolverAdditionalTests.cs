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

using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Wot;

namespace Opc.Ua.Types.Tests.Wot
{
    /// <summary>
    /// Additional tests for WotResolver covering NullWotResolver interface methods,
    /// WotResolverOptions validation, WotResolutionContext state properties,
    /// WotResolverResult factory methods, and resolution kind messaging.
    /// </summary>
    [TestFixture]
    [Category("WoT")]
    [Parallelizable]
    public class WotResolverAdditionalTests
    {
        [Test]
        public async Task NullResolverContextReturnsNotFound()
        {
            var context = new WotResolutionContext();
            WotResolverResult result = await NullWotResolver.Instance.ResolveContextAsync(
                "https://example.com/ctx.jsonld",
                context,
                CancellationToken.None);

            Assert.That(result.Found, Is.False);
        }

        [Test]
        public async Task NullResolverSchemaReturnsNotFound()
        {
            var context = new WotResolutionContext();
            WotResolverResult result = await NullWotResolver.Instance.ResolveSchemaAsync(
                "https://example.com/schema.json",
                context,
                CancellationToken.None);

            Assert.That(result.Found, Is.False);
        }

        [Test]
        public void NullResolverSharedInstanceIsNotNull()
        {
            Assert.That(NullWotResolver.Instance, Is.Not.Null);
        }

        [Test]
        public void NullResolverInstanceIsNullWotResolverType()
        {
            Assert.That(NullWotResolver.Instance, Is.InstanceOf<NullWotResolver>());
        }

        [Test]
        public void WotResolverOptionsDefaultsArePositive()
        {
            var options = new WotResolverOptions();

            Assert.That(options.MaxDepth, Is.GreaterThan(0));
            Assert.That(options.MaxDocuments, Is.GreaterThan(0));
            Assert.That(options.MaxDocumentBytes, Is.GreaterThan(0));
            Assert.That(options.MaxTotalBytes, Is.GreaterThan(0));
        }

        [Test]
        public void WotResolverOptionsValidateSucceedsWithDefaultValues()
        {
            var options = new WotResolverOptions();

            Assert.That(options.Validate, Throws.Nothing);
        }

        [Test]
        public void WotResolverOptionsValidateRejectsZeroMaxTotalBytes()
        {
            var options = new WotResolverOptions { MaxTotalBytes = 0 };

            Assert.That(
                options.Validate,
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void WotResolverOptionsValidateRejectsNegativeMaxDepth()
        {
            var options = new WotResolverOptions { MaxDepth = -1 };

            Assert.That(
                options.Validate,
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void ResolutionContextDepthIncreasesOnEnterDecreasesOnLeave()
        {
            var context = new WotResolutionContext();

            Assert.That(context.Depth, Is.Zero);
            context.TryEnter(WotResolutionKind.Thing, "urn:a", out _);
            Assert.That(context.Depth, Is.EqualTo(1));
            context.Leave("urn:a");
            Assert.That(context.Depth, Is.Zero);
        }

        [Test]
        public void ResolutionContextDocumentCountIncreasesOnEnter()
        {
            var context = new WotResolutionContext();

            Assert.That(context.DocumentCount, Is.Zero);
            context.TryEnter(WotResolutionKind.Context, "urn:ctx", out _);
            Assert.That(context.DocumentCount, Is.EqualTo(1));
            context.Leave("urn:ctx");
            context.TryEnter(WotResolutionKind.Schema, "urn:schema", out _);
            Assert.That(context.DocumentCount, Is.EqualTo(2));
        }

        [Test]
        public void ResolutionContextTotalBytesTrackedAfterAddBytes()
        {
            var options = new WotResolverOptions
            {
                MaxTotalBytes = 1000,
                MaxDocumentBytes = 500,
                MaxDepth = 10
            };
            var context = new WotResolutionContext(options);

            context.TryEnter(WotResolutionKind.Context, "urn:ctx", out _);
            context.TryAddBytes("urn:ctx", 42, out _);

            Assert.That(context.TotalBytes, Is.EqualTo(42));
        }

        [Test]
        public void TryAddBytesReturnsTrueAndUpdatesTotalBytesWithinLimits()
        {
            var options = new WotResolverOptions
            {
                MaxTotalBytes = 1000,
                MaxDocumentBytes = 500,
                MaxDepth = 10
            };
            var context = new WotResolutionContext(options);
            context.TryEnter(WotResolutionKind.Thing, "urn:test", out _);

            bool result = context.TryAddBytes("urn:test", 100, out WotDiagnostic diagnostic);

            Assert.That(result, Is.True);
            Assert.That(diagnostic, Is.Null);
            Assert.That(context.TotalBytes, Is.EqualTo(100));
        }

        [Test]
        public void TryAddBytesReturnsFalseWhenCumulativeTotalLimitExceeded()
        {
            var options = new WotResolverOptions
            {
                MaxTotalBytes = 10,
                MaxDocumentBytes = 100,
                MaxDepth = 10,
                MaxDocuments = 100
            };
            var context = new WotResolutionContext(options);

            context.TryEnter(WotResolutionKind.Thing, "urn:a", out _);
            context.TryAddBytes("urn:a", 6, out _);
            context.Leave("urn:a");

            context.TryEnter(WotResolutionKind.Schema, "urn:b", out _);
            bool result = context.TryAddBytes("urn:b", 6, out WotDiagnostic diagnostic);

            Assert.That(result, Is.False);
            Assert.That(diagnostic, Is.Not.Null);
            Assert.That(diagnostic!.Code, Is.EqualTo(WotDiagnosticCode.ResolverLimitExceeded));
        }

        [Test]
        public void WotResolverResultFromBytesCreatesFoundResult()
        {
            byte[] content = System.Text.Encoding.UTF8.GetBytes("{\"title\":\"T\"}");
            WotResolverResult result = WotResolverResult.FromBytes(
                content,
                contentType: "application/json");

            Assert.That(result.Found, Is.True);
            Assert.That(result.Content.Length, Is.EqualTo(content.Length));
            Assert.That(result.ContentType, Is.EqualTo("application/json"));
        }

        [Test]
        public void WotResolverResultNotFoundHasFoundFalse()
        {
            WotResolverResult result = WotResolverResult.NotFound;

            Assert.That(result.Found, Is.False);
            Assert.That(result.Content.Length, Is.Zero);
            Assert.That(result.ContentType, Is.Null);
        }

        [Test]
        public void TryEnterWithContextKindIncludesKindNameInDiagnosticMessage()
        {
            // With MaxDepth=1, the second entry hits the depth limit.
            var options = new WotResolverOptions { MaxDepth = 1 };
            var context = new WotResolutionContext(options);

            context.TryEnter(WotResolutionKind.Context, "urn:first", out _);
            context.TryEnter(
                WotResolutionKind.Context,
                "urn:second",
                out WotDiagnostic diagnostic);

            Assert.That(diagnostic, Is.Not.Null);
            Assert.That(
                diagnostic!.Message,
                Does.Contain(nameof(WotResolutionKind.Context)));
        }

        [Test]
        public void TryEnterWithSchemaKindIncludesKindNameInDiagnosticMessage()
        {
            // Cycle detection also embeds the kind name in its message.
            var context = new WotResolutionContext();

            context.TryEnter(WotResolutionKind.Schema, "urn:schema", out _);
            context.TryEnter(
                WotResolutionKind.Schema,
                "urn:schema",
                out WotDiagnostic diagnostic);

            Assert.That(diagnostic, Is.Not.Null);
            Assert.That(
                diagnostic!.Message,
                Does.Contain(nameof(WotResolutionKind.Schema)));
        }

        [Test]
        public void ResolutionContextWithNullOptionsUsesDefaultValues()
        {
            var context = new WotResolutionContext(null);

            Assert.That(context.Options.MaxDepth, Is.GreaterThan(0));
            Assert.That(context.Options.MaxDocuments, Is.GreaterThan(0));
        }

        [Test]
        public void ResolutionContextDiagnosticsAreEmptyInitially()
        {
            var context = new WotResolutionContext();

            Assert.That(context.Diagnostics, Is.Empty);
        }

        [Test]
        public void ResolutionContextDiagnosticsAccumulateOnFailure()
        {
            var options = new WotResolverOptions { MaxDepth = 1 };
            var context = new WotResolutionContext(options);

            context.TryEnter(WotResolutionKind.Context, "urn:a", out _);
            context.TryEnter(WotResolutionKind.Context, "urn:b", out _);

            Assert.That(context.Diagnostics, Is.Not.Empty);
            Assert.That(context.Diagnostics[0].Code, Is.EqualTo(WotDiagnosticCode.ResolverDepthExceeded));
        }
    }
}
