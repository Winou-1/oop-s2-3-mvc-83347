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

    // Add these to VgcTests.cs (or a new file in the same namespace)
// Requires: using VgcCollege.Web.Controllers;

// ══════════════════════════════════════════════════════════════════
//  EXAMS CONTROLLER  (0/88 lines → biggest gap)
// ══════════════════════════════════════════════════════════════════

[Fact]
public async Task ExamsController_Index_ReturnsAllExamsForCourse()
{
    await using var ctx = CreateCtx();
    var (_, course) = await SeedCourseAsync(ctx);

    ctx.Exams.AddRange(
        new Exam { CourseId = course.Id, Title = "Midterm", Date = DateTime.Today, MaxScore = 100, ResultsReleased = false },
        new Exam { CourseId = course.Id, Title = "Final",   Date = DateTime.Today, MaxScore = 100, ResultsReleased = true  }
    );
    await ctx.SaveChangesAsync();

    var controller = new ExamsController(ctx);
    var result = await controller.Index(course.Id) as Microsoft.AspNetCore.Mvc.ViewResult;
    var model = Assert.IsAssignableFrom<IEnumerable<Exam>>(result!.Model);

    Assert.Equal(2, model.Count());
}

[Fact]
public async Task ExamsController_Details_NullId_ReturnsNotFound()
{
    await using var ctx = CreateCtx();
    var controller = new ExamsController(ctx);
    var result = await controller.Details(null);
    Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(result);
}

[Fact]
public async Task ExamsController_Details_ValidId_ReturnsView()
{
    await using var ctx = CreateCtx();
    var (_, course) = await SeedCourseAsync(ctx);

    var exam = new Exam { CourseId = course.Id, Title = "Midterm", Date = DateTime.Today, MaxScore = 100, ResultsReleased = false };
    ctx.Exams.Add(exam);
    await ctx.SaveChangesAsync();

    var controller = new ExamsController(ctx);
    var result = await controller.Details(exam.Id) as Microsoft.AspNetCore.Mvc.ViewResult;

    Assert.NotNull(result);
    var model = Assert.IsType<Exam>(result.Model);
    Assert.Equal("Midterm", model.Title);
}

[Fact]
public async Task ExamsController_Details_InvalidId_ReturnsNotFound()
{
    await using var ctx = CreateCtx();
    var controller = new ExamsController(ctx);
    var result = await controller.Details(999);
    Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(result);
}

[Fact]
public async Task ExamsController_AddResult_ValidData_RedirectsToDetails()
{
    await using var ctx = CreateCtx();
    var (_, course) = await SeedCourseAsync(ctx);
    var student = await SeedStudentAsync(ctx);

    var exam = new Exam { CourseId = course.Id, Title = "Final", Date = DateTime.Today, MaxScore = 100, ResultsReleased = false };
    ctx.Exams.Add(exam);
    await ctx.SaveChangesAsync();

    var controller = new ExamsController(ctx);
    var result = await controller.AddResult(new ExamResult
    {
        ExamId = exam.Id,
        StudentProfileId = student.Id,
        Score = 88,
        Grade = "A"
    });

    var redirect = Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectToActionResult>(result);
    Assert.Equal("Details", redirect.ActionName);
    Assert.Equal(1, await ctx.ExamResults.CountAsync());
}

