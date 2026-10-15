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

using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Opc.Ua.Client;
using Opc.Ua.Configuration;
using Opc.Ua.Security.Certificates;

namespace Opc.Ua.Recycling.OperatorConsole;

/// <summary>
/// NativeAOT-compatible local operator workstation. No browser route exposes arbitrary OPC UA calls.
/// </summary>
public static class OperatorConsoleHost
{
    /// <summary>
    /// Builds an unstarted host. Callbacks permit TestServer and injected feeds without a fake runtime mode.
    /// </summary>
    public static WebApplication Build(
        ArrayOf<string> args,
        Action<WebApplicationBuilder>? configureBuilder = null,
        Action<IServiceCollection>? configureServices = null)
    {
        string contentRoot = Directory.Exists(Path.Combine(AppContext.BaseDirectory, "wwwroot"))
            ? AppContext.BaseDirectory : Directory.GetCurrentDirectory();
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            Args = args.Span.ToArray(),
            ContentRootPath = contentRoot,
            WebRootPath = Path.Combine(contentRoot, "wwwroot")
        });
        configureBuilder?.Invoke(builder);
        ConsoleOptions options = ConsoleOptions.Read(builder.Configuration);
        builder.WebHost.UseUrls(options.ContainerBind ? $"http://0.0.0.0:{options.Port}" : options.Origin);
        builder.WebHost.ConfigureKestrel(static server => server.Limits.MaxRequestBodySize = 8192);
        // Framework request-start messages include query strings, including the local bootstrap credential.
        builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
        builder.Services.ConfigureHttpJsonOptions(static json =>
            json.SerializerOptions.TypeInfoResolverChain.Insert(0, ConsoleJsonContext.Default));
        builder.Services.AddSingleton(options);
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(static services => new ConsoleSessions(
            services.GetRequiredService<ConsoleOptions>(), services.GetRequiredService<TimeProvider>()));
        builder.Services.AddSingleton<ConsoleConnections>();
        builder.Services.AddSingleton<ConsoleNativeInspector>();
        builder.Services.AddSingleton<ConsoleCommands>();
        builder.Services.AddSingleton<ConsoleFeed>();
        builder.Services.AddSingleton<IOperatorPlantFeed>(static services =>
            services.GetRequiredService<ConsoleFeed>());
        builder.Services.AddHostedService(static services => services.GetRequiredService<ConsoleFeed>());
        builder.Services.TryAddSingleton<ICertificatePasswordProvider>(_ =>
            options.UserCertificatePasswordFile.Length == 0
                ? new CertificatePasswordProvider()
                : new CertificatePasswordProvider(File.ReadAllText(options.UserCertificatePasswordFile)
                    .TrimEnd('\r', '\n').AsSpan()));
        builder.Services.TryAddSingleton<ICertificateProvider>(static services =>
            services.GetRequiredService<IOpcUaApplicationConfigurationProvider>()
                .Configuration.CertificateManager?.CertificateProvider ??
            throw new InvalidOperationException("Initialize the application certificate manager first."));
        builder.Services.AddOpcUa().ConfigureApplication(application =>
        {
            application.ApplicationName = "Recycling Operator Console";
            application.ApplicationUri = "urn:localhost:OPCFoundation:RecyclingOperatorConsole";
            application.ProductUri = "urn:opcfoundation.org:sample:recycling:operator";
            application.PkiRoot = Path.GetFullPath(options.ApplicationCertificateStore);
            application.AutoAcceptUntrustedCertificates = false;
            application.RejectSHA1SignedCertificates = true;
            application.MinimumCertificateKeySize = 2048;
            application.ConfigureSecurity = security => security.SetApplicationCertificates(
            [
                new CertificateIdentifier
                {
                    StoreType = CertificateStoreType.Directory,
                    StorePath = Path.GetFullPath(options.ApplicationCertificateStore),
                    SubjectName = "CN=Recycling Operator Console, O=OPC Foundation, DC=localhost",
                    CertificateType = ObjectTypeIds.RsaSha256ApplicationCertificateType
                }
            ]);
        }).AddClient(client =>
        {
            client.Session = new ManagedSessionOptions
            {
                SessionName = "Recycling operator workstation",
                SessionTimeout = TimeSpan.FromMinutes(1),
                CheckDomain = true
            };
            client.Identity.EnableAnonymous = false;
            client.Identity.X509 = new X509ClientIdentityOptions
            {
                StoreType = CertificateStoreType.Directory,
                StorePath = Path.GetFullPath(options.UserCertificatePath),
                SubjectName = options.UserCertificateSubject
            };
        }).AddDiscovery();
        builder.Services.AddSingleton<Func<CancellationToken, Task<ManagedSession>>>(static services =>
            cancellationToken => services.GetRequiredService<ConsoleConnections>().ConnectAsync(
                services.GetRequiredService<ConsoleOptions>().Endpoint, cancellationToken));
        configureServices?.Invoke(builder.Services);
        WebApplication app = builder.Build();
        _ = app.Services.GetRequiredService<ConsoleSessions>();
        ConfigureHttp(app, options);
        return app;
    }

    /// <summary>
    /// Configures strict local HTTP guards before both authenticated static assets and APIs.
    /// </summary>
    private static void ConfigureHttp(WebApplication app, ConsoleOptions options)
    {
        app.Use(async (context, next) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["Content-Security-Policy"] =
                "default-src 'none'; script-src 'self'; style-src 'self'; img-src 'self' data:; " +
                "connect-src 'self'; base-uri 'none'; frame-ancestors 'none'; form-action 'self'";
            if (!string.Equals(context.Request.Host.Value, options.AllowedHost, StringComparison.OrdinalIgnoreCase) ||
                context.Request.Headers["Sec-Fetch-Site"] == "cross-site")
            {
                await ErrorAsync(context, 403, "Host or fetch origin is not allowed.").ConfigureAwait(false);
                return;
            }
            ConsoleSessions sessions = context.RequestServices.GetRequiredService<ConsoleSessions>();
            if (context.Request.Path == "/bootstrap")
            {
                if (!HttpMethods.IsGet(context.Request.Method) ||
                    sessions.Bootstrap(context, context.Request.Query["token"].ToString()) == null)
                {
                    await ErrorAsync(context, 401, "Invalid bootstrap credential or session limit.")
                        .ConfigureAwait(false);
                    return;
                }
                context.Response.Redirect("/");
                return;
            }
            if (!context.Request.Path.StartsWithSegments("/api", StringComparison.Ordinal))
            {
                if (HttpMethods.IsGet(context.Request.Method) && context.Request.Path.Value is
                    "/" or "/index.html" or "/styles/site.css" or "/scripts/app.js" or "/scripts/api.js" or
                    "/scripts/format.js" or "/scripts/render.js" or "/assets/symbols.svg")
                {
                    await next(context).ConfigureAwait(false);
                }
                else
                {
                    await ErrorAsync(context, 404, "Unknown local resource.").ConfigureAwait(false);
                }
                return;
            }
            SessionDto? session = sessions.Get(context);
            if (session == null)
            {
                await ErrorAsync(context, 401,
                    "Open the local bootstrap URL using the externally provisioned launch credential.")
                    .ConfigureAwait(false);
                return;
            }
            if (!HttpMethods.IsGet(context.Request.Method) &&
                !ConsoleSessions.CanMutate(context, session, options))
            {
                await ErrorAsync(context, 403, "Same-origin anti-CSRF validation failed.").ConfigureAwait(false);
                return;
            }
            await next(context).ConfigureAwait(false);
        });
        app.UseDefaultFiles();
        app.UseStaticFiles();
        app.MapGet("/api/session", (HttpContext context, ConsoleSessions sessions) =>
            Results.Json(sessions.Get(context)!, ConsoleJsonContext.Default.SessionDto));
        app.MapGet("/api/state", (IOperatorPlantFeed feed) =>
            Results.Json(feed.State, ConsoleJsonContext.Default.ConsoleState));
        app.MapGet("/api/events", StreamAsync);
        app.MapGet("/api/processes/{process}", ReadProcessAsync);
        app.MapGet("/api/material", ReadMaterial);
        app.MapGet("/api/holds", ReadHolds);
        app.MapGet("/api/history", ReadHistoryAsync);
        app.MapPost("/api/commands", SubmitCommandAsync);
        app.MapGet("/api/commands/{requestId}", (string requestId, ConsoleCommands commands) =>
            commands.Find(requestId) is { } result
                ? Results.Json(result, ConsoleJsonContext.Default.CommandResult)
                : Results.Json(new ErrorDto("No workstation receipt exists."), ConsoleJsonContext.Default.ErrorDto,
                    statusCode: 404));
    }

    /// <summary>
    /// Streams one coalescible projection per consumer, with an explicit maximum consumer count.
    /// </summary>
    private static async Task StreamAsync(
        HttpContext context, IOperatorPlantFeed feed, ConsoleSessions sessions, TimeProvider timeProvider)
    {
        SessionDto? session = sessions.Get(context);
        TimeSpan lifetime = (session?.ExpiresUtc ?? DateTime.MinValue) -
            timeProvider.GetUtcNow().UtcDateTime;
        if (lifetime <= TimeSpan.Zero)
        {
            await ErrorAsync(context, 401, "Browser session expired.").ConfigureAwait(false);
            return;
        }
        using var expiry = new CancellationTokenSource(lifetime, timeProvider);
        using var streamStop = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, expiry.Token);
        var channel = feed.Subscribe();
        if (channel == null)
        {
            await ErrorAsync(context, 429, "Maximum live browser consumers reached.").ConfigureAwait(false);
            return;
        }
        context.Response.ContentType = "text/event-stream";
        context.Response.Headers["X-Accel-Buffering"] = "no";
        try
        {
            await foreach (ConsoleState state in channel.Reader.ReadAllAsync(streamStop.Token)
                .ConfigureAwait(false))
            {
                await context.Response.WriteAsync("event: state\ndata: " +
                    JsonSerializer.Serialize(state, ConsoleJsonContext.Default.ConsoleState) + "\n\n",
                    streamStop.Token).ConfigureAwait(false);
                await context.Response.Body.FlushAsync(streamStop.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (streamStop.IsCancellationRequested)
        {
        }
        finally
        {
            feed.Unsubscribe(channel);
        }
    }

    /// <summary>
    /// Loads independently qualified selected-process data.
    /// </summary>
    private static async Task<IResult> ReadProcessAsync(
        string process, IOperatorPlantFeed feed, ConsoleNativeInspector inspector, HttpContext context)
    {
        ConsoleState overview = feed.State;
        ProcessDto? selected = overview.Processes.FirstOrDefault(item => item.Name == process);
        if (selected == null || feed.Snapshot is not { } snapshot)
        {
            return Failure(404, "Unknown process or no coordinator image.");
        }
        if (overview.RunId != snapshot.RunId || overview.ConfigurationId != snapshot.ConfigurationId)
        {
            return Failure(409, "Overview changed during native inspection selection; read the current image.");
        }
        InspectorDto result = await inspector.ReadAsync(selected, snapshot, context.RequestAborted)
            .ConfigureAwait(false);
        if (feed.Snapshot?.RunId != snapshot.RunId || feed.Snapshot.ConfigurationId != snapshot.ConfigurationId)
        {
            return Failure(409, "Coordinator reset/configuration changed during native inspection.");
        }
        return Results.Json(result, ConsoleJsonContext.Default.InspectorDto);
    }

    /// <summary>
    /// Pages retained material without returning a full simulator snapshot per browser.
    /// </summary>
    private static IResult ReadMaterial(HttpContext context, IOperatorPlantFeed feed)
    {
        if (!PageRequest(context, feed, out CommittedSnapshot? snapshot, out long after, out int limit))
        {
            return Failure(409, "A current matching runId and valid after/limit (1..100) are required.");
        }
        var selected = new SortedDictionary<long, (MaterialParcel Parcel, long Tick, string Kind)>();
        void Consider(MaterialParcel parcel, long tick, string kind)
        {
            if (parcel.Id <= after)
            {
                return;
            }
            selected[parcel.Id] = (parcel, tick, kind);
            if (selected.Count > limit + 1)
            {
                selected.Remove(selected.Last().Key);
            }
        }
        foreach (MaterialParcel parcel in snapshot!.Parcels)
        {
            Consider(parcel, parcel.EnteredTick, "Parcel");
        }
        foreach (MaterialPackage package in snapshot.Packages)
        {
            Consider(package.Material, package.Tick, "Package");
        }
        foreach (MaterialResidue residue in snapshot.Residues)
        {
            Consider(residue.Material, residue.Tick, "Residue");
        }
        bool more = selected.Count > limit;
        MaterialDto[] items = selected.Values.Take(limit)
            .Select(static item => Material(item.Parcel, item.Tick, item.Kind)).ToArray();
        return Results.Json(new MaterialPage(snapshot.RunId,
            more ? items[^1].Id : null, items), ConsoleJsonContext.Default.MaterialPage);
    }

    /// <summary>
    /// Pages every retained hold, independently of the compact outstanding-hold preview.
    /// </summary>
    private static IResult ReadHolds(HttpContext context, IOperatorPlantFeed feed)
    {
        if (!PageRequest(context, feed, out CommittedSnapshot? snapshot, out long after, out int limit))
        {
            return Failure(409, "A current matching runId and valid after/limit (1..100) are required.");
        }
        string filter = context.Request.Query["disposition"].FirstOrDefault() ?? "All";
        HoldDisposition disposition = default;
        if (filter != "All" && (!Enum.TryParse(filter, false, out disposition) ||
            !Enum.IsDefined(disposition) || disposition.ToString() != filter))
        {
            return Failure(400, "Disposition must be All, Held, Released or Rejected.");
        }
        var selected = new SortedDictionary<long, QualityHold>();
        foreach (QualityHold hold in snapshot!.Holds)
        {
            if (hold.Id <= after || (filter != "All" && hold.Disposition != disposition))
            {
                continue;
            }
            selected[hold.Id] = hold;
            if (selected.Count > limit + 1)
            {
                selected.Remove(selected.Last().Key);
            }
        }
        bool more = selected.Count > limit;
        HoldDto[] items = selected.Values.Take(limit).Select(ConsoleFeed.ProjectHold).ToArray();
        return Results.Json(new HoldPage(snapshot.RunId, ConsoleFeed.Text(snapshot.Revision),
            more ? items[^1].Id : null, items), ConsoleJsonContext.Default.HoldPage);
    }

    /// <summary>
    /// Retrieves the authoritative typed bounded genealogy page and checks reset races.
    /// </summary>
    private static async Task<IResult> ReadHistoryAsync(HttpContext context, IOperatorPlantFeed feed)
    {
        if (!PageRequest(context, feed, out CommittedSnapshot? snapshot, out long after, out int limit))
        {
            return Failure(409, "A current matching runId and valid after/limit (1..100) are required.");
        }
        try
        {
            ArrayOf<MaterialExecution> history = await feed.ReadHistoryAsync(after, limit, context.RequestAborted)
                .ConfigureAwait(false);
            if (feed.Snapshot?.RunId != snapshot!.RunId)
            {
                return Failure(409, "Run changed while reading history.");
            }
            ExecutionDto[] items = history.Span.ToArray().Select(static execution => new ExecutionDto(
                ConsoleFeed.Text(execution.Id), ConsoleFeed.Text(execution.Tick), execution.Stage,
                execution.Inputs.Span.ToArray().Select(ConsoleFeed.Text).ToArray(),
                execution.Outputs.Span.ToArray().Select(ConsoleFeed.Text).ToArray(), ConsoleFeed.Mass(execution.Input),
                ConsoleFeed.Mass(execution.Addition), ConsoleFeed.Mass(execution.Output),
                ConsoleFeed.Mass(execution.Reject), ConsoleFeed.Mass(execution.Evaporation))).ToArray();
            return Results.Json(new HistoryPage(snapshot.RunId,
                items.Length == limit ? items[^1].Id : null, items), ConsoleJsonContext.Default.HistoryPage);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            ITelemetryContext telemetry = context.RequestServices.GetRequiredService<ITelemetryContext>();
            string status = exception is ServiceResultException service
                ? service.StatusCode.ToString() : "BadCommunicationError";
            ConsoleLog.HistoryFailed(telemetry.CreateLogger(nameof(OperatorConsoleHost)), status, exception);
            return Failure(503, "Typed coordinator history unavailable: " + status);
        }
    }

    /// <summary>
    /// Reads one bounded command envelope with generated JSON metadata.
    /// </summary>
    private static async Task<IResult> SubmitCommandAsync(HttpContext context, ConsoleCommands commands)
    {
        CommandRequest? request;
        try
        {
            request = await JsonSerializer.DeserializeAsync(context.Request.Body,
                ConsoleJsonContext.Default.CommandRequest, context.RequestAborted).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return Failure(400, "Invalid command JSON.");
        }
        if (request == null || request.RequestId == null || request.RunId == null ||
            request.ExpectedRevision == null || request.Kind == null || request.Process == null ||
            request.HoldId == null || request.Disposition == null)
        {
            return Failure(400, "Command fields must not be null.");
        }
        var submission = await commands.SubmitAsync(request, context.RequestAborted).ConfigureAwait(false);
        return submission.Status == 200 && submission.Result != null
            ? Results.Json(submission.Result, ConsoleJsonContext.Default.CommandResult)
            : Failure(submission.Status, submission.Error);
    }

    /// <summary>
    /// Validates bounded run-scoped cursors.
    /// </summary>
    private static bool PageRequest(
        HttpContext context, IOperatorPlantFeed feed, out CommittedSnapshot? snapshot, out long after, out int limit)
    {
        snapshot = feed.Snapshot;
        after = 0;
        limit = 50;
        return feed.State.Available && snapshot != null && context.Request.Query["runId"] == snapshot.RunId &&
            (context.Request.Query["after"].Count == 0 || long.TryParse(context.Request.Query["after"],
                NumberStyles.None, CultureInfo.InvariantCulture, out after)) &&
            (context.Request.Query["limit"].Count == 0 || int.TryParse(context.Request.Query["limit"],
                NumberStyles.None, CultureInfo.InvariantCulture, out limit)) && after >= 0 && limit is >= 1 and <= 100;
    }

    /// <summary>
    /// Projects exact retained material genealogy.
    /// </summary>
    private static MaterialDto Material(MaterialParcel parcel, long tick, string kind) => new(
        ConsoleFeed.Text(parcel.Id), ConsoleFeed.Text(tick), kind, parcel.Stage, parcel.Form.ToString(),
        ConsoleFeed.Mass(parcel.Mass), parcel.HoldIds.Span.ToArray().Select(ConsoleFeed.Text).ToArray(),
        parcel.Contributions.Span.ToArray().Select(static contribution => new SourceDto(
            contribution.SourceId, contribution.SourceZone, ConsoleFeed.Mass(contribution.Mass))).ToArray());

    /// <summary>
    /// Creates an explicit generated-JSON failure.
    /// </summary>
    private static IResult Failure(int status, string detail) =>
        Results.Json(new ErrorDto(detail), ConsoleJsonContext.Default.ErrorDto, statusCode: status);

    /// <summary>
    /// Writes a guarded middleware failure without reflection serialization.
    /// </summary>
    private static Task ErrorAsync(HttpContext context, int status, string detail)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(
            JsonSerializer.Serialize(new ErrorDto(detail), ConsoleJsonContext.Default.ErrorDto),
            context.RequestAborted);
    }
}
