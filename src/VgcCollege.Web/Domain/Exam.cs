using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace VgcCollege.Web.Domain
{

    public class Exam
    {
        public int Id { get; set; }
        public int CourseId { get; set; }
        [ValidateNever] public Course Course { get; set; } = null!;
        [Required, MaxLength(150)] public string Title { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        [Range(1, 1000)] public int MaxScore { get; set; }
        public bool ResultsReleased { get; set; }
        public ICollection<ExamResult> Results { get; set; } = new List<ExamResult>();
    }
}