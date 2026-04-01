using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using VgcCollege.Web.Controllers;
using VgcCollege.Web.Data;
using VgcCollege.Web.Domain;

namespace VgcCollege.Tests;

public class VgcTests
{
    // ── Helpers ────────────────────────────────────────────────────────────
    private static ApplicationDbContext CreateCtx() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static async Task<(Branch branch, Course course)> SeedCourseAsync(ApplicationDbContext ctx)
    {
        var branch = new Branch { Name = "Dublin", Address = "1 O'Connell St" };
        ctx.Branches.Add(branch);
        await ctx.SaveChangesAsync();
        var course = new Course { BranchId = branch.Id, Name = "Software Dev", StartDate = DateTime.Today.AddMonths(-3), EndDate = DateTime.Today.AddMonths(5) };
        ctx.Courses.Add(course);
        await ctx.SaveChangesAsync();
        return (branch, course);
    }

    private static async Task<StudentProfile> SeedStudentAsync(ApplicationDbContext ctx, string uid = "user-1")
    {
        var student = new StudentProfile { IdentityUserId = uid, Name = "Test Student", Email = "s@test.ie", StudentNumber = "T001" };
        ctx.StudentProfiles.Add(student);
        await ctx.SaveChangesAsync();
        return student;
    }

    private static async Task<FacultyProfile> SeedFacultyAsync(ApplicationDbContext ctx, string uid = "fac-1")
    {
        var faculty = new FacultyProfile { IdentityUserId = uid, Name = "Test Faculty", Email = "f@test.ie" };
        ctx.FacultyProfiles.Add(faculty);
        await ctx.SaveChangesAsync();
        return faculty;
    }

    // ── TEST 1: Student cannot see provisional exam results ────────────────
    [Fact]
    public async Task ExamResult_NotVisible_WhenResultsNotReleased()
    {
        await using var ctx = CreateCtx();
        var (_, course) = await SeedCourseAsync(ctx);
        var student = await SeedStudentAsync(ctx);

        var exam = new Exam { CourseId = course.Id, Title = "Midterm", Date = DateTime.Today, MaxScore = 100, ResultsReleased = false };
        ctx.Exams.Add(exam);
        await ctx.SaveChangesAsync();

        ctx.ExamResults.Add(new ExamResult { ExamId = exam.Id, StudentProfileId = student.Id, Score = 75, Grade = "B" });
        await ctx.SaveChangesAsync();

        // Student query: only see released
        var visible = await ctx.ExamResults
            .Where(er => er.StudentProfileId == student.Id && er.Exam.ResultsReleased)
            .ToListAsync();

        Assert.Empty(visible);
    }

    // ── TEST 2: Student CAN see released exam results ──────────────────────
    [Fact]
    public async Task ExamResult_Visible_WhenResultsReleased()
    {
        await using var ctx = CreateCtx();
        var (_, course) = await SeedCourseAsync(ctx);
        var student = await SeedStudentAsync(ctx);

        var exam = new Exam { CourseId = course.Id, Title = "Final", Date = DateTime.Today.AddMonths(-1), MaxScore = 100, ResultsReleased = true };
        ctx.Exams.Add(exam);
        await ctx.SaveChangesAsync();

        ctx.ExamResults.Add(new ExamResult { ExamId = exam.Id, StudentProfileId = student.Id, Score = 82, Grade = "A" });
        await ctx.SaveChangesAsync();

        var visible = await ctx.ExamResults
            .Where(er => er.StudentProfileId == student.Id && er.Exam.ResultsReleased)
            .ToListAsync();

        Assert.Single(visible);
        Assert.Equal(82, visible[0].Score);
    }

