namespace UserManagement.Client;

public sealed class UserNotProvisionedException : Exception
{
    public UserNotProvisionedException() : base("User or unit organization membership has not been provisioned.") { }
}
