using System.ComponentModel.DataAnnotations;

namespace Backend.Models
{
    public class FileRecord
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid SubmissionId { get; set; }
        public string Filename { get; set; } = default!;
        public string StoragePath { get; set; } = default!;
        public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
    }
}
