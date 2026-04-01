using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using VgcCollege.Web.Data;
using VgcCollege.Web.Domain;

namespace VgcCollege.Web.Controllers;

[Authorize(Roles = "Admin")]
public class FacultyProfilesController(ApplicationDbContext ctx) : Controller
{
    public async Task<IActionResult> Index() =>
        View(await ctx.FacultyProfiles.Include(f => f.CourseAssignments).ThenInclude(ca => ca.Course).ToListAsync());

    public async Task<IActionResult> Details(int? id)
    {
        if (id is null) return NotFound();
        var f = await ctx.FacultyProfiles
            .Include(f => f.CourseAssignments).ThenInclude(ca => ca.Course)
            .FirstOrDefaultAsync(f => f.Id == id);
        return f is null ? NotFound() : View(f);
    }

    public IActionResult Create()
    {
        ViewData["IdentityUserId"] = new SelectList(ctx.Users, "Id", "Email");
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("IdentityUserId,Name,Email,Phone")] FacultyProfile profile)
    {
        if (!ModelState.IsValid)
        {
            ViewData["IdentityUserId"] = new SelectList(ctx.Users, "Id", "Email", profile.IdentityUserId);
            return View(profile);
        }
        ctx.FacultyProfiles.Add(profile);
        await ctx.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null) return NotFound();
        var profile = await ctx.FacultyProfiles.FindAsync(id);
        if (profile is null) return NotFound();
        ViewData["IdentityUserId"] = new SelectList(ctx.Users, "Id", "Email", profile.IdentityUserId);
        return View(profile);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("Id,IdentityUserId,Name,Email,Phone")] FacultyProfile profile)
    {
        if (id != profile.Id) return NotFound();
        if (!ModelState.IsValid)
        {
            ViewData["IdentityUserId"] = new SelectList(ctx.Users, "Id", "Email", profile.IdentityUserId);
            return View(profile);
        }
        ctx.Update(profile);
        await ctx.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id is null) return NotFound();
        var profile = await ctx.FacultyProfiles.FirstOrDefaultAsync(f => f.Id == id);
        return profile is null ? NotFound() : View(profile);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var profile = await ctx.FacultyProfiles.FindAsync(id);
        if (profile is null) return NotFound();
        ctx.FacultyProfiles.Remove(profile);
        await ctx.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    // Assign faculty to a course
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignCourse(int facultyProfileId, int courseId, bool isTutor)
    {
        var already = await ctx.FacultyCourseAssignments
            .AnyAsync(fa => fa.FacultyProfileId == facultyProfileId && fa.CourseId == courseId);
        if (!already)
        {
            ctx.FacultyCourseAssignments.Add(new FacultyCourseAssignment
            {
                FacultyProfileId = facultyProfileId,
                CourseId = courseId,
                IsTutor = isTutor
            });
            await ctx.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Details), new { id = facultyProfileId });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> RemoveCourseAssignment(int id)
    {
        var fa = await ctx.FacultyCourseAssignments.FindAsync(id);
        if (fa is not null)
        {
            int facultyId = fa.FacultyProfileId;
            ctx.FacultyCourseAssignments.Remove(fa);
            await ctx.SaveChangesAsync();
            return RedirectToAction(nameof(Details), new { id = facultyId });
        }
        return RedirectToAction(nameof(Index));
    }
}