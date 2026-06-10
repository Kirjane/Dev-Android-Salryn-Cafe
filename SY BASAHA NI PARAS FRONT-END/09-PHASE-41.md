# 09 - Phase 41

Next phase:

    Phase 41: POS Checkout Read-Only Product Search Presenter Boundary

Goal:

Allow the checkout presenter to search/read products safely.

Expected flow:

    Product input or barcode
        -> presenter search method
            -> product read repository
                -> product picker boundary
                    -> prepared checkout line

Allowed in Phase 41:

- read product by barcode
- search product by text
- read product details needed for checkout
- prepare product line input
- show product result in the viewmodel
- keep everything read-only

Not allowed in Phase 41:

- complete sale
- write sale
- write stock movement
- print receipt
- change schema
- create SQL file
- create database file

Possible state later:

    HasPreparedProduct
    PreparedProductId
    PreparedProductName
    PreparedProductPriceText
    PreparedProductStockText
    ProductSearchStatusText

The UI can prepare space for this:

- product name
- barcode
- unit
- price
- stock
- Add to Cart button

But the save/final sale part stays locked until a later phase.
