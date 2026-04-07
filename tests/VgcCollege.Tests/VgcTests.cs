using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using VgcCollege.Web.Controllers;
using VgcCollege.Web.Data;
using VgcCollege.Web.Domain;
using VgcCollege.Web.Models;

namespace VgcCollege.Tests;

// ══════════════════════════════════════════════════════════════════════════
//  INFRASTRUCTURE PARTAGÉE
// ══════════════════════════════════════════════════════════════════════════

file static class Db
{
    public static ApplicationDbContext Create() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    public static async Task<(ApplicationDbContext ctx, Branch branch, Course course)> WithCourseAsync()
    {
        var ctx = Create();
        var branch = new Branch { Name = "Test Branch", Address = "1 Test St" };
        ctx.Branches.Add(branch);
        await ctx.SaveChangesAsync();
        var course = new Course
        {
            Name = "Test Course",
            BranchId = branch.Id,
            StartDate = DateTime.Today.AddMonths(-3),
            EndDate = DateTime.Today.AddMonths(9)
        };
        ctx.Courses.Add(course);
        await ctx.SaveChangesAsync();
        return (ctx, branch, course);
    }

    public static async Task<(ApplicationDbContext ctx, Course course, StudentProfile student, CourseEnrolment enrolment)> WithEnrolmentAsync()
    {
        var (ctx, _, course) = await WithCourseAsync();
        var student = new StudentProfile
        {
            IdentityUserId = "uid-s1",
            Name = "Alice",
            Email = "alice@test.ie",
            StudentNumber = "S001"
        };
        ctx.StudentProfiles.Add(student);
        await ctx.SaveChangesAsync();
        var enrolment = new CourseEnrolment
        {
            StudentProfileId = student.Id,
            CourseId = course.Id,
            EnrolDate = DateTime.Today,
            Status = EnrolmentStatus.Active
        };
        ctx.CourseEnrolments.Add(enrolment);
        await ctx.SaveChangesAsync();
        return (ctx, course, student, enrolment);
    }

    public static ITempDataDictionary FakeTempData() =>
        new TempDataDictionary(new DefaultHttpContext(), new FakeTempDataProvider());
}

file class FakeTempDataProvider : ITempDataProvider
{
    public IDictionary<string, object> LoadTempData(HttpContext context) => new Dictionary<string, object>();
    public void SaveTempData(HttpContext context, IDictionary<string, object> values) { }
}

file static class As
{
    public static void Admin(Controller ctrl, string uid = "admin-id") => Set(ctrl, "Admin", uid);
    public static void Faculty(Controller ctrl, string uid = "fac-id") => Set(ctrl, "Faculty", uid);
    public static void Student(Controller ctrl, string uid = "stu-id") => Set(ctrl, "Student", uid);
    public static void User(Controller ctrl, string uid) => Set(ctrl, "", uid);

    public static void Set(Controller ctrl, string role, string uid = "test-id")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, uid),
            new(ClaimTypes.Name, uid)
        };
        if (!string.IsNullOrEmpty(role))
            claims.Add(new Claim(ClaimTypes.Role, role));

        ctrl.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"))
            }
        };
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  DOMAINE — ENTITÉS
// ══════════════════════════════════════════════════════════════════════════

public class BranchEntityTests
{
    [Fact]
    public void Branch_DefaultValues_AreCorrect()
    {
        var b = new Branch();
        Assert.Equal(0, b.Id);
        Assert.Equal(string.Empty, b.Name);
        Assert.Equal(string.Empty, b.Address);
        Assert.NotNull(b.Courses);
        Assert.Empty(b.Courses);
    }

    [Fact]
    public void Branch_Properties_CanBeSet()
    {
        var b = new Branch { Id = 1, Name = "Dublin Campus", Address = "1 O'Connell St" };
        Assert.Equal(1, b.Id);
        Assert.Equal("Dublin Campus", b.Name);
    }

    [Fact]
    public void Branch_Courses_CanBePopulated()
    {
        var b = new Branch();
        b.Courses.Add(new Course { Name = "Software Dev" });
        Assert.Single(b.Courses);
    }
}

public class CourseEntityTests
{
    [Fact]
    public void Course_DefaultCollections_AreEmpty()
    {
        var c = new Course();
        Assert.Empty(c.Enrolments);
        Assert.Empty(c.Assignments);
        Assert.Empty(c.Exams);
        Assert.Empty(c.FacultyAssignments);
    }

    [Fact]
    public void Course_StartDateBeforeEndDate_IsValid()
    {
        var c = new Course { StartDate = DateTime.Today, EndDate = DateTime.Today.AddMonths(12) };
        Assert.True(c.StartDate < c.EndDate);
    }

    [Fact]
    public void Course_Properties_CanBeSet()
    {
        var start = DateTime.Today;
        var end = DateTime.Today.AddYears(1);
        var c = new Course { Id = 5, Name = "Web Dev", BranchId = 2, StartDate = start, EndDate = end };
        Assert.Equal(5, c.Id);
        Assert.Equal("Web Dev", c.Name);
        Assert.Equal(2, c.BranchId);
    }
}

public class StudentProfileEntityTests
{
    [Fact]
    public void StudentProfile_DefaultValues_AreCorrect()
    {
        var s = new StudentProfile();
        Assert.Equal(0, s.Id);
        Assert.Equal(string.Empty, s.IdentityUserId);
        Assert.Equal(string.Empty, s.Name);
        Assert.Equal(string.Empty, s.Email);
        Assert.Null(s.Phone);
        Assert.Null(s.Address);
        Assert.Null(s.DateOfBirth);
        Assert.Equal(string.Empty, s.StudentNumber);
        Assert.Empty(s.Enrolments);
        Assert.Empty(s.AssignmentResults);
        Assert.Empty(s.ExamResults);
    }

    [Fact]
    public void StudentProfile_OptionalProperties_CanBeSet()
    {
        var dob = new DateTime(2000, 1, 15);
        var s = new StudentProfile { Phone = "0871234567", Address = "10 Main St", DateOfBirth = dob };
        Assert.Equal("0871234567", s.Phone);
        Assert.Equal("10 Main St", s.Address);
        Assert.Equal(dob, s.DateOfBirth);
    }

    [Fact]
    public void StudentNumber_Format_IsSet()
    {
        var s = new StudentProfile { StudentNumber = "VGC-2024-001" };
        Assert.StartsWith("VGC-", s.StudentNumber);
    }
}

public class FacultyProfileEntityTests
{
    [Fact]
    public void FacultyProfile_DefaultValues_AreCorrect()
    {
        var f = new FacultyProfile();
        Assert.Equal(0, f.Id);
        Assert.Equal(string.Empty, f.IdentityUserId);
        Assert.Equal(string.Empty, f.Name);
        Assert.Equal(string.Empty, f.Email);
        Assert.Null(f.Phone);
        Assert.Empty(f.CourseAssignments);
    }

    [Fact]
    public void FacultyProfile_Phone_CanBeSet()
    {
        var f = new FacultyProfile { Phone = "0861234567" };
        Assert.Equal("0861234567", f.Phone);
    }
}

public class FacultyCourseAssignmentEntityTests
{
    [Fact]
    public void FacultyCourseAssignment_DefaultIsTutor_IsFalse()
    {
        var a = new FacultyCourseAssignment();
        Assert.False(a.IsTutor);
        Assert.Equal(0, a.FacultyProfileId);
        Assert.Equal(0, a.CourseId);
    }

    [Fact]
    public void FacultyCourseAssignment_IsTutor_CanBeSetTrue()
    {
        var a = new FacultyCourseAssignment { IsTutor = true, FacultyProfileId = 1, CourseId = 2 };
        Assert.True(a.IsTutor);
        Assert.Equal(1, a.FacultyProfileId);
        Assert.Equal(2, a.CourseId);
    }
}

public class CourseEnrolmentEntityTests
{
    [Fact]
    public void CourseEnrolment_DefaultStatus_IsActive()
    {
        var e = new CourseEnrolment();
        Assert.Equal(EnrolmentStatus.Active, e.Status);
        Assert.Empty(e.AttendanceRecords);
    }

    [Fact]
    public void CourseEnrolment_Properties_CanBeSet()
    {
        var date = DateTime.Today;
        var e = new CourseEnrolment
        {
            Id = 3,
            StudentProfileId = 10,
            CourseId = 5,
            EnrolDate = date,
            Status = EnrolmentStatus.Withdrawn
        };
        Assert.Equal(3, e.Id);
        Assert.Equal(EnrolmentStatus.Withdrawn, e.Status);
    }
}

public class AttendanceRecordEntityTests
{
    [Fact]
    public void AttendanceRecord_DefaultValues_AreCorrect()
    {
        var r = new AttendanceRecord();
        Assert.Equal(0, r.Id);
        Assert.Equal(0, r.CourseEnrolmentId);
        Assert.Equal(0, r.WeekNumber);
        Assert.False(r.Present);
    }

    [Fact]
    public void AttendanceRecord_Properties_CanBeSet()
    {
        var date = DateTime.Today;
        var r = new AttendanceRecord
        {
            Id = 1,
            CourseEnrolmentId = 2,
            WeekNumber = 5,
            SessionDate = date,
            Present = true
        };
        Assert.Equal(5, r.WeekNumber);
        Assert.True(r.Present);
        Assert.Equal(date, r.SessionDate);
    }
}

public class AssignmentEntityTests
{
    [Fact]
    public void Assignment_DefaultValues_AreCorrect()
    {
        var a = new Assignment();
        Assert.Equal(0, a.Id);
        Assert.Equal(string.Empty, a.Title);
        Assert.Equal(0, a.MaxScore);
        Assert.Empty(a.Results);
    }

    [Fact]
    public void Assignment_Properties_CanBeSet()
    {
        var due = DateTime.Today.AddDays(7);
        var a = new Assignment { Id = 1, CourseId = 3, Title = "CA1", MaxScore = 100, DueDate = due };
        Assert.Equal("CA1", a.Title);
        Assert.Equal(100, a.MaxScore);
        Assert.Equal(due, a.DueDate);
    }
}

public class AssignmentResultEntityTests
{
    [Fact]
    public void AssignmentResult_DefaultValues_AreCorrect()
    {
        var r = new AssignmentResult();
        Assert.Equal(0, r.Id);
        Assert.Equal(0, r.AssignmentId);
        Assert.Equal(0, r.StudentProfileId);
        Assert.Equal(0, r.Score);
        Assert.Null(r.Feedback);
    }

