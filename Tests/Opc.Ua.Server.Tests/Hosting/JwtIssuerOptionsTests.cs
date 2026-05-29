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

#nullable disable

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Opc.Ua.Identity;
using Opc.Ua.Server.Hosting;
using Opc.Ua.Tests;

namespace Opc.Ua.Server.Tests.Hosting
{
    [TestFixture]
    [Category("Identity")]
    [Category("Hosting")]
    public class JwtIssuerOptionsTests
    {
        private const string Issuer = "https://issuer.example.test";
        private const string DefaultAudience = "urn:opcua:default-server";
        private const string OverrideAudience = "urn:opcua:issuer-server";
        private static readonly string[] s_rs256Algorithms = ["RS256"];

        [Test]
        public void ValidateRejectsMissingIssuerUri()
        {
            var options = new JwtIssuerOptions();
            options.StaticKeys.Add(new JwtStaticKeyOptions { RsaModulus = "AQAB", RsaExponent = "AQAB" });

            Assert.That(options.Validate, Throws.InvalidOperationException);
        }

        [Test]
        public void ValidateRejectsMissingKeySource()
        {
            var options = new JwtIssuerOptions { IssuerUri = Issuer };

            Assert.That(options.Validate, Throws.InvalidOperationException);
        }

        [Test]
        public void AlgorithmsDefaultToRs256()
        {
            var options = new JwtIssuerOptions();

            Assert.That(options.Algorithms, Is.EqualTo(s_rs256Algorithms));
        }

        [Test]
        public async Task PerIssuerAudienceOverridesDefaultAudience()
        {
            using RSA rsa = RSA.Create(2048);
            RSAParameters parameters = rsa.ExportParameters(false);
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddOpcUa()
                .AddServer(_ => { })
                .AddJwtIssuer(options =>
                {
                    options.IssuerUri = Issuer;
                    options.Audience = OverrideAudience;
                    options.StaticKeys.Add(new JwtStaticKeyOptions
                    {
                        Kid = "kid-rsa",
                        Algorithm = "RS256",
                        RsaModulus = Base64UrlEncode(parameters.Modulus),
                        RsaExponent = Base64UrlEncode(parameters.Exponent)
                    });
                })
                .AddDefaultIdentityAuthenticators(options =>
                {
                    options.EnableAnonymous = false;
                    options.EnableUserNamePassword = false;
                    options.EnableX509 = false;
                    options.EnableJwt = true;
                    options.ExpectedAudience = DefaultAudience;
                    options.ClockSkewTolerance = TimeSpan.Zero;
                });
            using ServiceProvider provider = services.BuildServiceProvider();
            JwtAuthenticator authenticator = provider
                .GetServices<OpcUaServerIdentityAuthenticatorRegistration>()
                .SelectMany(registration => registration.CreateAuthenticators(provider, null))
                .OfType<JwtAuthenticator>()
                .Single();

            string accepted = CreateJwt(rsa, OverrideAudience);
            string rejected = CreateJwt(rsa, DefaultAudience);

            AuthenticationResult acceptedResult = await authenticator
                .AuthenticateAsync(CreateContext(accepted))
                .ConfigureAwait(false);
            AuthenticationResult rejectedResult = await authenticator
                .AuthenticateAsync(CreateContext(rejected))
                .ConfigureAwait(false);

            Assert.That(acceptedResult.Outcome, Is.EqualTo(AuthenticationOutcome.Accepted));
            Assert.That(rejectedResult.Outcome, Is.EqualTo(AuthenticationOutcome.Rejected));
        }

        private static AuthenticationContext CreateContext(string jwt)
        {
            return new AuthenticationContext(
                new IssuedIdentityTokenHandler(Profiles.JwtUserToken, Encoding.UTF8.GetBytes(jwt)),
                new UserTokenPolicy
                {
                    TokenType = UserTokenType.IssuedToken,
                    PolicyId = "jwt",
                    IssuedTokenType = Profiles.JwtUserToken
                },
                new EndpointDescription { SecurityMode = MessageSecurityMode.SignAndEncrypt },
                ServiceMessageContext.CreateEmpty(NUnitTelemetryContext.Create()));
        }

        private static string CreateJwt(RSA rsa, string audience)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string header = "{\"alg\":\"RS256\",\"kid\":\"kid-rsa\",\"typ\":\"JWT\"}";
            string payload = "{\"iss\":\"" +
                Issuer +
                "\",\"sub\":\"subject-1\",\"aud\":\"" +
                audience +
                "\",\"exp\":" +
                (now + 3600) +
                ",\"nbf\":" +
                (now - 60) +
                ",\"iat\":" +
                (now - 1) +
                "}";
            string signingInput = Base64UrlEncode(Encoding.UTF8.GetBytes(header)) +
                "." +
                Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
            byte[] signature = rsa.SignData(
                Encoding.ASCII.GetBytes(signingInput),
                HashAlgorithmName.SHA256,
                RSASignaturePadding.Pkcs1);
            return signingInput + "." + Base64UrlEncode(signature);
        }

        private static string Base64UrlEncode(byte[] bytes)
        {
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }
    }
}
