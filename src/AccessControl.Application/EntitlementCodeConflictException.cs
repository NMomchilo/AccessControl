namespace AccessControl.Application;

public sealed class EntitlementCodeConflictException() : InvalidOperationException("Entitlement code already exists in this unit organization.");
