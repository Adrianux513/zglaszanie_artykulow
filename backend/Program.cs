using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Backend.Data;
using Backend.Models;
using BCrypt.Net;
using Prometheus;
using Microsoft.Extensions.Caching.Memory;

// Build
var builder = WebApplication.CreateBuilder(args);

// Config
var cfg = builder.Configuration;
var conn = cfg.GetConnectionString("Default") ?? "Data Source=app.db";
var jwtKey = cfg["Jwt:Key"] ?? "ChangeThisSecretForDev_UseLongSecret";

// Services
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(conn));

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = false,
        ValidateAudience = false,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ValidateLifetime = true,
        // ważne: rozpoznawaj sub jako nazwa i "role" jako rola
        NameClaimType = System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub,
        RoleClaimType = "role"
    };

    options.Events = new Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents
    {
        OnAuthenticationFailed = context =>
        {
            Console.WriteLine("JWT Auth failed: " + (context.Exception?.Message ?? "(no exception message)"));
            return System.Threading.Tasks.Task.CompletedTask;
        },
        OnTokenValidated = context =>
        {
            var sub = context.Principal?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
                      ?? context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                      ?? context.Principal?.FindFirst("sub")?.Value;
            var role = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value
                       ?? context.Principal?.FindFirst("role")?.Value;
            Console.WriteLine("JWT validated. sub=" + sub + " role=" + role);
            return System.Threading.Tasks.Task.CompletedTask;
        },
        OnMessageReceived = context =>
        {
            var auth = context.Request.Headers["Authorization"].FirstOrDefault();
            Console.WriteLine("Auth header received: " + (auth ?? "(none)"));
            return System.Threading.Tasks.Task.CompletedTask;
        }
    };
});


builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy => policy.RequireAssertion(ctx =>
        // akceptujemy różne sposoby, w jaki rola może być dostarczona
        ctx.User.IsInRole("admin")
        || ctx.User.HasClaim(c => c.Type == "role" && c.Value == "admin")
        || ctx.User.HasClaim(c => c.Type == System.Security.Claims.ClaimTypes.Role && c.Value == "admin")
        || ctx.User.HasClaim(c => c.Type == System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub && c.Value == "admin") // defensywny zapas
    ));
});

builder.Services.AddMemoryCache();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// CORS
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:3000")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

// Create DB & seed safely (EnsureCreated for demo)
try
{
    using (var scope = app.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Console.WriteLine("EnsureCreated: creating DB schema if missing...");
        db.Database.EnsureCreated();
        Console.WriteLine("EnsureCreated completed.");

        if (!db.Users.Any())
        {
            var admin = new User
            {
                Email = "admin@example.com",
                Name = "Admin",
                Role = "admin",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("adminpass")
            };
            var student = new User
            {
                Email = "student@example.com",
                Name = "Student Test",
                Role = "author",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("pass123")
            };
            var reviewer1 = new User
            {
                Email = "reviewer1@example.com",
                Name = "Reviewer One",
                Role = "reviewer",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("reviewerpass1")
            };
            var reviewer2 = new User
            {
                Email = "reviewer2@example.com",
                Name = "Reviewer Two",
                Role = "reviewer",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("reviewerpass2")
            };
            var reviewer3 = new User
            {
                Email = "reviewer3@example.com",
                Name = "Reviewer Three",
                Role = "reviewer",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("reviewerpass3")
            };
            db.Users.Add(admin);
            db.Users.Add(student);
            db.Users.Add(reviewer1);
            db.Users.Add(reviewer2);
            db.Users.Add(reviewer3);
            db.SaveChanges();
            Console.WriteLine("Seeded admin, student, and 3 reviewer users.");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine("Błąd przy tworzeniu/seedowaniu DB:");
    Console.WriteLine(ex.ToString());
}

// Middleware
app.UseSwagger();
app.UseSwaggerUI();
app.UseHttpMetrics();
app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// Health
app.MapGet("/api/health", () => Results.Ok(new { ok = true }));
app.MapMetrics();

// Register
app.MapPost("/api/auth/register", async (AppDbContext db, RegisterDto dto) =>
{
    if (string.IsNullOrWhiteSpace(dto.Email) || string.IsNullOrWhiteSpace(dto.Password))
        return Results.BadRequest(new { ok = false, error = "email & password required" });

    if (await db.Users.AnyAsync(u => u.Email == dto.Email))
        return Results.BadRequest(new { ok = false, error = "user exists" });

    var user = new User { Email = dto.Email, Name = dto.Name, PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password), Role = "author" };
    db.Users.Add(user);
    await db.SaveChangesAsync();
    return Results.Ok(new { ok = true, user = new { user.Id, user.Email, user.Name } });
});

// Login
app.MapPost("/api/auth/login", async (AppDbContext db, LoginDto dto) =>
{
    var user = await db.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);
    if (user == null) return Results.Unauthorized();

    if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash)) return Results.Unauthorized();

    var claims = new[] {
    new System.Security.Claims.Claim(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub, user.Id.ToString()),
    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, user.Id.ToString()),
    new System.Security.Claims.Claim("role", user.Role),
    new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, user.Role)
};

    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
    var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
        claims: claims,
        expires: DateTime.UtcNow.AddHours(8),
        signingCredentials: creds
    );
    var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    return Results.Ok(new { ok = true, token = jwt, user = new { user.Id, user.Email, user.Name, user.Role } });
});

