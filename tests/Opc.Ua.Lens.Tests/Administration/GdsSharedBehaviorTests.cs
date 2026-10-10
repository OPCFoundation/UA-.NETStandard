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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Gds.Client;
using Opc.Ua.Security.Certificates;
using UaLens.Plugins.Gds;
using UaLens.Plugins.GdsPush;

namespace UaLens.Tests.Administration;

[TestFixture]
public sealed class GdsSharedBehaviorTests
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task PushAdapterForwardsRequestAndOrderedNonemptyIssuersWithoutApplyingChanges(bool applyRequired)
    {
        var protocol = new Mock<IServerPushConfigurationClient>(MockBehavior.Strict);
        var adapter = new PushClientAdapter(protocol.Object);
        var group = new NodeId("HttpsGroup", 2);
        NodeId type = ObjectTypeIds.RsaSha256ApplicationCertificateType;
        using var cancellation = new CancellationTokenSource();
        byte[] nonce = [9, 8, 7, 6];
        byte[] csr = TemporaryCertificateStores.CreateSigningRequest();
        protocol.Setup(c => c.CreateSigningRequestAsync(
            group, type, "CN=production.example.test,O=Fixture", true,
            It.Is<ByteString>(b => b.Memory.ToArray().SequenceEqual(nonce)), cancellation.Token))
            .Returns(new ValueTask<ByteString>(csr.ToByteString()));

        byte[] actual = await GdsCertRequestHelper.GenerateCsrAsync(
            adapter, group, type, "CN=production.example.test,O=Fixture", true, nonce, cancellation.Token)
            .ConfigureAwait(false);

        Assert.That(actual, Is.EqualTo(csr));
        Assert.That(actual, Is.Not.SameAs(csr));
        using Certificate leaf = TemporaryCertificateStores.CreateCertificate("CN=Production");
        using Certificate issuer = TemporaryCertificateStores.CreateCertificate("CN=Intermediate");
        using Certificate root = TemporaryCertificateStores.CreateCertificate("CN=Root");
        byte[] leafBytes = leaf.RawData;
        byte[] issuerBytes = issuer.RawData;
        byte[] rootBytes = root.RawData;
        protocol.Setup(c => c.UpdateCertificateAsync(
            group, type, It.Is<ByteString>(b => b.Memory.ToArray().SequenceEqual(leafBytes)),
            string.Empty, It.Is<ByteString>(b => b.IsEmpty),
            It.Is<ArrayOf<ByteString>>(items =>
                items.Count == 2 &&
                items[0].Memory.ToArray().SequenceEqual(issuerBytes) &&
                items[1].Memory.ToArray().SequenceEqual(rootBytes)),
            cancellation.Token)).Returns(new ValueTask<bool>(applyRequired));

        await GdsCertRequestHelper.ApplyUpdatedCertificateAsync(
            adapter, group, type, leafBytes, new[] { issuerBytes, Array.Empty<byte>(), null!, rootBytes },
            null!, null!, cancellation.Token).ConfigureAwait(false);

        protocol.VerifyAll();
        protocol.Verify(c => c.ApplyChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        protocol.VerifyNoOtherCalls();
    }

    [Test]
    public async Task RequestHelperNormalizesOptionalValuesAndRejectsMissingCertificateBeforeDispatch()
    {
        var client = new Mock<IGdsClientLike>(MockBehavior.Strict);
        var group = new NodeId(5001, 2);
        var type = new NodeId(5002, 2);
        byte[] csr = TemporaryCertificateStores.CreateSigningRequest();
        client.Setup(c => c.CreateSigningRequestAsync(group, type, string.Empty, false,
            It.Is<byte[]>(bytes => bytes.Length == 0), CancellationToken.None)).ReturnsAsync(csr);
        byte[] result = await GdsCertRequestHelper.GenerateCsrAsync(
            client.Object, group, type, null!, false, null!, CancellationToken.None).ConfigureAwait(false);
        Assert.That(result, Is.EqualTo(csr));
        client.Verify(c => c.CreateSigningRequestAsync(group, type, string.Empty, false,
            It.Is<byte[]>(bytes => bytes.Length == 0), CancellationToken.None), Times.Once);
        Exception? failure = await CaptureAsync(() => GdsCertRequestHelper.ApplyUpdatedCertificateAsync(
            client.Object, group, type, null!, [], string.Empty, [], CancellationToken.None)).ConfigureAwait(false);
        Assert.That(failure, Is.TypeOf<ArgumentNullException>());
        Assert.That(((ArgumentNullException)failure!).ParamName, Is.EqualTo("certificate"));
        client.VerifyNoOtherCalls();
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UpdateHelperPreservesTheOriginalFailureAndNeverCallsApplyChanges(bool canceled)
    {
        var protocol = new Mock<IServerPushConfigurationClient>(MockBehavior.Strict);
        var group = new NodeId(5011);
        var type = new NodeId(5012);
        using Certificate certificate = TemporaryCertificateStores.CreateCertificate("CN=Failed update");
        byte[] bytes = certificate.RawData;
        Exception error = canceled
            ? new OperationCanceledException("update canceled") : new IOException("update denied");
        protocol.Setup(c => c.UpdateCertificateAsync(
            group, type, It.Is<ByteString>(b => b.Memory.ToArray().SequenceEqual(bytes)),
            string.Empty, It.Is<ByteString>(b => b.IsEmpty), It.Is<ArrayOf<ByteString>>(b => b.Count == 0),
            CancellationToken.None)).Returns(() => ValueTask.FromException<bool>(error));

        Exception? failure = await CaptureAsync(() => GdsCertRequestHelper.ApplyUpdatedCertificateAsync(
            new PushClientAdapter(protocol.Object), group, type, bytes, null!, null!, null!, CancellationToken.None))
            .ConfigureAwait(false);

        Assert.That(failure, Is.SameAs(error));
        protocol.VerifyAll();
        protocol.Verify(c => c.ApplyChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        protocol.VerifyNoOtherCalls();
    }

    [TestCase(0, "CERTIFICATE")]
    [TestCase(1, "CERTIFICATE")]
    [TestCase(47, "CERTIFICATE REQUEST")]
    [TestCase(48, "CERTIFICATE REQUEST")]
    [TestCase(49, "CERTIFICATE REQUEST")]
    [TestCase(96, "CERTIFICATE")]
    public void PemEncodingUsesExactMarkersAndSixtyFourColumnLines(int length, string label)
    {
        byte[] data = Enumerable.Range(0, length).Select(i => (byte)(i + 31)).ToArray();

        string pem = GdsCertRequestHelper.ToPem(data, label);

        if (length == 0)
        {
            Assert.That(pem, Is.Empty);
            Assert.That(() => GdsCertRequestHelper.ToPem(data, string.Empty),
                Throws.ArgumentException.With.Property("ParamName").EqualTo("label"));
            return;
        }
        string[] lines = pem.Split('\n');
        Assert.That(lines[0], Is.EqualTo("-----BEGIN " + label + "-----"));
        Assert.That(lines[^2], Is.EqualTo("-----END " + label + "-----"));
        Assert.That(lines[^1], Is.Empty);
        string[] encoded = lines.Skip(1).Take(lines.Length - 3).ToArray();
        Assert.That(string.Concat(encoded), Is.EqualTo(Convert.ToBase64String(data)));
        Assert.That(encoded.Take(encoded.Length - 1).All(line => line.Length == 64), Is.True);
        Assert.That(encoded[^1], Has.Length.EqualTo((Convert.ToBase64String(data).Length - 1) % 64 + 1));
        Assert.That(pem, Does.Not.Contain("\r"));
    }

    [Test]
    public void PemParsingAcceptsWhitespaceAndTrailerWithoutIncludingNeighboringBlocks()
    {
        const string pem =
            "prefix\r\n -----BEGIN CERTIFICATE-----\r\n A Q I\tD / w = = \r\n" +
            "-----END CERTIFICATE-----\nignored\n-----BEGIN CERTIFICATE-----\nBA==\n-----END CERTIFICATE-----";
        Assert.That(GdsCertRequestHelper.FromPem(pem, "CERTIFICATE"), Is.EqualTo(new byte[] { 1, 2, 3, 255 }));
        Assert.That(() => GdsCertRequestHelper.FromPem(pem, "CERTIFICATE REQUEST"),
            Throws.TypeOf<FormatException>().With.Message.EqualTo("Missing BEGIN CERTIFICATE REQUEST marker."));
    }

    [TestCase("-----BEGIN CERTIFICATE REQUEST-----AQID-----END CERTIFICATE REQUEST-----", "Missing BEGIN")]
    [TestCase("-----BEGIN CERTIFICATE-----AQID-----END CERTIFICATE REQUEST-----", "Missing END")]
    [TestCase("-----BEGIN CERTIFICATE-----AQID", "Missing END")]
    public void PemParsingRejectsIncorrectOrMissingMarkers(string pem, string message)
    {
        Assert.That(() => GdsCertRequestHelper.FromPem(pem, "CERTIFICATE"),
            Throws.TypeOf<FormatException>().With.Message.StartsWith(message));
        Assert.That(() => GdsCertRequestHelper.FromPem(pem, string.Empty),
            Throws.ArgumentException.With.Property("ParamName").EqualTo("expectedLabel"));
    }

    [Test]
    public void PemParsingRejectsInvalidBase64AndCertificateParsingRejectsEmptyDer()
    {
        Assert.That(() => GdsCertRequestHelper.FromPem(
            "-----BEGIN CERTIFICATE-----not base64!-----END CERTIFICATE-----", "CERTIFICATE"),
            Throws.TypeOf<FormatException>());
        Assert.That(() => GdsCertRequestHelper.ParseCertificate([]),
            Throws.TypeOf<FormatException>().With.Message.EqualTo("Empty certificate buffer."));
        Assert.That(() => GdsCertRequestHelper.ParseCertificate(new byte[] { 1, 2, 3 }),
            Throws.InstanceOf<CryptographicException>());
    }

    [TestCase("")]
    [TestCase(" \r\n\t")]
    public void PemParsingRejectsBlankInputBeforeMarkerSearch(string input)
    {
        Assert.That(() => GdsCertRequestHelper.FromPem(input, "CERTIFICATE"),
            Throws.ArgumentException.With.Property("ParamName").EqualTo("pem"));
        if (input.Length == 0)
        {
            Assert.That(() => GdsCertRequestHelper.ParseCertificate([]),
                Throws.TypeOf<FormatException>().With.Message.EqualTo("Empty certificate buffer."));
        }
        else
        {
            Assert.That(() => GdsCertRequestHelper.ParseCertificate(Encoding.ASCII.GetBytes(input)),
                Throws.InstanceOf<CryptographicException>());
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CertificateFileParsingReturnsTheGeneratedPublicCertificateWithoutRetainingTheFile(bool pem)
    {
        using var temporary = new TemporaryCertificateStores();
        using Certificate original = TemporaryCertificateStores.CreateCertificate("CN=Parsed certificate,O=Fixture");
        byte[] der = original.RawData;
        byte[] input = pem
            ? Encoding.ASCII.GetBytes(" \t\r\n" + Encoding.ASCII.GetString(PEMWriter.ExportCertificateAsPEM(original)))
            : der;
        string path = Path.Combine(temporary.Root, pem ? "public.pem" : "public.der");
        await File.WriteAllBytesAsync(path, input).ConfigureAwait(false);

        using X509Certificate2 parsed = await GdsCertRequestHelper.LoadCertificateFromFileAsync(
            path, CancellationToken.None).ConfigureAwait(false);

        Assert.That(parsed.RawData, Is.EqualTo(der));
        Assert.That(parsed.Subject, Does.Contain("CN=Parsed certificate"));
        Assert.That(parsed.HasPrivateKey, Is.False);
        using FileStream exclusive = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.That(exclusive.Length, Is.EqualTo(input.Length));
    }

    [Test]
    public async Task CertificateFileReadHonorsCancellationWithoutProducingAParsedCertificate()
    {
        using var temporary = new TemporaryCertificateStores();
        using Certificate original = TemporaryCertificateStores.CreateCertificate("CN=Canceled read");
        string path = Path.Combine(temporary.Root, "public.der");
        await File.WriteAllBytesAsync(path, original.RawData).ConfigureAwait(false);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync().ConfigureAwait(false);
        Exception? failure = await CaptureAsync(() =>
            GdsCertRequestHelper.LoadCertificateFromFileAsync(path, cancellation.Token)).ConfigureAwait(false);
        Assert.That(failure, Is.InstanceOf<OperationCanceledException>());
        Assert.That(((OperationCanceledException)failure!).CancellationToken, Is.EqualTo(cancellation.Token));
        Assert.That(await File.ReadAllBytesAsync(path).ConfigureAwait(false), Is.EqualTo(original.RawData));
    }

    [TestCase("none", false)]
    [TestCase("disconnect", false)]
    [TestCase("detach", false)]
    [TestCase("none", true)]
    [TestCase("disconnect", true)]
    [TestCase("detach", true)]
    public async Task CleanupAttemptsDisposeEvenWhenDetachOrDisconnectFails(string failAt, bool disposeFails)
    {
        var order = new List<string>();
        var log = new AdministrationLogCapture();
        var first = new IOException("connection cleanup failed");
        var second = new IOException("dispose failed");
        var client = new Mock<IAsyncDisposable>(MockBehavior.Strict);
        client.Setup(c => c.DisposeAsync()).Returns(() =>
        {
            order.Add("dispose");
            return disposeFails ? ValueTask.FromException(second) : ValueTask.CompletedTask;
        });

        await GdsSessionHelper.SafeDisconnectAndDisposeAsync(client.Object, () =>
        {
            order.Add("detach");
            if (failAt == "detach")
            {
                throw first;
            }
        }, token =>
        {
            Assert.That(token, Is.EqualTo(CancellationToken.None));
            order.Add("disconnect");
            return failAt == "disconnect" ? ValueTask.FromException(first) : ValueTask.CompletedTask;
        }, log, "Owned secondary").ConfigureAwait(false);

        Assert.That(order, Is.EqualTo(failAt == "detach"
            ? s_cleanupAttemptsDisposeEvenWhenDetachOrDisconnectFailsExpected : s_cleanupAttemptsDisposeEvenWhenDetachOrDisconnectFailsExpected2));
        int[] expectedEvents = failAt == "none"
            ? (disposeFails ? new[] { 3401 } : [])
            : (disposeFails ? new[] { 3400, 3401 } : new[] { 3400 });
        Assert.That(log.Entries.Select(e => e.Event.Id), Is.EqualTo(expectedEvents));
        foreach (AdministrationLogCapture.Entry entry in log.Entries)
        {
            Assert.That(entry.Level, Is.EqualTo(LogLevel.Debug));
            Assert.That(entry.Exception, Is.SameAs(entry.Event.Id == 3400 ? first : second));
            Assert.That(entry.State.Single(p => p.Key == "Context").Value, Is.EqualTo("Owned secondary"));
        }
        client.Verify(c => c.DisposeAsync(), Times.Once);
    }

    [Test]
    public async Task NullSecondaryClientDoesNotInvokeAnyLifecycleDelegate()
    {
        var log = new AdministrationLogCapture();
        int callbacks = 0;
        await GdsSessionHelper.SafeDisconnectAndDisposeAsync(null, () => callbacks++, _ =>
        {
            callbacks++;
            return ValueTask.CompletedTask;
        }, log, "No secondary").ConfigureAwait(false);
        Assert.That(callbacks, Is.Zero);
        Assert.That(log.Entries, Is.Empty);
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    private static readonly string[] s_cleanupAttemptsDisposeEvenWhenDetachOrDisconnectFailsExpected =
    [
        "detach",
        "dispose",
    ];
    private static readonly string[] s_cleanupAttemptsDisposeEvenWhenDetachOrDisconnectFailsExpected2 =
    [
        "detach",
        "disconnect",
        "dispose",
    ];
}
