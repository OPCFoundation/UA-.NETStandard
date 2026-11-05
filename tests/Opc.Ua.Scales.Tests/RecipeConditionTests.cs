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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using Opc.Ua.PackML;
using Opc.Ua.Scales.Server.Builders;
using Opc.Ua.Scales.Server.Runtime;
using Opc.Ua.Tests;

namespace Opc.Ua.Scales.Tests
{
    /// <summary>
    /// Deterministic local-source condition wait execution and lifecycle tests.
    /// </summary>
    [TestFixture]
    [Category("Scales")]
    [NonParallelizable]
    public sealed class RecipeConditionTests
    {
        /// <summary>
        /// Non-good condition source quality partitions.
        /// </summary>
        private static TestCaseData[] NonGoodQualityCases =>
        [
            new(StatusCodes.BadNoCommunication),
            new(StatusCodes.Uncertain)
        ];

        [SetUp]
        public async Task SetUpAsync()
        {
            m_expectedShutdownFailure = null;
            m_controller = null!;
            m_fixture = new ScalesServerFixture();
            await m_fixture.StartAsync().ConfigureAwait(false);
            ScaleHandle scale = await m_fixture.Manager.CreateScaleAsync(
                m_fixture.Name("ConditionScale"), ScaleKind.Recipe, b => b
                    .WithIdentification(ScalesNodeManagerTests.Identity("ConditionScale"))
                    .WithWeighingRange(new WeighingRangeDefinition(0, 100, 0.001, 0.001))
                    .WithTypeFeatures());
            m_scale = scale;
            m_controller = scale.Recipes!;
            m_clock = new RecipeClock();
            m_recipe = m_controller.AddRecipe("R", new LocalizedText("Recipe"));
        }

        [TearDown]
        public async Task TearDownAsync()
        {
            m_controller?.Dispose();
            if (m_expectedShutdownFailure == null)
            {
                await m_fixture.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                AggregateException? failure = await Assert.ThrowsAsync<AggregateException>(
                    async () => await m_fixture.DisposeAsync().ConfigureAwait(false)).ConfigureAwait(false);
                var failures = failure!.Flatten().InnerExceptions;
                Assert.That(failures, Has.Count.EqualTo(1));
                Assert.That(failures[0], Is.SameAs(m_expectedShutdownFailure));
            }
        }

        /// <summary>
        /// Rejects optional runtime configuration on a builder without that capability.
        /// </summary>
        [Test]
        public void LegacyBuilderRejectsOptionalRuntimeConfiguration()
        {
            IScaleBuilder builder = Mock.Of<IScaleBuilder>();

            Assert.That(() => builder.WithItemProcessing(), Throws.TypeOf<NotSupportedException>());
            Assert.That(() => builder.WithCompletedFills(), Throws.TypeOf<NotSupportedException>());
            Assert.That(() => builder.ConfigureRecipes(_ => { }), Throws.TypeOf<NotSupportedException>());
            Assert.That(() => builder.WithRecipeReleaseCheck(() => true), Throws.TypeOf<NotSupportedException>());
        }

        /// <summary>
        /// Forwards optional configuration to the builder's runtime capability.
        /// </summary>
        [Test]
        public void OptionalRuntimeBuilderExtensionsForwardConfiguration()
        {
            var runtime = new Mock<IScaleRuntimeBuilder>(MockBehavior.Strict);
            IScaleBuilder builder = runtime.Object;
            Action<RecipeController> configure = _ => { };
            runtime.Setup(value => value.WithItemProcessing(null)).Returns(builder);
            runtime.Setup(value => value.WithCompletedFills()).Returns(builder);
            runtime.Setup(value => value.ConfigureRecipes(configure)).Returns(builder);

            Assert.That(builder.WithItemProcessing(), Is.SameAs(builder));
            Assert.That(builder.WithCompletedFills(), Is.SameAs(builder));
            Assert.That(builder.ConfigureRecipes(configure), Is.SameAs(builder));
            runtime.VerifyAll();
        }

        /// <summary>
        /// Rejects null builders and callbacks with the corresponding parameter names.
        /// </summary>
        [Test]
        public void RuntimeBuilderExtensionsRejectNullArguments()
        {
            IScaleBuilder builder = Mock.Of<IScaleBuilder>();
            IScaleBuilder missingBuilder = null!;

            Assert.That(Assert.Throws<ArgumentNullException>(
                () => missingBuilder.WithItemProcessing())!.ParamName, Is.EqualTo("builder"));
            Assert.That(Assert.Throws<ArgumentNullException>(
                () => missingBuilder.WithCompletedFills())!.ParamName, Is.EqualTo("builder"));
            Assert.That(Assert.Throws<ArgumentNullException>(
                () => missingBuilder.ConfigureRecipes(_ => { }))!.ParamName, Is.EqualTo("builder"));
            Assert.That(Assert.Throws<ArgumentNullException>(
                () => missingBuilder.WithRecipeReleaseCheck(() => true))!.ParamName, Is.EqualTo("builder"));
            Assert.That(Assert.Throws<ArgumentNullException>(
                () => builder.ConfigureRecipes(null!))!.ParamName, Is.EqualTo("configure"));
            Assert.That(Assert.Throws<ArgumentNullException>(
                () => builder.WithRecipeReleaseCheck(null!))!.ParamName, Is.EqualTo("isEnabledAndReleased"));
        }

        /// <summary>
        /// Preserves the configured release check when a null replacement is rejected.
        /// </summary>
        [Test]
        public void ApplicationReleaseCheckRejectsNullCallbackWithoutReplacingConfiguredCheck()
        {
            m_controller.SetApplicationReleaseCheck(() => false);

            ArgumentNullException? error = Assert.Throws<ArgumentNullException>(
                () => m_controller.SetApplicationReleaseCheck(null!));

            Assert.That(error!.ParamName, Is.EqualTo("isEnabledAndReleased"));
            Assert.That(m_controller.Start(m_recipe.NodeId).StatusCode,
                Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
        }

        [Test]
        public void DisabledConditionWaitsRetainManualExecution()
        {
            AnalogConditionSleepState element = Analog(0, 10.0);
            RecipeElementState? started = null;
            m_controller.ElementStarted += (_, args) => started = args.Element;
            AssertGood(m_controller.Start(m_recipe.NodeId));
            Assert.That(started, Is.SameAs(element));
            Assert.That(m_controller.ConditionWaitsEnabled, Is.False);
            Assert.That(m_controller.CurrentElements.ToArray(), Does.Contain(element));
            AssertGood(m_controller.CompleteElement(element));
            Assert.That(m_controller.State, Is.EqualTo(RecipeRunState.Idle));
        }

        /// <summary>
        /// Leaves the recipe idle when the application rejects its release check.
        /// </summary>
        [Test]
        public void ApplicationReleaseCheckRejectsStartWhenNotReleased()
        {
            AnalogConditionSleepState element = Analog(0, 10.0);
            int calls = 0;
            m_controller.SetApplicationReleaseCheck(() =>
            {
                calls++;
                return false;
            });

            ServiceResult result = m_controller.Start(m_recipe.NodeId);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(m_controller.State, Is.EqualTo(RecipeRunState.Idle));
            Assert.That(m_controller.CurrentElements.ToArray(), Does.Not.Contain(element));
        }

        /// <summary>
        /// Starts the recipe when the application permits processing.
        /// </summary>
        [Test]
        public void ApplicationReleaseCheckAllowsStartWhenReleased()
        {
            AnalogConditionSleepState element = Analog(0, 10.0);
            int calls = 0;
            m_controller.SetApplicationReleaseCheck(() =>
            {
                calls++;
                return true;
            });

            AssertGood(m_controller.Start(m_recipe.NodeId));

            Assert.That(calls, Is.EqualTo(1));
            Assert.That(m_controller.State, Is.EqualTo(RecipeRunState.Running));
            Assert.That(m_controller.CurrentElements.ToArray(), Does.Contain(element));
        }

        /// <summary>
        /// Rejects a local start after callback replacement, including restoration of the original delegate.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task LocalStartRejectsReleaseCheckReplacedBeforeCommitAsync(bool restoreOriginal)
        {
            AnalogConditionSleepState element = Analog(0, 10.0);
            using var callbackEntered = new ManualResetEventSlim(false);
            using var releaseCallback = new ManualResetEventSlim(false);
            bool callbackAllowed = false;
            Func<bool> originalCheck = () =>
            {
                callbackEntered.Set();
                callbackAllowed = releaseCallback.Wait(TimeSpan.FromSeconds(20));
                return callbackAllowed;
            };
            m_controller.SetApplicationReleaseCheck(originalCheck);

            Task<ServiceResult> start = Task.Run(() => m_controller.Start(m_recipe.NodeId));
            bool entered = callbackEntered.Wait(TimeSpan.FromSeconds(5));
            Task? replaceCheck = entered
                ? Task.Run(() =>
                {
                    m_controller.SetApplicationReleaseCheck(() => false);
                    if (restoreOriginal)
                    {
                        m_controller.SetApplicationReleaseCheck(originalCheck);
                    }
                })
                : null;
            bool replaced = false;
            try
            {
                if (replaceCheck is not null)
                {
                    await replaceCheck.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    replaced = true;
                }
            }
            catch (TimeoutException)
            {
            }
            finally
            {
                releaseCallback.Set();
            }
            if (replaceCheck is not null)
            {
                await replaceCheck.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
            }

            ServiceResult result = await start.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);

            Assert.That(entered, Is.True);
            Assert.That(replaced, Is.True);
            Assert.That(callbackAllowed, Is.True);
            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            Assert.That(m_controller.State, Is.EqualTo(RecipeRunState.Idle));
            Assert.That(m_controller.CurrentElements.ToArray(), Does.Not.Contain(element));
        }

