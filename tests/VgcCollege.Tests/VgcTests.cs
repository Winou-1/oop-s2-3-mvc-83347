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

    private static void SetFakeUser(Microsoft.AspNetCore.Mvc.ControllerBase controller, string userId = "test-user-id")
    {
        var claims = new List<System.Security.Claims.Claim>
        {
            new(System.Security.Claims.ClaimTypes.NameIdentifier, userId)
        };
        var identity = new System.Security.Claims.ClaimsIdentity(claims, "TestAuth");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);

        controller.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext
            {
                User = principal
            }
        };
    }

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
        await using var ctx = CreateCtx();
        ctx.Branches.Add(new Branch { Name = "Test Branch", Address = "123 Test St" });
        await ctx.SaveChangesAsync();
        var controller = new BranchesController(ctx);

        var result = await controller.Index() as Microsoft.AspNetCore.Mvc.ViewResult;

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

    // ══════════════════════════════════════════════════════════════════
    //  EXAMS CONTROLLER
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task ExamsController_Index_ReturnsAllExamsForCourse()
    {
        await using var ctx = CreateCtx();
        var (_, course) = await SeedCourseAsync(ctx);

        ctx.Exams.AddRange(
            new Exam { CourseId = course.Id, Title = "Midterm", Date = DateTime.Today, MaxScore = 100, ResultsReleased = false },
            new Exam { CourseId = course.Id, Title = "Final", Date = DateTime.Today, MaxScore = 100, ResultsReleased = true }
        );
        await ctx.SaveChangesAsync();

        var controller = new ExamsController(ctx);
        SetFakeUser(controller);

        var result = await controller.Index(course.Id) as Microsoft.AspNetCore.Mvc.ViewResult;
        var model = Assert.IsAssignableFrom<IEnumerable<Exam>>(result!.Model);

        Assert.Equal(2, model.Count());
    }

    [Fact]
    public async Task ExamsController_Details_NullId_ReturnsNotFound()
    {
        await using var ctx = CreateCtx();
        var controller = new ExamsController(ctx);
        SetFakeUser(controller);

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
        SetFakeUser(controller);

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
        SetFakeUser(controller);

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
        SetFakeUser(controller);

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
        var exam = new Exam { CourseId = course.Id, Title = "Final", Date = DateTime.Today, MaxScore = 100, ResultsReleased = false };
        ctx.Exams.Add(exam);
        await ctx.SaveChangesAsync();

        var controller = new ExamsController(ctx);
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        controller.TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(
            httpContext,
            new FakeTempDataProvider());
        var result = await controller.ReleaseResults(exam.Id);
        var redirectResult = Assert.IsType<Microsoft.AspNetCore.Mvc.RedirectToActionResult>(result);
        Assert.Equal("Details", redirectResult.ActionName);
        var savedExam = await ctx.Exams.FindAsync(exam.Id);
        Assert.True(savedExam!.ResultsReleased);
    }

    // ══════════════════════════════════════════════════════════════════
    //  ENROLMENTS CONTROLLER
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
        SetFakeUser(controller);

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
    //  ATTENDANCE CONTROLLER
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task AttendanceController_Index_ReturnsAttendanceForEnrolment()
    {
        await using var ctx = CreateCtx();
        var (_, course) = await SeedCourseAsync(ctx);
        var student = await SeedStudentAsync(ctx);
        var enrolment = new CourseEnrolment { StudentProfileId = student.Id, CourseId = course.Id, EnrolDate = DateTime.Today, Status = EnrolmentStatus.Active };
        ctx.CourseEnrolments.Add(enrolment);
        await ctx.SaveChangesAsync();

        var user = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Admin")]));
        var controller = new AttendanceController(ctx)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = user } }
        };
        var result = await controller.Index(enrolment.Id) as Microsoft.AspNetCore.Mvc.ViewResult;
        Assert.NotNull(result);
        var model = Assert.IsAssignableFrom<CourseEnrolment>(result.Model);
        Assert.Equal(enrolment.Id, model.Id);
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
    //  STUDENT PROFILES CONTROLLER
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task StudentProfilesController_Index_ReturnsAllStudents()
    {
        await using var ctx = CreateCtx();
        await SeedStudentAsync(ctx, "uid-a");
        await SeedStudentAsync(ctx, "uid-b");

        var controller = new StudentProfilesController(ctx);
        SetFakeUser(controller);

        var result = await controller.Index() as Microsoft.AspNetCore.Mvc.ViewResult;
        var model = Assert.IsAssignableFrom<IEnumerable<StudentProfile>>(result!.Model);

        Assert.Equal(2, model.Count());
    }

    [Fact]
    public async Task StudentProfilesController_Details_NullId_ReturnsNotFound()
    {
        await using var ctx = CreateCtx();
        var controller = new StudentProfilesController(ctx);
        SetFakeUser(controller);

        var result = await controller.Details(null);
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(result);
    }

    [Fact]
    public async Task StudentProfilesController_Details_ValidId_ReturnsView()
    {
        await using var ctx = CreateCtx();
        var student = await SeedStudentAsync(ctx);

        var controller = new StudentProfilesController(ctx);
        SetFakeUser(controller);

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
        SetFakeUser(controller);

        var result = await controller.Details(999);
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(result);
    }

    // ══════════════════════════════════════════════════════════════════
    //  FACULTY PROFILES CONTROLLER
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
    //  ASSIGNMENTS CONTROLLER — Edit actions
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
    //  COURSES CONTROLLER — Edit actions
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
    // ── TEST 18: HomeController - Index et Privacy ────────────────────────

    [Fact]
    public void HomeController_Index_ReturnsView()
    {
        var controller = new HomeController();
        var result = controller.Index();
        Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(result);
    }

    [Fact]
    public void HomeController_Privacy_ReturnsView()
    {
        var controller = new HomeController();
        var result = controller.Privacy();
        Assert.IsType<Microsoft.AspNetCore.Mvc.ViewResult>(result);
    }


    [Fact]
    public async Task StudentProfilesController_Create_ValidModel_RedirectsToIndex()
    {
        await using var ctx = CreateCtx();
        var controller = new StudentProfilesController(ctx);
        var student = new StudentProfile { IdentityUserId = "new-uid", Name = "New Student", Email = "new@vgc.ie", StudentNumber = "S123" };
        var result = await controller.Create(student) as Microsoft.AspNetCore.Mvc.RedirectToActionResult;
        Assert.NotNull(result);
        Assert.Equal("Index", result.ActionName);
        Assert.Single(ctx.StudentProfiles);
    }

    [Fact]
    public async Task StudentProfilesController_Edit_ValidModel_RedirectsToIndex()
    {
        await using var ctx = CreateCtx();
        var student = await SeedStudentAsync(ctx);
        var controller = new StudentProfilesController(ctx);

        student.Name = "Updated Name";
        var result = await controller.Edit(student.Id, student) as Microsoft.AspNetCore.Mvc.RedirectToActionResult;
        Assert.NotNull(result);
        Assert.Equal("Index", result.ActionName);
        var saved = await ctx.StudentProfiles.FindAsync(student.Id);
        Assert.Equal("Updated Name", saved!.Name);
    }

    [Fact]
    public async Task StudentProfilesController_DeleteConfirmed_RemovesStudent()
    {
        await using var ctx = CreateCtx();
        var student = await SeedStudentAsync(ctx);
        var controller = new StudentProfilesController(ctx);
        var result = await controller.DeleteConfirmed(student.Id) as Microsoft.AspNetCore.Mvc.RedirectToActionResult;
        Assert.NotNull(result);
        Assert.Equal("Index", result.ActionName);
        Assert.Empty(ctx.StudentProfiles); 
    }

    [Fact]
    public async Task FacultyProfilesController_Create_ValidModel_RedirectsToIndex()
    {
        await using var ctx = CreateCtx();
        var controller = new FacultyProfilesController(ctx);
        var faculty = new FacultyProfile { IdentityUserId = "fac-new", Name = "New Faculty", Email = "fac@vgc.ie" };
        var result = await controller.Create(faculty) as Microsoft.AspNetCore.Mvc.RedirectToActionResult;
        Assert.NotNull(result);
        Assert.Equal("Index", result.ActionName);
    }

    [Fact]
    public async Task FacultyProfilesController_DeleteConfirmed_RemovesFaculty()
    {
        await using var ctx = CreateCtx();
        var faculty = await SeedFacultyAsync(ctx);
        var controller = new FacultyProfilesController(ctx);
        var result = await controller.DeleteConfirmed(faculty.Id) as Microsoft.AspNetCore.Mvc.RedirectToActionResult;
        Assert.NotNull(result);
        Assert.Equal("Index", result.ActionName);
        Assert.Empty(ctx.FacultyProfiles);
    }


    [Fact]
    public async Task StudentProfilesController_Create_InvalidModel_ReturnsView()
    {
        await using var ctx = CreateCtx();
        var controller = new StudentProfilesController(ctx);

        controller.ModelState.AddModelError("Name", "Required");
        var student = new StudentProfile();

        var result = await controller.Create(student) as Microsoft.AspNetCore.Mvc.ViewResult;

        Assert.NotNull(result);
        Assert.Equal(student, result.Model);
    }

    [Fact]
    public async Task FacultyProfilesController_Create_InvalidModel_ReturnsView()
    {
        await using var ctx = CreateCtx();
        var controller = new FacultyProfilesController(ctx);

        controller.ModelState.AddModelError("Email", "Invalid");
        var faculty = new FacultyProfile();

        var result = await controller.Create(faculty) as Microsoft.AspNetCore.Mvc.ViewResult;

        Assert.NotNull(result);
        Assert.Equal(faculty, result.Model);
    }

    [Fact]
    public async Task CoursesController_Details_NullId_ReturnsNotFound()
    {
        await using var ctx = CreateCtx();
        var controller = new CoursesController(ctx);

        var result = await controller.Details(null);

        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(result);
    }

    [Fact]
    public async Task CoursesController_Details_NotFoundId_ReturnsNotFound()
    {
        await using var ctx = CreateCtx();
        var controller = new CoursesController(ctx);

        var result = await controller.Details(9999);

        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(result);
    }

    [Fact]
    public async Task StudentProfilesController_Edit_NullId_ReturnsNotFound()
    {
        await using var ctx = CreateCtx();
        var controller = new StudentProfilesController(ctx);

        var result = await controller.Edit((int?)null);

        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(result);
    }


    [Fact]
    public void AssignmentResult_Properties_CanBeSet_IncreasesDomainCoverage()
    {
        var result = new AssignmentResult
        {
            Id = 1,
            AssignmentId = 10,
            StudentProfileId = 5,
            Score = 85,
            Feedback = "Excellent"
        };

        Assert.Equal(1, result.Id);
        Assert.Equal(10, result.AssignmentId);
        Assert.Equal(5, result.StudentProfileId);
        Assert.Equal(85, result.Score);
        Assert.Equal("Excellent", result.Feedback);
    }

    [Fact]
    public async Task BranchesController_Details_ValidId_ReturnsViewWithBranch()
    {
        await using var ctx = CreateCtx();
        var branch = new Branch { Name = "Cork", Address = "123 Street" };
        ctx.Branches.Add(branch);
        await ctx.SaveChangesAsync();
        var controller = new BranchesController(ctx);

        var result = await controller.Details(branch.Id) as Microsoft.AspNetCore.Mvc.ViewResult;

        Assert.NotNull(result);
        var model = Assert.IsAssignableFrom<Branch>(result.Model);
        Assert.Equal(branch.Id, model.Id);
    }

    [Fact]
    public async Task BranchesController_Edit_ValidModel_RedirectsToIndex()
    {
        await using var ctx = CreateCtx();
        var branch = new Branch { Name = "Galway", Address = "456 Street" };
        ctx.Branches.Add(branch);
        await ctx.SaveChangesAsync();
        var controller = new BranchesController(ctx);
        branch.Name = "Galway Updated";

        var result = await controller.Edit(branch.Id, branch) as Microsoft.AspNetCore.Mvc.RedirectToActionResult;

        Assert.NotNull(result);
        Assert.Equal("Index", result.ActionName);
        var saved = await ctx.Branches.FindAsync(branch.Id);
        Assert.Equal("Galway Updated", saved!.Name);
    }

    [Fact]
    public async Task BranchesController_DeleteConfirmed_RemovesBranchAndRedirects()
    {
        await using var ctx = CreateCtx();
        var branch = new Branch { Name = "Limerick", Address = "789 Street" };
        ctx.Branches.Add(branch);
        await ctx.SaveChangesAsync();
        var controller = new BranchesController(ctx);

        var result = await controller.DeleteConfirmed(branch.Id) as Microsoft.AspNetCore.Mvc.RedirectToActionResult;

        Assert.NotNull(result);
        Assert.Equal("Index", result.ActionName);
        Assert.Empty(ctx.Branches);
    }

    [Fact]
    public async Task FacultyProfilesController_AssignCourse_RedirectsToDetails()
    {
        await using var ctx = CreateCtx();
        var faculty = await SeedFacultyAsync(ctx);
        var (_, course) = await SeedCourseAsync(ctx);
        var controller = new FacultyProfilesController(ctx);

        var result = await controller.AssignCourse(faculty.Id, course.Id, true) as Microsoft.AspNetCore.Mvc.RedirectToActionResult;

        Assert.NotNull(result);
        Assert.Equal("Details", result.ActionName);
        Assert.Equal(faculty.Id, result.RouteValues!["id"]);
        Assert.Single(ctx.FacultyCourseAssignments);
    }

    [Fact]
    public async Task FacultyProfilesController_RemoveCourseAssignment_RemovesAndRedirects()
    {
        await using var ctx = CreateCtx();
        var faculty = await SeedFacultyAsync(ctx);
        var (_, course) = await SeedCourseAsync(ctx);
        var assignment = new FacultyCourseAssignment { FacultyProfileId = faculty.Id, CourseId = course.Id, IsTutor = true };
        ctx.FacultyCourseAssignments.Add(assignment);
        await ctx.SaveChangesAsync();
        var controller = new FacultyProfilesController(ctx);

        var result = await controller.RemoveCourseAssignment(assignment.Id) as Microsoft.AspNetCore.Mvc.RedirectToActionResult;

        Assert.NotNull(result);
        Assert.Equal("Details", result.ActionName);
        Assert.Empty(ctx.FacultyCourseAssignments);
    }

    [Fact]
    public async Task BranchesController_UnhappyPaths_ReturnsNotFound()
    {
        await using var ctx = CreateCtx();
        var c = new BranchesController(ctx);

        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Details(null));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Details(9999));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Edit((int?)null));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Edit(9999));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Edit(1, new Branch { Id = 2 }));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Delete(null));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Delete(9999));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.DeleteConfirmed(9999));
    }

    [Fact]
    public async Task CoursesController_UnhappyPaths_ReturnsNotFound()
    {
        await using var ctx = CreateCtx();
        var c = new CoursesController(ctx);

        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Details(9999));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Edit((int?)null));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Edit(9999));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Edit(1, new Course { Id = 2 }));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Delete(null));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Delete(9999));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.DeleteConfirmed(9999));
    }

    [Fact]
    public async Task StudentProfilesController_UnhappyPaths_ReturnsNotFound()
    {
        await using var ctx = CreateCtx();
        var c = new StudentProfilesController(ctx);

        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Details(9999));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Edit(9999));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Edit(1, new StudentProfile { Id = 2 }));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Delete(null));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Delete(9999));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.DeleteConfirmed(9999));
    }

    [Fact]
    public async Task FacultyProfilesController_UnhappyPaths_ReturnsNotFound()
    {
        await using var ctx = CreateCtx();
        var c = new FacultyProfilesController(ctx);

        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Details(null));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Details(9999));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Edit((int?)null));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Edit(9999));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Edit(1, new FacultyProfile { Id = 2 }));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Delete(null));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Delete(9999));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.DeleteConfirmed(9999));
    }

    [Fact]
    public async Task EnrolmentsController_UnhappyPaths_ReturnsNotFound()
    {
        await using var ctx = CreateCtx();
        var c = new EnrolmentsController(ctx);

        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Edit((int?)null));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Edit(9999));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Edit(1, new CourseEnrolment { Id = 2 }));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Delete(null));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Delete(9999));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.DeleteConfirmed(9999));
    }

    [Fact]
    public async Task ExamsController_UnhappyPaths_ReturnsNotFound()
    {
        await using var ctx = CreateCtx();
        var c = new ExamsController(ctx);

        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Details(9999));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Edit((int?)null));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Edit(9999));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Edit(1, new Exam { Id = 2 }));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Delete(null));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Delete(9999));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.DeleteConfirmed(9999));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.ReleaseResults(9999));
    }

    [Fact]
    public async Task AssignmentsController_UnhappyPaths_ReturnsNotFound()
    {
        await using var ctx = CreateCtx();
        var c = new AssignmentsController(ctx);

        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Details(9999));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Edit(9999));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Edit(1, new Assignment { Id = 2 }));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Delete(null));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Delete(9999));
        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.DeleteConfirmed(9999));
    }

    [Fact]
    public async Task AttendanceController_UnhappyPaths_ReturnsNotFound()
    {
        await using var ctx = CreateCtx();
        var c = new AttendanceController(ctx);

        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Toggle(9999, 1));
        var user = new System.Security.Claims.ClaimsPrincipal(new System.Security.Claims.ClaimsIdentity(
            [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, "Admin")]));
        c.ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext { User = user } };

        Assert.IsType<Microsoft.AspNetCore.Mvc.NotFoundResult>(await c.Index(9999));
    }

    public class FakeTempDataProvider : Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider
    {
        public IDictionary<string, object> LoadTempData(Microsoft.AspNetCore.Http.HttpContext context)
            => new Dictionary<string, object>();

        public void SaveTempData(Microsoft.AspNetCore.Http.HttpContext context, IDictionary<string, object> values)
        { }
    }
}