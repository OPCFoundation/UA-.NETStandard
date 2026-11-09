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
using UaLens.Plugins.Companions;
using UaLens.Plugins.Companions.Providers;
using DataTypeIds = Opc.Ua.DataTypeIds;
using Scales = Opc.Ua.Scales;

namespace UaLens.Tests.Companions
{
    public sealed partial class WeighingCompanionProviderTests
    {
        [TestCase("set-zero", "SetZero")]
        [TestCase("set-tare", "SetTare")]
        [TestCase("clear-tare", "ClearTare")]
        [TestCase("register-weight", "RegisterWeight")]
        public async Task SupportedWeightCommandsUseTypedSingleInstanceDispatchAndBorrowTheSessionAsync(
            string operation, string methodName)
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale(ScaleKind.Laboratory);
            session.AddWeight(target.NodeId);
            NodeId method = session.AddMethod(target.NodeId, methodName);
            WeighingCompanionProvider provider = session.Provider();

            CompanionTaskInput input = await provider.PrepareInputAsync(
                session.Context(), target, operation, [], CancellationToken.None).ConfigureAwait(false);
            Assert.That(session.Services.Calls.IsEmpty, Is.True);
            CompanionOperationResult result = await provider.ExecutePreparedAsync(
                session.Context(), target, operation, input, null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(session.Services.Calls, Has.Count.EqualTo(1));
            Assert.That(session.Services.Calls[0].ObjectId, Is.EqualTo(target.NodeId));
            Assert.That(session.Services.Calls[0].MethodId, Is.EqualTo(method));
            Assert.That(session.Services.Calls[0].InputArguments.IsEmpty, Is.True);
            Assert.That(result.Summary, Does.Contain("accepted").And.Contain("not observed completion"));
            Assert.That(Field(result.Values, "Method result").Value,
                Is.EqualTo(Variant.From(StatusCodes.Good)));
            session.Policy.Verify(policy => policy.AuthorizeAsync(
                It.IsAny<CompanionContext>(), target, operation, It.IsAny<CancellationToken>()), Times.Exactly(2));
            session.Session.Verify(value => value.Dispose(), Times.Never);
            session.Transport.Verify(value => value.Dispose(), Times.Never);
            await Assert.ThatAsync(async () => await provider.ExecutePreparedAsync(
                    session.Context(), target, operation, input, null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<InvalidOperationException>().With.Message.Contains("consumed")).ConfigureAwait(false);
            Assert.That(session.Services.Calls, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task PreparationCopiesMutableEditorInputsAndDispatchesTheReviewedTypedUnitAsync()
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale();
            session.AddWeight(target.NodeId);
            NodeId method = session.AddMethod(target.NodeId, Scales.BrowseNames.SetPresetTare,
                [DataTypeIds.Double, DataTypeIds.EUInformation]);
            CompanionValue[] editor = [.. session.PresetInput(5)];
            WeighingCompanionProvider provider = session.Provider();

            CompanionTaskInput prepared = await provider.PrepareInputAsync(session.Context(), target,
                "preset-tare", ArrayOf.Wrapped(editor), CancellationToken.None).ConfigureAwait(false);
            editor[0] = new CompanionValue("presetTare", Variant.From(85.0));
            editor[1] = new CompanionValue("unitNamespace", Variant.From("urn:changed"));
            editor[2] = new CompanionValue("unitId", Variant.From(777));
            CompanionOperationResult result = await provider.ExecutePreparedAsync(session.Context(), target,
                "preset-tare", prepared, null, CancellationToken.None).ConfigureAwait(false);

            CallMethodRequest request = session.Services.Calls[0];
            Assert.That(request.MethodId, Is.EqualTo(method));
            Assert.That(request.InputArguments, Has.Count.EqualTo(2));
            Assert.That(request.InputArguments[0], Is.EqualTo(Variant.From(5.0)));
            Assert.That(request.InputArguments[1].TryGetValue<EUInformation>(
                out EUInformation? unit, session.Services.MessageContext), Is.True);
            Assert.That(unit, Is.Not.Null);
            Assert.That(unit!.UnitId, Is.EqualTo(session.Unit.UnitId));
            Assert.That(unit.NamespaceUri, Is.EqualTo(session.Unit.NamespaceUri));
            Assert.That(prepared.Review, Does.Contain("value: 5;").And.Not.Contain("85"));
            Assert.That(result.Summary, Does.Contain("accepted"));
        }

        [TestCase(0.0)]
        [TestCase(100.0)]
        public async Task PresetTareAcceptsTheServerRangeBoundariesAsync(double tare)
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale();
            session.AddWeight(target.NodeId);
            session.AddMethod(target.NodeId, Scales.BrowseNames.SetPresetTare,
                [DataTypeIds.Double, DataTypeIds.EUInformation]);
            WeighingCompanionProvider provider = session.Provider();

            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                session.Context(), target, "preset-tare", session.PresetInput(tare), CancellationToken.None)
                .ConfigureAwait(false);
            await provider.ExecutePreparedAsync(
                session.Context(), target, "preset-tare", prepared, null, CancellationToken.None).ConfigureAwait(false);

            Assert.That(session.Services.Calls[0].InputArguments[0], Is.EqualTo(Variant.From(tare)));
        }