        /// <summary>
        /// Applies the application release check supplied through the fluent builder.
        /// </summary>
        [Test]
        public async Task FluentRecipeReleaseCheckRejectsStartWhenNotReleased()
        {
            int calls = 0;
            ScaleHandle scale = await m_fixture.Manager.CreateScaleAsync(
                m_fixture.Name("FluentReleaseScale"),
                ScaleKind.Recipe,
                builder => builder
                    .WithIdentification(ScalesNodeManagerTests.Identity("FluentReleaseScale"))
                    .WithWeighingRange(new WeighingRangeDefinition(0, 100, 0.001, 0.001))
                    .WithRecipeReleaseCheck(() =>
                    {
                        calls++;
                        return false;
                    }));
            RecipeController recipes = scale.Recipes!;
            RecipeState recipe = recipes.AddRecipe("Fluent", new LocalizedText("Fluent"));
            recipes.AddRecipeElement(
                recipe,
                new NodeId(ObjectTypes.TimerType, m_fixture.Manager.NamespaceIndices.Scales),
                "Wait",
                recipe.NodeId);

            ServiceResult result = recipes.Start(recipe.NodeId);

            Assert.That(result.StatusCode, Is.EqualTo((StatusCode)StatusCodes.BadInvalidState));
            Assert.That(calls, Is.EqualTo(1));
            Assert.That(recipes.State, Is.EqualTo(RecipeRunState.Idle));
        }

        /// <summary>
        /// Uses PackML state rather than the application callback when PackML is configured.
        /// </summary>
        [Test]
        public async Task PackMLStartUsesPackMLStateInsteadOfApplicationReleaseCheck()
        {
            int calls = 0;
            ScaleHandle scale = await m_fixture.Manager.CreateScaleAsync(
                m_fixture.Name("PackMLReleaseScale"),
                ScaleKind.Recipe,
                builder => builder
                    .WithIdentification(ScalesNodeManagerTests.Identity("PackMLReleaseScale"))
                    .WithWeighingRange(new WeighingRangeDefinition(0, 100, 0.001, 0.001))
                    .WithPackMLState()
                    .WithRecipeReleaseCheck(() =>
                    {
                        calls++;
                        return false;
                    }));
            RecipeController recipes = scale.Recipes!;
            RecipeState recipe = recipes.AddRecipe("PackML", new LocalizedText("PackML"));
            recipes.AddRecipeElement(
                recipe,
                new NodeId(ObjectTypes.TimerType, m_fixture.Manager.NamespaceIndices.Scales),
                "Wait",
                recipe.NodeId);

            AssertGood(recipes.Start(recipe.NodeId));

            Assert.That(calls, Is.Zero);
            Assert.That(scale.PackML!.CurrentState, Is.EqualTo(PackMLStateNumbers.Idle));
            Assert.That(recipes.State, Is.EqualTo(RecipeRunState.Running));
            AssertGood(recipes.Abort(recipe.NodeId));
        }

