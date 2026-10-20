using FlowDesk.Application.DTOs;
using FlowDesk.Application.Interfaces;
using FlowDesk.Domain.Entities;

namespace FlowDesk.Application.Services;

public class AuthenticationService
{
    private readonly IUserRepository _userRepository;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenService _jwtTokenService;

    public AuthenticationService(
        IUserRepository userRepository,
        IPasswordHasher passwordHasher,
        IJwtTokenService jwtTokenService)
    {
        _userRepository = userRepository;
        _passwordHasher = passwordHasher;
        _jwtTokenService = jwtTokenService;
    }

    public async Task<User> RegisterAsync(RegisterUserRequest dto)
    {
        if (string.IsNullOrWhiteSpace(dto.FullName))
        {
            throw new ArgumentException("Full name is required.");
        }

        if (string.IsNullOrWhiteSpace(dto.Email))
        {
            throw new ArgumentException("Email is required.");
        }

        if (string.IsNullOrWhiteSpace(dto.Password) ||
            dto.Password.Length < 12)
        {
            throw new ArgumentException(
                "Password must contain at least 12 characters.");
        }

        var email = dto.Email.Trim().ToLowerInvariant();

        if (!System.Net.Mail.MailAddress.TryCreate(
                email, out var parsedEmail) ||
            !string.Equals(
                parsedEmail.Address,
                email,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Invalid email address.");
        }

        var existingUser = await _userRepository.GetByEmailAsync(email);

        if (existingUser != null)
        {
            throw new InvalidOperationException(
                "Email is already registered.");
        }

        var passwordHash = _passwordHasher.Hash(dto.Password);

        var user = new User(
            dto.FullName,
            email,
            passwordHash
        );

        await _userRepository.AddAsync(user);
        await _userRepository.SaveChangesAsync();

        return user;
    }

    public async Task<LoginUserResponse> LoginAsync(LoginUserRequest dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Email) ||
            string.IsNullOrWhiteSpace(dto.Password))
        {
            throw new ArgumentException(
                "Email and password are required.");
        }

        var email = dto.Email.Trim().ToLowerInvariant();

        var user = await _userRepository.GetByEmailAsync(email);

        if (user == null ||
            !user.IsActive ||
            !_passwordHasher.Verify(dto.Password, user.PasswordHash))
        {
            throw new UnauthorizedAccessException(
                "Invalid email or password.");
        }

        var (accessToken, expiresAtUtc) =
            _jwtTokenService.GenerateToken(user);

        return new LoginUserResponse
        {
            Id = user.Id,
            FullName = user.FullName,
            Email = user.Email,
            Role = user.Role,
            AccessToken = accessToken,
            ExpiresAtUtc = expiresAtUtc
        };
    }
}