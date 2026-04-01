using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace VgcCollege.Web.Domain
{

    public class StudentProfile
    {
        public int Id { get; set; }
        [Required] public string IdentityUserId { get; set; } = string.Empty;
        [ValidateNever] public IdentityUser IdentityUser { get; set; } = null!;
        [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Phone] public string? Phone { get; set; }
        public string? Address { get; set; }
        public DateTime? DateOfBirth { get; set; }
        [Required] public string StudentNumber { get; set; } = string.Empty;
        public ICollection<CourseEnrolment> Enrolments { get; set; } = new List<CourseEnrolment>();
        public ICollection<AssignmentResult> AssignmentResults { get; set; } = new List<AssignmentResult>();
        public ICollection<ExamResult> ExamResults { get; set; } = new List<ExamResult>();
    }
}