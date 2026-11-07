using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
using System.Linq;
using System.Text;

namespace Signet.Benchmarks;

/// <summary>
/// Samples the runtime's <c>GCAllocationTick</c> event (one per ~100 KB allocated) inside the process and sums it by type: the answer to "what
/// allocates", exact in kind and statistical in size, with none of the safepoint bias of a sampled CPU profile. The event carries the type, not
/// the stack, so it says <em>what</em> is allocated; the callers are found by searching the code for that type.
/// </summary>
internal sealed class AllocationProfile : EventListener
{
    private const int GcKeyword = 0x1;
    private readonly ConcurrentDictionary<(string Type, bool Large), long> _bytes = new();

    protected override void OnEventSourceCreated(EventSource source)
    {
        if (source.Name == "Microsoft-Windows-DotNETRuntime")
        {
            EnableEvents(source, EventLevel.Verbose, (EventKeywords)GcKeyword);
        }
    }

    protected override void OnEventWritten(EventWrittenEventArgs e)
    {
        if (e.EventName is null || !e.EventName.StartsWith("GCAllocationTick", StringComparison.Ordinal) || e.Payload is null)
        {
            return;
        }

        string type = "?";
        long amount = 0;
        bool large = false;
        for (int i = 0; i < e.PayloadNames!.Count; i++)
        {
            switch (e.PayloadNames[i])
            {
                case "TypeName": type = e.Payload[i]?.ToString() ?? "?"; break;
                case "AllocationAmount64": amount = Convert.ToInt64(e.Payload[i], System.Globalization.CultureInfo.InvariantCulture); break;
                case "AllocationKind": large = Convert.ToInt32(e.Payload[i], System.Globalization.CultureInfo.InvariantCulture) == 1; break;
            }
        }

        _bytes.AddOrUpdate((type, large), amount, (_, old) => old + amount);
    }

    /// <summary>The types that allocated most, as a markdown table (MiB, share of the total, LOH marked).</summary>
    public string Table(int top)
    {
        long total = _bytes.Values.Sum();
        StringBuilder sb = new();
        sb.AppendLine("| type | MiB | % |").AppendLine("|---|---:|---:|");
        foreach (KeyValuePair<(string Type, bool Large), long> kv in _bytes.OrderByDescending(k => k.Value).Take(top))
        {
            sb.Append("| ").Append(kv.Key.Type).Append(kv.Key.Large ? " (LOH)" : "").Append(" | ")
              .Append((kv.Value / 1048576.0).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)).Append(" | ")
              .Append((100.0 * kv.Value / Math.Max(1, total)).ToString("F1", System.Globalization.CultureInfo.InvariantCulture)).AppendLine(" |");
        }

        return sb.ToString();
    }
}