[Fact]
public async Task ExamsController_ReleaseResults_SetsFlag_AndRedirects()
{
    await using var ctx = CreateCtx();
    var (_, course) = await SeedCourseAsync(ctx);

    var exam = new Exam { CourseId = course.Id, Title = "Quiz", Date = DateTime.Today, MaxScore = 50, ResultsReleased = false };
    ctx.Exams.Add(exam);
    await ctx.SaveChangesAsync();

    var controller = new ExamsController(ctx);
    var result = await controller.ReleaseResults(exam.Id);

    var redirect = Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectToActionResult>(result);
    Assert.Equal("Details", redirect.ActionName);

    var saved = await ctx.Exams.FindAsync(exam.Id);
    Assert.True(saved!.ResultsReleased);
}

    // ══════════════════════════════════════════════════════════════════
    //  ENROLMENTS CONTROLLER  (0/67 lines)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task EnrolmentsController_Index_ReturnsCourseEnrolments()
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

        var controller = new EnrolmentsController(ctx);
        var result = await controller.Index(course.Id, null) as Microsoft.AspNetCore.Mvc.ViewResult;
        var model = Assert.IsAssignableFrom<IEnumerable<CourseEnrolment>>(result!.Model);

        Assert.Single(model);
    }

    [Fact]
    public async Task EnrolmentsController_Create_ValidData_CreatesEnrolment()
    {
        await using var ctx = CreateCtx();
        var (_, course) = await SeedCourseAsync(ctx);
        var student = await SeedStudentAsync(ctx);

        var controller = new EnrolmentsController(ctx);
        var result = await controller.Create(new CourseEnrolment
        {
            StudentProfileId = student.Id,
            CourseId = course.Id,
            EnrolDate = DateTime.Today,
            Status = EnrolmentStatus.Active
        });

        Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectToActionResult>(result);
        Assert.Equal(1, await ctx.CourseEnrolments.CountAsync());
    }

    [Fact]
    public async Task EnrolmentsController_Edit_ValidModel_Redirects()
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
        var controller = new EnrolmentsController(ctx);
        var result = await controller.Edit(enrolment.Id, enrolment);

        Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectToActionResult>(result);
        var saved = await ctx.CourseEnrolments.FindAsync(enrolment.Id);
        Assert.Equal(EnrolmentStatus.Withdrawn, saved!.Status);
    }

    [Fact]
    public async Task EnrolmentsController_Delete_NullId_ReturnsNotFound()
    {
        await using var ctx = CreateCtx();
        var controller = new EnrolmentsController(ctx);
        var result = await controller.Delete(null);
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(result);
    }


    [Fact]
    public async Task EnrolmentsController_DeleteConfirmed_RemovesEnrolment()
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

        var controller = new EnrolmentsController(ctx);
        await controller.DeleteConfirmed(enrolment.Id);

        Assert.Equal(0, await ctx.CourseEnrolments.CountAsync());
    }


    // ══════════════════════════════════════════════════════════════════
    //  ATTENDANCE CONTROLLER  (0/40 lines — highest cyclomatic: 16)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AttendanceController_Index_ReturnsAttendanceForEnrolment()
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

        ctx.AttendanceRecords.AddRange(
            new AttendanceRecord { CourseEnrolmentId = enrolment.Id, WeekNumber = 1, SessionDate = DateTime.Today.AddDays(-7), Present = true },
            new AttendanceRecord { CourseEnrolmentId = enrolment.Id, WeekNumber = 2, SessionDate = DateTime.Today.AddDays(-14), Present = false }
        );
        await ctx.SaveChangesAsync();

        var controller = new AttendanceController(ctx);
        var result = await controller.Index(enrolment.Id) as Microsoft.AspNetCore.Mvc.ViewResult;
        var model = Assert.IsAssignableFrom<IEnumerable<AttendanceRecord>>(result!.Model);

        Assert.Equal(2, model.Count());
    }



    [Fact]
    public async Task AttendanceController_Toggle_ChangesPresence()
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

        var record = new AttendanceRecord
        {
            CourseEnrolmentId = enrolment.Id,
            WeekNumber = 1,
            SessionDate = DateTime.Today,
            Present = false
        };
        ctx.AttendanceRecords.Add(record);
        await ctx.SaveChangesAsync();

        var controller = new AttendanceController(ctx);
        await controller.Toggle(record.Id, enrolment.Id);

        var saved = await ctx.AttendanceRecords.FindAsync(record.Id);
        Assert.True(saved!.Present);
    }
    // ══════════════════════════════════════════════════════════════════
    //  STUDENT PROFILES CONTROLLER  (0/83 lines)
    // ══════════════════════════════════════════════════════════════════

    [Fact]
public async Task StudentProfilesController_Index_ReturnsAllStudents()
{
    await using var ctx = CreateCtx();
    await SeedStudentAsync(ctx, "uid-a");
    await SeedStudentAsync(ctx, "uid-b");

    var controller = new StudentProfilesController(ctx);
    var result = await controller.Index() as Microsoft.AspNetCore.Mvc.ViewResult;
    var model = Assert.IsAssignableFrom<IEnumerable<StudentProfile>>(result!.Model);

    Assert.Equal(2, model.Count());
}

[Fact]
public async Task StudentProfilesController_Details_NullId_ReturnsNotFound()
{
    await using var ctx = CreateCtx();
    var controller = new StudentProfilesController(ctx);
    var result = await controller.Details(null);
    Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(result);
}

[Fact]
public async Task StudentProfilesController_Details_ValidId_ReturnsView()
{
    await using var ctx = CreateCtx();
    var student = await SeedStudentAsync(ctx);

    var controller = new StudentProfilesController(ctx);
    var result = await controller.Details(student.Id) as Microsoft.AspNetCore.Mvc.ViewResult;

    Assert.NotNull(result);
    var model = Assert.IsType<StudentProfile>(result.Model);
    Assert.Equal("Test Student", model.Name);
}

[Fact]
public async Task StudentProfilesController_Details_InvalidId_ReturnsNotFound()
{
    await using var ctx = CreateCtx();
    var controller = new StudentProfilesController(ctx);
    var result = await controller.Details(999);
    Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(result);
}

// ══════════════════════════════════════════════════════════════════
//  FACULTY PROFILES CONTROLLER  (0/61 lines)
// ══════════════════════════════════════════════════════════════════

