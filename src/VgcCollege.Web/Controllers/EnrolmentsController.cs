using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using VgcCollege.Web.Data;
using VgcCollege.Web.Domain;

namespace VgcCollege.Web.Controllers;

[Authorize]
public class EnrolmentsController(ApplicationDbContext ctx) : Controller
{
    public async Task<IActionResult> Index(int? courseId, int? studentId)
    {
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        IQueryable<CourseEnrolment> query = ctx.CourseEnrolments
            .Include(e => e.StudentProfile)
            .Include(e => e.Course).ThenInclude(c => c.Branch);

        if (User.IsInRole("Student"))
        {
            var student = await ctx.StudentProfiles.FirstOrDefaultAsync(s => s.IdentityUserId == uid);
            if (student is null) return View(new List<CourseEnrolment>());
            query = query.Where(e => e.StudentProfileId == student.Id);
        }
        else if (User.IsInRole("Faculty"))
        {
            var faculty = await ctx.FacultyProfiles
                .Include(f => f.CourseAssignments)
                .FirstOrDefaultAsync(f => f.IdentityUserId == uid);
            if (faculty is null) return View(new List<CourseEnrolment>());
            var cids = faculty.CourseAssignments.Select(fa => fa.CourseId).ToList();
            query = query.Where(e => cids.Contains(e.CourseId));
        }

        if (courseId.HasValue) query = query.Where(e => e.CourseId == courseId);
        if (studentId.HasValue && !User.IsInRole("Student")) query = query.Where(e => e.StudentProfileId == studentId);

        return View(await query.OrderBy(e => e.Course.Name).ToListAsync());
    }

    [Authorize(Roles = "Admin")]
    public IActionResult Create()
    {
        ViewData["StudentProfileId"] = new SelectList(ctx.StudentProfiles.OrderBy(s => s.Name), "Id", "Name");
        ViewData["CourseId"] = new SelectList(ctx.Courses.Include(c => c.Branch)
            .Select(c => new { c.Id, Display = c.Name + " (" + c.Branch.Name + ")" }), "Id", "Display");
        return View(new CourseEnrolment { EnrolDate = DateTime.Today, Status = EnrolmentStatus.Active });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([Bind("StudentProfileId,CourseId,EnrolDate,Status")] CourseEnrolment enrolment)
    {
        // Prevent duplicate enrolment
        var duplicate = await ctx.CourseEnrolments
            .AnyAsync(e => e.StudentProfileId == enrolment.StudentProfileId
                        && e.CourseId == enrolment.CourseId);
        if (duplicate)
            ModelState.AddModelError("", "This student is already enrolled in that course.");

        if (!ModelState.IsValid)
        {
            ViewData["StudentProfileId"] = new SelectList(ctx.StudentProfiles.OrderBy(s => s.Name), "Id", "Name", enrolment.StudentProfileId);
            ViewData["CourseId"] = new SelectList(ctx.Courses, "Id", "Name", enrolment.CourseId);
            return View(enrolment);
        }
        ctx.CourseEnrolments.Add(enrolment);
        await ctx.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null) return NotFound();
        var e = await ctx.CourseEnrolments.FindAsync(id);
        if (e is null) return NotFound();
        ViewData["StudentProfileId"] = new SelectList(ctx.StudentProfiles.OrderBy(s => s.Name), "Id", "Name", e.StudentProfileId);
        ViewData["CourseId"] = new SelectList(ctx.Courses, "Id", "Name", e.CourseId);
        return View(e);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
    public async Task<IActionResult> Edit(int id, [Bind("Id,StudentProfileId,CourseId,EnrolDate,Status")] CourseEnrolment enrolment)
    {
        if (id != enrolment.Id) return NotFound();
        if (!ModelState.IsValid)
        {
            ViewData["StudentProfileId"] = new SelectList(ctx.StudentProfiles.OrderBy(s => s.Name), "Id", "Name", enrolment.StudentProfileId);
            ViewData["CourseId"] = new SelectList(ctx.Courses, "Id", "Name", enrolment.CourseId);
            return View(enrolment);
        }
        ctx.Update(enrolment);
        await ctx.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int? id)
    {
        if (id is null) return NotFound();
        var e = await ctx.CourseEnrolments
            .Include(e => e.StudentProfile).Include(e => e.Course)
            .FirstOrDefaultAsync(e => e.Id == id);
        return e is null ? NotFound() : View(e);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var e = await ctx.CourseEnrolments.FindAsync(id);
        if (e is null) return NotFound();
        ctx.CourseEnrolments.Remove(e);
        await ctx.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}