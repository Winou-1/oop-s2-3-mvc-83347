using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace VgcCollege.Web.Domain
{

    public class FacultyProfile
    {
        public int Id { get; set; }
        [Required] public string IdentityUserId { get; set; } = string.Empty;
        [ValidateNever] public IdentityUser IdentityUser { get; set; } = null!;
        [Required, MaxLength(100)] public string Name { get; set; } = string.Empty;
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Phone] public string? Phone { get; set; }
        public ICollection<FacultyCourseAssignment> CourseAssignments { get; set; } = new List<FacultyCourseAssignment>();
    }
}