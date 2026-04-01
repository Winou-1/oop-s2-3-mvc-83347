using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using VgcCollege.Web.Domain;

namespace VgcCollege.Web.Data
{

    public static class SeedData
    {
        public static async Task InitialiseAsync(IServiceProvider services)
        {
            var ctx = services.GetRequiredService<ApplicationDbContext>();
            var userMgr = services.GetRequiredService<UserManager<IdentityUser>>();
            var roleMgr = services.GetRequiredService<RoleManager<IdentityRole>>();

            await ctx.Database.MigrateAsync();

            // ── Roles ──────────────────────────────────────────────────────────
            foreach (var role in new[] { "Admin", "Faculty", "Student" })
                if (!await roleMgr.RoleExistsAsync(role))
                    await roleMgr.CreateAsync(new IdentityRole(role));

            // ── Admin user ────────────────────────────────────────────────────
            await EnsureUserAsync(userMgr, "admin@vgc.ie", "Admin@1234", "Admin");

            if (await ctx.Branches.AnyAsync()) return; // already seeded

            // ── Branches ──────────────────────────────────────────────────────
            var branches = new[]
            {
            new Branch { Name = "Dublin City Centre", Address = "42 O'Connell Street, Dublin 1" },
            new Branch { Name = "Cork Campus",        Address = "15 Grand Parade, Cork"          },
            new Branch { Name = "Galway West",        Address = "7 Eyre Square, Galway"          },
        };
            ctx.Branches.AddRange(branches);
            await ctx.SaveChangesAsync();

            // ── Courses ───────────────────────────────────────────────────────
            var today = DateTime.Today;
            var courses = new[]
            {
            new Course { Name = "Software Development Year 1", BranchId = branches[0].Id, StartDate = today.AddMonths(-4), EndDate = today.AddMonths(4)  },
            new Course { Name = "Software Development Year 2", BranchId = branches[0].Id, StartDate = today.AddMonths(-4), EndDate = today.AddMonths(4)  },
            new Course { Name = "Business Analytics",          BranchId = branches[1].Id, StartDate = today.AddMonths(-3), EndDate = today.AddMonths(5)  },
            new Course { Name = "Digital Marketing",           BranchId = branches[1].Id, StartDate = today.AddMonths(-2), EndDate = today.AddMonths(6)  },
            new Course { Name = "Data Science",                BranchId = branches[2].Id, StartDate = today.AddMonths(-5), EndDate = today.AddMonths(3)  },
            new Course { Name = "Cybersecurity Fundamentals",  BranchId = branches[2].Id, StartDate = today.AddMonths(-4), EndDate = today.AddMonths(4)  },
        };
            ctx.Courses.AddRange(courses);
            await ctx.SaveChangesAsync();

            // ── Faculty users + profiles ───────────────────────────────────────
            var f1User = await EnsureUserAsync(userMgr, "faculty1@vgc.ie", "Faculty@1234", "Faculty");
            var f2User = await EnsureUserAsync(userMgr, "faculty2@vgc.ie", "Faculty@1234", "Faculty");

            var faculty1 = new FacultyProfile { IdentityUserId = f1User.Id, Name = "Dr. Aoife Murphy", Email = "faculty1@vgc.ie", Phone = "+353 1 234 5678" };
            var faculty2 = new FacultyProfile { IdentityUserId = f2User.Id, Name = "Prof. Séan O'Brien", Email = "faculty2@vgc.ie", Phone = "+353 21 987 6543" };
            ctx.FacultyProfiles.AddRange(faculty1, faculty2);
            await ctx.SaveChangesAsync();

            ctx.FacultyCourseAssignments.AddRange(
                new FacultyCourseAssignment { FacultyProfileId = faculty1.Id, CourseId = courses[0].Id, IsTutor = true },
                new FacultyCourseAssignment { FacultyProfileId = faculty1.Id, CourseId = courses[1].Id, IsTutor = true },
                new FacultyCourseAssignment { FacultyProfileId = faculty2.Id, CourseId = courses[2].Id, IsTutor = true },
                new FacultyCourseAssignment { FacultyProfileId = faculty2.Id, CourseId = courses[4].Id, IsTutor = false }
            );
            await ctx.SaveChangesAsync();

            // ── Student users + profiles ──────────────────────────────────────
            var s1User = await EnsureUserAsync(userMgr, "student1@vgc.ie", "Student@1234", "Student");
            var s2User = await EnsureUserAsync(userMgr, "student2@vgc.ie", "Student@1234", "Student");
            var s3User = await EnsureUserAsync(userMgr, "student3@vgc.ie", "Student@1234", "Student");
            var s4User = await EnsureUserAsync(userMgr, "student4@vgc.ie", "Student@1234", "Student");

            var students = new[]
            {
            new StudentProfile { IdentityUserId = s1User.Id, Name = "Ciarán Kelly",        Email = "student1@vgc.ie", StudentNumber = "VGC2024001", Phone = "+353 85 111 2222", DateOfBirth = new DateTime(2003, 5, 15),  Address = "10 Parnell St, Dublin 1"  },
            new StudentProfile { IdentityUserId = s2User.Id, Name = "Siobhán Walsh",       Email = "student2@vgc.ie", StudentNumber = "VGC2024002", Phone = "+353 86 333 4444", DateOfBirth = new DateTime(2002, 9, 22),  Address = "22 Grafton St, Dublin 2"  },
            new StudentProfile { IdentityUserId = s3User.Id, Name = "Darragh Ó'Sullivan",  Email = "student3@vgc.ie", StudentNumber = "VGC2024003", Phone = "+353 87 555 6666", DateOfBirth = new DateTime(2003, 1, 8),   Address = "5 Patrick St, Cork"       },
            new StudentProfile { IdentityUserId = s4User.Id, Name = "Niamh Brennan",       Email = "student4@vgc.ie", StudentNumber = "VGC2024004", Phone = "+353 89 777 8888", DateOfBirth = new DateTime(2001, 11, 30), Address = "3 Shop St, Galway"        },
        };
            ctx.StudentProfiles.AddRange(students);
            await ctx.SaveChangesAsync();

            // ── Enrolments ────────────────────────────────────────────────────
            var enrolments = new[]
            {
            new CourseEnrolment { StudentProfileId = students[0].Id, CourseId = courses[0].Id, EnrolDate = today.AddMonths(-4), Status = EnrolmentStatus.Active },
            new CourseEnrolment { StudentProfileId = students[1].Id, CourseId = courses[0].Id, EnrolDate = today.AddMonths(-4), Status = EnrolmentStatus.Active },
            new CourseEnrolment { StudentProfileId = students[1].Id, CourseId = courses[1].Id, EnrolDate = today.AddMonths(-4), Status = EnrolmentStatus.Active },
            new CourseEnrolment { StudentProfileId = students[2].Id, CourseId = courses[2].Id, EnrolDate = today.AddMonths(-3), Status = EnrolmentStatus.Active },
            new CourseEnrolment { StudentProfileId = students[3].Id, CourseId = courses[4].Id, EnrolDate = today.AddMonths(-5), Status = EnrolmentStatus.Active },
            new CourseEnrolment { StudentProfileId = students[0].Id, CourseId = courses[5].Id, EnrolDate = today.AddMonths(-4), Status = EnrolmentStatus.Active },
        };
            ctx.CourseEnrolments.AddRange(enrolments);
            await ctx.SaveChangesAsync();

            // ── Attendance (weeks 1–8) ────────────────────────────────────────
            var rng = new Random(42);
            foreach (var e in enrolments)
                for (int w = 1; w <= 8; w++)
                    ctx.AttendanceRecords.Add(new AttendanceRecord
                    {
                        CourseEnrolmentId = e.Id,
                        WeekNumber = w,
                        SessionDate = today.AddMonths(-4).AddDays(w * 7),
                        Present = rng.NextDouble() > 0.2
                    });
            await ctx.SaveChangesAsync();

            // ── Assignments ───────────────────────────────────────────────────
            var assignments = new[]
            {
            new Assignment { CourseId = courses[0].Id, Title = "Lab 1 – Hello World",        MaxScore = 100, DueDate = today.AddMonths(-3) },
            new Assignment { CourseId = courses[0].Id, Title = "Lab 2 – OOP Fundamentals",   MaxScore = 100, DueDate = today.AddMonths(-2) },
            new Assignment { CourseId = courses[1].Id, Title = "Project 1 – MVC App",        MaxScore = 100, DueDate = today.AddMonths(-1) },
            new Assignment { CourseId = courses[2].Id, Title = "Data Analysis Report",       MaxScore =  50, DueDate = today.AddMonths(-2) },
            new Assignment { CourseId = courses[4].Id, Title = "Python Data Wrangling",      MaxScore = 100, DueDate = today.AddMonths(-3) },
        };
            ctx.Assignments.AddRange(assignments);
            await ctx.SaveChangesAsync();

            // ── Assignment results ────────────────────────────────────────────
            ctx.AssignmentResults.AddRange(
                new AssignmentResult { AssignmentId = assignments[0].Id, StudentProfileId = students[0].Id, Score = 85, Feedback = "Good work, minor formatting issues." },
                new AssignmentResult { AssignmentId = assignments[0].Id, StudentProfileId = students[1].Id, Score = 92, Feedback = "Excellent submission." },
                new AssignmentResult { AssignmentId = assignments[1].Id, StudentProfileId = students[0].Id, Score = 78, Feedback = "Solid OOP understanding." },
                new AssignmentResult { AssignmentId = assignments[1].Id, StudentProfileId = students[1].Id, Score = 88, Feedback = "Well-structured, good encapsulation." },
                new AssignmentResult { AssignmentId = assignments[2].Id, StudentProfileId = students[1].Id, Score = 91, Feedback = "Strong MVC project." },
                new AssignmentResult { AssignmentId = assignments[3].Id, StudentProfileId = students[2].Id, Score = 42, Feedback = "Good analysis, needs deeper conclusions." },
                new AssignmentResult { AssignmentId = assignments[4].Id, StudentProfileId = students[3].Id, Score = 76, Feedback = "Good pandas usage." }
            );
            await ctx.SaveChangesAsync();

            // ── Exams ─────────────────────────────────────────────────────────
            var exams = new[]
            {
            new Exam { CourseId = courses[0].Id, Title = "Semester 1 Exam",        Date = today.AddMonths(-1),  MaxScore = 100, ResultsReleased = true  },
            new Exam { CourseId = courses[0].Id, Title = "Semester 2 Exam",        Date = today.AddDays(30),    MaxScore = 100, ResultsReleased = false },
            new Exam { CourseId = courses[1].Id, Title = "End of Year Exam",       Date = today.AddMonths(-1),  MaxScore = 100, ResultsReleased = true  },
            new Exam { CourseId = courses[2].Id, Title = "Business Analytics Final",Date = today.AddDays(14),   MaxScore = 100, ResultsReleased = false },
            new Exam { CourseId = courses[4].Id, Title = "Data Science Midterm",   Date = today.AddMonths(-2),  MaxScore = 100, ResultsReleased = true  },
        };
            ctx.Exams.AddRange(exams);
            await ctx.SaveChangesAsync();

            // ── Exam results ──────────────────────────────────────────────────
            ctx.ExamResults.AddRange(
                // Released
                new ExamResult { ExamId = exams[0].Id, StudentProfileId = students[0].Id, Score = 72, Grade = "B" },
                new ExamResult { ExamId = exams[0].Id, StudentProfileId = students[1].Id, Score = 85, Grade = "A" },
                new ExamResult { ExamId = exams[2].Id, StudentProfileId = students[1].Id, Score = 89, Grade = "A" },
                new ExamResult { ExamId = exams[4].Id, StudentProfileId = students[3].Id, Score = 68, Grade = "C" },
                // Provisional (not yet released)
                new ExamResult { ExamId = exams[1].Id, StudentProfileId = students[0].Id, Score = 55, Grade = "C" },
                new ExamResult { ExamId = exams[1].Id, StudentProfileId = students[1].Id, Score = 90, Grade = "A" }
            );
            await ctx.SaveChangesAsync();
        }

        private static async Task<IdentityUser> EnsureUserAsync(
            UserManager<IdentityUser> mgr, string email, string password, string role)
        {
            var existing = await mgr.FindByEmailAsync(email);
            if (existing is not null) return existing;
            var user = new IdentityUser { UserName = email, Email = email, EmailConfirmed = true };
            await mgr.CreateAsync(user, password);
            await mgr.AddToRoleAsync(user, role);
            return user;
        }
    }
}