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
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Gds.Client;
using Opc.Ua.Security.Certificates;
using UaLens.Plugins.GdsPush;
using UaLens.Tests.Desktop;

namespace UaLens.Tests.Administration;

[TestFixture]
[Platform("Win,Linux")]
[NonParallelizable]
public sealed class CertificateRequestDialogWorkflowTests
{
    [Test]
    public Task MissingGroupDoesNotAdvanceOrCallTheServerAndCancelReturnsNull()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var client = new Mock<IGdsClientLike>(MockBehavior.Strict);
            var dialog = new CertificateRequestDialog(client.Object, []);
            Task<CertificateRequestResult?> prompt =
                dialog.ShowDialog<CertificateRequestResult?>(DesktopInteraction.Owner)
                    .WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                Click(dialog, "NextButton");
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ErrorLabel").Text,
                    Is.EqualTo("Pick a certificate group first."));
                Assert.That(DesktopInteraction.Control<Control>(dialog, "Step1").IsVisible, Is.True);
                Assert.That(prompt.IsCompleted, Is.False);
                Click(dialog, "CancelButton");
                Assert.That(await prompt.ConfigureAwait(true), Is.Null);
                client.VerifyNoOtherCalls();
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    public Task GroupSubjectAndBackNavigationProduceTheExactSelectedCertificateRequest(int selected)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            CertificateGroupChoice[] groups = Groups();
            CertificateGroupChoice group = groups[selected];
            byte[] csr = TemporaryCertificateStores.CreateSigningRequest();
            var client = CsrClient(group, csr, " CN=production.example.test,O=Fixture ", regenerate: true);
            var dialog = new CertificateRequestDialog(client.Object, groups);
            Task<CertificateRequestResult?> prompt =
                dialog.ShowDialog<CertificateRequestResult?>(DesktopInteraction.Owner)
                    .WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                DesktopInteraction.Control<ComboBox>(dialog, "GroupBox").SelectedIndex = selected;
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "TypeLabel").Text,
                    Is.EqualTo(group.CertificateTypeId.ToString()));
                Click(dialog, "NextButton");
                DesktopInteraction.Control<TextBox>(dialog, "SubjectBox").Text = "  ";
                Click(dialog, "NextButton");
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ErrorLabel").Text,
                    Is.EqualTo("Subject name is required."));
                Assert.That(client.Invocations, Is.Empty);
                DesktopInteraction.Control<TextBox>(dialog, "SubjectBox").Text =
                    " CN=production.example.test,O=Fixture ";
                DesktopInteraction.Control<CheckBox>(dialog, "RegenKeyBox").IsChecked = true;
                Click(dialog, "BackButton");
                Assert.That(DesktopInteraction.Control<Control>(dialog, "Step1").IsVisible, Is.True);
                Click(dialog, "NextButton");
                Assert.That(DesktopInteraction.Control<TextBox>(dialog, "SubjectBox").Text,
                    Is.EqualTo(" CN=production.example.test,O=Fixture "));

                await GenerateAsync(dialog).ConfigureAwait(true);

                string pem = DesktopInteraction.Control<TextBox>(dialog, "CsrBox").Text!;
                string[] lines = pem.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                Assert.That(lines[0], Is.EqualTo("-----BEGIN CERTIFICATE REQUEST-----"));
                Assert.That(string.Concat(lines.Skip(1).Take(lines.Length - 2)),
                    Is.EqualTo(Convert.ToBase64String(csr)));
                Assert.That(lines[^1], Is.EqualTo("-----END CERTIFICATE REQUEST-----"));
                Assert.That(DesktopInteraction.Control<Control>(dialog, "Step2").IsVisible, Is.False);
                Click(dialog, "CancelButton");
                CertificateRequestResult result = (await prompt.ConfigureAwait(true))!;
                Assert.That(result.GroupName, Is.EqualTo(selected == 0 ? "Application group" : "HTTPS group"));
                Assert.That(result.Csr, Is.EqualTo(csr));
                Assert.That(result.Applied, Is.False);
                client.VerifyAll();
                client.VerifyNoOtherCalls();
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [Test]
    public Task CsrFailureIsRetryableAndPendingRetryDisablesNavigationUntilItsCompletion()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            CertificateGroupChoice group = Groups()[0];
            byte[] csr = TemporaryCertificateStores.CreateSigningRequest();
            var response = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            var client = new Mock<IGdsClientLike>(MockBehavior.Strict);
            client.SetupSequence(c => c.CreateSigningRequestAsync(
                group.CertificateGroupId, group.CertificateTypeId, "CN=Server", false,
                It.Is<byte[]>(b => b.Length == 0), CancellationToken.None))
                .ThrowsAsync(new IOException("CSR rejected"))
                .Returns(response.Task);
            var dialog = new CertificateRequestDialog(client.Object, [group]);
            Task<CertificateRequestResult?> prompt =
                dialog.ShowDialog<CertificateRequestResult?>(DesktopInteraction.Owner)
                    .WaitAsync(TimeSpan.FromSeconds(15));
            Task? generated = null;
            try
            {
                DesktopInteraction.Control<CheckBox>(dialog, "RegenKeyBox").IsChecked = false;
                Click(dialog, "NextButton");
                TextBlock error = DesktopInteraction.Control<TextBlock>(dialog, "ErrorLabel");
                await DesktopInteraction.ChangedAsync(error, () => error.Text == "Step 2 failed: CSR rejected", () =>
                {
                    Click(dialog, "NextButton");
                    return Task.CompletedTask;
                }).ConfigureAwait(true);
                Assert.That(DesktopInteraction.Control<Control>(dialog, "Step2").IsVisible, Is.True);
                generated = GenerateAsync(dialog);
                Assert.That(DesktopInteraction.Control<Button>(dialog, "NextButton").IsEnabled, Is.False);
                Assert.That(DesktopInteraction.Control<Button>(dialog, "BackButton").IsEnabled, Is.False);
                Assert.That(prompt.IsCompleted, Is.False);
                Assert.That(client.Invocations, Has.Count.EqualTo(2));
                response.SetResult(csr);
                await generated.ConfigureAwait(true);
                Assert.That(DesktopInteraction.Control<Button>(dialog, "NextButton").IsEnabled, Is.True);
                Assert.That(error.IsVisible, Is.False);
                Click(dialog, "CancelButton");
                CertificateRequestResult result = (await prompt.ConfigureAwait(true))!;
                Assert.That(result.Applied, Is.False);
                Assert.That(result.Csr, Is.EqualTo(csr));
                client.Verify(c => c.CreateSigningRequestAsync(
                    group.CertificateGroupId, group.CertificateTypeId, "CN=Server", false,
                    It.Is<byte[]>(b => b.Length == 0), CancellationToken.None), Times.Exactly(2));
            }
            finally
            {
                response.TrySetResult(csr);
                if (generated is not null)
                {
                    await generated.ConfigureAwait(true);
                }
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [Test]
    public Task CancelDuringCsrReturnsNullAndTheOwnedPendingRequestIsDrainedBeforeLeavingTheHost()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            CertificateGroupChoice group = Groups()[0];
            byte[] csr = TemporaryCertificateStores.CreateSigningRequest();
            var response = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            var client = CsrClient(group, csr);
            client.Setup(c => c.CreateSigningRequestAsync(group.CertificateGroupId, group.CertificateTypeId,
                "CN=Server", false, It.Is<byte[]>(b => b.Length == 0), CancellationToken.None)).Returns(response.Task);
            var dialog = new CertificateRequestDialog(client.Object, [group]);
            Task<CertificateRequestResult?> prompt =
                dialog.ShowDialog<CertificateRequestResult?>(DesktopInteraction.Owner)
                    .WaitAsync(TimeSpan.FromSeconds(15));
            DesktopInteraction.Control<CheckBox>(dialog, "RegenKeyBox").IsChecked = false;
            Click(dialog, "NextButton");
            Task generated = GenerateAsync(dialog);
            try
            {
                Assert.That(DesktopInteraction.Control<Button>(dialog, "NextButton").IsEnabled, Is.False);
                Click(dialog, "CancelButton");
                Assert.That(await prompt.ConfigureAwait(true), Is.Null);
                Assert.That(response.Task.IsCompleted, Is.False);
            }
            finally
            {
                response.TrySetResult(csr);
                await generated.ConfigureAwait(true);
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
            Assert.That(client.Invocations, Has.Count.EqualTo(1));
            Assert.That(DesktopInteraction.Control<Button>(dialog, "NextButton").IsEnabled, Is.True);
        });
    }

    [TestCase(0, false)]
    [TestCase(1, true)]
    public Task SignedBundleUpdatesTheExactGroupTypeAndOrderedChainWithoutImplicitApplyChanges(
        int selected, bool applyRequired)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            CertificateGroupChoice group = Groups()[selected];
            byte[] csr = TemporaryCertificateStores.CreateSigningRequest();
            using Certificate leaf = TemporaryCertificateStores.CreateCertificate("CN=Signed application");
            using Certificate issuer = TemporaryCertificateStores.CreateCertificate("CN=Signing intermediate");
            using Certificate root = TemporaryCertificateStores.CreateCertificate("CN=Signing root");
            byte[] leafDer = leaf.RawData;
            byte[] issuerDer = issuer.RawData;
            byte[] rootDer = root.RawData;
            var protocol = new Mock<IServerPushConfigurationClient>(MockBehavior.Strict);
            protocol.Setup(c => c.CreateSigningRequestAsync(
                group.CertificateGroupId, group.CertificateTypeId, "CN=Server", false,
                It.Is<ByteString>(b => b.IsEmpty), CancellationToken.None))
                .Returns(new ValueTask<ByteString>(csr.ToByteString()));
            protocol.Setup(c => c.UpdateCertificateAsync(
                group.CertificateGroupId, group.CertificateTypeId,
                It.Is<ByteString>(b => b.Memory.ToArray().SequenceEqual(leafDer)), string.Empty,
                It.Is<ByteString>(b => b.IsEmpty),
                It.Is<ArrayOf<ByteString>>(chain => chain.Count == 2 &&
                    chain[0].Memory.ToArray().SequenceEqual(issuerDer) &&
                    chain[1].Memory.ToArray().SequenceEqual(rootDer)),
                CancellationToken.None)).Returns(new ValueTask<bool>(applyRequired));
            var dialog = new CertificateRequestDialog(new PushClientAdapter(protocol.Object), [group]);
            Task<CertificateRequestResult?> prompt =
                dialog.ShowDialog<CertificateRequestResult?>(DesktopInteraction.Owner)
                    .WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                await SignedStepAsync(dialog).ConfigureAwait(true);
                Assert.That(protocol.Invocations, Has.Count.EqualTo(1),
                    "Generating a CSR must not update a certificate.");
                DesktopInteraction.Control<TextBox>(dialog, "SignedBox").Text =
                    "operator note\n" + Pem(leaf) + "\n" + Pem(issuer) + Pem(root) + "\nend note";
                DesktopInteraction.Control<CheckBox>(dialog, "CallApplyChangesBox").IsChecked = true;
                Click(dialog, "NextButton");
                CertificateRequestResult result = (await prompt.ConfigureAwait(true))!;
                Assert.That(result.Applied, Is.True);
                Assert.That(result.GroupName, Is.EqualTo(selected == 0 ? "Application group" : "HTTPS group"));
                Assert.That(result.Csr, Is.EqualTo(csr));
                protocol.VerifyAll();
                protocol.Verify(c => c.ApplyChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
                protocol.VerifyNoOtherCalls();
                Assert.That(leaf.HasPrivateKey, Is.False);
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [TestCase("blank")]
    [TestCase("markers")]
    [TestCase("base64")]
    [TestCase("service")]
    [TestCase("canceled")]
    public Task SignedInputOrUpdateFailureDoesNotClaimAppliedAndAllowsAnExplicitRetry(string failureKind)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            CertificateGroupChoice group = Groups()[0];
            byte[] csr = TemporaryCertificateStores.CreateSigningRequest();
            using Certificate certificate = TemporaryCertificateStores.CreateCertificate("CN=Retry update");
            byte[] der = certificate.RawData;
            var client = CsrClient(group, csr);
            bool protocolFailure = failureKind is "service" or "canceled";
            int updates = 0;
            client.Setup(c => c.ApplyUpdatedCertificateAsync(
                group.CertificateGroupId, group.CertificateTypeId,
                It.Is<byte[]>(b => b.SequenceEqual(der)), It.Is<IReadOnlyList<byte[]>>(b => b.Count == 0),
                string.Empty, It.Is<byte[]>(b => b.Length == 0), CancellationToken.None)).Returns(() =>
                {
                    updates++;
                    if (protocolFailure && updates == 1)
                    {
                        Exception error = failureKind == "service"
                            ? new IOException("update rejected") : new OperationCanceledException("update canceled");
                        return Task.FromException(error);
                    }
                    return Task.CompletedTask;
                });
            var dialog = new CertificateRequestDialog(client.Object, [group]);
            Task<CertificateRequestResult?> prompt =
                dialog.ShowDialog<CertificateRequestResult?>(DesktopInteraction.Owner)
                    .WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                await SignedStepAsync(dialog).ConfigureAwait(true);
                TextBox signed = DesktopInteraction.Control<TextBox>(dialog, "SignedBox");
                signed.Text = failureKind switch
                {
                    "blank" => " ",
                    "markers" => "-----BEGIN CERTIFICATE-----AQID",
                    "base64" => "-----BEGIN CERTIFICATE-----invalid!-----END CERTIFICATE-----",
                    _ => Pem(certificate)
                };
                TextBlock error = DesktopInteraction.Control<TextBlock>(dialog, "ErrorLabel");
                await DesktopInteraction.ChangedAsync(error, () => error.IsVisible, () =>
                {
                    Click(dialog, "NextButton");
                    return Task.CompletedTask;
                }).ConfigureAwait(true);
                Assert.That(prompt.IsCompleted, Is.False);
                Assert.That(DesktopInteraction.Control<Control>(dialog, "Step4").IsVisible, Is.True);
                Assert.That(DesktopInteraction.Control<Button>(dialog, "NextButton").IsEnabled, Is.True);
                Assert.That(updates, Is.EqualTo(protocolFailure ? 1 : 0));
                Assert.That(error.Text, failureKind == "blank"
                    ? Is.EqualTo("Paste the signed certificate PEM.") : Does.StartWith("Step 4 failed:"));
                signed.Text = Pem(certificate);
                Click(dialog, "NextButton");
                CertificateRequestResult result = (await prompt.ConfigureAwait(true))!;
                Assert.That(result.Applied, Is.True);
                Assert.That(result.Csr, Is.EqualTo(csr));
                Assert.That(updates, Is.EqualTo(protocolFailure ? 2 : 1));
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [Test]
    public Task CancelPendingUpdateReturnsCsrOnlyAndDrainsTheNoncancelableOperation()
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            CertificateGroupChoice group = Groups()[0];
            byte[] csr = TemporaryCertificateStores.CreateSigningRequest();
            using Certificate certificate = TemporaryCertificateStores.CreateCertificate("CN=Pending update");
            byte[] der = certificate.RawData;
            var response = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var client = CsrClient(group, csr);
            client.Setup(c => c.ApplyUpdatedCertificateAsync(
                group.CertificateGroupId, group.CertificateTypeId,
                It.Is<byte[]>(b => b.SequenceEqual(der)), It.Is<IReadOnlyList<byte[]>>(b => b.Count == 0),
                string.Empty, It.Is<byte[]>(b => b.Length == 0), CancellationToken.None)).Returns(response.Task);
            var dialog = new CertificateRequestDialog(client.Object, [group]);
            Task<CertificateRequestResult?> prompt =
                dialog.ShowDialog<CertificateRequestResult?>(DesktopInteraction.Owner)
                    .WaitAsync(TimeSpan.FromSeconds(15));
            Task? drained = null;
            try
            {
                await SignedStepAsync(dialog).ConfigureAwait(true);
                DesktopInteraction.Control<TextBox>(dialog, "SignedBox").Text = Pem(certificate);
                Click(dialog, "NextButton");
                Button next = DesktopInteraction.Control<Button>(dialog, "NextButton");
                Assert.That(next.IsEnabled, Is.False);
                Assert.That(DesktopInteraction.Control<Button>(dialog, "BackButton").IsEnabled, Is.False);
                Click(dialog, "CancelButton");
                CertificateRequestResult result = (await prompt.ConfigureAwait(true))!;
                Assert.That(result.Applied, Is.False);
                Assert.That(result.Csr, Is.EqualTo(csr));
                Assert.That(response.Task.IsCompleted, Is.False);
                drained = DesktopInteraction.ChangedAsync(next, () => next.IsEnabled, () =>
                {
                    response.TrySetResult();
                    return Task.CompletedTask;
                });
                await drained.ConfigureAwait(true);
                Assert.That(result.Applied, Is.False,
                    "Closing the form already returned an immutable CSR-only outcome.");
                client.VerifyAll();
                client.VerifyNoOtherCalls();
            }
            finally
            {
                if (!response.Task.IsCompleted)
                {
                    Button next = DesktopInteraction.Control<Button>(dialog, "NextButton");
                    drained = DesktopInteraction.ChangedAsync(next, () => next.IsEnabled, () =>
                    {
                        response.TrySetResult();
                        return Task.CompletedTask;
                    });
                }
                if (drained is not null)
                {
                    await drained.ConfigureAwait(true);
                }
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [TestCase(0, false)]
    [TestCase(1, true)]
    [TestCase(2, false)]
    [TestCase(0, true)]
    public Task AddCertificateReturnsGeneratedDerInTheChosenBucketOnlyOnAccept(int initial, bool issuer)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            using Certificate certificate = TemporaryCertificateStores.CreateCertificate("CN=Trust candidate");
            var dialog = new AddCertificateDialog((TrustListBucket)initial);
            Task<AddCertificateResult?> prompt = dialog.ShowDialog<AddCertificateResult?>(DesktopInteraction.Owner)
                .WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                Assert.That(DesktopInteraction.Control<RadioButton>(dialog, "DestIssuer").IsChecked,
                    Is.EqualTo(initial == 1));
                Assert.That(DesktopInteraction.Control<RadioButton>(dialog, "DestTrusted").IsChecked,
                    Is.EqualTo(initial != 1));
                DesktopInteraction.Control<RadioButton>(dialog, issuer ? "DestIssuer" : "DestTrusted").IsChecked = true;
                DesktopInteraction.Control<TextBox>(dialog, "PemBox").Text = Pem(certificate);
                Assert.That(prompt.IsCompleted, Is.False);
                Click(dialog, "OkButton");
                AddCertificateResult result = (await prompt.ConfigureAwait(true))!;
                using (result.Certificate)
                {
                    Assert.That(result.Bucket, Is.EqualTo(issuer ? TrustListBucket.Issuer : TrustListBucket.Trusted));
                    Assert.That(result.Certificate.RawData, Is.EqualTo(certificate.RawData));
                    Assert.That(result.Certificate.Subject, Is.EqualTo("CN=Trust candidate"));
                    Assert.That(result.Certificate.HasPrivateKey, Is.False);
                }
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    [TestCase("", "Pick a file or paste a PEM block.")]
    [TestCase("not a PEM certificate", "Parse failed: Missing BEGIN CERTIFICATE marker.")]
    public Task AddCertificateInvalidInputRemainsOpenAndCancelPublishesNoCertificate(string text, string expected)
    {
        return AvaloniaDesktopTestHost.RunAsync(async () =>
        {
            var dialog = new AddCertificateDialog(TrustListBucket.Trusted);
            Task<AddCertificateResult?> prompt = dialog.ShowDialog<AddCertificateResult?>(DesktopInteraction.Owner)
                .WaitAsync(TimeSpan.FromSeconds(15));
            try
            {
                DesktopInteraction.Control<TextBox>(dialog, "PemBox").Text = text;
                Click(dialog, "OkButton");
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ErrorLabel").Text, Is.EqualTo(expected));
                Assert.That(prompt.IsCompleted, Is.False);
                Assert.That(DesktopInteraction.Control<TextBlock>(dialog, "ErrorLabel").IsVisible, Is.True);
                Click(dialog, "CancelButton");
                Assert.That(await prompt.ConfigureAwait(true), Is.Null);
            }
            finally
            {
                dialog.Close();
                await prompt.ConfigureAwait(true);
            }
        });
    }

    private static CertificateGroupChoice[] Groups()
    {
        return [
            new("Application group", new NodeId(7101),
                ObjectTypeIds.RsaSha256ApplicationCertificateType, "Application"),
            new("HTTPS group", new NodeId(7102), ObjectTypeIds.HttpsCertificateType, "HTTPS")
        ];
    }

    private static Mock<IGdsClientLike> CsrClient(
        CertificateGroupChoice group, byte[] csr, string subject = "CN=Server", bool regenerate = false)
    {
        var client = new Mock<IGdsClientLike>(MockBehavior.Strict);
        client.Setup(c => c.CreateSigningRequestAsync(
            group.CertificateGroupId, group.CertificateTypeId, subject, regenerate,
            It.Is<byte[]>(b => b.Length == 0), CancellationToken.None)).ReturnsAsync(csr);
        return client;
    }

    private static Task GenerateAsync(CertificateRequestDialog dialog)
    {
        Control step = DesktopInteraction.Control<Control>(dialog, "Step3");
        return DesktopInteraction.ChangedAsync(step, () => step.IsVisible, () =>
        {
            Click(dialog, "NextButton");
            return Task.CompletedTask;
        });
    }

    private static async Task SignedStepAsync(CertificateRequestDialog dialog)
    {
        DesktopInteraction.Control<CheckBox>(dialog, "RegenKeyBox").IsChecked = false;
        Click(dialog, "NextButton");
        await GenerateAsync(dialog).ConfigureAwait(true);
        Click(dialog, "NextButton");
        Assert.That(DesktopInteraction.Control<Control>(dialog, "Step4").IsVisible, Is.True);
    }

    private static string Pem(Certificate certificate)
    {
        return Encoding.ASCII.GetString(PEMWriter.ExportCertificateAsPEM(certificate));
    }

    private static void Click(Window dialog, string name)
    {
        DesktopInteraction.Click(DesktopInteraction.Control<Button>(dialog, name));
    }
}
