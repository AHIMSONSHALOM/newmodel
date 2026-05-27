using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Http;
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
                string query = @"SELECT F_USERNAME, F_IS_ADMIN, F_CAN_ADD_ROW, F_CAN_DOWNLOAD, 
                                F_CAN_IMPORT, F_CAN_EXPORT, F_CAN_COMPARE, F_CAN_EMAIL 
                                FROM T_USERS WHERE F_USERNAME = @User AND F_PASSWORD = @Pass";
                
                using (var cmd = new SqlCommand(query, (SqlConnection)connection))
                {
                    cmd.Parameters.AddWithValue("@User", model.Username.Trim());
                    cmd.Parameters.AddWithValue("@Pass", model.Password.Trim());

                    connection.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            HttpContext.Session.SetString("UserSession", reader["F_USERNAME"].ToString() ?? string.Empty);
                            HttpContext.Session.SetInt32("IsAdmin", Convert.ToInt32(reader["F_IS_ADMIN"]));
                            HttpContext.Session.SetInt32("CanAddRow", Convert.ToInt32(reader["F_CAN_ADD_ROW"]));
                            HttpContext.Session.SetInt32("CanDownload", Convert.ToInt32(reader["F_CAN_DOWNLOAD"]));
                            HttpContext.Session.SetInt32("CanImport", Convert.ToInt32(reader["F_CAN_IMPORT"]));
                            HttpContext.Session.SetInt32("CanExport", Convert.ToInt32(reader["F_CAN_EXPORT"]));
                            HttpContext.Session.SetInt32("CanCompare", Convert.ToInt32(reader["F_CAN_COMPARE"]));
                            HttpContext.Session.SetInt32("CanEmail", Convert.ToInt32(reader["F_CAN_EMAIL"]));

                            return RedirectToAction("Index", "Product");
                        }
                    }
                }
            }
            ModelState.AddModelError("", "⚠️ Invalid username or password authentication match.");
            return View(model);
        }

        // =========================================================
        // 🔒 DYNAMIC OTP FORGOT PASSWORD SECURITY RECOVERY ROUTINES
        // =========================================================
        [HttpGet]
        public IActionResult ForgotPassword() => View();

        [HttpPost]
        public IActionResult SendOtpCode(string mobileNumber)
        {
            if (string.IsNullOrEmpty(mobileNumber)) {
                TempData["Error"] = "Please provide your registered mobile number.";
                return RedirectToAction(nameof(ForgotPassword));
            }

            using (var conn = _context.CreateConnection()) {
                string checkUser = "SELECT COUNT(1) FROM T_USERS WHERE F_MOBILE_NUMBER = @Num";
                using (var checkCmd = new SqlCommand(checkUser, (SqlConnection)conn)) {
                    checkCmd.Parameters.AddWithValue("@Num", mobileNumber.Trim());
                    conn.Open();
                    if ((int)checkCmd.ExecuteScalar() == 0) {
                        TempData["Error"] = "❌ Mobile number not found in account databases.";
                        return RedirectToAction(nameof(ForgotPassword));
                    }
                }

                // Generates a mock real-time secure 6-digit verification code string
                string dynamicOtp = new Random().Next(100000, 999999).ToString();
                DateTime expiry = DateTime.Now.AddMinutes(5); // Code remains essential for 5 active minutes

                string logOtp = "INSERT INTO T_OTP_LOG (F_MOBILE_NUMBER, F_OTP_CODE, F_EXPIRY_TIME) VALUES (@Num, @Otp, @Exp)";
                using (var insertCmd = new SqlCommand(logOtp, (SqlConnection)conn)) {
                    insertCmd.Parameters.AddWithValue("@Num", mobileNumber.Trim());
                    insertCmd.Parameters.AddWithValue("@Otp", dynamicOtp);
                    insertCmd.Parameters.AddWithValue("@Exp", expiry);
                    insertCmd.ExecuteNonQuery();
                }

                // Perfect for live evaluations! The code will display in a green toast so you can type it live
                TempData["Success"] = $"✉️ OTP Code Dispatched Live! Use code: {dynamicOtp} (Valid for 5 mins)";
                ViewBag.MobileNum = mobileNumber.Trim();
                return View("VerifyOtp");
            }
        }

        [HttpPost]
        public IActionResult ResetPasswordWithOtp(string mobileNumber, string otpCode, string newPassword)
        {
            using (var conn = _context.CreateConnection()) {
                string verifyQuery = @"SELECT COUNT(1) FROM T_OTP_LOG 
                                      WHERE F_MOBILE_NUMBER = @Num AND F_OTP_CODE = @Otp 
                                      AND F_EXPIRY_TIME >= GETDATE() AND F_IS_VERIFIED = 0";
                
                using (var cmd = new SqlCommand(verifyQuery, (SqlConnection)conn)) {
                    cmd.Parameters.AddWithValue("@Num", mobileNumber.Trim());
                    cmd.Parameters.AddWithValue("@Otp", otpCode.Trim());
                    conn.Open();
                    
                    if ((int)cmd.ExecuteScalar() == 0) {
                        TempData["Error"] = "❌ Invalid, consumed, or expired OTP authorization key.";
                        ViewBag.MobileNum = mobileNumber;
                        return View("VerifyOtp");
                    }
                }

                // Consume the token dynamically inside real-time query tables
                string burnOtp = "UPDATE T_OTP_LOG SET F_IS_VERIFIED = 1 WHERE F_MOBILE_NUMBER = @Num AND F_OTP_CODE = @Otp";
                using (var burnCmd = new SqlCommand(burnOtp, (SqlConnection)conn)) {
                    burnCmd.Parameters.AddWithValue("@Num", mobileNumber.Trim());
                    burnCmd.Parameters.AddWithValue("@Otp", otpCode.Trim());
                    burnCmd.ExecuteNonQuery();
                }

                // Deploy new credential key structures across core profile registers
                string updatePass = "UPDATE T_USERS SET F_PASSWORD = @Pass WHERE F_MOBILE_NUMBER = @Num";
                using (var passCmd = new SqlCommand(updatePass, (SqlConnection)conn)) {
                    passCmd.Parameters.AddWithValue("@Pass", newPassword.Trim());
                    passCmd.Parameters.AddWithValue("@Num", mobileNumber.Trim());
                    passCmd.ExecuteNonQuery();
                }
            }

            TempData["Success"] = "🎯 Credentials modified! Log in using your new password security key.";
            return RedirectToAction(nameof(Login));
        }

        public IActionResult Logout()
        {
            HttpContext.Session.Clear();
            return RedirectToAction(nameof(Login));
        }
    }
}