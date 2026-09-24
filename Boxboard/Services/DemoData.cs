using Bevdox.Models;
using Boxboard.Models;

namespace Boxboard.Services;

public static class DemoData
{
    public static List<DevBoxInstance> Machines() =>
    [
        Machine("azdo1", "Running"), Machine("azdo2", "Hibernated"),
        Machine("azdo3", "Stopped"), Machine("aitestagent", "Running")
    ];

    private static DevBoxInstance Machine(string name, string state) => new()
    {
        UniqueId = $"https://demo.invalid/projects/demo/users/demo/devboxes/{name}",
        OriginalName = name,
        ProjectName = "Demo project",
        DevCenterUri = "https://demo.invalid",
        State = state,
        Location = "Demo region"
    };

    public static BoardSettings Settings() => new()
    {
        NextSlotNumber = 4,
        Machines = [.. Machines(), Machine("offline-box", "Unknown")],
        Slots =
        [
            new(Guid.Parse("00000000-0000-0000-0000-000000000001"), 1, Machines()[0].UniqueId),
            new(Guid.Parse("00000000-0000-0000-0000-000000000002"), 2, Machines()[1].UniqueId),
            new(Guid.Parse("00000000-0000-0000-0000-000000000003"), 3, Machine("offline-box", "Unknown").UniqueId)
        ]
    };
}
