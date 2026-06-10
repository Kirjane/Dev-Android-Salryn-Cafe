# 09 - Phase 41 README

Next planned phase:

    Phase 41: POS Checkout Read-Only Product Search Presenter Boundary

## Goal

Wire read-only product search into the presenter.

Meaning:

User enters/scans barcode or product text.

Presenter can search products using read-only repository later.

Then product picker boundary prepares a line input.

Expected flow:

    Barcode / product text
        -> CheckoutPresenter search method
            -> IProductReadRepository
                -> PosCheckoutProductPickerBoundary
                    -> Prepared PosCheckoutDraftLineInput

Allowed:

- Read product by barcode.
- Read product by ID.
- Search product by name/barcode.
- Convert found product to prepared checkout line.
- Show product result in viewmodel.

Not allowed:

- Complete sale.
- Write sale.
- Write stock movement.
- Print receipt.
- Change schema.
- Create SQL file.

Possible presenter methods later:

    SearchProductAsync(string input)
    PrepareSelectedProductLine(...)
    ClearProductSelection()

Possible viewmodel additions later:

    bool HasPreparedProduct
    Guid? PreparedProductId
    string PreparedProductName
    string PreparedProductPriceText
    string PreparedProductStockText
    string ProductSearchStatusText

After Phase 41:

Frontend presenter can read product info safely.

But sale completion remains disabled.
