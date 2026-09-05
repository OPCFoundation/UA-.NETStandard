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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using NUnit.Framework;
using Opc.Ua.Wot;
using Opc.Ua.WotCon.Bindings.Planners;

namespace Opc.Ua.WotCon.Bindings.Tests
{
    /// <summary>
    /// A transmitted URI carries no non-ASCII character: every one is encoded
    /// as UTF-8 bytes and each byte percent-encoded.
    /// </summary>
    /// <remarks>
    /// A URI that still carries a non-ASCII character is an IRI, and what an
    /// IRI turns into on the wire depends on the framework's IRI parsing being
    /// on, on the target's own idea of the encoding, and on any proxy in
    /// between. That is three ways to reach a resource other than the one the
    /// document names, which is why the encoding is done once, here, rather
    /// than left to whoever transmits it.
    /// </remarks>
    [TestFixture]
    public sealed class WotTransmittedUriTests
    {
        [Test]
        public void ANonAsciiPathIsEncodedAsUtf8Octets()
        {
            string target = CompileHttpTarget("http://example.com/ger\u00E4te");

            Assert.Multiple(() =>
            {
                Assert.That(target, Is.EqualTo("http://example.com/ger%C3%A4te"));
                Assert.That(IsAscii(target), Is.True);
            });
        }

        /// <summary>
        /// The query is where a portable NodeId lands, so it is the part that
        /// most often carries a non-ASCII BrowseName.
        /// </summary>
        [Test]
        public void ANonAsciiQueryIsEncodedAsUtf8Octets()
        {
            string target = CompileHttpTarget(
                "http://example.com/?id=nsu=urn:t;s=Dr%23uck\u00FCberwachung");

            Assert.Multiple(() =>
            {
                Assert.That(target, Does.Contain("Dr%23uck%C3%BCberwachung"));
                Assert.That(IsAscii(target), Is.True);
            });
        }

        /// <summary>
        /// A code point outside the Basic Multilingual Plane is one character
        /// and one UTF-8 sequence, so its surrogate halves are never encoded
        /// separately.
        /// </summary>
        [Test]
        public void ASupplementaryCodePointIsEncodedAsOneSequence()
        {
            Assert.That(
                WotProtocolBinderBase.PercentEncodeNonAscii(
                    "http://example.com/\U0001F600"),
                Is.EqualTo("http://example.com/%F0%9F%98%80"));
        }

        /// <summary>
        /// A lone high surrogate is not a code point; it is encoded as the
        /// replacement character rather than emitted raw, because an invalid
        /// UTF-16 sequence has no UTF-8 form and passing it through would put
        /// a non-ASCII character back on the wire.
        /// </summary>
        [Test]
        public void AnUnpairedSurrogateStillYieldsAsciiOnly()
        {
            string encoded = WotProtocolBinderBase.PercentEncodeNonAscii(
                "http://example.com/a\uD83Db");

            Assert.Multiple(() =>
            {
                Assert.That(IsAscii(encoded), Is.True);
                Assert.That(encoded, Does.StartWith("http://example.com/a%"));
                Assert.That(encoded, Does.EndWith("b"));
            });
        }

        /// <summary>
        /// A URI that is already ASCII is returned unchanged, so existing
        /// percent-escapes are not encoded a second time.
        /// </summary>
        [TestCase("http://example.com/a/b?id=nsu=urn:t;s=X")]
        [TestCase("http://example.com/%23?id=nsu=urn:t;s=A%26B")]
        [TestCase("https://example.com:8443/x%C3%A4y")]
        public void AnAsciiUriIsUnchanged(string uri)
        {
            Assert.That(
                WotProtocolBinderBase.PercentEncodeNonAscii(uri),
                Is.EqualTo(uri));
        }

        /// <summary>
        /// The encoding uses upper-case hexadecimal, which RFC 3986 states as
        /// the canonical form, so two producers of the same URI agree byte for
        /// byte.
        /// </summary>
        [Test]
        public void TheEncodingUsesCanonicalUpperCaseHex()
        {
            Assert.That(
                WotProtocolBinderBase.PercentEncodeNonAscii("http://x/\u00FF"),
                Is.EqualTo("http://x/%C3%BF"));
        }

