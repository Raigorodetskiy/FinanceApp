using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using FinanceApp.API.Controllers;
using FinanceApp.API.Services;
using FinanceApp.Core.Models;
using FinanceApp.Data.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FinanceApp.Core.Tests;

public class AuthUsersControllerTests
{
    [Fact]
    public async Task Login_ByUsername_Works()
    {
        await using var context = CreateContext();
        var security = CreateSecurityService();
        var user = CreateAdaptiveUser(security, "Trader.User", "trader@example.com", "Password123!");
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var auth = CreateAuthController(context, security);
        var result = await auth.Login(new LoginDto { Identifier = "Trader.User", Password = "Password123!" });

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<LoginResponseDto>(ok.Value);
        Assert.False(string.IsNullOrWhiteSpace(payload.Token));
        Assert.Equal("Trader.User", payload.User.Username);
    }

    [Fact]
    public async Task Login_ByEmail_AndLegacyEmailPayload_Work()
    {
        await using var context = CreateContext();
        var security = CreateSecurityService();
        var user = CreateAdaptiveUser(security, "TraderUser", "trader@example.com", "Password123!");
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var auth = CreateAuthController(context, security);

        var byIdentifier = await auth.Login(new LoginDto { Identifier = "trader@example.com", Password = "Password123!" });
        Assert.IsType<OkObjectResult>(byIdentifier);

        var byLegacyEmailField = await auth.Login(new LoginDto { Email = "trader@example.com", Password = "Password123!" });
        Assert.IsType<OkObjectResult>(byLegacyEmailField);
    }

    [Fact]
    public async Task Login_Identifier_IsTrimmedAndCaseInsensitive()
    {
        await using var context = CreateContext();
        var security = CreateSecurityService();
        var user = CreateAdaptiveUser(security, "AlphaUser", "alpha@example.com", "Password123!");
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var auth = CreateAuthController(context, security);
        var byUsername = await auth.Login(new LoginDto { Identifier = "  alphauser  ", Password = "Password123!" });
        var byEmail = await auth.Login(new LoginDto { Identifier = "  ALPHA@EXAMPLE.COM  ", Password = "Password123!" });

        Assert.IsType<OkObjectResult>(byUsername);
        Assert.IsType<OkObjectResult>(byEmail);
    }

    [Fact]
    public async Task Login_InvalidCredentials_ReturnSameGenericMessage()
    {
        await using var context = CreateContext();
        var security = CreateSecurityService();
        context.Users.Add(CreateAdaptiveUser(security, "KnownUser", "known@example.com", "Password123!"));
        await context.SaveChangesAsync();

        var auth = CreateAuthController(context, security);

        var unknownUserResult = await auth.Login(new LoginDto { Identifier = "unknown", Password = "wrong" });
        var badPasswordResult = await auth.Login(new LoginDto { Identifier = "KnownUser", Password = "wrong" });

        var unknownUnauthorized = Assert.IsType<UnauthorizedObjectResult>(unknownUserResult);
        var badPasswordUnauthorized = Assert.IsType<UnauthorizedObjectResult>(badPasswordResult);

        Assert.Equal("Invalid credentials", unknownUnauthorized.Value);
        Assert.Equal("Invalid credentials", badPasswordUnauthorized.Value);
    }

