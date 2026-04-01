using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace VgcCollege.Web.Domain
{

    public class CourseEnrolment
    {
        public int Id { get; set; }
        public int StudentProfileId { get; set; }
        [ValidateNever] public StudentProfile StudentProfile { get; set; } = null!;
        public int CourseId { get; set; }
        [ValidateNever] public Course Course { get; set; } = null!;
        public DateTime EnrolDate { get; set; }
        public EnrolmentStatus Status { get; set; }
        public ICollection<AttendanceRecord> AttendanceRecords { get; set; } = new List<AttendanceRecord>();
    }

    public enum EnrolmentStatus { Active, Withdrawn, Completed }
}