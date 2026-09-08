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
using System.Globalization;
using System.Threading;
using Microsoft.Extensions.Hosting;
using Opc.Ua.WotCon;
using Opc.Ua.WotCon.Client;
using AggregationClient;

try
{
    HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
    string? timeoutText = builder.Configuration["timeoutSeconds"];
    int timeoutSeconds = 480;
    if (timeoutText is not null &&
        (!int.TryParse(timeoutText, NumberStyles.Integer, CultureInfo.InvariantCulture, out timeoutSeconds) ||
            timeoutSeconds is < 1 or > 3600))
    {
        throw new ArgumentException("timeoutSeconds must be an integer between 1 and 3600.");
    }
    string? exerciseText = builder.Configuration["exerciseControls"];
    bool exerciseControls = false;
    if (exerciseText is not null && !bool.TryParse(exerciseText, out exerciseControls))
    {
        throw new ArgumentException("exerciseControls must be true or false.");
    }
    var options = new AggregationClientOptions
    {
        AggregationEndpoint = builder.Configuration["aggregationEndpoint"] ??
            "opc.tcp://localhost:62550/AggregationServer",
        SourceAEndpoint = builder.Configuration["sourceAEndpoint"] ??
            "opc.tcp://localhost:62551/SourceA",
        SourceBEndpoint = builder.Configuration["sourceBEndpoint"] ??
            "opc.tcp://localhost:62552/SourceB",
        ApplicationName = builder.Configuration["applicationName"] ??
            "AggregationClient",
        PkiRoot = builder.Configuration["pkiRoot"],
        ExerciseControls = exerciseControls,
        DocumentsDirectory = builder.Configuration["documentsDirectory"] ??
            System.IO.Path.Combine(AppContext.BaseDirectory, "Documents")
    };

    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
    AggregationClientResult result = await AggregationClientRunner
        .RunAsync(options, timeout.Token)
        .ConfigureAwait(false);

    foreach (WotRegistryDocumentLoadOutcome upload in result.LoadResult.Uploaded)
    {
        Console.WriteLine(
            $"Loaded {upload.Document.Kind} {upload.Document.ResourceId} " +
            $"(version {upload.VersionId}, created: {upload.Created}).");
    }

    WotRegistryRefreshResult refresh = result.LoadResult.Refresh ??
        throw new InvalidOperationException("The registry refresh did not run.");
    Console.WriteLine(
        $"Refresh generation {refresh.NewGeneration}: {refresh.Summary.Outcome}, " +
        $"{refresh.Summary.Succeeded}/{refresh.Summary.Total} succeeded.");
    foreach (WoTResourceLoadResultDataType resource in refresh.Results)
    {
        Console.WriteLine(
            $"  {resource.ResourceId}: {resource.Phase}/{resource.Outcome} " +
            $"({resource.LoadState})");
        if (!string.IsNullOrWhiteSpace(resource.Message))
        {
            Console.WriteLine($"    Message: {resource.Message}");
        }
    }

    int totalValues = 0;
    foreach (WotPumpResult pump in result.Pumps)
    {
        Console.WriteLine($"Materialized {pump.Name} browse:");
        foreach (WotPumpBrowseNode node in pump.BrowsedNodes)
        {
            Console.WriteLine($"  {node.NodeId}: {node.DisplayName} ({node.NodeClass})");
        }
        Console.WriteLine($"Materialized {pump.Name} values:");
        foreach (WotPumpValueResult value in pump.Values)
        {
            Console.WriteLine($"  {pump.Name}.{value.Name}: {value.Value} [{value.StatusCode}]");
            totalValues++;
        }
    }
    Console.WriteLine(
        $"WOT_AGGREGATION_DATA_OK documents={result.LoadResult.Uploaded.Count} " +
        $"pumps={result.Pumps.Count} values={totalValues}");
    foreach (WotPumpControlResult control in result.Controls)
    {
        Console.WriteLine(
            $"{control.PumpName}/{control.SourceName}: Start, Stop, Reset, Acknowledge and Confirm completed.");
    }
    if (result.Controls.Count == 4)
    {
        Console.WriteLine($"WOT_AGGREGATION_CONTROLS_OK sourcePumpPairs={result.Controls.Count}");
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex);
    Console.Error.WriteLine(ex.StackTrace);
    Environment.ExitCode = 1;
}
