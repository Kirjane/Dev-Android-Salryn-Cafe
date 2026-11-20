namespace Salryn.Security.Pairing;

/// <summary>
/// Enforces strict Salryn.Cafe product isolation and tenant/store binding.
/// Rejects retail/General Merchandise or cross-tenant requests fail-closed.
/// </summary>
public static class ProductScopeGuard
{
    public static PairingResult ValidateProductAndTenant(TenantScope? scope)
    {
        if (scope is null || !string.Equals(scope.ProductId, PairingConstants.CanonicalProductId, StringComparison.Ordinal))
        {
            return PairingResult.Fail(
                PairingErrorCodes.ProductScopeMismatch,
                $"ProductId must be '{PairingConstants.CanonicalProductId}'. Cross-product requests are rejected.",
                httpStatusCode: 403);
        }

        if (scope.TenantId == Guid.Empty)
        {
            return PairingResult.Fail(
                PairingErrorCodes.TenantScopeMismatch,
                "A non-empty TenantId is required.",
                httpStatusCode: 403);
        }

        if (scope.StoreId == Guid.Empty)
        {
            return PairingResult.Fail(
                PairingErrorCodes.StoreScopeMismatch,
                "A non-empty StoreId is required.",
                httpStatusCode: 403);
        }

        return PairingResult.Success();
    }

    public static PairingResult ValidateExactMatch(TenantScope? actual, TenantScope expected)
    {
        ArgumentNullException.ThrowIfNull(expected);

        PairingResult baseCheck = ValidateProductAndTenant(actual);
        if (baseCheck.IsFailure)
        {
            return baseCheck;
        }

        if (!string.Equals(actual!.ProductId, expected.ProductId, StringComparison.Ordinal))
        {
            return PairingResult.Fail(
                PairingErrorCodes.ProductScopeMismatch,
                "Product scope mismatch.",
                httpStatusCode: 403);
        }

        if (actual.TenantId != expected.TenantId)
        {
            return PairingResult.Fail(
                PairingErrorCodes.TenantScopeMismatch,
                "Tenant scope mismatch.",
                httpStatusCode: 403);
        }

        if (actual.StoreId != expected.StoreId)
        {
            return PairingResult.Fail(
                PairingErrorCodes.StoreScopeMismatch,
                "Store scope mismatch.",
                httpStatusCode: 403);
        }

        return PairingResult.Success();
    }
}
