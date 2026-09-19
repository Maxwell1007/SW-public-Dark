using Content.Shared.Hands.EntitySystems;
using Content.Shared.Imperial.Medieval.MagicRunes.Components;
using Content.Shared.Imperial.Medieval.MagicRunes.Data;
using Content.Shared.Random.Helpers;
using Content.Shared.Stacks;
using Content.Shared.UserInterface;

//=========================================================================
// MagicRuneSystem.UI.cs
//=========================================================================
// Purpose: User interface handling for magic scroll interactions
// Author: rhailrake
//=========================================================================

namespace Content.Shared.Imperial.Medieval.MagicRunes.Systems;

public partial class MagicRuneSystem
{
    private readonly (string Id, string Effect, int Min, int Max)[] _rewards = new[]
    {
        ("MagicMedievalLight", "SunstrikeSpellCastEffectMiddle", 7, 15),
        ("MagicMedievalFire", "FireWallSpellCastEffectMiddle", 7, 15),
        ("MagicMedievalEarth", "SpikesSpellCastEffectBeginner", 7, 15),
        ("MagicMedievalVodka", "IceDaggerSpellCastEffectBeginner", 7, 15),
        ("MagicMedievalDarkness", "TentaclesSpellCastEffectBeginner", 1, 2)
    };

    [Dependency] private readonly SharedStackSystem _stacks = default!;
    public void InitializeUI()
    {
        SubscribeLocalEvent<MagicScrollComponent, ActivatableUIOpenAttemptEvent>(UIOpenAttempt);
        SubscribeLocalEvent<MagicScrollComponent, BeforeActivatableUIOpenEvent>(BeforeUIOpen);
        SubscribeLocalEvent<MagicScrollComponent, MagicScrollRuneUnlockedMessage>(OnRuneUnlocked);
        SubscribeLocalEvent<MagicScrollComponent, MagicScrollExplosionMessage>(OnScrollExplosion);
    }

    private void UIOpenAttempt(EntityUid uid, MagicScrollComponent component, ActivatableUIOpenAttemptEvent args)
    {
        if (!HasComp<MagicRuneKnowledgeComponent>(args.User))
            args.Cancel();
    }

    private void BeforeUIOpen(EntityUid uid, MagicScrollComponent component, BeforeActivatableUIOpenEvent args)
    {
        if (!TryComp<MagicRuneKnowledgeComponent>(args.User, out var knowledge))
            return;

        SendScrollState(uid, component, knowledge, args.User);
    }

    private void OnRuneUnlocked(EntityUid uid, MagicScrollComponent component, MagicScrollRuneUnlockedMessage args)
    {
        if (!TryComp<MagicRuneKnowledgeComponent>(args.Actor, out var knowledge))
            return;

        if (!knowledge.KnownRunes.Contains(args.Rune))
            return;

        if (!component.EncryptedRunes.Contains(args.Rune) || component.DecodedRunes.Contains(args.Rune))
            return;

        component.DecodedRunes.Add(args.Rune);

        RecalculateScrollPower(uid, component);
        SendScrollState(uid, component, knowledge, args.Actor);
        Dirty(uid, component);

        GetPlayerEssence(args.Actor);
    }

    private void OnScrollExplosion(EntityUid uid, MagicScrollComponent component, MagicScrollExplosionMessage args)
    {
        _boomSystem.TriggerExplosive(uid);
    }

    private void SendScrollState(EntityUid scrollUid, MagicScrollComponent scroll, MagicRuneKnowledgeComponent knowledge, EntityUid user)
    {
        var intelligence = GetIntelligence(user);
        var state = new MagicScrollBoundUserInterfaceState(
            scrollPower: scroll.Power,
            encryptedRunes: scroll.EncryptedRunes,
            decodedRunes: scroll.DecodedRunes,
            knownRunes: knowledge.KnownRunes,
            playerIntelligence: intelligence,
            scroll.GridSize,
            scroll.TotalMines
        );

        _uiSystem.SetUiState(scrollUid, MagicScrollUiKey.Key, state);
    }

    private void GetPlayerEssence(EntityUid user)
    {
        if (_net.IsClient)
            return;

        if (_rewards.Length == 0)
            return;

        var reward = _rewards[_random.Next(_rewards.Length)];

        int count = _random.Next(reward.Min, reward.Max + 1);

        var coords = Transform(user).Coordinates;

        var essence = Spawn(reward.Id, coords);
        _stacks.SetCount(essence, count);

        Spawn(reward.Effect, coords);
    }
}