        [Test]
        public void CoapTargetsAreEncodedTheSameWay()
        {
            var planner = new CoapBindingPlanner();
            WotBindingCompilation result = planner.Compile(
                MakeForm("coap://example.com/ger\u00E4te"), new WotBindingPlanContext());

            Assert.Multiple(() =>
            {
                Assert.That(result.IsSupported, Is.True, Diagnostics(result));
                Assert.That(
                    result.Entries[0].Addressing.Target,
                    Is.EqualTo("coap://example.com/ger%C3%A4te"));
            });
        }

        /// <summary>
        /// A percent-escape is not a spelling of a host. An internationalized
        /// authority is turned into ASCII by IDNA, so the transmitted URI names
        /// the host that resolves rather than one that resolves to nothing.
        /// </summary>
        [TestCase("http://\u00FC.example/x", "http://xn--tda.example/x")]
        [TestCase("http://\u4F8B\u3048.jp/x", "http://xn--r8jz45g.jp/x")]
        [TestCase("https://\u00FC.example:8443/a/b", "https://xn--tda.example:8443/a/b")]
        [TestCase("http://sub.\u00FC.example/", "http://sub.xn--tda.example/")]
        public void AnInternationalizedAuthorityBecomesItsALabel(string href, string expected)
        {
            string target = CompileHttpTarget(href);

            Assert.Multiple(() =>
            {
                Assert.That(target, Is.EqualTo(expected));
                Assert.That(IsAscii(target), Is.True);
                Assert.That(target, Does.Not.Contain("%C3"),
                    "A host is never percent-encoded; that names no host at all.");
            });
        }

        [TestCase("coap://\u00FC.example/x", "coap://xn--tda.example/x")]
        [TestCase("coap://\u4F8B\u3048.jp/ger\u00E4te", "coap://xn--r8jz45g.jp/ger%C3%A4te")]
        [TestCase("coaps://\u00FC.example:5684/a?q=\u00E4", "coaps://xn--tda.example:5684/a?q=%C3%A4")]
        public void ACoapInternationalizedAuthorityBecomesItsALabel(string href, string expected)
        {
            Assert.That(CompileCoapTarget(href), Is.EqualTo(expected));
        }

        /// <summary>
        /// Userinfo, an explicit port, an IPv6 literal, the path, the query and
        /// the fragment are the boundaries the component rebuild has to get
        /// right: each is carried through, and only the three that admit
        /// percent-encoding are encoded.
        /// </summary>
        [TestCase("http://user:pw@\u00FC.example:8443/p", "http://user:pw@xn--tda.example:8443/p")]
        [TestCase("http://user@example.com/p", "http://user@example.com/p")]
        [TestCase("http://[::1]:8080/p?q=\u00FC", "http://[::1]:8080/p?q=%C3%BC")]
        [TestCase("http://[2001:db8::1]/x", "http://[2001:db8::1]/x")]
        [TestCase("http://example.com:80/x", "http://example.com/x")]
        [TestCase("http://example.com:8080/x", "http://example.com:8080/x")]
        [TestCase("http://\u00FC.example/a/b?x=\u00E4#frag", "http://xn--tda.example/a/b?x=%C3%A4#frag")]
        [TestCase("http://example.com/", "http://example.com/")]
        public void EveryAuthorityAndPathBoundaryIsCarriedThrough(string href, string expected)
        {
            Assert.That(CompileHttpTarget(href), Is.EqualTo(expected));
        }

        /// <summary>
        /// The endpoint the plan carries, the authority a credential is scoped
        /// to and the URI on the wire are one host. If they disagreed, a policy
        /// would gate a name the request never uses and a credential scoped to
        /// one spelling would not be found when the other is presented.
        /// </summary>
        [Test]
        public void TheEndpointCredentialAndTargetAgreeOnTheAsciiAuthority()
        {
            var planner = new HttpBindingPlanner();
            WotBindingCompilation result = planner.Compile(
                MakeForm("https://\u00FC.example:8443/p", "basic_sc"),
                new WotBindingPlanContext(
                    ImmutableDictionary<string, WotSecurityDefinition>.Empty.Add(
                        "basic_sc",
                        new WotSecurityDefinition(
                            "basic_sc", WotSecurityScheme.Basic, "header", "Authorization"))));

            Assert.That(result.IsSupported, Is.True, Diagnostics(result));
            WotCompiledForm entry = result.Entries[0];

            Assert.Multiple(() =>
            {
                Assert.That(entry.Endpoint.Host, Is.EqualTo("xn--tda.example"));
                Assert.That(entry.Endpoint.BaseUri, Is.EqualTo("https://xn--tda.example:8443"));
                Assert.That(entry.Addressing.Target, Is.EqualTo("https://xn--tda.example:8443/p"));
                Assert.That(entry.Security, Has.Length.EqualTo(1));
                Assert.That(
                    entry.Security[0].Endpoint,
                    Is.EqualTo("https://xn--tda.example:8443"));
                Assert.That(
                    entry.Addressing.Target,
                    Does.StartWith(entry.Endpoint.BaseUri!),
                    "The transmitted URI starts at the authority the plan was scoped to.");
            });
        }

