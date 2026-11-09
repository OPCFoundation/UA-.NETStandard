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
using System.Threading;
using System.Threading.Tasks;
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Client;
using UaLens.Capabilities;

namespace UaLens.Tests.Capabilities;

[TestFixture]
public sealed class SessionCapabilityProbeTests
{
    [TestCase((int)CapabilityOperation.ReadValue, AccessLevels.CurrentRead)]
    [TestCase((int)CapabilityOperation.WriteValue, AccessLevels.CurrentWrite)]
    [TestCase((int)CapabilityOperation.ReadHistory, AccessLevels.HistoryRead)]
    [TestCase((int)CapabilityOperation.UpdateHistory, AccessLevels.HistoryWrite)]
    public async Task VariableChecksReadTwoScalarPermissionAttributesWithoutReadingOrWritingValues(
        int operation,
        byte mask)
    {
        Mock<ISession> session = ReadSession(
            [new DataValue(Variant.From(mask)), new DataValue(Variant.From(mask))]);
        var probe = new SessionCapabilityProbe();

        CapabilityResult result = await probe.ProbeAsync(
            session.Object, new CapabilityRequest(s_target, (CapabilityOperation)operation), CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(result.State, Is.EqualTo(CapabilityState.Supported));
        Assert.That(result.Reason, Does.Contain("advertises"));
        session.Verify(value => value.ReadAsync(
            null, 0, TimestampsToReturn.Neither,
            It.Is<ArrayOf<ReadValueId>>(attributes => attributes.Count == 2
                && attributes[0].NodeId == s_target && attributes[0].AttributeId == Attributes.AccessLevel
                && attributes[1].NodeId == s_target && attributes[1].AttributeId == Attributes.UserAccessLevel),
            CancellationToken.None), Times.Once);
        session.VerifyNoOtherCalls();
    }

    [TestCase((byte)0, (byte)0, (int)CapabilityState.Unsupported)]
    [TestCase((byte)0, AccessLevels.CurrentWrite, (int)CapabilityState.Unsupported)]
    [TestCase(AccessLevels.CurrentWrite, (byte)0, (int)CapabilityState.Denied)]
    [TestCase(AccessLevels.CurrentWrite, AccessLevels.CurrentWrite, (int)CapabilityState.Supported)]
    public async Task VariableSupportAndCurrentIdentityPermissionRemainDistinct(
        byte access,
        byte userAccess,
        int expected)
    {
        Mock<ISession> session = ReadSession(
            [new DataValue(Variant.From(access)), new DataValue(Variant.From(userAccess))]);

        CapabilityResult result = await new SessionCapabilityProbe().ProbeAsync(
            session.Object, new CapabilityRequest(s_target, CapabilityOperation.WriteValue), CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(result.State, Is.EqualTo((CapabilityState)expected));
    }

    [TestCase(true, true, (int)CapabilityState.Supported)]
    [TestCase(true, false, (int)CapabilityState.Denied)]
    [TestCase(false, false, (int)CapabilityState.Unsupported)]
    [TestCase(false, true, (int)CapabilityState.Unsupported)]
    public async Task MethodChecksInspectExecutableFlagsAndNeverCallTheMethod(
        bool executable,
        bool userExecutable,
        int expected)
    {
        Mock<ISession> session = ReadSession(
            [new DataValue(Variant.From(executable)), new DataValue(Variant.From(userExecutable))]);

        CapabilityResult result = await new SessionCapabilityProbe().ProbeAsync(
            session.Object, new CapabilityRequest(s_target, CapabilityOperation.CallMethod), CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(result.State, Is.EqualTo((CapabilityState)expected));
        session.Verify(value => value.ReadAsync(
            null, 0, TimestampsToReturn.Neither,
            It.Is<ArrayOf<ReadValueId>>(attributes => attributes.Count == 2
                && attributes[0].AttributeId == Attributes.Executable
                && attributes[1].AttributeId == Attributes.UserExecutable),
            CancellationToken.None), Times.Once);
        session.VerifyNoOtherCalls();
    }

    [TestCase((byte)0, (int)CapabilityState.Unsupported)]
    [TestCase(EventNotifiers.HistoryRead, (int)CapabilityState.Unsupported)]
    [TestCase(EventNotifiers.SubscribeToEvents, (int)CapabilityState.Supported)]
    public async Task EventChecksInspectTheConcreteNotifierRatherThanNamespacePresence(byte notifier, int expected)
    {
        Mock<ISession> session = ReadSession([new DataValue(Variant.From(notifier))]);

        CapabilityResult result = await new SessionCapabilityProbe().ProbeAsync(
            session.Object,
            new CapabilityRequest(s_target, CapabilityOperation.SubscribeEvents),
            CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(result.State, Is.EqualTo((CapabilityState)expected));
        session.Verify(value => value.ReadAsync(
            null, 0, TimestampsToReturn.Neither,
            It.Is<ArrayOf<ReadValueId>>(attributes =>
                attributes.Count == 1 && attributes[0].AttributeId == Attributes.EventNotifier),
            CancellationToken.None), Times.Once);
        session.VerifyNoOtherCalls();
    }

    [TestCaseSource(nameof(s_attributeCases))]
    public async Task BadAttributeStatusIsNotIgnoredWhenAResultHasNoValue(StatusCode status, int expected)
    {
        Mock<ISession> session = ReadSession(
            [DataValue.FromStatusCode(status), new DataValue(Variant.From(true))]);

        CapabilityResult result = await new SessionCapabilityProbe().ProbeAsync(
            session.Object, new CapabilityRequest(s_target, CapabilityOperation.CallMethod), CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(result.State, Is.EqualTo((CapabilityState)expected));
        Assert.That(result.CanExecute, Is.False);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task MissingOrMistypedAttributesRemainUnknown(bool empty)
    {
        ArrayOf<DataValue> values = empty ? [] :
            [new DataValue(Variant.From("not a permission flag")), new DataValue(Variant.From(true))];
        Mock<ISession> session = ReadSession(values);

        CapabilityResult result = await new SessionCapabilityProbe().ProbeAsync(
            session.Object, new CapabilityRequest(s_target, CapabilityOperation.CallMethod), CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(result.State, Is.EqualTo(CapabilityState.Unknown));
        Assert.That(result.Reason, Does.Contain("evidence"));
    }

    [Test]
    public async Task FailedReadServiceHeaderCannotBeOverriddenByGoodAttributeValues()
    {
        Mock<ISession> session = ReadSession(
            [new DataValue(Variant.From(true)), new DataValue(Variant.From(true))], StatusCodes.BadUserAccessDenied);

        CapabilityResult result = await new SessionCapabilityProbe().ProbeAsync(
            session.Object, new CapabilityRequest(s_target, CapabilityOperation.CallMethod), CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(result.State, Is.EqualTo(CapabilityState.Denied));
    }

    [Test]
    public async Task FailedReadServiceHeaderKeepsItsDenialWhenThereAreNoAttributeResults()
    {
        Mock<ISession> session = ReadSession([], StatusCodes.BadUserAccessDenied);

        CapabilityResult result = await new SessionCapabilityProbe().ProbeAsync(
            session.Object, new CapabilityRequest(s_target, CapabilityOperation.CallMethod), CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(result.State, Is.EqualTo(CapabilityState.Denied));
    }

    [Test]
    public async Task ThrownPermissionFailureIsReportedWithoutHidingTransportFailures()
    {
        Mock<ISession> session = ReadSession([]);
        session.Setup(value => value.ReadAsync(
            It.IsAny<RequestHeader?>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
            It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ServiceResultException(StatusCodes.BadUserAccessDenied));

        CapabilityResult result = await new SessionCapabilityProbe().ProbeAsync(
            session.Object, new CapabilityRequest(s_target, CapabilityOperation.CallMethod), CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(result.State, Is.EqualTo(CapabilityState.Denied));
        Assert.That(result.Reason, Does.Contain("authorized"));
    }

    [Test]
    public async Task BrowseIsLimitedToOnePageAndAlwaysReleasesItsContinuationPoint()
    {
        ByteString continuation = [1, 2, 3];
        Mock<ISession> session = BrowseSession(new BrowseResult
        {
            StatusCode = StatusCodes.Good,
            ContinuationPoint = continuation
        });

        CapabilityResult result = await new SessionCapabilityProbe().ProbeAsync(
            session.Object, new CapabilityRequest(s_target, CapabilityOperation.Browse), CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(result.State, Is.EqualTo(CapabilityState.Supported));
        Assert.That(result.Reason, Does.Contain("Browsing this target succeeded"));
        session.Verify(value => value.BrowseAsync(
            null, null, 1,
            It.Is<ArrayOf<BrowseDescription>>(descriptions => descriptions.Count == 1
                && descriptions[0].NodeId == s_target && descriptions[0].IncludeSubtypes),
            CancellationToken.None), Times.Once);
        session.Verify(value => value.BrowseNextAsync(
            null, true,
            It.Is<ArrayOf<ByteString>>(points => points.Count == 1 && points[0] == continuation),
            It.IsAny<CancellationToken>()), Times.Once);
        session.VerifyNoOtherCalls();
    }

    [TestCaseSource(nameof(s_browseCases))]
    public async Task EmptyBrowseReferencesOnlyMeanSuccessWhenTheBrowseActuallySucceeded(
        StatusCode status, int expected)
    {
        Mock<ISession> session = BrowseSession(new BrowseResult { StatusCode = status, References = [] });

        CapabilityResult result = await new SessionCapabilityProbe().ProbeAsync(
            session.Object, new CapabilityRequest(s_target, CapabilityOperation.Browse), CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(result.State, Is.EqualTo((CapabilityState)expected));
        session.Verify(value => value.BrowseNextAsync(
            It.IsAny<RequestHeader?>(), It.IsAny<bool>(), It.IsAny<ArrayOf<ByteString>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Test]
    public async Task MissingBrowseResultsDoNotProveAnOperationSucceeded()
    {
        Mock<ISession> session = BrowseSession(new BrowseResult());
        session.Setup(value => value.BrowseAsync(
            It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(), It.IsAny<uint>(),
            It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BrowseResponse { ResponseHeader = new ResponseHeader(), Results = [] });

        CapabilityResult result = await new SessionCapabilityProbe().ProbeAsync(
            session.Object, new CapabilityRequest(s_target, CapabilityOperation.Browse), CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(result.State, Is.EqualTo(CapabilityState.Unknown));
        Assert.That(result.CanExecute, Is.False);
    }

    [Test]
    public async Task FailedBrowseServiceHeaderKeepsItsDenialWhenThereAreNoResults()
    {
        Mock<ISession> session = BrowseSession(new BrowseResult());
        session.Setup(value => value.BrowseAsync(
            It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(), It.IsAny<uint>(),
            It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BrowseResponse
            {
                ResponseHeader = new ResponseHeader { ServiceResult = StatusCodes.BadUserAccessDenied },
                Results = []
            });

        CapabilityResult result = await new SessionCapabilityProbe().ProbeAsync(
            session.Object, new CapabilityRequest(s_target, CapabilityOperation.Browse), CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(result.State, Is.EqualTo(CapabilityState.Denied));
    }

    [TestCaseSource(nameof(s_cleanupCases))]
    public async Task FailedBrowseCleanupCannotBeReportedAsSupportedOrCachedAsFeatureAbsence(StatusCode status)
    {
        Mock<ISession> session = BrowseSession(new BrowseResult
        {
            StatusCode = StatusCodes.Good,
            ContinuationPoint = [7]
        });
        session.Setup(value => value.BrowseNextAsync(
            It.IsAny<RequestHeader?>(), true, It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ServiceResultException(status));

        CapabilityResult result = await new SessionCapabilityProbe().ProbeAsync(
            session.Object, new CapabilityRequest(s_target, CapabilityOperation.Browse), CancellationToken.None)
            .ConfigureAwait(false);

        Assert.That(result.State, Is.EqualTo(CapabilityState.Unknown));
        Assert.That(result.CanExecute, Is.False);
    }

    [Test]
    public async Task CallerCancellationAfterBrowseStillReleasesTheServerContinuationPoint()
    {
        ByteString continuation = [5, 4];
        using var cancellation = new CancellationTokenSource();
        var response = new BrowseResponse
        {
            ResponseHeader = new ResponseHeader(),
            Results = [new BrowseResult { StatusCode = StatusCodes.Good, ContinuationPoint = continuation }]
        };
        Mock<ISession> session = BrowseSession(response.Results[0]);
        async Task<BrowseResponse> CancelAndRespondAsync()
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
            return response;
        }
        session.Setup(value => value.BrowseAsync(
            It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(), It.IsAny<uint>(),
            It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
            .Returns(() => new ValueTask<BrowseResponse>(CancelAndRespondAsync()));

        await Assert.ThatAsync(() => new SessionCapabilityProbe().ProbeAsync(
            session.Object, new CapabilityRequest(s_target, CapabilityOperation.Browse), cancellation.Token),
            Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

        session.Verify(value => value.BrowseNextAsync(
            null, true, It.Is<ArrayOf<ByteString>>(points => points.Count == 1 && points[0] == continuation),
            It.Is<CancellationToken>(token => !token.IsCancellationRequested)), Times.Once);
    }

    [Test]
    public async Task CancellationBeforeAProbePerformsNoNetworkOperations()
    {
        var session = new Mock<ISession>(MockBehavior.Strict);

        await Assert.ThatAsync(() => new SessionCapabilityProbe().ProbeAsync(
            session.Object, new CapabilityRequest(s_target, CapabilityOperation.Browse),
            new CancellationToken(canceled: true)), Throws.InstanceOf<OperationCanceledException>())
            .ConfigureAwait(false);

        session.VerifyNoOtherCalls();
    }

    private static Mock<ISession> ReadSession(ArrayOf<DataValue> values, StatusCode status = default)
    {
        var session = new Mock<ISession>(MockBehavior.Strict);
        session.Setup(value => value.ReadAsync(
            It.IsAny<RequestHeader?>(), It.IsAny<double>(), It.IsAny<TimestampsToReturn>(),
            It.IsAny<ArrayOf<ReadValueId>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReadResponse
            {
                ResponseHeader = new ResponseHeader { ServiceResult = status },
                Results = values
            });
        return session;
    }

    private static Mock<ISession> BrowseSession(BrowseResult result)
    {
        var session = new Mock<ISession>(MockBehavior.Strict);
        session.Setup(value => value.BrowseAsync(
            It.IsAny<RequestHeader?>(), It.IsAny<ViewDescription?>(), It.IsAny<uint>(),
            It.IsAny<ArrayOf<BrowseDescription>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BrowseResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results = [result]
            });
        session.Setup(value => value.BrowseNextAsync(
            It.IsAny<RequestHeader?>(), true, It.IsAny<ArrayOf<ByteString>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BrowseNextResponse { ResponseHeader = new ResponseHeader(), Results = [] });
        return session;
    }

    private static readonly NodeId s_target = new("target", 0);
    private static readonly TestCaseData[] s_attributeCases =
    [
        new(StatusCodes.BadUserAccessDenied, (int)CapabilityState.Denied),
        new(StatusCodes.BadNodeIdUnknown, (int)CapabilityState.Unsupported),
        new(StatusCodes.BadTimeout, (int)CapabilityState.Unknown)
    ];
    private static readonly TestCaseData[] s_browseCases =
    [
        new(StatusCodes.Good, (int)CapabilityState.Supported),
        new(StatusCodes.BadUserAccessDenied, (int)CapabilityState.Denied),
        new(StatusCodes.BadNodeIdUnknown, (int)CapabilityState.Unsupported),
        new(StatusCodes.BadCommunicationError, (int)CapabilityState.Unknown)
    ];
    private static readonly StatusCode[] s_cleanupCases = [StatusCodes.BadTimeout, StatusCodes.BadServiceUnsupported];
}
