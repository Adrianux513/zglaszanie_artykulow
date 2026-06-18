using System.ComponentModel.DataAnnotations;

namespace Backend.Models
{
    public class ReviewAssignment
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid SubmissionId { get; set; }
        public Submission Submission { get; set; } = null!;
        public Guid ReviewerId { get; set; }
        public User Reviewer { get; set; } = null!;
        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;
    }
}
