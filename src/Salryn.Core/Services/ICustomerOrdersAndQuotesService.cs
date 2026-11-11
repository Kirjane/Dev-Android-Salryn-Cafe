using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Core.Services;

public interface ICustomerOrdersAndQuotesService
{
    Task<Result<IReadOnlyList<CustomerQuotation>>> GetQuotationsAsync(CancellationToken cancellationToken = default);
    Task<Result<CustomerQuotation>> GetQuotationByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<CustomerQuotation>> CreateQuotationAsync(Guid userId, string userName, CreateCustomerQuotationRequest request, CancellationToken cancellationToken = default);
    Task<Result<CustomerQuotation>> UpdateQuotationStatusAsync(Guid id, string status, CancellationToken cancellationToken = default, Guid? actorUserId = null);

    Task<Result<IReadOnlyList<CustomerOrder>>> GetOrdersAsync(string? status = null, CancellationToken cancellationToken = default);
    Task<Result<CustomerOrder>> GetOrderByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<Result<CustomerOrder>> CreateOrderAsync(Guid userId, string userName, CreateCustomerOrderRequest request, CancellationToken cancellationToken = default);
    Task<Result<CustomerOrder>> UpdateOrderStatusAsync(Guid id, string status, CancellationToken cancellationToken = default, Guid? actorUserId = null);
    Task<Result<CustomerOrder>> FulfillOrderAsync(Guid orderId, Guid saleId, CancellationToken cancellationToken = default, Guid? actorUserId = null);
    Task<Result<CustomerOrder>> FulfillOrderByReferenceAsync(Guid orderId, string saleReference, CancellationToken cancellationToken = default, Guid? actorUserId = null);
}
