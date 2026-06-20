using AIShield.Backend.Data;
using AIShield.Backend.Middleware;
using AIShield.Backend.Repositories;
using AIShield.Backend.Services;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);
const string FrontendCorsPolicy = "FrontendCorsPolicy";

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // 枚举使用字符串序列化，便于前端下拉框和调试阅读。
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("请通过配置或环境变量设置 ConnectionStrings:DefaultConnection。");
}

builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseSqlServer(connectionString);
});

builder.Services.AddScoped<IAgentRepository, AgentRepository>();
builder.Services.AddScoped<ISecurityRuleRepository, SecurityRuleRepository>();
builder.Services.AddScoped<IAuditRecordRepository, AuditRecordRepository>();

builder.Services.AddScoped<RuleEngine>();
builder.Services.AddScoped<ContentNormalizer>();
builder.Services.AddScoped<InputSecurityService>();
builder.Services.AddScoped<OutputSecurityService>();
builder.Services.AddScoped<ToolCallGuard>();
builder.Services.AddScoped<AuditService>();
builder.Services.AddScoped<JwtService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<AgentService>();
builder.Services.AddScoped<RuleConfigService>();
builder.Services.AddScoped<MemoryService>();
builder.Services.AddScoped<RagRerankService>();
builder.Services.AddScoped<TopicDriftService>();

builder.Services.AddCors(options =>
{
    options.AddPolicy(FrontendCorsPolicy, policy =>
    {
        policy.WithOrigins(
                "http://127.0.0.1:5173",
                "http://localhost:5173",
                "http://127.0.0.1:5174",
                "http://localhost:5174"
            )
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// CORS 必须放在鉴权中间件之前，否则浏览器预检请求会先被拦截。
app.UseCors(FrontendCorsPolicy);

// 统一处理管理端 JWT 和 Agent Key 鉴权。
app.UseMiddleware<ApiKeyAuthMiddleware>();

// 鉴权通过后再限流，减少未授权请求对限流计数的干扰。
app.UseMiddleware<RateLimitMiddleware>();

app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapControllers();

app.Run();
