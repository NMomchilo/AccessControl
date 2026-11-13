namespace AccessControl.Application;

public sealed class EntitlementConflictException(string message) : Exception(message);
