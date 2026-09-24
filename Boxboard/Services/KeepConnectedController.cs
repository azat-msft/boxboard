using Bevdox.Models;
using Boxboard.Models;

namespace Boxboard.Services;

public sealed class KeepConnectedController(
    ISessionWindows windows, SessionCoordinator sessions, TimeProvider? clock = null) : IDisposable
{
    private sealed class SlotState
    {
        public required string MachineId { get; init; }
        public DateTimeOffset? MissingSince { get; set; }
        public DateTimeOffset NextAttempt { get; set; }
        public int Attempts { get; set; }
        public CancellationTokenSource? Request { get; set; }
    }

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Dictionary<Guid, SlotState> _states = [];

    public void Reset()
    {
        foreach (var state in _states.Values)
            state.Request?.Cancel();
        _states.Clear();
    }

    public async Task TickAsync(IReadOnlyList<(SlotAssignment Slot, DevBoxInstance Machine)> assignments,
        IReadOnlyList<DevBoxInstance> catalog, bool enabled, CancellationToken ct = default)
    {
        if (!enabled)
        {
            Reset();
            return;
        }
        var environment = windows.GetEnvironment();
        if (!environment.CanInteract)
            return;
        sessions.Observe();
        var assignedSlots = assignments.Select(item => item.Slot.Id).ToHashSet();
        foreach (var removed in _states.Keys.Where(id => !assignedSlots.Contains(id)).ToList())
        {
            _states[removed].Request?.Cancel();
            _states.Remove(removed);
        }
        foreach (var (slot, machine) in assignments)
        {
            ct.ThrowIfCancellationRequested();
            if (!BoardSettings.SameId(slot.MachineId, machine.UniqueId))
                throw new InvalidOperationException($"{slot.Name} no longer identifies {machine.EffectiveName}.");
            if (_states.TryGetValue(slot.Id, out var previous) &&
                !BoardSettings.SameId(previous.MachineId, machine.UniqueId))
            {
                previous.Request?.Cancel();
                _states.Remove(slot.Id);
            }
            var session = sessions.For(slot);
            if (catalog.Count(item => string.Equals(item.OriginalName, machine.OriginalName,
                StringComparison.OrdinalIgnoreCase)) != 1)
            {
                session.Status = "Keep connected paused: this Dev Box name is ambiguous in the catalog.";
                continue;
            }
            var matches = sessions.Candidates(machine);
            if (matches.Count > 0)
            {
                if (_states.TryGetValue(slot.Id, out var observed))
                    observed.MissingSince = null;
                if (matches.Count > 1)
                    session.Status = "Keep connected paused: multiple matching client windows. Resolve them manually.";
                else if (matches[0].DesktopId != environment.DesktopId)
                    session.Status = "Client is on another desktop. Use Apply layout to move it; no duplicate was launched.";
                else if (!session.Connecting && session.BoundWindow != matches[0].Identity &&
                    !sessions.ReconnectPromptCandidates.Any(candidate => candidate.Identity == matches[0].Identity))
                    sessions.Bind(slot, machine, matches[0].Identity, preservePosition: false);
                continue;
            }
            var now = _clock.GetUtcNow();
            if (!_states.TryGetValue(slot.Id, out var state))
                _states.Add(slot.Id, state = new SlotState { MachineId = machine.UniqueId });
            state.MissingSince ??= now;
            if (session.Connecting)
                continue;
            if (state.Attempts >= 3)
            {
                session.Status = "Keep connected paused after three attempts without a client window. " +
                    "Use Apply layout or toggle Keep connected to retry.";
                continue;
            }
            if (now < state.MissingSince.Value.AddSeconds(3) || now < state.NextAttempt)
                continue;
            state.Attempts++;
            state.NextAttempt = now.AddSeconds(state.Attempts switch
            {
                1 => 5,
                2 => 30,
                _ => 120
            });
            session.Status = $"Keep connected: requesting {machine.EffectiveName} " +
                $"(attempt {state.Attempts}/3). Normal Windows App sign-in may be required.";
            var request = CancellationTokenSource.CreateLinkedTokenSource(ct);
            state.Request = request;
            try
            {
                await sessions.ConnectAsync(slot, machine, nameIsUnique: true, request.Token);
            }
            catch (OperationCanceledException) when (request.IsCancellationRequested) { return; }
            finally
            {
                if (ReferenceEquals(state.Request, request))
                    state.Request = null;
                request.Dispose();
            }
        }
    }

    public void Dispose() => Reset();
}
