using Content.Client.Examine;
using Content.Shared.Examine;
using Content.Shared.Verbs;
using Robust.Client.Player;
using Robust.Shared.Utility;

namespace Content.Client.Imperial.Medieval.Trading;

public sealed class TradingExamineSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly ExamineSystem _examine = default!;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(ExamineSystem));
        SubscribeLocalEvent<ClientExaminedEvent>(OnClientExamined);
    }

    public void Open(
        EntityUid pit,
        EntityUid target,
        FormattedMessage message,
        List<Verb> verbs,
        Guid? commodityId,
        Action<ExamineVerb> executeVerb)
    {
        if (_player.LocalEntity is not { } player ||
            !Exists(target) ||
            !TryComp<TradingExamineComponent>(player, out var state) ||
            state.Pit != pit ||
            state.Target != target ||
            state.CommodityId != commodityId)
        {
            return;
        }

        foreach (var verb in verbs)
        {
            if (verb is not ExamineVerb examineVerb)
                continue;

            examineVerb.ClientExclusive = true;
            examineVerb.Act = () => executeVerb(examineVerb);
        }

        _examine.UpdateTooltipInfo(player, target, message, verbs, false);
    }

    public void Begin(EntityUid pit, EntityUid target, Guid? commodityId = null)
    {
        if (_player.LocalEntity is not { } player || !Exists(target))
            return;

        var state = EnsureComp<TradingExamineComponent>(player);
        state.Pit = pit;
        state.Target = target;
        state.CommodityId = commodityId;
        _examine.OpenTooltip(player, target, true, false);
    }

    public void Close(EntityUid pit)
    {
        if (_player.LocalEntity is not { } player ||
            !TryComp<TradingExamineComponent>(player, out var state) ||
            state.Pit != pit)
        {
            return;
        }

        RestoreChecks(player, state);
        RemComp<TradingExamineComponent>(player);
    }

    public override void Update(float frameTime)
    {
        if (_player.LocalEntity is not { } player ||
            !TryComp<TradingExamineComponent>(player, out var state))
        {
            return;
        }

        if (!Exists(state.Target))
        {
            Close(state.Pit);
            return;
        }

        if (state.RestorePending || !TryComp<ExaminerComponent>(player, out var examiner))
            return;

        state.PreviousSkipChecks = examiner.SkipChecks;
        state.RestorePending = true;
        examiner.SkipChecks = true;
    }

    public void RestoreChecks()
    {
        if (_player.LocalEntity is not { } player ||
            !TryComp<TradingExamineComponent>(player, out var state))
        {
            return;
        }

        RestoreChecks(player, state);
    }

    private void RestoreChecks(EntityUid player, TradingExamineComponent state)
    {
        if (!state.RestorePending)
            return;

        if (TryComp<ExaminerComponent>(player, out var examiner))
            examiner.SkipChecks = state.PreviousSkipChecks;

        state.RestorePending = false;
    }

    private void OnClientExamined(ClientExaminedEvent args)
    {
        if (!TryComp<TradingExamineComponent>(args.Examiner, out var state) ||
            state.Target == args.Examined)
        {
            return;
        }

        RestoreChecks(args.Examiner, state);
        RemComp<TradingExamineComponent>(args.Examiner);
    }
}

public sealed class TradingExamineRestoreSystem : EntitySystem
{
    [Dependency] private readonly TradingExamineSystem _tradingExamine = default!;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesAfter.Add(typeof(ExamineSystem));
        UpdatesAfter.Add(typeof(TradingExamineSystem));
    }

    public override void Update(float frameTime)
    {
        _tradingExamine.RestoreChecks();
    }
}