        [TestCase(-0.001)]
        [TestCase(100.001)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        public async Task InvalidPresetTareCannotReachAMethodCallAsync(double tare)
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale();
            session.AddWeight(target.NodeId);
            session.AddMethod(target.NodeId, Scales.BrowseNames.SetPresetTare,
                [DataTypeIds.Double, DataTypeIds.EUInformation]);

            if (double.IsFinite(tare) && tare > 100)
            {
                await Assert.ThatAsync(async () => await session.Provider().PrepareInputAsync(session.Context(), target,
                        "preset-tare", session.PresetInput(tare), CancellationToken.None).ConfigureAwait(false),
                    Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                        .EqualTo(StatusCodes.BadOutOfRange)).ConfigureAwait(false);
            }
            else
            {
                await Assert.ThatAsync(async () => await session.Provider().PrepareInputAsync(session.Context(), target,
                        "preset-tare", session.PresetInput(tare), CancellationToken.None).ConfigureAwait(false),
                    Throws.TypeOf<ArgumentException>().With.Message.Contains("finite and nonnegative"))
                    .ConfigureAwait(false);
            }

            Assert.That(session.Services.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task IncompatibleUnitIsRejectedWithoutConvertingTheReviewedValueAsync()
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale();
            session.AddWeight(target.NodeId);
            session.AddMethod(target.NodeId, Scales.BrowseNames.SetPresetTare,
                [DataTypeIds.Double, DataTypeIds.EUInformation]);
            string unitNamespace = session.Unit.NamespaceUri ??
                throw new AssertionException("The fixture must supply the scale's engineering-unit namespace.");
            ArrayOf<CompanionValue> values =
            [
                new("presetTare", Variant.From(5000.0)),
                new("unitNamespace", Variant.From(unitNamespace)),
                new("unitId", Variant.From(0x47524d))
            ];

            await Assert.ThatAsync(async () => await session.Provider().PrepareInputAsync(
                    session.Context(), target, "preset-tare", values, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadInvalidArgument)).ConfigureAwait(false);
            Assert.That(session.Services.Calls.IsEmpty, Is.True);
        }

        [TestCaseSource(nameof(s_nonExecutableCases))]
        public async Task NonExecutableMethodsAreNotOfferedAndCannotBePreparedAsync(
            bool executable, bool userExecutable, StatusCode expectedStatus)
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale();
            session.AddWeight(target.NodeId);
            NodeId method = session.AddMethod(target.NodeId, Scales.BrowseNames.SetTare);
            session.Services.SetValue(method, Variant.From(executable), Attributes.Executable);
            session.Services.SetValue(method, Variant.From(userExecutable), Attributes.UserExecutable);
            WeighingCompanionProvider provider = session.Provider();

            CompanionInspection inspection = await provider.InspectAsync(
                session.Context(), target, CancellationToken.None).ConfigureAwait(false);
            CompanionOperation[] operations = [.. inspection.Operations];

            Assert.That(operations.Select(operation => operation.Id), Does.Not.Contain("set-tare"));
            Assert.That(Field(inspection.Values, "Command / SetTare").Quality, Is.EqualTo(expectedStatus));
            await Assert.ThatAsync(async () => await provider.PrepareInputAsync(
                    session.Context(), target, "set-tare", [], CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode").EqualTo(expectedStatus))
                .ConfigureAwait(false);
            Assert.That(session.Services.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task ExecutableCommandsUseDeploymentSafetyAndCannotBypassPreparationAsync()
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale();
            session.AddWeight(target.NodeId);
            session.AddMethod(target.NodeId, Scales.BrowseNames.SetTare);
            WeighingCompanionProvider provider = session.Provider(false);

            CompanionInspection inspection = await provider.InspectAsync(
                session.Context(), target, CancellationToken.None).ConfigureAwait(false);
            CompanionOperation[] operations = [.. inspection.Operations];
            CompanionOperation operation = operations.Single(candidate => candidate.Id == "set-tare");

            Assert.That(operation.Safety, Is.EqualTo(CompanionOperationSafety.DeploymentMutation));
            Assert.That(operation.HasTypedInput, Is.True);
            Assert.That(operation.DisplayName, Does.Contain("owned simulation"));
            await Assert.ThatAsync(async () => await provider.ExecuteAsync(
                    session.Context(), target, operation.Id, null, CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadNotSupported)).ConfigureAwait(false);
            Assert.That(session.Services.Calls.IsEmpty, Is.True);
        }

        [TestCase("start-calibration")]
        [TestCase("start-leveling")]
        [TestCase("set-feeder-speed")]
        [TestCase("open-draft-shields")]
        [TestCase("discharge-start")]
        [TestCase("packml-start")]
        public async Task PhysicalAndGenericPackMLCommandsAreOutsideTheWeighingDomainSliceAsync(string operation)
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale(ScaleKind.Laboratory);

            await Assert.ThatAsync(async () => await session.Provider().PrepareInputAsync(
                    session.Context(), target, operation, [], CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadNotSupported)).ConfigureAwait(false);
            Assert.That(session.Services.Calls.IsEmpty, Is.True);
        }

        [Test]
        public async Task AbsentOrWrongKindMethodCannotBePreparedEvenWhenTheBrowseNameExistsAsync()
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale();
            session.AddMethod(target.NodeId, Scales.BrowseNames.StartRecipe, [DataTypeIds.NodeId]);
            WeighingCompanionProvider provider = session.Provider();

            await Assert.ThatAsync(async () => await provider.PrepareInputAsync(
                    session.Context(), target, "set-tare", [], CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadNotSupported)).ConfigureAwait(false);
            await Assert.ThatAsync(async () => await provider.PrepareInputAsync(
                    session.Context(), target, "start-recipe",
                    [new CompanionValue("recipeId", Variant.From("r1"))], CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ServiceResultException>().With.Property("StatusCode")
                    .EqualTo(StatusCodes.BadNotSupported)).ConfigureAwait(false);
            Assert.That(session.Services.Calls.IsEmpty, Is.True);
        }

        [TestCase("select-product", "SelectProduct", false)]
        [TestCase("deselect-product", "DeselectProduct", true)]
        [TestCase("switch-product", "SwitchProduct", false)]
        [TestCase("remove-product", "RemoveProduct", false)]
        public async Task ProductOperationsUseTheTypedPresetOwnerAndReviewedProductIdAsync(
            string operation, string methodName, bool selected)
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale(ScaleKind.Checkweigher);
            (NodeId preset, _) = AddProductFixture(session, target, selected);
            NodeId method = session.AddMethod(preset, methodName, [DataTypeIds.String]);
            WeighingCompanionProvider provider = session.Provider();

            CompanionTaskInput prepared = await provider.PrepareInputAsync(session.Context(), target, operation,
                [new CompanionValue("productId", Variant.From("p1"))], CancellationToken.None).ConfigureAwait(false);
            await provider.ExecutePreparedAsync(session.Context(), target, operation, prepared, null,
                CancellationToken.None).ConfigureAwait(false);

            CallMethodRequest request = session.Services.Calls[0];
            Assert.That(request.ObjectId, Is.EqualTo(preset));
            Assert.That(request.MethodId, Is.EqualTo(method));
            Assert.That(request.InputArguments, Has.Count.EqualTo(1));
            Assert.That(request.InputArguments[0], Is.EqualTo(Variant.From("p1")));
        }

        [Test]
        public async Task ProductCreationUsesTypedArgumentsAndReportsTheReturnedNodeWithoutClaimingCompletionAsync()
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale(system: true);
            (NodeId preset, _) = AddProductFixture(session, target, false);
            session.AddMethod(preset, Scales.BrowseNames.AddProduct,
                [DataTypeIds.String, DataTypeIds.String, DataTypeIds.NodeId], returnsNode: true);
            NodeId created = session.Services.Id("created-product");
            session.Services.CallHandler = (_, _) => IndustrialCompanionTestSession.Good([Variant.From(created)]);
            WeighingCompanionProvider provider = session.Provider();

            CompanionTaskInput prepared = await provider.PrepareInputAsync(session.Context(), target, "add-product",
                [new("productName", Variant.From("New product")), new("productId", Variant.From("p2"))],
                CancellationToken.None).ConfigureAwait(false);
            CompanionOperationResult result = await provider.ExecutePreparedAsync(session.Context(), target,
                "add-product", prepared, null, CancellationToken.None).ConfigureAwait(false);

            CallMethodRequest request = session.Services.Calls[0];
            Assert.That(request.ObjectId, Is.EqualTo(preset));
            Assert.That(request.InputArguments, Has.Count.EqualTo(3));
            Assert.That(request.InputArguments[0], Is.EqualTo(Variant.From("New product")));
            Assert.That(request.InputArguments[1], Is.EqualTo(Variant.From("p2")));
            Assert.That(request.InputArguments[2].TryGetValue(out NodeId type), Is.True);
            Assert.That(type.IsNull, Is.True);
            Assert.That(Field(result.Values, "Created node").Value, Is.EqualTo(Variant.From(created)));
        }

