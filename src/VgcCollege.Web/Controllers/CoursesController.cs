using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using VgcCollege.Web.Data;
using VgcCollege.Web.Domain;

namespace VgcCollege.Web.Controllers
{
    [Authorize]
    public class CoursesController : Controller
    {
        private readonly ApplicationDbContext _context;

        public CoursesController(ApplicationDbContext context)
        {
            _context = context;
        }

        // GET: Courses — accessible to all authenticated users
        public async Task<IActionResult> Index()
        {
            var courses = await _context.Courses
                .Include(c => c.Branch)
                .Include(c => c.Enrolments)
                .Include(c => c.FacultyAssignments).ThenInclude(fa => fa.FacultyProfile)
                .OrderBy(c => c.Name)
                .ToListAsync();
            return View(courses);
        }

        // GET: Courses/Details/5 — enriched view for all roles
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var course = await _context.Courses
                .Include(c => c.Branch)
                .Include(c => c.FacultyAssignments).ThenInclude(fa => fa.FacultyProfile)
                .Include(c => c.Enrolments).ThenInclude(e => e.StudentProfile)
                .Include(c => c.Assignments).ThenInclude(a => a.Results)
                .Include(c => c.Exams).ThenInclude(e => e.Results)
                .FirstOrDefaultAsync(m => m.Id == id);

            if (course == null) return NotFound();

            return View(course);
        }

        // GET: Courses/Create — Admin only
        [Authorize(Roles = "Admin")]
        public IActionResult Create()
        {
            ViewData["BranchId"] = new SelectList(_context.Branches, "Id", "Name");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Create([Bind("Id,Name,BranchId,StartDate,EndDate")] Course course)
        {
            if (ModelState.IsValid)
            {
                _context.Add(course);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            ViewData["BranchId"] = new SelectList(_context.Branches, "Id", "Name", course.BranchId);
            return View(course);
        }

        // GET: Courses/Edit/5 — Admin only
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var course = await _context.Courses.FindAsync(id);
            if (course == null) return NotFound();
            ViewData["BranchId"] = new SelectList(_context.Branches, "Id", "Name", course.BranchId);
            return View(course);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Name,BranchId,StartDate,EndDate")] Course course)
        {
            if (id != course.Id) return NotFound();
            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(course);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!CourseExists(course.Id)) return NotFound();
                    else throw;
                }
                return RedirectToAction(nameof(Index));
            }
            ViewData["BranchId"] = new SelectList(_context.Branches, "Id", "Name", course.BranchId);
            return View(course);
        }

        // GET: Courses/Delete/5 — Admin only
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();
            var course = await _context.Courses
                .Include(c => c.Branch)
                .Include(c => c.Enrolments)
                .FirstOrDefaultAsync(m => m.Id == id);
            if (course == null) return NotFound();
            return View(course);
        }

        // POST: Courses/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var course = await _context.Courses.FindAsync(id);
            if (course == null) return RedirectToAction(nameof(Index));

            var enrolments = await _context.CourseEnrolments
                .Where(e => e.CourseId == id)
                .ToListAsync();
            _context.CourseEnrolments.RemoveRange(enrolments);

            _context.Courses.Remove(course);
            await _context.SaveChangesAsync();
            TempData["Success"] = "Course deleted successfully.";
            return RedirectToAction(nameof(Index));
        }

        private bool CourseExists(int id) =>
            _context.Courses.Any(e => e.Id == id);
    }
}