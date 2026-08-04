namespace Lantern.Core.Services;

public sealed record ServiceDefinition(
    string Id,
    string Name,
    IReadOnlyList<string> Domains);
