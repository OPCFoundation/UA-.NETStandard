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
using Opc.Ua.Scales;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using DataTypeIds = Opc.Ua.DataTypeIds;
using Scales = Opc.Ua.Scales;

namespace UaLens.Tests.Companions
{
    public sealed partial class WeighingCompanionProviderTests
    {
        [TestCase("opc.tcp://localhost:4840/ScalesServer")]
        [TestCase("opc.tcp://127.0.0.1:4840/OwnedSimulation")]
        [TestCase("opc.tcp://owned-weighing.example:4840/Simulation")]
        public async Task DefaultPolicyDeniesMutationRegardlessOfEndpointOrServerNamesAsync(string endpoint)
        {
            var session = new WeighingTestSession();
            session.Endpoint.EndpointUrl = endpoint;
            session.Endpoint.Server.ApplicationName = new LocalizedText("Owned ScalesServer");
            CompanionTarget target = session.AddScale();
            session.AddWeight(target.NodeId);
            session.AddMethod(target.NodeId, Scales.BrowseNames.SetTare);

            await Assert.ThatAsync(async () => await new WeighingCompanionProvider().PrepareInputAsync(
                    session.Context(), target, "set-tare", [], CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<UnauthorizedAccessException>().With.Message.Contains("owned-simulation"))
                .ConfigureAwait(false);

            Assert.That(session.Services.Calls.IsEmpty, Is.True);
        }

        [TestCase(MessageSecurityMode.None)]
        [TestCase(MessageSecurityMode.Sign)]
        public async Task EvenAnInjectedSimulationPolicyCannotAuthorizeAnUnencryptedChannelAsync(
            MessageSecurityMode mode)
        {
            var session = new WeighingTestSession();
            session.Endpoint.SecurityMode = mode;
            CompanionTarget target = session.AddScale();
            session.AddMethod(target.NodeId, Scales.BrowseNames.SetTare);

            await Assert.ThatAsync(async () => await session.Provider().PrepareInputAsync(
                    session.Context(), target, "set-tare", [], CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<UnauthorizedAccessException>().With.Message.Contains("signed and encrypted"))
                .ConfigureAwait(false);

            Assert.That(session.Policy.Invocations, Is.Empty);
            Assert.That(session.Services.Calls.IsEmpty, Is.True);
        }

        [TestCaseSource(nameof(s_weightEvidenceCases))]
        public async Task InvalidWeightStateOrEvidencePreventsCommandsWithoutDefaultSubstitutionAsync(
            string partition, StatusCode expectedStatus)
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale();
            NodeId weight = partition == "missing-weight" ? default :
                session.AddWeight(target.NodeId,
                    omit: partition == "missing-stability" ? Scales.BrowseNames.WeightStable : null);
            session.AddMethod(target.NodeId, Scales.BrowseNames.SetTare);
            switch (partition)
            {
                case "unstable":
                    session.Set(weight, Scales.BrowseNames.WeightStable, Variant.From(false));
                    break;
                case "overloaded":
                    session.Set(weight, Scales.BrowseNames.Overload, Variant.From(true));
                    break;
                case "underloaded":
                    session.Set(weight, Scales.BrowseNames.Underload, Variant.From(true));
                    break;
                case "denied-stability":
                    session.Set(weight, Scales.BrowseNames.WeightStable, Variant.From(true),
                        StatusCodes.BadUserAccessDenied);
                    break;
                case "uncertain-stability":
                    session.Set(weight, Scales.BrowseNames.WeightStable, Variant.From(true), StatusCodes.Uncertain);
                    break;
                case "wrong-stability":
                    session.Set(weight, Scales.BrowseNames.WeightStable, Variant.From("true"));
                    break;
                case "wrong-weight":
                    session.Services.SetValue(weight, Variant.From(12.0));
                    break;
                case "nan-weight":
                    session.Services.SetValue(weight,
                        Variant.From(new ExtensionObject(new WeightType { Gross = 1, Net = double.NaN, Tare = 0 })));
                    break;
                case "negative-gross":
                    session.Services.SetValue(weight,
                        Variant.From(new ExtensionObject(new WeightType { Gross = -1, Net = -1, Tare = 0 })));
                    break;
            }

            await Assert.ThatAsync(async () => await session.Provider().PrepareInputAsync(
                    session.Context(), target, "set-tare", [], CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(expectedStatus))
                .ConfigureAwait(false);

            Assert.That(session.Services.Calls.IsEmpty, Is.True);
        }

        [TestCaseSource(nameof(s_unitEvidenceCases))]
        public async Task InvalidUnitOrRangeMetadataCannotAuthorizePresetTareAsync(
            string partition, StatusCode expectedStatus)
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale();
            NodeId weight = session.AddWeight(target.NodeId,
                omit: partition == "missing" ? Opc.Ua.BrowseNames.EngineeringUnits : null);
            session.AddMethod(target.NodeId, Scales.BrowseNames.SetPresetTare,
                [DataTypeIds.Double, DataTypeIds.EUInformation]);
            switch (partition)
            {
                case "denied":
                    session.Set(weight, Opc.Ua.BrowseNames.EngineeringUnits,
                        Variant.From(new ExtensionObject(session.Unit)), StatusCodes.BadUserAccessDenied,
                        Opc.Ua.Namespaces.OpcUa);
                    break;
                case "wrong-type":
                    session.Set(weight, Opc.Ua.BrowseNames.EngineeringUnits, Variant.From("kg"),
                        namespaceUri: Opc.Ua.Namespaces.OpcUa);
                    break;
                case "negative-id":
                    session.Unit.UnitId = -1;
                    break;
                case "relative-namespace":
                    session.Unit.NamespaceUri = "not-an-absolute-namespace";
                    break;
                case "reversed-range":
                    session.Set(weight, Opc.Ua.BrowseNames.EURange,
                        Variant.From(new ExtensionObject(new Opc.Ua.Range { Low = 10, High = 5 })),
                        namespaceUri: Opc.Ua.Namespaces.OpcUa);
                    break;
                case "nan-range":
                    session.Set(weight, Opc.Ua.BrowseNames.EURange,
                        Variant.From(new ExtensionObject(new Opc.Ua.Range { Low = 0, High = double.NaN })),
                        namespaceUri: Opc.Ua.Namespaces.OpcUa);
                    break;
            }
            ArrayOf<CompanionValue> inputs =
            [
                new("presetTare", Variant.From(5.0)),
                new("unitNamespace", Variant.From("http://www.opcfoundation.org/UA/units/un/cefact")),
                new("unitId", Variant.From(0x4b474d))
            ];

            await Assert.ThatAsync(async () => await session.Provider().PrepareInputAsync(
                    session.Context(), target, "preset-tare", inputs, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(expectedStatus))
                .ConfigureAwait(false);

            Assert.That(session.Services.Calls.IsEmpty, Is.True);
        }

        [TestCase("identity")]
        [TestCase("session")]
        [TestCase("namespace")]
        [TestCase("endpoint")]
        [TestCase("server")]
        [TestCase("security-mode")]
        [TestCase("security-policy")]
        [TestCase("certificate")]
        [TestCase("message-context")]
        public async Task PreparedCommandsRejectStaleSessionIdentityOrNamespaceMetadataAsync(string change)
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale();
            session.AddWeight(target.NodeId);
            session.AddMethod(target.NodeId, Scales.BrowseNames.SetTare);
            WeighingCompanionProvider provider = session.Provider();
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                session.Context(), target, "set-tare", [], CancellationToken.None).ConfigureAwait(false);
            switch (change)
            {
                case "identity":
                    session.Identity = new Mock<IUserIdentity>(MockBehavior.Strict).Object;
                    break;
                case "session":
                    session.SessionId = new NodeId(124u);
                    break;
                case "namespace":
                    session.Services.NamespaceUris.Append("urn:new:namespace");
                    break;
                case "endpoint":
                    session.Endpoint.EndpointUrl = "opc.tcp://replacement.example:4840/";
                    break;
                case "server":
                    session.Endpoint.Server.ApplicationUri = "urn:replacement";
                    break;
                case "security-mode":
                    session.Endpoint.SecurityMode = MessageSecurityMode.Sign;
                    break;
                case "security-policy":
                    session.Endpoint.SecurityPolicyUri = SecurityPolicies.Aes256_Sha256_RsaPss;
                    break;
                case "certificate":
                    session.Endpoint.ServerCertificate = ByteString.From([3, 1, 4]);
                    break;
                case "message-context":
                    session.Session.SetupGet(value => value.MessageContext)
                        .Returns(ServiceMessageContext.Create(session.Services.Telemetry));
                    break;
            }

            await Assert.ThatAsync(async () => await provider.ExecutePreparedAsync(
                    session.Context(), target, "set-tare", prepared, null, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadInvalidState)).ConfigureAwait(false);

            Assert.That(session.Services.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task PreparedWeightSnapshotDoesNotAliasMutableServerStructuresAsync()
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale();
            NodeId weight = session.AddWeight(target.NodeId);
            session.AddMethod(target.NodeId, Scales.BrowseNames.SetTare);
            WeighingCompanionProvider provider = session.Provider();
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                session.Context(), target, "set-tare", [], CancellationToken.None).ConfigureAwait(false);
            DataValue value = session.Services.Read(
                new ReadValueId { NodeId = weight, AttributeId = Attributes.Value });
            Assert.That(value.WrappedValue.TryGetValue<WeightType>(
                out WeightType? mutable, session.Services.MessageContext), Is.True);
            Assert.That(mutable, Is.Not.Null);
            mutable!.Gross = 26;

            await Assert.ThatAsync(async () => await provider.ExecutePreparedAsync(
                    session.Context(), target, "set-tare", prepared, null, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadInvalidState)).ConfigureAwait(false);

            Assert.That(session.Services.Calls.IsEmpty, Is.True);
        }

