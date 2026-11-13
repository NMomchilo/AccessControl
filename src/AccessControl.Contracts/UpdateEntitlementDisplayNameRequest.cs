using System.Text.Json.Serialization;

namespace AccessControl.Contracts;

public sealed record UpdateEntitlementDisplayNameRequest([property: JsonRequired] string DisplayName);
