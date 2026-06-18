using Microsoft.EntityFrameworkCore;
using Backend.Models;

namespace Backend.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> opts) : base(opts) { }

        public DbSet<User> Users => Set<User>();
        public DbSet<Submission> Submissions => Set<Submission>();
        public DbSet<FileRecord> Files => Set<FileRecord>();
        public DbSet<ReviewAssignment> ReviewAssignments => Set<ReviewAssignment>();
        public DbSet<Review> Reviews => Set<Review>();
        public DbSet<Notification> Notifications => Set<Notification>();
    }
}
