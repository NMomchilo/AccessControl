namespace UserManagement.Client;

public sealed class UnitOrganizationAccessException : Exception
{
    public UnitOrganizationAccessException() : base("Unit organization is unavailable for this request.") { }
}