// helper to read sub/role robustly
string? ReadSub(HttpContext http) =>
    http.User?.FindFirst(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub)?.Value
    ?? http.User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
    ?? http.User?.FindFirst("sub")?.Value;

string? ReadRole(HttpContext http) =>
    http.User?.FindFirst(System.Security.Claims.ClaimTypes.Role)?.Value
    ?? http.User?.FindFirst("role")?.Value;

// Create submission draft (auth required)
app.MapPost("/api/submissions", [Microsoft.AspNetCore.Authorization.Authorize] async (AppDbContext db, IMemoryCache cache, SubmissionCreateDto dto, HttpContext http) =>
{
    var sub = ReadSub(http);
    if (sub == null) return Results.Unauthorized();
    var userId = Guid.Parse(sub);

    var keywordsJoined = dto.Keywords != null ? string.Join(';', dto.Keywords) : null;
    var s = new Submission
    {
        Title = dto.Title,
        Abstract = dto.Abstract,
        Authors = dto.Authors,
        Category = dto.Category,
        Keywords = keywordsJoined,
        CorrespondingUserId = userId,
        Status = "draft"
    };
    db.Submissions.Add(s);
    await db.SaveChangesAsync();
    // Invalidate admin cache so admin view sees new submissions immediately
    cache.Remove("admin_submissions");
    return Results.Ok(new { ok = true, submission = s });
});

// Get a submission by id (owner only)
app.MapGet("/api/submissions/{id}", [Microsoft.AspNetCore.Authorization.Authorize] async (AppDbContext db, IMemoryCache cache, Guid id, HttpContext http) =>
{
    var sub = ReadSub(http);
    if (sub == null) return Results.Unauthorized();
    var userId = Guid.Parse(sub);
    
    string cacheKey = $"submission_{id}_{userId}";
    if (cache.TryGetValue(cacheKey, out var cachedResult))
    {
        return Results.Ok(cachedResult);
    }

    var submission = await db.Submissions.FirstOrDefaultAsync(x => x.Id == id && x.CorrespondingUserId == userId);
    if (submission == null) return Results.NotFound(new { ok = false, error = "Submission not found" });

    var files = await db.Files.Where(f => f.SubmissionId == id).Select(f => f.Filename).ToListAsync();
    var assignments = await db.ReviewAssignments.Include(a => a.Reviewer)
        .Where(a => a.SubmissionId == id)
        .ToListAsync();

    var result = new {
        ok = true,
        submission,
        files,
        assignments = assignments.Select(a => new {
            a.ReviewerId,
            ReviewerEmail = a.Reviewer.Email,
            ReviewerName = a.Reviewer.Name,
            a.AssignedAt
        }).ToList()
    };
    
    cache.Set(cacheKey, result, TimeSpan.FromMinutes(5));
    return Results.Ok(result);
});

// Update draft submission before review
app.MapPut("/api/submissions/{id}", [Microsoft.AspNetCore.Authorization.Authorize] async (AppDbContext db, IMemoryCache cache, Guid id, SubmissionUpdateDto dto, HttpContext http) =>
{
    var sub = ReadSub(http);
    if (sub == null) return Results.Unauthorized();
    var userId = Guid.Parse(sub);

    var submission = await db.Submissions.FirstOrDefaultAsync(x => x.Id == id && x.CorrespondingUserId == userId);
    if (submission == null) return Results.NotFound(new { ok = false, error = "Submission not found" });
    if (submission.Status != "draft" && submission.Status != "rejected") return Results.BadRequest(new { ok = false, error = "Only draft or rejected submissions can be edited before review" });

    submission.Title = dto.Title;
    submission.Abstract = dto.Abstract;
    submission.Authors = dto.Authors;
    submission.Category = dto.Category;
    submission.Keywords = dto.Keywords != null ? string.Join(';', dto.Keywords) : null;
    await db.SaveChangesAsync();
    
    // Invalidate cache
    cache.Remove($"mysubmissions_{userId}");
    cache.Remove($"submission_{id}_{userId}");
    // Also ensure admin list updates
    cache.Remove("admin_submissions");
    
    return Results.Ok(new { ok = true, submission });
});

