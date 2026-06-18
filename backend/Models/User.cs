using System.ComponentModel.DataAnnotations;

namespace Backend.Models
{
    public class User
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();
        [Required] public string Email { get; set; } = default!;
        [Required] public string PasswordHash { get; set; } = default!;
        public string? Name { get; set; }
        public string Role { get; set; } = "author";
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
