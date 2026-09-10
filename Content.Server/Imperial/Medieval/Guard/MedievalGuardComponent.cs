using System.Threading;

namespace Content.Server.Imperial.Medieval.Guard;

[RegisterComponent]
public sealed partial class MedievalGuardComponent : Component
{
    [DataField]
    public EntityUid? GuardedEntity;

    [DataField]
    public float FollowDistance = 3f;

    [DataField]
    public float MaxDistance = 60f;

    [DataField]
    public TimeSpan ThinkInterval = TimeSpan.FromSeconds(0.25);

    [DataField]
    public float ObstacleRadius = 0.25f;

    [ViewVariables]
    public EntityUid? AttackTarget;

    [ViewVariables]
    public MedievalGuardState State;

    public CancellationTokenSource? ThinkCancellation;

    public readonly HashSet<EntityUid> Obstacles = new();
}

public enum MedievalGuardState : byte
{
    Idle,
    Follow,
    Attack,
}

[ByRefEvent]
public record struct MedievalGuardBehaviorEvent(MedievalGuardState State, EntityUid? Target, bool Handled = false);
