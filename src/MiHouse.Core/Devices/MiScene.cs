namespace MiHouse.Core.Devices;

/// <summary>
/// A manual scene defined in Mi Home.
/// </summary>
/// <param name="Id">Scene id used when running it.</param>
/// <param name="Name">User-visible scene name.</param>
/// <param name="Enabled">Whether the scene is enabled in Mi Home.</param>
/// <param name="IconUrl">Icon reported by the cloud, when present.</param>
public sealed record MiScene(string Id, string Name, bool Enabled, string? IconUrl);
