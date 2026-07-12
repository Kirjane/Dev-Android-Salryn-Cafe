using Salryn.Core.Common;
using Salryn.Core.Models;
using Salryn.Core.ReadModels;
using Salryn.Core.Repositories;
using Salryn.Core.Services;
using Salryn.Core.ValueObjects;

namespace Salryn.Tests;

public static class PosM2PrinterReprintFailureChecks
{
    public static void Run()
    {
        string persistedSnapshot = "SALRYN POS\nReceipt: POS-M2-000001\nSnapshot Item\nTOTAL: PHP 120.00";
        SaleDetailReadModel sale = CreateSaleDetail(Guid.NewGuid(), "POS-M2-000001", persistedSnapshot);

        MemorySaleReadRepository successRepo = new(sale);
        NoOpReceiptPrinterAdapter successAdapter = new("FakeNoOpPrinter");
        PosReceiptPrintService successService = new(successRepo, successAdapter);
        PosReceiptPrintResult success = successService.PrintSaleReceiptAsync(sale.Id).GetAwaiter().GetResult();

        Require(success.Succeeded, "Print service must succeed with no-op/fake printer.");
        Require(string.Equals(success.Status, PosReceiptPrintStatuses.Succeeded, StringComparison.Ordinal), "Print success status must be Succeeded.");
        Require(string.Equals(success.Source, PosReceiptPrintSources.PersistedReceiptSnapshot, StringComparison.Ordinal), "Print source must be persisted receipt snapshot.");
        Require(string.Equals(successAdapter.LastReceiptText, persistedSnapshot, StringComparison.Ordinal), "Printer adapter must receive persisted snapshot text exactly.");
        Require(successRepo.FindByIdCalls == 1, "Print service should read the existing sale once.");

        MemorySaleReadRepository failureRepo = new(sale);
        PosReceiptPrintService failureService = new(failureRepo, new FailingReceiptPrinterAdapter());
        PosReceiptPrintResult failure = failureService.PrintSaleReceiptAsync(sale.Id).GetAwaiter().GetResult();

        Require(!failure.Succeeded, "Print failure must not report success.");
        Require(string.Equals(failure.Status, PosReceiptPrintStatuses.Failed, StringComparison.Ordinal), "Print failure status must be Failed.");
        Require(!string.IsNullOrWhiteSpace(failure.ErrorMessage), "Print failure must be diagnosable.");
        Require(failureRepo.FindByIdCalls == 1, "Failure path must still only read existing sale state.");

        MemorySaleReadRepository disabledRepo = new(sale);
        PosReceiptPrintService disabledService = new(disabledRepo, new DisabledReceiptPrinterAdapter());
        PosReceiptPrintResult disabled = disabledService.PrintSaleReceiptAsync(sale.Id).GetAwaiter().GetResult();

        Require(!disabled.Succeeded, "Disabled printer path must not report success.");
        Require(string.Equals(disabled.Status, PosReceiptPrintStatuses.DisabledUnavailable, StringComparison.Ordinal), "Disabled printer path must return Disabled/Unavailable.");
        Require(disabledRepo.FindByIdCalls == 1, "Disabled path must not mutate sale state.");

        SaleDetailReadModel noSnapshotSale = CreateSaleDetail(Guid.NewGuid(), "POS-M2-000002", null);
        PosReceiptPrintService noSnapshotService = new(new MemorySaleReadRepository(noSnapshotSale), new NoOpReceiptPrinterAdapter());
        PosReceiptPrintResult noSnapshot = noSnapshotService.PrintSaleReceiptAsync(noSnapshotSale.Id).GetAwaiter().GetResult();

        Require(!noSnapshot.Succeeded, "Missing snapshot must not print.");
        Require(string.Equals(noSnapshot.Status, PosReceiptPrintStatuses.NotAttempted, StringComparison.Ordinal), "Missing snapshot must return NotAttempted.");

        PosReceiptPrintService testPrintService = new(successRepo, new NoOpReceiptPrinterAdapter("FakeTestPrinter"));
        PosReceiptPrintResult testPrint = testPrintService.TestPrintAsync().GetAwaiter().GetResult();
        Require(testPrint.Succeeded, "Test print must succeed with fake/no-op adapter.");
        Require(string.Equals(testPrint.Source, PosReceiptPrintSources.TestPrintSample, StringComparison.Ordinal), "Test print must use sample source.");
    }

    private static SaleDetailReadModel CreateSaleDetail(Guid saleId, string receiptNo, string? snapshot)
    {
        Guid itemId = Guid.NewGuid();
        Guid productId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        Guid paymentId = Guid.NewGuid();
        DateTimeOffset saleDate = new(2026, 7, 13, 9, 0, 0, TimeSpan.Zero);

        return new SaleDetailReadModel(
            saleId,
            receiptNo,
            saleDate,
            "POS-M2 Cashier",
            Money.FromCents(12000),
            Money.Zero,
            Money.Zero,
            Money.FromCents(12000),
            Money.FromCents(15000),
            Money.FromCents(3000),
            SaleStatus.Completed,
            null,
            null,
            snapshot,
            new[]
            {
                new SaleItemDetailReadModel(
                    itemId,
                    productId,
                    "Snapshot Item",
                    "480000000002",
                    "pc",
                    Quantity.FromX1000(1000).Value,
                    Money.FromCents(12000),
                    Money.FromCents(8000),
                    Money.Zero,
                    Money.FromCents(12000))
            }.ToList(),
            new[]
            {
                new PaymentReadModel(
                    paymentId,
                    PaymentMethod.Cash,
                    Money.FromCents(15000),
                    saleDate.AddMinutes(1))
            }.ToList());
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private sealed class MemorySaleReadRepository : ISaleReadRepository
    {
        private readonly SaleDetailReadModel _sale;

        public MemorySaleReadRepository(SaleDetailReadModel sale)
        {
            _sale = sale;
        }

        public int FindByIdCalls { get; private set; }

        public Task<Result<SaleDetailReadModel?>> FindSaleByIdAsync(
            Guid saleId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FindByIdCalls++;
            SaleDetailReadModel? result = saleId == _sale.Id ? _sale : null;
            return Task.FromResult(Result<SaleDetailReadModel?>.Success(result));
        }

        public Task<Result<SaleDetailReadModel?>> FindSaleByReceiptNoAsync(
            string receiptNo,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaleDetailReadModel? result = string.Equals(receiptNo, _sale.ReceiptNo, StringComparison.Ordinal) ? _sale : null;
            return Task.FromResult(Result<SaleDetailReadModel?>.Success(result));
        }

        public Task<Result<PagedResult<SaleHistoryItemReadModel>>> SearchSalesAsync(
            SaleHistoryFilter filter,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Result<PagedResult<SaleHistoryItemReadModel>>.Fail("Search is not used by POS-M2 printer smoke checks."));
        }
    }

    private sealed class FailingReceiptPrinterAdapter : IReceiptPrinterAdapter
    {
        public string AdapterName => "FailingReceiptPrinterAdapter";

        public bool IsEnabled => true;

        public Task<ReceiptPrinterAdapterResult> PrintAsync(
            string jobName,
            string receiptText,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(ReceiptPrinterAdapterResult.Failed(
                "Fake printer failed for POS-M2 smoke coverage.",
                "FAKE_PRINTER_OFFLINE"));
        }
    }
}
