using System.ComponentModel.DataAnnotations;

namespace Backend.Models
{
    public class Submission
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public string? Title { get; set; }
        public string? Abstract { get; set; }
        public string? Authors { get; set; }
        public string? Category { get; set; }
        // keywords stored as semicolon-separated string
        public string? Keywords { get; set; }
        public string Status { get; set; } = "draft";
        public Guid CorrespondingUserId { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
