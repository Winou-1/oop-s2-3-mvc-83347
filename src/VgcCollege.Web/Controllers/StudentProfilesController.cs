using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using VgcCollege.Web.Data;
using VgcCollege.Web.Domain;

namespace VgcCollege.Web.Controllers;

[Authorize]
public class StudentProfilesController(ApplicationDbContext ctx) : Controller
{
    // ── Index: Admin sees all; Faculty sees their students ─────────────────
    [Authorize(Roles = "Admin,Faculty")]
    public async Task<IActionResult> Index()
    {
        IQueryable<StudentProfile> query = ctx.StudentProfiles;

        if (User.IsInRole("Faculty"))
        {
            var uid = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var faculty = await ctx.FacultyProfiles
                .Include(f => f.CourseAssignments)
                .FirstOrDefaultAsync(f => f.IdentityUserId == uid);

            if (faculty is null) return Forbid();

            var courseIds = faculty.CourseAssignments.Select(fa => fa.CourseId).ToList();
            var studentIds = await ctx.CourseEnrolments
                .Where(ce => courseIds.Contains(ce.CourseId))
                .Select(ce => ce.StudentProfileId).Distinct().ToListAsync();

            query = query.Where(s => studentIds.Contains(s.Id));
        }

        return View(await query.OrderBy(s => s.Name).ToListAsync());
    }

    // ── Details: Admin full; Faculty only their students; Student only self ─
    public async Task<IActionResult> Details(int? id)
    {
        if (id is null) return NotFound();

        var student = await ctx.StudentProfiles
            .Include(s => s.Enrolments).ThenInclude(e => e.Course)
            .Include(s => s.AssignmentResults).ThenInclude(ar => ar.Assignment)
            .FirstOrDefaultAsync(s => s.Id == id);

        if (student is null) return NotFound();

        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier)!;

        if (User.IsInRole("Student"))
        {
            // student can only view their own profile
            if (student.IdentityUserId != uid) return Forbid();
        }
        else if (User.IsInRole("Faculty"))
        {
            var faculty = await ctx.FacultyProfiles
                .Include(f => f.CourseAssignments)
                .FirstOrDefaultAsync(f => f.IdentityUserId == uid);
            if (faculty is null) return Forbid();

            var courseIds = faculty.CourseAssignments.Select(fa => fa.CourseId).ToList();
            var enrolled = await ctx.CourseEnrolments
                .AnyAsync(ce => courseIds.Contains(ce.CourseId) && ce.StudentProfileId == id);
            if (!enrolled) return Forbid();
        }

        return View(student);
    }

    // ── Student: My Profile ───────────────────────────────────────────────
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> MyProfile()
    {
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        var student = await ctx.StudentProfiles
            .Include(s => s.Enrolments).ThenInclude(e => e.Course)
            .Include(s => s.AssignmentResults).ThenInclude(ar => ar.Assignment)
            .Include(s => s.ExamResults).ThenInclude(er => er.Exam)
            .FirstOrDefaultAsync(s => s.IdentityUserId == uid);

        if (student is null)
            return RedirectToAction("Create"); // first time – no profile yet

        // Filter out unreleased exam results
        var releasedExamResults = student.ExamResults
            .Where(er => er.Exam.ResultsReleased).ToList();
        ViewBag.ReleasedExamResults = releasedExamResults;

        return View(student);
    }

    // ── Admin: Create / Edit / Delete ─────────────────────────────────────
    [Authorize(Roles = "Admin")]
    public IActionResult Create()
    {
        ViewData["IdentityUserId"] = new SelectList(ctx.Users, "Id", "Email");
        return View();
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create(
        [Bind("IdentityUserId,Name,Email,Phone,Address,DateOfBirth,StudentNumber")] StudentProfile profile)
    {
        if (!ModelState.IsValid)
        {
            ViewData["IdentityUserId"] = new SelectList(ctx.Users, "Id", "Email", profile.IdentityUserId);
            return View(profile);
        }
        ctx.StudentProfiles.Add(profile);
        await ctx.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null) return NotFound();
        var profile = await ctx.StudentProfiles.FindAsync(id);
        if (profile is null) return NotFound();
        ViewData["IdentityUserId"] = new SelectList(ctx.Users, "Id", "Email", profile.IdentityUserId);
        return View(profile);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
    public async Task<IActionResult> Edit(int id,
        [Bind("Id,IdentityUserId,Name,Email,Phone,Address,DateOfBirth,StudentNumber")] StudentProfile profile)
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

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int? id)
    {
        if (id is null) return NotFound();
        var profile = await ctx.StudentProfiles.FirstOrDefaultAsync(s => s.Id == id);
        return profile is null ? NotFound() : View(profile);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var profile = await ctx.StudentProfiles.FindAsync(id);
        if (profile is null) return NotFound();
        ctx.StudentProfiles.Remove(profile);
        await ctx.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }
}