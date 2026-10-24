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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Pcap.KeyLog;
using UaLens.Diagnostics;
using UaLens.Diagnostics.Capture;

namespace UaLens.Tests.Samples
{
    public sealed partial class RepositorySampleLiveTests
    {
        [Test]
        [Explicit("Hosts an owned private-PKI server and records real secure UA-TCP client traffic without key logs.")]
        [Category("RepositorySampleProbe")]
        public async Task PrivateClientCaptureRecordsSecureReadsReplaysOfflineAndNeverOwnsTheSessionAsync()
        {
            string root = Path.Combine(Path.GetTempPath(), "UaLens-owned-capture-" + Guid.NewGuid());
            Directory.CreateDirectory(root);
            DiagnosticCaptureWorkspace? capture = null;
            try
            {
                await WithPrivateCompanionServerAsync(false, async (context, _, _, token) =>
                {
                    DiagnosticCaptureWorkspace current = capture ??
                        throw new AssertionException("The capture module was not created before connecting.");
                    Assert.That(context.Session.Endpoint.SecurityMode, Is.EqualTo(MessageSecurityMode.SignAndEncrypt));
                    ReadResponse read = await context.Session.ReadAsync(null, 0, TimestampsToReturn.Both,
                        [new ReadValueId { NodeId = VariableIds.Server_ServerStatus_CurrentTime,
                            AttributeId = Attributes.Value }], token).ConfigureAwait(false);
                    Assert.That(read.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                    await current.StopAsync().ConfigureAwait(false);
                    DiagnosticCaptureSnapshot recorded = current.Snapshot();
                    Assert.That(recorded.Phase, Is.EqualTo(DiagnosticCapturePhase.Completed), recorded.Status);
                    Assert.That(recorded.Frames, Is.GreaterThan(0));
                    Assert.That(recorded.Packets.Count, Is.EqualTo(recorded.Frames));
                    Assert.That(recorded.PayloadBytes, Is.GreaterThan(0).And.LessThanOrEqualTo(2 * 1024 * 1024));
                    Assert.That(context.Session.Connected, Is.True);
                    ReadResponse after = await context.Session.ReadAsync(null, 0, TimestampsToReturn.Neither,
                        [new ReadValueId { NodeId = VariableIds.Server_ServerStatus_CurrentTime,
                            AttributeId = Attributes.Value }], token).ConfigureAwait(false);
                    Assert.That(after.Results[0].StatusCode, Is.EqualTo(StatusCodes.Good));
                    Assert.That(current.Snapshot().Frames, Is.EqualTo(recorded.Frames));
                    string[] keyLogs = Directory.GetFiles(root, "*.uakeys.json", SearchOption.AllDirectories);
                    Assert.That(keyLogs, Has.Length.EqualTo(1));
                    int keys = 0;
                    await foreach (ChannelKeyMaterial material in
                        new UaKeyLogJsonReader().ReadAllAsync(keyLogs[0], token)
                        .ConfigureAwait(false))
                    {
                        using (material)
                        {
                            keys++;
                        }
                    }
                    Assert.That(keys, Is.Zero, "Packet-data consent must never enable decryption-key collection.");
                    string exported = Path.Combine(root, "owned-capture.pcap");
                    await current.ExportAsync(recorded.Id, exported, true, token).ConfigureAwait(false);
                    ByteString metadata = DiagnosticEvidenceExport.Create(context.Session, null, capture: recorded);
                    string json = System.Text.Encoding.UTF8.GetString(metadata.Span);
                    Assert.That(json, Does.Contain(recorded.Sha256).And.Not.Contain(exported));
                    var file = new FileStream(exported, FileMode.Open, FileAccess.Read, FileShare.Read, 8192,
                        FileOptions.Asynchronous);
                    await using (file.ConfigureAwait(false))
                    {
                        await current.ReplayAsync(file, new(MaxFrames: 512), true, token).ConfigureAwait(false);
                    }
                    DiagnosticCaptureSnapshot replay = current.Snapshot();
                    Assert.That(replay.Kind, Is.EqualTo(DiagnosticCaptureKind.Replay));
                    Assert.That(replay.Sha256, Is.EqualTo(recorded.Sha256));
                    Assert.That(replay.Packets.ToList(), Is.EqualTo(recorded.Packets.ToList()));
                    Assert.That(context.Session.Connected, Is.True);
                }, async (connection, token) =>
                {
                    capture = new DiagnosticCaptureWorkspace(DefaultTelemetry.Create(static _ => { }),
                        connection.ConfiguredBackend?.Transports.CaptureRegistry
                            ?? throw new AssertionException("The default UA-TCP binding is not capture-aware."),
                        connection, newFolder: () => Path.Combine(root, Guid.NewGuid().ToString("N")));
                    await capture.StartAsync("client",
                        new DiagnosticCaptureLimits(30, 512, 2 * 1024 * 1024), true, token).ConfigureAwait(false);
                }).ConfigureAwait(false);
            }
            finally
            {
                try
                {
                    if (capture is not null)
                    {
                        await capture.DisposeAsync().ConfigureAwait(false);
                    }
                }
                finally
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }
    }
}
