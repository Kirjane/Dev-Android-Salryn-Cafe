using Salryn.Core.Common;
using Salryn.Core.ReadModels;

namespace Salryn.App.WinForms.Features.Checkout;

public interface ICheckoutProductSearchBoundary
{
    Task<Result<ProductLookupReadModel?>> FindProductAsync(
        string searchText,
        CancellationToken cancellationToken = default);
}
