using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace VgcCollege.Web.Domain
{

    public class FacultyCourseAssignment
    {
        public int Id { get; set; }
        public int FacultyProfileId { get; set; }
        [ValidateNever] public FacultyProfile FacultyProfile { get; set; } = null!;
        public int CourseId { get; set; }
        [ValidateNever] public Course Course { get; set; } = null!;
        public bool IsTutor { get; set; }
    }
}