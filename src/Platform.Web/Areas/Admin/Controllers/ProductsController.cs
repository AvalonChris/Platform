using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Platform.Web.Areas.Admin.Data;
using Platform.Web.Areas.Admin.Models;
using Platform.Web.Models;

namespace Platform.Web.Areas.Admin.Controllers;

[Route("admin/products")]
public class ProductsController(AdminProductRepository products) : AdminController
{
    [HttpGet("")]
    public async Task<IActionResult> Index() => View(await products.ListAsync());

    [HttpGet("new")]
    public async Task<IActionResult> New() => await EditViewAsync(new ProductForm());

    [HttpPost("new")]
    public async Task<IActionResult> Create(ProductForm form)
    {
        form.Id = 0;
        if (form.Kind is not (Product.KindSingle or Product.KindStack))
            ModelState.AddModelError("Form.Kind", "Choose a kind.");

        if (ModelState.IsValid && await TrySaveAsync(() => products.CreateAsync(form)))
        {
            TempData["Message"] = $"{form.Name} was created.";
            return RedirectToAction(nameof(Index));
        }

        return await EditViewAsync(form);
    }

    [HttpGet("{id:long}")]
    public async Task<IActionResult> Edit(long id)
    {
        var form = await products.GetAsync(id);
        return form is null ? NotFound() : await EditViewAsync(form);
    }

    [HttpPost("{id:long}")]
    public async Task<IActionResult> Update(long id, ProductForm form)
    {
        var existing = await products.GetAsync(id);
        if (existing is null)
            return NotFound();

        form.Id = id;
        form.Kind = existing.Kind;

        if (ModelState.IsValid && await TrySaveAsync(() => products.UpdateAsync(form)))
        {
            TempData["Message"] = $"{form.Name} was saved.";
            return RedirectToAction(nameof(Index));
        }

        return await EditViewAsync(form);
    }

    private async Task<IActionResult> EditViewAsync(ProductForm form)
    {
        var singles = (await products.ListAsync()).Where(product => !product.IsStack).ToList();
        return View("Edit", new ProductEditViewModel(form, singles));
    }

    /// <summary>Turns a duplicate slug or SKU into a field error instead of a crash.</summary>
    private async Task<bool> TrySaveAsync(Func<Task> save)
    {
        try
        {
            await save();
            return true;
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            var field = exception.ConstraintName == "products_sku_key" ? "Form.Sku" : "Form.Slug";
            ModelState.AddModelError(field, "Already used by another product.");
            return false;
        }
    }
}
