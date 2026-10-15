/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00. See LICENSE.txt in the repository root.
 * ======================================================================*/

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Opc.Ua.Recycling.Operator.Tests
{
    /// <summary>
    /// Real-browser interaction profile; install Chromium with the generated playwright.ps1 first.
    /// </summary>
    [TestFixture]
    [Category("Browser")]
    [NonParallelizable]
    [Explicit("Requires the explicitly installed Playwright Chromium browser.")]
    public sealed class OperatorBrowserTests
    {
        /// <summary>
        /// The plant remains readable on desktop and narrow viewports, including reduced-motion operation.
        /// </summary>
        [TestCase(1440, false)]
        [TestCase(1280, false)]
        [TestCase(900, true)]
        public async Task PlantShowsAllProcessesAndSupportsKeyboardInspectionAsync(int width, bool reducedMotion)
        {
            await using OperatorBrowserHarness fixture =
                await OperatorBrowserHarness.CreateAsync(width: width, reducedMotion: reducedMotion)
                    .ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("process-Collection")).ToBeVisibleAsync()
                .ConfigureAwait(false);
            foreach (string process in Enum.GetNames<RecyclingProcess>())
            {
                await Assertions.Expect(fixture.Page.GetByTestId("process-" + process)).ToBeVisibleAsync()
                    .ConfigureAwait(false);
            }
            ILocator sorting = fixture.Page.GetByTestId("process-Sorting");
            await sorting.FocusAsync().ConfigureAwait(false);
            await sorting.PressAsync("Enter").ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("inspector")).ToBeVisibleAsync().ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("inspector-name")).ToContainTextAsync("Sorting")
                .ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("inspector")).ToContainTextAsync("DriveSpeed")
                .ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("inspector")).ToContainTextAsync("BadNoData")
                .ConfigureAwait(false);
            Assert.That(fixture.PageErrors, Is.Empty);

            string screenshotDirectory = Path.Combine(TestContext.CurrentContext.TestDirectory, "screenshots");
            Directory.CreateDirectory(screenshotDirectory);
            string screenshot = Path.Combine(screenshotDirectory, $"operator-{width}.png");
            await fixture.Page.ScreenshotAsync(new PageScreenshotOptions { Path = screenshot, FullPage = true })
                .ConfigureAwait(false);
            TestContext.AddTestAttachment(screenshot, "Actual operator UI; deterministic browser-only test fixture.");
        }

        /// <summary>
        /// Observer controls cannot submit a production mutation merely by selecting equipment.
        /// </summary>
        [Test]
        public async Task ObserverInspectionDoesNotGrantControlAsync()
        {
            await using OperatorBrowserHarness fixture = await OperatorBrowserHarness.CreateAsync()
                .ConfigureAwait(false);
            await fixture.Page.GetByTestId("process-Washing").ClickAsync().ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("inspector-name")).ToContainTextAsync("Washing")
                .ConfigureAwait(false);
            ILocator disable = fixture.Page.GetByTestId("process-disable");
            if (await disable.CountAsync().ConfigureAwait(false) > 0 &&
                await disable.IsVisibleAsync().ConfigureAwait(false))
            {
                await Assertions.Expect(disable).ToBeDisabledAsync().ConfigureAwait(false);
            }
            Assert.That(fixture.Commands, Is.Empty);
            Assert.That(fixture.PageErrors, Is.Empty);
        }

        /// <summary>
        /// Exception navigation performs one inspection, not two competing native reads for one click.
        /// </summary>
        [Test]
        public async Task SelectingAFaultRequestsItsInspectorExactlyOnceAsync()
        {
            await using OperatorBrowserHarness fixture = await OperatorBrowserHarness.CreateAsync()
                .ConfigureAwait(false);
            await fixture.Page.GetByTestId("fault-row-Shredding").GetByRole(AriaRole.Button).ClickAsync()
                .ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("readings-section")).ToContainTextAsync("DriveSpeed")
                .ConfigureAwait(false);
            Assert.That(fixture.InspectorRequests, Has.Count.EqualTo(1));
            Assert.That(fixture.InspectorRequests[0], Is.EqualTo("/api/processes/Shredding"));
            Assert.That(fixture.PageErrors, Is.Empty);
        }

        /// <summary>
        /// Last-known data remains visible with uncertainty, while control cannot operate on an unavailable image.
        /// </summary>
        [Test]
        public async Task UnavailableImageRetainsItsQualityAndDisablesControlAsync()
        {
            await using OperatorBrowserHarness fixture =
                await OperatorBrowserHarness.CreateAsync("Operator", unavailable: true).ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("data-quality"))
                .ToContainTextAsync("UncertainLastUsableValue").ConfigureAwait(false);
            await fixture.Page.GetByTestId("process-Washing").ClickAsync().ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("process-disable")).ToBeDisabledAsync()
                .ConfigureAwait(false);
            Assert.That(fixture.Commands, Is.Empty);
            Assert.That(fixture.PageErrors, Is.Empty);
        }

        /// <summary>
        /// Destructive process control requires a confirmation and preserves the exact observed revision.
        /// </summary>
        [Test]
        public async Task ProcessDisableRequiresConfirmationAndPreservesLargeRevisionAsync()
        {
            await using OperatorBrowserHarness fixture = await OperatorBrowserHarness.CreateAsync("Operator")
                .ConfigureAwait(false);
            await fixture.Page.GetByTestId("process-Washing").ClickAsync().ConfigureAwait(false);
            await fixture.Page.GetByTestId("process-disable").ClickAsync().ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("confirm-dialog")).ToBeVisibleAsync()
                .ConfigureAwait(false);
            Assert.That(fixture.Commands, Is.Empty);
            Task<IResponse> commandResponse = fixture.Page.WaitForResponseAsync(response =>
                response.Url.EndsWith("/api/commands", StringComparison.Ordinal) &&
                response.Request.Method == "POST");
            await fixture.Page.GetByTestId("confirm-submit").ClickAsync().ConfigureAwait(false);
            await commandResponse.ConfigureAwait(false);
            Assert.That(fixture.Commands, Has.Count.EqualTo(1));
            Assert.That(fixture.Commands[0]["kind"]!.GetValue<string>(), Is.EqualTo("DisableProcess"));
            Assert.That(fixture.Commands[0]["process"]!.GetValue<string>(), Is.EqualTo("Washing"));
            Assert.That(fixture.Commands[0]["expectedRevision"]!.GetValue<string>(), Is.EqualTo("9007199254740993"));
            Assert.That(fixture.Commands[0]["confirmed"]!.GetValue<bool>(), Is.True);
            Assert.That(fixture.Commands[0]["runId"]!.GetValue<string>(), Is.EqualTo("browser-test-run"));
            await Assertions.Expect(fixture.Page.GetByTestId("process-disable")).ToBeVisibleAsync()
                .ConfigureAwait(false);
            Assert.That(fixture.PageErrors, Is.Empty);
        }

        /// <summary>
        /// Administrative commands are explicit sample operations, not a start button or an emergency stop.
        /// </summary>
        [Test]
        public async Task AdministratorPauseUsesTheObservedRunAsync()
        {
            await using OperatorBrowserHarness fixture =
                await OperatorBrowserHarness.CreateAsync("SimulatorAdministrator").ConfigureAwait(false);
            await fixture.Page.GetByTestId("admin-open").ClickAsync().ConfigureAwait(false);
            Task<IResponse> response = fixture.Page.WaitForResponseAsync(value =>
                value.Url.EndsWith("/api/commands", StringComparison.Ordinal) &&
                value.Request.Method == "POST");
            await fixture.Page.GetByTestId("admin-pause").ClickAsync().ConfigureAwait(false);
            await response.ConfigureAwait(false);
            Assert.That(fixture.Commands, Has.Count.EqualTo(1));
            Assert.That(fixture.Commands[0]["kind"]!.GetValue<string>(), Is.EqualTo("Pause"));
            Assert.That(fixture.Commands[0]["runId"]!.GetValue<string>(), Is.EqualTo("browser-test-run"));
            Assert.That(fixture.Commands[0]["expectedRevision"]!.GetValue<string>(), Is.EqualTo("9007199254740993"));
            Assert.That(fixture.PageErrors, Is.Empty);
        }

        /// <summary>
        /// A revision conflict is visible and must not cause a rewritten, automatically resubmitted command.
        /// </summary>
        [Test]
        public async Task CommandConflictIsVisibleWithoutAutomaticResubmissionAsync()
        {
            await using OperatorBrowserHarness fixture =
                await OperatorBrowserHarness.CreateAsync("SimulatorAdministrator").ConfigureAwait(false);
            fixture.CommandStatus = 409;
            await fixture.Page.GetByTestId("admin-open").ClickAsync().ConfigureAwait(false);
            await fixture.Page.GetByTestId("admin-pause").ClickAsync().ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("command-outcomes")).ToContainTextAsync("Rejected")
                .ConfigureAwait(false);
            Assert.That(fixture.Commands, Has.Count.EqualTo(1));
            Assert.That(fixture.Commands[0]["expectedRevision"]!.GetValue<string>(), Is.EqualTo("9007199254740993"));
            Assert.That(fixture.Commands[0]["retry"]!.GetValue<bool>(), Is.False);
            Assert.That(fixture.PageErrors, Is.Empty);
        }

        /// <summary>
        /// A live observation must not erase keyboard focus or close the operator's administration drawer.
        /// </summary>
        [Test]
        public async Task LiveUpdatesPreserveFocusAndOpenInspectorStateAsync()
        {
            await using OperatorBrowserHarness fixture =
                await OperatorBrowserHarness.CreateAsync("SimulatorAdministrator").ConfigureAwait(false);
            await fixture.Page.GetByTestId("process-Sorting").ClickAsync().ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("readings-section")).ToContainTextAsync("DriveSpeed")
                .ConfigureAwait(false);
            await fixture.Page.GetByTestId("admin-open").ClickAsync().ConfigureAwait(false);
            await fixture.Page.GetByTestId("process-Sorting").FocusAsync().ConfigureAwait(false);

            fixture.State["tick"] = "43";
            fixture.State["revision"] = "9007199254740994";
            await fixture.PublishAsync().ConfigureAwait(false);

            await Assertions.Expect(fixture.Page.GetByTestId("session-status")).ToContainTextAsync("Tick 43")
                .ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("process-Sorting")).ToBeFocusedAsync()
                .ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("admin-drawer")).ToHaveAttributeAsync("open", string.Empty)
                .ConfigureAwait(false);
            Assert.That(fixture.PageErrors, Is.Empty);
        }

        /// <summary>
        /// A run reset cannot leave native readings from the previous run in the active inspector.
        /// </summary>
        [Test]
        public async Task NewRunDoesNotReuseThePreviousNativeInspectorAsync()
        {
            await using OperatorBrowserHarness fixture = await OperatorBrowserHarness.CreateAsync()
                .ConfigureAwait(false);
            await fixture.Page.GetByTestId("process-Sorting").ClickAsync().ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("readings-section")).ToContainTextAsync("browser-test-run")
                .ConfigureAwait(false);

            fixture.State["runId"] = "replacement-run";
            fixture.State["tick"] = "0";
            fixture.State["revision"] = "9007199254741000";
            fixture.State["lifecycle"] = "WaitingForParticipants";
            await fixture.PublishAsync().ConfigureAwait(false);

            await Assertions.Expect(fixture.Page.GetByTestId("session-status")).ToContainTextAsync("replacement-run")
                .ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("readings-section"))
                .Not.ToContainTextAsync("browser-test-run").ConfigureAwait(false);
            Assert.That(fixture.PageErrors, Is.Empty);
        }

        /// <summary>
        /// A short-lived browser session must return to authentication instead of leaving an apparently usable console.
        /// </summary>
        [Test]
        public async Task ExpiredSessionReturnsToTheLocalAuthenticationFormAsync()
        {
            await using OperatorBrowserHarness fixture = await OperatorBrowserHarness.CreateAsync()
                .ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("process-Collection")).ToBeVisibleAsync()
                .ConfigureAwait(false);
            fixture.StateStatus = 401;
            fixture.DisconnectStream();
            await Assertions.Expect(fixture.Page.GetByTestId("connect-page")).ToBeVisibleAsync()
                .ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("connect-bootstrap-token"))
                .ToHaveAttributeAsync("type", "password").ConfigureAwait(false);
            Assert.That(fixture.Commands, Is.Empty);
            Assert.That(fixture.PageErrors, Is.Empty);
        }
    }
}