        /// <summary>
        /// An IPv6 endpoint keeps its brackets on the wire and loses them in
        /// the host member, which is what a connect call takes.
        /// </summary>
        [Test]
        public void AnIPv6EndpointKeepsItsBracketsOnlyInTheUri()
        {
            var planner = new HttpBindingPlanner();
            WotBindingCompilation result = planner.Compile(
                MakeForm("http://[2001:db8::1]:8080/p"), new WotBindingPlanContext());

            Assert.That(result.IsSupported, Is.True, Diagnostics(result));

            Assert.Multiple(() =>
            {
                Assert.That(result.Entries[0].Endpoint.Host, Is.EqualTo("2001:db8::1"));
                Assert.That(
                    result.Entries[0].Addressing.Target,
                    Is.EqualTo("http://[2001:db8::1]:8080/p"));
            });
        }

        /// <summary>
        /// A blocked host is blocked in either spelling. Refusing only the
        /// A-label while a document writes the Unicode name refuses nothing.
        /// </summary>
        [TestCase("http://\u00FC.example/x")]
        [TestCase("http://xn--tda.example/x")]
        public void ABlockedInternationalizedHostIsRefusedInEitherSpelling(string endpoint)
        {
            var policy = new WotEndpointPolicy();
            policy.BlockedHosts.Add("xn--tda.example");

            ServiceResult result = WotEndpointValidator.Validate(endpoint, policy, out _);

            Assert.That(ServiceResult.IsBad(result), Is.True, result.ToString());
        }

        /// <summary>
        /// An allow list names one host however it is spelled, because both
        /// spellings denote the same name.
        /// </summary>
        [TestCase("xn--tda.example")]
        [TestCase("\u00FC.example")]
        public void AnAllowedInternationalizedHostIsAcceptedInEitherSpelling(string allowed)
        {
            var policy = new WotEndpointPolicy();
            policy.AllowedHosts.Add(allowed);

            ServiceResult result = WotEndpointValidator.Validate(
                "http://\u00FC.example/x", policy, out _);

            Assert.That(ServiceResult.IsBad(result), Is.False, result.ToString());
        }

        /// <summary>
        /// The ASCII host is the literal for an IP endpoint and the A-label
        /// for a name. A URI that parses with no authority at all still parses
        /// with whatever host the scheme's own syntax implies, which is why the
        /// authority itself - not the host - decides whether one is written.
        /// </summary>
        [TestCase("http://\u00FC.example/x", "xn--tda.example")]
        [TestCase("http://[::1]/x", "::1")]
        [TestCase("http://192.0.2.1/x", "192.0.2.1")]
        [TestCase("urn:example:thing", "")]
        [TestCase("mailto:a@example.com", "example.com")]
        public void TheAsciiHostIsTheNameThatResolves(string uri, string expected)
        {
            Assert.That(
                WotEndpointValidator.ToAsciiHost(new Uri(uri)),
                Is.EqualTo(expected));
        }

        [Test]
        public void TheAsciiHostRefusesANullUri()
        {
            Assert.That(
                () => WotEndpointValidator.ToAsciiHost(null!),
                Throws.ArgumentNullException);
        }

