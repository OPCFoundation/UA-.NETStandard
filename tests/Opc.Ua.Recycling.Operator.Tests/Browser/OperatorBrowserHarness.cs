/* ========================================================================
 * Copyright (c) 2005-2026 The OPC Foundation, Inc. All rights reserved.
 * OPC Foundation MIT License 1.00. See LICENSE.txt in the repository root.
 * ======================================================================*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using NUnit.Framework;

namespace Opc.Ua.Recycling.Operator.Tests
{
    /// <summary>
    /// Serves the actual static UI in Chromium with explicitly test-only deterministic HTTP responses.
    /// Backend security and UA integration are exercised separately through the real host.
    /// </summary>
    internal sealed class OperatorBrowserHarness : IAsyncDisposable
    {
        private OperatorBrowserHarness(
            WebApplication application, IPlaywright playwright, IBrowser browser, Channel<string> updates)
        {
            m_application = application;
            m_playwright = playwright;
            m_browser = browser;
            m_updates = updates;
        }

        /// <summary>
        /// Gets the current controlled plant observation returned to the browser.
        /// </summary>
        internal JsonObject State { get; } = BrowserPlantFixture.CreateState();

        /// <summary>
        /// Gets the captured command bodies, retaining exact 64-bit string identifiers.
        /// </summary>
        internal List<JsonObject> Commands { get; } = [];

        /// <summary>
        /// Gets the actual native-inspector request paths initiated by browser interactions.
        /// </summary>
        internal List<string> InspectorRequests { get; } = [];

        /// <summary>
        /// Gets or sets the full retained history, independently of the bounded live preview.
        /// </summary>
        internal JsonArray? HoldArchive { get; set; }

        /// <summary>
        /// Gets uncaught browser errors; expected API refusals are not swallowed.
        /// </summary>
        internal List<string> PageErrors { get; } = [];

        /// <summary>
        /// Gets or sets the controlled command HTTP result for conflict and unavailable-outcome tests.
        /// </summary>
        internal int CommandStatus { get; set; } = 200;

        /// <summary>
        /// Gets or sets the controlled state HTTP result when an expired stream tries to resynchronize.
        /// </summary>
        internal int StateStatus { get; set; } = 200;

        /// <summary>
        /// Gets the browser page under test.
        /// </summary>
        internal IPage Page { get; private set; } = null!;

        /// <summary>
        /// Starts an ephemeral local static host and opens the real console UI.
        /// </summary>
        internal static async Task<OperatorBrowserHarness> CreateAsync(
            string role = "Observer", int width = 1440, int height = 1000, bool reducedMotion = false,
            bool unavailable = false, Action<OperatorBrowserHarness>? configure = null)
        {
            string webRoot = Path.Combine(TestContext.CurrentContext.TestDirectory, "wwwroot");
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { WebRootPath = webRoot });
            builder.Logging.ClearProviders();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            WebApplication application = builder.Build();
            Channel<string> updates = Channel.CreateBounded<string>(new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropOldest
            });
            OperatorBrowserHarness? activeFixture = null;
            application.MapGet("/api/events", async (HttpContext context) =>
            {
                context.Response.ContentType = "text/event-stream";
                await context.Response.WriteAsync(
                    "event: state\ndata: " + activeFixture!.State.ToJsonString() + "\n\n",
                    context.RequestAborted).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(context.RequestAborted).ConfigureAwait(false);
                await foreach (string update in updates.Reader.ReadAllAsync(context.RequestAborted)
                    .ConfigureAwait(false))
                {
                    await context.Response.WriteAsync(
                        "event: state\ndata: " + update + "\n\n", context.RequestAborted).ConfigureAwait(false);
                    await context.Response.Body.FlushAsync(context.RequestAborted).ConfigureAwait(false);
                }
            });
            application.UseDefaultFiles();
            application.UseStaticFiles();
            await application.StartAsync().ConfigureAwait(false);
            IPlaywright? playwright = null;
            IBrowser? browser = null;
            try
            {
                playwright = await Playwright.CreateAsync().ConfigureAwait(false);
                browser = await playwright.Chromium.LaunchAsync(
                    new BrowserTypeLaunchOptions { Headless = true }).ConfigureAwait(false);
                var fixture = new OperatorBrowserHarness(application, playwright, browser, updates);
                activeFixture = fixture;
                configure?.Invoke(fixture);
                if (unavailable)
                {
                    fixture.State["connection"] = "Reconnecting";
                    fixture.State["available"] = false;
                    fixture.State["quality"] = "UncertainLastUsableValue";
                    fixture.State["detail"] = "Coordinator connection lost; showing the last received image.";
                }
                IBrowserContext context = await browser.NewContextAsync(new BrowserNewContextOptions
                {
                    ViewportSize = new ViewportSize { Width = width, Height = height },
                    ReducedMotion = reducedMotion ? ReducedMotion.Reduce : ReducedMotion.NoPreference
                }).ConfigureAwait(false);
                fixture.Page = await context.NewPageAsync().ConfigureAwait(false);
                fixture.Page.PageError += (_, message) => fixture.PageErrors.Add(message);
                await fixture.Page.RouteAsync("**/api/**",
                    route => fixture.RespondAsync(route, role)).ConfigureAwait(false);
                IServer server = application.Services.GetRequiredService<IServer>();
                IServerAddressesFeature addresses = server.Features.Get<IServerAddressesFeature>()!;
                string address = string.Empty;
                foreach (string candidate in addresses.Addresses)
                {
                    address = candidate;
                    break;
                }
                await fixture.Page.GotoAsync(address).ConfigureAwait(false);
                return fixture;
            }
            catch
            {
                if (browser != null)
                {
                    await browser.DisposeAsync().ConfigureAwait(false);
                }
                playwright?.Dispose();
                await application.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        /// <inheritdoc/>
        public async ValueTask DisposeAsync()
        {
            m_updates.Writer.TryComplete();
            await m_browser.DisposeAsync().ConfigureAwait(false);
            m_playwright.Dispose();
            await m_application.DisposeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Publishes the current controlled observation through an actual open event stream.
        /// </summary>
        internal ValueTask PublishAsync()
        {
            return m_updates.Writer.WriteAsync(State.ToJsonString());
        }

        /// <summary>
        /// Ends the current stream to exercise the browser's real reconnect path.
        /// </summary>
        internal void DisconnectStream()
        {
            m_updates.Writer.TryComplete();
        }

        private async Task RespondAsync(IRoute route, string role)
        {
            var uri = new Uri(route.Request.Url);
            string path = uri.AbsolutePath;
            JsonNode response;
            if (path == "/api/session")
            {
                response = new JsonObject
                {
                    ["role"] = role,
                    ["csrfToken"] = "browser-test-csrf",
                    ["expiresUtc"] = "2099-10-15T12:00:00Z"
                };
            }
            else if (path == "/api/events")
            {
                await route.ContinueAsync().ConfigureAwait(false);
                return;
            }
            else if (path == "/api/state")
            {
                if (StateStatus != 200)
                {
                    await route.FulfillAsync(new RouteFulfillOptions
                    {
                        Status = StateStatus,
                        ContentType = "application/json",
                        Body = "{\"error\":\"The local browser session expired.\"}"
                    }).ConfigureAwait(false);
                    return;
                }
                response = State;
            }
            else if (path.StartsWith("/api/processes/", StringComparison.Ordinal))
            {
                InspectorRequests.Add(path);
                string process = Uri.UnescapeDataString(path["/api/processes/".Length..]);
                response = BrowserPlantFixture.CreateInspector(State, process);
            }
            else if (path == "/api/holds")
            {
                var query = QueryHelpers.ParseQuery(uri.Query);
                long after = long.Parse(query["after"].ToString(), CultureInfo.InvariantCulture);
                int limit = int.Parse(query["limit"].ToString(), CultureInfo.InvariantCulture);
                string disposition = query["disposition"].ToString();
                JsonNode?[] page = (HoldArchive ?? State["holds"]!.AsArray())
                    .Where(item => long.Parse(item!["id"]!.GetValue<string>(), CultureInfo.InvariantCulture) > after &&
                        (disposition == "All" || item["disposition"]!.GetValue<string>() == disposition))
                    .OrderBy(item => long.Parse(item!["id"]!.GetValue<string>(), CultureInfo.InvariantCulture))
                    .Take(limit + 1).ToArray();
                var items = new JsonArray();
                foreach (JsonNode? item in page.Take(limit))
                {
                    items.Add(item!.DeepClone());
                }
                response = new JsonObject
                {
                    ["runId"] = State["runId"]!.DeepClone(),
                    ["revision"] = State["revision"]!.DeepClone(),
                    ["nextAfter"] = page.Length > limit ? items[^1]!["id"]!.DeepClone() : null,
                    ["items"] = items
                };
            }
            else if (path == "/api/history" || path == "/api/material")
            {
                response = new JsonObject
                {
                    ["runId"] = State["runId"]!.DeepClone(),
                    ["nextAfter"] = null,
                    ["items"] = new JsonArray()
                };
            }
            else if (path == "/api/commands" && route.Request.Method == "POST")
            {
                JsonObject command = JsonNode.Parse(route.Request.PostData!)!.AsObject();
                Commands.Add(command);
                if (CommandStatus != 200)
                {
                    await route.FulfillAsync(new RouteFulfillOptions
                    {
                        Status = CommandStatus,
                        ContentType = "application/json",
                        Body = "{\"error\":\"The observed revision changed; refresh before a new action.\"}"
                    }).ConfigureAwait(false);
                    return;
                }
                response = new JsonObject
                {
                    ["requestId"] = command["requestId"]!.DeepClone(),
                    ["state"] = "Accepted",
                    ["kind"] = command["kind"]!.DeepClone(),
                    ["runId"] = command["runId"]!.DeepClone(),
                    ["expectedRevision"] = command["expectedRevision"]!.DeepClone(),
                    ["revision"] = State["revision"]!.DeepClone(),
                    ["tick"] = State["tick"]!.DeepClone(),
                    ["status"] = "Good",
                    ["message"] = "Test command accepted; plant state remains authoritative.",
                    ["updatedUtc"] = "2026-10-15T12:00:00Z"
                };
            }
            else
            {
                throw new InvalidOperationException("Unexpected browser API request: " + path);
            }
            await route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = response.ToJsonString()
            }).ConfigureAwait(false);
        }

        private readonly WebApplication m_application;
        private readonly IPlaywright m_playwright;
        private readonly IBrowser m_browser;
        private readonly Channel<string> m_updates;
    }
}
