namespace Ltwlf.Azure.B2C;

/// <summary>
/// The application settings.
/// </summary>
public class ConfigOptions
{
    public string AppId { get; set; }
    public string AppSecret { get; set; }
    public string Tenant { get; set; }
    public string RedirectUri { get; set; }
    public string SignInPolicy { get; set; }
    public string VerificationUri { get; set; }
    public int ExpiresIn { get; set; } = 300;
    public int UserCodeLength { get; set; } = 6;
    public string UserCodePage { get; set; }
    public string SuccessPage { get; set; }
    public string ErrorPage { get; set; }
}

public record AuthorizationState(string DeviceCode, string UserCode, string VerificationUri, string Scope, TokenResponse TokenResponse, int ExpiresOn);

public record AuthorizationResponse(string DeviceCode, string UserCode, string VerificationUri, int? ExpiresIn, int? ExpiresOn, string Message, int Interval);

public record TokenResponse(string TokenType, string AccessToken, string Resource, string Scope, int ExpiresIn, long? ExpiresOn, long? NotBefore,
    string IdentityToken, int? IdentityTokenExpiresIn, string ProfileInfo, string RefreshToken, int? RefreshTokenExpiresIn);

public record TokenResponseError(string Error);