        [TestCase("start-recipe", "StartRecipe")]
        [TestCase("stop-recipe", "StopRecipe")]
        [TestCase("continue-recipe", "ContinueRecipe")]
        [TestCase("abort-recipe", "AbortRecipe")]
        [TestCase("skip-recipe-element", "SkipCurrentRecipeElement")]
        public async Task RecipeOperationsResolveAndPinARealRecipeRatherThanTakingAnArbitraryNodeAsync(
            string operation, string methodName)
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale(ScaleKind.Recipe);
            NodeId recipes = session.AddGroup(target.NodeId, Scales.BrowseNames.Recipes,
                Scales.ObjectTypeIds.RecipeManagementType);
            NodeId recipe = session.AddGroup(recipes, "RecipeOne", Scales.ObjectTypeIds.RecipeType);
            session.AddVariable(recipe, Scales.BrowseNames.RecipeId, Variant.From("r1"));
            NodeId method = session.AddMethod(target.NodeId, methodName, [DataTypeIds.NodeId]);
            WeighingCompanionProvider provider = session.Provider();

            CompanionTaskInput prepared = await provider.PrepareInputAsync(session.Context(), target, operation,
                [new CompanionValue("recipeId", Variant.From("r1"))], CancellationToken.None).ConfigureAwait(false);
            await provider.ExecutePreparedAsync(session.Context(), target, operation, prepared, null,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(session.Services.Calls, Has.Count.EqualTo(1));
            Assert.That(session.Services.Calls[0].ObjectId, Is.EqualTo(target.NodeId));
            Assert.That(session.Services.Calls[0].MethodId, Is.EqualTo(method));
            Assert.That(session.Services.Calls[0].InputArguments[0], Is.EqualTo(Variant.From(recipe)));
        }

