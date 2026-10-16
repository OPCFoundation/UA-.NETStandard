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

using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

/// <summary>
/// Observes the pinned WMTP diagnostic without modifying library or test behavior.
/// </summary>
internal static class StartupHook
{
    /// <summary>
    /// Installs bounded process-local observers when the diagnostic environment is present.
    /// </summary>
    public static void Initialize()
    {
        string? directory = Environment.GetEnvironmentVariable("WMTP_BUSY_DIAGNOSTICS");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }
        Directory.CreateDirectory(directory);
        s_path = Path.Combine(directory, $"wmtp-busy-{Environment.ProcessId}.jsonl");
        Emit("initialized", new
        {
            process = Environment.ProcessId,
            runtime = Environment.Version.ToString(),
            architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString()
        });
        AppDomain.CurrentDomain.FirstChanceException += OnException;
        s_meter = new MeterListener
        {
            InstrumentPublished = static (instrument, listener) =>
            {
                if (instrument.Name == "opcua.server.isolation.rejections")
                {
                    Emit("meter-subscribed", new { instrument = instrument.Name });
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        s_meter.SetMeasurementEventCallback<long>(static (instrument, value, tags, _) =>
        {
            Interlocked.Add(ref s_rejections, value);
            var labels = new Dictionary<string, string?>();
            foreach (KeyValuePair<string, object?> tag in tags)
            {
                if (tag.Key is "stage" or "reason")
                {
                    labels.Add(tag.Key, tag.Value?.ToString());
                }
            }
            Observe("admission-rejection", new { instrument = instrument.Name, value, labels });
        });
        s_meter.Start();
        s_activities = new ActivityListener
        {
            ShouldListenTo = static source =>
                source.Name is "Experimental.ModelContextProtocol" or "ModelContextProtocol",
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllData,
            SampleUsingParentId = static (ref ActivityCreationOptions<string> _) =>
                ActivitySamplingResult.AllData,
            ActivityStarted = static activity => OnActivity(activity, "mcp-start"),
            ActivityStopped = static activity => OnActivity(activity, "mcp-stop")
        };
        ActivitySource.AddActivityListener(s_activities);
        AppDomain.CurrentDomain.ProcessExit += static (_, _) =>
        {
            Emit("completed", new
            {
                busyNotifications = s_busy,
                admissionRejections = s_rejections,
                hookErrors = s_errors,
                truncated = s_truncated
            });
            s_meter?.Dispose();
            s_activities?.Dispose();
        };
    }

    private static void OnActivity(Activity activity, string phase)
    {
        if (!activity.DisplayName.StartsWith("tools/call wmtp_", StringComparison.Ordinal))
        {
            return;
        }
        Observe(phase, new
        {
            tool = activity.DisplayName,
            source = activity.Source.Name,
            kind = activity.Kind.ToString(),
            trace = activity.TraceId.ToString(),
            span = activity.SpanId.ToString(),
            parent = activity.ParentSpanId.ToString(),
            status = activity.Status.ToString()
        });
    }

    private static void OnException(object? sender, FirstChanceExceptionEventArgs args)
    {
        if (s_observing ||
            args.Exception.GetType().FullName is not
                ("Opc.Ua.ServiceResultException" or "Opc.Ua.ServerBusyException"))
        {
            return;
        }
        // Only classify the exception here; do not export its message, AdditionalInfo or data.
        if (!args.Exception.ToString().Contains("BadServerTooBusy", StringComparison.Ordinal))
        {
            return;
        }
        Interlocked.Increment(ref s_busy);
        Observe("busy", new
        {
            exception = args.Exception.GetType().FullName,
            exceptionIdentity = RuntimeHelpers.GetHashCode(args.Exception),
            status = "BadServerTooBusy",
            peripheralGateMessage = args.Exception.Message.Contains(
                "This peripheral already has an active operation.", StringComparison.Ordinal),
            throwStack = args.Exception.StackTrace,
            observerStack = Environment.StackTrace
        });
    }

    private static void Observe(string kind, object detail)
    {
        if (s_observing)
        {
            return;
        }
        s_observing = true;
        try
        {
            Emit(kind, new
            {
                thread = Environment.CurrentManagedThreadId,
                trace = Activity.Current?.TraceId.ToString(),
                span = Activity.Current?.SpanId.ToString(),
                context = CaptureContext(),
                detail
            });
        }
        catch (Exception error)
        {
            Interlocked.Increment(ref s_errors);
            Emit("hook-error", new { kind, errorType = error.GetType().FullName });
        }
        finally
        {
            s_observing = false;
        }
    }

    private static Dictionary<string, object?> CaptureContext()
    {
        var result = new Dictionary<string, object?>();
        Type? contextType = Type.GetType("NUnit.Framework.Internal.TestExecutionContext, nunit.framework");
        object? context = contextType?.GetProperty("CurrentContext", kStatic)?.GetValue(null);
        object? test = Property(context, "CurrentTest");
        string? name = Property(test, "FullName") as string;
        if (name is null || !name.StartsWith(
            "Opc.Ua.Tools.Tests.WmtpMcpIntegrationTests", StringComparison.Ordinal))
        {
            result["testContextAvailable"] = false;
            return result;
        }
        result["test"] = name;
        object? testObject = Property(context, "TestObject");
        if (testObject?.GetType().FullName != "Opc.Ua.Tools.Tests.WmtpMcpIntegrationTests")
        {
            result["fixtureAvailable"] = false;
            return result;
        }
        object? fixture = Field(testObject, "m_fixture");
        object? harness = Field(testObject, "m_mcp");
        result["lastMcpRequestHash"] = Hash(Property(harness, "LastRequestId"));
        var owners = new List<Dictionary<string, object?>>();
        foreach (string role in new[] { "Peripheral", "Other", "Minimal" })
        {
            object? peripheral = Property(fixture, role);
            if (peripheral is null)
            {
                continue;
            }
            object? coordinator = Field(peripheral, "m_operations");
            object? active = Field(coordinator, "m_active");
            object? session = Property(active, "SessionId");
            object? cancellation = Property(active, "Cancellation");
            var owner = new Dictionary<string, object?>
            {
                ["role"] = role,
                ["peripheralHash"] = Hash(Property(peripheral, "NodeId")),
                ["coordinatorIdentity"] = coordinator is null ? null : RuntimeHelpers.GetHashCode(coordinator),
                ["activeIdentity"] = active is null ? null : RuntimeHelpers.GetHashCode(active),
                ["activeSessionHash"] = Hash(session),
                ["trustedApplicationOperation"] = Property(session, "IsNull"),
                ["cancellationRequested"] = Property(cancellation, "IsCancellationRequested"),
                ["ageTaskPending"] = Field(peripheral, "m_ageTask") is Task age && !age.IsCompleted,
                ["sameActiveAtEnd"] = ReferenceEquals(active, Field(coordinator, "m_active")),
                ["snapshotIsAtomic"] = false
            };
            owners.Add(owner);
        }
        result["owners"] = owners;
        return result;
    }

    private static object? Property(object? instance, string name)
    {
        return instance?.GetType().GetProperty(name, kInstance)?.GetValue(instance);
    }

    private static object? Field(object? instance, string name)
    {
        return instance?.GetType().GetField(name, kInstance)?.GetValue(instance);
    }

    private static string? Hash(object? value)
    {
        return value is null ? null :
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToString() ?? string.Empty)))[..16];
    }

    private static void Emit(string kind, object data)
    {
        lock (s_gate)
        {
            if (s_bytes >= kMaximumBytes && kind != "completed")
            {
                s_truncated = true;
                return;
            }
            string line = JsonSerializer.Serialize(new
            {
                marker = "DEBUG-wmtp-busy-68eda",
                utc = DateTimeOffset.UtcNow,
                process = Environment.ProcessId,
                kind,
                data
            }) + Environment.NewLine;
            File.AppendAllText(s_path!, line);
            s_bytes += Encoding.UTF8.GetByteCount(line);
        }
    }

    private const BindingFlags kInstance = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags kStatic = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    private const int kMaximumBytes = 8 * 1024 * 1024;
    private static readonly Lock s_gate = new();
    private static string? s_path;
    private static MeterListener? s_meter;
    private static ActivityListener? s_activities;
    private static long s_busy;
    private static long s_rejections;
    private static long s_errors;
    private static long s_bytes;
    private static bool s_truncated;
    [ThreadStatic]
    private static bool s_observing;
}