        [TestCaseSource(nameof(s_staleDefinitionCases))]
        public async Task DispatchRereadsRealTypeMethodStateAndPermissionAsync(string change, StatusCode expectedStatus)
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale();
            NodeId weight = session.AddWeight(target.NodeId);
            NodeId method = session.AddMethod(target.NodeId, Scales.BrowseNames.SetPresetTare,
                [DataTypeIds.Double, DataTypeIds.EUInformation]);
            WeighingCompanionProvider provider = session.Provider();
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                session.Context(), target, "preset-tare", session.PresetInput(), CancellationToken.None)
                .ConfigureAwait(false);
            switch (change)
            {
                case "kind":
                    session.Services.AddObject(target.NodeId, Scales.ObjectTypeIds.HopperScaleType);
                    break;
                case "method":
                    session.AddMethod(target.NodeId, Scales.BrowseNames.SetPresetTare,
                        [DataTypeIds.Double, DataTypeIds.EUInformation]);
                    break;
                case "permission":
                    session.Services.SetValue(method, Variant.From(false), Attributes.UserExecutable);
                    break;
                case "stability":
                    session.Set(weight, Scales.BrowseNames.WeightStable, Variant.From(false));
                    break;
                case "signature":
                    session.Set(method, Opc.Ua.BrowseNames.InputArguments,
                        WeighingTestSession.Arguments([DataTypeIds.Float, DataTypeIds.EUInformation]),
                        namespaceUri: Opc.Ua.Namespaces.OpcUa);
                    break;
            }

