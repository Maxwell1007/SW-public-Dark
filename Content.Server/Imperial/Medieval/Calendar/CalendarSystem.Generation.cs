using System.Linq;
using Content.Server.Imperial.Medieval.Calendar;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared.Imperial.Medieval.Calendar;

public sealed partial class CalendarSystem
{
    private sealed record Candidate(CalendarEventPrototype Prototype, List<ProtoId<CalendarEventPrototype>> Variants);

    private void GenerateSchedule(CalendarStateComponent state, bool preserveCurrent)
    {
        state.RebuildRequested = false;
        var start = _dayNight.CurrentPhase / state.PhasesCount * state.PhasesCount;
        var preserved = preserveCurrent && state.DeckStartPhase == start
            ? Math.Min(_dayNight.CurrentPhase - start + 1, state.Deck.Count)
            : 0;
        var slots = new ProtoId<CalendarEventPrototype>?[state.PhasesCount];
        for (var i = 0; i < preserved; i++)
            slots[i] = state.Deck[i];

        var candidates = GetCandidates(state);
        var counts = candidates.ToDictionary(candidate => candidate.Prototype.ID,
            candidate => slots.Count(slot => slot?.Id == candidate.Prototype.ID));
        var remaining = candidates.ToDictionary(candidate => candidate.Prototype.ID,
            candidate => Math.Max(0, candidate.Prototype.MinOccurrences - counts[candidate.Prototype.ID]));
        if (preserved > 0)
        {
            foreach (var candidate in candidates)
            {
                var placements = new List<int>();
                for (var i = preserved; i < slots.Length; i++)
                {
                    if (!CanPlace(state, candidate.Prototype, slots, counts, start, i))
                        continue;

                    slots[i] = candidate.Prototype.ID;
                    counts[candidate.Prototype.ID]++;
                    placements.Add(i);
                }

                remaining[candidate.Prototype.ID] = Math.Min(remaining[candidate.Prototype.ID], placements.Count);
                counts[candidate.Prototype.ID] -= placements.Count;
                foreach (var i in placements)
                    slots[i] = null;
            }
        }

        var searchBudget = state.GenerationSearchLimit;
        if (!PlaceRequired(state, candidates, slots, counts, remaining, start, preserved, ref searchBudget))
        {
            Log.Error($"Cannot satisfy calendar event minimums for phases {start + 1}-{(long) start + state.PhasesCount}; remaining slots will use weighted events and fallbacks.");
        }

        for (var i = preserved; i < slots.Length; i++)
        {
            if (slots[i] != null)
                continue;

            var day = (int) (((long) start + i) / 2 + 1);
            var eligible = candidates.Where(candidate => CanPlace(state, candidate.Prototype, slots, counts, start, i) &&
                candidate.Prototype.GetWeight(day) > 0f).ToList();
            if (eligible.Count == 0)
            {
                slots[i] = (start + i) % 2 == 0 ? state.DayFallback : state.NightFallback;
                continue;
            }

            var total = eligible.Sum(candidate => (double) candidate.Prototype.GetWeight(day));
            var roll = _random.NextFloat() * total;
            var selected = eligible[^1];
            foreach (var candidate in eligible)
            {
                roll -= candidate.Prototype.GetWeight(day);
                if (roll > 0)
                    continue;

                selected = candidate;
                break;
            }

            slots[i] = selected.Prototype.ID;
            counts[selected.Prototype.ID]++;
        }

        var previousDisplay = state.DisplayDeck.ToArray();
        state.DeckStartPhase = start;
        state.Deck.Clear();
        state.DisplayDeck.Clear();
        var byId = candidates.ToDictionary(candidate => candidate.Prototype.ID);
        for (var i = 0; i < slots.Length; i++)
        {
            var id = slots[i]!.Value;
            state.Deck.Add(id);
            state.DisplayDeck.Add(i < preserved ? previousDisplay[i] :
                byId.TryGetValue(id.Id, out var candidate) && candidate.Variants.Count > 0 ? _random.Pick(candidate.Variants) : id);
        }

        UpdateDeckViews(state);
        RaiseLocalEvent(new CalendarDecksGeneratedEvent());
        if (state.ScheduleReady)
            RaiseLocalEvent(new CalendarUpdatedEvent());
    }

