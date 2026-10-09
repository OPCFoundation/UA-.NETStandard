// Trace analyzer for perf/ServerLoadHarness traces. Not part of the product; see perf/README.md.
//   TraceAnalyzer cpu        <trace> [top] [focus]  : on-CPU samples (blocked leaf frames dropped), exclusive/inclusive, callers of focus
//   TraceAnalyzer alloc      <trace> [top] [type]   : GCAllocationTick bytes by type, by first Opc.Ua frame, and stacks
//   TraceAnalyzer contention <trace> [top]          : ContentionStart events by Opc.Ua stack
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Etlx;
using Microsoft.Diagnostics.Tracing.Parsers.Clr;

string mode = args[0];
string file = args[1];
int top = args.Length > 2 ? int.Parse(args[2]) : 40;
string focus = args.Length > 3 ? args[3] : null;
string[] s_blocked = { "LowLevelLifoSemaphore.Wait", "WaitForSignal", "WorkerThreadStart", "IOCompletionPoller.Poll", "WaitOneNoCheck", "Thread.Sleep", "PollGCWorker|70_0", "WaitMultipleIgnoringSyncContext", "Monitor.Wait", "!?" };

string etlx = TraceLog.CreateFromEventPipeDataFile(file, null, new TraceLogOptions { ContinueOnError = true });
using var log = new TraceLog(etlx);

static string Name(TraceCallStack s)
{
    var m = s.CodeAddress.FullMethodName;
    if (string.IsNullOrEmpty(m))
    {
        m = s.CodeAddress.ModuleName + "!?";
    }
    return m;
}

static string Short(string m)
{
    int p = m.IndexOf('(');
    return p > 0 ? m[..p] : m;
}

static bool IsOurs(string m) => m.StartsWith("Opc.Ua", StringComparison.Ordinal) || m.StartsWith("Quickstarts", StringComparison.Ordinal);

if (mode == "contention")
{
    var byStack = new Dictionary<string, int>();
    int total = 0;
    foreach (TraceEvent e in log.Events)
    {
        if (e.EventName == null || !e.EventName.StartsWith("Contention/Start")) continue;
        total++;
        var chain = new List<string>();
        for (TraceCallStack f = e.CallStack(); f != null && chain.Count < 7; f = f.Caller) { string n = Short(Name(f)); if (IsOurs(n) || chain.Count == 0) chain.Add(n); }
        string k = string.Join("\n      ", chain);
        byStack[k] = byStack.GetValueOrDefault(k) + 1;
    }
    Console.WriteLine($"contention events: {total}");
    foreach (var kv in byStack.OrderByDescending(k => k.Value).Take(top)) Console.WriteLine($"{100.0 * kv.Value / total,6:F2}%  {kv.Key}");
    return;
}
if (mode == "cpu")
{
    var excl = new Dictionary<string, int>();
    var incl = new Dictionary<string, int>();
    var firstOurs = new Dictionary<string, int>();
    var callers = new Dictionary<string, int>();
    int total = 0, all = 0;
    foreach (TraceEvent e in log.Events)
    {
        if (e.ProviderName != "Microsoft-DotNETCore-SampleProfiler")
        {
            continue;
        }
        all++;
        object type = e.PayloadByName("Type");
        if (type != null && Convert.ToInt32(type) != 2)
        {
            continue; // not running managed code
        }
        TraceCallStack s = e.CallStack();
        if (s == null)
        {
            continue;
        }
        string leaf = Short(Name(s));
        if (s_blocked.Any(b => leaf.EndsWith(b))) continue;
        total++;
        excl[leaf] = excl.GetValueOrDefault(leaf) + 1;
        var seen = new HashSet<string>();
        bool gotOurs = false;
        bool focusHit = false;
        for (TraceCallStack f = s; f != null; f = f.Caller)
        {
            string n = Short(Name(f));
            if (seen.Add(n))
            {
                incl[n] = incl.GetValueOrDefault(n) + 1;
            }
            if (!gotOurs && IsOurs(n))
            {
                gotOurs = true;
                firstOurs[n] = firstOurs.GetValueOrDefault(n) + 1;
            }
            if (focus != null && !focusHit && n.Contains(focus))
            {
                focusHit = true;
                string c = f.Caller != null ? Short(Name(f.Caller)) : "<root>";
                callers[c] = callers.GetValueOrDefault(c) + 1;
            }
        }
    }
    Console.WriteLine($"samples: managed-running={total} all={all}");
    void Dump(string title, Dictionary<string, int> d, int n)
    {
        Console.WriteLine($"--- {title}");
        foreach (var kv in d.OrderByDescending(k => k.Value).Take(n))
        {
            Console.WriteLine($"{100.0 * kv.Value / total,6:F2}%  {kv.Key}");
        }
    }
    Dump("exclusive", excl, top);
    Dump("first Opc.Ua frame (exclusive-ish)", firstOurs, top);
    Dump("inclusive (Opc.Ua only)", incl.Where(k => IsOurs(k.Key)).ToDictionary(k => k.Key, k => k.Value), top);
    if (focus != null)
    {
        Dump("callers of " + focus, callers, top);
    }
}
else
{
    var byType = new Dictionary<string, double>();
    var byFrame = new Dictionary<string, double>();
    var byStack = new Dictionary<string, double>();
    double total = 0;
    foreach (TraceEvent e in log.Events)
    {
        if (e is not GCAllocationTickTraceData a)
        {
            continue;
        }
        double amount = a.AllocationAmount64;
        total += amount;
        string t = a.TypeName ?? "?";
        byType[t] = byType.GetValueOrDefault(t) + amount;
        TraceCallStack s = e.CallStack();
        string frame = "?";
        var chain = new List<string>();
        for (TraceCallStack f = s; f != null; f = f.Caller)
        {
            string n = Short(Name(f));
            if (IsOurs(n))
            {
                if (frame == "?")
                {
                    frame = n;
                }
                if (chain.Count < 6)
                {
                    chain.Add(n);
                }
            }
        }
        byFrame[t + " <- " + frame] = byFrame.GetValueOrDefault(t + " <- " + frame) + amount;
        if (focus == null || t.Contains(focus))
        {
            string k = t + "\n      " + string.Join("\n      ", chain);
            byStack[k] = byStack.GetValueOrDefault(k) + amount;
        }
    }
    Console.WriteLine($"sampled alloc total: {total / 1048576:F1} MB");
    void Dump(string title, Dictionary<string, double> d, int n)
    {
        Console.WriteLine($"--- {title}");
        foreach (var kv in d.OrderByDescending(k => k.Value).Take(n))
        {
            Console.WriteLine($"{100.0 * kv.Value / total,6:F2}%  {kv.Key}");
        }
    }
    Dump("by type", byType, top);
    Dump("by type <- first Opc.Ua frame", byFrame, top);
    Dump("stacks", byStack, top / 2);
}
