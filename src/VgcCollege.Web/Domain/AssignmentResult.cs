using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace VgcCollege.Web.Domain
{

    public class AssignmentResult
    {
        public int Id { get; set; }
        public int AssignmentId { get; set; }
        [ValidateNever] public Assignment Assignment { get; set; } = null!;
        public int StudentProfileId { get; set; }
        [ValidateNever] public StudentProfile StudentProfile { get; set; } = null!;
        [Range(0, 1000)] public int Score { get; set; }
        public string? Feedback { get; set; }
    }
}