    private List<Candidate> GetCandidates(CalendarStateComponent state)
    {
        var candidates = new List<Candidate>();
        foreach (var proto in _prototype.EnumeratePrototypes<CalendarEventPrototype>())
        {
            if (proto.ID == state.DayFallback.Id || proto.ID == state.NightFallback.Id ||
                proto.Weight <= 0 && proto.MinOccurrences <= 0 || proto.MaxOccurrences == 0 && proto.MinOccurrences == 0 ||
                _playerManager.PlayerCount < proto.MinPlayers || _playerManager.PlayerCount > proto.MaxPlayers ||
                GetPhases(proto) == CalendarEventPhases.None || !IsAvailable(proto))
                continue;

            if (!float.IsFinite(proto.Weight) || proto.Weight < 0 || proto.MinOccurrences < 0 ||
                proto.MaxOccurrences < proto.MinOccurrences || proto.MinPhaseGap < 0 || proto.MinOffset < 1 ||
                proto.WeightCurve != null && proto.WeightCurve.Points.Any(point => !float.IsFinite(point.X) || !float.IsFinite(point.Y) || point.Y < 0))
            {
                Log.Error($"Invalid calendar event distribution settings on {proto.ID}.");
                continue;
            }

            var variants = new List<ProtoId<CalendarEventPrototype>>();
            foreach (var id in proto.Variants)
            {
                if (_prototype.TryIndex(id, out var variant) && IsAvailable(variant))
                    variants.Add(id);
            }

            if (proto.Variants.Count > 0 && variants.Count == 0)
                continue;

            candidates.Add(new Candidate(proto, variants));
        }

        return candidates;
    }

    private bool IsAvailable(CalendarEventPrototype prototype)
    {
        if (prototype.RequiredGameRules.Count > 0 && !prototype.RequiredGameRules.Any(rule => _ticker.IsGameRuleActive(rule.Id)))
            return false;

        var ev = new CalendarEventAvailabilityEvent(prototype);
        RaiseLocalEvent(ev);
        return ev.Available;
    }

    private static CalendarEventPhases GetPhases(CalendarEventPrototype prototype)
    {
        if (prototype.Phases is { } phases)
            return phases;

        var result = CalendarEventPhases.None;
        if (prototype.Tags.Contains("Day"))
            result |= CalendarEventPhases.Day;
        if (prototype.Tags.Contains("Night"))
            result |= CalendarEventPhases.Night;
        return result;
    }

    private bool PlaceRequired(CalendarStateComponent state, List<Candidate> candidates,
        ProtoId<CalendarEventPrototype>?[] slots, Dictionary<string, int> counts, Dictionary<string, int> remaining,
        int start, int preserved, ref int budget)
    {
        if (remaining.Values.Sum(value => (long) value) > slots.Count(slot => slot == null))
            return false;

        Candidate? selected = null;
        List<int>? positions = null;
        foreach (var candidate in candidates)
        {
            var needed = remaining[candidate.Prototype.ID];
            if (needed == 0)
                continue;

            var available = Enumerable.Range(preserved, slots.Length - preserved)
                .Where(index => slots[index] == null && CanPlace(state, candidate.Prototype, slots, counts, start, index)).ToList();
            if (available.Count < needed)
                return false;

            int? previous = null;
            var capacity = 0;
            foreach (var index in available)
            {
                if (previous != null && !HasRequiredDistance(candidate.Prototype, (long) start + index, (long) start + previous.Value))
                    continue;

                previous = index;
                capacity++;
            }

            if (capacity < needed)
                return false;

            if (positions != null && available.Count - needed >= positions.Count - remaining[selected!.Prototype.ID])
                continue;

            selected = candidate;
            positions = available;
        }

        if (selected == null)
            return true;
        if (--budget < 0)
            return false;

        _random.Shuffle(positions!);
        var id = selected.Prototype.ID;
        foreach (var position in positions!)
        {
            slots[position] = id;
            counts[id]++;
            remaining[id]--;
            if (PlaceRequired(state, candidates, slots, counts, remaining, start, preserved, ref budget))
                return true;

            slots[position] = null;
            counts[id]--;
            remaining[id]++;
            if (budget < 0)
                break;
        }

        return false;
    }

    private static bool CanPlace(CalendarStateComponent state, CalendarEventPrototype prototype,
        ProtoId<CalendarEventPrototype>?[] slots, Dictionary<string, int> counts, int start, int index)
    {
        var phase = (long) start + index;
        var type = phase % 2 == 0 ? CalendarEventPhases.Day : CalendarEventPhases.Night;
        if ((GetPhases(prototype) & type) == 0 || phase / 2 + 1 < prototype.MinDay ||
            counts[prototype.ID] >= prototype.MaxOccurrences)
            return false;

        if (state.LastOccurrence.TryGetValue(prototype.ID, out var previous) &&
            !HasRequiredDistance(prototype, phase, previous))
            return false;

        for (var i = 0; i < slots.Length; i++)
        {
            if (slots[i]?.Id == prototype.ID && !HasRequiredDistance(prototype, phase, (long) start + i))
                return false;
        }

        return true;
    }

    private static bool HasRequiredDistance(CalendarEventPrototype prototype, long first, long second)
    {
        return Math.Abs(first - second) > prototype.MinPhaseGap &&
            (prototype.MinOffset <= 1 || Math.Abs(first / 2 - second / 2) >= prototype.MinOffset);
    }

    private static void UpdateDeckViews(CalendarStateComponent state)
    {
        state.DayDeck.Clear();
        state.NightDeck.Clear();
        for (var i = 0; i < state.DisplayDeck.Count; i++)
        {
            var deck = (state.DeckStartPhase + i) % 2 == 0 ? state.DayDeck : state.NightDeck;
            deck.Add(state.DisplayDeck[i]);
        }
    }
}
