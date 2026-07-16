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
        private static readonly string[] s_rs512Algorithms = ["RS512"];

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
        public void GetEffectiveAlgorithmsFiltersBlankEntriesAndRestoresDefault()
        {
            var options = new JwtIssuerOptions();
            options.Algorithms.Clear();
            options.Algorithms.Add(" ");
            options.Algorithms.Add("RS512");
            options.Algorithms.Add(string.Empty);

            Assert.That(options.GetEffectiveAlgorithms(), Is.EqualTo(s_rs512Algorithms));

            options.Algorithms.Clear();
            options.Algorithms.Add(" ");

            Assert.That(options.GetEffectiveAlgorithms(), Is.EqualTo(s_rs256Algorithms));
        }

        [Test]
        public void StaticRsaModulusAndExponentCreateVerificationKey()
        {
            using var rsa = RSA.Create(2048);
            RSAParameters parameters = rsa.ExportParameters(false);
            var options = new JwtStaticKeyOptions
            {
                Kid = "kid-rsa",
                Algorithm = "RS384",
                RsaModulus = Base64UrlEncode(parameters.Modulus),
                RsaExponent = Base64UrlEncode(parameters.Exponent)
            };

            using IssuerVerificationKey key = options.CreateVerificationKey();

            Assert.That(key.KeyId, Is.EqualTo("kid-rsa"));
            Assert.That(key.Algorithm, Is.EqualTo("RS384"));
        }

        [Test]
        public void StaticRsaPemSupportsPkcsOneAndSubjectPublicKeyInfo()
        {
            using var rsa = RSA.Create(2048);
            RSAParameters parameters = rsa.ExportParameters(false);
            string pkcsOnePem = ToPem("RSA PUBLIC KEY", EncodeRsaPublicKey(parameters));
            string spkiPem = ToPem("PUBLIC KEY", EncodeSubjectPublicKeyInfo(parameters));

            using IssuerVerificationKey pkcsOneKey = new JwtStaticKeyOptions
            {
                Kid = "pkcs1",
                RsaPublicKeyPem = pkcsOnePem
            }.CreateVerificationKey();

            using IssuerVerificationKey spkiKey = new JwtStaticKeyOptions
            {
                Kid = "spki",
                RsaPublicKeyPem = spkiPem
            }.CreateVerificationKey();

            Assert.That(pkcsOneKey.KeyId, Is.EqualTo("pkcs1"));
            Assert.That(spkiKey.KeyId, Is.EqualTo("spki"));
        }

        [Test]
        public void StaticKeyRejectsMissingAlgorithmAndMissingMaterial()
        {
            Assert.That(
                () => new JwtStaticKeyOptions { Algorithm = " " }.CreateVerificationKey(),
                Throws.InvalidOperationException);

            Assert.That(
                () => new JwtStaticKeyOptions().CreateVerificationKey(),
                Throws.InvalidOperationException);
        }

        [Test]
        public void StaticKeyRejectsInvalidPemShapes()
        {
            Assert.That(
                () => new JwtStaticKeyOptions
                {
                    RsaPublicKeyPem = "-----BEGIN PUBLIC KEY-----\r\nAQAB\r\n-----END PUBLIC KEY-----"
                }.CreateVerificationKey(),
                Throws.InvalidOperationException);

            Assert.That(
                () => new JwtStaticKeyOptions
                {
                    RsaPublicKeyPem = "-----BEGIN RSA PUBLIC KEY-----\r\nAQAB"
                }.CreateVerificationKey(),
                Throws.InvalidOperationException);
        }

        [Test]
        public async Task PerIssuerAudienceOverridesDefaultAudience()
        {
            using var rsa = RSA.Create(2048);
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
            const string header = /*lang=json,strict*/ "{\"alg\":\"RS256\",\"kid\":\"kid-rsa\",\"typ\":\"JWT\"}";
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

        private static string ToPem(string label, byte[] der)
        {
            string base64 = Convert.ToBase64String(der);
            var builder = new StringBuilder();
            builder.Append("-----BEGIN ");
            builder.Append(label);
            builder.AppendLine("-----");
            for (int offset = 0; offset < base64.Length; offset += 64)
            {
                builder.AppendLine(base64.Substring(offset, Math.Min(64, base64.Length - offset)));
            }
            builder.Append("-----END ");
            builder.Append(label);
            builder.AppendLine("-----");
            return builder.ToString();
        }

        private static byte[] EncodeSubjectPublicKeyInfo(RSAParameters parameters)
        {
            byte[] rsaPublicKey = EncodeRsaPublicKey(parameters);
            byte[] algorithm = EncodeSequence(
                new byte[] { 0x06, 0x09, 0x2A, 0x86, 0x48, 0x86, 0xF7, 0x0D, 0x01, 0x01, 0x01 },
                new byte[] { 0x05, 0x00 });
            byte[] bitString = Encode(0x03, Prepend(rsaPublicKey, 0));
            return EncodeSequence(algorithm, bitString);
        }

        private static byte[] EncodeRsaPublicKey(RSAParameters parameters)
        {
            return EncodeSequence(
                EncodeInteger(parameters.Modulus),
                EncodeInteger(parameters.Exponent));
        }

        private static byte[] EncodeInteger(byte[] value)
        {
            byte[] normalized = value[0] < 0x80 ? value : Prepend(value, 0);
            return Encode(0x02, normalized);
        }

        private static byte[] EncodeSequence(params byte[][] values)
        {
            return Encode(0x30, Concatenate(values));
        }

        private static byte[] Encode(byte tag, byte[] value)
        {
            byte[] length = EncodeLength(value.Length);
            var result = new byte[1 + length.Length + value.Length];
            result[0] = tag;
            Buffer.BlockCopy(length, 0, result, 1, length.Length);
            Buffer.BlockCopy(value, 0, result, 1 + length.Length, value.Length);
            return result;
        }

        private static byte[] EncodeLength(int length)
        {
            if (length < 0x80)
            {
                return [(byte)length];
            }

            var bytes = new List<byte>();
            int value = length;
            while (value > 0)
            {
                bytes.Insert(0, (byte)value);
                value >>= 8;
            }
            return [.. new[] { (byte)(0x80 | bytes.Count) }, .. bytes];
        }

        private static byte[] Prepend(byte[] value, byte prefix)
        {
            var result = new byte[value.Length + 1];
            result[0] = prefix;
            Buffer.BlockCopy(value, 0, result, 1, value.Length);
            return result;
        }

        private static byte[] Concatenate(params byte[][] values)
        {
            int length = values.Sum(value => value.Length);
            var result = new byte[length];
            int offset = 0;
            foreach (byte[] value in values)
            {
                Buffer.BlockCopy(value, 0, result, offset, value.Length);
                offset += value.Length;
            }
            return result;
        }
    }
}