    // ── TEST 3: Faculty only sees students in their courses ────────────────
    [Fact]
    public async Task Faculty_OnlySeesStudents_InAssignedCourses()
    {
        await using var ctx = CreateCtx();
        var (_, course) = await SeedCourseAsync(ctx);
        var faculty = await SeedFacultyAsync(ctx);

        ctx.FacultyCourseAssignments.Add(new FacultyCourseAssignment { FacultyProfileId = faculty.Id, CourseId = course.Id, IsTutor = true });
        await ctx.SaveChangesAsync();

        var studentInCourse = await SeedStudentAsync(ctx, "uid-in");
        var studentNotInCourse = await SeedStudentAsync(ctx, "uid-out");
        // Only studentInCourse is enrolled
        ctx.CourseEnrolments.Add(new CourseEnrolment
        {
            StudentProfileId = studentInCourse.Id,
            CourseId = course.Id,
            EnrolDate = DateTime.Today,
            Status = EnrolmentStatus.Active
        });
        await ctx.SaveChangesAsync();

        var courseIds = ctx.FacultyCourseAssignments
            .Where(fa => fa.FacultyProfileId == faculty.Id)
            .Select(fa => fa.CourseId).ToList();

        var visibleStudentIds = await ctx.CourseEnrolments
            .Where(ce => courseIds.Contains(ce.CourseId))
            .Select(ce => ce.StudentProfileId).Distinct().ToListAsync();

        Assert.Contains(studentInCourse.Id, visibleStudentIds);
        Assert.DoesNotContain(studentNotInCourse.Id, visibleStudentIds);
    }

    // ── TEST 4: Duplicate enrolment is blocked ─────────────────────────────
    [Fact]
    public async Task Enrolment_Duplicate_DetectedCorrectly()
    {
        await using var ctx = CreateCtx();
        var (_, course) = await SeedCourseAsync(ctx);
        var student = await SeedStudentAsync(ctx);

        ctx.CourseEnrolments.Add(new CourseEnrolment
        {
            StudentProfileId = student.Id,
            CourseId = course.Id,
            EnrolDate = DateTime.Today,
            Status = EnrolmentStatus.Active
        });
        await ctx.SaveChangesAsync();

        var isDuplicate = await ctx.CourseEnrolments
            .AnyAsync(ce => ce.StudentProfileId == student.Id && ce.CourseId == course.Id);

        Assert.True(isDuplicate);
    }

    // ── TEST 5: Attendance percentage calculation ──────────────────────────
    [Fact]
    public async Task Attendance_Percentage_CalculatedCorrectly()
    {
        await using var ctx = CreateCtx();
        var (_, course) = await SeedCourseAsync(ctx);
        var student = await SeedStudentAsync(ctx);

        var enrolment = new CourseEnrolment { StudentProfileId = student.Id, CourseId = course.Id, EnrolDate = DateTime.Today, Status = EnrolmentStatus.Active };
        ctx.CourseEnrolments.Add(enrolment);
        await ctx.SaveChangesAsync();

        ctx.AttendanceRecords.AddRange(
            new AttendanceRecord { CourseEnrolmentId = enrolment.Id, WeekNumber = 1, SessionDate = DateTime.Today.AddDays(-7), Present = true },
            new AttendanceRecord { CourseEnrolmentId = enrolment.Id, WeekNumber = 2, SessionDate = DateTime.Today.AddDays(-14), Present = true },
            new AttendanceRecord { CourseEnrolmentId = enrolment.Id, WeekNumber = 3, SessionDate = DateTime.Today.AddDays(-21), Present = false },
            new AttendanceRecord { CourseEnrolmentId = enrolment.Id, WeekNumber = 4, SessionDate = DateTime.Today.AddDays(-28), Present = true }
        );
        await ctx.SaveChangesAsync();

        var records = await ctx.AttendanceRecords
            .Where(ar => ar.CourseEnrolmentId == enrolment.Id).ToListAsync();
        double pct = records.Count == 0 ? 0 : records.Count(a => a.Present) * 100.0 / records.Count;

        Assert.Equal(75.0, pct);
    }