    [Fact]
    public async Task Login_LegacySha256_RehashesToAdaptive()
    {
        await using var context = CreateContext();
        var security = CreateSecurityService();
        var user = new User
        {
            Username = "legacy",
            NormalizedUsername = "LEGACY",
            Email = "legacy@example.com",
            NormalizedEmail = "LEGACY@EXAMPLE.COM",
            PasswordHash = LegacySha256("Password123!"),
            CreatedAt = DateTime.UtcNow,
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var auth = CreateAuthController(context, security);
        var result = await auth.Login(new LoginDto { Identifier = "legacy", Password = "Password123!" });

        Assert.IsType<OkObjectResult>(result);
        var reloaded = await context.Users.SingleAsync();
        Assert.NotEqual(LegacySha256("Password123!"), reloaded.PasswordHash);
        Assert.StartsWith("AQAAAA", reloaded.PasswordHash);
    }

    [Fact]
    public async Task Login_AdaptiveHash_Works()
    {
        await using var context = CreateContext();
        var security = CreateSecurityService();
        context.Users.Add(CreateAdaptiveUser(security, "adaptive", "adaptive@example.com", "Password123!"));
        await context.SaveChangesAsync();

        var auth = CreateAuthController(context, security);
        var result = await auth.Login(new LoginDto { Identifier = "adaptive", Password = "Password123!" });

        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Login_MalformedHash_IsRejected()
    {
        await using var context = CreateContext();
        var security = CreateSecurityService();
        context.Users.Add(new User
        {
            Username = "bad",
            NormalizedUsername = "BAD",
            Email = "bad@example.com",
            NormalizedEmail = "BAD@EXAMPLE.COM",
            PasswordHash = "not-a-valid-hash",
            CreatedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();

        var auth = CreateAuthController(context, security);
        var result = await auth.Login(new LoginDto { Identifier = "bad", Password = "Password123!" });

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        Assert.Equal("Invalid credentials", unauthorized.Value);
    }

    [Fact]
    public async Task Register_UsesAdaptiveHash()
    {
        await using var context = CreateContext();
        var security = CreateSecurityService();
        var controller = CreateUsersController(context, security, userId: 1);

        var result = await controller.Register(new RegisterDto
        {
            Username = "newuser",
            Email = "newuser@example.com",
            Password = "Password123!",
        });

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.IsType<MeUserDto>(created.Value);
        var persisted = await context.Users.SingleAsync();
        Assert.StartsWith("AQAAAA", persisted.PasswordHash);
    }

    [Fact]
    public async Task UpdateProfile_RequiresValidCurrentPassword_AndRejectsInvalidUsernames()
    {
        await using var context = CreateContext();
        var security = CreateSecurityService();
        var user = CreateAdaptiveUser(security, "firstuser", "first@example.com", "Password123!");
        context.Users.Add(user);
        context.Users.Add(CreateAdaptiveUser(security, "otheruser", "other@example.com", "Password123!"));
        await context.SaveChangesAsync();

        var controller = CreateUsersController(context, security, user.Id);

        var wrongPassword = await controller.UpdateProfile(new UpdateProfileDto
        {
            Username = "seconduser",
            CurrentPassword = "wrong",
        });
        Assert.Equal(StatusCodes.Status400BadRequest, ((ObjectResult)wrongPassword.Result!).StatusCode ?? 400);

        var duplicateCase = await controller.UpdateProfile(new UpdateProfileDto
        {
            Username = "OtherUser",
            CurrentPassword = "Password123!",
        });
        Assert.Equal(StatusCodes.Status409Conflict, ((ObjectResult)duplicateCase.Result!).StatusCode ?? 409);

        var withAt = await controller.UpdateProfile(new UpdateProfileDto
        {
            Username = "name@bad",
            CurrentPassword = "Password123!",
        });
        Assert.Equal(StatusCodes.Status400BadRequest, ((ObjectResult)withAt.Result!).StatusCode ?? 400);

        var ok = await controller.UpdateProfile(new UpdateProfileDto
        {
            Username = " second.user ",
            CurrentPassword = "Password123!",
        });

        var okResult = Assert.IsType<OkObjectResult>(ok.Result);
        var dto = Assert.IsType<MeUserDto>(okResult.Value);
        Assert.Equal("second.user", dto.Username);

        var persisted = await context.Users.FirstAsync(u => u.Id == user.Id);
        Assert.Equal("second.user", persisted.Username);
        Assert.Equal("SECOND.USER", persisted.NormalizedUsername);
    }

    [Fact]
    public async Task UpdateProfile_RejectsUsernameEqualToAnotherEmail()
    {
        await using var context = CreateContext();
        var security = CreateSecurityService();
        var user = CreateAdaptiveUser(security, "firstuser", "first@example.com", "Password123!");
        context.Users.Add(user);
        context.Users.Add(CreateAdaptiveUser(security, "otheruser", "target@example.com", "Password123!"));
        await context.SaveChangesAsync();

        var controller = CreateUsersController(context, security, user.Id);

        var result = await controller.UpdateProfile(new UpdateProfileDto
        {
            Username = "target@example.com",
            CurrentPassword = "Password123!",
        });

        var badRequest = Assert.IsType<BadRequestObjectResult>(result.Result);
        Assert.Equal("Логин не должен содержать символ @.", badRequest.Value);
    }

    [Fact]
    public async Task ChangePassword_ThenOldPasswordFails_NewPasswordWorks()
    {
        await using var context = CreateContext();
        var security = CreateSecurityService();
        var user = CreateAdaptiveUser(security, "changepw", "changepw@example.com", "Password123!");
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var usersController = CreateUsersController(context, security, user.Id);
        var auth = CreateAuthController(context, security);

        var changeResult = await usersController.ChangePassword(new ChangePasswordDto
        {
            CurrentPassword = "Password123!",
            NewPassword = "Password456!",
            ConfirmPassword = "Password456!",
        });
        Assert.IsType<NoContentResult>(changeResult);

        var oldLoginResult = await auth.Login(new LoginDto { Identifier = "changepw", Password = "Password123!" });
        Assert.IsType<UnauthorizedObjectResult>(oldLoginResult);

        var newLoginResult = await auth.Login(new LoginDto { Identifier = "changepw", Password = "Password456!" });
        Assert.IsType<OkObjectResult>(newLoginResult);
    }

    [Fact]
    public async Task GetMe_DoesNotExposePasswordHash()
    {
        await using var context = CreateContext();
        var security = CreateSecurityService();
        var user = CreateAdaptiveUser(security, "safe", "safe@example.com", "Password123!");
        context.Users.Add(user);
        await context.SaveChangesAsync();

        var controller = CreateUsersController(context, security, user.Id);
        var result = await controller.GetMe();

        var dto = Assert.IsType<MeUserDto>(result.Value);
        Assert.Equal(user.Username, dto.Username);
        var json = System.Text.Json.JsonSerializer.Serialize(dto);
        Assert.DoesNotContain("PasswordHash", json);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString("N"))
            .Options;

        return new AppDbContext(options);
    }

    private static IUserSecurityService CreateSecurityService() =>
        new UserSecurityService(new PasswordHasher<User>());

    private static AuthController CreateAuthController(AppDbContext context, IUserSecurityService securityService)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Key"] = "super-secure-test-key-1234567890",
                ["Jwt:Issuer"] = "tests",
                ["Jwt:Audience"] = "tests",
            })
            .Build();

        return new AuthController(context, config, securityService);
    }

    private static UsersController CreateUsersController(AppDbContext context, IUserSecurityService securityService, int userId)
    {
        return new UsersController(context, securityService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    ], "TestAuth"))
                }
            }
        };
    }

    private static User CreateAdaptiveUser(IUserSecurityService securityService, string username, string email, string password)
    {
        var user = new User
        {
            Username = username,
            NormalizedUsername = username.Trim().ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.Trim().ToUpperInvariant(),
            CreatedAt = DateTime.UtcNow,
        };
        user.PasswordHash = securityService.HashPassword(user, password);
        return user;
    }

    private static string LegacySha256(string password)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
