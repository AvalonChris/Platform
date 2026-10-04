using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Platform.Web.Areas.Admin.Data;
using Platform.Web.Areas.Admin.Models;
using Platform.Web.Models;

namespace Platform.Web.Areas.Admin.Controllers;

public class CostsController(AdminCostRepository costs, IOptions<StoreOptions> store) : AdminController
{
    private const int BlankShippingRows = 2;

    [HttpGet("/admin/profit")]
    public async Task<IActionResult> Profit()
    {
        var settings = await costs.GetSettingsAsync();
        var rates = await costs.GetShippingRatesAsync();
        var (products, stackItems) = await costs.GetProfitInputsAsync();

        var rows = ProfitCalculator.Calculate(products, stackItems, settings, rates, store.Value);
        return View(new ProfitReportViewModel(rows, settings, store.Value.SubscriptionDiscountPercent));
    }

    [HttpGet("/admin/costs")]
    public async Task<IActionResult> Index()
    {
        var model = new CostSettingsViewModel
        {
            Settings = await costs.GetSettingsAsync(),
            ShippingRates = [.. await costs.GetShippingRatesAsync()]
        };
        return View(WithBlankRows(model));
    }

    [HttpPost("/admin/costs")]
    public async Task<IActionResult> Save(CostSettingsViewModel model)
    {
        // Rows left completely empty are ignored; half-filled ones are an error.
        var rates = model.ShippingRates.Where(rate => rate.MaxWeightLb is not null || rate.Rate is not null).ToList();

        if (rates.Any(rate => rate.MaxWeightLb is null || rate.Rate is null))
            ModelState.AddModelError("ShippingRates", "Fill in both the weight and the price for every shipping row.");
        else if (rates.Count == 0)
            ModelState.AddModelError("ShippingRates", "Add at least one shipping rate.");
        else if (rates.GroupBy(rate => rate.MaxWeightLb).Any(group => group.Count() > 1))
            ModelState.AddModelError("ShippingRates", "Each weight can only appear once.");

        if (!ModelState.IsValid)
            return View("Index", WithBlankRows(model));

        await costs.SaveAsync(model.Settings, rates);
        TempData["Message"] = "Costs saved.";
        return RedirectToAction(nameof(Index));
    }

    private static CostSettingsViewModel WithBlankRows(CostSettingsViewModel model)
    {
        var filled = model.ShippingRates
            .Where(rate => rate.MaxWeightLb is not null || rate.Rate is not null)
            .OrderBy(rate => rate.MaxWeightLb)
            .ToList();
        filled.AddRange(Enumerable.Range(0, BlankShippingRows).Select(_ => new ShippingRate()));
        model.ShippingRates = filled;
        return model;
    }
}
