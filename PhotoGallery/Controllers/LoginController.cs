using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using PhotoGallery.Interfaces;

namespace PhotoGallery.Controllers;

public class LoginController : Controller
{
    public readonly IConfiguration _configuration;
    private readonly IExternalAuthService _externalAuthService;
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public LoginController(IConfiguration configuration, IExternalAuthService externalAuthService, SignInManager<IdentityUser> signInManager, IHttpContextAccessor httpContextAccessor)
    {
        _configuration = configuration;
        _externalAuthService = externalAuthService;
        _signInManager = signInManager;
        _httpContextAccessor = httpContextAccessor;
    }

    public IActionResult Index()
    {
        return View();
    }

    public IActionResult Login()
    {
        return Ok();
    }
    
    public async Task<IActionResult> Logout()
    {
        // Clear the JWT
        Response.Cookies.Delete("jwt"); 
        await _signInManager.SignOutAsync();
        return RedirectToAction("Index", "Login");
    }
    
    public IActionResult GoogleLogin()
    {
        var redirectUri = Url.Action("GoogleCallback", "Login", null, Request.Scheme);
        var clientId = _configuration["Google:ClientId"];
        var scopes = "openid email profile";

        var authUrl = QueryHelpers.AddQueryString("https://accounts.google.com/o/oauth2/v2/auth", new Dictionary<string, string>
        {
            { "client_id", clientId },
            { "redirect_uri", redirectUri },
            { "response_type", "code" },
            { "scope", scopes },
            { "access_type", "offline" },
            { "prompt", "consent" }
        });

        return Redirect(authUrl);
    }
    
    public async Task<IActionResult> GoogleCallback(string code)
    {
        if (string.IsNullOrEmpty(code))
            return RedirectToAction("Index", "Login");

        var tokenEndpoint = "https://oauth2.googleapis.com/token";
        var redirectUri = Url.Action("GoogleCallback", "Login", null, Request.Scheme);

        var httpClient = new HttpClient();
        var response = await httpClient.PostAsync(tokenEndpoint, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "code", code },
            { "client_id", _configuration["Google:ClientId"] },
            { "client_secret", _configuration["Google:ClientSecret"] },
            { "redirect_uri", redirectUri },
            { "grant_type", "authorization_code" }
        }));

        if (!response.IsSuccessStatusCode)
            return RedirectToAction("Index");

        var json = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>();
        if (json?.IdToken == null)
            return RedirectToAction("Index");

        // // Send to AuthController
        // var authResponse = await httpClient.PostAsJsonAsync($"{Request.Scheme}://{Request.Host}/api/auth/external-login", new
        // {
        //     Provider = "Google",
        //     IdToken = json.IdToken
        // });
        //
        // if (!authResponse.IsSuccessStatusCode)
        //     return RedirectToAction("Index");
        //
        // var result = await authResponse.Content.ReadFromJsonAsync<AuthTokenResponse>();
        //
        // // Store JWT in a cookie or redirect with token
        // Response.Cookies.Append("jwt", result!.Token, new CookieOptions
        // {
        //     HttpOnly = true,
        //     Secure = true,
        //     SameSite = SameSiteMode.Strict
        // });
        //
        // return Redirect("/"); // or wherever you want
        
        var token = await _externalAuthService.HandleExternalLoginAsync("Google", json.IdToken);
        if (token == null)
            return RedirectToAction("Index");

        Response.Cookies.Append("jwt", token, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict
        });

        return Redirect("/");
    }
}

public class GoogleTokenResponse
{
    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; }

    [JsonPropertyName("id_token")]
    public string IdToken { get; set; }
}

public class AuthTokenResponse
{
    public string Token { get; set; }
}