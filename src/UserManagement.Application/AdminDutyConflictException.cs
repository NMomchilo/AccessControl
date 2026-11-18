namespace UserManagement.Application;

public sealed class AdminDutyConflictException(string message) : Exception(message);