        [TestCase(0, 10.0, true)]
        [TestCase(0, 9.0, false)]
        [TestCase(1, 10.0, false)]
        [TestCase(1, 11.0, true)]
        [TestCase(2, 10.0, true)]
        [TestCase(2, 11.0, false)]
        [TestCase(3, 10.0, true)]
        [TestCase(3, 9.0, false)]
        [TestCase(4, 10.0, false)]
        [TestCase(4, 9.0, true)]
        [TestCase(5, 10.0, false)]
        [TestCase(5, 11.0, true)]
        public void AnalogOperatorsObserveEqualityAndAdjacentBoundaries(int mode, double sample, bool completes)
        {
            Enable(BuiltInType.Double);
            AnalogConditionSleepState element = Analog(mode, 10.0);
            AssertGood(m_controller.Start(m_recipe.NodeId));
            Publish(sample);
            Assert.That(m_controller.State, Is.EqualTo(completes ? RecipeRunState.Idle : RecipeRunState.Running));
            Assert.That(m_controller.CurrentElements.Contains(element), Is.EqualTo(!completes));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void RegisteredSourceResolvesByIdOrLocalNodeId(bool useNodeId)
        {
            Enable(BuiltInType.Double);
            AnalogConditionSleepState element = Analog(0, 10.0);
            if (useNodeId)
            {
                element.ThresholdValueId!.Value = string.Empty;
                element.AddThresholdValueNodeId(m_fixture.Context);
                element.ThresholdValueNodeId!.Value = SourceNodeId;
            }
            Publish(10.0);
            AssertGood(m_controller.Start(m_recipe.NodeId));
            Assert.That(
                m_controller.State,
                Is.EqualTo(RecipeRunState.Idle),
                "a fresh activation snapshot may satisfy analog waits");
            RecipeThresholdType advertised = m_controller.Scale.SupportedThresholdValues!.Value.ToArray()!.Single();
            Assert.That(advertised.ThresholdId, Is.EqualTo(7));
            Assert.That(advertised.ThresholdNodeId, Is.EqualTo(SourceNodeId));
        }

        [TestCase("unknown")]
        [TestCase("unsupported")]
        [TestCase("conflicting")]
        public void UnregisteredUnadvertisedAndConflictingSourcesRequireIntervention(string invalid)
        {
            Enable(BuiltInType.Double);
            AnalogConditionSleepState element = Analog(0, 10.0);
            if (invalid == "unknown")
            {
                element.ThresholdValueId!.Value = "8";
            }
            else if (invalid == "unsupported")
            {
                m_controller.Scale.SupportedThresholdValues!.Value = [];
            }
            else
            {
                element.AddThresholdValueNodeId(m_fixture.Context);
                element.ThresholdValueNodeId!.Value = new NodeId("RemoteOrUnregistered", 77);
            }
            AssertGood(m_controller.Start(m_recipe.NodeId));
            Publish(10.0);
            Assert.That(m_controller.CurrentElements.ToArray(), Does.Contain(element));
            Assert.That(m_controller.ConditionWaitsRequiringIntervention.ToArray(), Does.Contain(element));
        }

        [TestCase("unit")]
        [TestCase("type")]
        [TestCase("operator")]
        [TestCase("nonfinite")]
        public void IncompatibleAnalogConfigurationRequiresIntervention(string invalid)
        {
            Enable(BuiltInType.Double);
            AnalogConditionSleepState element = Analog(0, 10.0);
            if (invalid == "unit")
            {
                element.TargetThresholdValue!.EngineeringUnits!.Value = ScaleUnits.Gram;
            }
            else if (invalid == "type")
            {
                element.TargetThresholdValue!.WrappedValue = new Variant("10");
            }
            else if (invalid == "operator")
            {
                element.ConditionMode!.Value = (EqualityAndRelationalOperator)99;
            }
            else
            {
                element.TargetThresholdValue!.WrappedValue = new Variant(double.NaN);
            }
            AssertGood(m_controller.Start(m_recipe.NodeId));
            Publish(10.0);
            Assert.That(m_controller.ConditionWaitsRequiringIntervention.ToArray(), Does.Contain(element));
        }

        [Test]
        public void ConditionConfigurationRequiresPositiveAgeUniqueSourcesAndIdleController()
        {
            Assert.That(() => m_controller.EnableConditionWaits(TimeSpan.Zero),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => m_controller.EnableConditionWaits(TimeSpan.FromTicks(-1)),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => m_controller.RegisterConditionSource(
                7, SourceNodeId, new LocalizedText("Text"), BuiltInType.String),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Enable(BuiltInType.Double);
            Assert.That(() => m_controller.RegisterConditionSource(
                7, NodeId.Null, new LocalizedText("Duplicate id"), BuiltInType.Double),
                Throws.TypeOf<ArgumentException>());
            Assert.That(() => m_controller.RegisterConditionSource(
                8, SourceNodeId, new LocalizedText("Duplicate node"), BuiltInType.Double),
                Throws.TypeOf<ArgumentException>());
            Analog(0, 10.0);
            AssertGood(m_controller.Start(m_recipe.NodeId));
            Assert.That(() => m_controller.EnableConditionWaits(TimeSpan.FromSeconds(1)),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => m_controller.RegisterConditionSource(
                9, NodeId.Null, new LocalizedText("Late source"), BuiltInType.Double),
                Throws.TypeOf<InvalidOperationException>());
        }

        [TestCase("source")]
        [TestCase("operator")]
        [TestCase("target")]
        [TestCase("targetType")]
        public void IncompatibleEdgeConfigurationRequiresIntervention(string invalid)
        {
            Enable(invalid == "source" ? BuiltInType.Double : BuiltInType.Boolean);
            EdgeTriggeredSleepState edge = Edge(0);
            if (invalid == "operator")
            {
                edge.ConditionMode!.Value = (EdgeOperator)99;
            }
            else if (invalid == "target")
            {
                edge.TargetThresholdValue!.WrappedValue = new Variant(false);
            }
            else if (invalid == "targetType")
            {
                edge.TargetThresholdValue!.WrappedValue = new Variant(1);
            }
            AssertGood(m_controller.Start(m_recipe.NodeId));
            Assert.That(m_controller.ConditionWaitsRequiringIntervention.ToArray(), Does.Contain(edge));
            Assert.That(m_controller.State, Is.EqualTo(RecipeRunState.Running));
        }

        [TestCase(-1.0)]
        [TestCase(double.NaN)]
        [TestCase(double.PositiveInfinity)]
        [TestCase(double.MaxValue)]
        [TestCase(9.223372036854776E14)]
        public void InvalidConditionTimeoutRequiresIntervention(double timeout)
        {
            Enable(BuiltInType.Double);
            AnalogConditionSleepState element = Analog(0, 10.0);
            element.AddTimeout(m_fixture.Context);
            element.Timeout!.Value = timeout;
            AssertGood(m_controller.Start(m_recipe.NodeId));
            Assert.That(m_controller.ConditionWaitsRequiringIntervention.ToArray(), Does.Contain(element));
            Publish(10.0);
            Assert.That(m_controller.State, Is.EqualTo(RecipeRunState.Running));
        }

        [Test]
        public void IntegralComparisonsRetainUInt64Precision()
        {
            Enable(BuiltInType.UInt64);
            AnalogConditionSleepState element = Analog(5, new Variant(ulong.MaxValue - 1));
            AssertGood(m_controller.Start(m_recipe.NodeId));
            Publish(new Variant(ulong.MaxValue - 1));
            Assert.That(m_controller.CurrentElements.ToArray(), Does.Contain(element));
            Publish(new Variant(ulong.MaxValue));
            Assert.That(m_controller.State, Is.EqualTo(RecipeRunState.Idle));
        }

        [TestCase(BuiltInType.SByte)]
        [TestCase(BuiltInType.Byte)]
        [TestCase(BuiltInType.Int16)]
        [TestCase(BuiltInType.UInt16)]
        [TestCase(BuiltInType.Int32)]
        [TestCase(BuiltInType.UInt32)]
        [TestCase(BuiltInType.Int64)]
        [TestCase(BuiltInType.UInt64)]
        [TestCase(BuiltInType.Float)]
        [TestCase(BuiltInType.Double)]
        public void AllNumericScalarsCompareAcrossCompatibleTypes(BuiltInType type)
        {
            Enable(type);
            AnalogConditionSleepState element = Analog(0, new Variant(10));
            Variant sample = type switch
            {
                BuiltInType.SByte => new Variant((sbyte)10),
                BuiltInType.Byte => new Variant((byte)10),
                BuiltInType.Int16 => new Variant((short)10),
                BuiltInType.UInt16 => new Variant((ushort)10),
                BuiltInType.Int32 => new Variant(10),
                BuiltInType.UInt32 => new Variant(10u),
                BuiltInType.Int64 => new Variant(10L),
                BuiltInType.UInt64 => new Variant(10UL),
                BuiltInType.Float => new Variant(10f),
                _ => new Variant(10.0)
            };
            AssertGood(m_controller.Start(m_recipe.NodeId));
            Publish(sample);
            Assert.That(m_controller.CurrentElements.ToArray(), Does.Not.Contain(element));
            Assert.That(m_controller.State, Is.EqualTo(RecipeRunState.Idle));
        }

        [Test]
        public void MixedIntegralAndFloatingComparisonDoesNotRoundLargeIntegers()
        {
            Enable(BuiltInType.UInt64);
            AnalogConditionSleepState element = Analog(5, new Variant(9007199254740992.0));
            AssertGood(m_controller.Start(m_recipe.NodeId));
            Publish(new Variant(9007199254740992UL));
            Assert.That(m_controller.CurrentElements.ToArray(), Does.Contain(element));
            Publish(new Variant(9007199254740993UL));
            Assert.That(m_controller.State, Is.EqualTo(RecipeRunState.Idle));
        }

        [TestCase(0, false, true)]
        [TestCase(1, true, false)]
        public void BooleanEdgesRequireTransitionAfterActivation(int mode, bool before, bool after)
        {
            Enable(BuiltInType.Boolean);
            Publish(after);
            EdgeTriggeredSleepState edge = Edge(mode);
            AssertGood(m_controller.Start(m_recipe.NodeId));
            Publish(after);
            Assert.That(
                m_controller.CurrentElements.ToArray(),
                Does.Contain(edge),
                "initial true is not a rising edge");
            Publish(before);
            Assert.That(
                m_controller.CurrentElements.ToArray(),
                Does.Contain(edge),
                "the opposite edge does not satisfy this wait");
            Publish(after);
            Assert.That(m_controller.State, Is.EqualTo(RecipeRunState.Idle));
        }

        [TestCase("bad")]
        [TestCase("uncertain")]
        [TestCase("overflow")]
        [TestCase("gap")]
        [TestCase("stale")]
        [TestCase("future")]
        [TestCase("unordered")]
        [TestCase("type")]
        [TestCase("missingTimestamp")]
        public void InvalidObservationsCannotBridgeBooleanEdges(string invalid)
        {
            Enable(BuiltInType.Boolean);
            Publish(false);
            EdgeTriggeredSleepState edge = Edge(0);
            AssertGood(m_controller.Start(m_recipe.NodeId));
            m_clock.Advance(TimeSpan.FromMilliseconds(1));
            DateTime timestamp = m_clock.GetUtcNow().UtcDateTime;
            StatusCode quality = StatusCodes.Good;
            Variant value = new(true);
            if (invalid == "bad")
            {
                quality = StatusCodes.BadNoCommunication;
            }
            else if (invalid == "uncertain")
            {
                quality = StatusCodes.Uncertain;
            }
            else if (invalid == "overflow")
            {
                quality = quality.SetOverflow(true);
            }
            else if (invalid == "stale")
            {
                m_clock.Advance(TimeSpan.FromSeconds(6));
            }
            else if (invalid == "future")
            {
                timestamp = timestamp.AddSeconds(1);
            }
            else if (invalid == "unordered")
            {
                timestamp = timestamp.AddSeconds(-1);
            }
            else if (invalid == "type")
            {
                value = new Variant(1);
            }
            else if (invalid == "missingTimestamp")
            {
                timestamp = DateTime.MinValue;
            }
            var sample = new DataValue(value, quality, (DateTimeUtc)timestamp);
            m_controller.ObserveConditionSource(7, sample, invalid == "gap");
            Publish(true);
            Assert.That(m_controller.CurrentElements.ToArray(), Does.Contain(edge));
            Publish(false);
            Publish(true);
            Assert.That(m_controller.State, Is.EqualTo(RecipeRunState.Idle), "fresh contiguous edges recover");
        }

        [TestCaseSource(nameof(NonGoodQualityCases))]
        public void AnalogWaitRejectsNonGoodQuality(StatusCode quality)
        {
            Enable(BuiltInType.Double);
            AnalogConditionSleepState element = Analog(0, 10.0);
            AssertGood(m_controller.Start(m_recipe.NodeId));
            m_clock.Advance(TimeSpan.FromMilliseconds(1));
            var value = new DataValue(new Variant(10.0), quality, (DateTimeUtc)m_clock.GetUtcNow().UtcDateTime);
            m_controller.ObserveConditionSource(7, value);
            Assert.That(m_controller.CurrentElements.ToArray(), Does.Contain(element));
            Publish(10.0);
            Assert.That(m_controller.State, Is.EqualTo(RecipeRunState.Idle));
        }

        [Test]
        public void TimeoutLatchesInterventionInsteadOfAutomaticSuccess()
        {
            Enable(BuiltInType.Double);
            AnalogConditionSleepState element = Analog(0, 10.0);
            element.AddTimeout(m_fixture.Context);
            element.Timeout!.Value = 1000;
            int completed = 0;
            m_controller.RecipeCompleted += (_, _) => completed++;
            AssertGood(m_controller.Start(m_recipe.NodeId));
            m_clock.Advance(TimeSpan.FromMilliseconds(999));
            Assert.That(m_controller.ConditionWaitsRequiringIntervention.ToArray(), Is.Empty);
            m_clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.That(m_controller.ConditionWaitsRequiringIntervention.ToArray(), Does.Contain(element));
            Publish(10.0);
            Assert.That(completed, Is.Zero);
            Assert.That(m_controller.CurrentElements.ToArray(), Does.Contain(element));
            AssertGood(m_controller.CompleteElement(element));
            Assert.That(completed, Is.EqualTo(1), "explicit intervention can complete the element");
            Assert.That(m_controller.ConditionWaitsRequiringIntervention.ToArray(), Is.Empty);
        }

        [Test]
        public async Task DrainPendingEvaluationWaitsForScaleWriteAdmissionAsync()
        {
            ScaleHandle scale = await m_fixture.Manager.CreateScaleAsync(
                m_fixture.Name("AdmissionConditionScale"), ScaleKind.Recipe, b => b
                    .WithIdentification(ScalesNodeManagerTests.Identity("AdmissionConditionScale"))
                    .WithWeighingRange(new WeighingRangeDefinition(0, 100, 0.001, 0.001))
                    .WithTypeFeatures()
                    .WithProductionPreset(p => p.AddProduct("A", new LocalizedText("First")).Select("A")));
            m_scale = scale;
            m_controller = scale.Recipes!;
            m_recipe = m_controller.AddRecipe("Admission", new LocalizedText("Admission recipe"));
            Enable(BuiltInType.Double);
            Publish(10.0);
            AnalogConditionSleepState element = Analog(0, 10.0);
            var product = (RecipeProductState)scale.ProductionPreset!.ActiveProduct!;
            var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_controller.RecipeCompleted += (_, _) => completed.TrySetResult(true);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<bool> write = m_scale.WriteOperations.ExecuteAsynchronouslyAsync(
                async cancellationToken =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    return true;
                },
                CancellationToken.None).AsTask();

            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                AssertGood(m_controller.Start(m_recipe.NodeId));
                int reportCount = product.Report!.Value.Count;
                Assert.That(m_controller.CurrentElements.ToArray(), Does.Contain(element));
                Assert.That(completed.Task.IsCompleted, Is.False);
                Assert.That(product.Report.Value.Count, Is.EqualTo(reportCount));
            }
            finally
            {
                release.TrySetResult(true);
            }

            await write.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(m_controller.State, Is.EqualTo(RecipeRunState.Idle));
            Assert.That(product.Report!.Value.Contains(
                entry => entry.ReportMessage.Text?.Contains("condition satisfied", StringComparison.Ordinal) == true),
                Is.True);
        }

