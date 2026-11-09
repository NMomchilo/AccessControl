namespace AccessControl.Contracts;

public sealed record DecisionResponse(bool Permit, string Reason, bool AuthenticationRequired = false);
