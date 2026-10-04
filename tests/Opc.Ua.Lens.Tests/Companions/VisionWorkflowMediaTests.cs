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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Vision;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using static UaLens.Tests.Companions.CellProviderTestSession;
using static UaLens.Tests.Companions.VisionWorkflowTestSupport;

namespace UaLens.Tests.Companions
{
    [TestFixture]
    public sealed class VisionWorkflowMediaTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public async Task ClipUsesExactInputsAndReturnsVerifiedEvidenceWithoutFetchingUri(bool inline)
        {
            var fixture = new VisionWorkflowTestSupport();
            VisionImageReferenceDataType image = Image();
            image.Uri = "https://media.invalid/never-open?lease=redacted-marker";
            fixture.OnCall = (request, _) =>
            {
                Assert.That(request.ObjectId, Is.EqualTo(fixture.Media));
                Assert.That(request.MethodId, Is.EqualTo(fixture.Fixture.Children[(fixture.Media, "GetClip")]));
                Assert.That(request.InputArguments.Count, Is.EqualTo(5));
                Assert.That(request.InputArguments[0].TryGetValue(out NodeId endpoint), Is.True);
                Assert.That(endpoint, Is.EqualTo(fixture.Clip));
                Assert.That(request.InputArguments[1].TryGetValue(out string? id), Is.True);
                Assert.That(id, Is.EqualTo("result-7"));
                Assert.That(request.InputArguments[2].TryGetValue(out DateTimeUtc time), Is.True);
                Assert.That(time, Is.EqualTo(new DateTimeUtc(fixture.UtcNow)));
                Assert.That(request.InputArguments[3].TryGetValue(out int format), Is.True);
                Assert.That(format, Is.EqualTo((int)VisionClipFormatEnum.Jpeg));
                Assert.That(request.InputArguments[4].TryGetValue(out bool requested), Is.True);
                Assert.That(requested, Is.True);
                return ValueTask.FromResult(new CallMethodResult
                {
                    OutputArguments =
                    [
                        Variant.FromStructure(image), Variant.From(fixture.Clip),
                        Variant.From(inline ? ByteString.From(1, 2, 3, 4) : ByteString.Empty)
                    ]
                });
            };

            CompanionOperationResult result = await RunAsync(fixture, "get-clip").ConfigureAwait(false);

            Assert.That(Field(result.Values, "Endpoint").TryGetValue(out NodeId endpoint), Is.True);
            Assert.That(endpoint, Is.EqualTo(fixture.Clip));
            Assert.That(Field(result.Values, "Frame width").TryGetValue(out uint width), Is.True);
            Assert.That(width, Is.EqualTo(640u));
            Assert.That(Field(result.Values, "Inline bytes returned").TryGetValue(out int bytes), Is.True);
            Assert.That(bytes, Is.EqualTo(inline ? 4 : 0));
            Assert.That(string.Join("\n", result.Values.ToList().Select(value => value.Text)),
                Does.Not.Contain("redacted-marker").And.Not.Contain("https://media.invalid"));
            Assert.That(fixture.Calls.Count, Is.EqualTo(1));
            fixture.VerifyBorrowedSession();
        }

