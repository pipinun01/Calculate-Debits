using Debits.Domain;

namespace Debits.Application.Tenancy;

public sealed class TenantMismatchException(string message)
    : DomainException("tenant.mismatch", message);
