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
using Moq;
using NUnit.Framework;
using Opc.Ua;
using Opc.Ua.Scales;
using Opc.Ua.Scales.Client;
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using Scales = Opc.Ua.Scales;

namespace UaLens.Tests.Companions
{
    /// <summary>
    /// Exercises real typed Scales discovery, inspection and generated method dispatch over mocked services.
    /// </summary>
    [TestFixture]
    [Category("Weighing")]
    public sealed partial class WeighingCompanionProviderTests
    {
        [Test]
        public async Task DiscoveryCoversAllFourteenKindsAndSystemsWithoutDuplicateRootEntriesAsync()
        {
            var session = new WeighingTestSession();
            foreach (ScaleKind kind in ScalesModel.AllKinds)
            {
                session.AddScale(kind);
            }
            CompanionTarget system = session.AddScale(system: true);
            var provider = new WeighingCompanionProvider();

            ArrayOf<CompanionTarget> result = await provider.DiscoverAsync(
                session.Context(), CancellationToken.None).ConfigureAwait(false);

            CompanionTarget[] targets = [.. result];
            ScaleKind[] kinds = [.. ScalesModel.AllKinds];
            Assert.That(targets, Has.Length.EqualTo(15));
            Assert.That(targets.Select(target => target.TypeName),
                Is.EquivalentTo(kinds.Select(kind => kind.ToString()).Append("Scale system")));
            Assert.That(targets.Select(target => target.NodeId).Distinct().Count(), Is.EqualTo(15));
            Assert.That(targets.Single(target => target.NodeId == system.NodeId).TypeName, Is.EqualTo("Scale system"));
            Assert.That(targets.All(target => target.ProviderId == "weighing"), Is.True);
            Assert.That(provider.Descriptor.Id, Is.EqualTo("weighing"));
            Assert.That(provider.Descriptor.DisplayName, Is.EqualTo("Weighing"));
            Assert.That(session.Services.Calls.IsEmpty, Is.True);
            Assert.That(session.Policy.Invocations, Is.Empty);
        }

        [Test]
        public async Task DiscoveryIncludesNestedSystemScalesAndStandaloneKindsAsync()
        {
            var session = new WeighingTestSession();
            CompanionTarget system = session.AddScale(system: true);
            CompanionTarget standalone = session.AddScale(ScaleKind.Laboratory);
            CompanionTarget child = session.AddScale(ScaleKind.AutomaticFilling, roots: false);
            CompanionTarget module = session.AddScale(ScaleKind.WeighingModule, roots: false);
            NodeId subDevices = session.AddGroup(system.NodeId, Scales.BrowseNames.SubDevices);
            NodeId modules = session.AddGroup(child.NodeId, Scales.BrowseNames.SubDevices);
            session.Services.AddChild(subDevices, child.NodeId);
            session.Services.AddChild(modules, module.NodeId);

            ArrayOf<CompanionTarget> result = await session.Provider(false).DiscoverAsync(
                session.Context(), CancellationToken.None).ConfigureAwait(false);

            CompanionTarget[] targets = [.. result];
            Assert.That(targets.Select(target => target.NodeId),
                Is.EquivalentTo([system.NodeId, standalone.NodeId, child.NodeId, module.NodeId]));
            Assert.That(targets.Single(target => target.NodeId == module.NodeId).TypeName,
                Is.EqualTo(nameof(ScaleKind.WeighingModule)));
            Assert.That(session.Services.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task DiscoveryUsesLiveAndTypedClientAncestryForVendorSubtypesAsync()
        {
            var session = new WeighingTestSession();
            NodeId type = session.Services.Id("vendor-checkweigher-type");
            NodeId scale = session.Services.Id("vendor-checkweigher");
            session.AddSubtype(type, session.Services.Resolve(Scales.ObjectTypeIds.CheckweigherType));
            session.Services.AddObject(scale, new ExpandedNodeId(type), "Vendor checkweigher");
            var client = new ScalesClient(session.Session.Object, session.Services.Telemetry);
            session.Services.AddChild(client.Topology.DeviceSetId, scale);

            ArrayOf<CompanionTarget> result = await session.Provider(false).DiscoverAsync(
                session.Context(), CancellationToken.None).ConfigureAwait(false);

            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0].NodeId, Is.EqualTo(scale));
            Assert.That(result[0].TypeName, Is.EqualTo(nameof(ScaleKind.Checkweigher)));
        }

        [Test]
        public async Task DiscoveryRejectsTargetBudgetOverflowInsteadOfReturningAnUnmarkedPartialListAsync()
        {
            var session = new WeighingTestSession();
            session.AddScale();
            session.AddScale(ScaleKind.Laboratory);

            await Assert.ThatAsync(async () => await session.Provider(false).DiscoverAsync(
                    session.Context(maxTargets: 1), CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadEncodingLimitsExceeded)).ConfigureAwait(false);
            Assert.That(session.Services.Calls.IsEmpty, Is.True);
        }