        [TestCase("endpoint")]
        [TestCase("format")]
        [TestCase("width")]
        [TestCase("height")]
        [TestCase("pixels")]
        [TestCase("timestamp")]
        [TestCase("digest")]
        [TestCase("algorithm")]
        [TestCase("size")]
        [TestCase("byte-cap")]
        [TestCase("uri")]
        [TestCase("unsolicited-inline")]
        public async Task ClipRejectsMismatchedOrUnboundedReturnedEvidence(string fault)
        {
            var fixture = new VisionWorkflowTestSupport();
            VisionImageReferenceDataType image = Image();
            NodeId endpoint = fixture.Clip;
            switch (fault)
            {
                case "endpoint":
                    endpoint = fixture.Stream;
                    break;
                case "format":
                    image.Format = VisionClipFormatEnum.Png;
                    break;
                case "width":
                    image.Width = 0;
                    break;
                case "height":
                    image.Height = 8193;
                    break;
                case "pixels":
                    image.Width = 4096;
                    image.Height = 4097;
                    break;
                case "timestamp":
                    image.Timestamp = default;
                    break;
                case "digest":
                    image.Digest = new ByteString(new byte[32]);
                    break;
                case "algorithm":
                    image.DigestAlgorithm = "SHA-1";
                    break;
                case "size":
                    image.SizeBytes = 3;
                    break;
                case "byte-cap":
                    image.SizeBytes = 1_048_577;
                    break;
                case "uri":
                    image.Uri = "relative";
                    break;
                case "unsolicited-inline":
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fault));
            }
            fixture.OnCall = (_, _) => ValueTask.FromResult(new CallMethodResult
            {
                OutputArguments =
                    [Variant.FromStructure(image), Variant.From(endpoint), Variant.From(ByteString.From(1, 2, 3, 4))]
            });
            ArrayOf<CompanionValue> inputs = fixture.Inputs("get-clip");
            if (fault == "unsolicited-inline")
            {
                inputs = Replace(inputs, "requestInline", Variant.From(false));
            }
            await Assert.ThatAsync(() => RunAsync(fixture, "get-clip", inputs),
                Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);
            Assert.That(fixture.Calls.Count, Is.EqualTo(1));
            fixture.VerifyBorrowedSession();
        }

        [TestCase("none")]
        [TestCase("endpoint")]
        [TestCase("protocol")]
        [TestCase("expiry")]
        [TestCase("uri")]
        [TestCase("large-token")]
        [TestCase("cancel")]
        [TestCase("review-expiry")]
        public async Task AcquiredLeaseIsReleasedExactlyOnceOnItsOwningSession(string fault)
        {
            var fixture = new VisionWorkflowTestSupport();
            using var cancellation = new CancellationTokenSource();
            VisionStreamSessionDataType lease = Lease(fixture);
            NodeId endpoint = fixture.Stream;
            switch (fault)
            {
                case "endpoint":
                    endpoint = fixture.Clip;
                    break;
                case "protocol":
                    lease.Protocol = VisionStreamProtocolEnum.Other;
                    break;
                case "expiry":
                    lease.ExpiresAt = new DateTimeUtc(fixture.UtcNow);
                    break;
                case "uri":
                    lease.Uri = "relative";
                    break;
                case "large-token":
                    lease.SessionToken = new ByteString(new byte[4097]);
                    break;
            }
            fixture.OnCall = async (request, token) =>
            {
                if (request.MethodId == fixture.Fixture.Children[(fixture.Media, "GetStreamEndpoint")])
                {
                    if (fault == "cancel")
                    {
                        await cancellation.CancelAsync().ConfigureAwait(false);
                    }
                    if (fault == "review-expiry")
                    {
                        fixture.UtcNow += TimeSpan.FromMinutes(5);
                    }
                    return new CallMethodResult
                    {
                        OutputArguments = [Variant.FromStructure(lease), Variant.From(endpoint)]
                    };
                }
                Assert.That(request.MethodId,
                    Is.EqualTo(fixture.Fixture.Children[(fixture.Media, "ReleaseStreamEndpoint")]));
                Assert.That(token.IsCancellationRequested, Is.False);
                Assert.That(token, Is.Not.EqualTo(fixture.CallTokens[0]));
                Assert.That(request.InputArguments[0].TryGetValue(out ByteString released), Is.True);
                Assert.That(released, Is.EqualTo(lease.SessionToken));
                return new CallMethodResult();
            };
            if (fault is "none" or "review-expiry")
            {
                CompanionOperationResult result = await RunAsync(
                    fixture, "probe-stream", cancellationToken: cancellation.Token).ConfigureAwait(false);
                Assert.That(Field(result.Values, "Lease released").TryGetValue(out bool released), Is.True);
                Assert.That(released, Is.True);
                Assert.That(string.Join("\n", result.Values.ToList().Select(value => value.Text)),
                    Does.Not.Contain("secret-lease-marker"));
            }
            else if (fault == "cancel")
            {
                await Assert.ThatAsync(() => RunAsync(fixture, "probe-stream", cancellationToken: cancellation.Token),
                    Throws.InvalidOperationException.With.Message.Contains("unknown")).ConfigureAwait(false);
            }
            else
            {
                await Assert.ThatAsync(() => RunAsync(fixture, "probe-stream", cancellationToken: cancellation.Token),
                    Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);
            }
            Assert.That(fixture.Calls.Count, Is.EqualTo(2));
            await fixture.Timers.WaitForTimerAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            fixture.VerifyBorrowedSession();
        }

        [Test]
        public async Task EmptyLeaseTokenCannotBeReportedAsReleased()
        {
            var fixture = new VisionWorkflowTestSupport();
            VisionStreamSessionDataType lease = Lease(fixture);
            lease.SessionToken = default;
            fixture.OnCall = (_, _) => ValueTask.FromResult(new CallMethodResult
            {
                OutputArguments = [Variant.FromStructure(lease), Variant.From(fixture.Stream)]
            });

            await Assert.ThatAsync(() => RunAsync(fixture, "probe-stream"),
                Throws.TypeOf<ServiceResultException>().With.Message.Contains("release cannot be established"))
                .ConfigureAwait(false);
            Assert.That(fixture.Calls.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task InvalidLeaseAndFailedReleaseRetainBothErrors()
        {
            var fixture = new VisionWorkflowTestSupport();
            VisionStreamSessionDataType lease = Lease(fixture);
            lease.Uri = "invalid";
            fixture.OnCall = (request, _) => ValueTask.FromResult(
                request.MethodId == fixture.Fixture.Children[(fixture.Media, "GetStreamEndpoint")]
                    ? new CallMethodResult
                    {
                        OutputArguments = [Variant.FromStructure(lease), Variant.From(fixture.Stream)]
                    }
                    : new CallMethodResult { StatusCode = StatusCodes.BadUserAccessDenied });
            AggregateException? failure = null;
            try
            {
                await RunAsync(fixture, "probe-stream").ConfigureAwait(false);
            }
            catch (AggregateException error)
            {
                failure = error;
            }

            Assert.That(failure, Is.Not.Null);
            Assert.That(failure!.InnerExceptions, Has.Count.EqualTo(2));
            Assert.That(failure.InnerExceptions[0], Is.TypeOf<ServiceResultException>());
            Assert.That(((ServiceResultException)failure.InnerExceptions[0]).StatusCode,
                Is.EqualTo(StatusCodes.BadTypeMismatch));
            Assert.That(failure.InnerExceptions[1], Is.TypeOf<ServiceResultException>());
            Assert.That(((ServiceResultException)failure.InnerExceptions[1]).StatusCode,
                Is.EqualTo(StatusCodes.BadUserAccessDenied));
            Assert.That(fixture.Calls.Count, Is.EqualTo(2));
        }

        [Test]
        public async Task ReleaseHasAnIndependentFiveSecondDeadline()
        {
            var fixture = new VisionWorkflowTestSupport();
            fixture.OnCall = (request, token) =>
                request.MethodId == fixture.Fixture.Children[(fixture.Media, "GetStreamEndpoint")]
                    ? ValueTask.FromResult(new CallMethodResult
                    {
                        OutputArguments = [Variant.FromStructure(Lease(fixture)), Variant.From(fixture.Stream)]
                    })
                    : new ValueTask<CallMethodResult>(PendingAsync(token));
            Task<CompanionOperationResult> operation = RunAsync(fixture, "probe-stream");
            await fixture.Timers.WaitForTimerAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            fixture.Advance(TimeSpan.FromSeconds(5) - TimeSpan.FromTicks(1));
            Assert.That(operation.IsCompleted, Is.False);
            fixture.Advance(TimeSpan.FromTicks(1));

            await Assert.ThatAsync(() => operation.WaitAsync(TimeSpan.FromSeconds(5)),
                Throws.InvalidOperationException.With.Message.Contains("unknown")).ConfigureAwait(false);
            Assert.That(fixture.CallTokens[1].IsCancellationRequested, Is.True);
            fixture.VerifyBorrowedSession();
        }

        [TestCase("get-clip")]
        [TestCase("configure-stream")]
        [TestCase("select-endpoints")]
        public async Task AcceptedRejectedAndAmbiguousDispatchesAreNeverReplayed(string operation)
        {
            foreach (StatusCode status in new[]
            {
                StatusCodes.Good, StatusCodes.BadInvalidState, StatusCodes.BadTimeout
            })
            {
                var fixture = new VisionWorkflowTestSupport();
                var provider = new VisionCompanionProvider(timeProvider: fixture.Clock.Object);
                fixture.OnCall = (_, _) => ValueTask.FromResult(new CallMethodResult
                {
                    StatusCode = status,
                    OutputArguments = operation == "get-clip" && StatusCode.IsGood(status)
                        ? [Variant.FromStructure(Image()), Variant.From(fixture.Clip), Variant.From(ByteString.Empty)]
                        : []
                });
                CompanionTaskInput prepared = await provider.PrepareInputAsync(
                    fixture.Context, fixture.SensorTarget, operation, fixture.Inputs(operation), CancellationToken.None)
                    .ConfigureAwait(false);
                if (StatusCode.IsGood(status))
                {
                    await provider.ExecutePreparedAsync(fixture.Context, fixture.SensorTarget, operation,
                        prepared, null, CancellationToken.None).ConfigureAwait(false);
                }
                else if (status == StatusCodes.BadTimeout)
                {
                    await Assert.ThatAsync(() => provider.ExecutePreparedAsync(
                        fixture.Context, fixture.SensorTarget, operation, prepared, null, CancellationToken.None)
                        .AsTask(),
                        Throws.InvalidOperationException.With.Message.Contains("unknown")).ConfigureAwait(false);
                }
                else
                {
                    await Assert.ThatAsync(() => provider.ExecutePreparedAsync(
                        fixture.Context, fixture.SensorTarget, operation, prepared, null, CancellationToken.None)
                        .AsTask(),
                        Throws.TypeOf<ServiceResultException>()).ConfigureAwait(false);
                }
                await Assert.ThatAsync(() => provider.ExecutePreparedAsync(fixture.Context, fixture.SensorTarget,
                    operation, prepared, null, CancellationToken.None).AsTask(),
                    Throws.InvalidOperationException.With.Message.Contains("already dispatched")).ConfigureAwait(false);
                Assert.That(fixture.Calls.Count, Is.EqualTo(1));
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task SelectLeavesExactlyOnePreferenceUnchanged(bool streamUnchanged)
        {
            var fixture = new VisionWorkflowTestSupport();
            ArrayOf<CompanionValue> inputs = Replace(fixture.Inputs("select-endpoints"),
                streamUnchanged ? "stream" : "clip", Variant.From(NodeId.Null));
            fixture.OnCall = (request, _) =>
            {
                Assert.That(request.InputArguments.Count, Is.EqualTo(2));
                Assert.That(request.InputArguments[0].TryGetValue(out NodeId stream), Is.True);
                Assert.That(stream, Is.EqualTo(streamUnchanged ? NodeId.Null : fixture.Stream));
                Assert.That(request.InputArguments[1].TryGetValue(out NodeId clip), Is.True);
                Assert.That(clip, Is.EqualTo(streamUnchanged ? fixture.Clip : NodeId.Null));
                return ValueTask.FromResult(new CallMethodResult());
            };
            await RunAsync(fixture, "select-endpoints", inputs).ConfigureAwait(false);
            Assert.That(fixture.Calls.Count, Is.EqualTo(1));
        }

        [Test]
        public async Task ConfigureStreamPreservesEveryReviewedWireArgument()
        {
            var fixture = new VisionWorkflowTestSupport();
            fixture.OnCall = (request, _) =>
            {
                Assert.That(request.ObjectId, Is.EqualTo(fixture.Media));
                Assert.That(request.MethodId,
                    Is.EqualTo(fixture.Fixture.Children[(fixture.Media, "ConfigureStreamEndpoint")]));
                Assert.That(request.InputArguments.Count, Is.EqualTo(6));
                Assert.That(request.InputArguments[0].TryGetValue(out NodeId endpoint), Is.True);
                Assert.That(endpoint, Is.EqualTo(fixture.Stream));
                Assert.That(request.InputArguments[1].TryGetValue(out int codec), Is.True);
                Assert.That(codec, Is.EqualTo((int)VisionVideoCodecEnum.H264));
                Assert.That(request.InputArguments[2].TryGetValue(out uint width), Is.True);
                Assert.That(width, Is.EqualTo(640u));
                Assert.That(request.InputArguments[3].TryGetValue(out uint height), Is.True);
                Assert.That(height, Is.EqualTo(480u));
                Assert.That(request.InputArguments[4].TryGetValue(out double frameRate), Is.True);
                Assert.That(frameRate, Is.EqualTo(29.5));
                Assert.That(request.InputArguments[5].TryGetValue(out uint bitrate), Is.True);
                Assert.That(bitrate, Is.EqualTo(8_000_000u));
                return ValueTask.FromResult(new CallMethodResult());
            };

            CompanionOperationResult result = await RunAsync(fixture, "configure-stream").ConfigureAwait(false);

            Assert.That(Field(result.Values, "Method returned").TryGetValue(out bool accepted), Is.True);
            Assert.That(accepted, Is.True);
            Assert.That(fixture.Calls.Count, Is.EqualTo(1));
            fixture.VerifyBorrowedSession();
        }

        [Test]
        public async Task BothNullOrForeignEndpointsCannotBeSelected()
        {
            var fixture = new VisionWorkflowTestSupport();
            ArrayOf<CompanionValue> inputs =
                [new("stream", Variant.From(NodeId.Null)), new("clip", Variant.From(NodeId.Null))];
            await Assert.ThatAsync(() => RunAsync(fixture, "select-endpoints", inputs), Throws.ArgumentException)
                .ConfigureAwait(false);
            inputs = Replace(fixture.Inputs("get-clip"), "endpoint", Variant.From(fixture.Stream));
            await Assert.ThatAsync(() => RunAsync(fixture, "get-clip", inputs),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadNodeIdInvalid)).ConfigureAwait(false);
            fixture.Fixture.VerifyNoMutationOrSessionOwnership();
        }

        private static VisionStreamSessionDataType Lease(VisionWorkflowTestSupport fixture)
        {
            return new()
            {
                SessionToken = ByteString.From(17, 31, 47),
                Uri = "rtsps://localhost/secret-lease-marker",
                Protocol = VisionStreamProtocolEnum.Rtsp,
                ExpiresAt = new DateTimeUtc(fixture.UtcNow.AddHours(1))
            };
        }

        private static async Task<CompanionOperationResult> RunAsync(
            VisionWorkflowTestSupport fixture, string operation, ArrayOf<CompanionValue> inputs = default,
            CancellationToken cancellationToken = default)
        {
            var provider = new VisionCompanionProvider(timeProvider: fixture.Clock.Object);
            CompanionTaskInput task = await provider.PrepareInputAsync(fixture.Context, fixture.SensorTarget,
                operation, inputs.IsNull ? fixture.Inputs(operation) : inputs, cancellationToken).ConfigureAwait(false);
            return await provider.ExecutePreparedAsync(fixture.Context, fixture.SensorTarget,
                operation, task, null, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<CallMethodResult> PendingAsync(CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
            throw new AssertionException("An uncompleted release cannot return successfully.");
        }
    }
}