    [Fact]
    public void AssignmentResult_Properties_CanBeSet()
    {
        var r = new AssignmentResult { Id = 1, AssignmentId = 10, StudentProfileId = 5, Score = 85, Feedback = "Excellent" };
        Assert.Equal(85, r.Score);
        Assert.Equal("Excellent", r.Feedback);
    }
}

public class ExamEntityTests
{
    [Fact]
    public void Exam_DefaultResultsReleased_IsFalse()
    {
        var e = new Exam();
        Assert.False(e.ResultsReleased);
        Assert.Equal(string.Empty, e.Title);
        Assert.Equal(0, e.MaxScore);
        Assert.Empty(e.Results);
    }

    [Fact]
    public void Exam_Properties_CanBeSet()
    {
        var date = DateTime.Today;
        var e = new Exam { Id = 1, CourseId = 2, Title = "Final Exam", Date = date, MaxScore = 100, ResultsReleased = true };
        Assert.Equal("Final Exam", e.Title);
        Assert.Equal(100, e.MaxScore);
        Assert.True(e.ResultsReleased);
    }
}

public class ExamResultEntityTests
{
    [Fact]
    public void ExamResult_DefaultValues_AreCorrect()
    {
        var r = new ExamResult();
        Assert.Equal(0, r.Id);
        Assert.Equal(0, r.ExamId);
        Assert.Equal(0, r.StudentProfileId);
        Assert.Equal(0, r.Score);
        Assert.Null(r.Grade);
    }

    [Fact]
    public void ExamResult_Properties_CanBeSet()
    {
        var r = new ExamResult { Id = 1, ExamId = 5, StudentProfileId = 3, Score = 92, Grade = "A1" };
        Assert.Equal(92, r.Score);
        Assert.Equal("A1", r.Grade);
    }
}

public class ErrorViewModelTests
{
    [Fact]
    public void ShowRequestId_WhenRequestIdIsNull_ReturnsFalse()
    {
        var vm = new ErrorViewModel { RequestId = null };
        Assert.False(vm.ShowRequestId);
    }

