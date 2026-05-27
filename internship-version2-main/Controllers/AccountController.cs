using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using ProductHub_MVC.Data;
using ProductHub_MVC.Models;

namespace ProductHub_MVC.Controllers
{
    public class AccountController : Controller
    {
        private readonly SqlDbContext _context;

        public AccountController(SqlDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public IActionResult Login()
        {
            // If user is already logged in, skip page redirection straight to dashboard
            if (HttpContext.Session.GetString("UserSession") != null)
            {
                return RedirectToAction("Index", "Product");
            }
            return View();
        }

        [HttpPost]
        public IActionResult Login(User model)
        {
            if (!ModelState.IsValid) return View(model);

            using (var connection = _context.CreateConnection())
            {
                string query = "SELECT F_USER_ID FROM T_USERS WHERE F_USERNAME = @User AND F_PASSWORD = @Pass";
                using (var cmd = new SqlCommand(query, (SqlConnection)connection))
                {
                    cmd.Parameters.AddWithValue("@User", model.Username.Trim());
                    cmd.Parameters.AddWithValue("@Pass", model.Password.Trim());

                    connection.Open();
                    object result = cmd.ExecuteScalar();

                    if (result != null)
                    {
                        // ✅ LOGIN SUCCESS: Store identity string tokens within the session environment layer
                        HttpContext.Session.SetString("UserSession", model.Username);
                        return RedirectToAction("Index", "Product");
                    }
                }
            }

            ModelState.AddModelError("", "⚠️ Invalid username or password authentication match.");
            return View(model);
        }

        public IActionResult Logout()
        {
            // Clear session values entirely to wipe login footprint
            HttpContext.Session.Clear();
            return RedirectToAction(nameof(Login));
        }
    }
}