// Submit draft for review
app.MapPost("/api/submissions/{id}/submit", [Microsoft.AspNetCore.Authorization.Authorize] async (AppDbContext db, IMemoryCache cache, Guid id, HttpContext http) =>
{
    var sub = ReadSub(http);
    if (sub == null) return Results.Unauthorized();
    var userId = Guid.Parse(sub);

    var submission = await db.Submissions.FirstOrDefaultAsync(x => x.Id == id && x.CorrespondingUserId == userId);
    if (submission == null) return Results.NotFound(new { ok = false, error = "Submission not found" });
    if (submission.Status != "draft" && submission.Status != "rejected") return Results.BadRequest(new { ok = false, error = "Only draft or rejected submissions can be submitted" });

    submission.Status = "submitted";
    await db.SaveChangesAsync();
    
    // Invalidate cache
    cache.Remove($"mysubmissions_{userId}");
    cache.Remove($"submission_{id}_{userId}");
    cache.Remove("allsubmissions");
    cache.Remove("publicarticles");
    // Ensure admin sees newly submitted items
    cache.Remove("admin_submissions");
    
    return Results.Ok(new { ok = true, submission });
});

// List my submissions
app.MapGet("/api/submissions", [Microsoft.AspNetCore.Authorization.Authorize] async (AppDbContext db, IMemoryCache cache, HttpContext http) =>
{
    var sub = ReadSub(http);
    if (sub == null) return Results.Unauthorized();
    var userId = Guid.Parse(sub);
    
    string cacheKey = $"mysubmissions_{userId}";
    if (cache.TryGetValue(cacheKey, out var cachedResult))
    {
        return Results.Ok(cachedResult);
    }
    
    var submissions = await db.Submissions.Where(x => x.CorrespondingUserId == userId).OrderByDescending(x => x.CreatedAt).ToListAsync();
    var submissionIds = submissions.Select(s => s.Id).ToList();
    var reviews = await db.Reviews
        .Where(r => submissionIds.Contains(r.SubmissionId))
        .Select(r => new { r.Id, r.SubmissionId, r.ReviewerId, r.Content, r.Rating, r.CreatedAt })
        .ToListAsync();
    var assignments = await db.ReviewAssignments.Include(a => a.Reviewer)
        .Where(a => submissionIds.Contains(a.SubmissionId))
        .ToListAsync();
    var files = await db.Files
        .Where(f => submissionIds.Contains(f.SubmissionId))
        .Select(f => new { f.SubmissionId, f.Filename })
        .ToListAsync();

    var result = new { ok = true, submissions = submissions.Select(s => new {
        s.Id,
        s.Title,
        s.Abstract,
        s.Authors,
        s.Category,
        s.Keywords,
        s.Status,
        s.CorrespondingUserId,
        s.CreatedAt,
        Files = files.Where(f => f.SubmissionId == s.Id).Select(f => f.Filename).ToList(),
        Assignments = assignments.Where(a => a.SubmissionId == s.Id).Select(a => new {
            a.ReviewerId,
            ReviewerEmail = a.Reviewer.Email,
            ReviewerName = a.Reviewer.Name,
            a.AssignedAt
        }).ToList(),
        Reviews = reviews.Where(r => r.SubmissionId == s.Id).ToList()
    }).ToList() };
    
    cache.Set(cacheKey, result, TimeSpan.FromMinutes(5));
    return Results.Ok(result);
});

// Admin: list all submissions
app.MapGet("/api/admin/submissions",
async (AppDbContext db, IMemoryCache cache) =>
{
    const string cacheKey = "admin_submissions";

    if (!cache.TryGetValue(cacheKey, out List<Submission>? list))
    {
        Console.WriteLine("DB HIT");

        list = await db.Submissions
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();

        cache.Set(cacheKey, list, TimeSpan.FromMinutes(5));
    }
    else
    {
        Console.WriteLine("CACHE HIT");
    }

    var assignments = await db.ReviewAssignments
        .Include(a => a.Reviewer)
        .ToListAsync();

    var reviews = await db.Reviews
        .Include(r => r.Reviewer)
        .ToListAsync();

    return Results.Ok(new {
        ok = true,
        submissions = list,
        assignments = assignments.Select(a => new {
            a.Id,
            a.SubmissionId,
            a.ReviewerId,
            ReviewerEmail = a.Reviewer.Email,
            ReviewerName = a.Reviewer.Name,
            a.AssignedAt
        }),
        reviews = reviews.Select(r => new {
            r.Id,
            r.SubmissionId,
            r.ReviewerId,
            ReviewerEmail = r.Reviewer.Email,
            ReviewerName = r.Reviewer.Name,
            r.Content,
            r.Rating,
            r.CreatedAt
        })
    });
});

