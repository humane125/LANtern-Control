using System.ComponentModel;
using Lantern.Core.Control;

namespace Lantern.App.ViewModels;

public sealed class DomainPresetRuleViewModel : INotifyPropertyChanged
{
    private string deviceName;
    private bool isExpanded;

    public DomainPresetRuleViewModel(
        string macKey,
        string deviceName,
        string presetName,
        IReadOnlyList<string> domains)
    {
        MacKey = macKey;
        this.deviceName = deviceName;
        PresetName = presetName;
        Domains = domains;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string MacKey { get; }

    public string DeviceName
    {
        get => deviceName;
        private set
        {
            if (string.Equals(deviceName, value, StringComparison.Ordinal))
            {
                return;
            }

            deviceName = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DeviceName)));
        }
    }

    public string PresetName { get; }

    public IReadOnlyList<string> Domains { get; }

    public bool IsExpanded
    {
        get => isExpanded;
        set
        {
            if (isExpanded == value)
            {
                return;
            }

            isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public string DomainCountText =>
        $"{Domains.Count} blocked domain{(Domains.Count == 1 ? string.Empty : "s")}";

    public void UpdateDeviceName(string value)
    {
        DeviceName = value;
    }
}

public sealed record DomainRulePresentation(
    IReadOnlyList<DomainPresetRuleViewModel> Presets,
    IReadOnlyList<DomainRuleViewModel> IndividualRules);

public static class DomainRulePresentationBuilder
{
    public static DomainRulePresentation Build(
        string macKey,
        string deviceName,
        IEnumerable<string> blockedDomains,
        IEnumerable<string> appliedPresetNames)
    {
        var blocked = blockedDomains.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var presets = new List<DomainPresetRuleViewModel>();
        foreach (var requestedName in appliedPresetNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var preset = DomainBlockPresetCatalog.All.FirstOrDefault(candidate =>
                candidate.Name.Equals(requestedName, StringComparison.OrdinalIgnoreCase));
            if (preset is null)
            {
                continue;
            }

            var domains = preset.Domains
                .Where(blocked.Contains)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (domains.Length == 0)
            {
                continue;
            }

            presets.Add(new DomainPresetRuleViewModel(
                macKey,
                deviceName,
                preset.Name,
                domains));
            claimed.UnionWith(domains);
        }

        var individualRules = blocked
            .Where(domain => !claimed.Contains(domain))
            .Order(StringComparer.OrdinalIgnoreCase)
            .Select(domain => new DomainRuleViewModel(macKey, deviceName, domain))
            .ToArray();
        return new DomainRulePresentation(presets, individualRules);
    }
}
