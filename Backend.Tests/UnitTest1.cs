namespace Backend.Tests;

using Backend.Models;
using Xunit;

public class UserModelTests
{
    [Fact]
    public void UserCreation_ShouldInitializeWithCorrectDefaults()
    {
        // Arrange & Act
        var user = new User
        {
            Email = "test@example.com",
            PasswordHash = "hashed_password_123",
            Name = "Jan Kowalski"
        };

        // Assert
        Assert.NotEqual(Guid.Empty, user.Id);
        Assert.Equal("test@example.com", user.Email);
        Assert.Equal("hashed_password_123", user.PasswordHash);
        Assert.Equal("Jan Kowalski", user.Name);
        Assert.Equal("author", user.Role);
        Assert.True(user.CreatedAt > DateTime.MinValue);
    }

    [Fact]
    public void UserRoleAssignment_ShouldAllowDifferentRoles()
    {
        // Arrange & Act
        var reviewer = new User
        {
            Email = "reviewer@example.com",
            PasswordHash = "hashed_password",
            Role = "reviewer"
        };

        var admin = new User
        {
            Email = "admin@example.com",
            PasswordHash = "hashed_password",
            Role = "admin"
        };

        // Assert
        Assert.Equal("reviewer", reviewer.Role);
        Assert.Equal("admin", admin.Role);
    }
}

public class SubmissionModelTests
{
    [Fact]
    public void SubmissionCreation_ShouldInitializeWithCorrectDefaults()
    {
        // Arrange
        var correspondingUserId = Guid.NewGuid();

        // Act
        var submission = new Submission
        {
            Title = "Badania nad AI",
            Abstract = "To jest streszczenie artykułu",
            Authors = "Anna Nowak, Piotr Lewandowski",
            Category = "Informatyka",
            Keywords = "AI;ML;Neural Networks",
            CorrespondingUserId = correspondingUserId
        };

        // Assert
        Assert.NotEqual(Guid.Empty, submission.Id);
        Assert.Equal("Badania nad AI", submission.Title);
        Assert.Equal("streszczenie artykułu", submission.Abstract);
        Assert.Equal("draft", submission.Status);
        Assert.Equal(correspondingUserId, submission.CorrespondingUserId);
        Assert.True(submission.CreatedAt > DateTime.MinValue);
    }

    [Fact]
    public void SubmissionStatus_ShouldChangeCorrectly()
    {
        // Arrange
        var submission = new Submission
        {
            Title = "Test Article",
            CorrespondingUserId = Guid.NewGuid()
        };

        // Act
        submission.Status = "under_review";

        // Assert
        Assert.Equal("under_review", submission.Status);
    }
}

public class ReviewModelTests
{
    [Fact]
    public void ReviewCreation_ShouldInitializeCorrectly()
    {
        // Arrange
        var submissionId = Guid.NewGuid();
        var reviewerId = Guid.NewGuid();

        // Act
        var review = new Review
        {
            SubmissionId = submissionId,
            ReviewerId = reviewerId,
            Content = "Artykuł jest dobrze napisany i merytoryczny",
            Rating = 8
        };

        // Assert
        Assert.NotEqual(Guid.Empty, review.Id);
        Assert.Equal(submissionId, review.SubmissionId);
        Assert.Equal(reviewerId, review.ReviewerId);
        Assert.Equal("Artykuł jest dobrze napisany i merytoryczny", review.Content);
        Assert.Equal(8, review.Rating);
        Assert.True(review.CreatedAt > DateTime.MinValue);
    }

    [Fact]
    public void ReviewRating_ShouldAcceptValidRatings()
    {
        // Arrange & Act
        var lowRatingReview = new Review { Rating = 1 };
        var highRatingReview = new Review { Rating = 10 };
        var middleRatingReview = new Review { Rating = 5 };

        // Assert
        Assert.Equal(1, lowRatingReview.Rating);
        Assert.Equal(10, highRatingReview.Rating);
        Assert.Equal(5, middleRatingReview.Rating);
    }
}