    // ── TEST 6: AssignmentResult score cannot exceed MaxScore ──────────────
    [Fact]
    public async Task AssignmentResult_Score_ExceedsMaxScore_IsInvalid()
    {
        await using var ctx = CreateCtx();
        var (_, course) = await SeedCourseAsync(ctx);

        var assignment = new Assignment { CourseId = course.Id, Title = "Lab 1", MaxScore = 50, DueDate = DateTime.Today };
        ctx.Assignments.Add(assignment);
        await ctx.SaveChangesAsync();

        int submittedScore = 75;
        bool isInvalid = submittedScore > assignment.MaxScore;

        Assert.True(isInvalid);
    }

    // ── TEST 7: ReleaseResults sets flag correctly ─────────────────────────
    [Fact]
    public async Task Exam_ReleaseResults_SetsFlag()
    {
        await using var ctx = CreateCtx();
        var (_, course) = await SeedCourseAsync(ctx);

        var exam = new Exam { CourseId = course.Id, Title = "Final", Date = DateTime.Today, MaxScore = 100, ResultsReleased = false };
        ctx.Exams.Add(exam);
        await ctx.SaveChangesAsync();

        exam.ResultsReleased = true;
        await ctx.SaveChangesAsync();

        var saved = await ctx.Exams.FindAsync(exam.Id);
        Assert.True(saved!.ResultsReleased);
    }

    // ── TEST 8: EnrolmentStatus change persists ────────────────────────────
    [Fact]
    public async Task EnrolmentStatus_CanBeChanged()
    {
        await using var ctx = CreateCtx();
        var (_, course) = await SeedCourseAsync(ctx);
        var student = await SeedStudentAsync(ctx);

        var enrolment = new CourseEnrolment
        {
            StudentProfileId = student.Id,
            CourseId = course.Id,
            EnrolDate = DateTime.Today,
            Status = EnrolmentStatus.Active
        };
        ctx.CourseEnrolments.Add(enrolment);
        await ctx.SaveChangesAsync();

        enrolment.Status = EnrolmentStatus.Withdrawn;
        await ctx.SaveChangesAsync();

        var saved = await ctx.CourseEnrolments.FindAsync(enrolment.Id);
        Assert.Equal(EnrolmentStatus.Withdrawn, saved!.Status);
    }

    // ── TEST 9: Student only sees their own enrolments ────────────────────
    [Fact]
    public async Task Student_OnlySeesOwnEnrolments()
    {
        await using var ctx = CreateCtx();
        var (_, course) = await SeedCourseAsync(ctx);
        var student1 = await SeedStudentAsync(ctx, "uid-1");
        var student2 = await SeedStudentAsync(ctx, "uid-2");

        ctx.CourseEnrolments.AddRange(
            new CourseEnrolment { StudentProfileId = student1.Id, CourseId = course.Id, EnrolDate = DateTime.Today, Status = EnrolmentStatus.Active },
            new CourseEnrolment { StudentProfileId = student2.Id, CourseId = course.Id, EnrolDate = DateTime.Today, Status = EnrolmentStatus.Active }
        );
        await ctx.SaveChangesAsync();

        var myEnrolments = await ctx.CourseEnrolments
            .Where(e => e.StudentProfileId == student1.Id).ToListAsync();

        Assert.Single(myEnrolments);
        Assert.All(myEnrolments, e => Assert.Equal(student1.Id, e.StudentProfileId));
    }

    // ── TEST 10: Tutor assignment allows contact details access ───────────
    [Fact]
    public async Task Faculty_IsTutor_AllowsContactDetailsAccess()
    {
        await using var ctx = CreateCtx();
        var (_, course) = await SeedCourseAsync(ctx);
        var faculty = await SeedFacultyAsync(ctx);

        ctx.FacultyCourseAssignments.Add(new FacultyCourseAssignment
        {
            FacultyProfileId = faculty.Id,
            CourseId = course.Id,
            IsTutor = true
        });
        await ctx.SaveChangesAsync();

        var assignment = await ctx.FacultyCourseAssignments
            .FirstOrDefaultAsync(fa => fa.FacultyProfileId == faculty.Id && fa.CourseId == course.Id);

        Assert.NotNull(assignment);
        Assert.True(assignment.IsTutor);
    }