// Admin: delete a review
app.MapDelete("/api/admin/reviews/{reviewId}", [Microsoft.AspNetCore.Authorization.Authorize(Policy = "AdminOnly")] async (AppDbContext db, IMemoryCache cache, Guid reviewId) =>
{
    var review = await db.Reviews.FirstOrDefaultAsync(r => r.Id == reviewId);
    if (review == null) return Results.NotFound(new { ok = false, error = "Review not found" });

    var submission = await db.Submissions.FirstOrDefaultAsync(s => s.Id == review.SubmissionId);
    var submissionUserId = submission?.CorrespondingUserId;

    db.Reviews.Remove(review);
    await db.SaveChangesAsync();

    cache.Remove("admin_submissions");
    cache.Remove("allsubmissions");
    cache.Remove("publicarticles");
    if (submissionUserId != null)
    {
        cache.Remove($"mysubmissions_{submissionUserId}");
        cache.Remove($"submission_{review.SubmissionId}_{submissionUserId}");
    }

    return Results.Ok(new { ok = true });
});

// Admin: list reviewer accounts
app.MapGet("/api/admin/reviewers", [Microsoft.AspNetCore.Authorization.Authorize(Policy = "AdminOnly")] async (AppDbContext db) =>
{
    var reviewers = await db.Users.Where(u => u.Role == "reviewer")
        .Select(u => new { u.Id, u.Email, u.Name })
        .ToListAsync();
    return Results.Ok(new { ok = true, reviewers });
});

// Admin: assign reviewer to submission
app.MapPost("/api/submissions/{id}/assign-reviewer", [Microsoft.AspNetCore.Authorization.Authorize(Policy = "AdminOnly")] async (AppDbContext db, IMemoryCache cache, Guid id, AssignReviewerDto dto) =>
{
    var submission = await db.Submissions.FirstOrDefaultAsync(x => x.Id == id);
    if (submission == null) return Results.NotFound(new { ok = false, error = "Submission not found" });

    var reviewer = await db.Users.FirstOrDefaultAsync(u => u.Id == dto.ReviewerId && u.Role == "reviewer");
    if (reviewer == null) return Results.BadRequest(new { ok = false, error = "Reviewer not found" });

    if (submission.Status == "draft")
        return Results.BadRequest(new { ok = false, error = "Cannot assign reviewer to a draft submission" });

    if (await db.ReviewAssignments.AnyAsync(a => a.SubmissionId == id && a.ReviewerId == dto.ReviewerId))
        return Results.BadRequest(new { ok = false, error = "Reviewer already assigned" });

    var assignment = new ReviewAssignment { SubmissionId = id, ReviewerId = dto.ReviewerId };
    db.ReviewAssignments.Add(assignment);
    if (submission.Status == "submitted") submission.Status = "in review";

    db.Notifications.Add(new Notification
    {
        UserId = dto.ReviewerId,
        Message = $"You have been assigned to review article '{submission.Title}'."
    });

    await db.SaveChangesAsync();
    return Results.Ok(new { ok = true, assignment });
});

// Admin: delete submission and related data
app.MapDelete("/api/admin/submissions/{id}", [Microsoft.AspNetCore.Authorization.Authorize(Policy = "AdminOnly")] async (AppDbContext db, IMemoryCache cache, Guid id) =>
{
    var submission = await db.Submissions.FirstOrDefaultAsync(x => x.Id == id);
    if (submission == null) return Results.NotFound(new { ok = false, error = "Submission not found" });

    // delete related files records and physical files
    var files = await db.Files.Where(f => f.SubmissionId == id).ToListAsync();
    foreach (var f in files)
    {
        try { if (System.IO.File.Exists(f.StoragePath)) System.IO.File.Delete(f.StoragePath); } catch { }
    }
    db.Files.RemoveRange(files);

    // delete assignments and reviews
    var assignments = await db.ReviewAssignments.Where(a => a.SubmissionId == id).ToListAsync();
    db.ReviewAssignments.RemoveRange(assignments);
    var reviews = await db.Reviews.Where(r => r.SubmissionId == id).ToListAsync();
    db.Reviews.RemoveRange(reviews);

    // attempt to remove notifications that mention the submission title (best-effort)
    if (!string.IsNullOrWhiteSpace(submission.Title))
    {
        var relatedNotifications = await db.Notifications.Where(n => n.Message.Contains(submission.Title)).ToListAsync();
        db.Notifications.RemoveRange(relatedNotifications);
    }

    // remove storage directory
    try
    {
        var storageRoot = Path.Combine(AppContext.BaseDirectory, "storage", id.ToString());
        if (Directory.Exists(storageRoot)) Directory.Delete(storageRoot, true);
    }
    catch { }

    db.Submissions.Remove(submission);
    await db.SaveChangesAsync();

    // invalidate caches
    cache.Remove("admin_submissions");
    cache.Remove("allsubmissions");
    cache.Remove("publicarticles");
    cache.Remove($"mysubmissions_{submission.CorrespondingUserId}");

    return Results.Ok(new { ok = true });
});