        /// <summary>
        /// A URI with no authority has none to rewrite, so it is encoded whole
        /// rather than gaining a <c>//</c> that would name a different
        /// resource.
        /// </summary>
        [Test]
        public void AnAuthorityLessUriIsEncodedWhole()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    ExposedBinder.Transmit(new Uri("urn:example:ger\u00E4te")),
                    Is.EqualTo("urn:example:ger%C3%A4te"));
                Assert.That(
                    ExposedBinder.Transmit(new Uri("mailto:a@example.com")),
                    Is.EqualTo("mailto:a@example.com"),
                    "A mailto parses with a host but has no authority.");
                Assert.That(
                    ExposedBinder.Authority(new Uri("urn:example:thing")),
                    Is.Empty,
                    "There is no authority, so nothing is scoped to one.");
            });
        }

        [Test]
        public void TheAuthorityHelperRefusesANullUri()
        {
            Assert.That(
                () => ExposedBinder.Authority(null!),
                Throws.ArgumentNullException);
        }

        /// <summary>
        /// The base class wires <c>Identification</c> and <c>Planner</c> to the
        /// binder itself, so a concrete binder implements one type rather than
        /// three that have to be kept consistent.
        /// </summary>
        [Test]
        public void TheBinderBaseIsItsOwnIdentificationAndPlanner()
        {
            var binder = new ExposedBinder();

            Assert.Multiple(() =>
            {
                Assert.That(binder.Identification, Is.SameAs(binder));
                Assert.That(binder.Planner, Is.SameAs(binder));
                Assert.That(binder.Identity.Id, Is.EqualTo("test.exposed"));
                Assert.That(binder.Capability.IsExecutable, Is.False);
                Assert.That(
                    binder.Match(
                        MakeForm("http://example.com/x"),
                        new WotBindingSelectionContext([], [])).IsMatch,
                    Is.False);
                Assert.That(
                    binder.Compile(MakeForm("http://example.com/x"), new WotBindingPlanContext())
                        .IsSupported,
                    Is.False);
            });
        }

        /// <summary>
        /// A name IDNA can convert becomes its A-label; one that is already
        /// ASCII is left exactly as written, so an existing configuration is
        /// not rewritten; and one IDNA refuses is handed back rather than
        /// turned into a spelling that names something else.
        /// </summary>
        [TestCase("\u00FC.example", "xn--tda.example")]
        [TestCase("\u4F8B\u3048.jp", "xn--r8jz45g.jp")]
        [TestCase("example.com", "example.com")]
        [TestCase("XN--TDA.EXAMPLE", "XN--TDA.EXAMPLE")]
        [TestCase("\u00FC..example", "\u00FC..example")]
        public void AnAsciiNameIsTheOneIdnaProduces(string host, string expected)
        {
            Assert.That(WotEndpointValidator.ToAsciiName(host), Is.EqualTo(expected));
        }

        /// <summary>
        /// An IPv6 literal is written with brackets inside a URI and without
        /// them everywhere else, so both spellings have to arrive at the same
        /// address.
        /// </summary>
        [TestCase("[::1]", "::1")]
        [TestCase("::1", "::1")]
        [TestCase("[::1", "[::1")]
        [TestCase("[", "[")]
        [TestCase("", "")]
        public void AnIPv6LiteralLosesItsBracketsExactlyOnce(string host, string expected)
        {
            Assert.That(WotEndpointValidator.Unbracket(host), Is.EqualTo(expected));
        }

        /// <summary>
        /// The gates a policy applies before it ever looks at a host: a policy
        /// is required, an endpoint is required, it has to parse, and its
        /// scheme has to be one the deployment opted in to.
        /// </summary>
        [Test]
        public void TheEndpointGatesRefuseWhatTheyAreFor()
        {
            var policy = new WotEndpointPolicy();

            Assert.Multiple(() =>
            {
                Assert.That(
                    () => WotEndpointValidator.Validate("http://example.com/", null!, out _),
                    Throws.ArgumentNullException);
                Assert.That(
                    ServiceResult.IsBad(
                        WotEndpointValidator.Validate("  ", policy, out _)),
                    Is.True,
                    "An endpoint is required.");
                Assert.That(
                    ServiceResult.IsBad(
                        WotEndpointValidator.Validate("not a uri", policy, out _)),
                    Is.True,
                    "An endpoint has to parse as an absolute URI.");
                Assert.That(
                    ServiceResult.IsBad(
                        WotEndpointValidator.Validate("mem://x/y", policy, out _)),
                    Is.True,
                    "A scheme the deployment did not opt in to is refused.");
            });
        }

        /// <summary>
        /// A URI that names no host produces an endpoint that carries none,
        /// rather than an empty string that reads like one.
        /// </summary>
        [Test]
        public void AnEndpointWithNoHostCarriesNoHost()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    ExposedBinder.Endpoint(new Uri("urn:device:1")).Host,
                    Is.Null);
                Assert.That(
                    ExposedBinder.Endpoint(new Uri("http://example.com/x")).Host,
                    Is.EqualTo("example.com"));
            });
        }

        [Test]
        public void ANullUriIsRefused()
        {
            Assert.That(
                () => WotProtocolBinderBase.PercentEncodeNonAscii(null!),
                Throws.ArgumentNullException);
        }

        /// <summary>
        /// A high surrogate at the very end of a URI has no partner, so the
        /// pairing test has to look at the length before it looks at the next
        /// character.
        /// </summary>
        [Test]
        public void ATrailingHighSurrogateStillYieldsAsciiOnly()
        {
            string encoded = WotProtocolBinderBase.PercentEncodeNonAscii(
                "http://example.com/a\uD83D");

            Assert.Multiple(() =>
            {
                Assert.That(IsAscii(encoded), Is.True);
                Assert.That(encoded, Does.StartWith("http://example.com/a%"));
            });
        }

        /// <summary>
        /// The protected entry point a planner uses is the same encoding
        /// applied to a parsed URI, and it refuses a null rather than throwing
        /// where the cause is no longer visible.
        /// </summary>
        [Test]
        public void TheProtectedEntryPointEncodesAndRefusesNull()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    ExposedBinder.Transmit(new Uri("http://example.com/ger\u00E4te")),
                    Is.EqualTo("http://example.com/ger%C3%A4te"));
                Assert.That(
                    () => ExposedBinder.Transmit(null!),
                    Throws.ArgumentNullException);
            });
        }

        /// <summary>
        /// Exposes the protected helper so the entry point a planner calls is
        /// covered by the same tests as the encoding behind it.
        /// </summary>
        private sealed class ExposedBinder : WotProtocolBinderBase
        {
            public static string Transmit(Uri uri)
            {
                return ToTransmittedUri(uri);
            }

            public static string Authority(Uri uri)
            {
                return ToTransmittedAuthority(uri);
            }

            public static WotEndpointDescriptor Endpoint(Uri uri)
            {
                return MakeEndpoint(uri);
            }

            public override WotBindingIdentity Identity =>
                new WotBindingIdentity("test.exposed", "1.0", "urn:test:exposed");

            public override WotBindingCapability Capability =>
                new WotBindingCapability(
                    "urn:test:exposed",
                    "exposed",
                    WotBindingSources.Http,
                    [WoTBindingCapabilityEnum.ReadProperty],
                    ["application/json"],
                    isExecutable: false);

            protected override IReadOnlyCollection<string> Schemes { get; } = ["http"];

            public override WotBindingMatch Match(
                WotAffordanceForm form, WotBindingSelectionContext context)
            {
                return WotBindingMatch.NoMatch;
            }

            public override WotBindingCompilation Compile(
                WotAffordanceForm form, WotBindingPlanContext context)
            {
                return WotBindingCompilation.Unsupported([]);
            }
        }

        private static string CompileHttpTarget(string href)
        {
            var planner = new HttpBindingPlanner();
            WotBindingCompilation result = planner.Compile(
                MakeForm(href), new WotBindingPlanContext());

            Assert.That(result.IsSupported, Is.True, Diagnostics(result));
            return result.Entries[0].Addressing.Target;
        }

        private static string CompileCoapTarget(string href)
        {
            var planner = new CoapBindingPlanner();
            WotBindingCompilation result = planner.Compile(
                MakeForm(href), new WotBindingPlanContext());

            Assert.That(result.IsSupported, Is.True, Diagnostics(result));
            return result.Entries[0].Addressing.Target;
        }

        private static string Diagnostics(WotBindingCompilation result)
        {
            var builder = new StringBuilder();
            foreach (WotBindingDiagnostic diagnostic in result.Diagnostics)
            {
                builder.Append(diagnostic.Code).Append(": ")
                    .Append(diagnostic.Message).Append("; ");
            }
            return builder.ToString();
        }

        private static bool IsAscii(string value)
        {
            foreach (char c in value)
            {
                if (c > '\u007F')
                {
                    return false;
                }
            }
            return true;
        }

        private static WotAffordanceForm MakeForm(string href, params string[] security)
        {
            using JsonDocument formDoc = JsonDocument.Parse("{}");
            using JsonDocument affordanceDoc = JsonDocument.Parse("{}");
            return new WotAffordanceForm(
                WotAffordanceKind.Property,
                "p",
                ["readproperty"],
                href,
                "application/json",
                null,
                [.. security],
                "/properties/p/forms/0",
                formDoc.RootElement.Clone(),
                affordanceDoc.RootElement.Clone());
        }
    }
}
