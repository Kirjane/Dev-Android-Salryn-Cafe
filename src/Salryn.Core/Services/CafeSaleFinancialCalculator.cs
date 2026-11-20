using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using Salryn.Core.Common;
using Salryn.Core.Models;

namespace Salryn.Core.Services;

public static class CafeSaleFinancialCalculator
{
    public static Result<CafeSaleFinancialDecision> Calculate(
        CafeSaleFinancialDecisionRequest request,
        bool allowDeliveryFee = false)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result identity = ValidateIdentity(request);
        if (identity.IsFailure)
            return Result<CafeSaleFinancialDecision>.Fail(identity.ErrorDetail);

        if (request.Lines is null || request.Lines.Count == 0)
        {
            return Result<CafeSaleFinancialDecision>.Fail(Error.Validation(
                "CafeFinancial.LinesRequired",
                "At least one sale line is required."));
        }

        HashSet<Guid> lineIds = new();
        long grossSubtotal = 0;
        long lineDiscountTotal = 0;

        foreach (CafeSaleFinancialLineInput line in request.Lines)
        {
            if (line.SaleItemId == Guid.Empty)
            {
                return Result<CafeSaleFinancialDecision>.Fail(Error.Validation(
                    "CafeFinancial.InvalidSaleItemId",
                    "Sale item ID is required."));
            }

            if (!lineIds.Add(line.SaleItemId))
            {
                return Result<CafeSaleFinancialDecision>.Fail(Error.Validation(
                    "CafeFinancial.DuplicateSaleItemId",
                    "Sale item IDs must be unique."));
            }

            if (line.GrossLineCents < 0 ||
                line.LineDiscountCents < 0 ||
                line.LineDiscountCents > line.GrossLineCents)
            {
                return Result<CafeSaleFinancialDecision>.Fail(Error.Validation(
                    "CafeFinancial.InvalidLineDiscount",
                    "Line discount must be between zero and the gross line amount."));
            }

            grossSubtotal = checked(grossSubtotal + line.GrossLineCents);
            lineDiscountTotal = checked(lineDiscountTotal + line.LineDiscountCents);
        }

        long afterLineDiscounts = checked(grossSubtotal - lineDiscountTotal);
        if (request.OrderDiscountCents < 0 ||
            request.OrderDiscountCents > afterLineDiscounts)
        {
            return Result<CafeSaleFinancialDecision>.Fail(Error.Validation(
                "CafeFinancial.InvalidOrderDiscount",
                "Order discount cannot exceed the amount remaining after line discounts."));
        }

        if ((lineDiscountTotal > 0 || request.OrderDiscountCents > 0) &&
            string.IsNullOrWhiteSpace(request.DiscountReason))
        {
            return Result<CafeSaleFinancialDecision>.Fail(Error.Validation(
                "CafeFinancial.DiscountReasonRequired",
                "A discount reason is required when a discount is applied."));
        }

        Result<IReadOnlyList<CafeSaleFeeDecision>> feesResult =
            BuildFees(request, allowDeliveryFee);
        if (feesResult.IsFailure)
            return Result<CafeSaleFinancialDecision>.Fail(feesResult.ErrorDetail);

        IReadOnlyList<CafeSaleFeeDecision> fees = feesResult.Value;
        long feeTotal = fees.Aggregate(
            0L,
            (current, fee) => checked(current + fee.AmountCents));

        if (request.TaxCents < 0)
        {
            return Result<CafeSaleFinancialDecision>.Fail(Error.Validation(
                "CafeFinancial.InvalidTax",
                "Tax cannot be negative."));
        }

        IReadOnlyList<CafeSaleFinancialLineDecision> lineDecisions =
            AllocateOrderDiscount(
                request.Lines,
                request.OrderDiscountCents);

        long netSaleTotal = checked(
            grossSubtotal
            - lineDiscountTotal
            - request.OrderDiscountCents
            + feeTotal
            + request.TaxCents);

        if (request.CashTenderCents < 0 ||
            request.AccountsReceivableTenderCents < 0 ||
            request.BookingDepositAppliedCents < 0)
        {
            return Result<CafeSaleFinancialDecision>.Fail(Error.Validation(
                "CafeFinancial.InvalidTender",
                "Cash, accounts receivable and booking deposit settlement amounts cannot be negative."));
        }

