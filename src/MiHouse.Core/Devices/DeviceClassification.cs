using MiHouse.Core.Session;

namespace MiHouse.Core.Devices;

/// <summary>
/// Devices with switch classification applied, plus whether every source answered.
/// </summary>
/// <param name="Devices">The input devices with switch classification applied.</param>
/// <param name="IsComplete">
/// True when the subclass catalog and every membership request needed for these devices
/// succeeded. An incomplete result keeps previously cached categories instead of dropping them.
/// </param>
public sealed record DeviceClassificationResult(IReadOnlyList<MiDevice> Devices, bool IsComplete);

/// <summary>
/// Resolves the precise category of switch channels from the cloud's switch subclass catalog,
/// so a channel that is really a light is no longer grouped as a plain switch.
/// </summary>
public sealed class DeviceClassification
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<int, int>> _noMemberships =
        new Dictionary<string, IReadOnlyDictionary<int, int>>();

    private readonly IMiHomeCloudClient _cloud;
    private readonly SwitchSubclassCatalog _catalog;

    /// <summary>
    /// Creates a classifier over the switch subclass sources.
    /// </summary>
    public DeviceClassification(IMiHomeCloudClient cloud, SwitchSubclassCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(cloud);
        ArgumentNullException.ThrowIfNull(catalog);

        _cloud = cloud;
        _catalog = catalog;
    }

    /// <summary>
    /// Returns <paramref name="devices"/> with <see cref="MiDevice.SubclassId"/> and
    /// <see cref="MiDevice.SubclassCategory"/> filled in for switches. Classification is
    /// decoration: when the catalog or the membership request fails, devices keep the category
    /// they had in <paramref name="previousDevices"/> and the result reports itself incomplete.
    /// </summary>
    public async Task<DeviceClassificationResult> EnrichAsync(
        MiHomeSession session,
        IReadOnlyList<MiDevice> devices,
        IReadOnlyList<MiDevice> previousDevices,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(devices);
        ArgumentNullException.ThrowIfNull(previousDevices);

        if (!devices.Any(IsSwitch))
        {
            return new DeviceClassificationResult(devices, IsComplete: true);
        }

        IReadOnlyDictionary<int, SwitchSubclass> catalog;
        try
        {
            catalog = await _catalog.GetAsync(session, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            // Without the catalog no subclass id can become a category, but the previous list
            // may already know the category; keep it rather than dropping it.
            return new DeviceClassificationResult(
                RestoreCategories(devices, IndexByDid(previousDevices)),
                IsComplete: false);
        }

        if (catalog.Count == 0)
        {
            // The catalog is global and never legitimately empty while devices exist. An empty
            // one means the last fetch failed with nothing cached, so nothing can be resolved.
            return new DeviceClassificationResult(
                RestoreCategories(devices, IndexByDid(previousDevices)),
                IsComplete: false);
        }

        // Only split channels need the parent's sub-relation; a standalone switch reports its
        // own subclass id in the device list, so a membership failure must not affect it.
        string[] parentDids = devices
            .Where(device => IsSwitch(device) && !string.IsNullOrEmpty(device.ParentDid))
            .Select(device => device.ParentDid!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        bool isComplete = true;
        IReadOnlyDictionary<string, IReadOnlyDictionary<int, int>> memberships = _noMemberships;
        if (parentDids.Length > 0)
        {
            try
            {
                memberships = await _cloud
                    .GetSwitchMembershipsAsync(session, parentDids, cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                // Standalone switches still classify from their own top-level id.
                isComplete = false;
            }
        }

        Dictionary<string, MiDevice>? previous = isComplete
            ? null
            : IndexByDid(previousDevices);

        var enriched = new List<MiDevice>(devices.Count);
        foreach (MiDevice device in devices)
        {
            if (!IsSwitch(device))
            {
                enriched.Add(device);
                continue;
            }

            MiDevice applied = Apply(device, ResolveSubclassId(device, memberships), catalog);
            enriched.Add(applied.SubclassCategory is null && previous is not null
                ? RestoreCategory(applied, previous)
                : applied);
        }

        return new DeviceClassificationResult(enriched, isComplete);
    }

    /// <summary>
    /// Returns the key the parent's <c>member_ship</c> uses for <paramref name="device"/>:
    /// the channel index for a split sub-device, and <c>1</c> for a standalone switch.
    /// </summary>
    public static int MemberIndex(MiDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        return device.ChannelSiid is > 0 ? device.ChannelSiid.Value - 1 : 1;
    }

    /// <summary>
    /// Returns <paramref name="device"/> with its subclass resolved against
    /// <paramref name="catalog"/>. An id the catalog does not know leaves the category unset.
    /// </summary>
    public static MiDevice Apply(
        MiDevice device,
        int? subclassId,
        IReadOnlyDictionary<int, SwitchSubclass> catalog)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(catalog);

        string? category = null;
        if (subclassId is { } id && catalog.TryGetValue(id, out SwitchSubclass? entry))
        {
            category = DeviceClass.FromSubclassCategory(entry.CategoryId);
        }

        return device with { SubclassId = subclassId, SubclassCategory = category };
    }

    private static bool IsSwitch(MiDevice device)
        => string.Equals(DeviceClass.FromSpecType(device.SpecType), "switch", StringComparison.Ordinal);

    private static int? ResolveSubclassId(
        MiDevice device,
        IReadOnlyDictionary<string, IReadOnlyDictionary<int, int>> memberships)
    {
        if (device.SubclassId is { } own)
        {
            return own;
        }

        // A split channel is the only device whose subclass id comes from the parent's
        // member_ship; a standalone switch without its own id stays unclassified.
        if (string.IsNullOrEmpty(device.ParentDid))
        {
            return null;
        }

        return memberships.TryGetValue(device.ParentDid, out IReadOnlyDictionary<int, int>? channels)
            && channels.TryGetValue(MemberIndex(device), out int subclassId)
            ? subclassId
            : null;
    }

    private static Dictionary<string, MiDevice> IndexByDid(IReadOnlyList<MiDevice> devices)
    {
        var index = new Dictionary<string, MiDevice>(StringComparer.Ordinal);
        foreach (MiDevice device in devices)
        {
            index.TryAdd(device.Did, device);
        }

        return index;
    }

    private static List<MiDevice> RestoreCategories(
        IReadOnlyList<MiDevice> devices,
        IReadOnlyDictionary<string, MiDevice> previous)
    {
        var restored = new List<MiDevice>(devices.Count);
        foreach (MiDevice device in devices)
        {
            restored.Add(IsSwitch(device) && device.SubclassCategory is null
                ? RestoreCategory(device, previous)
                : device);
        }

        return restored;
    }

    private static MiDevice RestoreCategory(MiDevice device, IReadOnlyDictionary<string, MiDevice> previous)
    {
        if (!previous.TryGetValue(device.Did, out MiDevice? cached)
            || cached.SubclassCategory is null
            || !string.Equals(cached.ParentDid, device.ParentDid, StringComparison.Ordinal)
            || cached.ChannelSiid != device.ChannelSiid
            || !string.Equals(cached.SpecType, device.SpecType, StringComparison.Ordinal)
            || !HasSameTopLevelSubclass(device, cached))
        {
            return device;
        }

        return device with { SubclassCategory = cached.SubclassCategory };
    }

    /// <summary>
    /// Compares the subclass id the device list reports itself. A split channel never reports
    /// one at the top level, so its identity is settled by the parent and channel instead; the
    /// id cached for it may be the resolved member id, which must not block retention.
    /// </summary>
    private static bool HasSameTopLevelSubclass(MiDevice device, MiDevice previous)
        => !string.IsNullOrEmpty(device.ParentDid) || device.SubclassId == previous.SubclassId;
}
