namespace Common.Persistence;

public interface ICurrentActorProvider
{
    string? ActorId { get; }
}
