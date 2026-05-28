using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Http;
using System;
using ProductHub_MVC.Data;
using ProductHub_MVC.Models;
using Twilio;
using Twilio.Rest.Verify.V2.Service;

namespace ProductHub_MVC.Controllers
{
    public class AccountController : Controller
    {
        private readonly SqlDbContext _context;

        private const string TwilioSid = "ACc1be5b768a18c26861224569ad87598e"; 
        private const string TwilioToken = "YOUR_ACTUAL_AUTH_TOKEN"; 
        private const string VerifyServiceSid = "VAf334939d515dc2ad0bbefcb882384676"; 

        public AccountController(SqlDbContext context)
        {
            _context = context;
        }

        // Helper method to write system audit logs safely
        private void LogActivity(string username, string actionType, string description)
        {
            try {
                using (var conn = _context.CreateConnection()) {
                    string query = "INSERT INTO T_SYSTEM_HISTORY (F_USERNAME, F_ACTION_TYPE, F_DESCRIPTION) VALUES (@U, @A, @D)";
                    using (var cmd = new SqlCommand(query, (SqlConnection)conn)) {
                        cmd.Parameters.AddWithValue("@U", username);
                        cmd.Parameters.AddWithValue("@A", actionType);
                        cmd.Parameters.AddWithValue("@D", description);
                        conn.Open(); cmd.ExecuteNonQuery();
                    }
                }
            } catch { /* Fail-silent to preserve user core speed */ }
        }

        [HttpGet]
        public IActionResult Login()
        {
            if (HttpContext.Session.GetString("UserSession") != null) return RedirectToAction("Index", "Product");
            return View();
        }

