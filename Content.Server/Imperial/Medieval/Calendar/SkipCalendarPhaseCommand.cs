using Content.Server.Administration;
using Content.Server.Imperial.Medieval.DayNight;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server.Imperial.Medieval.Calendar;

[AdminCommand(AdminFlags.VarEdit)]
public sealed class SkipCalendarPhaseCommand : IConsoleCommand
{
    [Dependency] private readonly IEntitySystemManager _entitySystems = default!;

    public string Command => "skipcalendarphase";
    public string Description => Loc.GetString("cmd-skip-calendar-phase-desc");
    public string Help => Loc.GetString("cmd-skip-calendar-phase-help", ("command", Command));

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 0)
        {
            shell.WriteError(Help);
            return;
        }

        if (!_entitySystems.GetEntitySystem<MedievalDayNightSystem>().TrySkipPhase())
            shell.WriteError(Loc.GetString("cmd-skip-calendar-phase-unavailable"));
    }
}
