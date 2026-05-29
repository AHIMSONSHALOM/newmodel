using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Http;
using System;
using ProductHub_MVC.Data;
using ProductHub_MVC.Models;
using System.Linq;
using Microsoft.Extensions.Configuration;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Net;
using System.Net.Mail;

namespace ProductHub_MVC.Controllers
{
    public class AccountController : Controller
    {
        private readonly SqlDbContext _context;
        private readonly string _fast2SmsApiKey;
        private readonly string _fast2SmsRoute;
        private readonly HttpClient _httpClient;

        public AccountController(SqlDbContext context, IConfiguration configuration)
        {
            _context = context;
            _fast2SmsApiKey = configuration["Fast2Sms:ApiKey"] ?? string.Empty;
            _fast2SmsRoute = configuration["Fast2Sms:Route"] ?? "q";
            _httpClient = new HttpClient();
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
        public async Task<IActionResult> SendOtpCode(string username, string mobileNumber)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(mobileNumber))
            {
                TempData["Error"] = "❌ Please enter username and registered mobile number.";
                return RedirectToAction(nameof(ForgotPassword));
            }

            string normalizedUser = username.Trim();
            string normalizedMobile = mobileNumber.Trim();
            string mobileDigits = new string(normalizedMobile.Where(char.IsDigit).ToArray());
            if (mobileDigits.Length > 10) mobileDigits = mobileDigits.Substring(mobileDigits.Length - 10);

            using (var conn = _context.CreateConnection()) {
                string checkUser = "SELECT F_USERNAME, F_EMAIL FROM T_USERS WHERE F_USERNAME = @User AND F_MOBILE_NUMBER = @Num";
                string targetUser = "UNKNOWN_USER";
                string userEmail = "";
                using (var checkCmd = new SqlCommand(checkUser, (SqlConnection)conn)) {
                    checkCmd.Parameters.AddWithValue("@User", normalizedUser);
                    checkCmd.Parameters.AddWithValue("@Num", mobileDigits);
                    conn.Open();
                    using (var reader = checkCmd.ExecuteReader()) {
                        if (reader.Read()) {
                            targetUser = reader["F_USERNAME"].ToString() ?? "UNKNOWN_USER";
                            userEmail = reader["F_EMAIL"]?.ToString() ?? "";
                        } else {
                            TempData["Error"] = "❌ Username and mobile number do not match our records.";
                            return RedirectToAction(nameof(ForgotPassword));
                        }
                    }
                }

                string generatedOtp = new Random().Next(100000, 999999).ToString();
                DateTime expiry = DateTime.Now.AddMinutes(5);

                try
                {
                    string deleteOldOtp = "DELETE FROM T_OTP_LOG WHERE F_MOBILE_NUMBER = @Num";
                    using (var deleteCmd = new SqlCommand(deleteOldOtp, (SqlConnection)conn))
                    {
                        deleteCmd.Parameters.AddWithValue("@Num", mobileDigits);
                        deleteCmd.ExecuteNonQuery();
                    }

                    string saveOtp = "INSERT INTO T_OTP_LOG (F_MOBILE_NUMBER, F_OTP_CODE, F_EXPIRY_TIME, F_IS_VERIFIED) VALUES (@Num, @Otp, @Exp, 0)";
                    using (var saveCmd = new SqlCommand(saveOtp, (SqlConnection)conn))
                    {
                        saveCmd.Parameters.AddWithValue("@Num", mobileDigits);
                        saveCmd.Parameters.AddWithValue("@Otp", generatedOtp);
                        saveCmd.Parameters.AddWithValue("@Exp", expiry);
                        saveCmd.ExecuteNonQuery();
                    }

                    bool otpDispatched = false;

                    // 1. Try SMS via Fast2SMS
                    if (!string.IsNullOrWhiteSpace(_fast2SmsApiKey) && _fast2SmsApiKey != "PASTE_FAST2SMS_API_KEY_HERE")
                    {
                        try
                        {
                            var sendRequest = new HttpRequestMessage(HttpMethod.Post, "https://www.fast2sms.com/dev/bulkV2");
                            sendRequest.Headers.Add("authorization", _fast2SmsApiKey);
                            sendRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                            {
                                { "route", _fast2SmsRoute },
                                { "message", $"Your ProductHub OTP is {generatedOtp}. Valid for 5 minutes." },
                                { "language", "english" },
                                { "flash", "0" },
                                { "numbers", mobileDigits }
                            });

                            var response = await _httpClient.SendAsync(sendRequest);
                            string apiOutput = await response.Content.ReadAsStringAsync();
                            if (response.IsSuccessStatusCode && !apiOutput.Contains("\"return\":false", StringComparison.OrdinalIgnoreCase))
                            {
                                otpDispatched = true;
                                TempData["Success"] = "✉️ OTP sent successfully to your registered mobile number.";
                            }
                        }
                        catch { /* Fail-silent to allow Email or UI fallback */ }
                    }

                    // 2. Try Email via SMTP
                    if (!otpDispatched && !string.IsNullOrWhiteSpace(userEmail))
                    {
                        try
                        {
                            string senderEmail = "tpass2829@gmail.com"; 
                            string senderPassword = "uozwlvrkykjzgjmj"; 
                            using (MailMessage mail = new MailMessage()) { 
                                mail.From = new MailAddress(senderEmail); 
                                mail.To.Add(userEmail.Trim()); 
                                mail.Subject = "🔑 Your ProductHub Password Recovery OTP"; 
                                mail.Body = $"Hello {targetUser},\n\nYour OTP code for password recovery is: {generatedOtp}\n\nThis OTP is valid for 5 minutes."; 
                                
                                using (SmtpClient smtp = new SmtpClient("smtp.gmail.com", 587)) { 
                                    smtp.EnableSsl = true; 
                                    smtp.UseDefaultCredentials = false; 
                                    smtp.Credentials = new NetworkCredential(senderEmail, senderPassword); 
                                    await smtp.SendMailAsync(mail); 
                                } 
                            }
                            otpDispatched = true;
                            TempData["Success"] = $"✉️ Password recovery OTP sent successfully to your registered email: {userEmail}";
                        }
                        catch { /* Fail-silent to allow UI fallback */ }
                    }

                    // 3. Fallback to Screen/UI Alert
                    if (!otpDispatched)
                    {
                        TempData["Success"] = $"⚠️ OTP Generated (Demo Mode): {generatedOtp} (Valid for 5 minutes).";
                    }
                }
                catch (Exception ex) {
                    TempData["Error"] = $"❌ OTP generation failed: {ex.Message}";
                    return RedirectToAction(nameof(ForgotPassword));
                }

