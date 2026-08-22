using Salryn.Core.Common;
using Salryn.Core.ReadModels;

namespace Salryn.App.WinForms.Features.Checkout;

// Kirch file guide: interface for product search used by CheckoutPresenter.
// UI location: abstraction behind the Checkout search box.
// Frontend note: forms should depend on presenter, not this interface directly.

public interface ICheckoutProductSearchBoundary
{
    Task<Result<ProductLookupReadModel?>> FindProductAsync(
        string searchText,
        CancellationToken cancellationToken = default);
}