    [Fact]
    public void ShowRequestId_WhenRequestIdHasValue_ReturnsTrue()
    {
        var vm = new ErrorViewModel { RequestId = "abc-123" };
        Assert.True(vm.ShowRequestId);
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  LOGIQUE MÉTIER / RÈGLES
// ══════════════════════════════════════════════════════════════════════════

public class EnrolmentRuleTests
{
    [Fact]
    public async Task Enrolment_Duplicate_IsDetected()
    {
        var (ctx, course, student, _) = await Db.WithEnrolmentAsync();
        var dup = await ctx.CourseEnrolments
            .AnyAsync(e => e.StudentProfileId == student.Id && e.CourseId == course.Id);
        Assert.True(dup);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Enrolment_NewCourse_Succeeds()
    {
        var (ctx, _, course) = await Db.WithCourseAsync();
        var student = new StudentProfile { IdentityUserId = "uid-new", Name = "Bob", Email = "bob@test.ie", StudentNumber = "S002" };
        ctx.StudentProfiles.Add(student);
        await ctx.SaveChangesAsync();
        ctx.CourseEnrolments.Add(new CourseEnrolment
        {
            StudentProfileId = student.Id,
            CourseId = course.Id,
            EnrolDate = DateTime.Today,
            Status = EnrolmentStatus.Active
        });
        await ctx.SaveChangesAsync();
        Assert.Equal(1, await ctx.CourseEnrolments.CountAsync());
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task EnrolmentStatus_CanBeChangedToWithdrawn()
    {
        var (ctx, _, _, enrolment) = await Db.WithEnrolmentAsync();
        enrolment.Status = EnrolmentStatus.Withdrawn;
        await ctx.SaveChangesAsync();
        var saved = await ctx.CourseEnrolments.FindAsync(enrolment.Id);
        Assert.Equal(EnrolmentStatus.Withdrawn, saved!.Status);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Student_OnlySeesOwnEnrolments()
    {
        var (ctx, _, course) = await Db.WithCourseAsync();
        var s1 = new StudentProfile { IdentityUserId = "u1", Name = "S1", Email = "s1@t.ie", StudentNumber = "001" };
        var s2 = new StudentProfile { IdentityUserId = "u2", Name = "S2", Email = "s2@t.ie", StudentNumber = "002" };
        ctx.StudentProfiles.AddRange(s1, s2);
        await ctx.SaveChangesAsync();
        ctx.CourseEnrolments.AddRange(
            new CourseEnrolment { StudentProfileId = s1.Id, CourseId = course.Id, EnrolDate = DateTime.Today, Status = EnrolmentStatus.Active },
            new CourseEnrolment { StudentProfileId = s2.Id, CourseId = course.Id, EnrolDate = DateTime.Today, Status = EnrolmentStatus.Active }
        );
        await ctx.SaveChangesAsync();
        var myEnrolments = await ctx.CourseEnrolments.Where(e => e.StudentProfileId == s1.Id).ToListAsync();
        Assert.Single(myEnrolments);
        Assert.All(myEnrolments, e => Assert.Equal(s1.Id, e.StudentProfileId));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Student_CanBeEnrolledInMultipleCourses()
    {
        var ctx = Db.Create();
        var branch = new Branch { Name = "B", Address = "A" };
        ctx.Branches.Add(branch);
        await ctx.SaveChangesAsync();
        var c1 = new Course { Name = "C1", BranchId = branch.Id, StartDate = DateTime.Today, EndDate = DateTime.Today.AddYears(1) };
        var c2 = new Course { Name = "C2", BranchId = branch.Id, StartDate = DateTime.Today, EndDate = DateTime.Today.AddYears(1) };
        ctx.Courses.AddRange(c1, c2);
        await ctx.SaveChangesAsync();
        var student = new StudentProfile { IdentityUserId = "u1", Name = "S1", Email = "s@t.ie", StudentNumber = "001" };
        ctx.StudentProfiles.Add(student);
        await ctx.SaveChangesAsync();
        ctx.CourseEnrolments.AddRange(
            new CourseEnrolment { StudentProfileId = student.Id, CourseId = c1.Id, EnrolDate = DateTime.Today, Status = EnrolmentStatus.Active },
            new CourseEnrolment { StudentProfileId = student.Id, CourseId = c2.Id, EnrolDate = DateTime.Today, Status = EnrolmentStatus.Active }
        );
        await ctx.SaveChangesAsync();
        Assert.Equal(2, await ctx.CourseEnrolments.CountAsync(e => e.StudentProfileId == student.Id));
        await ctx.DisposeAsync();
    }
}

public class AttendanceRuleTests
{
    [Fact]
    public async Task AttendanceRate_CalculatedCorrectly()
    {
        var (ctx, _, _, enrolment) = await Db.WithEnrolmentAsync();
        ctx.AttendanceRecords.AddRange(
            new AttendanceRecord { CourseEnrolmentId = enrolment.Id, WeekNumber = 1, SessionDate = DateTime.Today, Present = true },
            new AttendanceRecord { CourseEnrolmentId = enrolment.Id, WeekNumber = 2, SessionDate = DateTime.Today.AddDays(7), Present = true },
            new AttendanceRecord { CourseEnrolmentId = enrolment.Id, WeekNumber = 3, SessionDate = DateTime.Today.AddDays(14), Present = false },
            new AttendanceRecord { CourseEnrolmentId = enrolment.Id, WeekNumber = 4, SessionDate = DateTime.Today.AddDays(21), Present = true }
        );
        await ctx.SaveChangesAsync();
        var records = await ctx.AttendanceRecords.Where(a => a.CourseEnrolmentId == enrolment.Id).ToListAsync();
        var pct = records.Count(r => r.Present) * 100.0 / records.Count;
        Assert.Equal(75.0, pct);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Attendance_Present_IsStored()
    {
        var (ctx, _, _, enrolment) = await Db.WithEnrolmentAsync();
        ctx.AttendanceRecords.Add(new AttendanceRecord { CourseEnrolmentId = enrolment.Id, WeekNumber = 1, SessionDate = DateTime.Today, Present = true });
        await ctx.SaveChangesAsync();
        Assert.True((await ctx.AttendanceRecords.FirstAsync()).Present);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Attendance_Absent_IsStored()
    {
        var (ctx, _, _, enrolment) = await Db.WithEnrolmentAsync();
        ctx.AttendanceRecords.Add(new AttendanceRecord { CourseEnrolmentId = enrolment.Id, WeekNumber = 2, SessionDate = DateTime.Today, Present = false });
        await ctx.SaveChangesAsync();
        Assert.False((await ctx.AttendanceRecords.FirstAsync()).Present);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task AttendanceDuplicateWeek_IsDetected()
    {
        var (ctx, _, _, enrolment) = await Db.WithEnrolmentAsync();
        ctx.AttendanceRecords.Add(new AttendanceRecord { CourseEnrolmentId = enrolment.Id, WeekNumber = 1, SessionDate = DateTime.Today, Present = true });
        await ctx.SaveChangesAsync();
        var exists = await ctx.AttendanceRecords
            .AnyAsync(a => a.CourseEnrolmentId == enrolment.Id && a.WeekNumber == 1);
        Assert.True(exists);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task AttendanceRecords_MultipleWeeks_AllSaved()
    {
        var (ctx, _, _, enrolment) = await Db.WithEnrolmentAsync();
        for (int i = 1; i <= 10; i++)
            ctx.AttendanceRecords.Add(new AttendanceRecord
            {
                CourseEnrolmentId = enrolment.Id,
                WeekNumber = i,
                SessionDate = DateTime.Today.AddDays(i * 7),
                Present = i % 2 == 0
            });
        await ctx.SaveChangesAsync();
        Assert.Equal(10, await ctx.AttendanceRecords.CountAsync());
        await ctx.DisposeAsync();
    }
}

public class ExamVisibilityTests
{
    [Fact]
    public async Task ProvisionalExamResult_IsHiddenFromStudent()
    {
        var (ctx, course, student, _) = await Db.WithEnrolmentAsync();
        var exam = new Exam { CourseId = course.Id, Title = "Final", Date = DateTime.Today, MaxScore = 100, ResultsReleased = false };
        ctx.Exams.Add(exam);
        await ctx.SaveChangesAsync();
        ctx.ExamResults.Add(new ExamResult { ExamId = exam.Id, StudentProfileId = student.Id, Score = 72, Grade = "B2" });
        await ctx.SaveChangesAsync();
        var visible = await ctx.ExamResults.Include(r => r.Exam)
            .Where(r => r.StudentProfileId == student.Id && r.Exam.ResultsReleased).ToListAsync();
        Assert.Empty(visible);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task ReleasedExamResult_IsVisibleToStudent()
    {
        var (ctx, course, student, _) = await Db.WithEnrolmentAsync();
        var exam = new Exam { CourseId = course.Id, Title = "Released", Date = DateTime.Today, MaxScore = 100, ResultsReleased = true };
        ctx.Exams.Add(exam);
        await ctx.SaveChangesAsync();
        ctx.ExamResults.Add(new ExamResult { ExamId = exam.Id, StudentProfileId = student.Id, Score = 85, Grade = "A2" });
        await ctx.SaveChangesAsync();
        var visible = await ctx.ExamResults.Include(r => r.Exam)
            .Where(r => r.StudentProfileId == student.Id && r.Exam.ResultsReleased).ToListAsync();
        Assert.Single(visible);
        Assert.Equal(85, visible[0].Score);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task ReleaseResults_UpdatesFlag()
    {
        var (ctx, course, _, _) = await Db.WithEnrolmentAsync();
        var exam = new Exam { CourseId = course.Id, Title = "Pending", Date = DateTime.Today, MaxScore = 100, ResultsReleased = false };
        ctx.Exams.Add(exam);
        await ctx.SaveChangesAsync();
        exam.ResultsReleased = true;
        await ctx.SaveChangesAsync();
        Assert.True((await ctx.Exams.FindAsync(exam.Id))!.ResultsReleased);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task MixedExams_StudentOnlySeesReleased()
    {
        var (ctx, course, student, _) = await Db.WithEnrolmentAsync();
        var e1 = new Exam { CourseId = course.Id, Title = "Exam 1", Date = DateTime.Today, MaxScore = 100, ResultsReleased = true };
        var e2 = new Exam { CourseId = course.Id, Title = "Exam 2", Date = DateTime.Today, MaxScore = 100, ResultsReleased = false };
        ctx.Exams.AddRange(e1, e2);
        await ctx.SaveChangesAsync();
        ctx.ExamResults.AddRange(
            new ExamResult { ExamId = e1.Id, StudentProfileId = student.Id, Score = 70, Grade = "B2" },
            new ExamResult { ExamId = e2.Id, StudentProfileId = student.Id, Score = 55, Grade = "C3" }
        );
        await ctx.SaveChangesAsync();
        var visible = await ctx.ExamResults.Include(r => r.Exam)
            .Where(r => r.StudentProfileId == student.Id && r.Exam.ResultsReleased).ToListAsync();
        Assert.Single(visible);
        Assert.Equal("Exam 1", visible[0].Exam.Title);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task PendingExams_CountIsCorrect()
    {
        var (ctx, course, _, _) = await Db.WithEnrolmentAsync();
        ctx.Exams.AddRange(
            new Exam { CourseId = course.Id, Title = "E1", Date = DateTime.Today, MaxScore = 100, ResultsReleased = false },
            new Exam { CourseId = course.Id, Title = "E2", Date = DateTime.Today, MaxScore = 100, ResultsReleased = false },
            new Exam { CourseId = course.Id, Title = "E3", Date = DateTime.Today, MaxScore = 100, ResultsReleased = true }
        );
        await ctx.SaveChangesAsync();
        Assert.Equal(2, await ctx.Exams.CountAsync(e => !e.ResultsReleased));
        await ctx.DisposeAsync();
    }
}

public class FacultyAuthorizationTests
{
    [Fact]
    public async Task Faculty_OnlySeesStudentsInTheirCourses()
    {
        var ctx = Db.Create();
        var branch = new Branch { Name = "B", Address = "A" };
        ctx.Branches.Add(branch);
        await ctx.SaveChangesAsync();
        var c1 = new Course { Name = "C1", BranchId = branch.Id, StartDate = DateTime.Today, EndDate = DateTime.Today.AddYears(1) };
        var c2 = new Course { Name = "C2", BranchId = branch.Id, StartDate = DateTime.Today, EndDate = DateTime.Today.AddYears(1) };
        ctx.Courses.AddRange(c1, c2);
        await ctx.SaveChangesAsync();
        var faculty = new FacultyProfile { IdentityUserId = "fac", Name = "Dr F", Email = "f@t.ie" };
        ctx.FacultyProfiles.Add(faculty);
        await ctx.SaveChangesAsync();
        ctx.FacultyCourseAssignments.Add(new FacultyCourseAssignment { FacultyProfileId = faculty.Id, CourseId = c1.Id });
        await ctx.SaveChangesAsync();
        var s1 = new StudentProfile { IdentityUserId = "s1", Name = "S1", Email = "s1@t.ie", StudentNumber = "001" };
        var s2 = new StudentProfile { IdentityUserId = "s2", Name = "S2", Email = "s2@t.ie", StudentNumber = "002" };
        ctx.StudentProfiles.AddRange(s1, s2);
        await ctx.SaveChangesAsync();
        ctx.CourseEnrolments.AddRange(
            new CourseEnrolment { StudentProfileId = s1.Id, CourseId = c1.Id, EnrolDate = DateTime.Today, Status = EnrolmentStatus.Active },
            new CourseEnrolment { StudentProfileId = s2.Id, CourseId = c2.Id, EnrolDate = DateTime.Today, Status = EnrolmentStatus.Active }
        );
        await ctx.SaveChangesAsync();

        var courseIds = await ctx.FacultyCourseAssignments
            .Where(a => a.FacultyProfileId == faculty.Id).Select(a => a.CourseId).ToListAsync();
        var studentIds = await ctx.CourseEnrolments
            .Where(e => courseIds.Contains(e.CourseId)).Select(e => e.StudentProfileId).Distinct().ToListAsync();
        var visible = await ctx.StudentProfiles.Where(s => studentIds.Contains(s.Id)).ToListAsync();

        Assert.Single(visible);
        Assert.Equal("S1", visible[0].Name);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Faculty_IsTutor_AllowsContactDetailsAccess()
    {
        var (ctx, _, course) = await Db.WithCourseAsync();
        var faculty = new FacultyProfile { IdentityUserId = "fac", Name = "Dr F", Email = "f@t.ie" };
        ctx.FacultyProfiles.Add(faculty);
        await ctx.SaveChangesAsync();
        ctx.FacultyCourseAssignments.Add(new FacultyCourseAssignment { FacultyProfileId = faculty.Id, CourseId = course.Id, IsTutor = true });
        await ctx.SaveChangesAsync();
        var assignment = await ctx.FacultyCourseAssignments
            .FirstOrDefaultAsync(fa => fa.FacultyProfileId == faculty.Id && fa.CourseId == course.Id);
        Assert.NotNull(assignment);
        Assert.True(assignment.IsTutor);
        await ctx.DisposeAsync();
    }
}

public class GradeCalculationTests
{
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

    [Fact]
    public void AssignmentResult_ScoreExceedsMax_IsInvalid()
    {
        var a = new Assignment { MaxScore = 100 };
        var r = new AssignmentResult { Score = 110 };
        Assert.True(r.Score > a.MaxScore);
    }

    [Fact]
    public void AssignmentResult_ScoreWithinMax_IsValid()
    {
        var a = new Assignment { MaxScore = 100 };
        var r = new AssignmentResult { Score = 85 };
        Assert.True(r.Score <= a.MaxScore);
    }

    [Fact]
    public void Percentage_CalculatedCorrectly()
    {
        var a = new Assignment { MaxScore = 100 };
        var r = new AssignmentResult { Score = 72 };
        var pct = (int)(r.Score * 100.0 / a.MaxScore);
        Assert.Equal(72, pct);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    public void AssignmentResult_ScoreInRange_IsValid(int score)
    {
        var a = new Assignment { MaxScore = 100 };
        var r = new AssignmentResult { Score = score };
        Assert.True(r.Score >= 0 && r.Score <= a.MaxScore);
    }

    [Fact]
    public async Task AssignmentResult_DuplicateForStudent_IsDetected()
    {
        var (ctx, course, student, _) = await Db.WithEnrolmentAsync();
        var assignment = new Assignment { CourseId = course.Id, Title = "CA1", MaxScore = 100, DueDate = DateTime.Today };
        ctx.Assignments.Add(assignment);
        await ctx.SaveChangesAsync();
        ctx.AssignmentResults.Add(new AssignmentResult { AssignmentId = assignment.Id, StudentProfileId = student.Id, Score = 75 });
        await ctx.SaveChangesAsync();
        Assert.True(await ctx.AssignmentResults.AnyAsync(r => r.AssignmentId == assignment.Id && r.StudentProfileId == student.Id));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task MultipleStudents_SameAssignment_AllResultsStored()
    {
        var (ctx, _, course) = await Db.WithCourseAsync();
        var s1 = new StudentProfile { IdentityUserId = "u1", Name = "S1", Email = "s1@t.ie", StudentNumber = "001" };
        var s2 = new StudentProfile { IdentityUserId = "u2", Name = "S2", Email = "s2@t.ie", StudentNumber = "002" };
        ctx.StudentProfiles.AddRange(s1, s2);
        await ctx.SaveChangesAsync();
        var assignment = new Assignment { CourseId = course.Id, Title = "CA2", MaxScore = 100, DueDate = DateTime.Today };
        ctx.Assignments.Add(assignment);
        await ctx.SaveChangesAsync();
        ctx.AssignmentResults.AddRange(
            new AssignmentResult { AssignmentId = assignment.Id, StudentProfileId = s1.Id, Score = 80 },
            new AssignmentResult { AssignmentId = assignment.Id, StudentProfileId = s2.Id, Score = 60 }
        );
        await ctx.SaveChangesAsync();
        Assert.Equal(2, await ctx.AssignmentResults.CountAsync(r => r.AssignmentId == assignment.Id));
        await ctx.DisposeAsync();
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  BRANCHES CONTROLLER
// ══════════════════════════════════════════════════════════════════════════

public class BranchesControllerTests
{
    [Fact]
    public async Task Index_ReturnsAllBranches()
    {
        await using var ctx = Db.Create();
        ctx.Branches.AddRange(new Branch { Name = "Dublin", Address = "1 St" }, new Branch { Name = "Cork", Address = "2 St" });
        await ctx.SaveChangesAsync();
        var result = await new BranchesController(ctx).Index();
        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal(2, Assert.IsAssignableFrom<IEnumerable<Branch>>(view.Model).Count());
    }

    [Fact]
    public async Task Details_NullId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new BranchesController(ctx).Details(null));
    }

    [Fact]
    public async Task Details_UnknownId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new BranchesController(ctx).Details(999));
    }

    [Fact]
    public async Task Details_ValidId_ReturnsViewWithModel()
    {
        await using var ctx = Db.Create();
        var b = new Branch { Name = "Test", Address = "Addr" };
        ctx.Branches.Add(b); await ctx.SaveChangesAsync();
        var result = await new BranchesController(ctx).Details(b.Id);
        var view = Assert.IsType<ViewResult>(result);
        Assert.Equal(b.Id, Assert.IsType<Branch>(view.Model).Id);
    }

    [Fact]
    public async Task Create_Post_ValidModel_RedirectsAndSaves()
    {
        await using var ctx = Db.Create();
        var result = await new BranchesController(ctx).Create(new Branch { Name = "New", Address = "Addr" });
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Equal(1, await ctx.Branches.CountAsync());
    }

    [Fact]
    public async Task Create_Post_InvalidModel_ReturnsView()
    {
        await using var ctx = Db.Create();
        var ctrl = new BranchesController(ctx);
        ctrl.ModelState.AddModelError("Name", "Required");
        Assert.IsType<ViewResult>(await ctrl.Create(new Branch()));
    }

    [Fact]
    public async Task Edit_Get_NullId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new BranchesController(ctx).Edit(null));
    }

    [Fact]
    public async Task Edit_Get_UnknownId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new BranchesController(ctx).Edit(999));
    }

    [Fact]
    public async Task Edit_Get_ValidId_ReturnsView()
    {
        await using var ctx = Db.Create();
        var b = new Branch { Name = "Test", Address = "Addr" };
        ctx.Branches.Add(b); await ctx.SaveChangesAsync();
        Assert.IsType<ViewResult>(await new BranchesController(ctx).Edit(b.Id));
    }

    [Fact]
    public async Task Edit_Post_IdMismatch_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        var b = new Branch { Name = "Test", Address = "Addr" };
        ctx.Branches.Add(b); await ctx.SaveChangesAsync();
        b.Id = 999;
        Assert.IsType<NotFoundResult>(await new BranchesController(ctx).Edit(1, b));
    }

    [Fact]
    public async Task Edit_Post_InvalidModel_ReturnsView()
    {
        await using var ctx = Db.Create();
        var b = new Branch { Name = "Test", Address = "Addr" };
        ctx.Branches.Add(b); await ctx.SaveChangesAsync();
        var ctrl = new BranchesController(ctx);
        ctrl.ModelState.AddModelError("Name", "Required");
        Assert.IsType<ViewResult>(await ctrl.Edit(b.Id, b));
    }

    [Fact]
    public async Task Edit_Post_Valid_RedirectsAndUpdates()
    {
        await using var ctx = Db.Create();
        var b = new Branch { Name = "Test", Address = "Addr" };
        ctx.Branches.Add(b); await ctx.SaveChangesAsync();
        var ctrl = new BranchesController(ctx) { TempData = Db.FakeTempData() };
        b.Name = "Updated";
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(await ctrl.Edit(b.Id, b)).ActionName);
        Assert.Equal("Updated", (await ctx.Branches.FindAsync(b.Id))!.Name);
    }

    [Fact]
    public async Task Delete_Get_NullId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new BranchesController(ctx).Delete(null));
    }

    [Fact]
    public async Task Delete_Get_UnknownId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new BranchesController(ctx).Delete(999));
    }

    [Fact]
    public async Task Delete_Get_ValidId_ReturnsView()
    {
        await using var ctx = Db.Create();
        var b = new Branch { Name = "Test", Address = "Addr" };
        ctx.Branches.Add(b); await ctx.SaveChangesAsync();
        Assert.IsType<ViewResult>(await new BranchesController(ctx).Delete(b.Id));
    }

    [Fact]
    public async Task DeleteConfirmed_UnknownId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new BranchesController(ctx).DeleteConfirmed(999));
    }

    [Fact]
    public async Task DeleteConfirmed_ValidId_DeletesAndRedirects()
    {
        await using var ctx = Db.Create();
        var b = new Branch { Name = "Empty", Address = "Addr" };
        ctx.Branches.Add(b); await ctx.SaveChangesAsync();
        var result = await new BranchesController(ctx).DeleteConfirmed(b.Id);
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Equal(0, await ctx.Branches.CountAsync());
    }

    [Fact]
    public async Task UnhappyPaths_AllReturnNotFound()
    {
        await using var ctx = Db.Create();
        var c = new BranchesController(ctx);
        Assert.IsType<NotFoundResult>(await c.Details(null));
        Assert.IsType<NotFoundResult>(await c.Details(9999));
        Assert.IsType<NotFoundResult>(await c.Edit((int?)null));
        Assert.IsType<NotFoundResult>(await c.Edit(9999));
        Assert.IsType<NotFoundResult>(await c.Edit(1, new Branch { Id = 2 }));
        Assert.IsType<NotFoundResult>(await c.Delete(null));
        Assert.IsType<NotFoundResult>(await c.Delete(9999));
        Assert.IsType<NotFoundResult>(await c.DeleteConfirmed(9999));
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  COURSES CONTROLLER
// ══════════════════════════════════════════════════════════════════════════

public class CoursesControllerTests
{
    [Fact]
    public async Task Index_ReturnsAllCourses()
    {
        var (ctx, _, _) = await Db.WithCourseAsync();
        var result = await new CoursesController(ctx).Index();
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<Course>>(Assert.IsType<ViewResult>(result).Model));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Index_TwoCourses_ReturnsBoth()
    {
        var (ctx, branch, _) = await Db.WithCourseAsync();
        ctx.Courses.Add(new Course { Name = "C2", BranchId = branch.Id, StartDate = DateTime.Today, EndDate = DateTime.Today.AddYears(1) });
        await ctx.SaveChangesAsync();
        var result = await new CoursesController(ctx).Index();
        Assert.Equal(2, Assert.IsAssignableFrom<IEnumerable<Course>>(Assert.IsType<ViewResult>(result).Model).Count());
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Details_NullId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new CoursesController(ctx).Details(null));
    }

    [Fact]
    public async Task Details_UnknownId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new CoursesController(ctx).Details(999));
    }

    [Fact]
    public async Task Details_ValidId_ReturnsView()
    {
        var (ctx, _, course) = await Db.WithCourseAsync();
        var result = await new CoursesController(ctx).Details(course.Id);
        Assert.Equal(course.Id, Assert.IsType<Course>(Assert.IsType<ViewResult>(result).Model).Id);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Create_Post_InvalidModel_ReturnsView()
    {
        var (ctx, _, _) = await Db.WithCourseAsync();
        var ctrl = new CoursesController(ctx);
        ctrl.ModelState.AddModelError("Name", "Required");
        Assert.IsType<ViewResult>(await ctrl.Create(new Course()));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Create_Post_Valid_RedirectsToIndex()
    {
        var (ctx, branch, _) = await Db.WithCourseAsync();
        var ctrl = new CoursesController(ctx) { TempData = Db.FakeTempData() };
        var result = await ctrl.Create(new Course { Name = "New", BranchId = branch.Id, StartDate = DateTime.Today, EndDate = DateTime.Today.AddYears(1) });
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Edit_Get_NullId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new CoursesController(ctx).Edit(null));
    }

    [Fact]
    public async Task Edit_Get_UnknownId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new CoursesController(ctx).Edit(999));
    }

    [Fact]
    public async Task Edit_Get_ValidId_ReturnsView()
    {
        var (ctx, _, course) = await Db.WithCourseAsync();
        Assert.Equal("Test Course", Assert.IsType<Course>(Assert.IsType<ViewResult>(await new CoursesController(ctx).Edit(course.Id)).Model).Name);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Edit_Post_IdMismatch_ReturnsNotFound()
    {
        var (ctx, _, course) = await Db.WithCourseAsync();
        course.Id = 999;
        Assert.IsType<NotFoundResult>(await new CoursesController(ctx).Edit(1, course));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Edit_Post_Valid_RedirectsAndUpdates()
    {
        var (ctx, _, course) = await Db.WithCourseAsync();
        var ctrl = new CoursesController(ctx) { TempData = Db.FakeTempData() };
        course.Name = "Updated Course";
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(await ctrl.Edit(course.Id, course)).ActionName);
        Assert.Equal("Updated Course", (await ctx.Courses.FindAsync(course.Id))!.Name);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Delete_Get_NullId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new CoursesController(ctx).Delete(null));
    }

    [Fact]
    public async Task Delete_Get_UnknownId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new CoursesController(ctx).Delete(999));
    }

    [Fact]
    public async Task Delete_Get_ValidId_ReturnsView()
    {
        var (ctx, _, course) = await Db.WithCourseAsync();
        Assert.IsType<ViewResult>(await new CoursesController(ctx).Delete(course.Id));
        await ctx.DisposeAsync();
    }

    // Le vrai DeleteConfirmed retourne un Redirect (pas NotFound) quand l'id n'existe pas
    [Fact]
    public async Task DeleteConfirmed_UnknownId_RedirectsToIndex()
    {
        await using var ctx = Db.Create();
        Assert.IsType<RedirectToActionResult>(await new CoursesController(ctx).DeleteConfirmed(999));
    }

    // Le vrai code supprime d'abord les enrolments, puis le cours
    [Fact]
    public async Task DeleteConfirmed_WithEnrolments_DeletesEnrolmentsAndCourse()
    {
        var (ctx, _, _, _) = await Db.WithEnrolmentAsync();
        var course = await ctx.Courses.FirstAsync();
        var ctrl = new CoursesController(ctx) { TempData = Db.FakeTempData() };
        await ctrl.DeleteConfirmed(course.Id);
        Assert.Equal(0, await ctx.Courses.CountAsync());
        Assert.Equal(0, await ctx.CourseEnrolments.CountAsync());
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task DeleteConfirmed_NoEnrolments_DeletesAndRedirects()
    {
        var (ctx, _, course) = await Db.WithCourseAsync();
        var ctrl = new CoursesController(ctx) { TempData = Db.FakeTempData() };
        await ctrl.DeleteConfirmed(course.Id);
        Assert.Equal(0, await ctx.Courses.CountAsync());
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task UnhappyPaths_AllReturnExpectedResults()
    {
        await using var ctx = Db.Create();
        var c = new CoursesController(ctx);
        Assert.IsType<NotFoundResult>(await c.Details(9999));
        Assert.IsType<NotFoundResult>(await c.Edit((int?)null));
        Assert.IsType<NotFoundResult>(await c.Edit(9999));
        Assert.IsType<NotFoundResult>(await c.Edit(1, new Course { Id = 2 }));
        Assert.IsType<NotFoundResult>(await c.Delete(null));
        Assert.IsType<NotFoundResult>(await c.Delete(9999));
        Assert.IsType<RedirectToActionResult>(await c.DeleteConfirmed(9999));
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  ENROLMENTS CONTROLLER
// ══════════════════════════════════════════════════════════════════════════

public class EnrolmentsControllerTests
{
    [Fact]
    public async Task Index_AsAdmin_ReturnsAllEnrolments()
    {
        var (ctx, _, _, _) = await Db.WithEnrolmentAsync();
        var ctrl = new EnrolmentsController(ctx);
        As.Admin(ctrl);
        var result = await ctrl.Index(null, null);
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<CourseEnrolment>>(Assert.IsType<ViewResult>(result).Model));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Index_AsStudent_ReturnsOwnEnrolmentsOnly()
    {
        var (ctx, _, student, _) = await Db.WithEnrolmentAsync();
        var ctrl = new EnrolmentsController(ctx);
        As.Student(ctrl, student.IdentityUserId);
        var result = await ctrl.Index(null, null);
        var model = Assert.IsAssignableFrom<IEnumerable<CourseEnrolment>>(Assert.IsType<ViewResult>(result).Model).ToList();
        Assert.Single(model);
        Assert.All(model, e => Assert.Equal(student.Id, e.StudentProfileId));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Index_FilterByCourse_ReturnsFiltered()
    {
        var (ctx, course, _, _) = await Db.WithEnrolmentAsync();
        var ctrl = new EnrolmentsController(ctx);
        As.Admin(ctrl);
        var result = await ctrl.Index(course.Id, null);
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<CourseEnrolment>>(Assert.IsType<ViewResult>(result).Model));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Create_Post_DuplicateEnrolment_ReturnsViewWithError()
    {
        var (ctx, course, student, _) = await Db.WithEnrolmentAsync();
        var ctrl = new EnrolmentsController(ctx);
        var result = await ctrl.Create(new CourseEnrolment
        {
            StudentProfileId = student.Id,
            CourseId = course.Id,
            EnrolDate = DateTime.Today,
            Status = EnrolmentStatus.Active
        });
        Assert.IsType<ViewResult>(result);
        Assert.False(ctrl.ModelState.IsValid);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Create_Post_NewEnrolment_RedirectsToIndex()
    {
        var (ctx, _, course) = await Db.WithCourseAsync();
        var student = new StudentProfile { IdentityUserId = "new-uid", Name = "New", Email = "new@t.ie", StudentNumber = "N001" };
        ctx.StudentProfiles.Add(student);
        await ctx.SaveChangesAsync();
        var ctrl = new EnrolmentsController(ctx);
        var result = await ctrl.Create(new CourseEnrolment
        {
            StudentProfileId = student.Id,
            CourseId = course.Id,
            EnrolDate = DateTime.Today,
            Status = EnrolmentStatus.Active
        });
        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(1, await ctx.CourseEnrolments.CountAsync());
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Edit_Get_NullId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new EnrolmentsController(ctx).Edit(null));
    }

    [Fact]
    public async Task Edit_Get_UnknownId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new EnrolmentsController(ctx).Edit(999));
    }

    [Fact]
    public async Task Edit_Get_ValidId_ReturnsView()
    {
        var (ctx, _, _, enrolment) = await Db.WithEnrolmentAsync();
        Assert.IsType<ViewResult>(await new EnrolmentsController(ctx).Edit(enrolment.Id));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Edit_Post_IdMismatch_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new EnrolmentsController(ctx).Edit(1, new CourseEnrolment { Id = 999 }));
    }

    [Fact]
    public async Task Edit_Post_Valid_RedirectsAndUpdates()
    {
        var (ctx, _, _, enrolment) = await Db.WithEnrolmentAsync();
        enrolment.Status = EnrolmentStatus.Withdrawn;
        var ctrl = new EnrolmentsController(ctx);
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(await ctrl.Edit(enrolment.Id, enrolment)).ActionName);
        Assert.Equal(EnrolmentStatus.Withdrawn, (await ctx.CourseEnrolments.FindAsync(enrolment.Id))!.Status);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Delete_Get_NullId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new EnrolmentsController(ctx).Delete(null));
    }

    [Fact]
    public async Task Delete_Get_UnknownId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new EnrolmentsController(ctx).Delete(999));
    }

    [Fact]
    public async Task Delete_Get_ValidId_ReturnsView()
    {
        var (ctx, _, _, enrolment) = await Db.WithEnrolmentAsync();
        Assert.IsType<ViewResult>(await new EnrolmentsController(ctx).Delete(enrolment.Id));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task DeleteConfirmed_RemovesEnrolment()
    {
        var (ctx, _, _, enrolment) = await Db.WithEnrolmentAsync();
        var ctrl = new EnrolmentsController(ctx) { TempData = Db.FakeTempData() };
        await ctrl.DeleteConfirmed(enrolment.Id);
        Assert.Equal(0, await ctx.CourseEnrolments.CountAsync());
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task DeleteConfirmed_UnknownId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new EnrolmentsController(ctx).DeleteConfirmed(999));
    }

    [Fact]
    public async Task UnhappyPaths_AllReturnNotFound()
    {
        await using var ctx = Db.Create();
        var c = new EnrolmentsController(ctx);
        Assert.IsType<NotFoundResult>(await c.Edit((int?)null));
        Assert.IsType<NotFoundResult>(await c.Edit(9999));
        Assert.IsType<NotFoundResult>(await c.Edit(1, new CourseEnrolment { Id = 2 }));
        Assert.IsType<NotFoundResult>(await c.Delete(null));
        Assert.IsType<NotFoundResult>(await c.Delete(9999));
        Assert.IsType<NotFoundResult>(await c.DeleteConfirmed(9999));
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  ASSIGNMENTS CONTROLLER
// ══════════════════════════════════════════════════════════════════════════

public class AssignmentsControllerTests
{
    [Fact]
    public async Task Index_ReturnsAllAssignments()
    {
        var (ctx, _, course) = await Db.WithCourseAsync();
        ctx.Assignments.AddRange(
            new Assignment { CourseId = course.Id, Title = "CA1", MaxScore = 100, DueDate = DateTime.Today },
            new Assignment { CourseId = course.Id, Title = "CA2", MaxScore = 100, DueDate = DateTime.Today.AddDays(7) }
        );
        await ctx.SaveChangesAsync();
        var result = await new AssignmentsController(ctx).Index();
        Assert.Equal(2, Assert.IsAssignableFrom<IEnumerable<Assignment>>(Assert.IsType<ViewResult>(result).Model).Count());
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Details_NullId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new AssignmentsController(ctx).Details(null));
    }

    [Fact]
    public async Task Details_UnknownId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new AssignmentsController(ctx).Details(999));
    }

    [Fact]
    public async Task Details_ValidId_ReturnsView()
    {
        var (ctx, _, course) = await Db.WithCourseAsync();
        var a = new Assignment { CourseId = course.Id, Title = "CA1", MaxScore = 100, DueDate = DateTime.Today };
        ctx.Assignments.Add(a); await ctx.SaveChangesAsync();
        Assert.IsType<ViewResult>(await new AssignmentsController(ctx).Details(a.Id));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Create_Post_ValidModel_RedirectsAndSaves()
    {
        var (ctx, _, course) = await Db.WithCourseAsync();
        var ctrl = new AssignmentsController(ctx) { TempData = Db.FakeTempData() };
        var result = await ctrl.Create(new Assignment { CourseId = course.Id, Title = "CA1", MaxScore = 100, DueDate = DateTime.Today });
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Equal(1, await ctx.Assignments.CountAsync());
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Create_Post_InvalidModel_ReturnsView()
    {
        var (ctx, _, _) = await Db.WithCourseAsync();
        var ctrl = new AssignmentsController(ctx);
        ctrl.ModelState.AddModelError("Title", "Required");
        Assert.IsType<ViewResult>(await ctrl.Create(new Assignment()));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Edit_Get_NullId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new AssignmentsController(ctx).Edit(null));
    }

    [Fact]
    public async Task Edit_Get_UnknownId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new AssignmentsController(ctx).Edit(999));
    }

    [Fact]
    public async Task Edit_Get_ValidId_ReturnsView()
    {
        var (ctx, _, course) = await Db.WithCourseAsync();
        var a = new Assignment { CourseId = course.Id, Title = "CA1", MaxScore = 100, DueDate = DateTime.Today };
        ctx.Assignments.Add(a); await ctx.SaveChangesAsync();
        Assert.Equal("CA1", Assert.IsType<Assignment>(Assert.IsType<ViewResult>(await new AssignmentsController(ctx).Edit(a.Id)).Model).Title);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Edit_Post_IdMismatch_ReturnsNotFound()
    {
        var (ctx, _, course) = await Db.WithCourseAsync();
        var a = new Assignment { CourseId = course.Id, Title = "CA1", MaxScore = 100, DueDate = DateTime.Today };
        ctx.Assignments.Add(a); await ctx.SaveChangesAsync();
        a.Id = 999;
        Assert.IsType<NotFoundResult>(await new AssignmentsController(ctx).Edit(1, a));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Edit_Post_InvalidModel_ReturnsView()
    {
        var (ctx, _, course) = await Db.WithCourseAsync();
        var a = new Assignment { CourseId = course.Id, Title = "CA1", MaxScore = 100, DueDate = DateTime.Today };
        ctx.Assignments.Add(a); await ctx.SaveChangesAsync();
        var ctrl = new AssignmentsController(ctx);
        ctrl.ModelState.AddModelError("Title", "Required");
        Assert.IsType<ViewResult>(await ctrl.Edit(a.Id, a));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Edit_Post_Valid_RedirectsAndUpdates()
    {
        var (ctx, _, course) = await Db.WithCourseAsync();
        var a = new Assignment { CourseId = course.Id, Title = "CA1", MaxScore = 100, DueDate = DateTime.Today };
        ctx.Assignments.Add(a); await ctx.SaveChangesAsync();
        var ctrl = new AssignmentsController(ctx) { TempData = Db.FakeTempData() };
        a.Title = "Updated";
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(await ctrl.Edit(a.Id, a)).ActionName);
        Assert.Equal("Updated", (await ctx.Assignments.FindAsync(a.Id))!.Title);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Delete_Get_NullId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new AssignmentsController(ctx).Delete(null));
    }

    [Fact]
    public async Task Delete_Get_ValidId_ReturnsView()
    {
        var (ctx, _, course) = await Db.WithCourseAsync();
        var a = new Assignment { CourseId = course.Id, Title = "CA1", MaxScore = 100, DueDate = DateTime.Today };
        ctx.Assignments.Add(a); await ctx.SaveChangesAsync();
        Assert.IsType<ViewResult>(await new AssignmentsController(ctx).Delete(a.Id));
        await ctx.DisposeAsync();
    }

    // DeleteConfirmed retourne Redirect même si l'assignment n'existe pas (le code fait FindAsync + null check sans return NotFound)
    [Fact]
    public async Task DeleteConfirmed_UnknownId_StillRedirects()
    {
        await using var ctx = Db.Create();
        Assert.IsType<RedirectToActionResult>(await new AssignmentsController(ctx).DeleteConfirmed(999));
    }

    [Fact]
    public async Task DeleteConfirmed_ValidId_DeletesAndRedirects()
    {
        var (ctx, _, course) = await Db.WithCourseAsync();
        var a = new Assignment { CourseId = course.Id, Title = "CA1", MaxScore = 100, DueDate = DateTime.Today };
        ctx.Assignments.Add(a); await ctx.SaveChangesAsync();
        await new AssignmentsController(ctx).DeleteConfirmed(a.Id);
        Assert.Equal(0, await ctx.Assignments.CountAsync());
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task UnhappyPaths_AllReturnExpectedResults()
    {
        await using var ctx = Db.Create();
        var c = new AssignmentsController(ctx);
        Assert.IsType<NotFoundResult>(await c.Details(9999));
        Assert.IsType<NotFoundResult>(await c.Edit(9999));
        Assert.IsType<NotFoundResult>(await c.Edit(1, new Assignment { Id = 2 }));
        Assert.IsType<NotFoundResult>(await c.Delete(null));
        Assert.IsType<NotFoundResult>(await c.Delete(9999));
        Assert.IsType<RedirectToActionResult>(await c.DeleteConfirmed(9999));
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  EXAMS CONTROLLER
// ══════════════════════════════════════════════════════════════════════════

public class ExamsControllerTests
{
    [Fact]
    public async Task Index_AsAdmin_ReturnsAllExams()
    {
        var (ctx, _, course) = await Db.WithCourseAsync();
        ctx.Exams.AddRange(
            new Exam { CourseId = course.Id, Title = "E1", Date = DateTime.Today, MaxScore = 100 },
            new Exam { CourseId = course.Id, Title = "E2", Date = DateTime.Today, MaxScore = 100 }
        );
        await ctx.SaveChangesAsync();
        var ctrl = new ExamsController(ctx);
        As.Admin(ctrl);
        var result = await ctrl.Index(null);
        Assert.Equal(2, Assert.IsAssignableFrom<IEnumerable<Exam>>(Assert.IsType<ViewResult>(result).Model).Count());
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Index_FilterByCourse_ReturnsFiltered()
    {
        var (ctx, branch, course1) = await Db.WithCourseAsync();
        var course2 = new Course { Name = "C2", BranchId = branch.Id, StartDate = DateTime.Today, EndDate = DateTime.Today.AddYears(1) };
        ctx.Courses.Add(course2); await ctx.SaveChangesAsync();
        ctx.Exams.AddRange(
            new Exam { CourseId = course1.Id, Title = "E1", Date = DateTime.Today, MaxScore = 100 },
            new Exam { CourseId = course2.Id, Title = "E2", Date = DateTime.Today, MaxScore = 100 }
        );
        await ctx.SaveChangesAsync();
        var ctrl = new ExamsController(ctx);
        As.Admin(ctrl);
        var result = await ctrl.Index(course1.Id);
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<Exam>>(Assert.IsType<ViewResult>(result).Model));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Details_NullId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        var ctrl = new ExamsController(ctx);
        As.Admin(ctrl);
        Assert.IsType<NotFoundResult>(await ctrl.Details(null));
    }

    [Fact]
    public async Task Details_UnknownId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        var ctrl = new ExamsController(ctx);
        As.Admin(ctrl);
        Assert.IsType<NotFoundResult>(await ctrl.Details(999));
    }

    [Fact]
    public async Task Details_ValidId_ReturnsView()
    {
        var (ctx, _, course) = await Db.WithCourseAsync();
        var e = new Exam { CourseId = course.Id, Title = "Midterm", Date = DateTime.Today, MaxScore = 100 };
        ctx.Exams.Add(e); await ctx.SaveChangesAsync();
        var ctrl = new ExamsController(ctx);
        As.Admin(ctrl);
        var result = await ctrl.Details(e.Id);
        Assert.Equal("Midterm", Assert.IsType<Exam>(Assert.IsType<ViewResult>(result).Model).Title);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Create_Post_Valid_RedirectsToIndex()
    {
        var (ctx, _, course) = await Db.WithCourseAsync();
        var ctrl = new ExamsController(ctx) { TempData = Db.FakeTempData() };
        var result = await ctrl.Create(new Exam { CourseId = course.Id, Title = "E1", Date = DateTime.Today, MaxScore = 100 });
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Create_Post_InvalidModel_ReturnsView()
    {
        var (ctx, _, _) = await Db.WithCourseAsync();
        var ctrl = new ExamsController(ctx);
        ctrl.ModelState.AddModelError("Title", "Required");
        Assert.IsType<ViewResult>(await ctrl.Create(new Exam()));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Edit_Get_NullId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new ExamsController(ctx).Edit(null));
    }

    [Fact]
    public async Task Edit_Get_UnknownId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new ExamsController(ctx).Edit(999));
    }

    [Fact]
    public async Task Edit_Post_IdMismatch_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new ExamsController(ctx).Edit(1, new Exam { Id = 999 }));
    }

    [Fact]
    public async Task Edit_Post_InvalidModel_ReturnsView()
    {
        var (ctx, _, course) = await Db.WithCourseAsync();
        var e = new Exam { CourseId = course.Id, Title = "E1", Date = DateTime.Today, MaxScore = 100 };
        ctx.Exams.Add(e); await ctx.SaveChangesAsync();
        var ctrl = new ExamsController(ctx);
        ctrl.ModelState.AddModelError("Title", "Required");
        Assert.IsType<ViewResult>(await ctrl.Edit(e.Id, e));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Edit_Post_Valid_RedirectsAndUpdates()
    {
        var (ctx, _, course) = await Db.WithCourseAsync();
        var e = new Exam { CourseId = course.Id, Title = "E1", Date = DateTime.Today, MaxScore = 100 };
        ctx.Exams.Add(e); await ctx.SaveChangesAsync();
        var ctrl = new ExamsController(ctx) { TempData = Db.FakeTempData() };
        e.Title = "Updated";
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(await ctrl.Edit(e.Id, e)).ActionName);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task ReleaseResults_UnknownId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new ExamsController(ctx).ReleaseResults(999));
    }

    [Fact]
    public async Task ReleaseResults_SetsFlag_AndRedirectsToDetails()
    {
        var (ctx, _, course) = await Db.WithCourseAsync();
        var e = new Exam { CourseId = course.Id, Title = "E1", Date = DateTime.Today, MaxScore = 100, ResultsReleased = false };
        ctx.Exams.Add(e); await ctx.SaveChangesAsync();
        var ctrl = new ExamsController(ctx) { TempData = Db.FakeTempData() };
        var result = await ctrl.ReleaseResults(e.Id);
        Assert.Equal("Details", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.True((await ctx.Exams.FindAsync(e.Id))!.ResultsReleased);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task AddResult_ValidData_SavesAndRedirectsToDetails()
    {
        var (ctx, course, student, _) = await Db.WithEnrolmentAsync();
        var e = new Exam { CourseId = course.Id, Title = "Final", Date = DateTime.Today, MaxScore = 100 };
        ctx.Exams.Add(e); await ctx.SaveChangesAsync();
        var ctrl = new ExamsController(ctx) { TempData = Db.FakeTempData() };
        As.Admin(ctrl);
        var result = await ctrl.AddResult(new ExamResult { ExamId = e.Id, StudentProfileId = student.Id, Score = 88, Grade = "A2" });
        Assert.Equal("Details", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Equal(1, await ctx.ExamResults.CountAsync());
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task AddResult_ScoreExceedsMax_ReturnsViewWithError()
    {
        var (ctx, course, student, _) = await Db.WithEnrolmentAsync();
        var e = new Exam { CourseId = course.Id, Title = "E1", Date = DateTime.Today, MaxScore = 100 };
        ctx.Exams.Add(e); await ctx.SaveChangesAsync();
        var ctrl = new ExamsController(ctx);
        As.Admin(ctrl);
        var result = await ctrl.AddResult(new ExamResult { ExamId = e.Id, StudentProfileId = student.Id, Score = 150 });
        Assert.IsType<ViewResult>(result);
        Assert.False(ctrl.ModelState.IsValid);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Delete_Get_NullId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new ExamsController(ctx).Delete(null));
    }

    [Fact]
    public async Task Delete_Get_ValidId_ReturnsView()
    {
        var (ctx, _, course) = await Db.WithCourseAsync();
        var e = new Exam { CourseId = course.Id, Title = "E1", Date = DateTime.Today, MaxScore = 100 };
        ctx.Exams.Add(e); await ctx.SaveChangesAsync();
        Assert.IsType<ViewResult>(await new ExamsController(ctx).Delete(e.Id));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task DeleteConfirmed_ValidId_DeletesAndRedirects()
    {
        var (ctx, _, course) = await Db.WithCourseAsync();
        var e = new Exam { CourseId = course.Id, Title = "E1", Date = DateTime.Today, MaxScore = 100 };
        ctx.Exams.Add(e); await ctx.SaveChangesAsync();
        await new ExamsController(ctx).DeleteConfirmed(e.Id);
        Assert.Equal(0, await ctx.Exams.CountAsync());
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task DeleteConfirmed_UnknownId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new ExamsController(ctx).DeleteConfirmed(999));
    }

    [Fact]
    public async Task UnhappyPaths_AllReturnNotFound()
    {
        await using var ctx = Db.Create();
        var c = new ExamsController(ctx);
        Assert.IsType<NotFoundResult>(await c.Details(9999));
        Assert.IsType<NotFoundResult>(await c.Edit((int?)null));
        Assert.IsType<NotFoundResult>(await c.Edit(9999));
        Assert.IsType<NotFoundResult>(await c.Edit(1, new Exam { Id = 2 }));
        Assert.IsType<NotFoundResult>(await c.Delete(null));
        Assert.IsType<NotFoundResult>(await c.Delete(9999));
        Assert.IsType<NotFoundResult>(await c.DeleteConfirmed(9999));
        Assert.IsType<NotFoundResult>(await c.ReleaseResults(9999));
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  ATTENDANCE CONTROLLER
// ══════════════════════════════════════════════════════════════════════════

public class AttendanceControllerTests
{
    [Fact]
    public async Task Index_NullEnrolmentId_RedirectsToEnrolments()
    {
        await using var ctx = Db.Create();
        var ctrl = new AttendanceController(ctx);
        As.Admin(ctrl);
        var result = await ctrl.Index(null);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Index", redirect.ActionName);
        Assert.Equal("Enrolments", redirect.ControllerName);
    }

    [Fact]
    public async Task Index_UnknownEnrolmentId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        var ctrl = new AttendanceController(ctx);
        As.Admin(ctrl);
        Assert.IsType<NotFoundResult>(await ctrl.Index(999));
    }

    [Fact]
    public async Task Index_ValidId_ReturnsViewWithCourseEnrolmentModel()
    {
        var (ctx, _, _, enrolment) = await Db.WithEnrolmentAsync();
        ctx.AttendanceRecords.Add(new AttendanceRecord
        {
            CourseEnrolmentId = enrolment.Id,
            WeekNumber = 1,
            SessionDate = DateTime.Today,
            Present = true
        });
        await ctx.SaveChangesAsync();
        var ctrl = new AttendanceController(ctx);
        As.Admin(ctrl);
        var result = await ctrl.Index(enrolment.Id);
        var view = Assert.IsType<ViewResult>(result);
        // Le controller retourne la CourseEnrolment comme modèle
        var model = Assert.IsType<CourseEnrolment>(view.Model);
        Assert.Equal(enrolment.Id, model.Id);
        await ctx.DisposeAsync();
    }

    // Create GET prend un int (pas nullable) — retourne View directement sans vérifier si l'enrolment existe
    [Fact]
    public async Task Create_Get_ValidEnrolmentId_ReturnsView()
    {
        var (ctx, _, _, enrolment) = await Db.WithEnrolmentAsync();
        Assert.IsType<ViewResult>(new AttendanceController(ctx).Create(enrolment.Id));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Create_Post_Valid_RedirectsToIndex()
    {
        var (ctx, _, _, enrolment) = await Db.WithEnrolmentAsync();
        var ctrl = new AttendanceController(ctx) { TempData = Db.FakeTempData() };
        var record = new AttendanceRecord
        {
            CourseEnrolmentId = enrolment.Id,
            WeekNumber = 1,
            SessionDate = DateTime.Today,
            Present = true
        };
        var result = await ctrl.Create(record);
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Equal(1, await ctx.AttendanceRecords.CountAsync());
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Create_Post_InvalidModel_ReturnsView()
    {
        var (ctx, _, _, enrolment) = await Db.WithEnrolmentAsync();
        var ctrl = new AttendanceController(ctx);
        ctrl.ModelState.AddModelError("WeekNumber", "Required");
        var result = await ctrl.Create(new AttendanceRecord { CourseEnrolmentId = enrolment.Id });
        Assert.IsType<ViewResult>(result);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Toggle_ChangesPresenceFromFalseToTrue()
    {
        var (ctx, _, _, enrolment) = await Db.WithEnrolmentAsync();
        var record = new AttendanceRecord
        {
            CourseEnrolmentId = enrolment.Id,
            WeekNumber = 1,
            SessionDate = DateTime.Today,
            Present = false
        };
        ctx.AttendanceRecords.Add(record); await ctx.SaveChangesAsync();
        var ctrl = new AttendanceController(ctx) { TempData = Db.FakeTempData() };
        As.Admin(ctrl);
        await ctrl.Toggle(record.Id, enrolment.Id);
        Assert.True((await ctx.AttendanceRecords.FindAsync(record.Id))!.Present);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Toggle_ChangesPresenceFromTrueToFalse()
    {
        var (ctx, _, _, enrolment) = await Db.WithEnrolmentAsync();
        var record = new AttendanceRecord
        {
            CourseEnrolmentId = enrolment.Id,
            WeekNumber = 1,
            SessionDate = DateTime.Today,
            Present = true
        };
        ctx.AttendanceRecords.Add(record); await ctx.SaveChangesAsync();
        var ctrl = new AttendanceController(ctx) { TempData = Db.FakeTempData() };
        As.Admin(ctrl);
        await ctrl.Toggle(record.Id, enrolment.Id);
        Assert.False((await ctx.AttendanceRecords.FindAsync(record.Id))!.Present);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Toggle_UnknownId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new AttendanceController(ctx).Toggle(999, 1));
    }

    [Fact]
    public async Task UnhappyPaths_AllReturnNotFound()
    {
        await using var ctx = Db.Create();
        var ctrl = new AttendanceController(ctx);
        Assert.IsType<NotFoundResult>(await ctrl.Toggle(9999, 1));
        As.Admin(ctrl);
        Assert.IsType<NotFoundResult>(await ctrl.Index(9999));
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  STUDENT PROFILES CONTROLLER
// ══════════════════════════════════════════════════════════════════════════

public class StudentProfilesControllerTests
{
    [Fact]
    public async Task Index_AsAdmin_ReturnsAllStudents()
    {
        var (ctx, _, _, _) = await Db.WithEnrolmentAsync();
        var ctrl = new StudentProfilesController(ctx);
        As.Admin(ctrl);
        var result = await ctrl.Index();
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<StudentProfile>>(Assert.IsType<ViewResult>(result).Model));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Index_AsFaculty_ReturnsOnlyTheirStudents()
    {
        var (ctx, course, _, _) = await Db.WithEnrolmentAsync();
        var faculty = new FacultyProfile { IdentityUserId = "fac-id", Name = "Dr F", Email = "f@t.ie" };
        ctx.FacultyProfiles.Add(faculty); await ctx.SaveChangesAsync();
        ctx.FacultyCourseAssignments.Add(new FacultyCourseAssignment { FacultyProfileId = faculty.Id, CourseId = course.Id });
        await ctx.SaveChangesAsync();
        var ctrl = new StudentProfilesController(ctx);
        As.Faculty(ctrl, "fac-id");
        var result = await ctrl.Index();
        Assert.Single(Assert.IsAssignableFrom<IEnumerable<StudentProfile>>(Assert.IsType<ViewResult>(result).Model));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Details_NullId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        var ctrl = new StudentProfilesController(ctx);
        As.Admin(ctrl);
        Assert.IsType<NotFoundResult>(await ctrl.Details(null));
    }

    [Fact]
    public async Task Details_UnknownId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        var ctrl = new StudentProfilesController(ctx);
        As.Admin(ctrl);
        Assert.IsType<NotFoundResult>(await ctrl.Details(999));
    }

    [Fact]
    public async Task Details_AsAdmin_ValidId_ReturnsView()
    {
        var (ctx, _, student, _) = await Db.WithEnrolmentAsync();
        var ctrl = new StudentProfilesController(ctx);
        As.Admin(ctrl);
        Assert.IsType<ViewResult>(await ctrl.Details(student.Id));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Details_AsStudent_OwnProfile_ReturnsView()
    {
        var (ctx, _, student, _) = await Db.WithEnrolmentAsync();
        var ctrl = new StudentProfilesController(ctx);
        As.Student(ctrl, student.IdentityUserId);
        Assert.IsType<ViewResult>(await ctrl.Details(student.Id));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Create_Post_ValidModel_RedirectsAndSaves()
    {
        await using var ctx = Db.Create();
        var ctrl = new StudentProfilesController(ctx);
        As.Admin(ctrl);
        var result = await ctrl.Create(new StudentProfile
        {
            IdentityUserId = "new-uid",
            Name = "New Student",
            Email = "new@t.ie",
            StudentNumber = "N001"
        });
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Equal(1, await ctx.StudentProfiles.CountAsync());
    }

    [Fact]
    public async Task Create_Post_InvalidModel_ReturnsView()
    {
        await using var ctx = Db.Create();
        var ctrl = new StudentProfilesController(ctx);
        As.Admin(ctrl);
        ctrl.ModelState.AddModelError("Name", "Required");
        Assert.IsType<ViewResult>(await ctrl.Create(new StudentProfile()));
    }

    [Fact]
    public async Task Edit_Get_NullId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new StudentProfilesController(ctx).Edit(null));
    }

    [Fact]
    public async Task Edit_Get_UnknownId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new StudentProfilesController(ctx).Edit(999));
    }

    [Fact]
    public async Task Edit_Get_ValidId_ReturnsView()
    {
        var (ctx, _, student, _) = await Db.WithEnrolmentAsync();
        Assert.IsType<ViewResult>(await new StudentProfilesController(ctx).Edit(student.Id));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Edit_Post_IdMismatch_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new StudentProfilesController(ctx).Edit(1, new StudentProfile { Id = 999 }));
    }

    [Fact]
    public async Task Edit_Post_Valid_RedirectsAndUpdates()
    {
        var (ctx, _, student, _) = await Db.WithEnrolmentAsync();
        var ctrl = new StudentProfilesController(ctx) { TempData = Db.FakeTempData() };
        student.Name = "Updated";
        var result = await ctrl.Edit(student.Id, student);
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Equal("Updated", (await ctx.StudentProfiles.FindAsync(student.Id))!.Name);
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task Delete_Get_NullId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new StudentProfilesController(ctx).Delete(null));
    }

    [Fact]
    public async Task Delete_Get_UnknownId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new StudentProfilesController(ctx).Delete(999));
    }

    [Fact]
    public async Task Delete_Get_ValidId_ReturnsView()
    {
        var (ctx, _, student, _) = await Db.WithEnrolmentAsync();
        Assert.IsType<ViewResult>(await new StudentProfilesController(ctx).Delete(student.Id));
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task DeleteConfirmed_ValidId_DeletesAndRedirects()
    {
        var (ctx, _, student, _) = await Db.WithEnrolmentAsync();
        var ctrl = new StudentProfilesController(ctx) { TempData = Db.FakeTempData() };
        await ctrl.DeleteConfirmed(student.Id);
        Assert.Equal(0, await ctx.StudentProfiles.CountAsync());
        await ctx.DisposeAsync();
    }

    [Fact]
    public async Task DeleteConfirmed_UnknownId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new StudentProfilesController(ctx).DeleteConfirmed(999));
    }

    [Fact]
    public async Task UnhappyPaths_AllReturnNotFound()
    {
        await using var ctx = Db.Create();
        var c = new StudentProfilesController(ctx);
        Assert.IsType<NotFoundResult>(await c.Details(9999));
        Assert.IsType<NotFoundResult>(await c.Edit(9999));
        Assert.IsType<NotFoundResult>(await c.Edit(1, new StudentProfile { Id = 2 }));
        Assert.IsType<NotFoundResult>(await c.Delete(null));
        Assert.IsType<NotFoundResult>(await c.Delete(9999));
        Assert.IsType<NotFoundResult>(await c.DeleteConfirmed(9999));
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  FACULTY PROFILES CONTROLLER
// ══════════════════════════════════════════════════════════════════════════

public class FacultyProfilesControllerTests
{
    [Fact]
    public async Task Index_ReturnsAllFaculty()
    {
        await using var ctx = Db.Create();
        ctx.FacultyProfiles.AddRange(
            new FacultyProfile { IdentityUserId = "u1", Name = "F1", Email = "f1@t.ie" },
            new FacultyProfile { IdentityUserId = "u2", Name = "F2", Email = "f2@t.ie" }
        );
        await ctx.SaveChangesAsync();
        var result = await new FacultyProfilesController(ctx).Index();
        Assert.Equal(2, Assert.IsAssignableFrom<IEnumerable<FacultyProfile>>(Assert.IsType<ViewResult>(result).Model).Count());
    }

    [Fact]
    public async Task Details_NullId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new FacultyProfilesController(ctx).Details(null));
    }

    [Fact]
    public async Task Details_UnknownId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new FacultyProfilesController(ctx).Details(999));
    }

    [Fact]
    public async Task Details_ValidId_ReturnsView()
    {
        await using var ctx = Db.Create();
        var f = new FacultyProfile { IdentityUserId = "u1", Name = "F1", Email = "f1@t.ie" };
        ctx.FacultyProfiles.Add(f); await ctx.SaveChangesAsync();
        Assert.IsType<ViewResult>(await new FacultyProfilesController(ctx).Details(f.Id));
    }

    [Fact]
    public async Task Create_Post_ValidModel_RedirectsAndSaves()
    {
        await using var ctx = Db.Create();
        var result = await new FacultyProfilesController(ctx).Create(new FacultyProfile
        {
            IdentityUserId = "u1",
            Name = "Dr New",
            Email = "new@t.ie"
        });
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Equal(1, await ctx.FacultyProfiles.CountAsync());
    }

    [Fact]
    public async Task Edit_Get_NullId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new FacultyProfilesController(ctx).Edit(null));
    }

    [Fact]
    public async Task Edit_Get_UnknownId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new FacultyProfilesController(ctx).Edit(999));
    }

    [Fact]
    public async Task Edit_Get_ValidId_ReturnsView()
    {
        await using var ctx = Db.Create();
        var f = new FacultyProfile { IdentityUserId = "u1", Name = "F1", Email = "f1@t.ie" };
        ctx.FacultyProfiles.Add(f); await ctx.SaveChangesAsync();
        Assert.IsType<ViewResult>(await new FacultyProfilesController(ctx).Edit(f.Id));
    }

    [Fact]
    public async Task Edit_Post_IdMismatch_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new FacultyProfilesController(ctx).Edit(1, new FacultyProfile { Id = 999 }));
    }

    [Fact]
    public async Task Edit_Post_Valid_RedirectsAndUpdates()
    {
        await using var ctx = Db.Create();
        var f = new FacultyProfile { IdentityUserId = "u1", Name = "F1", Email = "f1@t.ie" };
        ctx.FacultyProfiles.Add(f); await ctx.SaveChangesAsync();
        var ctrl = new FacultyProfilesController(ctx) { TempData = Db.FakeTempData() };
        f.Name = "Updated";
        var result = await ctrl.Edit(f.Id, f);
        Assert.Equal("Index", Assert.IsType<RedirectToActionResult>(result).ActionName);
        Assert.Equal("Updated", (await ctx.FacultyProfiles.FindAsync(f.Id))!.Name);
    }

    [Fact]
    public async Task Delete_Get_NullId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new FacultyProfilesController(ctx).Delete(null));
    }

    [Fact]
    public async Task Delete_Get_ValidId_ReturnsView()
    {
        await using var ctx = Db.Create();
        var f = new FacultyProfile { IdentityUserId = "u1", Name = "F1", Email = "f1@t.ie" };
        ctx.FacultyProfiles.Add(f); await ctx.SaveChangesAsync();
        Assert.IsType<ViewResult>(await new FacultyProfilesController(ctx).Delete(f.Id));
    }

    [Fact]
    public async Task DeleteConfirmed_ValidId_DeletesAndRedirects()
    {
        await using var ctx = Db.Create();
        var f = new FacultyProfile { IdentityUserId = "u1", Name = "F1", Email = "f1@t.ie" };
        ctx.FacultyProfiles.Add(f); await ctx.SaveChangesAsync();
        await new FacultyProfilesController(ctx).DeleteConfirmed(f.Id);
        Assert.Equal(0, await ctx.FacultyProfiles.CountAsync());
    }

    [Fact]
    public async Task DeleteConfirmed_UnknownId_ReturnsNotFound()
    {
        await using var ctx = Db.Create();
        Assert.IsType<NotFoundResult>(await new FacultyProfilesController(ctx).DeleteConfirmed(999));
    }

    [Fact]
    public async Task AssignCourse_ValidData_RedirectsToDetails()
    {
        await using var ctx = Db.Create();
        var f = new FacultyProfile { IdentityUserId = "u1", Name = "F1", Email = "f1@t.ie" };
        ctx.FacultyProfiles.Add(f); await ctx.SaveChangesAsync();
        var branch = new Branch { Name = "B", Address = "A" };
        ctx.Branches.Add(branch); await ctx.SaveChangesAsync();
        var course = new Course { Name = "C", BranchId = branch.Id, StartDate = DateTime.Today, EndDate = DateTime.Today.AddYears(1) };
        ctx.Courses.Add(course); await ctx.SaveChangesAsync();

        var result = await new FacultyProfilesController(ctx).AssignCourse(f.Id, course.Id, true);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal(f.Id, redirect.RouteValues!["id"]);
        Assert.Single(ctx.FacultyCourseAssignments);
    }

    [Fact]
    public async Task AssignCourse_Duplicate_DoesNotCreateDuplicate()
    {
        await using var ctx = Db.Create();
        var f = new FacultyProfile { IdentityUserId = "u1", Name = "F1", Email = "f1@t.ie" };
        ctx.FacultyProfiles.Add(f); await ctx.SaveChangesAsync();
        var branch = new Branch { Name = "B", Address = "A" };
        ctx.Branches.Add(branch); await ctx.SaveChangesAsync();
        var course = new Course { Name = "C", BranchId = branch.Id, StartDate = DateTime.Today, EndDate = DateTime.Today.AddYears(1) };
        ctx.Courses.Add(course); await ctx.SaveChangesAsync();
        ctx.FacultyCourseAssignments.Add(new FacultyCourseAssignment { FacultyProfileId = f.Id, CourseId = course.Id });
        await ctx.SaveChangesAsync();

        await new FacultyProfilesController(ctx).AssignCourse(f.Id, course.Id, false);
        // Le vrai code vérifie le doublon et ne recrée pas
        Assert.Equal(1, await ctx.FacultyCourseAssignments.CountAsync());
    }

    [Fact]
    public async Task RemoveCourseAssignment_ValidId_RemovesAndRedirects()
    {
        await using var ctx = Db.Create();
        var f = new FacultyProfile { IdentityUserId = "u1", Name = "F1", Email = "f1@t.ie" };
        ctx.FacultyProfiles.Add(f); await ctx.SaveChangesAsync();
        var branch = new Branch { Name = "B", Address = "A" };
        ctx.Branches.Add(branch); await ctx.SaveChangesAsync();
        var course = new Course { Name = "C", BranchId = branch.Id, StartDate = DateTime.Today, EndDate = DateTime.Today.AddYears(1) };
        ctx.Courses.Add(course); await ctx.SaveChangesAsync();
        var assignment = new FacultyCourseAssignment { FacultyProfileId = f.Id, CourseId = course.Id };
        ctx.FacultyCourseAssignments.Add(assignment); await ctx.SaveChangesAsync();

        var result = await new FacultyProfilesController(ctx).RemoveCourseAssignment(assignment.Id);
        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("Details", redirect.ActionName);
        Assert.Equal(0, await ctx.FacultyCourseAssignments.CountAsync());
    }

    [Fact]
    public async Task UnhappyPaths_AllReturnNotFound()
    {
        await using var ctx = Db.Create();
        var c = new FacultyProfilesController(ctx);
        Assert.IsType<NotFoundResult>(await c.Details(null));
        Assert.IsType<NotFoundResult>(await c.Details(9999));
        Assert.IsType<NotFoundResult>(await c.Edit((int?)null));
        Assert.IsType<NotFoundResult>(await c.Edit(9999));
        Assert.IsType<NotFoundResult>(await c.Edit(1, new FacultyProfile { Id = 2 }));
        Assert.IsType<NotFoundResult>(await c.Delete(null));
        Assert.IsType<NotFoundResult>(await c.Delete(9999));
        Assert.IsType<NotFoundResult>(await c.DeleteConfirmed(9999));
    }
}