        [Test]
        public async Task RegisterWeightDisplaysOnlyTheServersRegisteredReadbackAsync()
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale();
            session.AddWeight(target.NodeId);
            NodeId registered = session.AddWeight(target.NodeId, "RegisteredWeight", 44, 32, 12);
            session.AddMethod(target.NodeId, Scales.BrowseNames.RegisterWeight);
            WeighingCompanionProvider provider = session.Provider();

            CompanionTaskInput prepared = await provider.PrepareInputAsync(session.Context(), target,
                "register-weight", [], CancellationToken.None).ConfigureAwait(false);
            CompanionOperationResult result = await provider.ExecutePreparedAsync(session.Context(), target,
                "register-weight", prepared, null, CancellationToken.None).ConfigureAwait(false);

            CompanionValue net = Field(result.Values, "RegisteredWeight / Net");
            Assert.That(net.Value, Is.EqualTo(Variant.From(32.0)));
            Assert.That(net.SourceNodeId, Is.EqualTo(registered));
            Assert.That(net.SourceTimestamp, Is.EqualTo((DateTime)session.Timestamp));
            Assert.That(session.Services.Calls, Has.Count.EqualTo(1));
        }

        [Test]
        public async Task RecipeManagementUsesTypedLocalizedNameAndExistingRecipeIdentityAsync()
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale(ScaleKind.Recipe);
            NodeId recipes = session.AddGroup(target.NodeId, Scales.BrowseNames.Recipes,
                Scales.ObjectTypeIds.RecipeManagementType);
            NodeId add = session.AddMethod(recipes, Scales.BrowseNames.AddRecipe,
                [DataTypeIds.String, DataTypeIds.LocalizedText], returnsNode: true);
            NodeId remove = session.AddMethod(recipes, Scales.BrowseNames.RemoveRecipe, [DataTypeIds.String]);
            NodeId recipe = session.Services.Id("created-recipe");
            session.Services.CallHandler = (request, _) => request.MethodId == add
                ? IndustrialCompanionTestSession.Good([Variant.From(recipe)])
                : IndustrialCompanionTestSession.Good();
            WeighingCompanionProvider provider = session.Provider();
            var recipeName = new LocalizedText("de", "Morgen");