        [TestCaseSource(nameof(s_nonGoodStatuses))]
        public async Task DiscoveryCannotTurnDeniedOrUncertainBrowseIntoAnEmptyServerAsync(StatusCode status)
        {
            var session = new WeighingTestSession();
            session.Services.BrowseHandler = (_, _) => new BrowseResult { StatusCode = status };

            await Assert.ThatAsync(async () => await session.Provider(false).DiscoverAsync(
                    session.Context(), CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(status))
                .ConfigureAwait(false);
        }

        [Test]
        public async Task InspectionPreservesServerGrossNetTareSourceQualityTimestampAndUnitsAsync()
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale(ScaleKind.Laboratory);
            NodeId current = session.AddWeight(target.NodeId, gross: 123, net: 21, tare: 7);
            NodeId registered = session.AddWeight(target.NodeId, "RegisteredWeight", 60, 45, 15);

            CompanionInspection result = await session.Provider(false).InspectAsync(
                session.Context(), target, CancellationToken.None).ConfigureAwait(false);

            CompanionValue gross = Field(result.Values, "CurrentWeight / Gross");
            CompanionValue net = Field(result.Values, "CurrentWeight / Net");
            Assert.That(gross.Value, Is.EqualTo(Variant.From(123.0)));
            Assert.That(net.Value, Is.EqualTo(Variant.From(21.0)));
            Assert.That(net.Value, Is.Not.EqualTo(Variant.From(123.0 - 7.0)));
            Assert.That(Field(result.Values, "CurrentWeight / Tare").Value, Is.EqualTo(Variant.From(7.0)));
            Assert.That(gross.SourceNodeId, Is.EqualTo(current));
            Assert.That(gross.Quality, Is.EqualTo(StatusCodes.Good));
            Assert.That(gross.SourceTimestamp, Is.EqualTo((DateTime)session.Timestamp));
            Assert.That(gross.EngineeringMetadata, Does.Contain("kg").And.Contain("0 .. 100"));
            Assert.That(Field(result.Values, "RegisteredWeight / Net").Value, Is.EqualTo(Variant.From(45.0)));
            Assert.That(Field(result.Values, "RegisteredWeight / Net").SourceNodeId, Is.EqualTo(registered));
            Assert.That(session.Services.Calls.IsEmpty, Is.True);
            Assert.That(session.Policy.Invocations, Is.Empty);
        }

        [Test]
        public async Task SystemInspectionDoesNotInventAWeightOrOfferScaleOnlyCommandsAsync()
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale(system: true);
            session.AddMethod(target.NodeId, Scales.BrowseNames.SetZero);

            CompanionInspection result = await session.Provider(false).InspectAsync(
                session.Context(), target, CancellationToken.None).ConfigureAwait(false);

            CompanionValue weight = Field(result.Values, "CurrentWeight");
            CompanionOperation[] operations = [.. result.Operations];
            Assert.That(weight.Quality, Is.EqualTo(StatusCodes.BadNotFound));
            Assert.That(weight.Value.IsNull, Is.True);
            Assert.That(weight.EngineeringMetadata, Does.Contain("no weight of its own"));
            Assert.That(operations.Select(operation => operation.Id), Does.Not.Contain("set-zero"));
            Assert.That(session.Services.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task OptionalWeightAndStabilityAbsenceRemainDistinctFromGoodZeroAndFalseAsync()
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale();
            session.AddWeight(target.NodeId, omit: Scales.BrowseNames.WeightStable);

            CompanionOperationResult result = await session.Provider(false).ExecuteAsync(
                session.Context(), target, "weights", null, CancellationToken.None).ConfigureAwait(false);

            CompanionValue stable = Field(result.Values, "CurrentWeight / WeightStable");
            CompanionValue registered = Field(result.Values, "RegisteredWeight");
            Assert.That(stable.Quality, Is.EqualTo(StatusCodes.BadNotFound));
            Assert.That(stable.Value.IsNull, Is.True);
            Assert.That(stable.SourceNodeId.IsNull, Is.True);
            Assert.That(registered.Quality, Is.EqualTo(StatusCodes.BadNotFound));
            Assert.That(registered.Value.IsNull, Is.True);
            Assert.That(Field(result.Values, "CurrentWeight / Net").Value, Is.EqualTo(Variant.From(19.0)));
        }

        [TestCaseSource(nameof(s_nonGoodStatuses))]
        public async Task BadAndUncertainMeasurementsRetainTheirValuesWithoutPretendingGoodQualityAsync(
            StatusCode status)
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale();
            NodeId current = session.AddWeight(target.NodeId);
            session.Services.SetValue(current,
                Variant.From(new ExtensionObject(new WeightType { Gross = 9, Net = 8, Tare = 1 })), status: status);

