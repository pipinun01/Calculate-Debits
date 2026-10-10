using Debits.Domain;

namespace Debits.Application.Common;

/// <summary>Entity does not exist in the current tenant → HTTP 404.</summary>
public sealed class NotFoundException(string code, string message) : DomainException(code, message);

/// <summary>The acting member is not allowed to do this → HTTP 403.</summary>
public sealed class ForbiddenException(string code, string message) : DomainException(code, message);

/// <summary>State conflict (duplicate, last owner, …) → HTTP 409.</summary>
public sealed class ConflictException(string code, string message) : DomainException(code, message);
