using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Salryn.Api.Local.Services;
using Salryn.Composition.Local;

namespace Salryn.Api.Local;

/// <summary>
/// Registers all Salryn Cafe C1-C11 domain endpoints with unified fail-closed mutation idempotency
/// (<see cref="CafeMutationIdempotencyEndpointFilter"/>). Excludes retail-only ProcureToPay and
/// CustomerOrdersAndQuotations endpoints from the Cafe runtime surface.
/// </summary>
public static class LocalCafeDomainEndpoints
{
    public static IServiceCollection AddLocalCafeDomainServices(
        this IServiceCollection services,
        SalrynLocalComposition composition)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(composition);
        services.AddSingleton(new CafeMutationIdempotencyGuard(composition.ConnectionFactory));
        return services;
    }

    public static WebApplication MapLocalCafeDomainEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        RouteGroupBuilder cafeGroup = app.MapGroup(string.Empty)
            .AddEndpointFilter<CafeMutationIdempotencyEndpointFilter>();

        cafeGroup.MapLocalMenuCatalogEndpoints();
        cafeGroup.MapLocalIngredientRecipeEndpoints();
        cafeGroup.MapLocalCafeInventoryEndpoints();
        cafeGroup.MapLocalCafeOrderEndpoints();
        cafeGroup.MapLocalCafeCashEndpoints();
        cafeGroup.MapLocalCafePrintEndpoints();
        cafeGroup.MapLocalCafeDeliveryEndpoints();
        cafeGroup.MapLocalCafeBookingEndpoints();
        cafeGroup.MapLocalCafeAttendanceIncidentEndpoints();

        return app;
    }
}
