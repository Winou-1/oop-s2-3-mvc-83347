using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace VgcCollege.Web.Domain
{

    public class AttendanceRecord
    {
        public int Id { get; set; }
        public int CourseEnrolmentId { get; set; }
        [ValidateNever] public CourseEnrolment CourseEnrolment { get; set; } = null!;
        public int WeekNumber { get; set; }
        public DateTime SessionDate { get; set; }
        public bool Present { get; set; }
    }
}