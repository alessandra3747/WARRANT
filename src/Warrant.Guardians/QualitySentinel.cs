using Warrant.Application;
using Warrant.Domain;

namespace Warrant.Guardians;

public sealed class QualitySentinel : IGuardian
{
    public string Name => "QualitySentinel";

    public Task<IReadOnlyList<DomainEvent>> InspectAsync(AssetContext ctx, CancellationToken ct = default)
    {
        var rows = ctx.SampleRows;
        var findings = new List<Finding>();
        double completeness;
        int dup;
        string? dupKey;

        if (ctx.Stats is { Exact: true } s)
        {
            completeness = WeightedCompleteness(s, ctx.Fields);
            dupKey = s.DupKeyField;
            dup = s.DupRedundantRows;

            if (s.RowCount == 0)
            {
                findings.Add(new("Incomplete", null, "Table is empty", "Medium"));
            }
            else if (completeness < 0.5)
            {
                findings.Add(new("Incomplete", null, $"Low weighted completeness ({completeness:P0}) across {s.RowCount} rows", "Medium"));
            }

            foreach (var f in ctx.Fields.Where(f => f.IsPriority))
            {
                var fillRate = s.RowCount == 0 ? 0 : (double)(s.FilledByField.TryGetValue(f.LogicalName, out var v) ? v : 0) / s.RowCount;
                
                if (fillRate < 0.8)
                {
                    findings.Add(new("Incomplete", f.LogicalName, $"Priority field {(1-fillRate):P0} empty across {s.RowCount} rows", fillRate < 0.2 ? "High" : "Medium"));
                }
            }

            if (dup > 0 && dupKey != null)
            {
                findings.Add(new("ExactDuplicate", dupKey, $"{dup} duplicate row(s) on key '{dupKey}' across {s.RowCount} rows", "Medium"));
            }
        }
        else
        {
            int cells = rows.Count * Math.Max(ctx.Fields.Count, 1);
            int filled = rows.Sum(r => r.Count(kv => kv.Value != null && kv.Value.ToString()!.Length > 0));
            completeness = cells == 0 ? 0 : (double)filled / cells;

            if (rows.Count == 0)
            {
                findings.Add(new("Incomplete", null, "No sample rows available", "Medium"));
            }
            else if (completeness < 0.5)
            {
                findings.Add(new("Incomplete", null, $"Low completeness ({completeness:P0}) on sample (table too large for exact scan)", "Medium"));
            }

            dupKey = GuardianHeuristics.PickDupKey(ctx.Fields);
            dup = 0;
            if (dupKey != null)
            {
                dup = rows
                    .Select(r => r.TryGetValue(dupKey, out var v) ? v?.ToString()?.Trim().ToLowerInvariant() : null)
                    .Where(x => !string.IsNullOrEmpty(x))
                    .GroupBy(x => x)
                    .Count(g => g.Count() > 1);
                if (dup > 0)
                {
                    findings.Add(new("ExactDuplicate", dupKey, $"{dup} duplicate key value(s) on '{dupKey}' (sample)", "Medium"));
                }
            }
        }

        double fresh = (DateTimeOffset.UtcNow - ctx.LastModified).TotalHours;

        foreach (var f in ctx.Fields.Where(f => f.IsPii || f.IsPriority))
        {
            foreach (var r in rows)
            {
                if (!r.TryGetValue(f.LogicalName, out var raw) || raw == null)
                {
                    continue;
                }
                var val = raw.ToString() ?? "";
                if (f.LogicalName.Contains("email") && val.Length > 0 && !val.Contains('@'))
                { 
                    findings.Add(new("FormatViolation", f.LogicalName, "Value does not look like an email", "Low")); 
                    break; 
                }
            }
        }

        IReadOnlyList<DomainEvent> ev = [new QualityAssessed(ctx.StreamId, DateTimeOffset.UtcNow, completeness, dup, fresh, FindingSanitizer.Clean(findings))];
        return Task.FromResult(ev);
    }

    private static double WeightedCompleteness(TableStats s, IReadOnlyList<FieldMeta> fields)
    {
        if (s.RowCount == 0 || fields.Count == 0)
        {
            return 0;
        }
        double acc = 0, wsum = 0;
        foreach (var f in fields)
        {
            double rate = (double)(s.FilledByField.TryGetValue(f.LogicalName, out var v) ? v : 0) / s.RowCount;
            double w = f.IsPriority ? 2.0 : 1.0;
            acc += w * rate; wsum += w;
        }
        return wsum == 0 ? 0 : acc / wsum;
    }
}