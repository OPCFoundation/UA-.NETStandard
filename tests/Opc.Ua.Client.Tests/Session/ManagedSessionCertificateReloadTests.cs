/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 *
 * OPC Foundation MIT License 1.00
 *
 * Permission is hereby granted, free of charge, to any person obtaining a
 * copy of this software and associated documentation files (the "Software"),
 * to deal in the Software without restriction, including without limitation
 * the rights to use, copy, modify, merge, publish, distribute, sublicense,
 * and/or sell copies of the Software, and to permit persons to whom the
 * Software is furnished to do so, subject to the following conditions:
 *
 * The above copyright notice and this permission notice shall be included
 * in all copies or substantial portions of the Software.
 *
 * THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
 * OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
 * FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL
 * THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR
 * OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE,
 * ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
 * OTHER DEALINGS IN THE SOFTWARE.
 * ======================================================================*/

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua.Client.TestFramework;
using Opc.Ua.Security.Certificates;
using ManagedSessionType = Opc.Ua.Client.ManagedSession;

namespace Opc.Ua.Client.Tests
{
    /// <summary>
    /// Exercises certificate replacement on a real managed UA-TCP session.
    /// </summary>
    [TestFixture]
    [Category("Client")]
    [Category("Session")]
    [NonParallelizable]
    public sealed class ManagedSessionCertificateReloadTests : ClientTestFramework
    {
        /// <summary>
        /// Uses a dedicated reference server and client certificate store.
        /// </summary>
        public ManagedSessionCertificateReloadTests()
            : base(Utils.UriSchemeOpcTcp)
        {
            SingleSession = false;
        }

        /// <inheritdoc/>
        [OneTimeSetUp]
        public override Task OneTimeSetUpAsync()
        {
            return base.OneTimeSetUpAsync();
        }

        /// <inheritdoc/>
        [OneTimeTearDown]
        public override Task OneTimeTearDownAsync()
        {
            return base.OneTimeTearDownAsync();
        }

        /// <summary>
        /// A manually reconnected channel must present the installed certificate even when its key is unchanged.
        /// </summary>
        [Test]
        public async Task ManualReconnectPresentsReissuedCertificateWithSameKeyAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
            await using ManagedSessionType session = await new ManagedSessionBuilder(ClientFixture.Config, Telemetry)
                .UseEndpoint(ServerUrl.ToString(), MessageSecurityMode.SignAndEncrypt, SecurityPolicies.Basic256Sha256)
                .ConnectAsync(timeout.Token).ConfigureAwait(false);
            session.DisableAutoReconnectOnCertificateChange = true;
            Assert.That(session.ConfiguredEndpoint.Description.SecurityMode,
                Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
            Assert.That(session.ConfiguredEndpoint.Description.SecurityPolicyUri,
                Is.EqualTo(SecurityPolicies.Basic256Sha256));
            Assert.That(session.InnerSession.ManagedChannel, Is.Null);

            ICertificateManager manager = ClientFixture.Config.CertificateManager ??
                throw new AssertionException("The client must have a certificate manager.");
            using CertificateEntry original = manager.AcquireApplicationCertificateByType(
                ObjectTypeIds.RsaSha256ApplicationCertificateType) ??
                throw new AssertionException("The client must have an RSA application certificate.");
            using RSA publicKey = original.Certificate.GetRSAPublicKey() ??
                throw new AssertionException("The original certificate must use RSA.");
            using RSA privateKey = original.Certificate.GetRSAPrivateKey() ??
                throw new AssertionException("The original certificate must have its private key.");
            byte[] originalCertificate = original.Certificate.RawData;
            Assert.That(session.TransportChannel.ClientChannelCertificate, Is.EqualTo(originalCertificate));

            string applicationUri = ClientFixture.Config.ApplicationUri ??
                throw new AssertionException("The client must have an application URI.");
            X509SignatureGenerator generator = X509SignatureGenerator.CreateForRSA(
                privateKey, RSASignaturePadding.Pkcs1);
            using Certificate issued = CertificateBuilder.Create(original.Certificate.SubjectName)
                .AddExtension(new X509SubjectAltNameExtension(
                    applicationUri, X509Utils.GetDomainsFromCertificate(original.Certificate).ToArray() ?? []))
                .SetRSAPublicKey(publicKey)
                .CreateForRSA(generator);
            using Certificate replacement = DefaultCertificateFactory.Instance.CreateWithPrivateKey(
                issued, original.Certificate);
            using RSA replacementKey = replacement.GetRSAPublicKey() ??
                throw new AssertionException("The replacement certificate must use RSA.");
            byte[] replacementCertificate = replacement.RawData;
            RSAParameters originalParameters = publicKey.ExportParameters(false);
            RSAParameters replacementParameters = replacementKey.ExportParameters(false);
            Assert.That(replacementCertificate, Is.Not.EqualTo(originalCertificate));
            Assert.That(originalParameters.Modulus, Is.Not.Null);
            Assert.That(originalParameters.Exponent, Is.Not.Null);
            Assert.That(replacementParameters.Modulus, Is.EqualTo(originalParameters.Modulus));
            Assert.That(replacementParameters.Exponent, Is.EqualTo(originalParameters.Exponent));

            CertificateIdentifier identifier = ClientFixture.Config.SecurityConfiguration.ApplicationCertificates
                .ToList().First(value => value.CertificateType == ObjectTypeIds.RsaSha256ApplicationCertificateType);
            var storeIdentifier = new CertificateStoreIdentifier(
                identifier.StorePath ?? throw new AssertionException("The certificate store path is missing."),
                identifier.StoreType ?? throw new AssertionException("The certificate store type is missing."),
                noPrivateKeys: false);
            using ICertificateStore store = storeIdentifier.OpenStore(Telemetry);
            char[] password = ClientFixture.Config.SecurityConfiguration.CertificatePasswordProvider?
                .GetPassword(identifier) ?? [];
            try
            {
                await store.AddAsync(replacement, password, timeout.Token).ConfigureAwait(false);
            }
            finally
            {
                Array.Clear(password, 0, password.Length);
            }
            using var issuerChain = new CertificateCollection();
            await manager.UpdateApplicationCertificateAsync(
                ObjectTypeIds.RsaSha256ApplicationCertificateType, replacement, issuerChain, timeout.Token)
                .ConfigureAwait(false);
            identifier.RawData = null;
            identifier.Thumbprint = replacement.Thumbprint;
            identifier.SubjectName = replacement.Subject;
            using Certificate reloaded = await ClientFixture.Config.SecurityConfiguration
                .FindApplicationCertificateAsync(
                    SecurityPolicies.Basic256Sha256, privateKey: true, Telemetry, timeout.Token)
                .ConfigureAwait(false) ??
                throw new AssertionException("The replacement certificate must be available from the store.");
            Assert.That(reloaded.RawData, Is.EqualTo(replacementCertificate));
            Assert.That(reloaded.HasPrivateKey, Is.True);

            await session.ReloadInstanceCertificateAsync(timeout.Token).ConfigureAwait(false);
            await session.ReconnectAsync(null, null, timeout.Token).ConfigureAwait(false);

            Assert.That(session.TransportChannel.ClientChannelCertificate, Is.EqualTo(replacementCertificate));
            DataValue value = await session.ReadValueAsync(VariableIds.Server_ServerStatus_State, timeout.Token)
                .ConfigureAwait(false);
            Assert.That(value.StatusCode, Is.EqualTo(StatusCodes.Good));
            await session.CloseAsync(timeout.Token).ConfigureAwait(false);
        }
    }
}
