namespace UserManagement.Application;

public sealed class UnitOrganizationConflictException(string message) : Exception(message);
