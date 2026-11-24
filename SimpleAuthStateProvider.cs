using System.Security.Claims;
using AMS.Models;
using AMS.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace AMS;

public class UserSessionData
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public UserRole Role { get; set; }
}

public class SimpleAuthStateProvider : AuthenticationStateProvider
{
    private readonly ProtectedSessionStorage _sessionStorage;
    private readonly AttendanceService _attendanceService;
    private readonly ClaimsPrincipal _anonymous = new(new ClaimsIdentity());
    private UserSessionData? _currentSession;

    public SimpleAuthStateProvider(ProtectedSessionStorage sessionStorage, AttendanceService attendanceService)
    {
        _sessionStorage = sessionStorage;
        _attendanceService = attendanceService;
    }

    public UserSessionData? CurrentSession => _currentSession;

    public override Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var principal = _currentSession != null ? BuildPrincipal(_currentSession) : _anonymous;
        return Task.FromResult(new AuthenticationState(principal));
    }

    public async Task<UserSessionData?> RestoreSessionFromStorageAsync()
    {
        try
        {
            var result = await _sessionStorage.GetAsync<UserSessionData>("UserSession");
            if (result.Success && result.Value != null)
            {
                _currentSession = result.Value;
                await _attendanceService.RestoreSessionAsync(_currentSession.Id);

                if (_attendanceService.CurrentUser == null)
                {
                    _currentSession = null;
                    await _sessionStorage.DeleteAsync("UserSession");
                    NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_anonymous)));
                    return null;
                }

                NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(BuildPrincipal(_currentSession))));
                return _currentSession;
            }
        }
        catch (InvalidOperationException)
        {
            // ProtectedSessionStorage / JS interop not ready during prerendering
        }
        catch
        {
            // Treat unreadable storage as unauthenticated
        }

        _currentSession = null;
        return null;
    }

    public async Task UpdateAuthenticationState(User? user)
    {
        ClaimsPrincipal claimsPrincipal;

        if (user != null)
        {
            var sessionData = new UserSessionData
            {
                Id = user.Id,
                Email = user.Email,
                FullName = user.FullName,
                Role = user.Role
            };

            _currentSession = sessionData;
            await _attendanceService.RestoreSessionAsync(user.Id);
            await _sessionStorage.SetAsync("UserSession", sessionData);
            claimsPrincipal = BuildPrincipal(sessionData);
        }
        else
        {
            _currentSession = null;
            _attendanceService.Logout();
            await _sessionStorage.DeleteAsync("UserSession");
            claimsPrincipal = _anonymous;
        }

        NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(claimsPrincipal)));
    }

    private static ClaimsPrincipal BuildPrincipal(UserSessionData session)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, session.Id.ToString()),
            new(ClaimTypes.Name, session.Email),
            new(ClaimTypes.GivenName, session.FullName),
            new(ClaimTypes.Role, session.Role.ToString())
        };

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "CustomAuth"));
    }
}