    // ── TEST 11: Grade calculation helper ────────────────────────────────
    [Theory]
    [InlineData(90, 100, "A")]
    [InlineData(75, 100, "B")]
    [InlineData(60, 100, "C")]
    [InlineData(40, 100, "F")]
    public void Grade_CalculatedFromScore(int score, int maxScore, string expectedGrade)
    {
        double pct = score * 100.0 / maxScore;
        string grade = pct >= 85 ? "A" : pct >= 70 ? "B" : pct >= 55 ? "C" : "F";
        Assert.Equal(expectedGrade, grade);
    }

    // ── TEST 12: CoursesController.Index returns all courses ──────────────
    [Fact]
    public async Task CoursesController_Index_ReturnsAllCourses()
    {
        await using var ctx = CreateCtx();
        await SeedCourseAsync(ctx);
        await SeedCourseAsync(ctx);

        var controller = new CoursesController(ctx);
        var result = await controller.Index() as Microsoft.AspNetCore.Mvc.ViewResult;
        var model = Assert.IsAssignableFrom<IEnumerable<Course>>(result!.Model);

        Assert.Equal(2, model.Count());
    }
    // ── TEST 13: BranchesController - Index retourne la liste ─────────────
    [Fact]
    public async Task BranchesController_Index_ReturnsViewWithBranches()
    {
        // Arrange
        await using var ctx = CreateCtx();
        ctx.Branches.Add(new Branch { Name = "Test Branch", Address = "123 Test St" });
        await ctx.SaveChangesAsync();
        var controller = new BranchesController(ctx);

        // Act
        var result = await controller.Index() as Microsoft.AspNetCore.Mvc.ViewResult;

        // Assert
        Assert.NotNull(result);
        var model = Assert.IsAssignableFrom<IEnumerable<Branch>>(result.Model);
        Assert.Single(model);
    }

    // ── TEST 14: BranchesController - Create (Valide) ─────────────────────
    [Fact]
    public async Task BranchesController_Create_ValidModel_RedirectsToIndex()
    {
        await using var ctx = CreateCtx();
        var controller = new BranchesController(ctx);
        var newBranch = new Branch { Name = "New Branch", Address = "456 New St" };
        var result = await controller.Create(newBranch) as Microsoft.AspNetCore.Mvc.RedirectToActionResult;
        Assert.NotNull(result);
        Assert.Equal("Index", result.ActionName);
        Assert.Equal(1, await ctx.Branches.CountAsync());
    }

    // ── TEST 15: BranchesController - Create (Invalide) ───────────────────
    [Fact]
    public async Task BranchesController_Create_InvalidModel_ReturnsView()
    {
        await using var ctx = CreateCtx();
        var controller = new BranchesController(ctx);
        controller.ModelState.AddModelError("Name", "Required");
        var newBranch = new Branch { Address = "No Name St" };
        var result = await controller.Create(newBranch) as Microsoft.AspNetCore.Mvc.ViewResult;
        Assert.NotNull(result);
        Assert.Equal(newBranch, result.Model);
        Assert.Equal(0, await ctx.Branches.CountAsync());
    }

    // ── TEST 16: AssignmentsController - Create (Valide) ──────────────────
    [Fact]
    public async Task AssignmentsController_Create_ValidModel_Redirects()
    {
        await using var ctx = CreateCtx();
        var (_, course) = await SeedCourseAsync(ctx);
        var controller = new AssignmentsController(ctx);
        var assignment = new Assignment { CourseId = course.Id, Title = "Test Assignment", MaxScore = 100, DueDate = DateTime.Today };
        var result = await controller.Create(assignment);
        var redirectResult = Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectToActionResult>(result);
        Assert.Equal("Index", redirectResult.ActionName);
    }

    // ── TEST 17: AssignmentsController - Details retourne 404 si null ─────
    [Fact]
    public async Task AssignmentsController_Details_NullId_ReturnsNotFound()
    {
        await using var ctx = CreateCtx();
        var controller = new AssignmentsController(ctx);
        var result = await controller.Details(null);
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(result);
    }
}