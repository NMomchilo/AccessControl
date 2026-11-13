using System.Text.Json.Serialization;

namespace UserManagement.Contracts;

public sealed record SetActiveRequest([property: JsonRequired] bool IsActive);