        if (checked(
                request.AccountsReceivableTenderCents +
                request.BookingDepositAppliedCents) > netSaleTotal)
        {
            return Result<CafeSaleFinancialDecision>.Fail(Error.Validation(
                "CafeFinancial.NonCashSettlementExceedsSale",
                "Accounts receivable plus booking deposit application cannot exceed the sale total."));
        }

        long amountPaid = checked(
            request.CashTenderCents +
            request.AccountsReceivableTenderCents +
            request.BookingDepositAppliedCents);

        if (amountPaid < netSaleTotal)
        {
            return Result<CafeSaleFinancialDecision>.Fail(Error.Validation(
                "CafeFinancial.InsufficientTender",
                "Cash plus accounts receivable plus booking deposit settlement must cover the sale total."));
        }

        long change = checked(amountPaid - netSaleTotal);

        string hash = ComputeContentHash(
            request,
            lineDecisions,
            fees,
            grossSubtotal,
            lineDiscountTotal,
            feeTotal,
            netSaleTotal,
            amountPaid,
            change);

        return Result<CafeSaleFinancialDecision>.Success(
            new CafeSaleFinancialDecision(
                request.FinancialDecisionId,
                request.OperationId,
                request.StoreId,
                request.OrderId,
                request.OrderRevisionId,
                request.ExpectedOrderVersion,
                request.SaleId,
                request.CashierSessionId,
                request.ActorUserId,
                grossSubtotal,
                lineDiscountTotal,
                request.OrderDiscountCents,
                feeTotal,
                request.TaxCents,
                netSaleTotal,
                request.CashTenderCents,
                request.AccountsReceivableTenderCents,
                amountPaid,
                request.AccountsReceivableTenderCents,
                change,
                NormalizeOptional(request.DiscountReason),
                hash,
                request.OccurredAtUtc,
                lineDecisions,
                fees)
            {
                BookingDepositAppliedCents =
                    request.BookingDepositAppliedCents
            });
    }

    private static Result ValidateIdentity(
        CafeSaleFinancialDecisionRequest request)
    {
        if (request.OperationId == Guid.Empty ||
            request.FinancialDecisionId == Guid.Empty ||
            request.StoreId == Guid.Empty ||
            request.SaleId == Guid.Empty ||
            request.CashierSessionId == Guid.Empty ||
            request.ActorUserId == Guid.Empty)
        {
            return Result.Fail(Error.Validation(
                "CafeFinancial.InvalidIdentity",
                "Operation, decision, store, sale, cashier session and actor IDs are required."));
        }

        bool hasOrder = request.OrderId.HasValue;
        bool hasRevision = request.OrderRevisionId.HasValue;
        bool hasVersion = request.ExpectedOrderVersion.HasValue;

        if (!(hasOrder == hasRevision && hasRevision == hasVersion))
        {
            return Result.Fail(Error.Validation(
                "CafeFinancial.IncompleteOrderBinding",
                "Order ID, order revision ID and expected order version must be supplied together."));
        }

        if (request.OrderId == Guid.Empty ||
            request.OrderRevisionId == Guid.Empty ||
            request.ExpectedOrderVersion < 0)
        {
            return Result.Fail(Error.Validation(
                "CafeFinancial.InvalidOrderBinding",
                "Order binding contains an invalid identifier or version."));
        }

        return Result.Success();
    }

    private static Result<IReadOnlyList<CafeSaleFeeDecision>> BuildFees(
        CafeSaleFinancialDecisionRequest request,
        bool allowDeliveryFee)
    {
        IReadOnlyList<CafeSaleFeeMutation> source =
            request.Fees ?? Array.Empty<CafeSaleFeeMutation>();

        HashSet<Guid> ids = new();
        List<CafeSaleFeeDecision> result = new(source.Count);

        foreach (CafeSaleFeeMutation fee in source)
        {
            if (fee.FeeId == Guid.Empty || !ids.Add(fee.FeeId))
            {
                return Result<IReadOnlyList<CafeSaleFeeDecision>>.Fail(
                    Error.Validation(
                        "CafeFinancial.InvalidFeeId",
                        "Fee IDs must be non-empty and unique."));
            }

            bool isDelivery = string.Equals(
                fee.FeeType,
                CafeSaleFeeTypes.Delivery,
                StringComparison.Ordinal);

            if (isDelivery && !allowDeliveryFee)
            {
                return Result<IReadOnlyList<CafeSaleFeeDecision>>.Fail(
                    Error.Validation(
                        "CafeFinancial.DeliveryFeeDeferred",
                        "Delivery fee requires C8 delivery authority."));
            }

            if (!CafeSaleFeeTypes.IsSupportedForC5(fee.FeeType) &&
                !(allowDeliveryFee && isDelivery))
            {
                return Result<IReadOnlyList<CafeSaleFeeDecision>>.Fail(
                    Error.Validation(
                        "CafeFinancial.UnsupportedFeeType",
                        "Unsupported Cafe sale fee type."));
            }

            if (fee.AmountCents < 0)
            {
                return Result<IReadOnlyList<CafeSaleFeeDecision>>.Fail(
                    Error.Validation(
                        "CafeFinancial.InvalidFeeAmount",
                        "Fee amount cannot be negative."));
            }

            string label = (fee.DisplayLabel ?? string.Empty).Trim();
            if (label.Length == 0)
            {
                return Result<IReadOnlyList<CafeSaleFeeDecision>>.Fail(
                    Error.Validation(
                        "CafeFinancial.FeeLabelRequired",
                        "Fee display label is required."));
            }

            result.Add(new CafeSaleFeeDecision(
                fee.FeeId,
                fee.FeeType,
                label,
                fee.AmountCents,
                request.ActorUserId,
                NormalizeOptional(fee.Reason),
                request.OccurredAtUtc));
        }

        return Result<IReadOnlyList<CafeSaleFeeDecision>>.Success(result);
    }

    private static IReadOnlyList<CafeSaleFinancialLineDecision>
        AllocateOrderDiscount(
            IReadOnlyList<CafeSaleFinancialLineInput> lines,
            long orderDiscountCents)
    {
        CafeSaleFinancialLineInput[] ordered = lines
            .OrderBy(line => line.DisplayOrder)
            .ThenBy(line => line.SaleItemId)
            .ToArray();

        long eligibleTotal = ordered.Aggregate(
            0L,
            (current, line) =>
                checked(
                    current +
                    line.GrossLineCents -
                    line.LineDiscountCents));

        Dictionary<Guid, long> allocations = ordered.ToDictionary(
            line => line.SaleItemId,
            _ => 0L);

        if (orderDiscountCents > 0 && eligibleTotal > 0)
        {
            long allocated = 0;

            foreach (CafeSaleFinancialLineInput line in ordered)
            {
                long eligible =
                    line.GrossLineCents -
                    line.LineDiscountCents;

                BigInteger numerator =
                    (BigInteger)orderDiscountCents * eligible;

                long share = (long)(numerator / eligibleTotal);
                allocations[line.SaleItemId] = share;
                allocated = checked(allocated + share);
            }

            long remainder = orderDiscountCents - allocated;

            foreach (CafeSaleFinancialLineInput line in ordered)
            {
                if (remainder == 0)
                    break;

                long eligible =
                    line.GrossLineCents -
                    line.LineDiscountCents;

                if (eligible <= allocations[line.SaleItemId])
                    continue;

                allocations[line.SaleItemId]++;
                remainder--;
            }

            if (remainder != 0)
            {
                throw new InvalidOperationException(
                    "Deterministic order discount allocation did not exhaust the requested discount.");
            }
        }

        return lines.Select(line =>
            new CafeSaleFinancialLineDecision(
                line.SaleItemId,
                line.LineDiscountCents,
                allocations[line.SaleItemId]))
            .ToArray();
    }

    public static string ComputeContentHash(
        CafeSaleFinancialDecision decision)
    {
        ArgumentNullException.ThrowIfNull(decision);

        StringBuilder canonical = new();

        Append(canonical, decision.FinancialDecisionId);
        Append(canonical, decision.StoreId);
        Append(canonical, decision.OrderId);
        Append(canonical, decision.OrderRevisionId);
        Append(canonical, decision.ExpectedOrderVersion);
        Append(canonical, decision.SaleId);
        Append(canonical, decision.CashierSessionId);
        Append(canonical, decision.ActorUserId);
        Append(canonical, decision.GrossSubtotalCents);
        Append(canonical, decision.LineDiscountTotalCents);
        Append(canonical, decision.OrderDiscountTotalCents);
        Append(canonical, decision.FeeTotalCents);
        Append(canonical, decision.TaxCents);
        Append(canonical, decision.NetSaleTotalCents);
        Append(canonical, decision.CashTenderCents);
        Append(canonical, decision.AccountsReceivableTenderCents);
        Append(canonical, decision.AmountPaidCents);
        if (decision.BookingDepositAppliedCents > 0)
            Append(canonical, decision.BookingDepositAppliedCents);
        Append(canonical, decision.ChangeCents);
        Append(canonical, NormalizeOptional(decision.DiscountReason));

        foreach (CafeSaleFinancialLineDecision line in decision.LineDecisions
                     .OrderBy(x => x.SaleItemId))
        {
            Append(canonical, line.SaleItemId);
            Append(canonical, line.LineDiscountCents);
            Append(canonical, line.OrderDiscountAllocatedCents);
        }

        foreach (CafeSaleFeeDecision fee in decision.Fees
                     .OrderBy(x => x.FeeId))
        {
            Append(canonical, fee.FeeId);
            Append(canonical, fee.FeeType);
            Append(canonical, fee.DisplayLabelSnapshot);
            Append(canonical, fee.AmountCents);
            Append(canonical, fee.Reason);
        }

        return Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }

    private static string ComputeContentHash(
        CafeSaleFinancialDecisionRequest request,
        IReadOnlyList<CafeSaleFinancialLineDecision> lines,
        IReadOnlyList<CafeSaleFeeDecision> fees,
        long grossSubtotal,
        long lineDiscountTotal,
        long feeTotal,
        long netSaleTotal,
        long amountPaid,
        long change)
    {
        StringBuilder canonical = new();

        Append(canonical, request.FinancialDecisionId);
        Append(canonical, request.StoreId);
        Append(canonical, request.OrderId);
        Append(canonical, request.OrderRevisionId);
        Append(canonical, request.ExpectedOrderVersion);
        Append(canonical, request.SaleId);
        Append(canonical, request.CashierSessionId);
        Append(canonical, request.ActorUserId);
        Append(canonical, grossSubtotal);
        Append(canonical, lineDiscountTotal);
        Append(canonical, request.OrderDiscountCents);
        Append(canonical, feeTotal);
        Append(canonical, request.TaxCents);
        Append(canonical, netSaleTotal);
        Append(canonical, request.CashTenderCents);
        Append(canonical, request.AccountsReceivableTenderCents);
        Append(canonical, amountPaid);
        if (request.BookingDepositAppliedCents > 0)
            Append(canonical, request.BookingDepositAppliedCents);
        Append(canonical, change);
        Append(canonical, NormalizeOptional(request.DiscountReason));

        foreach (CafeSaleFinancialLineDecision line in lines
                     .OrderBy(x => x.SaleItemId))
        {
            Append(canonical, line.SaleItemId);
            Append(canonical, line.LineDiscountCents);
            Append(canonical, line.OrderDiscountAllocatedCents);
        }

        foreach (CafeSaleFeeDecision fee in fees
                     .OrderBy(x => x.FeeId))
        {
            Append(canonical, fee.FeeId);
            Append(canonical, fee.FeeType);
            Append(canonical, fee.DisplayLabelSnapshot);
            Append(canonical, fee.AmountCents);
            Append(canonical, fee.Reason);
        }

        return Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(canonical.ToString())))
            .ToLowerInvariant();
    }

    private static void Append(
        StringBuilder builder,
        object? value)
    {
        string text = value switch
        {
            null => string.Empty,
            Guid guid => guid.ToString("D"),
            DateTimeOffset date => date.ToUniversalTime().ToString("O"),
            _ => Convert.ToString(
                    value,
                    System.Globalization.CultureInfo.InvariantCulture)
                 ?? string.Empty
        };

        builder
            .Append(text.Length)
            .Append(':')
            .Append(text)
            .Append('|');
    }

    private static string? NormalizeOptional(string? value)
    {
        string normalized = (value ?? string.Empty).Trim();
        return normalized.Length == 0 ? null : normalized;
    }
}
