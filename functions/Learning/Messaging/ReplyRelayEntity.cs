using Microsoft.DurableTask.Entities;

namespace LineLanguageLearn.Learning.Messaging;

public sealed record ReplyTarget(string WebhookEventId, string Token);
public sealed record ReplyRelayRegistration(string InstanceId, ReplyTarget Target, DateTime Now);
public sealed record ReplyRelayAdmission(bool StartTurn, ReplyTarget? WaitingTarget);
public sealed record ReplyRelayState(string OwnerInstanceId, ReplyTarget LatestTarget, DateTime LeaseExpiresAt);

/// <summary>Tracks the active tutor turn and its latest reply token for one LINE user.</summary>
public sealed class ReplyRelayEntity : TaskEntity<ReplyRelayState?>
{
    protected override ReplyRelayState? InitializeState(TaskEntityOperation entityOperation) => null;

    public ReplyRelayAdmission Register(ReplyRelayRegistration registration)
    {
        var active = State;
        // Reclaim a relay whose owner was terminated without completing it.
        if (active is null || active.LeaseExpiresAt <= registration.Now)
        {
            State = new(registration.InstanceId, registration.Target, registration.Now.AddMinutes(5));
            return new(true, null);
        }

        if (active.OwnerInstanceId == registration.InstanceId || active.LatestTarget.WebhookEventId == registration.Target.WebhookEventId)
            return new(false, null);

        State = active with { LatestTarget = registration.Target };
        return new(false, active.LatestTarget);
    }

    public ReplyTarget? Complete(string ownerInstanceId)
    {
        if (State is not { } active || active.OwnerInstanceId != ownerInstanceId) return null;
        State = null;
        return active.LatestTarget;
    }
}