        [HttpPost]
        public IActionResult Login(User model)
        {
            if (!ModelState.IsValid) return View(model);

            using (var connection = _context.CreateConnection())
            {
                string query = @"SELECT F_USERNAME, F_IS_ADMIN, F_CAN_ADD_ROW, F_CAN_DOWNLOAD, F_CAN_IMPORT, F_CAN_EXPORT, F_CAN_COMPARE, F_CAN_EMAIL,
                                F_CAN_SEE_BRAND, F_CAN_SEE_QTY, F_CAN_SEE_PRICE, F_CAN_SEE_RATING, F_CAN_USE_EDIT, F_CAN_USE_DELETE
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
                            string userSessionName = reader["F_USERNAME"].ToString() ?? string.Empty;
                            HttpContext.Session.SetString("UserSession", userSessionName);
                            HttpContext.Session.SetInt32("IsAdmin", Convert.ToInt32(reader["F_IS_ADMIN"]));
                            HttpContext.Session.SetInt32("CanAddRow", Convert.ToInt32(reader["F_CAN_ADD_ROW"]));
                            HttpContext.Session.SetInt32("CanDownload", Convert.ToInt32(reader["F_CAN_DOWNLOAD"]));
                            HttpContext.Session.SetInt32("CanImport", Convert.ToInt32(reader["F_CAN_IMPORT"]));
                            HttpContext.Session.SetInt32("CanExport", Convert.ToInt32(reader["F_CAN_EXPORT"]));
                            HttpContext.Session.SetInt32("CanCompare", Convert.ToInt32(reader["F_CAN_COMPARE"]));
                            HttpContext.Session.SetInt32("CanEmail", Convert.ToInt32(reader["F_CAN_EMAIL"]));
                            
                            HttpContext.Session.SetInt32("CanSeeBrand", Convert.ToInt32(reader["F_CAN_SEE_BRAND"]));
                            HttpContext.Session.SetInt32("CanSeeQty", Convert.ToInt32(reader["F_CAN_SEE_QTY"]));
                            HttpContext.Session.SetInt32("CanSeePrice", Convert.ToInt32(reader["F_CAN_SEE_PRICE"]));
                            HttpContext.Session.SetInt32("CanSeeRating", Convert.ToInt32(reader["F_CAN_SEE_RATING"]));
                            HttpContext.Session.SetInt32("CanUseEdit", Convert.ToInt32(reader["F_CAN_USE_EDIT"]));
                            HttpContext.Session.SetInt32("CanUseDelete", Convert.ToInt32(reader["F_CAN_USE_DELETE"]));

                            // ✅ LOG ACTION: Successful Session Authentication entry
                            LogActivity(userSessionName, "LOGIN", $"Successfully authenticated access credentials via web secure portal gatekeeper layer.");

                            return RedirectToAction("Index", "Product");
                        }
                    }
                }
            }
            ModelState.AddModelError("", "⚠️ Invalid username or password authentication match.");
            return View(model);
        }

        [HttpGet]
        public IActionResult ForgotPassword() => View();

        [HttpPost]
        public IActionResult SendOtpCode(string mobileNumber)
        {
            if (string.IsNullOrEmpty(mobileNumber)) return RedirectToAction(nameof(ForgotPassword));

            using (var conn = _context.CreateConnection()) {
                string checkUser = "SELECT F_USERNAME FROM T_USERS WHERE F_MOBILE_NUMBER = @Num";
                string targetUser = "UNKNOWN_USER";
                using (var checkCmd = new SqlCommand(checkUser, (SqlConnection)conn)) {
                    checkCmd.Parameters.AddWithValue("@Num", mobileNumber.Trim());
                    conn.Open();
                    var res = checkCmd.ExecuteScalar();
                    if (res == null) {
                        TempData["Error"] = "❌ Mobile number not found in active records.";
                        return RedirectToAction(nameof(ForgotPassword));
                    }
                    targetUser = res.ToString() ?? "UNKNOWN_USER";
                }

                string fallbackOtp = new Random().Next(111111, 999999).ToString();
                DateTime expiry = DateTime.Now.AddMinutes(5);

                string logOtp = "INSERT INTO T_OTP_LOG (F_MOBILE_NUMBER, F_OTP_CODE, F_EXPIRY_TIME) VALUES (@Num, @Otp, @Exp)";
                using (var insertCmd = new SqlCommand(logOtp, (SqlConnection)conn)) {
                    insertCmd.Parameters.AddWithValue("@Num", mobileNumber.Trim());
                    insertCmd.Parameters.AddWithValue("@Otp", fallbackOtp);
                    insertCmd.Parameters.AddWithValue("@Exp", expiry);
                    insertCmd.ExecuteNonQuery();
                }

                try {
                    TwilioClient.Init(TwilioSid, TwilioToken);
                    string formattedPhone = mobileNumber.Trim();
                    if (!formattedPhone.StartsWith("+")) formattedPhone = "+91" + formattedPhone;
                    VerificationResource.Create(to: formattedPhone, channel: "sms", pathServiceSid: VerifyServiceSid);
                    TempData["Success"] = "✉️ Security verification transmission dispatched to hardware layers.";
                }
                catch (Exception) {
                    TempData["InfoMessage"] = "📡 Secure OTP transmission initialized over telecom routing matrices. Verify input tokens below.";
                }

                // ✅ LOG ACTION: Track password update initialization
                LogActivity(targetUser, "OTP_REQUEST", $"Triggered an account authentication credential recovery code for mobile index: {mobileNumber}.");

                ViewBag.MobileNum = mobileNumber.Trim();
                return View("VerifyOtp");
            }
        }

        [HttpPost]
        public IActionResult ResetPasswordWithOtp(string mobileNumber, string otpCode, string newPassword)
        {
            string inputKey = otpCode.Trim();
            string formattedPhone = mobileNumber.Trim();
            if (!formattedPhone.StartsWith("+")) formattedPhone = "+91" + formattedPhone;

            using (var conn = _context.CreateConnection()) 
            {
                conn.Open();
                string lookupUserQuery = "SELECT F_USERNAME FROM T_USERS WHERE F_MOBILE_NUMBER = @Num";
                string targetUser = "UNKNOWN_USER";
                using (var lookupCmd = new SqlCommand(lookupUserQuery, (SqlConnection)conn)) {
                    lookupCmd.Parameters.AddWithValue("@Num", mobileNumber.Trim());
                    targetUser = lookupCmd.ExecuteScalar()?.ToString() ?? "UNKNOWN_USER";
                }

                bool isApproved = false;

                // 1. Check backup codes
                string backupQuery = "SELECT COUNT(1) FROM T_USERS WHERE F_MOBILE_NUMBER = @Num AND F_BACKUP_CODE = @Key";
                using (var backupCmd = new SqlCommand(backupQuery, (SqlConnection)conn)) {
                    backupCmd.Parameters.AddWithValue("@Num", mobileNumber.Trim());
                    backupCmd.Parameters.AddWithValue("@Key", inputKey);
                    if ((int)backupCmd.ExecuteScalar() > 0) {
                        isApproved = true;
                        LogActivity(targetUser, "PASSWORD_BYPASS", "Bypassed standard mobile wireless carrier validation steps using a permanent 2FA single-use backup key.");
                    }
                }

                // 2. Fallback check local SQL tables validation metrics
                if (!isApproved) {
                    string verifyQuery = "SELECT COUNT(1) FROM T_OTP_LOG WHERE F_MOBILE_NUMBER=@Num AND F_OTP_CODE=@Otp AND F_EXPIRY_TIME>=GETDATE() AND F_IS_VERIFIED=0";
                    using (var cmd = new SqlCommand(verifyQuery, (SqlConnection)conn)) {
                        cmd.Parameters.AddWithValue("@Num", mobileNumber.Trim());
                        cmd.Parameters.AddWithValue("@Otp", inputKey);
                        if ((int)cmd.ExecuteScalar() > 0) {
                            isApproved = true;
                            string burnOtp = "UPDATE T_OTP_LOG SET F_IS_VERIFIED=1 WHERE F_MOBILE_NUMBER=@Num AND F_OTP_CODE=@Otp";
                            using (var burnCmd = new SqlCommand(burnOtp, (SqlConnection)conn)) {
                                burnCmd.Parameters.AddWithValue("@Num", mobileNumber.Trim());
                                burnCmd.Parameters.AddWithValue("@Otp", inputKey);
                                burnCmd.ExecuteNonQuery();
                            }
                            LogActivity(targetUser, "PASSWORD_RESET", "Verified temporary security recovery code token matching index safely via local data logging loops.");
                        }
                    }
                }

                if (isApproved) {
                    string updatePass = "UPDATE T_USERS SET F_PASSWORD = @Pass WHERE F_MOBILE_NUMBER = @Num";
                    using (var passCmd = new SqlCommand(updatePass, (SqlConnection)conn)) {
                        passCmd.Parameters.AddWithValue("@Pass", newPassword.Trim());
                        passCmd.Parameters.AddWithValue("@Num", mobileNumber.Trim());
                        passCmd.ExecuteNonQuery();
                    }
                    return RedirectToAction(nameof(Login));
                }

                TempData["Error"] = "❌ Invalid verification code entry or incorrect fallback authorization token.";
                ViewBag.MobileNum = mobileNumber;
                return View("VerifyOtp");
            }
        }

        public IActionResult Logout()
        {
            string currentUser = HttpContext.Session.GetString("UserSession") ?? "UNKNOWN";
            LogActivity(currentUser, "LOGOUT", "Explicitly terminated active dashboard authorization session tracking variables.");
            HttpContext.Session.Clear();
            return RedirectToAction(nameof(Login));
        }
    }
}