// Reviewer: list assigned reviews
app.MapGet("/api/reviews/assigned", [Microsoft.AspNetCore.Authorization.Authorize] async (AppDbContext db, HttpContext http) =>
{
    var sub = ReadSub(http);
    if (sub == null) return Results.Unauthorized();
    var userId = Guid.Parse(sub);
    var assignments = await db.ReviewAssignments
        .Include(a => a.Submission)
        .Where(a => a.ReviewerId == userId)
        .OrderByDescending(a => a.AssignedAt)
        .ToListAsync();

    // Get all reviews by this reviewer for their assigned submissions
    var submissionIds = assignments.Select(a => a.SubmissionId).ToList();
    var myReviews = await db.Reviews
        .Where(r => submissionIds.Contains(r.SubmissionId) && r.ReviewerId == userId)
        .ToListAsync();

    return Results.Ok(new { ok = true, assignments = assignments.Select(a => new {
        a.Id,
        a.SubmissionId,
        a.Submission.Title,
        a.Submission.Abstract,
        a.Submission.Status,
        a.AssignedAt,
        MyReview = myReviews.FirstOrDefault(r => r.SubmissionId == a.SubmissionId)
    }) });
});

// Reviewer: add review for assigned submission
app.MapPost("/api/submissions/{id}/reviews", [Microsoft.AspNetCore.Authorization.Authorize] async (AppDbContext db, IMemoryCache cache, Guid id, ReviewCreateDto dto, HttpContext http) =>
{
    var sub = ReadSub(http);
    if (sub == null) return Results.Unauthorized();
    var userId = Guid.Parse(sub);

    var assignment = await db.ReviewAssignments.FirstOrDefaultAsync(a => a.SubmissionId == id && a.ReviewerId == userId);
    if (assignment == null) return Results.Forbid();

    var submission = await db.Submissions.FirstOrDefaultAsync(x => x.Id == id);
    if (submission == null) return Results.NotFound(new { ok = false, error = "Submission not found" });

    var existingReview = await db.Reviews.FirstOrDefaultAsync(r => r.SubmissionId == id && r.ReviewerId == userId);
    Review review;
    if (existingReview != null)
    {
        existingReview.Content = dto.Content;
        existingReview.Rating = dto.Rating;
        existingReview.CreatedAt = DateTime.UtcNow;
        review = existingReview;
    }
    else
    {
        review = new Review
        {
            SubmissionId = id,
            ReviewerId = userId,
            Content = dto.Content,
            Rating = dto.Rating
        };
        db.Reviews.Add(review);
    }
    if (submission.Status == "submitted") submission.Status = "in review";

    var commentText = string.IsNullOrWhiteSpace(dto.Content) ? "brak komentarza" : dto.Content;
    db.Notifications.Add(new Notification
    {
        UserId = submission.CorrespondingUserId,
        Message = $"Recenzent ocenił Twój artykuł '{submission.Title}' na {dto.Rating}/5. Komentarz: {commentText}."
    });

    await db.SaveChangesAsync();
    return Results.Ok(new { ok = true, review });
});

// Get reviews for a submission
app.MapGet("/api/submissions/{id}/reviews", [Microsoft.AspNetCore.Authorization.Authorize] async (AppDbContext db, IMemoryCache cache, Guid id, HttpContext http) =>
{
    var sub = ReadSub(http);
    if (sub == null) return Results.Unauthorized();
    var userId = Guid.Parse(sub);

    var submission = await db.Submissions.FirstOrDefaultAsync(x => x.Id == id);
    if (submission == null) return Results.NotFound(new { ok = false, error = "Submission not found" });

    var isAuthor = submission.CorrespondingUserId == userId;
    var isAdmin = http.User.IsInRole("admin");
    var isReviewer = await db.ReviewAssignments.AnyAsync(a => a.SubmissionId == id && a.ReviewerId == userId);
    if (!isAuthor && !isAdmin && !isReviewer) return Results.Forbid();

    var reviews = await db.Reviews
        .Where(r => r.SubmissionId == id)
        .Select(r => new { r.Id, r.ReviewerId, r.Content, r.Rating, r.CreatedAt })
        .ToListAsync();

    return Results.Ok(new { ok = true, reviews });
});

// Admin: submit decision for article
app.MapPost("/api/submissions/{id}/decision", [Microsoft.AspNetCore.Authorization.Authorize] async (AppDbContext db, IMemoryCache cache, Guid id, SubmissionDecisionDto dto, HttpContext http) =>
{
    var sub = ReadSub(http);
    if (sub == null) return Results.Unauthorized();
    var userId = Guid.Parse(sub);
    var role = ReadRole(http);

    var submission = await db.Submissions.FirstOrDefaultAsync(x => x.Id == id);
    if (submission == null) return Results.NotFound(new { ok = false, error = "Submission not found" });

    var isAdmin = string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase);
    var isAssignedReviewer = string.Equals(role, "reviewer", StringComparison.OrdinalIgnoreCase)
        && await db.ReviewAssignments.AnyAsync(a => a.SubmissionId == id && a.ReviewerId == userId);
    if (!isAdmin && !isAssignedReviewer)
        return Results.Forbid();

    var allowed = new[] { "accepted", "rejected", "published" };
    var normalized = dto.Status?.ToLowerInvariant();
    if (!allowed.Contains(normalized)) return Results.BadRequest(new { ok = false, error = "Status must be accepted, rejected, or published" });

    submission.Status = normalized!;
    db.Notifications.Add(new Notification
    {
        UserId = submission.CorrespondingUserId,
        Message = $"Your article '{submission.Title}' has been {normalized}."
    });

    await db.SaveChangesAsync();

    cache.Remove("admin_submissions");
    cache.Remove("allsubmissions");
    cache.Remove("publicarticles");
    cache.Remove($"mysubmissions_{submission.CorrespondingUserId}");
    cache.Remove($"submission_{id}_{submission.CorrespondingUserId}");

    return Results.Ok(new { ok = true, submission });
});

