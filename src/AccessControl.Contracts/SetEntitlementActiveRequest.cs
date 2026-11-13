using System.Text.Json.Serialization;

namespace AccessControl.Contracts;

public sealed record SetEntitlementActiveRequest([property: JsonRequired] bool IsActive);
