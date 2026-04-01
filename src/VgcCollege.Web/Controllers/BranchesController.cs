using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VgcCollege.Web.Data;
using VgcCollege.Web.Domain;

namespace VgcCollege.Web.Controllers;

[Authorize(Roles = "Admin")]
public class BranchesController(ApplicationDbContext ctx) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await ctx.Branches.Include(b => b.Courses).ToListAsync());

    public async Task<IActionResult> Details(int? id)
    {
        if (id is null) return NotFound();
        var branch = await ctx.Branches.Include(b => b.Courses)
            .FirstOrDefaultAsync(b => b.Id == id);
        return branch is null ? NotFound() : View(branch);
    }

    public IActionResult Create() => View();

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("Name,Address")] Branch branch)
    {
        if (!ModelState.IsValid) return View(branch);
        ctx.Branches.Add(branch);
        await ctx.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null) return NotFound();
        var branch = await ctx.Branches.FindAsync(id);
        return branch is null ? NotFound() : View(branch);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Id,Name,Address")] Branch branch)
    {
        if (id != branch.Id) return NotFound();
        if (!ModelState.IsValid) return View(branch);
        ctx.Update(branch);
        await ctx.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id is null) return NotFound();
        var branch = await ctx.Branches.Include(b => b.Courses)
            .FirstOrDefaultAsync(b => b.Id == id);
        return branch is null ? NotFound() : View(branch);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var branch = await ctx.Branches.FindAsync(id);
        if (branch is null) return NotFound();
        ctx.Branches.Remove(branch);
        await ctx.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}