app.MapGet("/api/articles", async (AppDbContext db, IMemoryCache cache, HttpRequest req) =>
{
    var title = req.Query["title"].ToString();
    var authors = req.Query["authors"].ToString();
    var keywords = req.Query["keywords"].ToString();
    var status = req.Query["status"].ToString()?.ToLowerInvariant();
    var category = req.Query["category"].ToString();
    
    // Cache key based on query params
    string cacheKey = $"articles_{status ?? "published"}_{title}_{authors}_{keywords}_{category}";
    if (cache.TryGetValue(cacheKey, out var cachedArticles))
    {
        return Results.Ok(cachedArticles);
    }

    var query = db.Submissions.AsQueryable();
    if (string.IsNullOrWhiteSpace(status))
        query = query.Where(s => s.Status == "published");
    else
        query = query.Where(s => s.Status == status);

    if (!string.IsNullOrWhiteSpace(title))
        query = query.Where(s => s.Title != null && s.Title.ToLower().Contains(title.ToLower()));
    if (!string.IsNullOrWhiteSpace(authors))
        query = query.Where(s => s.Authors != null && s.Authors.ToLower().Contains(authors.ToLower()));
    if (!string.IsNullOrWhiteSpace(keywords))
        query = query.Where(s => s.Keywords != null && s.Keywords.ToLower().Contains(keywords.ToLower()));
    if (!string.IsNullOrWhiteSpace(category))
        query = query.Where(s => s.Category != null && s.Category.ToLower().Contains(category.ToLower()));

    var articles = await query
        .OrderByDescending(s => s.CreatedAt)
        .Select(s => new
        {
            s.Id,
            s.Title,
            s.Abstract,
            s.Authors,
            s.Category,
            s.Keywords,
            s.Status,
            s.CreatedAt,
            Files = db.Files.Where(f => f.SubmissionId == s.Id).Select(f => f.Filename).ToList()
        })
        .ToListAsync();

    var result = new { ok = true, articles };
    cache.Set(cacheKey, result, TimeSpan.FromMinutes(10));
    
    return Results.Ok(result);
});

app.MapGet("/api/articles/{id}/files/{filename}", async (AppDbContext db, Guid id, string filename) =>
{
    var submission = await db.Submissions.FirstOrDefaultAsync(x => x.Id == id && x.Status == "published");
    if (submission == null) return Results.NotFound(new { ok = false, error = "Published article not found" });

    var safeFileName = Path.GetFileName(filename);
    var storageRoot = Path.Combine(AppContext.BaseDirectory, "storage", id.ToString());
    var filePath = Path.Combine(storageRoot, safeFileName);
    if (!System.IO.File.Exists(filePath)) return Results.NotFound(new { ok = false, error = "File not found" });

    var contentType = Path.GetExtension(filePath).ToLowerInvariant() switch
    {
        ".pdf" => "application/pdf",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".tex" => "text/x-tex",
        _ => "application/octet-stream"
    };

    return Results.File(filePath, contentType, safeFileName);
});

// Notifications
app.MapGet("/api/notifications", [Microsoft.AspNetCore.Authorization.Authorize] async (AppDbContext db, HttpContext http) =>
{
    var sub = ReadSub(http);
    if (sub == null) return Results.Unauthorized();
    var userId = Guid.Parse(sub);
    var notifications = await db.Notifications
        .Where(n => n.UserId == userId)
        .OrderByDescending(n => n.CreatedAt)
        .Select(n => new { n.Id, n.Message, n.IsRead, n.CreatedAt })
        .ToListAsync();
    return Results.Ok(new { ok = true, notifications });
});

app.MapPut("/api/notifications/{id}/read", [Microsoft.AspNetCore.Authorization.Authorize] async (AppDbContext db, Guid id, HttpContext http) =>
{
    var sub = ReadSub(http);
    if (sub == null) return Results.Unauthorized();
    var userId = Guid.Parse(sub);
    var notification = await db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == userId);
    if (notification == null) return Results.NotFound(new { ok = false, error = "Notification not found" });
    notification.IsRead = true;
    await db.SaveChangesAsync();
    return Results.Ok(new { ok = true });
});