            await Assert.ThatAsync(async () => await provider.ExecutePreparedAsync(
                    session.Context(), target, "preset-tare", prepared, null, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(expectedStatus))
                .ConfigureAwait(false);

            Assert.That(session.Services.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task RevokedOrChangedOwnedSimulationGrantStopsDispatchAsync()
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale();
            session.AddWeight(target.NodeId);
            session.AddMethod(target.NodeId, Scales.BrowseNames.SetTare);
            WeighingCompanionProvider provider = session.Provider();
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                session.Context(), target, "set-tare", [], CancellationToken.None).ConfigureAwait(false);
            session.Grant = session.Grant with { Revision = "revoked-run-revision" };

            await Assert.ThatAsync(async () => await provider.ExecutePreparedAsync(
                    session.Context(), target, "set-tare", prepared, null, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<UnauthorizedAccessException>().With.Message.Contains("grant changed"))
                .ConfigureAwait(false);

            Assert.That(session.Services.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task ProductProcessingAndSelectionChangesInvalidateRemovalAsync()
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale(ScaleKind.Checkweigher);
            (NodeId preset, NodeId product) = AddProductFixture(session, target, false);
            session.AddMethod(preset, Scales.BrowseNames.RemoveProduct, [DataTypeIds.String]);
            WeighingCompanionProvider provider = session.Provider();
            CompanionTaskInput prepared = await provider.PrepareInputAsync(session.Context(), target, "remove-product",
                [new CompanionValue("productId", Variant.From("p1"))], CancellationToken.None).ConfigureAwait(false);
            session.Set(product, Scales.BrowseNames.ProductMode, Variant.From(true));
            session.Set(preset, Scales.BrowseNames.CurrentProducts, Variant.From(["p1"]));

            await Assert.ThatAsync(async () => await provider.ExecutePreparedAsync(session.Context(), target,
                    "remove-product", prepared, null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadInvalidState)).ConfigureAwait(false);

            Assert.That(session.Services.Calls.IsEmpty, Is.True);
        }

        [TestCaseSource(nameof(s_nonGoodMethodStatuses))]
        public async Task NonGoodMethodRepliesAreNeverAcceptedOrAutomaticallyRetriedAsync(StatusCode status)
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale();
            session.AddWeight(target.NodeId);
            session.AddMethod(target.NodeId, Scales.BrowseNames.SetTare);
            session.Services.CallHandler = (_, _) => new CallMethodResult { StatusCode = status };
            WeighingCompanionProvider provider = session.Provider();
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                session.Context(), target, "set-tare", [], CancellationToken.None).ConfigureAwait(false);

            await Assert.ThatAsync(async () => await provider.ExecutePreparedAsync(
                    session.Context(), target, "set-tare", prepared, null, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(status))
                .ConfigureAwait(false);
            await Assert.ThatAsync(async () => await provider.ExecutePreparedAsync(
                    session.Context(), target, "set-tare", prepared, null, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("consumed")).ConfigureAwait(false);

            Assert.That(session.Services.Calls, Has.Count.EqualTo(1));
            session.Transport.Verify(value => value.Dispose(), Times.Never);
        }

        [TestCase("missing-result")]
        [TestCase("extra-result")]
        [TestCase("extra-output")]
        public async Task MalformedCallRepliesConsumePreparationAndCannotBecomeSuccessfulOutcomesAsync(string shape)
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale();
            session.AddWeight(target.NodeId);
            session.AddMethod(target.NodeId, Scales.BrowseNames.SetTare);
            var response = new CallResponse
            {
                ResponseHeader = new ResponseHeader(),
                Results = shape switch
                {
                    "extra-result" => [IndustrialCompanionTestSession.Good(), IndustrialCompanionTestSession.Good()],
                    "extra-output" => [IndustrialCompanionTestSession.Good([Variant.From(7)])],
                    _ => []
                }
            };
            session.Session.Setup(value => value.CallAsync(
                    It.IsAny<RequestHeader?>(), It.IsAny<ArrayOf<CallMethodRequest>>(),
                    It.IsAny<CancellationToken>()))
                .Returns(new ValueTask<CallResponse>(response));
            WeighingCompanionProvider provider = session.Provider();
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                session.Context(), target, "set-tare", [], CancellationToken.None).ConfigureAwait(false);

            await Assert.ThatAsync(async () => await provider.ExecutePreparedAsync(
                    session.Context(), target, "set-tare", prepared, null, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadDecodingError)).ConfigureAwait(false);
            await Assert.ThatAsync(async () => await provider.ExecutePreparedAsync(
                    session.Context(), target, "set-tare", prepared, null, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("consumed")).ConfigureAwait(false);

            session.Session.Verify(value => value.CallAsync(It.IsAny<RequestHeader?>(),
                It.IsAny<ArrayOf<CallMethodRequest>>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Test]
        public async Task TimeoutAfterDispatchLeavesAnUnknownNonReplayableOutcomeAsync()
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale();
            session.AddWeight(target.NodeId);
            session.AddMethod(target.NodeId, Scales.BrowseNames.SetTare);
            session.Services.CallHandler = static (_, _) => throw new ServiceResultException(StatusCodes.BadTimeout);
            WeighingCompanionProvider provider = session.Provider();
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                session.Context(), target, "set-tare", [], CancellationToken.None).ConfigureAwait(false);

            await Assert.ThatAsync(async () => await provider.ExecutePreparedAsync(
                    session.Context(), target, "set-tare", prepared, null, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(StatusCodes.BadTimeout))
                .ConfigureAwait(false);
            await Assert.ThatAsync(async () => await provider.ExecutePreparedAsync(
                    session.Context(), target, "set-tare", prepared, null, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);

            Assert.That(session.Services.Calls, Has.Count.EqualTo(1));
            session.Transport.Verify(value => value.Dispose(), Times.Never);
        }

        [Test]
        public async Task CancellationBeforeDispatchMakesNoCallAndCancellationAfterDispatchCannotReplayAsync()
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale();
            session.AddWeight(target.NodeId);
            session.AddMethod(target.NodeId, Scales.BrowseNames.SetTare);
            WeighingCompanionProvider provider = session.Provider();
            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                session.Context(), target, "set-tare", [], CancellationToken.None).ConfigureAwait(false);

            await Assert.ThatAsync(async () => await provider.ExecutePreparedAsync(
                    session.Context(), target, "set-tare", prepared, null, new CancellationToken(true))
                    .ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            Assert.That(session.Services.Calls.IsEmpty, Is.True);
            using var canceled = new CancellationTokenSource();
            session.Services.CallHandler = (_, _) =>
            {
                canceled.Cancel();
                return IndustrialCompanionTestSession.Good();
            };
            await Assert.ThatAsync(async () => await provider.ExecutePreparedAsync(
                    session.Context(), target, "set-tare", prepared, null, canceled.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            await Assert.ThatAsync(async () => await provider.ExecutePreparedAsync(
                    session.Context(), target, "set-tare", prepared, null, CancellationToken.None)
                    .ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>()).ConfigureAwait(false);
            Assert.That(session.Services.Calls, Has.Count.EqualTo(1));
            session.Transport.Verify(value => value.Dispose(), Times.Never);
        }

        private static readonly TestCaseData[] s_weightEvidenceCases =
        [
            new("missing-weight", StatusCodes.BadNoData),
            new("missing-stability", StatusCodes.BadNotFound),
            new("unstable", StatusCodes.BadInvalidState),
            new("overloaded", StatusCodes.BadInvalidState),
            new("underloaded", StatusCodes.BadInvalidState),
            new("denied-stability", StatusCodes.BadUserAccessDenied),
            new("uncertain-stability", StatusCodes.Uncertain),
            new("wrong-stability", StatusCodes.BadTypeMismatch),
            new("wrong-weight", StatusCodes.BadTypeMismatch),
            new("nan-weight", StatusCodes.BadOutOfRange),
            new("negative-gross", StatusCodes.BadOutOfRange)
        ];

        private static readonly TestCaseData[] s_unitEvidenceCases =
        [
            new("missing", StatusCodes.BadNotFound),
            new("denied", StatusCodes.BadUserAccessDenied),
            new("wrong-type", StatusCodes.BadTypeMismatch),
            new("negative-id", StatusCodes.BadInvalidArgument),
            new("relative-namespace", StatusCodes.BadInvalidArgument),
            new("reversed-range", StatusCodes.BadOutOfRange),
            new("nan-range", StatusCodes.BadOutOfRange)
        ];

        private static readonly TestCaseData[] s_staleDefinitionCases =
        [
            new("kind", StatusCodes.BadTypeMismatch),
            new("method", StatusCodes.BadInvalidState),
            new("permission", StatusCodes.BadUserAccessDenied),
            new("stability", StatusCodes.BadInvalidState),
            new("signature", StatusCodes.BadTypeMismatch)
        ];

        private static readonly StatusCode[] s_nonGoodMethodStatuses =
            [StatusCodes.Uncertain, StatusCodes.BadUserAccessDenied, StatusCodes.BadMethodInvalid];
    }
}
