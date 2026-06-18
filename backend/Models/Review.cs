using System.ComponentModel.DataAnnotations;

namespace Backend.Models
{
    public class Review
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid SubmissionId { get; set; }
        public Submission Submission { get; set; } = null!;
        public Guid ReviewerId { get; set; }
        public User Reviewer { get; set; } = null!;
        public string? Content { get; set; }
        public int Rating { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