            CompanionTaskInput create = await provider.PrepareInputAsync(session.Context(), target, "add-recipe",
                [new("recipeId", Variant.From("r1")), new("recipeName", Variant.From(recipeName))],
                CancellationToken.None).ConfigureAwait(false);
            CompanionOperationResult created = await provider.ExecutePreparedAsync(session.Context(), target,
                "add-recipe", create, null, CancellationToken.None).ConfigureAwait(false);
            session.Services.AddObject(recipe, Scales.ObjectTypeIds.RecipeType, "Morgen");
            session.Services.AddChild(recipes, recipe);
            session.AddVariable(recipe, Scales.BrowseNames.RecipeId, Variant.From("r1"));
            CompanionTaskInput delete = await provider.PrepareInputAsync(session.Context(), target, "remove-recipe",
                [new CompanionValue("recipeId", Variant.From("r1"))], CancellationToken.None).ConfigureAwait(false);
            await provider.ExecutePreparedAsync(session.Context(), target, "remove-recipe", delete, null,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(session.Services.Calls, Has.Count.EqualTo(2));
            Assert.That(session.Services.Calls[0].ObjectId, Is.EqualTo(recipes));
            Assert.That(session.Services.Calls[0].InputArguments[1], Is.EqualTo(Variant.From(recipeName)));
            Assert.That(Field(created.Values, "Created node").Value, Is.EqualTo(Variant.From(recipe)));
            Assert.That(session.Services.Calls[1].ObjectId, Is.EqualTo(recipes));
            Assert.That(session.Services.Calls[1].MethodId, Is.EqualTo(remove));
            Assert.That(session.Services.Calls[1].InputArguments[0], Is.EqualTo(Variant.From("r1")));
        }

        [TestCase("reference-piece-count", "SetNumberOfReferencePieces", 1u)]
        [TestCase("reference-piece-count", "SetNumberOfReferencePieces", 1_000_000u)]
        [TestCase("start-reference", "StartReference", 15u)]
        public async Task PieceCountingUsesTheGeneratedUnsignedInputOnTheCorrectKindAsync(
            string operation, string methodName, uint count)
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale(ScaleKind.PieceCounting);
            session.AddWeight(target.NodeId);
            NodeId method = session.AddMethod(target.NodeId, methodName, [DataTypeIds.UInt32]);
            WeighingCompanionProvider provider = session.Provider();

            CompanionTaskInput prepared = await provider.PrepareInputAsync(session.Context(), target, operation,
                [new CompanionValue("count", Variant.From(count))], CancellationToken.None).ConfigureAwait(false);
            await provider.ExecutePreparedAsync(session.Context(), target, operation, prepared, null,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(session.Services.Calls[0].MethodId, Is.EqualTo(method));
            Assert.That(session.Services.Calls[0].InputArguments[0], Is.EqualTo(Variant.From(count)));
        }