            CompanionOperationResult result = await session.Provider(false).ExecuteAsync(
                session.Context(), target, "weights", null, CancellationToken.None).ConfigureAwait(false);

            CompanionValue net = Field(result.Values, "CurrentWeight / Net");
            Assert.That(net.Quality, Is.EqualTo(status));
            Assert.That(net.Value, Is.EqualTo(Variant.From(8.0)));
            Assert.That(net.SourceNodeId, Is.EqualTo(current));
            Assert.That(net.Text, Does.Contain(status.ToString()));
        }

        [Test]
        public async Task WrongWeightTypeIsExplicitAndProducesNoPlausibleDerivedValuesAsync()
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale();
            NodeId current = session.AddWeight(target.NodeId);
            session.Services.SetValue(current, Variant.From("not-a-weight"));

            CompanionOperationResult result = await session.Provider(false).ExecuteAsync(
                session.Context(), target, "weights", null, CancellationToken.None).ConfigureAwait(false);

            CompanionValue raw = Field(result.Values, "CurrentWeight");
            CompanionValue[] values = [.. result.Values];
            Assert.That(raw.Value, Is.EqualTo(Variant.From("not-a-weight")));
            Assert.That(raw.SourceNodeId, Is.EqualTo(current));
            Assert.That(raw.Quality, Is.EqualTo(StatusCodes.Good));
            Assert.That(raw.EngineeringMetadata, Does.Contain("expected WeightType"));
            Assert.That(values.Select(value => value.Name), Does.Not.Contain("CurrentWeight / Gross"));
        }

        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.NegativeInfinity)]
        public async Task NonFiniteInspectionIsVisibleRatherThanSilentlyChangedToZeroAsync(double number)
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale();
            session.AddWeight(target.NodeId, net: number);

            CompanionOperationResult result = await session.Provider(false).ExecuteAsync(
                session.Context(), target, "weights", null, CancellationToken.None).ConfigureAwait(false);

            CompanionValue net = Field(result.Values, "CurrentWeight / Net");
            Assert.That(net.Value.TryGetValue(out double actual), Is.True);
            Assert.That(actual, Is.EqualTo(number));
            Assert.That(net.EngineeringMetadata, Does.Contain("non-finite"));
            Assert.That(net.Quality, Is.EqualTo(StatusCodes.Good));
        }

        [Test]
        public async Task ProductsStatisticsRecipesAndRangesExposeTheirRealSourceValuesAsync()
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale(ScaleKind.Recipe);
            NodeId preset = session.AddGroup(target.NodeId, Scales.BrowseNames.ProductionPreset,
                Scales.ObjectTypeIds.ProductionPresetType);
            NodeId products = session.AddGroup(preset, Scales.BrowseNames.Products);
            NodeId product = session.AddGroup(products, "Coffee", Scales.ObjectTypeIds.RecipeProductType);
            NodeId productId = session.AddVariable(product, Scales.BrowseNames.ProductId, Variant.From("coffee"));
            NodeId output = session.AddGroup(target.NodeId, Scales.BrowseNames.ProductionOutput,
                Scales.ObjectTypeIds.StatisticType);
            NodeId counter = session.AddGroup(output, "TotalPackages", Scales.ObjectTypeIds.StatisticCounterType);
            NodeId count = session.AddVariable(counter, "ItemCount", Variant.From(17u));
            NodeId recipes = session.AddGroup(target.NodeId, Scales.BrowseNames.Recipes,
                Scales.ObjectTypeIds.RecipeManagementType);
            NodeId recipe = session.AddGroup(recipes, "Morning", Scales.ObjectTypeIds.RecipeType);
            NodeId recipeId = session.AddVariable(recipe, Scales.BrowseNames.RecipeId, Variant.From("morning"));
            NodeId range = session.AddGroup(target.NodeId, "RangeOne", Scales.ObjectTypeIds.WeighingRangeElementType);
            NodeId limits = session.AddVariable(range, Scales.BrowseNames.Range,
                Variant.From(new ExtensionObject(new Opc.Ua.Range { Low = 0.2, High = 75 })));
            session.AddVariable(range, Scales.BrowseNames.ActualScaleInterval, Variant.From(0.01));
            WeighingCompanionProvider provider = session.Provider(false);

            CompanionOperationResult productResult = await provider.ExecuteAsync(
                session.Context(), target, "products", null, CancellationToken.None).ConfigureAwait(false);
            CompanionOperationResult statistics = await provider.ExecuteAsync(
                session.Context(), target, "statistics", null, CancellationToken.None).ConfigureAwait(false);
            CompanionOperationResult recipeResult = await provider.ExecuteAsync(
                session.Context(), target, "recipes", null, CancellationToken.None).ConfigureAwait(false);
            CompanionOperationResult rangeResult = await provider.ExecuteAsync(
                session.Context(), target, "ranges", null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(Field(productResult.Values, "products / Products / Coffee / ProductId").SourceNodeId,
                Is.EqualTo(productId));
            Assert.That(Field(productResult.Values, "products / Products / Coffee / ProductId").Value,
                Is.EqualTo(Variant.From("coffee")));
            Assert.That(Field(statistics.Values, "statistics / TotalPackages / ItemCount").SourceNodeId,
                Is.EqualTo(count));
            Assert.That(Field(statistics.Values, "statistics / TotalPackages / ItemCount").Value,
                Is.EqualTo(Variant.From(17u)));
            Assert.That(Field(recipeResult.Values, "recipes / Morning / RecipeId").SourceNodeId, Is.EqualTo(recipeId));
            Assert.That(Field(recipeResult.Values, "recipes / Morning / RecipeId").Value,
                Is.EqualTo(Variant.From("morning")));
            Assert.That(Field(rangeResult.Values, "Range 1 / Range").SourceNodeId, Is.EqualTo(limits));
            Assert.That(Field(rangeResult.Values, "Range 1 / Range").Value, Is.EqualTo(Variant.From("0.2 .. 75")));
            Assert.That(session.Services.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task NotificationsUseScalesFilterMetadataWithoutStartingAnEventSubscriptionAsync()
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale();
            session.Services.SetValue(target.NodeId, Variant.From((byte)1), Attributes.EventNotifier);

            CompanionOperationResult result = await session.Provider(false).ExecuteAsync(
                session.Context(), target, "notifications", null, CancellationToken.None).ConfigureAwait(false);

            CompanionValue[] values = [.. result.Values];
            Assert.That(Field(result.Values, "Event source").Value, Is.EqualTo(Variant.From(target.NodeId)));
            Assert.That(Field(result.Values, "Scale event type").Value,
                Is.EqualTo(Variant.From(session.Services.Resolve(Scales.ObjectTypeIds.ScaleEventType))));
            Assert.That(values.Count(value => value.Name == "Notification field"), Is.EqualTo(11));
            Assert.That(values.Any(value => value.Value.ToString().Contains("NotificationCategory",
                StringComparison.Ordinal)), Is.True);
            Assert.That(result.Summary, Does.Contain("no subscription"));
            Assert.That(session.Session.Invocations.Any(invocation =>
                invocation.Method.Name.Contains("Subscription", StringComparison.Ordinal)), Is.False);
        }

        [Test]
        public async Task InspectionFieldBoundsAndExplicitCancellationStopWithoutMutationAsync()
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale();
            session.AddWeight(target.NodeId);
            WeighingCompanionProvider provider = session.Provider(false);

            await Assert.ThatAsync(async () => await provider.InspectAsync(
                    session.Context(maxFields: 4), target, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadEncodingLimitsExceeded)).ConfigureAwait(false);
            int calls = session.Session.Invocations.Count;
            await Assert.ThatAsync(async () => await provider.InspectAsync(
                    session.Context(), target, new CancellationToken(true)).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);
            Assert.That(session.Session.Invocations, Has.Count.EqualTo(calls));
            Assert.That(session.Services.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task CanceledDiscoveryReleasesContinuationPointsWithoutClosingTheBorrowedSessionAsync()
        {
            var session = new WeighingTestSession();
            using var canceled = new CancellationTokenSource();
            var continuation = ByteString.From([4, 2]);
            session.Services.BrowseHandler = (_, _) =>
            {
                canceled.Cancel();
                return new BrowseResult
                {
                    StatusCode = StatusCodes.Good,
                    ContinuationPoint = continuation,
                    References = []
                };
            };

            await Assert.ThatAsync(async () => await session.Provider(false).DiscoverAsync(
                    session.Context(), canceled.Token).ConfigureAwait(false),
                Throws.InstanceOf<OperationCanceledException>()).ConfigureAwait(false);

            Assert.That(session.Services.Released, Has.Count.EqualTo(1));
            Assert.That(session.Services.Released[0], Is.EqualTo(continuation));
            session.Session.Verify(value => value.Dispose(), Times.Never);
            session.Transport.Verify(value => value.Dispose(), Times.Never);
        }

        private static CompanionValue Field(ArrayOf<CompanionValue> fields, string name)
        {
            foreach (CompanionValue field in fields)
            {
                if (field.Name == name)
                {
                    return field;
                }
            }
            throw new AssertionException("Missing weighing field: " + name);
        }

        private static readonly StatusCode[] s_nonGoodStatuses =
            [StatusCodes.BadUserAccessDenied, StatusCodes.BadOutOfService, StatusCodes.Uncertain];
    }
}
