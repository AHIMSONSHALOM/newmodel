using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using System;
using System.Threading.Tasks;
using ProductHub_MVC.Data;

namespace ProductHub_MVC.Middlewares
{
    public class SessionValidationMiddleware
    {
        private readonly RequestDelegate _next;

        public SessionValidationMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task InvokeAsync(HttpContext context, SqlDbContext dbContext)
        {
            var path = context.Request.Path.Value ?? "";

            // Bypass authentication routes and static assets
            if (path.StartsWith("/Account/Login", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/Account/GoogleLoginExternal", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/Account/GoogleCallback", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/Account/GoogleProfileSelector", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/Account/SelectGoogleProfile", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/Account/Register", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/Account/VerifyRegisterOtp", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/Account/ForgotPassword", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/Account/SendOtpCode", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/Account/ResetPasswordWithOtp", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/signin-google", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/css", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/js", StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith("/lib", StringComparison.OrdinalIgnoreCase) ||
                path.Equals("/", StringComparison.OrdinalIgnoreCase))
            {
                await _next(context);
                return;
            }

            var session = context.Session;
            string? loggedUser = session.GetString("UserSession");
            string? sessionVarId = session.GetString("UserSessionId");

            if (string.IsNullOrEmpty(loggedUser) || string.IsNullOrEmpty(sessionVarId))
            {
                session.Clear();
                context.Response.Redirect("/Account/Login");
                return;
            }

            // Validate active session state in SQL Server UserSessions database
            bool isSessionActive = false;
            try
            {
                using (var connection = dbContext.CreateConnection())
                {
                    string query = "SELECT IsActive FROM UserSessions WHERE SessionId = @SessionId";
                    using (var cmd = new SqlCommand(query, (SqlConnection)connection))
                    {
                        if (Guid.TryParse(sessionVarId, out Guid sessionGuid))
                        {
                            cmd.Parameters.AddWithValue("@SessionId", sessionGuid);
                            connection.Open();
                            var result = cmd.ExecuteScalar();
                            if (result != null)
                            {
                                isSessionActive = Convert.ToBoolean(result);
                            }
                        }
                    }
                }
            }
            catch
            {
                // DB transient exception fail-safe
                isSessionActive = true;
            }

            if (!isSessionActive)
            {
                session.Clear();
                context.Response.Redirect("/Account/Login?error=SessionExpired");
                return;
            }

            await _next(context);
        }
    }
}
