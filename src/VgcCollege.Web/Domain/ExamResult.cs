using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace VgcCollege.Web.Domain
{

    public class ExamResult
    {
        public int Id { get; set; }
        public int ExamId { get; set; }
        [ValidateNever] public Exam Exam { get; set; } = null!;
        public int StudentProfileId { get; set; }
        [ValidateNever] public StudentProfile StudentProfile { get; set; } = null!;
        [Range(0, 1000)] public int Score { get; set; }
        public string? Grade { get; set; }
    }
}