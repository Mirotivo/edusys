using System;
using System.Security.Claims;
using System.Threading.Tasks;
using Backend.Database.Models;
using Backend.Interfaces;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder.Extensions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Backend.Tests.Services
{
    public class UserServiceTests : BaseTest
    {
        private readonly IUserService _userService;

        private Mock<INotificationService>? _notificationServiceMock;
        private Mock<IFileUploadService>? _fileUploadServiceMock;
        private Mock<UserManager<User>>? _userManagerMock;
        private Mock<SignInManager<User>>? _signInManagerMock;

        public UserServiceTests(WebApplicationFactory<Program> factory) : base(factory)
        {
            _userService = _serviceProvider.GetRequiredService<IUserService>();
        }

        public Mock<SignInManager<User>> GetMockSignInManager()
        {
            var contextAccessorMock = new Mock<IHttpContextAccessor>();
            var claimsFactoryMock = new Mock<IUserClaimsPrincipalFactory<User>>();
            var optionsMock = new Mock<IOptions<IdentityOptions>>();
            optionsMock.Setup(o => o.Value).Returns(new IdentityOptions());
            var loggerMock = new Mock<ILogger<SignInManager<User>>>();
            var schemesMock = new Mock<IAuthenticationSchemeProvider>();
            var confirmationMock = new Mock<IUserConfirmation<User>>();

            var signInManagerMock = new Mock<SignInManager<User>>(
                _userManagerMock.Object,
                contextAccessorMock.Object,
                claimsFactoryMock.Object,
                optionsMock.Object,
                loggerMock.Object,
                schemesMock.Object,
                confirmationMock.Object
            );

            return signInManagerMock;
        }

        protected override void ConfigureServices(IServiceCollection services)
        {
            _notificationServiceMock = new Mock<INotificationService>();
            _notificationServiceMock
                .Setup(ns => ns.NotifyAsync(
                    It.IsAny<string>(),
                    NotificationEvent.ChangePassword,
                    It.IsAny<string>(),
                    It.IsAny<object>()
                ))
                .Returns(Task.CompletedTask);
            services.AddSingleton(_notificationServiceMock.Object);

            _fileUploadServiceMock = new Mock<IFileUploadService>();
            services.AddSingleton(_fileUploadServiceMock.Object);

            _userManagerMock = new Mock<UserManager<User>>(
                new Mock<IUserStore<User>>().Object,
                null,
                null,
                null,
                null,
                null,
                null,
                null,
                null
            );
            _userManagerMock
                .Setup(um => um.CreateAsync(It.IsAny<User>(), It.IsAny<string>()))
                .ReturnsAsync((User user, string password) =>
                {
                    if (user.Email == "newuser@example.com")
                    {
                        _dbContext.Users.Add(user);
                        _dbContext.SaveChanges();
                        return IdentityResult.Success;
                    }
                    return IdentityResult.Failed(new IdentityError { Description = "Invalid password" });
                });
            _userManagerMock
                .Setup(um => um.GenerateEmailConfirmationTokenAsync(It.IsAny<User>()))
                .ReturnsAsync("mocked-token");
            _userManagerMock
                .Setup(um => um.FindByEmailAsync(It.IsAny<string>()))
                .ReturnsAsync((string email) =>
                {
                    if (email == "testuser@example.com")
                    {
                        return new User { Email = email, PasswordHash = "hashed_password" };
                    }
                    return null;
                });
            _userManagerMock
                .Setup(um => um.GetRolesAsync(It.IsAny<User>()))
                .ReturnsAsync((User user) =>
                {
                    return new List<string> { UserRole.Tutor.ToString(), UserRole.Student.ToString() };
                });
            _userManagerMock
                .Setup(um => um.GeneratePasswordResetTokenAsync(It.IsAny<User>()))
                .ReturnsAsync((User user) =>
                {
                    if (user.Email == "existinguser@example.com")
                    {
                        return "mocked-reset-token";
                    }
                    return string.Empty;
                });
            services.AddSingleton(_userManagerMock.Object);

            _signInManagerMock = GetMockSignInManager();
            _signInManagerMock
                .Setup(sm => sm.PasswordSignInAsync(
                    It.IsAny<string>(),
                    It.IsAny<string>(),
                    It.IsAny<bool>(),
                    It.IsAny<bool>()
                ))
                .ReturnsAsync((string username, string password, bool isPersistent, bool lockoutOnFailure) =>
                {
                    return password == "correct_password" ? SignInResult.Success : SignInResult.Failed;
                });
            services.AddSingleton(_signInManagerMock.Object);
        }

        private DefaultHttpContext CreateHttpContext(string token, string userId, string email, string role)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers["Authorization"] = token;
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.Role, role)
            }, "Bearer"));

            return httpContext;
        }


        [Fact]
        public async Task RegisterUser_ShouldRegisterUser_WhenEmailDoesNotExist()
        {
            // Arrange
            var model = new RegisterViewModel
            {
                Email = "newuser@example.com",
                Password = "securepassword",
                ReferralToken = null
            };

            // Act
            var (result, error) = await _userService.RegisterUserAsync(model, "AU");

            // Assert
            Assert.True(result);
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == model.Email);
            Assert.NotNull(user);
            Assert.Equal("newuser@example.com", user.Email);
        }

        [Fact]
        public async Task RegisterUser_ShouldReturnFalse_WhenEmailAlreadyExists()
        {
            // Arrange
            await _dbContext.Users.AddAsync(new User { Email = "existinguser@example.com" });
            await _dbContext.SaveChangesAsync();

            var model = new RegisterViewModel
            {
                Email = "existinguser@example.com",
                Password = "securepassword",
                ReferralToken = null
            };

            // Act
            var (result, error) = await _userService.RegisterUserAsync(model, "AU");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task LoginUser_ShouldReturnToken_WhenCredentialsAreValid()
        {
            // Arrange
            var user = new User
            {
                Email = "testuser@example.com",
                Active = true,
                PasswordHash = "hashed_password"
            };
            await _dbContext.Users.AddAsync(user);
            await _dbContext.SaveChangesAsync();

            var model = new LoginViewModel
            {
                Email = "testuser@example.com",
                Password = "correct_password"
            };

            // Act
            var result = await _userService.LoginUserAsync(model);

            // Assert
            Assert.NotNull(result);
            Assert.NotEmpty(result?.Token);
            Assert.Contains("Tutor", result?.Roles);
        }

        [Fact]
        public async Task LoginUser_ShouldReturnNull_WhenCredentialsAreInvalid()
        {
            // Arrange
            var user = new User
            {
                Email = "testuser@example.com",
                Active = true,
                PasswordHash = "hashed_password"
            };
            await _dbContext.Users.AddAsync(user);
            await _dbContext.SaveChangesAsync();

            var model = new LoginViewModel
            {
                Email = "testuser@example.com",
                Password = "wrong_password"
            };

            // Act
            var result = await _userService.LoginUserAsync(model);

            // Assert
            Assert.Null(result);
        }

        [Fact]
        public async Task SendPasswordResetEmail_ShouldSendEmail_WhenUserExists_AndTokenIsValid()
        {
            // Arrange
            var user = new User
            {
                Id = "1",
                Email = "testuser@example.com",
                FirstName = "Test"
            };
            await _dbContext.Users.AddAsync(user);
            await _dbContext.SaveChangesAsync();

            var httpContext = CreateHttpContext("Bearer test-token", user.Id.ToString(), user.Email, "User");

            // Act
            var result = await _userService.SendPasswordResetEmail(user.Email);

            // Assert
            Assert.True(result);
        }

        [Fact]
        public async Task SendPasswordResetEmail_ShouldReturnFalse_WhenBearerTokenIsInvalid()
        {
            // Act
            var result = await _userService.SendPasswordResetEmail("nonexistentuser@example.com");

            // Assert
            Assert.False(result);
        }

        [Fact]
        public async Task GetUser_ShouldReturnUserDto_WhenUserExists()
        {
            // Arrange
            var user = new User
            {
                Id = "1",
                Email = "user@example.com",
                FirstName = "John",
                LastName = "Doe"
            };
            await _dbContext.Users.AddAsync(user);
            await _dbContext.SaveChangesAsync();

            // Act
            var result = await _userService.GetUserAsync(user.Id);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(user.Id, result.Id);
            Assert.Equal(user.Email, result.Email);
        }

        [Fact]
        public async Task GetUser_ShouldReturnNull_WhenUserDoesNotExist()
        {
            // Act
            var result = await _userService.GetUserAsync("999");

            // Assert
            Assert.Null(result);
        }
    }
}
