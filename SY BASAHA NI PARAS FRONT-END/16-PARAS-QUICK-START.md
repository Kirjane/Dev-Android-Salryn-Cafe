# 16 - Paras Quick Start

1. Open the solution:

    Salryn.sln

2. Run the app.

The app currently launches:

    CheckoutShellForm

3. First files to inspect:

    src/Salryn.App.WinForms/Features/Checkout/CheckoutShellForm.cs
    src/Salryn.App.WinForms/Features/Checkout/CheckoutPresenter.cs
    src/Salryn.App.WinForms/Features/Checkout/CheckoutViewModels.cs

4. What to improve first:

- Spacing
- Alignment
- Labels
- Grid columns
- Total panel
- Product search area
- Action button layout

5. Do not connect sale completion.

Complete Sale must stay disabled until later phase.

6. Verify:

    dotnet build "Salryn.sln"
    dotnet run --project "tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj" -- pos-checkout-ui-presenter-viewmodel
    dotnet run --project "tools\Salryn.Tools.InfrastructureSmoke\Salryn.Tools.InfrastructureSmoke.csproj" -- full
