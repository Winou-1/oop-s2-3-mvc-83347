using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace VgcCollege.Web.Domain
{

    public class Assignment
    {
        public int Id { get; set; }
        public int CourseId { get; set; }
        [ValidateNever] public Course Course { get; set; } = null!;
        [Required, MaxLength(150)] public string Title { get; set; } = string.Empty;
        [Range(1, 1000)] public int MaxScore { get; set; }
        public DateTime DueDate { get; set; }
        public ICollection<AssignmentResult> Results { get; set; } = new List<AssignmentResult>();
    }
}