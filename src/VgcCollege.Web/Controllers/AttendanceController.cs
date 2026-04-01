using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using VgcCollege.Web.Data;
using VgcCollege.Web.Domain;

namespace VgcCollege.Web.Controllers;

[Authorize]
public class AttendanceController(ApplicationDbContext ctx) : Controller
{
    public async Task<IActionResult> Index(int? enrolmentId)
    {
        if (enrolmentId is null) return RedirectToAction("Index", "Enrolments");

        var enrolment = await ctx.CourseEnrolments
            .Include(e => e.StudentProfile)
            .Include(e => e.Course)
            .Include(e => e.AttendanceRecords.OrderBy(a => a.WeekNumber))
            .FirstOrDefaultAsync(e => e.Id == enrolmentId);

        if (enrolment is null) return NotFound();

        // Authorization check
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        if (User.IsInRole("Student") && enrolment.StudentProfile.IdentityUserId != uid)
            return Forbid();

        if (User.IsInRole("Faculty"))
        {
            var faculty = await ctx.FacultyProfiles
                .Include(f => f.CourseAssignments)
                .FirstOrDefaultAsync(f => f.IdentityUserId == uid);
            if (faculty is null || !faculty.CourseAssignments.Any(fa => fa.CourseId == enrolment.CourseId))
                return Forbid();
        }

        ViewBag.EnrolmentId = enrolmentId;
        return View(enrolment);
    }

    [Authorize(Roles = "Admin,Faculty")]
    public IActionResult Create(int enrolmentId)
    {
        ViewBag.EnrolmentId = enrolmentId;
        return View(new AttendanceRecord
        {
            CourseEnrolmentId = enrolmentId,
            SessionDate = DateTime.Today,
            WeekNumber = 1
        });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin,Faculty")]
    public async Task<IActionResult> Create(
        [Bind("CourseEnrolmentId,WeekNumber,SessionDate,Present")] AttendanceRecord record)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.EnrolmentId = record.CourseEnrolmentId;
            return View(record);
        }
        ctx.AttendanceRecords.Add(record);
        await ctx.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { enrolmentId = record.CourseEnrolmentId });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin,Faculty")]
    public async Task<IActionResult> Toggle(int id, int enrolmentId)
    {
        var record = await ctx.AttendanceRecords.FindAsync(id);
        if (record is null) return NotFound();
        record.Present = !record.Present;
        await ctx.SaveChangesAsync();
        return RedirectToAction(nameof(Index), new { enrolmentId });
    }
}