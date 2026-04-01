using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using VgcCollege.Web.Data;
using VgcCollege.Web.Domain;

namespace VgcCollege.Web.Controllers;

[Authorize]
public class ExamsController(ApplicationDbContext ctx) : Controller
{
    public async Task<IActionResult> Index(int? courseId)
    {
        var uid = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
        IQueryable<Exam> query = ctx.Exams
            .Include(e => e.Course).ThenInclude(c => c.Branch)
            .Include(e => e.Results);

        // Students only see released exams (or ones they're enrolled in)
        if (User.IsInRole("Student"))
        {
            var student = await ctx.StudentProfiles
                .FirstOrDefaultAsync(s => s.IdentityUserId == uid);
            if (student is not null)
            {
                var enrolledCourseIds = await ctx.CourseEnrolments
                    .Where(ce => ce.StudentProfileId == student.Id)
                    .Select(ce => ce.CourseId).ToListAsync();
                query = query.Where(e => enrolledCourseIds.Contains(e.CourseId));
            }
        }

        if (courseId.HasValue) query = query.Where(e => e.CourseId == courseId);

        return View(await query.OrderByDescending(e => e.Date).ToListAsync());
    }

    public async Task<IActionResult> Details(int? id)
    {
        if (id is null) return NotFound();
        var exam = await ctx.Exams
            .Include(e => e.Course)
            .Include(e => e.Results).ThenInclude(r => r.StudentProfile)
            .FirstOrDefaultAsync(e => e.Id == id);
        if (exam is null) return NotFound();

        // Students: only released
        if (User.IsInRole("Student"))
        {
            var uid = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var student = await ctx.StudentProfiles.FirstOrDefaultAsync(s => s.IdentityUserId == uid);
            // Only show their own result, only if released
            ViewBag.StudentResult = exam.ResultsReleased && student is not null
                ? exam.Results.FirstOrDefault(r => r.StudentProfileId == student.Id)
                : null;
            ViewBag.IsStudent = true;
        }
        return View(exam);
    }

    [Authorize(Roles = "Admin")]
    public IActionResult Create(int? courseId)
    {
        ViewData["CourseId"] = new SelectList(ctx.Courses, "Id", "Name", courseId);
        return View(new Exam { Date = DateTime.Today.AddMonths(1) });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
    public async Task<IActionResult> Create([Bind("CourseId,Title,Date,MaxScore,ResultsReleased")] Exam exam)
    {
        if (!ModelState.IsValid)
        {
            ViewData["CourseId"] = new SelectList(ctx.Courses, "Id", "Name", exam.CourseId);
            return View(exam);
        }
        ctx.Exams.Add(exam);
        await ctx.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Edit(int? id)
    {
        if (id is null) return NotFound();
        var exam = await ctx.Exams.FindAsync(id);
        if (exam is null) return NotFound();
        ViewData["CourseId"] = new SelectList(ctx.Courses, "Id", "Name", exam.CourseId);
        return View(exam);
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
    public async Task<IActionResult> Edit(int id, [Bind("Id,CourseId,Title,Date,MaxScore,ResultsReleased")] Exam exam)
    {
        if (id != exam.Id) return NotFound();
        if (!ModelState.IsValid)
        {
            ViewData["CourseId"] = new SelectList(ctx.Courses, "Id", "Name", exam.CourseId);
            return View(exam);
        }
        ctx.Update(exam);
        await ctx.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    // ── Release results (Admin only) ─────────────────────────────────────
    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
    public async Task<IActionResult> ReleaseResults(int id)
    {
        var exam = await ctx.Exams.FindAsync(id);
        if (exam is null) return NotFound();
        exam.ResultsReleased = true;
        await ctx.SaveChangesAsync();
        TempData["Success"] = "Exam results have been released to students.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int? id)
    {
        if (id is null) return NotFound();
        var exam = await ctx.Exams.Include(e => e.Course).FirstOrDefaultAsync(e => e.Id == id);
        return exam is null ? NotFound() : View(exam);
    }

    [HttpPost, ActionName("Delete"), ValidateAntiForgeryToken, Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var exam = await ctx.Exams.FindAsync(id);
        if (exam is null) return NotFound();
        ctx.Exams.Remove(exam);
        await ctx.SaveChangesAsync();
        return RedirectToAction(nameof(Index));
    }

    // ── Add exam result (Admin + Faculty) ─────────────────────────────────
    [Authorize(Roles = "Admin,Faculty")]
    public IActionResult AddResult(int examId)
    {
        ViewData["ExamId"] = examId;
        ViewData["StudentProfileId"] = new SelectList(ctx.StudentProfiles.OrderBy(s => s.Name), "Id", "Name");
        return View(new ExamResult { ExamId = examId });
    }

    [HttpPost, ValidateAntiForgeryToken, Authorize(Roles = "Admin,Faculty")]
    public async Task<IActionResult> AddResult([Bind("ExamId,StudentProfileId,Score,Grade")] ExamResult result)
    {
        var exam = await ctx.Exams.FindAsync(result.ExamId);
        if (exam is not null && result.Score > exam.MaxScore)
            ModelState.AddModelError("Score", $"Score cannot exceed {exam.MaxScore}.");

        if (!ModelState.IsValid)
        {
            ViewData["ExamId"] = result.ExamId;
            ViewData["StudentProfileId"] = new SelectList(ctx.StudentProfiles.OrderBy(s => s.Name), "Id", "Name", result.StudentProfileId);
            return View(result);
        }
        ctx.ExamResults.Add(result);
        await ctx.SaveChangesAsync();
        return RedirectToAction(nameof(Details), new { id = result.ExamId });
    }
}