// File upload
app.MapPost("/api/submissions/{id}/files", [Microsoft.AspNetCore.Authorization.Authorize] async (AppDbContext db, IMemoryCache cache, Guid id, HttpRequest req, HttpContext http) =>
{
    var sub = ReadSub(http);
    if (sub == null) return Results.Unauthorized();
    var userId = Guid.Parse(sub);

    var submission = await db.Submissions.FirstOrDefaultAsync(x => x.Id == id && x.CorrespondingUserId == userId);
    if (submission == null) return Results.NotFound(new { ok = false, error = "Submission not found" });

    if (!req.HasFormContentType) return Results.BadRequest(new { ok = false, error = "form required" });
    var form = await req.ReadFormAsync();
    var files = form.Files;
    var allowed = new[] { ".pdf", ".docx", ".tex" };
    var storageRoot = Path.Combine(AppContext.BaseDirectory, "storage", id.ToString());
    Directory.CreateDirectory(storageRoot);
    var saved = new List<object>();
    foreach (var f in files)
    {
        var safe = Path.GetFileName(f.FileName);
        var ext = Path.GetExtension(safe).ToLowerInvariant();
        if (!allowed.Contains(ext)) return Results.BadRequest(new { ok = false, error = "Allowed file formats: PDF, DOCX, TEX" });
        var dest = Path.Combine(storageRoot, safe);
        using var stream = System.IO.File.Create(dest);
        await f.CopyToAsync(stream);
        var fr = new FileRecord { SubmissionId = id, Filename = safe, StoragePath = dest };
        db.Files.Add(fr);
        saved.Add(new { filename = safe });
    }
    await db.SaveChangesAsync();
    
    // Invalidate cache
    cache.Remove($"mysubmissions_{userId}");
    cache.Remove($"submission_{id}_{userId}");
    cache.Remove("allsubmissions");
    cache.Remove("publicarticles");
    // Ensure admin list refreshes when files are added
    cache.Remove("admin_submissions");
    
    return Results.Ok(new { ok = true, files = saved });
});
// Temporary debug endpoint to inspect DB contents (no auth)
app.MapGet("/debug/db", (AppDbContext db) =>
{
    var users = db.Users.Select(u => new { u.Id, u.Email, u.Role }).ToList();
    var submissions = db.Submissions.Select(s => new { s.Id, s.Title, s.Status, s.CorrespondingUserId }).ToList();
    var assignments = db.ReviewAssignments.Select(a => new { a.Id, a.SubmissionId, a.ReviewerId, a.AssignedAt }).ToList();
    var reviews = db.Reviews.Select(r => new { r.Id, r.SubmissionId, r.ReviewerId, r.Rating }).ToList();
    var notifications = db.Notifications.Select(n => new { n.Id, n.UserId, n.Message, n.IsRead }).ToList();
    return Results.Ok(new { ok = true, users, submissions, assignments, reviews, notifications });
});

app.MapGet("/api/admin/debug", [Microsoft.AspNetCore.Authorization.Authorize(Policy = "AdminOnly")] (HttpContext http) =>
{
    var claims = http.User.Claims.Select(c => new { c.Type, c.Value }).ToList();
    return Results.Ok(new { ok = true, claims });
});

// Generate PDF of published article
app.MapGet("/api/articles/{id}/pdf", async (AppDbContext db, Guid id) =>
{
    var submission = await db.Submissions.FirstOrDefaultAsync(x => x.Id == id && x.Status == "published");
    if (submission == null) return Results.NotFound(new { ok = false, error = "Published article not found" });

    try
    {
        using (var ms = new MemoryStream())
        {
            var writer = new iText.Kernel.Pdf.PdfWriter(ms);
            var pdf = new iText.Kernel.Pdf.PdfDocument(writer);
            var document = new iText.Layout.Document(pdf);
            document.Add(new iText.Layout.Element.Paragraph(submission.Title).SetFontSize(24).SetBold());
            document.Add(new iText.Layout.Element.Paragraph(submission.Authors ?? "").SetFontSize(12));
            document.Add(new iText.Layout.Element.Paragraph($"Category: {submission.Category}").SetFontSize(10));
            document.Add(new iText.Layout.Element.Paragraph("").SetHeight(10));
            document.Add(new iText.Layout.Element.Paragraph(submission.Abstract ?? "").SetFontSize(11));
            document.Close();
            return Results.File(ms.ToArray(), "application/pdf", $"{submission.Title}.pdf");
        }
    }
    catch (Exception ex)
    {
        return Results.BadRequest(new { ok = false, error = ex.Message });
    }
});

