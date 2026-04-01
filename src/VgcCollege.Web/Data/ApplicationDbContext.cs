using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using VgcCollege.Web.Domain;

namespace VgcCollege.Web.Data
{

    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : IdentityDbContext<IdentityUser>(options)
    {
        public DbSet<Branch> Branches => Set<Branch>();
        public DbSet<Course> Courses => Set<Course>();
        public DbSet<StudentProfile> StudentProfiles => Set<StudentProfile>();
        public DbSet<FacultyProfile> FacultyProfiles => Set<FacultyProfile>();
        public DbSet<FacultyCourseAssignment> FacultyCourseAssignments => Set<FacultyCourseAssignment>();
        public DbSet<CourseEnrolment> CourseEnrolments => Set<CourseEnrolment>();
        public DbSet<AttendanceRecord> AttendanceRecords => Set<AttendanceRecord>();
        public DbSet<Assignment> Assignments => Set<Assignment>();
        public DbSet<AssignmentResult> AssignmentResults => Set<AssignmentResult>();
        public DbSet<Exam> Exams => Set<Exam>();
        public DbSet<ExamResult> ExamResults => Set<ExamResult>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Course>()
                .HasOne(c => c.Branch).WithMany(b => b.Courses)
                .HasForeignKey(c => c.BranchId).OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<StudentProfile>()
                .HasOne(s => s.IdentityUser).WithMany()
                .HasForeignKey(s => s.IdentityUserId).OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<FacultyProfile>()
                .HasOne(f => f.IdentityUser).WithMany()
                .HasForeignKey(f => f.IdentityUserId).OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<FacultyCourseAssignment>()
                .HasOne(fca => fca.FacultyProfile).WithMany(fp => fp.CourseAssignments)
                .HasForeignKey(fca => fca.FacultyProfileId).OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<FacultyCourseAssignment>()
                .HasOne(fca => fca.Course).WithMany(c => c.FacultyAssignments)
                .HasForeignKey(fca => fca.CourseId).OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CourseEnrolment>()
                .HasOne(ce => ce.StudentProfile).WithMany(s => s.Enrolments)
                .HasForeignKey(ce => ce.StudentProfileId).OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CourseEnrolment>()
                .HasOne(ce => ce.Course).WithMany(c => c.Enrolments)
                .HasForeignKey(ce => ce.CourseId).OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<AttendanceRecord>()
                .HasOne(ar => ar.CourseEnrolment).WithMany(ce => ce.AttendanceRecords)
                .HasForeignKey(ar => ar.CourseEnrolmentId).OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Assignment>()
                .HasOne(a => a.Course).WithMany(c => c.Assignments)
                .HasForeignKey(a => a.CourseId).OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AssignmentResult>()
                .HasOne(ar => ar.Assignment).WithMany(a => a.Results)
                .HasForeignKey(ar => ar.AssignmentId).OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<AssignmentResult>()
                .HasOne(ar => ar.StudentProfile).WithMany(s => s.AssignmentResults)
                .HasForeignKey(ar => ar.StudentProfileId).OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Exam>()
                .HasOne(e => e.Course).WithMany(c => c.Exams)
                .HasForeignKey(e => e.CourseId).OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ExamResult>()
                .HasOne(er => er.Exam).WithMany(e => e.Results)
                .HasForeignKey(er => er.ExamId).OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ExamResult>()
                .HasOne(er => er.StudentProfile).WithMany(s => s.ExamResults)
                .HasForeignKey(er => er.StudentProfileId).OnDelete(DeleteBehavior.Cascade);
        }
    }
}