        [Test]
        public async Task QueuedObservationsPreserveBooleanEdgesAsync()
        {
            Enable(BuiltInType.Boolean);
            Publish(false);
            EdgeTriggeredSleepState edge = Edge(0);
            var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_controller.RecipeCompleted += (_, _) => completed.TrySetResult(true);
            AssertGood(m_controller.Start(m_recipe.NodeId));
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<bool> write = m_scale.WriteOperations.ExecuteAsynchronouslyAsync(
                async cancellationToken =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    return true;
                },
                CancellationToken.None).AsTask();

            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Publish(true);
                Publish(false);
                Assert.That(m_controller.CurrentElements.ToArray(), Does.Contain(edge));
                Assert.That(completed.Task.IsCompleted, Is.False);
            }
            finally
            {
                release.TrySetResult(true);
            }

            await write.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            Assert.That(m_controller.State, Is.EqualTo(RecipeRunState.Idle));
        }

        [Test]
        public async Task TimeoutEventWaitsForScaleWriteAdmissionAsync()
        {
            Enable(BuiltInType.Double);
            AnalogConditionSleepState element = Analog(0, 10.0);
            element.AddTimeout(m_fixture.Context);
            element.Timeout!.Value = 1000;
            AssertGood(m_controller.Start(m_recipe.NodeId));
            var eventReported = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            NodeState source = m_scale.Scale;
            NodeStateReportEventHandler? previous = source.OnReportEvent;
            int reported = 0;
            source.OnReportEvent = (context, node, e) =>
            {
                previous?.Invoke(context, node, e);
                reported++;
                eventReported.TrySetResult(true);
            };
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Task<bool> write = m_scale.WriteOperations.ExecuteAsynchronouslyAsync(
                async cancellationToken =>
                {
                    entered.TrySetResult(true);
                    await release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
                    return true;
                },
                CancellationToken.None).AsTask();

            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                m_clock.Advance(TimeSpan.FromMilliseconds(1000));
                Assert.That(m_controller.ConditionWaitsRequiringIntervention.ToArray(), Is.Empty);
                Assert.That(eventReported.Task.IsCompleted, Is.False);
                release.TrySetResult(true);
                await write.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                await eventReported.Task.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                Assert.That(m_controller.ConditionWaitsRequiringIntervention.ToArray(), Does.Contain(element));
                Assert.That(reported, Is.EqualTo(1));
            }
            finally
            {
                release.TrySetResult(true);
                source.OnReportEvent = previous;
            }
        }

        [Test]
        public async Task AutomaticConditionReportsRemainWithTheStartingProductAsync()
        {
            ScaleHandle scale = await m_fixture.Manager.CreateScaleAsync(
                m_fixture.Name("OwnedConditionScale"), ScaleKind.Recipe, b => b
                    .WithIdentification(ScalesNodeManagerTests.Identity("OwnedConditionScale"))
                    .WithWeighingRange(new WeighingRangeDefinition(0, 100, 0.001, 0.001))
                    .WithTypeFeatures()
                    .WithProductionPreset(p => p.AllowSelection()
                        .AddProduct("A", new LocalizedText("First"))
                        .AddProduct("B", new LocalizedText("Second")).Select("A")));
            m_controller = scale.Recipes!;
            m_recipe = m_controller.AddRecipe("Owned", new LocalizedText("Owned recipe"));
            Enable(BuiltInType.Double);
            AnalogConditionSleepState element = Analog(0, 10.0);
            element.AddTimeout(m_fixture.Context);
            element.Timeout!.Value = 1000;
            var first = (RecipeProductState)scale.ProductionPreset!.ActiveProduct!;
            AssertGood(m_controller.Start(m_recipe.NodeId));
            int firstCount = first.Report!.Value.Count;
            AssertGood(scale.ProductionPreset.Switch("B"));
            var second = (RecipeProductState)scale.ProductionPreset.ActiveProduct!;
            int secondCount = second.Report!.Value.Count;
            m_clock.Advance(TimeSpan.FromSeconds(1));
            Assert.That(first.Report.Value.Count, Is.GreaterThan(firstCount));
            Assert.That(first.Report.Value.Contains(
                entry => entry.ReportMessage.Text?.Contains("intervention", StringComparison.Ordinal) == true),
                Is.True);
            Assert.That(second.Report.Value.Count, Is.EqualTo(secondCount));
            AssertGood(m_controller.CompleteElement(element));
            Assert.That(first.Report.Value.Contains(
                entry => entry.ReportMessage.Text?.Contains("completed", StringComparison.Ordinal) == true), Is.True);
            Assert.That(second.Report.Value.Count, Is.EqualTo(secondCount));
        }

        [Test]
        public void ActivationSnapshotsSurviveClientEditsAndForkJoinWaitsForBothBranches()
        {
            Enable(BuiltInType.Double);
            AnalogConditionSleepState first = Analog(3, 10.0);
            AnalogConditionSleepState second = Analog(3, 20.0, "Second");
            RecipeElementState join = m_controller.AddRecipeElement(
                m_recipe, ElementType(ObjectTypes.TimerType), "Join", first.NodeId, second.NodeId);
            AssertGood(m_controller.Start(m_recipe.NodeId));
            second.TargetThresholdValue!.WrappedValue = new Variant(1.0);
            second.ConditionMode!.Value = EqualityAndRelationalOperator.LessThan_4;
            second.ThresholdValueId!.Value = "unknown";
            Publish(10.0);
            Assert.That(m_controller.CurrentElements.ToArray(), Does.Not.Contain(first));
            Assert.That(m_controller.CurrentElements.ToArray(), Does.Contain(second));
            Assert.That(m_controller.CurrentElements.ToArray(), Does.Not.Contain(join));
            Publish(20.0);
            Assert.That(m_controller.CurrentElements.ToArray(), Is.EqualTo(new[] { join }));
            AssertGood(m_controller.CompleteElement(join));
            Assert.That(m_controller.State, Is.EqualTo(RecipeRunState.Idle));
        }

        [Test]
        public void LongChainOfSatisfiedConditionsCompletesWithoutRecursion()
        {
            const int length = 2000;
            Enable(BuiltInType.Double);
            NodeId predecessor = m_recipe.NodeId;
            for (int i = 0; i < length; i++)
            {
                var element = (AnalogConditionSleepState)m_controller.AddRecipeElement(
                    m_recipe, ElementType(ObjectTypes.AnalogConditionSleepType), "Wait" + i, predecessor);
                element.ThresholdValueId!.Value = "7";
                element.TargetThresholdValue!.WrappedValue = new Variant(10.0);
                element.TargetThresholdValue.EngineeringUnits!.Value = ScaleUnits.Kilogram;
                element.ConditionMode!.Value = EqualityAndRelationalOperator.Equal_0;
                predecessor = element.NodeId;
            }
            Publish(10.0);
            int started = 0;
            int completed = 0;
            bool lockFreeDuringNotification = true;
            m_controller.ElementStarted += (_, _) =>
            {
                // Probe the first and last notification: another thread must be able to take the controller lock.
                if (started++ % (length - 1) == 0)
                {
                    var probe = new Thread(() => _ = m_controller.CurrentElements) { IsBackground = true };
                    probe.Start();
                    lockFreeDuringNotification &= probe.Join(TimeSpan.FromSeconds(10));
                }
            };
            m_controller.RecipeCompleted += (_, _) => completed++;
            ServiceResult? result = null;
            Exception? failure = null;

            // A small fixed stack turns any per-element recursion into a deterministic failure.
            var thread = new Thread(
                () =>
                {
                    try
                    {
                        result = m_controller.Start(m_recipe.NodeId);
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                    }
                },
                256 * 1024);
            thread.Start();
            thread.Join();

            Assert.That(failure, Is.Null);
            AssertGood(result!);
            Assert.That(started, Is.EqualTo(length));
            Assert.That(completed, Is.EqualTo(1));
            Assert.That(lockFreeDuringNotification, Is.True);
            Assert.That(m_controller.State, Is.EqualTo(RecipeRunState.Idle));
            Assert.That(m_controller.CurrentElements.ToArray(), Is.Empty);
        }

        [Test]
        public void TimerDrivenSubscriberFailuresAreLoggedAfterEveryNotification()
        {
            using var logs = new RecordingLoggerProvider(LogLevel.Error);
            m_fixture.Server.Telemetry.LoggerFactory.AddProvider(logs);
            Enable(BuiltInType.Double);
            AnalogConditionSleepState first = Analog(0, 10.0);
            AnalogConditionSleepState second = Analog(0, 10.0, "Second");
            foreach (AnalogConditionSleepState element in new[] { first, second })
            {
                element.AddTimeout(m_fixture.Context);
                element.Timeout!.Value = 1000;
            }
            AssertGood(m_controller.Start(m_recipe.NodeId));
            NodeState source = m_scale.Scale;
            NodeStateReportEventHandler? previous = source.OnReportEvent;
            int reported = 0;
            source.OnReportEvent = (context, node, e) =>
            {
                previous?.Invoke(context, node, e);
                reported++;
                throw new InvalidOperationException("Subscriber failed.");
            };
            try
            {
                Assert.That(() => m_clock.Advance(TimeSpan.FromMilliseconds(1000)), Throws.Nothing);
            }
            finally
            {
                source.OnReportEvent = previous;
            }

            RecordedLogRecord[] failures =
                [.. logs.Records.Where(r => r.EventId.Id == RecipeConditionEvaluationFailedEventId)];
            Assert.That(reported, Is.EqualTo(2), "the first failure does not stop the second notification");
            Assert.That(
                m_controller.ConditionWaitsRequiringIntervention.ToArray(),
                Is.EquivalentTo(new[] { first, second }));
            Assert.That(failures, Has.Length.EqualTo(1));
            Assert.That(failures[0].LogLevel, Is.EqualTo(LogLevel.Error));
            Assert.That(failures[0].Exception, Is.TypeOf<AggregateException>());
            Assert.That(
                ((AggregateException)failures[0].Exception!).InnerExceptions.Select(ex => ex.Message),
                Is.EqualTo(SubscriberFailureMessages));
            AssertGood(m_controller.CompleteElement(first));
            AssertGood(m_controller.CompleteElement(second));
            Assert.That(m_controller.State, Is.EqualTo(RecipeRunState.Idle));
        }

        [Test]
        public void SynchronousCallersStillObserveSubscriberFailures()
        {
            Enable(BuiltInType.Double);
            Publish(10.0);
            AnalogConditionSleepState first = Analog(0, 10.0);
            int completed = 0;
            m_controller.ElementStarted += (_, _) => throw new InvalidOperationException("Subscriber failed.");
            m_controller.RecipeCompleted += (_, _) => completed++;
            Assert.That(
                () => m_controller.Start(m_recipe.NodeId),
                Throws.InvalidOperationException.With.Message.EqualTo("Subscriber failed."));
            Assert.That(completed, Is.EqualTo(1), "notifications after the failing one are still delivered");
            Assert.That(m_controller.CurrentElements.ToArray(), Does.Not.Contain(first));
            Assert.That(m_controller.State, Is.EqualTo(RecipeRunState.Idle));
        }

        [Test]
        public async Task DisposeStopsDeliveryWithoutWaitingForARunningCallbackAsync()
        {
            Analog(0, 10.0);
            Analog(0, 10.0, "Second");
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            int started = 0;
            m_controller.ElementStarted += (_, _) =>
            {
                if (Interlocked.Increment(ref started) == 1)
                {
                    entered.Set();
                    release.Wait(TimeSpan.FromSeconds(10));
                }
            };
            Task<ServiceResult> start = Task.Run(() => m_controller.Start(m_recipe.NodeId));
            Assert.That(entered.Wait(TimeSpan.FromSeconds(10)), Is.True);

            m_controller.Dispose();

            Assert.That(start.IsCompleted, Is.False, "Dispose returns while the callback is still running");
            release.Set();
            AssertGood(await start.ConfigureAwait(false));
            Assert.That(started, Is.EqualTo(1), "the notification queued behind the running one is discarded");
            Assert.That(m_controller.State, Is.EqualTo(RecipeRunState.Idle));
            Assert.That(m_controller.CurrentElements.ToArray(), Is.Empty);
        }

        [Test]
        public async Task DisposeAsyncWaitsForARunningCallbackAsync()
        {
            Analog(0, 10.0);
            Analog(0, 10.0, "Second");
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            int started = 0;
            m_controller.ElementStarted += (_, _) =>
            {
                if (Interlocked.Increment(ref started) == 1)
                {
                    entered.Set();
                    release.Wait(TimeSpan.FromSeconds(10));
                }
            };
            Task<ServiceResult> start = Task.Run(() => m_controller.Start(m_recipe.NodeId));
            Assert.That(entered.Wait(TimeSpan.FromSeconds(10)), Is.True);

            Task disposed = m_controller.DisposeAsync().AsTask();
            await Task.Delay(100).ConfigureAwait(false);

            Assert.That(disposed.IsCompleted, Is.False, "disposal waits while the callback runs");
            release.Set();
            Task first = await Task.WhenAny(disposed, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
            Assert.That(first, Is.SameAs(disposed));
            Assert.That(start.IsCompleted, Is.True, "the drain has ended when disposal completes");
            AssertGood(await start.ConfigureAwait(false));
            Assert.That(started, Is.EqualTo(1));
            Task again = m_controller.DisposeAsync().AsTask();
            Assert.That(again.IsCompleted, Is.True, "a second disposal has nothing to wait for");
        }

        [Test]
        public async Task DisposeAsyncFromACallbackCompletesAfterTheCallbackReturnsAsync()
        {
            Analog(0, 10.0);
            Analog(0, 10.0, "Second");
            Task? disposed = null;
            bool completedInCallback = true;
            int started = 0;
            m_controller.ElementStarted += (_, _) =>
            {
                if (Interlocked.Increment(ref started) == 1)
                {
                    disposed = m_controller.DisposeAsync().AsTask();
                    completedInCallback = disposed.IsCompleted;
                }
            };

            AssertGood(m_controller.Start(m_recipe.NodeId));

            Assert.That(completedInCallback, Is.False, "the callback's own drain is still running");
            Assert.That(disposed, Is.Not.Null);
            Task first = await Task.WhenAny(disposed!, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
            Assert.That(first, Is.SameAs(disposed));
            Assert.That(started, Is.EqualTo(1));
            Assert.That(m_controller.State, Is.EqualTo(RecipeRunState.Idle));
        }

        [Test]
        public async Task DisposeAsyncAfterDisposeWaitsForTheRunningCallbackAsync()
        {
            Analog(0, 10.0);
            Analog(0, 10.0, "Second");
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            int started = 0;
            m_controller.ElementStarted += (_, _) =>
            {
                if (Interlocked.Increment(ref started) == 1)
                {
                    entered.Set();
                    release.Wait(TimeSpan.FromSeconds(10));
                }
            };
            Task<ServiceResult> start = Task.Run(() => m_controller.Start(m_recipe.NodeId));
            Assert.That(entered.Wait(TimeSpan.FromSeconds(10)), Is.True);

            m_controller.Dispose();
            Task first = m_controller.DisposeAsync().AsTask();
            Task second = m_controller.DisposeAsync().AsTask();
            await Task.Delay(100).ConfigureAwait(false);

            Assert.That(first.IsCompleted, Is.False, "a disposal after Dispose still waits for the callback");
            Assert.That(second.IsCompleted, Is.False, "every caller awaits the same completion");
            release.Set();
            Task all = Task.WhenAll(first, second);
            Task winner = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
            Assert.That(winner, Is.SameAs(all));
            Assert.That(start.IsCompleted, Is.True);
            Assert.That(started, Is.EqualTo(1));
        }

        [Test]
        public async Task DisposeAsyncAfterDisposeWaitsForTheTimerAsync()
        {
            Enable(BuiltInType.Double);
            var timerStopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_clock.TimerDisposalGate = timerStopped.Task;

            m_controller.Dispose();
            Task disposed = m_controller.DisposeAsync().AsTask();
            await Task.Delay(100).ConfigureAwait(false);

            Assert.That(disposed.IsCompleted, Is.False, "the timer stopped by Dispose has not finished yet");
            timerStopped.SetResult(true);
            Task winner = await Task.WhenAny(disposed, Task.Delay(TimeSpan.FromSeconds(10))).ConfigureAwait(false);
            Assert.That(winner, Is.SameAs(disposed));
        }

        [Test]
        public async Task TimerDisposalFailureIsLoggedAndPropagatedAsync()
        {
            using var logs = new RecordingLoggerProvider(LogLevel.Error);
            m_fixture.Server.Telemetry.LoggerFactory.AddProvider(logs);
            Enable(BuiltInType.Double);
            var failure = new InvalidOperationException("Timer disposal failed.");
            m_expectedShutdownFailure = failure;
            m_clock.TimerDisposalGate = Task.FromException(failure);

            m_controller.Dispose();

            RecordedLogRecord[] logged =
                [.. logs.Records.Where(r => r.EventId.Id == RecipeShutdownFailedEventId)];
            Assert.That(logged, Has.Length.EqualTo(1), "Dispose alone does not leave the failure unobserved");
            Assert.That(logged[0].LogLevel, Is.EqualTo(LogLevel.Error));
            Assert.That(logged[0].Exception, Is.SameAs(failure));
            InvalidOperationException? thrown = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await m_controller.DisposeAsync().ConfigureAwait(false)).ConfigureAwait(false);
            Assert.That(thrown, Is.SameAs(failure), "async callers still observe the failure");
            Assert.That(
                logs.Records.Count(r => r.EventId.Id == RecipeShutdownFailedEventId),
                Is.EqualTo(1),
                "the failure is logged once");
        }

        /// <summary>
        /// A failed timer cannot bypass an admitted subscriber drain, directly or through shared services.
        /// </summary>
        [TestCase(false)]
        [TestCase(true)]
        public async Task FailingTimerStillDrainsAdmittedSubscriberBeforeFaultingAsync(bool sharedServices)
        {
            Enable(BuiltInType.Double);
            Analog(0, 10.0);
            Analog(0, 10.0, "Second");
            var failure = new InvalidOperationException("Timer disposal failed with an admitted subscriber.");
            m_expectedShutdownFailure = failure;
            m_clock.TimerDisposalGate = Task.FromException(failure);
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var release = new ManualResetEventSlim(false);
            int notifications = 0;
            m_controller.ElementStarted += (_, _) =>
            {
                Interlocked.Increment(ref notifications);
                entered.TrySetResult(true);
                if (!release.Wait(TimeSpan.FromSeconds(15)))
                {
                    throw new TimeoutException("The admitted subscriber was not released.");
                }
            };
            Task<ServiceResult> start = Task.Run(() => m_controller.Start(m_recipe.NodeId));
            Task? first = null;
            Task? second = null;
            try
            {
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                first = sharedServices
                    ? m_scale.Services.DisposeAsync().AsTask()
                    : m_controller.DisposeAsync().AsTask();
                second = sharedServices
                    ? m_scale.Services.DisposeAsync().AsTask()
                    : m_controller.DisposeAsync().AsTask();
                Assert.That(first.IsCompleted, Is.False, "timer failure must not bypass the subscriber drain");
                Assert.That(second.IsCompleted, Is.False, "every waiter must retain the subscriber drain");
            }
            finally
            {
                release.Set();
                AssertGood(await start.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false));
            }
            InvalidOperationException? firstFailure = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await first!.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false));
            InvalidOperationException? secondFailure = await Assert.ThrowsAsync<InvalidOperationException>(
                async () => await second!.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false));
            InvalidOperationException? repeatedFailure = await Assert.ThrowsAsync<InvalidOperationException>(
                async () =>
                {
                    if (sharedServices)
                    {
                        await m_scale.Services.DisposeAsync().ConfigureAwait(false);
                    }
                    else
                    {
                        await m_controller.DisposeAsync().ConfigureAwait(false);
                    }
                });
            Assert.That(firstFailure, Is.SameAs(failure));
            Assert.That(secondFailure, Is.SameAs(failure));
            Assert.That(repeatedFailure, Is.SameAs(failure));
            Assert.That(notifications, Is.EqualTo(1), "notifications queued behind the subscriber are discarded");
        }

        /// <summary>
        /// Shared services retain pending timer cleanup and its result across overlapping shutdown calls.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task RuntimeServicesShareTimerDrainAndFailureAcrossShutdownCallersAsync(
            bool synchronousFirst, bool fails)
        {
            using var logs = new RecordingLoggerProvider(LogLevel.Error);
            m_fixture.Server.Telemetry.LoggerFactory.AddProvider(logs);
            Enable(BuiltInType.Double);
            var timerStopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_clock.TimerDisposalGate = timerStopped.Task;
            ScaleRuntimeServices services = m_scale.Services;
            if (synchronousFirst)
            {
                services.Dispose();
            }
            Task first = services.DisposeAsync().AsTask();
            Task second = services.DisposeAsync().AsTask();
            try
            {
                Assert.That(first.IsCompleted, Is.False, "the first caller must wait for the timer");
                Assert.That(second.IsCompleted, Is.False, "a later caller must share the timer drain");
                Assert.That(() => services.CreateFileBinder(), Throws.TypeOf<ObjectDisposedException>());
            }
            finally
            {
                if (fails)
                {
                    m_expectedShutdownFailure = new InvalidOperationException("Shared timer disposal failed.");
                    timerStopped.TrySetException(m_expectedShutdownFailure);
                }
                else
                {
                    timerStopped.TrySetResult(true);
                }
            }
            if (fails)
            {
                InvalidOperationException? firstFailure = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await first.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false));
                InvalidOperationException? secondFailure = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await second.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false));
                InvalidOperationException? repeatedFailure = await Assert.ThrowsAsync<InvalidOperationException>(
                    async () => await services.DisposeAsync().ConfigureAwait(false));
                Assert.That(firstFailure, Is.SameAs(m_expectedShutdownFailure));
                Assert.That(secondFailure, Is.SameAs(firstFailure));
                Assert.That(repeatedFailure, Is.SameAs(firstFailure));
                RecordedLogRecord[] logged =
                    [.. logs.Records.Where(r => r.EventId.Id == RuntimeResourceShutdownFailedEventId)];
                Assert.That(logged, Has.Length.EqualTo(1));
                Assert.That(logged[0].Exception, Is.SameAs(firstFailure));
            }
            else
            {
                await Task.WhenAll(first, second).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                await services.DisposeAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Every synchronous shutdown caller stops later controllers even while another caller is paused.
        /// </summary>
        [TestCase(false, false)]
        [TestCase(false, true)]
        [TestCase(true, false)]
        [TestCase(true, true)]
        public async Task ConcurrentServicesDisposeStopsEveryControllerBeforeReturningAsync(
            bool asyncFirst, bool pauseDuringStop)
        {
            Enable(BuiltInType.Double);
            Analog(0, 10.0);
            int notifications = 0;
            m_controller.ElementStarted += (_, _) => notifications++;
            using var stopGate = new ManualResetEventSlim(false);
            var resource = new GatedShutdownResource(pauseDuringStop, stopGate);
            ScaleRuntimeServices services = m_scale.Services;
            services.OwnResource(resource);
            Task first = Task.Run(async () =>
            {
                if (asyncFirst)
                {
                    await services.DisposeAsync().ConfigureAwait(false);
                }
                else
                {
                    services.Dispose();
                }
            });
            Task? drain = null;
            try
            {
                await resource.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                await Task.Run(services.Dispose).WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                Assert.That(m_controller.Start(m_recipe.NodeId).StatusCode,
                    Is.EqualTo((StatusCode)StatusCodes.BadInvalidState),
                    "the later controller must be stopped before the overlapping Dispose returns");
                Assert.That(notifications, Is.Zero, "no new notification can be admitted");
                drain = services.DisposeAsync().AsTask();
                Assert.That(drain.IsCompleted, Is.False, "all asynchronous callers retain the pending drain");
            }
            finally
            {
                resource.Release();
                await first.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                await services.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
                if (drain != null)
                {
                    await drain.ConfigureAwait(false);
                }
            }
        }

        [Test]
        public async Task DisposeAsyncAfterDisposeWaitsForARunningTimerCallbackAsync()
        {
            Enable(BuiltInType.Double);
            AnalogConditionSleepState first = Analog(0, 10.0);
            first.AddTimeout(m_fixture.Context);
            first.Timeout!.Value = 1000;
            AssertGood(m_controller.Start(m_recipe.NodeId));
            NodeState source = m_scale.Scale;
            NodeStateReportEventHandler? previous = source.OnReportEvent;
            using var entered = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            source.OnReportEvent = (context, node, e) =>
            {
                previous?.Invoke(context, node, e);
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(10));
            };
            try
            {
                Task fired = Task.Run(() => m_clock.Advance(TimeSpan.FromMilliseconds(1000)));
                Assert.That(entered.Wait(TimeSpan.FromSeconds(10)), Is.True);

                m_controller.Dispose();
                Task disposed = m_controller.DisposeAsync().AsTask();
                await Task.Delay(100).ConfigureAwait(false);

                Assert.That(disposed.IsCompleted, Is.False, "the timer-driven notification is still running");
                release.Set();
                Task winner = await Task.WhenAny(disposed, Task.Delay(TimeSpan.FromSeconds(10)))
                    .ConfigureAwait(false);
                Assert.That(winner, Is.SameAs(disposed));
                await disposed.ConfigureAwait(false);
                winner = await Task.WhenAny(fired, Task.Delay(TimeSpan.FromSeconds(10)))
                    .ConfigureAwait(false);
                Assert.That(winner, Is.SameAs(fired), "the timer callback has returned");
                await fired.ConfigureAwait(false);
            }
            finally
            {
                release.Set();
                source.OnReportEvent = previous;
            }
        }

        [Test]
        public void EdgeActivationSnapshotSurvivesClientEdits()
        {
            Enable(BuiltInType.Boolean);
            Publish(false);
            EdgeTriggeredSleepState edge = Edge(0);
            AssertGood(m_controller.Start(m_recipe.NodeId));
            edge.ConditionMode!.Value = EdgeOperator.Falling_1;
            edge.ThresholdValueId!.Value = "unknown";
            edge.TargetThresholdValue!.WrappedValue = new Variant(false);
            Publish(true);
            Assert.That(m_controller.State, Is.EqualTo(RecipeRunState.Idle));
            Assert.That(m_controller.CurrentElements.ToArray(), Is.Empty);
        }

        [Test]
        public void ConditionTimeoutContinuesAcrossPauseAndNeedsExplicitIntervention()
        {
            Enable(BuiltInType.Double);
            AnalogConditionSleepState element = Analog(0, 10.0);
            element.AddTimeout(m_fixture.Context);
            element.Timeout!.Value = 1000;
            AssertGood(m_controller.Start(m_recipe.NodeId));
            m_clock.Advance(TimeSpan.FromMilliseconds(500));
            AssertGood(m_controller.Stop(m_recipe.NodeId));
            m_clock.Advance(TimeSpan.FromMilliseconds(499));
            Assert.That(m_controller.ConditionWaitsRequiringIntervention.ToArray(), Is.Empty);
            m_clock.Advance(TimeSpan.FromMilliseconds(1));
            Assert.That(m_controller.ConditionWaitsRequiringIntervention.ToArray(), Does.Contain(element));
            AssertGood(m_controller.Continue(m_recipe.NodeId));
            Publish(10.0);
            Assert.That(m_controller.CurrentElements.ToArray(), Does.Contain(element));
            AssertGood(m_controller.CompleteElement(element));
            Assert.That(m_controller.State, Is.EqualTo(RecipeRunState.Idle));
        }

        [Test]
        public void PauseDoesNotCreateAnEdgeAcrossUnobservedExecution()
        {
            Enable(BuiltInType.Boolean);
            Publish(false);
            EdgeTriggeredSleepState edge = Edge(0);
            AssertGood(m_controller.Start(m_recipe.NodeId));
            AssertGood(m_controller.Stop(m_recipe.NodeId));
            Publish(true);
            AssertGood(m_controller.Continue(m_recipe.NodeId));
            Assert.That(m_controller.CurrentElements.ToArray(), Does.Contain(edge));
            Publish(false);
            Publish(true);
            Assert.That(m_controller.State, Is.EqualTo(RecipeRunState.Idle));
        }

        [Test]
        public void AbortSkipAndDisposeDiscardWaitsAndPreventLateCompletion()
        {
            Enable(BuiltInType.Double);
            AnalogConditionSleepState element = Analog(0, 10.0);
            int completed = 0;
            m_controller.RecipeCompleted += (_, _) => completed++;
            AssertGood(m_controller.Start(m_recipe.NodeId));
            AssertGood(m_controller.Abort(m_recipe.NodeId));
            Publish(10.0);
            Assert.That(completed, Is.Zero);
            Publish(0.0);
            AssertGood(m_controller.Start(m_recipe.NodeId));
            AssertGood(m_controller.Skip(m_recipe.NodeId));
            Publish(10.0);
            Assert.That(completed, Is.EqualTo(1));
            Publish(0.0);
            AssertGood(m_controller.Start(m_recipe.NodeId));
            m_controller.Dispose();
            Publish(10.0);
            m_clock.Advance(TimeSpan.FromDays(1));
            m_controller.EvaluateConditionWaits();
            Assert.That(completed, Is.EqualTo(1));
            Assert.That(m_controller.CurrentElements.ToArray(), Is.Empty);
            Assert.That(m_controller.ConditionWaitsEnabled, Is.False);
            Assert.That(ServiceResult.IsBad(m_controller.Start(m_recipe.NodeId)), Is.True);
            Assert.That(ServiceResult.IsBad(m_controller.CompleteElement(element)), Is.True);
        }

        private NodeId SourceNodeId => new("AllowedSource", m_fixture.Manager.InstanceNamespaceIndex);

        private NodeId ElementType(uint id)
        {
            return new NodeId(id, m_fixture.Manager.NamespaceIndices.Scales);
        }

        private void Enable(BuiltInType type)
        {
            m_controller.EnableConditionWaits(TimeSpan.FromSeconds(5), m_clock);
            m_controller.RegisterConditionSource(
                7, SourceNodeId, new LocalizedText("Allowed source"), type,
                type == BuiltInType.Boolean ? null : ScaleUnits.Kilogram);
        }

        private AnalogConditionSleepState Analog(int mode, Variant target, string name = "Wait")
        {
            var element = (AnalogConditionSleepState)m_controller.AddRecipeElement(
                m_recipe, ElementType(ObjectTypes.AnalogConditionSleepType), name, m_recipe.NodeId);
            element.ThresholdValueId!.Value = "7";
            element.TargetThresholdValue!.WrappedValue = target;
            element.TargetThresholdValue.EngineeringUnits!.Value = ScaleUnits.Kilogram;
            element.ConditionMode!.Value = (EqualityAndRelationalOperator)mode;
            return element;
        }

        private EdgeTriggeredSleepState Edge(int mode)
        {
            var element = (EdgeTriggeredSleepState)m_controller.AddRecipeElement(
                m_recipe, ElementType(ObjectTypes.EdgeTriggeredSleepType), "Edge", m_recipe.NodeId);
            element.ThresholdValueId!.Value = "7";
            element.TargetThresholdValue!.WrappedValue = new Variant(mode == 0);
            element.ConditionMode!.Value = (EdgeOperator)mode;
            return element;
        }

        private void Publish(Variant value)
        {
            m_clock.Advance(TimeSpan.FromMilliseconds(1));
            var sample = new DataValue(value, StatusCodes.Good, (DateTimeUtc)m_clock.GetUtcNow().UtcDateTime);
            m_controller.ObserveConditionSource(7, sample);
        }

        private static void AssertGood(ServiceResult result)
        {
            Assert.That(ServiceResult.IsGood(result), Is.True, result.ToString());
        }

        /// <summary>
        /// A manually advanced clock; evaluation remains synchronous and never sleeps.
        /// </summary>
        internal sealed class RecipeClock : TimeProvider
        {
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;

            /// <summary>
            /// When set, asynchronous timer disposal completes with this task, standing in for a
            /// timer callback that is still running.
            /// </summary>
            public Task? TimerDisposalGate { get; set; }

            public override DateTimeOffset GetUtcNow()
            {
                return m_now;
            }

            public override long GetTimestamp()
            {
                return m_now.Ticks;
            }

            public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                var timer = new RecipeTimer(this, () => callback(state));
                timer.Change(dueTime, period);
                m_timers.Add(timer);
                return timer;
            }

            public void Advance(TimeSpan amount)
            {
                m_now += amount;
                foreach (RecipeTimer timer in m_timers.ToArray())
                {
                    timer.Fire();
                }
            }

            private DateTimeOffset m_now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
            private readonly List<RecipeTimer> m_timers = [];
        }

        /// <summary>
        /// A scheduled callback driven only by the manually advanced clock.
        /// </summary>
        private sealed class RecipeTimer : ITimer
        {
            public RecipeTimer(RecipeClock clock, Action callback)
            {
                m_clock = clock;
                m_callback = callback;
            }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                m_due = dueTime == Timeout.InfiniteTimeSpan
                    ? DateTimeOffset.MaxValue
                    : m_clock.GetUtcNow() + dueTime;
                return true;
            }

            public void Fire()
            {
                if (!m_disposed && m_clock.GetUtcNow() >= m_due)
                {
                    m_due = DateTimeOffset.MaxValue;
                    m_callback();
                }
            }

            public void Dispose()
            {
                m_disposed = true;
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                Task? gate = m_clock.TimerDisposalGate;
                return gate == null ? default : new ValueTask(gate);
            }

            private readonly RecipeClock m_clock;
            private readonly Action m_callback;
            private DateTimeOffset m_due;
            private bool m_disposed;
        }

        /// <summary>
        /// Pauses only the first stop or the asynchronous drain, allowing another caller to stop later resources.
        /// </summary>
        private sealed class GatedShutdownResource(bool pauseDuringStop, ManualResetEventSlim stopGate)
            : IDisposable, IAsyncDisposable
        {
            /// <summary>
            /// Signals that the first shutdown has reached the selected pause.
            /// </summary>
            public TaskCompletionSource<bool> Entered { get; } =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <inheritdoc/>
            public void Dispose()
            {
                if (Interlocked.Increment(ref m_stops) == 1 && pauseDuringStop)
                {
                    Entered.TrySetResult(true);
                    if (!stopGate.Wait(TimeSpan.FromSeconds(15)))
                    {
                        throw new TimeoutException("The synchronous resource stop was not released.");
                    }
                }
            }

            /// <inheritdoc/>
            public ValueTask DisposeAsync()
            {
                Entered.TrySetResult(true);
                return new ValueTask(m_drained.Task);
            }

            /// <summary>
            /// Releases both shutdown phases without blocking asynchronous disposal.
            /// </summary>
            public void Release()
            {
                stopGate.Set();
                m_drained.TrySetResult(true);
            }

            /// <summary>The asynchronous drain gate.</summary>
            private readonly TaskCompletionSource<bool> m_drained =
                new(TaskCreationOptions.RunContinuationsAsynchronously);

            /// <summary>The number of idempotent stop calls.</summary>
            private int m_stops;
        }

        /// <summary>The ScalesServerEventIds.ScaleRecipes event id of a failed timer-driven evaluation.</summary>
        private const int RecipeConditionEvaluationFailedEventId = 8230;

        /// <summary>The ScalesServerEventIds.ScaleRecipes + 1 event id of a failed controller shutdown.</summary>
        private const int RecipeShutdownFailedEventId = 8231;

        /// <summary>The ScalesServerEventIds.ScaleRuntimeServices cleanup failure event id.</summary>
        private const int RuntimeResourceShutdownFailedEventId = 8240;

        /// <summary>A timer-disposal failure a test injects on purpose; teardown expects it.</summary>
        private Exception? m_expectedShutdownFailure;
        /// <summary>The failures from the two throwing process-fault subscribers.</summary>
        private static readonly string[] SubscriberFailureMessages = ["Subscriber failed.", "Subscriber failed."];

        private ScalesServerFixture m_fixture = null!;
        private ScaleHandle m_scale = null!;
        private RecipeController m_controller = null!;
        private RecipeState m_recipe = null!;
        private RecipeClock m_clock = null!;
    }
}
