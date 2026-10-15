using System.ComponentModel.DataAnnotations;

namespace MediCore.Contracts.Auth;

public record LoginRequest(
    [Required, EmailAddress, MaxLength(254)] string Email,
    [Required, MaxLength(128)] string Password);

public record RefreshRequest([Required, MaxLength(200)] string RefreshToken);

public record UserDto(Guid Id, string DisplayName, string Email, string Role);

public record LoginResponse(string AccessToken, int ExpiresInSeconds, string RefreshToken, UserDto User);

public record RefreshResponse(string AccessToken, int ExpiresInSeconds, string RefreshToken);
