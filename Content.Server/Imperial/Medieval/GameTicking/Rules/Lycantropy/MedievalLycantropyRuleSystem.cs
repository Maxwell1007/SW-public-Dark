using System.Linq;
using Content.Server.GameTicking;
using Content.Server.GameTicking.Rules;
using Content.Server.Imperial.Medieval.DayNight;
using Content.Server.Imperial.Medieval.Lycantropy;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;
using Content.Shared.Imperial.Medieval.Calendar;
using Content.Shared.Imperial.Medieval.CCVar;
using Content.Shared.Imperial.Medieval.Lycantropy;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server.Imperial.Medieval.GameTicking.Rules;

public sealed class MedievalLycantropyRuleSystem : GameRuleSystem<MedievalLycantropyRuleComponent>
{
    [Dependency] private readonly LycantropySystem _lycantropy = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IConfigurationManager _config = default!;
    [Dependency] private readonly MedievalDayNightSystem _dayNight = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    public static bool IsBloodMoon = false;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MedievalDayNightChangedEvent>(OnTimeChanged);
        SubscribeLocalEvent<RoundEndedEvent>(OnRoundEnd);
    }

    private void OnRoundEnd(RoundEndedEvent args)
    {
        IsBloodMoon = false;
    }

    private void OnTimeChanged(MedievalDayNightChangedEvent args)
    {
        if (IsBloodMoon)
            return;

        if (args.DayType == CalendarDayType.Day)
        {
            _lycantropy.OnNightEnded();
            return;
        }

        if (args.DayNumber - 1 >= _config.GetCVar(MedievalCCVars.BloodMoonPeriod) &&
            EntityManager.AllEntities<LycantropyComponent>().Count(x => _mobState.IsAlive(x)) >=
            _config.GetCVar(MedievalCCVars.BloodMoonWerewolves))
        {
            var query = QueryActiveRules();
            while (query.MoveNext(out _, out var component, out _))
            {
                _dayNight.SetColorMultiplier(Color.Red);
                component.EndTime = _timing.CurTime + component.BloodMoonRoundEndDelay;
                IsBloodMoon = true;
            }
        }

        _lycantropy.OnNightStarted();
    }

    protected override void AppendRoundEndText(EntityUid uid, MedievalLycantropyRuleComponent component, GameRuleComponent gameRule, ref RoundEndTextAppendEvent args)
    {
        base.AppendRoundEndText(uid, component, gameRule, ref args);

        var werewolfCount = EntityManager.AllEntities<LycantropyComponent>().Count();
        if (werewolfCount == 0)
            return;

        args.AddLine(Loc.GetString("round-end-lycantropy-werewolf-count-summary",
            ("count", werewolfCount)));

        foreach (var item in EntityManager.AllEntities<LycantropyComponent>())
        {
            args.AddLine(Loc.GetString("round-end-lycantropy-werewolf-summary",
                ("name", Name(item.Owner))));
        }
    }

    protected override void ActiveTick(EntityUid uid, MedievalLycantropyRuleComponent component, GameRuleComponent gameRule, float frameTime)
    {
        base.ActiveTick(uid, component, gameRule, frameTime);

        if (component.EndTime != null && component.EndTime <= _timing.CurTime)
        {
            component.EndTime = null;
            GameTicker.EndRound();
            GameTicker.RestartRound();
            return;
        }
    }
}
