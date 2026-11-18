namespace AccessControl.Service.Security;

public sealed class AdminLookupUnavailableException(string message) : Exception(message);