// ══════════════════════════════════════════════════════════════════════════
//  HOME CONTROLLER
// ══════════════════════════════════════════════════════════════════════════

public class HomeControllerTests
{
    [Fact]
    public void Index_ReturnsView()
    {
        // HomeController.Index retourne View() directement
        var ctrl = new HomeController();
        ctrl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        Assert.IsType<ViewResult>(ctrl.Index());
    }

    [Fact]
    public void Privacy_ReturnsView()
    {
        var ctrl = new HomeController();
        ctrl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        Assert.IsType<ViewResult>(ctrl.Privacy());
    }

    [Fact]
    public void Error_WithTraceIdentifier_PopulatesRequestId()
    {
        var ctrl = new HomeController();
        var httpContext = new DefaultHttpContext();
        httpContext.TraceIdentifier = "trace-123";
        ctrl.ControllerContext = new ControllerContext { HttpContext = httpContext };

        var result = ctrl.Error();
        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ErrorViewModel>(view.Model);
        Assert.Equal("trace-123", model.RequestId);
        Assert.True(model.ShowRequestId);
    }

    [Fact]
    public void Error_RequestId_ShowRequestId_IsTrue()
    {
        var ctrl = new HomeController();
        ctrl.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        var result = ctrl.Error();
        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<ErrorViewModel>(view.Model);
        // TraceIdentifier est toujours non-null sur DefaultHttpContext
        Assert.True(model.ShowRequestId);
    }
}