using AccessControl.Contracts;

namespace AccessControl.Client;

public interface IAccessControlDecisionClient
{
    Task<DecisionResponse> DecideAsync(DecisionRequest request, CancellationToken cancellationToken);
}