// Export articles list to PDF or CSV
app.MapGet("/api/articles/export/{format}", async (AppDbContext db, string format, HttpRequest req) =>
{
    var title = req.Query["title"].ToString();
    var authors = req.Query["authors"].ToString();
    var keywords = req.Query["keywords"].ToString();
    var status = req.Query["status"].ToString()?.ToLowerInvariant() ?? "published";
    var category = req.Query["category"].ToString();

    var query = db.Submissions.AsQueryable();
    query = query.Where(s => s.Status == status);
    if (!string.IsNullOrWhiteSpace(title))
        query = query.Where(s => s.Title != null && s.Title.ToLower().Contains(title.ToLower()));
    if (!string.IsNullOrWhiteSpace(authors))
        query = query.Where(s => s.Authors != null && s.Authors.ToLower().Contains(authors.ToLower()));
    if (!string.IsNullOrWhiteSpace(keywords))
        query = query.Where(s => s.Keywords != null && s.Keywords.ToLower().Contains(keywords.ToLower()));
    if (!string.IsNullOrWhiteSpace(category))
        query = query.Where(s => s.Category != null && s.Category.ToLower().Contains(category.ToLower()));

    var articles = await query.OrderByDescending(x => x.CreatedAt).ToListAsync();

    if (format.Equals("csv", StringComparison.OrdinalIgnoreCase))
    {
        using (var ms = new MemoryStream())
        using (var writer = new StreamWriter(ms))
        using (var csv = new CsvHelper.CsvWriter(writer, System.Globalization.CultureInfo.InvariantCulture))
        {
            csv.WriteHeader<ArticleExportDto>();
            await csv.NextRecordAsync();
            foreach (var a in articles)
            {
                var kw = a.Keywords != null ? string.Join(", ", a.Keywords) : "";
                csv.WriteRecord(new ArticleExportDto(a.Title, a.Authors, a.Category, kw, a.Status, a.CreatedAt));
                await csv.NextRecordAsync();
            }
            writer.Flush();
            return Results.File(ms.ToArray(), "text/csv", $"articles_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
        }
    }
    else if (format.Equals("pdf", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            using (var ms = new MemoryStream())
            {
                var pdfWriter = new iText.Kernel.Pdf.PdfWriter(ms);
                var pdf = new iText.Kernel.Pdf.PdfDocument(pdfWriter);
                var document = new iText.Layout.Document(pdf);
                document.Add(new iText.Layout.Element.Paragraph("Articles List").SetFontSize(18).SetBold());
                document.Add(new iText.Layout.Element.Paragraph($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}").SetFontSize(10));
                document.Add(new iText.Layout.Element.Paragraph("").SetHeight(10));

                foreach (var a in articles)
                {
                    document.Add(new iText.Layout.Element.Paragraph(a.Title).SetBold().SetFontSize(12));
                    document.Add(new iText.Layout.Element.Paragraph($"Authors: {a.Authors}").SetFontSize(10));
                    document.Add(new iText.Layout.Element.Paragraph($"Category: {a.Category} | Status: {a.Status}").SetFontSize(9));
                    document.Add(new iText.Layout.Element.Paragraph("").SetHeight(5));
                }

                document.Close();
                return Results.File(ms.ToArray(), "application/pdf", $"articles_{DateTime.Now:yyyyMMdd_HHmmss}.pdf");
            }
        }
        catch (Exception ex)
        {
            return Results.BadRequest(new { ok = false, error = ex.Message });
        }
    }
    else
    {
        return Results.BadRequest(new { ok = false, error = "Format must be 'csv' or 'pdf'" });
    }
});

// Statistics endpoint
app.MapGet("/api/statistics", async (AppDbContext db) =>
{
    var totalSubmissions = await db.Submissions.CountAsync();
    var submittedCount = await db.Submissions.CountAsync(s => s.Status == "submitted");
    var inReviewCount = await db.Submissions.CountAsync(s => s.Status == "in review");
    var acceptedCount = await db.Submissions.CountAsync(s => s.Status == "accepted");
    var rejectedCount = await db.Submissions.CountAsync(s => s.Status == "rejected");
    var publishedCount = await db.Submissions.CountAsync(s => s.Status == "published");
    
    var totalReviews = await db.Reviews.CountAsync();
    var avgRating = totalReviews > 0 ? await db.Reviews.AverageAsync(r => r.Rating) : 0;

    var stats = new
    {
        submissions = new
        {
            total = totalSubmissions,
            submitted = submittedCount,
            inReview = inReviewCount,
            accepted = acceptedCount,
            rejected = rejectedCount,
            published = publishedCount
        },
        reviews = new
        {
            total = totalReviews,
            avgRating = Math.Round(avgRating, 2)
        }
    };

    return Results.Ok(new { ok = true, data = stats });
});

app.Run();

// DTOs and models
record RegisterDto(string Email, string Password, string? Name);
record LoginDto(string Email, string Password);
record SubmissionCreateDto(string? Title, string? Abstract, string? Authors, string? Category, string[]? Keywords);
record SubmissionUpdateDto(string? Title, string? Abstract, string? Authors, string? Category, string[]? Keywords);
record AssignReviewerDto(Guid ReviewerId);
record ReviewCreateDto(string? Content, int Rating);
record SubmissionDecisionDto(string Status);
record ArticleExportDto(string? Title, string? Authors, string? Category, string? Keywords, string? Status, DateTime CreatedAt);