        [TestCase(0u)]
        [TestCase(1_000_001u)]
        public async Task PieceCountingRejectsValuesImmediatelyOutsideItsInputBoundsAsync(uint count)
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale(ScaleKind.PieceCounting);

            await Assert.ThatAsync(async () => await session.Provider().PrepareInputAsync(session.Context(), target,
                    "reference-piece-count", [new CompanionValue("count", Variant.From(count))],
                    CancellationToken.None).ConfigureAwait(false),
                Throws.TypeOf<ArgumentException>().With.Message.Contains("1–1000000")).ConfigureAwait(false);

            Assert.That(session.Services.Calls.IsEmpty, Is.True);
        }

        [TestCase("inbound-weighing", "InboundWeighing")]
        [TestCase("outbound-weighing", "OutboundWeighing")]
        [TestCase("one-pass-weighing", "OnePassWeighing")]
        public async Task VehicleWeighingPassesTheReviewedIdentifierToTheTypedMethodAsync(
            string operation, string methodName)
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale(ScaleKind.Vehicle);
            session.AddWeight(target.NodeId);
            NodeId method = session.AddMethod(target.NodeId, methodName, [DataTypeIds.String]);
            WeighingCompanionProvider provider = session.Provider();

            CompanionTaskInput prepared = await provider.PrepareInputAsync(session.Context(), target, operation,
                [new CompanionValue("vehicleId", Variant.From("vehicle-17"))], CancellationToken.None)
                .ConfigureAwait(false);
            await provider.ExecutePreparedAsync(session.Context(), target, operation, prepared, null,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(session.Services.Calls[0].MethodId, Is.EqualTo(method));
            Assert.That(session.Services.Calls[0].InputArguments[0], Is.EqualTo(Variant.From("vehicle-17")));
        }

        [Test]
        public async Task ScaleSystemStatisticsResetDoesNotRequireOrInventAnOwnWeightAsync()
        {
            var session = new WeighingTestSession();
            CompanionTarget target = session.AddScale(system: true);
            NodeId method = session.AddMethod(target.NodeId, Scales.BrowseNames.ResetGlobalStatistics);
            WeighingCompanionProvider provider = session.Provider();

            CompanionTaskInput prepared = await provider.PrepareInputAsync(
                session.Context(), target, "reset-statistics", [], CancellationToken.None).ConfigureAwait(false);
            await provider.ExecutePreparedAsync(session.Context(), target, "reset-statistics", prepared, null,
                CancellationToken.None).ConfigureAwait(false);

            Assert.That(session.Services.Calls, Has.Count.EqualTo(1));
            Assert.That(session.Services.Calls[0].MethodId, Is.EqualTo(method));
            Assert.That(session.Services.Calls[0].InputArguments.IsEmpty, Is.True);
        }

        private static (NodeId Preset, NodeId Product) AddProductFixture(
            WeighingTestSession session, CompanionTarget target, bool selected)
        {
            NodeId preset = session.AddGroup(target.NodeId, Scales.BrowseNames.ProductionPreset,
                Scales.ObjectTypeIds.ProductionPresetType);
            NodeId products = session.AddGroup(preset, Scales.BrowseNames.Products);
            NodeId product = session.AddGroup(products, "ProductOne", Scales.ObjectTypeIds.CheckweigherProductType);
            session.AddSubtype(session.Services.Resolve(Scales.ObjectTypeIds.CheckweigherProductType),
                session.Services.Resolve(Scales.ObjectTypeIds.ProductType));
            session.AddVariable(product, Scales.BrowseNames.ProductId, Variant.From("p1"));
            session.AddVariable(product, Scales.BrowseNames.ProductMode, Variant.From(selected));
            ArrayOf<string> current = selected ? ["p1"] : [];
            session.AddVariable(preset, Scales.BrowseNames.CurrentProducts, Variant.From(current));
            return (preset, product);
        }

        private static readonly TestCaseData[] s_nonExecutableCases =
        [
            new(false, true, StatusCodes.BadInvalidState),
            new(true, false, StatusCodes.BadUserAccessDenied)
        ];
    }
}
