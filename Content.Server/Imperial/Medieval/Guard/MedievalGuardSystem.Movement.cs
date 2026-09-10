namespace Content.Server.Imperial.Medieval.Guard;

public sealed partial class MedievalGuardSystem
{
    private void MoveTowards(EntityUid uid, MedievalGuardComponent component, EntityUid target, float stopDistance)
    {
        _navigation.Navigate(uid, target, stopDistance);
    }

    private void StopMoving(EntityUid uid)
    {
        _navigation.Stop(uid);
    }
}
