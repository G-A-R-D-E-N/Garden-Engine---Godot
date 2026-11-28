using System.Collections.Generic;
using System.Linq;

namespace OpenCommonwealth.Services.Ui.Service;

public sealed class InMemoryProfileStore : IProfileStore
{
    public const string DefaultId = "default";

    private readonly List<ProfileInfo> _live = new() { new ProfileInfo(DefaultId, "Default", true) };
    private readonly List<ProfileInfo> _deleted = new();
    private int _next = 1;

    public string ActiveId { get; private set; } = DefaultId;

    public bool FailWrites { get; set; }

    public List<string> ChosenIds { get; } = new();

    public IReadOnlyList<ProfileInfo> List() => _live.ToList();

    public IReadOnlyList<ProfileInfo> Deleted() => _deleted.ToList();

    public ProfileResult Create(string name)
    {
        if (FailWrites) return new ProfileResult(false, WriteFailed);
        if (ProfileNames.Check(name) is { } error) return new ProfileResult(false, error);
        var profile = new ProfileInfo("p" + _next++, ProfileNames.Normalize(name));
        _live.Add(profile);
        return new ProfileResult(true, "Created.", profile);
    }

    public ServiceResult Rename(string id, string name)
    {
        if (FailWrites) return ServiceResult.Fail(WriteFailed);
        if (ProfileNames.Check(name) is { } error) return ServiceResult.Fail(error);
        var index = _live.FindIndex(p => p.Id == id);
        if (index < 0) return ServiceResult.Fail("That profile no longer exists.");
        _live[index] = _live[index] with { Name = ProfileNames.Normalize(name) };
        return ServiceResult.Success();
    }

    public ServiceResult Delete(string id)
    {
        if (FailWrites) return ServiceResult.Fail(WriteFailed);
        var profile = _live.FirstOrDefault(p => p.Id == id);
        if (profile is null) return ServiceResult.Fail("That profile no longer exists.");
        if (profile.IsDefault || id == ActiveId) return ServiceResult.Fail("The default and active profiles cannot be deleted.");
        _live.Remove(profile);
        _deleted.Add(profile);
        return ServiceResult.Success();
    }

    public ServiceResult Restore(string id)
    {
        if (FailWrites) return ServiceResult.Fail(WriteFailed);
        var profile = _deleted.FirstOrDefault(p => p.Id == id);
        if (profile is null) return ServiceResult.Fail("That profile is not in Deleted.");
        _deleted.Remove(profile);
        _live.Add(profile);
        return ServiceResult.Success();
    }

    public ServiceResult Choose(string id)
    {
        if (FailWrites) return ServiceResult.Fail(WriteFailed);
        if (_live.All(p => p.Id != id)) return ServiceResult.Fail("That profile no longer exists.");
        ActiveId = id;
        ChosenIds.Add(id);
        return ServiceResult.Success();
    }

    private const string WriteFailed = "The profile data could not be written.";
}