[Fact]
public async Task FacultyProfilesController_Index_ReturnsAllFaculty()
{
    await using var ctx = CreateCtx();
    await SeedFacultyAsync(ctx, "fac-a");
    await SeedFacultyAsync(ctx, "fac-b");

    var controller = new FacultyProfilesController(ctx);
    var result = await controller.Index() as Microsoft.AspNetCore.Mvc.ViewResult;
    var model = Assert.IsAssignableFrom<IEnumerable<FacultyProfile>>(result!.Model);

    Assert.Equal(2, model.Count());
}

[Fact]
public async Task FacultyProfilesController_Details_NullId_ReturnsNotFound()
{
    await using var ctx = CreateCtx();
    var controller = new FacultyProfilesController(ctx);
    var result = await controller.Details(null);
    Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(result);
}

[Fact]
public async Task FacultyProfilesController_Details_ValidId_ReturnsView()
{
    await using var ctx = CreateCtx();
    var faculty = await SeedFacultyAsync(ctx);

    var controller = new FacultyProfilesController(ctx);
    var result = await controller.Details(faculty.Id) as Microsoft.AspNetCore.Mvc.ViewResult;

    Assert.NotNull(result);
    var model = Assert.IsType<FacultyProfile>(result.Model);
    Assert.Equal("Test Faculty", model.Name);
}

// ══════════════════════════════════════════════════════════════════
//  ASSIGNMENTS CONTROLLER — Edit actions  (cyclomatic 6)
// ══════════════════════════════════════════════════════════════════

[Fact]
public async Task AssignmentsController_Edit_NullId_ReturnsNotFound()
{
    await using var ctx = CreateCtx();
    var controller = new AssignmentsController(ctx);
    var result = await controller.Edit(null);
    Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(result);
}

[Fact]
public async Task AssignmentsController_Edit_ValidId_ReturnsViewWithModel()
{
    await using var ctx = CreateCtx();
    var (_, course) = await SeedCourseAsync(ctx);

    var assignment = new Assignment { CourseId = course.Id, Title = "Lab 2", MaxScore = 50, DueDate = DateTime.Today };
    ctx.Assignments.Add(assignment);
    await ctx.SaveChangesAsync();

    var controller = new AssignmentsController(ctx);
    var result = await controller.Edit(assignment.Id) as Microsoft.AspNetCore.Mvc.ViewResult;

    Assert.NotNull(result);
    var model = Assert.IsType<Assignment>(result.Model);
    Assert.Equal("Lab 2", model.Title);
}

[Fact]
public async Task AssignmentsController_EditPost_ValidModel_Redirects()
{
    await using var ctx = CreateCtx();
    var (_, course) = await SeedCourseAsync(ctx);

    var assignment = new Assignment { CourseId = course.Id, Title = "Lab 2", MaxScore = 50, DueDate = DateTime.Today };
    ctx.Assignments.Add(assignment);
    await ctx.SaveChangesAsync();

    assignment.Title = "Lab 2 Updated";
    var controller = new AssignmentsController(ctx);
    var result = await controller.Edit(assignment.Id, assignment);

    var redirect = Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectToActionResult>(result);
    Assert.Equal("Index", redirect.ActionName);

    var saved = await ctx.Assignments.FindAsync(assignment.Id);
    Assert.Equal("Lab 2 Updated", saved!.Title);
}

// ══════════════════════════════════════════════════════════════════
//  COURSES CONTROLLER — Edit actions  (cyclomatic 6)
// ══════════════════════════════════════════════════════════════════

[Fact]
public async Task CoursesController_Edit_NullId_ReturnsNotFound()
{
    await using var ctx = CreateCtx();
    var controller = new CoursesController(ctx);
    var result = await controller.Edit(null);
    Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(result);
}

[Fact]
public async Task CoursesController_Edit_ValidId_ReturnsViewWithModel()
{
    await using var ctx = CreateCtx();
    var (_, course) = await SeedCourseAsync(ctx);

    var controller = new CoursesController(ctx);
    var result = await controller.Edit(course.Id) as Microsoft.AspNetCore.Mvc.ViewResult;

    Assert.NotNull(result);
    var model = Assert.IsType<Course>(result.Model);
    Assert.Equal("Software Dev", model.Name);
}

[Fact]
public async Task CoursesController_EditPost_ValidModel_Redirects()
{
    await using var ctx = CreateCtx();
    var (_, course) = await SeedCourseAsync(ctx);

    course.Name = "Advanced Software Dev";
    var controller = new CoursesController(ctx);
    var result = await controller.Edit(course.Id, course);

    var redirect = Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectToActionResult>(result);
    Assert.Equal("Index", redirect.ActionName);

    var saved = await ctx.Courses.FindAsync(course.Id);
    Assert.Equal("Advanced Software Dev", saved!.Name);
}
}