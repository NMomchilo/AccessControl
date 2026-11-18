namespace Common.Persistence;

public static class CurrentActor
{
    public static string Require(ICurrentActorProvider? provider)
    {
        string? actorId = provider?.ActorId;
        if (string.IsNullOrWhiteSpace(actorId) || actorId.Length > 200)
            throw new InvalidOperationException("A validated actor ID is required for database writes.");
        return actorId;
    }
}
