using AIShield.Backend.Dtos;
using AIShield.Backend.Services;
using Microsoft.AspNetCore.Mvc;

namespace AIShield.Backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AuthService _authService;

        public AuthController(AuthService authService)
        {
            _authService = authService;
        }

        // 本地管理员登录，登录成功后返回管理端 JWT
        [HttpPost("login")]
        public IActionResult Login([FromBody] AdminLoginRequest request)
        {
            var response = _authService.Login(request);

            if (!response.Success)
            {
                return Unauthorized(response);
            }

            return Ok(response);
        }
    }
}
