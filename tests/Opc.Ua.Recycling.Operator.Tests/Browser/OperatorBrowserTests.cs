/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00. See LICENSE.txt in the repository root.
 * ======================================================================*/

using System;
using System.Globalization;
using System.IO;
using System.Text.Json.Nodes;
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
        /// Retained hold paging reaches late records and submits their original IDs, independently of the live preview.
        /// </summary>
        [Test]
        public async Task RetainedHoldPagesReachAndDisposeRecordsBeyondTheLivePreviewAsync()
        {
            await using OperatorBrowserHarness fixture = await OperatorBrowserHarness.CreateAsync(
                "Operator", configure: browser =>
                {
                    var retained = new JsonArray();
                    var preview = new JsonArray();
                    for (int id = 1; id <= 260; id++)
                    {
                        var hold = new JsonObject
                        {
                            ["id"] = id.ToString(CultureInfo.InvariantCulture),
                            ["tick"] = "40",
                            ["stage"] = 5,
                            ["reason"] = "Retained color inspection",
                            ["disposition"] = id <= 200 ? "Released" : "Held",
                            ["disposedBy"] = string.Empty,
                            ["dispositionRevision"] = "0"
                        };
                        retained.Add(hold);
                        if (id <= 200)
                        {
                            preview.Add(hold.DeepClone());
                        }
                    }
                    browser.HoldArchive = retained;
                    browser.State["holds"] = preview;
                    browser.State["summary"]!["heldCount"] = 60;
                }).ConfigureAwait(false);
            await fixture.Page.GetByTestId("nav-holds").ClickAsync().ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("holds-content")
                .GetByTestId("hold-row-201")).ToBeVisibleAsync().ConfigureAwait(false);
            await fixture.Page.GetByTestId("holds-next").ClickAsync().ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("holds-content")
                .GetByTestId("hold-row-251")).ToBeVisibleAsync().ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("holds-next")).ToBeDisabledAsync().ConfigureAwait(false);
            await fixture.Page.GetByTestId("command-DisposeHold-Released-251").ClickAsync().ConfigureAwait(false);
            await fixture.Page.GetByTestId("confirm-submit").ClickAsync().ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("command-outcomes")).ToContainTextAsync("Accepted")
                .ConfigureAwait(false);
            Assert.That(fixture.Commands, Has.Count.EqualTo(1));
            Assert.That(fixture.Commands[0]["holdId"]!.GetValue<string>(), Is.EqualTo("251"));
            Assert.That(fixture.Commands[0]["disposition"]!.GetValue<string>(), Is.EqualTo("Released"));
            await fixture.Page.GetByTestId("holds-previous").ClickAsync().ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("holds-content")
                .GetByTestId("hold-row-201")).ToBeVisibleAsync().ConfigureAwait(false);
            await fixture.Page.GetByTestId("holds-disposition").SelectOptionAsync("All").ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("holds-content")
                .GetByTestId("hold-row-1")).ToBeVisibleAsync().ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("command-DisposeHold-Released-1")).ToBeDisabledAsync()
                .ConfigureAwait(false);
            Assert.That(fixture.PageErrors, Is.Empty);
        }

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
        /// A first confirmation displays live revisions without submitting until the operator confirms.
        /// </summary>
        [Test]
        public async Task ConfirmationTracksLiveRevisionUntilSubmissionAsync()
        {
            await using OperatorBrowserHarness fixture = await OperatorBrowserHarness.CreateAsync("Operator")
                .ConfigureAwait(false);
            await fixture.Page.GetByTestId("process-Washing").ClickAsync().ConfigureAwait(false);
            await fixture.Page.GetByTestId("process-disable").ClickAsync().ConfigureAwait(false);
            string requestId = await fixture.Page.Locator("#command-dialog-payload")
                .EvaluateAsync<string>("element => JSON.parse(element.textContent).requestId").ConfigureAwait(false);
            fixture.State["tick"] = "43";
            fixture.State["revision"] = "9007199254740994";
            await fixture.PublishAsync().ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.Locator("#command-dialog-payload"))
                .ToContainTextAsync("9007199254740994").ConfigureAwait(false);
            Assert.That(fixture.Commands, Is.Empty);
            await fixture.Page.GetByTestId("confirm-submit").ClickAsync().ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("command-outcomes")).ToContainTextAsync("Accepted")
                .ConfigureAwait(false);
            Assert.That(fixture.Commands, Has.Count.EqualTo(1));
            Assert.That(fixture.Commands[0]["requestId"]!.GetValue<string>(), Is.EqualTo(requestId));
            Assert.That(fixture.Commands[0]["expectedRevision"]!.GetValue<string>(), Is.EqualTo("9007199254740994"));
            Assert.That(fixture.Commands[0]["process"]!.GetValue<string>(), Is.EqualTo("Washing"));
            Assert.That(fixture.PageErrors, Is.Empty);
        }

        /// <summary>
        /// An accepted receipt alone is not an optimistic disable; actual disabled substeps make Enable available.
        /// </summary>
        [Test]
        public async Task EnableBecomesAvailableAfterAuthoritativeDisableAsync()
        {
            await using OperatorBrowserHarness fixture = await OperatorBrowserHarness.CreateAsync("Operator")
                .ConfigureAwait(false);
            await fixture.Page.GetByTestId("process-Washing").ClickAsync().ConfigureAwait(false);
            ILocator enable = fixture.Page.GetByTestId("command-EnableProcess-Washing");
            await Assertions.Expect(enable).ToBeDisabledAsync().ConfigureAwait(false);
            await fixture.Page.GetByTestId("process-disable").ClickAsync().ConfigureAwait(false);
            await fixture.Page.GetByTestId("confirm-submit").ClickAsync().ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("command-outcomes")).ToContainTextAsync("Accepted")
                .ConfigureAwait(false);
            await Assertions.Expect(enable).ToBeDisabledAsync().ConfigureAwait(false);
            foreach (JsonNode? process in fixture.State["processes"]!.AsArray())
            {
                if (process!["name"]!.GetValue<string>() == "Washing")
                {
                    foreach (JsonNode? stage in process["stages"]!.AsArray())
                    {
                        stage!["enabled"] = false;
                    }
                }
            }
            fixture.State["tick"] = "43";
            fixture.State["revision"] = "9007199254740994";
            await fixture.PublishAsync().ConfigureAwait(false);
            await Assertions.Expect(enable).ToBeEnabledAsync().ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("process-disable")).ToBeDisabledAsync()
                .ConfigureAwait(false);
            Task<IResponse> enabled = fixture.Page.WaitForResponseAsync(response =>
                response.Url.EndsWith("/api/commands", StringComparison.Ordinal) &&
                response.Request.Method == "POST");
            await enable.ClickAsync().ConfigureAwait(false);
            await enabled.ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("command-outcomes")).ToContainTextAsync("EnableProcess")
                .ConfigureAwait(false);
            Assert.That(fixture.Commands, Has.Count.EqualTo(2));
            Assert.That(fixture.Commands[1]["kind"]!.GetValue<string>(), Is.EqualTo("EnableProcess"));
            Assert.That(fixture.Commands[1]["expectedRevision"]!.GetValue<string>(), Is.EqualTo("9007199254740994"));
            Assert.That(fixture.PageErrors, Is.Empty);
        }

        /// <summary>
        /// Live revision updates never rewrite an ambiguous command's already-submitted retry payload.
        /// </summary>
        [Test]
        public async Task RetryConfirmationPreservesExactPayloadAcrossLiveUpdatesAsync()
        {
            await using OperatorBrowserHarness fixture = await OperatorBrowserHarness.CreateAsync("Operator")
                .ConfigureAwait(false);
            fixture.CommandStatus = 500;
            await fixture.Page.GetByTestId("process-Washing").ClickAsync().ConfigureAwait(false);
            await fixture.Page.GetByTestId("process-disable").ClickAsync().ConfigureAwait(false);
            await fixture.Page.GetByTestId("confirm-submit").ClickAsync().ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("command-outcomes")).ToContainTextAsync("Unknown")
                .ConfigureAwait(false);
            string requestId = fixture.Commands[0]["requestId"]!.GetValue<string>();
            await fixture.Page.GetByTestId("command-retry-" + requestId).ClickAsync().ConfigureAwait(false);
            fixture.State["tick"] = "43";
            fixture.State["revision"] = "9007199254740994";
            await fixture.PublishAsync().ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("session-status")).ToContainTextAsync("Tick 43")
                .ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.Locator("#command-dialog-payload"))
                .ToContainTextAsync("9007199254740993").ConfigureAwait(false);
            fixture.CommandStatus = 200;
            await fixture.Page.GetByTestId("confirm-submit").ClickAsync().ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("command-outcomes")).ToContainTextAsync("Accepted")
                .ConfigureAwait(false);
            Assert.That(fixture.Commands, Has.Count.EqualTo(2));
            JsonNode expected = fixture.Commands[0].DeepClone();
            expected["retry"] = true;
            Assert.That(fixture.Commands[1].ToJsonString(), Is.EqualTo(expected.ToJsonString()));
            Assert.That(fixture.PageErrors, Is.Empty);
        }

        /// <summary>
        /// Escape on a reused dialog cancels only that action, even after an earlier confirmed command.
        /// </summary>
        [Test]
        public async Task EscapeAfterConfirmedActionDoesNotSubmitTheNextCommandAsync()
        {
            await using OperatorBrowserHarness fixture = await OperatorBrowserHarness.CreateAsync("Operator")
                .ConfigureAwait(false);
            await fixture.Page.GetByTestId("process-Washing").ClickAsync().ConfigureAwait(false);
            await fixture.Page.GetByTestId("process-disable").ClickAsync().ConfigureAwait(false);
            await fixture.Page.GetByTestId("confirm-submit").ClickAsync().ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("command-outcomes")).ToContainTextAsync("Accepted")
                .ConfigureAwait(false);

            await fixture.Page.GetByTestId("process-Sorting").ClickAsync().ConfigureAwait(false);
            await fixture.Page.GetByTestId("process-disable").ClickAsync().ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("confirm-dialog")).ToBeVisibleAsync()
                .ConfigureAwait(false);
            Assert.That(await fixture.Page.GetByTestId("confirm-dialog")
                .EvaluateAsync<string>("dialog => dialog.returnValue").ConfigureAwait(false), Is.Empty);
            await fixture.Page.GetByTestId("confirm-dialog").PressAsync("Escape").ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("confirm-dialog")).Not.ToBeVisibleAsync()
                .ConfigureAwait(false);

            await fixture.Page.GetByTestId("process-Bagging").ClickAsync().ConfigureAwait(false);
            await fixture.Page.GetByTestId("process-disable").ClickAsync().ConfigureAwait(false);
            Task<IResponse> final = fixture.Page.WaitForResponseAsync(response =>
                response.Url.EndsWith("/api/commands", StringComparison.Ordinal) &&
                response.Request.PostData?.Contains("Bagging", StringComparison.Ordinal) == true);
            await fixture.Page.GetByTestId("confirm-submit").ClickAsync().ConfigureAwait(false);
            await final.ConfigureAwait(false);

            Assert.That(fixture.Commands, Has.Count.EqualTo(2));
            Assert.That(fixture.Commands[0]["process"]!.GetValue<string>(), Is.EqualTo("Washing"));
            Assert.That(fixture.Commands[1]["process"]!.GetValue<string>(), Is.EqualTo("Bagging"));
            Assert.That(fixture.PageErrors, Is.Empty);
        }

        /// <summary>
        /// A terminal consumer-limit response retains readable data but cannot masquerade as live control.
        /// </summary>
        [Test]
        public async Task ConsumerLimitKeepsControlsUnavailableUntilLiveDeliveryRecoversAsync()
        {
            await using OperatorBrowserHarness fixture = await OperatorBrowserHarness.CreateAsync(
                "Operator", configure: browser => browser.EventStatus = 429).ConfigureAwait(false);
            await fixture.Page.GetByTestId("process-Washing").ClickAsync().ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("process-disable")).ToBeDisabledAsync()
                .ConfigureAwait(false);
            using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(20));
            while (fixture.EventRequests < 2)
            {
                await Task.Delay(20, timeout.Token).ConfigureAwait(false);
            }
            await Assertions.Expect(fixture.Page.GetByTestId("process-disable")).ToBeDisabledAsync()
                .ConfigureAwait(false);
            fixture.State["tick"] = "43";
            fixture.State["revision"] = "9007199254740994";
            fixture.EventStatus = 200;

            await Assertions.Expect(fixture.Page.GetByTestId("session-status")).ToContainTextAsync("Tick 43",
                new LocatorAssertionsToContainTextOptions { Timeout = 15000 }).ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("process-disable")).ToBeEnabledAsync()
                .ConfigureAwait(false);
            Assert.That(fixture.Commands, Is.Empty);
            Assert.That(fixture.PageErrors, Is.Empty);
        }

        /// <summary>
        /// Losing live delivery while a dialog is open cannot submit its retained destructive envelope.
        /// </summary>
        [Test]
        public async Task StreamFailureCancelsPendingDestructiveConfirmationAsync()
        {
            await using OperatorBrowserHarness fixture = await OperatorBrowserHarness.CreateAsync("Operator")
                .ConfigureAwait(false);
            await fixture.Page.GetByTestId("process-Washing").ClickAsync().ConfigureAwait(false);
            await fixture.Page.GetByTestId("process-disable").ClickAsync().ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("confirm-dialog")).ToBeVisibleAsync()
                .ConfigureAwait(false);
            fixture.EventStatus = 429;
            fixture.DisconnectStream();

            await Assertions.Expect(fixture.Page.GetByTestId("confirm-dialog")).Not.ToBeVisibleAsync()
                .ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("process-disable")).ToBeDisabledAsync()
                .ConfigureAwait(false);
            Assert.That(fixture.Commands, Is.Empty);
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
            fixture.State["tick"] = "43";
            fixture.State["revision"] = "9007199254740994";
            await fixture.PublishAsync().ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("session-status")).ToContainTextAsync("Tick 43")
                .ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("command-outcomes")).ToContainTextAsync("Rejected")
                .ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("system-banner")).ToContainTextAsync("not accepted")
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
        /// A live update cannot replace an equipment icon or destroy the target between pointer down and up.
        /// </summary>
        [Test]
        public async Task LiveUpdatesPreserveEquipmentIconsAndPointerClicksAsync()
        {
            await using OperatorBrowserHarness fixture = await OperatorBrowserHarness.CreateAsync("Operator")
                .ConfigureAwait(false);
            ILocator sorting = fixture.Page.GetByTestId("process-Sorting");
            await Assertions.Expect(sorting).ToBeVisibleAsync().ConfigureAwait(false);
            await fixture.Page.EvaluateAsync("""
                () => {
                    window.savedProcess = document.querySelector("[data-testid='process-Sorting']");
                    window.savedIcon = window.savedProcess.querySelector("use");
                }
                """).ConfigureAwait(false);
            var box = await sorting.BoundingBoxAsync().ConfigureAwait(false)
                ?? throw new InvalidOperationException("The real SVG process is not visible.");
            await fixture.Page.Mouse.MoveAsync(box.X + 35, box.Y + 30).ConfigureAwait(false);
            await fixture.Page.Mouse.DownAsync().ConfigureAwait(false);
            fixture.State["tick"] = "43";
            fixture.State["revision"] = "9007199254740994";
            await fixture.PublishAsync().ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("session-status")).ToContainTextAsync("Tick 43")
                .ConfigureAwait(false);
            await fixture.Page.Mouse.UpAsync().ConfigureAwait(false);
            Assert.That(await fixture.Page.EvaluateAsync<bool>("""
                () => window.savedProcess === document.querySelector("[data-testid='process-Sorting']")
                    && window.savedIcon === window.savedProcess.querySelector("use")
                    && window.savedIcon.isConnected
                """).ConfigureAwait(false), Is.True, "Live updates must not tear down external SVG icon nodes.");
            await Assertions.Expect(fixture.Page.GetByTestId("inspector-name")).ToContainTextAsync("Sorting")
                .ConfigureAwait(false);
            Assert.That(fixture.InspectorRequests, Has.Count.EqualTo(1));
            Assert.That(fixture.PageErrors, Is.Empty);
        }

        /// <summary>
        /// Live updates preserve action targets and their current payload without accumulating click handlers.
        /// </summary>
        [Test]
        public async Task LiveUpdatesPreserveCommandButtonsAndSubmitOnceAsync()
        {
            await using OperatorBrowserHarness fixture = await OperatorBrowserHarness.CreateAsync("Operator")
                .ConfigureAwait(false);
            await fixture.Page.GetByTestId("process-Washing").ClickAsync().ConfigureAwait(false);
            ILocator disable = fixture.Page.GetByTestId("process-disable");
            await Assertions.Expect(disable).ToBeEnabledAsync().ConfigureAwait(false);
            await disable.ScrollIntoViewIfNeededAsync().ConfigureAwait(false);
            await fixture.Page.EvaluateAsync("""
                () => { window.savedCommandButton = document.querySelector("[data-testid='process-disable']"); }
                """).ConfigureAwait(false);
            var box = await disable.BoundingBoxAsync().ConfigureAwait(false)
                ?? throw new InvalidOperationException("The action button is not visible.");
            await fixture.Page.Mouse.MoveAsync(box.X + 15, box.Y + 10).ConfigureAwait(false);
            await fixture.Page.Mouse.DownAsync().ConfigureAwait(false);
            fixture.State["tick"] = "43";
            fixture.State["revision"] = "9007199254740994";
            await fixture.PublishAsync().ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("session-status")).ToContainTextAsync("Tick 43")
                .ConfigureAwait(false);
            await fixture.Page.Mouse.UpAsync().ConfigureAwait(false);
            Assert.That(await fixture.Page.EvaluateAsync<bool>("""
                () => window.savedCommandButton === document.querySelector("[data-testid='process-disable']")
                """).ConfigureAwait(false), Is.True);
            await Assertions.Expect(fixture.Page.GetByTestId("confirm-dialog")).ToBeVisibleAsync()
                .ConfigureAwait(false);
            await fixture.Page.GetByTestId("confirm-submit").ClickAsync().ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("command-outcomes")).ToContainTextAsync("Accepted")
                .ConfigureAwait(false);
            Assert.That(fixture.Commands, Has.Count.EqualTo(1));
            Assert.That(fixture.Commands[0]["expectedRevision"]!.GetValue<string>(), Is.EqualTo("9007199254740994"));
            Assert.That(fixture.PageErrors, Is.Empty);
        }

        /// <summary>
        /// Same-run observations replace coordinator state without discarding cached native readings.
        /// </summary>
        [Test]
        public async Task SameRunUpdatesRefreshInspectorCountersAndCommandGatingAsync()
        {
            await using OperatorBrowserHarness fixture = await OperatorBrowserHarness.CreateAsync("Operator")
                .ConfigureAwait(false);
            await fixture.Page.GetByTestId("process-Washing").ClickAsync().ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("readings-section")).ToContainTextAsync("DriveSpeed")
                .ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("process-disable")).ToBeEnabledAsync()
                .ConfigureAwait(false);

            foreach (JsonNode? process in fixture.State["processes"]!.AsArray())
            {
                if (process!["name"]!.GetValue<string>() != "Washing")
                {
                    continue;
                }
                foreach (JsonNode? stage in process["stages"]!.AsArray())
                {
                    stage!["enabled"] = false;
                    stage["state"] = "Disabled";
                    stage["inventoryMg"] = "1000000";
                }
            }
            fixture.State["tick"] = "43";
            fixture.State["revision"] = "9007199254740994";
            await fixture.PublishAsync().ConfigureAwait(false);

            await Assertions.Expect(fixture.Page.GetByTestId("session-status")).ToContainTextAsync("Tick 43")
                .ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("command-EnableProcess-Washing")).ToBeEnabledAsync()
                .ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("process-disable")).ToBeDisabledAsync()
                .ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("inspector-process").Locator(".metric").First)
                .ToContainTextAsync("4 kg").ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("stage-row-Washing-8")).ToContainTextAsync("Disabled")
                .ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("readings-section")).ToContainTextAsync("DriveSpeed")
                .ConfigureAwait(false);
            Assert.That(fixture.InspectorRequests, Has.Count.EqualTo(1));
            Assert.That(fixture.PageErrors, Is.Empty);
        }

        /// <summary>
        /// Both coordinator resumable lifecycles authorize the unchanged Resume command envelope.
        /// </summary>
        [TestCase("Paused")]
        [TestCase("Stopped")]
        public async Task AdministratorCanResumeResumableLifecycleAsync(string lifecycle)
        {
            await using OperatorBrowserHarness fixture =
                await OperatorBrowserHarness.CreateAsync("SimulatorAdministrator").ConfigureAwait(false);
            fixture.State["lifecycle"] = lifecycle;
            fixture.State["tick"] = "43";
            fixture.State["revision"] = "9007199254740994";
            await fixture.PublishAsync().ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("session-status")).ToContainTextAsync("Tick 43")
                .ConfigureAwait(false);
            await fixture.Page.GetByTestId("admin-open").ClickAsync().ConfigureAwait(false);
            ILocator resume = fixture.Page.GetByTestId("command-Resume-plant");
            await Assertions.Expect(resume).ToBeEnabledAsync().ConfigureAwait(false);
            Task<IResponse> response = fixture.Page.WaitForResponseAsync(value =>
                value.Url.EndsWith("/api/commands", StringComparison.Ordinal) &&
                value.Request.Method == "POST");
            await resume.ClickAsync().ConfigureAwait(false);
            await response.ConfigureAwait(false);
            Assert.That(fixture.Commands, Has.Count.EqualTo(1));
            Assert.That(fixture.Commands[0]["kind"]!.GetValue<string>(), Is.EqualTo("Resume"));
            Assert.That(fixture.Commands[0]["runId"]!.GetValue<string>(), Is.EqualTo("browser-test-run"));
            Assert.That(fixture.Commands[0]["expectedRevision"]!.GetValue<string>(), Is.EqualTo("9007199254740994"));
            Assert.That(fixture.PageErrors, Is.Empty);
        }

        /// <summary>
        /// Long pages remain keyboard-scrollable without moving the persistent desktop exception dock.
        /// </summary>
        [TestCase("material")]
        [TestCase("history")]
        [TestCase("holds")]
        public async Task DesktopSecondaryPanelsExposeTheirLastRowByKeyboardAsync(string content)
        {
            await using OperatorBrowserHarness fixture =
                await OperatorBrowserHarness.CreateAsync(width: 1440, height: 900).ConfigureAwait(false);
            var items = new JsonArray();
            for (int index = 1; index <= 50; index++)
            {
                string id = index.ToString(CultureInfo.InvariantCulture);
                items.Add(new JsonObject
                {
                    ["id"] = id,
                    ["tick"] = "42",
                    ["stage"] = 5,
                    ["kind"] = "Material",
                    ["form"] = "Flake",
                    ["mass"] = new JsonObject { ["total"] = "1000000" },
                    ["reason"] = "Quality inspection required",
                    ["disposition"] = "Held"
                });
            }
            var page = new JsonObject
            {
                ["runId"] = "browser-test-run",
                ["nextAfter"] = null,
                ["items"] = items
            };
            await fixture.Page.RouteAsync("**/api/" + content + "**", route =>
                route.FulfillAsync(new RouteFulfillOptions
                {
                    Status = 200,
                    ContentType = "application/json",
                    Body = page.ToJsonString()
                })).ConfigureAwait(false);
            fixture.State["holds"] = items.DeepClone();
            fixture.State["tick"] = "43";
            await fixture.PublishAsync().ConfigureAwait(false);
            await Assertions.Expect(fixture.Page.GetByTestId("session-status")).ToContainTextAsync("Tick 43")
                .ConfigureAwait(false);

            string panelName = content == "holds" ? "holds" : "material";
            await fixture.Page.GetByTestId("nav-" + panelName).ClickAsync().ConfigureAwait(false);
            if (content != "holds")
            {
                await fixture.Page.GetByTestId(content + "-load-more").ClickAsync().ConfigureAwait(false);
            }
            ILocator panel = fixture.Page.GetByTestId(panelName + "-panel");
            string rowPrefix = content == "holds" ? "hold" : content;
            ILocator lastRow = panel.GetByTestId(rowPrefix + "-row-50");
            await Assertions.Expect(lastRow).ToBeAttachedAsync().ConfigureAwait(false);
            await Assertions.Expect(lastRow).Not.ToBeInViewportAsync().ConfigureAwait(false);
            await panel.FocusAsync().ConfigureAwait(false);
            await panel.PressAsync("Control+End").ConfigureAwait(false);
            await Assertions.Expect(lastRow).ToBeInViewportAsync().ConfigureAwait(false);
            Assert.That(await panel.EvaluateAsync<bool>("element => element.scrollTop > 0")
                .ConfigureAwait(false), Is.True);
            await Assertions.Expect(fixture.Page.GetByTestId("exceptions-panel")).ToBeInViewportAsync()
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
