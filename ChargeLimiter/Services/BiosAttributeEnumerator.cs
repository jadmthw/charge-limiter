using System.Management;
using System.Runtime.Versioning;
using ChargeLimiter.Models;

namespace ChargeLimiter.Services;

public interface IBiosAttributeEnumerator
{
    Task<IReadOnlyList<BiosAttributeRow>> EnumerateAsync(CancellationToken cancellationToken = default);
}

[SupportedOSPlatform("windows")]
public sealed class DellBiosAttributeEnumerator : IBiosAttributeEnumerator
{
    private static readonly string[] ChargeKeywords =
    [
        "batt", "charge", "ac", "adapter", "power", "smart", "peak", "express", "prim"
    ];

    public Task<IReadOnlyList<BiosAttributeRow>> EnumerateAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() =>
        {
            var rows = new List<BiosAttributeRow>();
            void Ingest(string className)
            {
                try
                {
                    using var searcher = new ManagementObjectSearcher(
                        DellWmiChargeLimitBackend.Namespace,
                        $"SELECT AttributeName, CurrentValue FROM {className}");
                    foreach (ManagementBaseObject obj in searcher.Get())
                    {
                        var name = obj["AttributeName"]?.ToString() ?? "(unnamed)";
                        var current = obj["CurrentValue"]?.ToString();
                        if (current is null && obj["CurrentValue"] is Array arr)
                        {
                            current = string.Join(",", arr.Cast<object?>().Select(x => x?.ToString()));
                        }

                        var looks = ChargeKeywords.Any(k =>
                            name.Contains(k, StringComparison.OrdinalIgnoreCase));
                        rows.Add(new BiosAttributeRow
                        {
                            Name = name,
                            CurrentValue = current,
                            LooksChargeRelated = looks
                        });
                    }
                }
                catch
                {
                    // optional class
                }
            }

            Ingest("DCIM_BIOSEnumeration");
            Ingest("DCIM_BIOSInteger");
            Ingest("DCIM_BIOSString");
            return (IReadOnlyList<BiosAttributeRow>)rows
                .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderByDescending(r => r.LooksChargeRelated)
                .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }, cancellationToken);
}

public sealed class EmptyBiosAttributeEnumerator : IBiosAttributeEnumerator
{
    public Task<IReadOnlyList<BiosAttributeRow>> EnumerateAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BiosAttributeRow>>(Array.Empty<BiosAttributeRow>());
}