                // ✅ LOG ACTION: Track password update initialization
                LogActivity(targetUser, "OTP_REQUEST", $"Triggered an account authentication credential recovery code for mobile index: {mobileDigits}.");

                ViewBag.MobileNum = mobileDigits;
                ViewBag.Username = normalizedUser;
                return View("VerifyOtp");
            }
        }

        [HttpPost]
        public async Task<IActionResult> ResetPasswordWithOtp(string username, string mobileNumber, string otpCode, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(mobileNumber) || string.IsNullOrWhiteSpace(otpCode) || string.IsNullOrWhiteSpace(newPassword))
            {
                TempData["Error"] = "❌ All fields are required to reset password.";
                ViewBag.MobileNum = mobileNumber;
                ViewBag.Username = username;
                return View("VerifyOtp");
            }

            string normalizedUser = username.Trim();
            string normalizedMobile = mobileNumber.Trim();
            string mobileDigits = new string(normalizedMobile.Where(char.IsDigit).ToArray());
            if (mobileDigits.Length > 10) mobileDigits = mobileDigits.Substring(mobileDigits.Length - 10);

            string inputKey = otpCode.Trim();

            using (var conn = _context.CreateConnection()) 
            {
                conn.Open();
                string lookupUserQuery = "SELECT F_USERNAME FROM T_USERS WHERE F_USERNAME = @User AND F_MOBILE_NUMBER = @Num";
                string targetUser = "UNKNOWN_USER";
                using (var lookupCmd = new SqlCommand(lookupUserQuery, (SqlConnection)conn)) {
                    lookupCmd.Parameters.AddWithValue("@User", normalizedUser);
                    lookupCmd.Parameters.AddWithValue("@Num", mobileDigits);
                    targetUser = lookupCmd.ExecuteScalar()?.ToString() ?? "UNKNOWN_USER";
                }

                if (targetUser == "UNKNOWN_USER")
                {
                    TempData["Error"] = "❌ Username and mobile number do not match our records.";
                    ViewBag.MobileNum = mobileDigits;
                    ViewBag.Username = normalizedUser;
                    return View("VerifyOtp");
                }

                bool isApproved = false;

                // 1. Check backup codes
                string backupQuery = "SELECT COUNT(1) FROM T_USERS WHERE F_MOBILE_NUMBER = @Num AND F_BACKUP_CODE = @Key";
                using (var backupCmd = new SqlCommand(backupQuery, (SqlConnection)conn)) {
                    backupCmd.Parameters.AddWithValue("@Num", mobileDigits);
                    backupCmd.Parameters.AddWithValue("@Key", inputKey);
                    if ((int)backupCmd.ExecuteScalar() > 0) {
                        isApproved = true;
                        LogActivity(targetUser, "PASSWORD_BYPASS", "Bypassed standard mobile wireless carrier validation steps using a permanent 2FA single-use backup key.");
                    }
                }

                // 2. Verify OTP from local OTP table (sent via SMS provider)
                if (!isApproved) {
                    try
                    {
                        string verifyQuery = @"SELECT COUNT(1) FROM T_OTP_LOG
                                               WHERE F_MOBILE_NUMBER=@Num
                                               AND F_OTP_CODE=@Otp
                                               AND F_EXPIRY_TIME>=GETDATE()
                                               AND F_IS_VERIFIED=0";
                        using (var verifyCmd = new SqlCommand(verifyQuery, (SqlConnection)conn))
                        {
                            verifyCmd.Parameters.AddWithValue("@Num", mobileDigits);
                            verifyCmd.Parameters.AddWithValue("@Otp", inputKey);
                            if ((int)verifyCmd.ExecuteScalar() > 0)
                            {
                                isApproved = true;
                                string markUsedQuery = "UPDATE T_OTP_LOG SET F_IS_VERIFIED=1 WHERE F_MOBILE_NUMBER=@Num AND F_OTP_CODE=@Otp";
                                using (var usedCmd = new SqlCommand(markUsedQuery, (SqlConnection)conn))
                                {
                                    usedCmd.Parameters.AddWithValue("@Num", mobileDigits);
                                    usedCmd.Parameters.AddWithValue("@Otp", inputKey);
                                    usedCmd.ExecuteNonQuery();
                                }
                                LogActivity(targetUser, "PASSWORD_RESET", "Verified SMS OTP and approved password reset.");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        TempData["Error"] = $"❌ OTP verification failed: {ex.Message}";
                        ViewBag.MobileNum = mobileDigits;
                        ViewBag.Username = normalizedUser;
                        return View("VerifyOtp");
                    }
                }

                if (isApproved) {
                    string updatePass = "UPDATE T_USERS SET F_PASSWORD = @Pass WHERE F_USERNAME = @User AND F_MOBILE_NUMBER = @Num";
                    using (var passCmd = new SqlCommand(updatePass, (SqlConnection)conn)) {
                        passCmd.Parameters.AddWithValue("@Pass", newPassword.Trim());
                        passCmd.Parameters.AddWithValue("@User", normalizedUser);
                        passCmd.Parameters.AddWithValue("@Num", mobileDigits);
                        passCmd.ExecuteNonQuery();
                    }
                    TempData["Success"] = "✅ Password reset successful. Please login with your new password.";
                    return RedirectToAction(nameof(Login));
                }

                TempData["Error"] = "❌ Invalid OTP code. Please try again.";
                ViewBag.MobileNum = mobileDigits;
                ViewBag.Username = normalizedUser;
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