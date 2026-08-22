namespace Salryn.Tests;

public static class J20UiWorkflowReadinessChecks
{
    public static void Run()
    {
        DirectoryInfo repoRoot = FindRepositoryRoot();

        string shellText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Shell", "SalrynShellForm.cs");
        string checkoutFormText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Checkout", "CheckoutShellForm.cs");
        string checkoutPresenterText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Checkout", "CheckoutPresenter.cs");
        string productManagementFormText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Products", "ProductManagementForm.cs");
        string productCreateFormText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Products", "ProductCreateForm.cs");
        string productPresenterText = ReadRequiredFile(repoRoot, "src", "Salryn.App.WinForms", "Features", "Products", "ProductManagementPresenter.cs");
        string coreSmokeText = ReadRequiredFile(repoRoot, "tools", "Salryn.Tools.CoreSmoke", "Program.cs");
        string handoffText = ReadRequiredFile(repoRoot, "00-HANDOFF", "J20_4B_UI_WORKFLOW_READINESS_GUARDS.md");

        RequireContains(shellText, "OpenCheckout();", "Shell must route POS Checkout module to OpenCheckout.");
        RequireContains(shellText, "using CashierShiftGateForm shiftGateForm = new(", "Checkout must pass through cashier shift gate before opening.");
        RequireContains(shellText, "CheckoutSessionContext.From(", "Checkout must bind authenticated user plus open cashier shift context.");
        RequireContains(shellText, "new CheckoutShellForm(new CheckoutPresenter(", "Shell must open CheckoutShellForm through CheckoutPresenter.");
        RequireContains(shellText, "_services.CheckoutProductSearch", "Checkout UI must use AppServices product search boundary.");
        RequireContains(shellText, "completionBoundary: new CheckoutCompletionBoundary(_services.PosCheckoutApplication)", "Checkout UI must wire completion to PosCheckoutApplication.");

        RequireContains(shellText, "new ProductManagementForm(", "Shell must open Products through ProductManagementForm.");
        RequireContains(shellText, "new ProductManagementPresenter(", "Shell must open Products through ProductManagementPresenter.");
        RequireContains(shellText, "_services.ProductReadRepository", "Products UI must receive product read repository through AppServices.");
        RequireContains(shellText, "_services.ProductWriteRepository", "Products UI must receive product write repository through AppServices.");
        RequireContains(shellText, "_services.CatalogReadRepository", "Products UI must receive catalog repository through AppServices.");

        RequireContains(checkoutFormText, "_productInputTextBox.TextChanged += (_, _) =>", "Checkout product text box must update presenter input.");
        RequireContains(checkoutFormText, "_presenter.SetProductInputText(_productInputTextBox.Text)", "Checkout product input must stay presenter-backed.");
        RequireContains(checkoutFormText, "_productInputTextBox.KeyDown += async (_, e) =>", "Checkout scanner/Enter path must be wired.");
        RequireContains(checkoutFormText, "await SearchCurrentProductAsync();", "Checkout scanner/search path must call SearchCurrentProductAsync.");
        RequireContains(checkoutFormText, "_searchButton.Click += async (_, _) => await SearchCurrentProductAsync();", "Checkout Search button must call product search.");
        RequireContains(checkoutFormText, "_presenter.SearchProductAsync(_productInputTextBox.Text)", "Checkout search must call presenter SearchProductAsync.");
        RequireContains(checkoutFormText, "_presenter.AddPreparedProductLine()", "Checkout Add button must add prepared product through presenter.");
        RequireContains(checkoutFormText, "_presenter.SetCashAmountText(_cashAmountTextBox.Text)", "Checkout cash amount must be parsed by presenter.");
        RequireContains(checkoutFormText, "_presenter.AddCashPayment()", "Checkout Pay Cash button must add payment through presenter.");
        RequireContains(checkoutFormText, "_presenter.DryRunCompletionRequest()", "Checkout dry-run button must call presenter dry-run.");
        RequireContains(checkoutFormText, "_presenter.PrepareSaleSubmitGuard()", "Checkout submit guard button must call presenter guard.");
        RequireContains(checkoutFormText, "private DialogResult ShowCheckoutMessageDialog(", "Checkout completion dialogs must use a proper modal window.");
        RequireContains(checkoutFormText, "Size = new Size(560, 380),", "Checkout completion dialogs must be large enough to read.");

        RequireContains(checkoutFormText, "ShowCompletionConfirmationDialog(beforeSubmit)", "Complete Sale must show confirmation before submit.");
        RequireContains(checkoutFormText, "_presenter.ExecutePreparedSaleSubmitAsync()", "Complete Sale button must execute presenter sale submit.");
        RequireContains(checkoutFormText, "ShowCompletionResultDialog(result.State, result.IsSuccess)", "Complete Sale must report submit result.");
        RequireContains(checkoutFormText, "MinimumSize = new Size(1120, 680);", "Checkout window must fit short 768p work areas.");
        RequireContains(checkoutFormText, "AutoScroll = true,", "Checkout right action column must remain scrollable on short displays.");
        RequireContains(checkoutFormText, "right.RowStyles.Add(new RowStyle(SizeType.Absolute, 230F));", "Checkout totals card must be compact enough for action visibility.");
        RequireContains(checkoutFormText, "right.RowStyles.Add(new RowStyle(SizeType.Absolute, 164F));", "Checkout payment card must be tall enough to show Add Cash Payment.");
        RequireContains(checkoutFormText, "layout.Controls.Add(_completeSaleButton, 0, 4);", "Complete Sale button must stay near the top of the action panel.");
        RequireContains(checkoutFormText, "_completeSaleButton.Enabled = state.CanCompleteSale;", "Complete Sale button must be enabled only from view-model readiness.");
        RequireContains(checkoutFormText, "_clearCartButton.Text = saleCompleted ? \"Start New Sale\" : \"Clear Draft Cart\";", "Checkout must expose Start New Sale after completion.");
        RequireContains(checkoutFormText, "_cartGrid.Rows[rowIndex].Tag = line.LineId;", "Checkout cart rows must keep selected line IDs for remove flow.");

        RequireContains(checkoutPresenterText, "public async Task<CheckoutPresenterOperationResult> SearchProductAsync(", "Checkout presenter must expose async product search.");
        RequireContains(checkoutPresenterText, "public CheckoutPresenterOperationResult AddPreparedProductLine()", "Checkout presenter must expose add prepared product.");
        RequireContains(checkoutPresenterText, "public CheckoutPresenterOperationResult AddCashPayment()", "Checkout presenter must expose cash payment.");
        RequireContains(checkoutPresenterText, "public CheckoutPresenterOperationResult DryRunCompletionRequest()", "Checkout presenter must expose completion dry run.");
        RequireContains(checkoutPresenterText, "public CheckoutPresenterOperationResult PrepareSaleSubmitGuard()", "Checkout presenter must expose submit guard.");
        RequireContains(checkoutPresenterText, "public async Task<CheckoutPresenterOperationResult> ExecutePreparedSaleSubmitAsync(", "Checkout presenter must expose guarded final submit.");

        RequireContains(productManagementFormText, "protected override async void OnShown(EventArgs e)", "Product management must load products on shown.");
        RequireContains(productManagementFormText, "await LoadProductsAsync();", "Product management OnShown must call LoadProductsAsync.");
        RequireContains(productManagementFormText, "_addButton.Click += async (_, _) => await OpenCreateFormAsync();", "Add Product button must open create form.");
        RequireContains(productManagementFormText, "using ProductCreateForm createForm = new(_presenter);", "Product create form must reuse the same presenter.");
        RequireContains(productManagementFormText, "await _presenter.SearchAsync(_searchTextBox.Text)", "Product management must refresh list after successful create.");

        RequireContains(productCreateFormText, "await LoadCatalogsAsync();", "Product create form must load catalog options on shown.");
        RequireContains(productCreateFormText, "ProductCreateRequest request = new(", "Product create form must package input into ProductCreateRequest.");
        RequireContains(productCreateFormText, "await _presenter.CreateProductAsync(request)", "Product create Save must call presenter CreateProductAsync.");
        RequireContains(productCreateFormText, "DialogResult = DialogResult.OK;", "Product create form must close only after successful save.");

        RequireContains(productPresenterText, "_catalogs.GetUnitsAsync", "Product presenter must load unit catalog for create form.");
        RequireContains(productPresenterText, "_productWrites.AddAsync(product, cancellationToken)", "Product presenter must save through product write repository.");

        RequireContains(coreSmokeText, "J20UiWorkflowReadinessChecks.Run();", "CoreSmoke must run J20 UI workflow readiness checks.");

        RequireContains(handoffText, "Status: UI workflow readiness guards only.", "J20-4B handoff must mark guard-only scope.");
        RequireContains(handoffText, "No app run.", "J20-4B handoff must preserve no-run boundary.");
    }

    private static DirectoryInfo FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        current = new DirectoryInfo(Directory.GetCurrentDirectory());

        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Salryn.sln")))
            {
                return current;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate Salryn repository root.");
    }

    private static string ReadRequiredFile(DirectoryInfo repoRoot, params string[] pathParts)
    {
        string path = repoRoot.FullName;

        foreach (string pathPart in pathParts)
        {
            path = Path.Combine(path, pathPart);
        }

        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Required file was not found: {path}");
        }

        return File.ReadAllText(path);
    }

    private static void RequireContains(string text, string value, string message)
    {
        if (!text.Contains(value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(message);
        }
    }
}
