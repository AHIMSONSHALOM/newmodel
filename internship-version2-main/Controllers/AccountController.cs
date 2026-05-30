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
                                F_CAN_SEE_BRAND, F_CAN_SEE_QTY, F_CAN_SEE_PRICE, F_CAN_SEE_RATING, F_CAN_USE_EDIT, F_CAN_USE_DELETE, F_IS_APPROVED
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
                            int isApproved = Convert.ToInt32(reader["F_IS_APPROVED"]);
                            if (isApproved == 0)
                            {
                                ModelState.AddModelError("", "⏳ Access Pending: Your account is pending administrator approval. Please contact your administrator.");
                                return View(model);
                            }

                            string userSessionName = reader["F_USERNAME"].ToString() ?? string.Empty;
                            string sessionId = Guid.NewGuid().ToString("N");

                            // Enforce SSO Lockout by updating Session ID in DB
                            using (var conn2 = _context.CreateConnection())
                            {
                                string updateQuery = "UPDATE T_USERS SET F_SESSION_ID = @S WHERE F_USERNAME = @U";
                                using (var cmd2 = new SqlCommand(updateQuery, (SqlConnection)conn2))
                                {
                                    cmd2.Parameters.AddWithValue("@S", sessionId);
                                    cmd2.Parameters.AddWithValue("@U", userSessionName);
                                    conn2.Open();
                                    cmd2.ExecuteNonQuery();
                                }
                            }

                            HttpContext.Session.SetString("UserSession", userSessionName);
                            HttpContext.Session.SetString("UserSessionId", sessionId);
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
        public IActionResult GoogleLogin()
        {
            if (HttpContext.Session.GetString("UserSession") != null) return RedirectToAction("Index", "Product");
            
            // Generate a secure one-time login token
            string token = Guid.NewGuid().ToString("N");
            HttpContext.Session.SetString("GoogleLoginToken", token);
            ViewBag.GoogleLoginToken = token;
            
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> GoogleSendOtp([FromBody] GoogleAuthRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Email))
            {
                return Json(new { success = false, message = "Invalid auth request parameters." });
            }

            // Retrieve the active login session token to prevent forged requests
            string expectedToken = HttpContext.Session.GetString("GoogleLoginToken");
            if (string.IsNullOrEmpty(expectedToken) || request.Token != expectedToken)
            {
                return Json(new { success = false, message = "Security warning: Invalid or expired login token signature." });
            }

            // Generate a secure 6-digit OTP
            string generatedOtp = new Random().Next(100000, 999999).ToString();
            DateTime expiry = DateTime.Now.AddMinutes(5);

            // Buffer in Session
            HttpContext.Session.SetString("Google_Pending_Email", request.Email.Trim());
            HttpContext.Session.SetString("Google_Pending_Name", request.Name.Trim());
            HttpContext.Session.SetString("Google_Pending_Otp", generatedOtp);
            HttpContext.Session.SetString("Google_Pending_OtpExpiry", expiry.ToString());

            bool otpDispatched = false;

            // Attempt SMTP dispatch
            try
            {
                string senderEmail = "tpass2829@gmail.com"; 
                string senderPassword = "uozwlvrkykjzgjmj"; 
                using (MailMessage mail = new MailMessage()) { 
                    mail.From = new MailAddress(senderEmail); 
                    mail.To.Add(request.Email.Trim()); 
                    mail.Subject = "🔑 Google Verification Code"; 
                    mail.Body = $"To help protect your Google Account, Google wants to make sure it's really you trying to sign in.\n\nUse this verification code to complete sign-in:\n{generatedOtp}\n\nThis code is valid for 5 minutes.\n\nIf you didn't make this request, someone else might be trying to access your account."; 
                    
                    using (SmtpClient smtp = new SmtpClient("smtp.gmail.com", 587)) { 
                        smtp.EnableSsl = true; 
                        smtp.UseDefaultCredentials = false; 
                        smtp.Credentials = new NetworkCredential(senderEmail, senderPassword); 
                        await smtp.SendMailAsync(mail); 
                    } 
                }
                otpDispatched = true;
            }
            catch (Exception ex)
            {
                // Fail-silent, handled below via fallback
                System.Diagnostics.Debug.WriteLine($"SMTP Error: {ex.Message}");
            }

            if (otpDispatched)
            {
                return Json(new { success = true, isDemo = false });
            }
            else
            {
                // Fallback to screen demo OTP so testing does not block
                return Json(new { success = true, isDemo = true, demoOtp = generatedOtp });
            }
        }

        private void LoadUserPermissionsToSession(string username)
        {
            using (var connection = _context.CreateConnection())
            {
                string query = @"SELECT F_IS_ADMIN, F_CAN_ADD_ROW, F_CAN_DOWNLOAD, F_CAN_IMPORT, F_CAN_EXPORT, F_CAN_COMPARE, F_CAN_EMAIL,
                                F_CAN_SEE_BRAND, F_CAN_SEE_QTY, F_CAN_SEE_PRICE, F_CAN_SEE_RATING, F_CAN_USE_EDIT, F_CAN_USE_DELETE
                                FROM T_USERS WHERE F_USERNAME = @User";
                using (var cmd = new SqlCommand(query, (SqlConnection)connection))
                {
                    cmd.Parameters.AddWithValue("@User", username);
                    connection.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
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
                        }
                    }
                }
            }
        }

        [HttpPost]
        public IActionResult GoogleConfirmProfile([FromBody] GoogleConfirmProfileRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Username))
            {
                return Json(new { success = false, message = "Invalid request." });
            }

            string verifiedEmail = HttpContext.Session.GetString("Google_Verified_Email");
            if (string.IsNullOrEmpty(verifiedEmail))
            {
                return Json(new { success = false, message = "Session expired or invalid. Please re-authenticate." });
            }

            int isApprovedVal = 0;
            bool isValid = false;

            using (var connection = _context.CreateConnection())
            {
                string query = "SELECT F_IS_APPROVED FROM T_USERS WHERE F_USERNAME = @U AND F_EMAIL = @E";
                using (var cmd = new SqlCommand(query, (SqlConnection)connection))
                {
                    cmd.Parameters.AddWithValue("@U", request.Username.Trim());
                    cmd.Parameters.AddWithValue("@E", verifiedEmail.Trim());
                    connection.Open();
                    var res = cmd.ExecuteScalar();
                    if (res != null)
                    {
                        isValid = true;
                        isApprovedVal = Convert.ToInt32(res);
                    }
                }
            }

            if (!isValid)
            {
                return Json(new { success = false, message = "Selected username does not match the verified email." });
            }

            if (isApprovedVal == 1)
            {
                string sessionId = Guid.NewGuid().ToString("N");
                HttpContext.Session.SetString("UserSession", request.Username.Trim());
                HttpContext.Session.SetString("UserSessionId", sessionId);

                using (var connection = _context.CreateConnection())
                {
                    string updateQuery = "UPDATE T_USERS SET F_SESSION_ID = @S WHERE F_USERNAME = @U";
                    using (var cmd = new SqlCommand(updateQuery, (SqlConnection)connection))
                    {
                        cmd.Parameters.AddWithValue("@S", sessionId);
                        cmd.Parameters.AddWithValue("@U", request.Username.Trim());
                        connection.Open();
                        cmd.ExecuteNonQuery();
                    }
                }

                LoadUserPermissionsToSession(request.Username.Trim());
                HttpContext.Session.Remove("Google_Verified_Email"); // Clean up choice

                LogActivity(request.Username.Trim(), "GOOGLE_LOGIN", $"Successfully verified profile choice and signed in. User: {request.Username.Trim()}, Email: {verifiedEmail}");
                return Json(new { success = true, isApproved = true });
            }
            else
            {
                LogActivity(request.Username.Trim(), "GOOGLE_REGISTER_PENDING", $"Submitted Google sign-in request for profile choice. User: {request.Username.Trim()}, Email: {verifiedEmail} is pending admin approval.");
                return Json(new { success = true, isApproved = false });
            }
        }

        [HttpPost]
        public IActionResult GoogleVerifyOtp([FromBody] GoogleVerifyRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Code))
            {
                return Json(new { success = false, message = "Verification code is required." });
            }

            string pendingEmail = HttpContext.Session.GetString("Google_Pending_Email") ?? "";
            string pendingName = HttpContext.Session.GetString("Google_Pending_Name") ?? "";
            string expectedOtp = HttpContext.Session.GetString("Google_Pending_Otp") ?? "";
            string expiryStr = HttpContext.Session.GetString("Google_Pending_OtpExpiry") ?? "";

            if (string.IsNullOrEmpty(pendingEmail) || string.IsNullOrEmpty(expectedOtp))
            {
                return Json(new { success = false, message = "Session expired or invalid state. Please try logging in again." });
            }

            if (request.Code.Trim() != expectedOtp)
            {
                return Json(new { success = false, message = "Incorrect verification code. Please try again." });
            }

            if (!string.IsNullOrEmpty(expiryStr) && DateTime.TryParse(expiryStr, out DateTime expiryTime) && DateTime.Now > expiryTime)
            {
                return Json(new { success = false, message = "The verification code has expired. Please request a new one." });
            }

            // Search for all profiles matching the email
            List<Dictionary<string, object>> profiles = new List<Dictionary<string, object>>();
            using (var connection = _context.CreateConnection())
            {
                string query = "SELECT F_USERNAME, F_IS_APPROVED FROM T_USERS WHERE F_EMAIL = @Email";
                using (var cmd = new SqlCommand(query, (SqlConnection)connection))
                {
                    cmd.Parameters.AddWithValue("@Email", pendingEmail.Trim());
                    connection.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            profiles.Add(new Dictionary<string, object>
                            {
                                { "username", reader["F_USERNAME"].ToString() ?? "" },
                                { "isApproved", Convert.ToInt32(reader["F_IS_APPROVED"]) }
                            });
                        }
                    }
                }
            }

            // Invalidate the session-bound login token and verification codes
            HttpContext.Session.Remove("GoogleLoginToken");
            HttpContext.Session.Remove("Google_Pending_Email");
            HttpContext.Session.Remove("Google_Pending_Name");
            HttpContext.Session.Remove("Google_Pending_Otp");
            HttpContext.Session.Remove("Google_Pending_OtpExpiry");

            if (profiles.Count > 1)
            {
                // Multi-User Profile Selector Flow
                HttpContext.Session.SetString("Google_Verified_Email", pendingEmail);
                
                // Return multiProfile response
                return Json(new { success = true, multiProfile = true, profiles = profiles });
            }

            string targetUser = "";
            int isApprovedVal = 0;
            bool userExists = (profiles.Count == 1);

            if (userExists)
            {
                targetUser = profiles[0]["username"].ToString() ?? "";
                isApprovedVal = Convert.ToInt32(profiles[0]["isApproved"]);
            }
            else
            {
                // Auto registration logic:
                string baseUsername = pendingEmail.Split('@')[0];
                string uniqueUsername = baseUsername;
                
                int suffix = 1;
                bool isUnique = false;
                using (var connection = _context.CreateConnection())
                {
                    connection.Open();
                    while (!isUnique)
                    {
                        string checkUserQuery = "SELECT COUNT(1) FROM T_USERS WHERE F_USERNAME = @User";
                        using (var checkCmd = new SqlCommand(checkUserQuery, (SqlConnection)connection))
                        {
                            checkCmd.Parameters.AddWithValue("@User", uniqueUsername);
                            int count = (int)checkCmd.ExecuteScalar();
                            if (count == 0)
                            {
                                isUnique = true;
                            }
                            else
                            {
                                uniqueUsername = baseUsername + suffix;
                                suffix++;
                            }
                        }
                    }
                }

                string generatedPassword = Guid.NewGuid().ToString("N");

                // Save to T_USERS with default reader permissions and F_IS_APPROVED = 0 (Pending)
                using (var conn = _context.CreateConnection())
                {
                    string query = @"INSERT INTO T_USERS (
                                        F_USERNAME, F_PASSWORD, F_MOBILE_NUMBER, F_EMAIL, F_IS_ADMIN, 
                                        F_CAN_ADD_ROW, F_CAN_DOWNLOAD, F_CAN_IMPORT, F_CAN_EXPORT, F_CAN_COMPARE, F_CAN_EMAIL,
                                        F_CAN_SEE_BRAND, F_CAN_SEE_QTY, F_CAN_SEE_PRICE, F_CAN_SEE_RATING, F_CAN_USE_EDIT, F_CAN_USE_DELETE,
                                        F_RESTRICTED_BRAND, F_IS_APPROVED
                                     ) VALUES (
                                        @U, @P, '', @E, 0, 
                                        0, 0, 0, 0, 0, 0,
                                        1, 1, 1, 1, 0, 0,
                                        'ALL', 0
                                     )";
                    using (var cmd = new SqlCommand(query, (SqlConnection)conn))
                    {
                        cmd.Parameters.AddWithValue("@U", uniqueUsername);
                        cmd.Parameters.AddWithValue("@P", generatedPassword);
                        cmd.Parameters.AddWithValue("@E", pendingEmail.Trim());
                        conn.Open();
                        cmd.ExecuteNonQuery();
                    }
                }

                targetUser = uniqueUsername;
                isApprovedVal = 0;
            }

            if (isApprovedVal == 1)
            {
                string sessionId = Guid.NewGuid().ToString("N");
                HttpContext.Session.SetString("UserSession", targetUser);
                HttpContext.Session.SetString("UserSessionId", sessionId);

                using (var connection = _context.CreateConnection())
                {
                    string updateQuery = "UPDATE T_USERS SET F_SESSION_ID = @S WHERE F_USERNAME = @U";
                    using (var cmd = new SqlCommand(updateQuery, (SqlConnection)connection))
                    {
                        cmd.Parameters.AddWithValue("@S", sessionId);
                        cmd.Parameters.AddWithValue("@U", targetUser);
                        connection.Open();
                        cmd.ExecuteNonQuery();
                    }
                }

                LoadUserPermissionsToSession(targetUser);

                LogActivity(targetUser, "GOOGLE_LOGIN", $"Successfully verified Google 2-Step OTP and signed in. User: {targetUser}, Email: {pendingEmail}");
                return Json(new { success = true, isApproved = true });
            }
            else
            {
                LogActivity(targetUser, "GOOGLE_REGISTER_PENDING", $"Submitted Google sign-in request for verification. User: {targetUser}, Email: {pendingEmail} is pending admin approval.");
                return Json(new { success = true, isApproved = false });
            }
        }

        [HttpGet]
        public IActionResult Register()
        {
            if (HttpContext.Session.GetString("UserSession") != null) return RedirectToAction("Index", "Product");
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> Register(string username, string mobileNumber, string email)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(mobileNumber) || string.IsNullOrWhiteSpace(email))
            {
                TempData["Error"] = "❌ All fields are required for registration.";
                return View();
            }

            string normalizedUser = username.Trim();
            string normalizedMobile = mobileNumber.Trim();
            string mobileDigits = new string(normalizedMobile.Where(char.IsDigit).ToArray());
            if (mobileDigits.Length > 10) mobileDigits = mobileDigits.Substring(mobileDigits.Length - 10);

            // Check if username already exists in database
            using (var conn = _context.CreateConnection())
            {
                string checkUser = "SELECT COUNT(1) FROM T_USERS WHERE F_USERNAME = @User";
                using (var checkCmd = new SqlCommand(checkUser, (SqlConnection)conn))
                {
                    checkCmd.Parameters.AddWithValue("@User", normalizedUser);
                    conn.Open();
                    if ((int)checkCmd.ExecuteScalar() > 0)
                    {
                        TempData["Error"] = "❌ Username is already taken. Please choose another one.";
                        return View();
                    }
                }
            }

            string generatedOtp = new Random().Next(100000, 999999).ToString();
            DateTime expiry = DateTime.Now.AddMinutes(5);

            // Store in Session for verification
            HttpContext.Session.SetString("Pending_Username", normalizedUser);
            HttpContext.Session.SetString("Pending_Mobile", mobileDigits);
            HttpContext.Session.SetString("Pending_Email", email.Trim());
            HttpContext.Session.SetString("Pending_Otp", generatedOtp);
            HttpContext.Session.SetString("Pending_OtpExpiry", expiry.ToString());

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
                        { "message", $"Your ProductHub registration OTP is {generatedOtp}. Valid for 5 minutes." },
                        { "language", "english" },
                        { "flash", "0" },
                        { "numbers", mobileDigits }
                    });

                    var response = await _httpClient.SendAsync(sendRequest);
                    string apiOutput = await response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode && !apiOutput.Contains("\"return\":false", StringComparison.OrdinalIgnoreCase))
                    {
                        otpDispatched = true;
                        TempData["Success"] = "✉️ Registration OTP sent successfully to your mobile number.";
                    }
                }
                catch { }
            }

            // 2. Try Email via SMTP
            if (!otpDispatched && !string.IsNullOrWhiteSpace(email))
            {
                try
                {
                    string senderEmail = "tpass2829@gmail.com"; 
                    string senderPassword = "uozwlvrkykjzgjmj"; 
                    using (MailMessage mail = new MailMessage()) { 
                        mail.From = new MailAddress(senderEmail); 
                        mail.To.Add(email.Trim()); 
                        mail.Subject = "🔑 ProductHub Registration OTP"; 
                        mail.Body = $"Hello {normalizedUser},\n\nYour OTP code for verification is: {generatedOtp}\n\nThis OTP is valid for 5 minutes."; 
                        
                        using (SmtpClient smtp = new SmtpClient("smtp.gmail.com", 587)) { 
                            smtp.EnableSsl = true; 
                            smtp.UseDefaultCredentials = false; 
                            smtp.Credentials = new NetworkCredential(senderEmail, senderPassword); 
                            await smtp.SendMailAsync(mail); 
                        } 
                    }
                    otpDispatched = true;
                    TempData["Success"] = $"✉️ Registration OTP sent successfully to your email: {email}";
                }
                catch { }
            }

            // 3. Fallback to Screen/UI Alert
            if (!otpDispatched)
            {
                TempData["Success"] = $"⚠️ OTP Generated (Demo Mode): {generatedOtp} (Valid for 5 minutes).";
            }

            // Log activity step into history table
            LogActivity(normalizedUser, "REGISTER_OTP_REQUEST", $"Requested a registration authentication OTP code for mobile: {mobileDigits}.");

            return RedirectToAction(nameof(VerifyRegisterOtp));
        }

        [HttpGet]
        public IActionResult VerifyRegisterOtp()
        {
            if (HttpContext.Session.GetString("Pending_Username") == null) return RedirectToAction(nameof(Register));
            ViewBag.Username = HttpContext.Session.GetString("Pending_Username");
            ViewBag.MobileNum = HttpContext.Session.GetString("Pending_Mobile");
            return View();
        }

        [HttpPost]
        public async Task<IActionResult> VerifyRegisterOtp(string otpCode)
        {
            string pendingUser = HttpContext.Session.GetString("Pending_Username") ?? "";
            string pendingMobile = HttpContext.Session.GetString("Pending_Mobile") ?? "";
            string pendingEmail = HttpContext.Session.GetString("Pending_Email") ?? "";
            string expectedOtp = HttpContext.Session.GetString("Pending_Otp") ?? "";
            string expiryStr = HttpContext.Session.GetString("Pending_OtpExpiry") ?? "";

            if (string.IsNullOrEmpty(pendingUser) || string.IsNullOrEmpty(expectedOtp))
            {
                return RedirectToAction(nameof(Register));
            }

            if (string.IsNullOrWhiteSpace(otpCode) || otpCode.Trim() != expectedOtp)
            {
                TempData["Error"] = "❌ Invalid OTP code. Please try again.";
                ViewBag.Username = pendingUser;
                ViewBag.MobileNum = pendingMobile;
                return View();
            }

            if (!string.IsNullOrEmpty(expiryStr) && DateTime.TryParse(expiryStr, out DateTime expiryTime) && DateTime.Now > expiryTime)
            {
                TempData["Error"] = "❌ OTP code has expired. Please register again.";
                return RedirectToAction(nameof(Register));
            }

            // Generate secure random password
            string autoGeneratedPassword = GenerateRandomPassword(8);

            // Insert new user into database
            using (var conn = _context.CreateConnection())
            {
                string query = @"INSERT INTO T_USERS (
                                    F_USERNAME, F_PASSWORD, F_MOBILE_NUMBER, F_EMAIL, F_IS_ADMIN, 
                                    F_CAN_ADD_ROW, F_CAN_DOWNLOAD, F_CAN_IMPORT, F_CAN_EXPORT, F_CAN_COMPARE, F_CAN_EMAIL,
                                    F_CAN_SEE_BRAND, F_CAN_SEE_QTY, F_CAN_SEE_PRICE, F_CAN_SEE_RATING, F_CAN_USE_EDIT, F_CAN_USE_DELETE,
                                    F_RESTRICTED_BRAND, F_IS_APPROVED
                                 ) VALUES (
                                    @U, @P, @M, @E, 0, 
                                    0, 0, 0, 0, 0, 0,
                                    1, 1, 1, 1, 0, 0,
                                    'ALL', 0
                                 )";
                using (var cmd = new SqlCommand(query, (SqlConnection)conn))
                {
                    cmd.Parameters.AddWithValue("@U", pendingUser);
                    cmd.Parameters.AddWithValue("@P", autoGeneratedPassword);
                    cmd.Parameters.AddWithValue("@M", pendingMobile);
                    cmd.Parameters.AddWithValue("@E", (object)pendingEmail ?? DBNull.Value);
                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }

            // Log activity step into history table
            LogActivity(pendingUser, "USER_REGISTERED", "Completed public registration OTP verification. Account created in Pending Approval state.");

            // Dispatch username and auto-generated password via Email/SMS
            bool notificationDispatched = await SendCredentialsToNewUser(pendingUser, autoGeneratedPassword, pendingEmail, pendingMobile);

            // Clean up registration session values
            HttpContext.Session.Remove("Pending_Username");
            HttpContext.Session.Remove("Pending_Mobile");
            HttpContext.Session.Remove("Pending_Email");
            HttpContext.Session.Remove("Pending_Otp");
            HttpContext.Session.Remove("Pending_OtpExpiry");

            if (notificationDispatched)
            {
                TempData["Success"] = "✅ Registration submitted! Your credentials have been sent, but your account is pending administrator approval before you can log in.";
            }
            else
            {
                TempData["Success"] = $"✅ Registration submitted! Account: {pendingUser}, Password: {autoGeneratedPassword} (Note this, but login will fail until approved by administrator).";
            }

            return RedirectToAction(nameof(Login));
        }

        private string GenerateRandomPassword(int length = 8)
        {
            const string validChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";
            Random random = new Random();
            return new string(Enumerable.Repeat(validChars, length)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        private async Task<bool> SendCredentialsToNewUser(string username, string password, string email, string mobile)
        {
            string messageText = $"Welcome to ProductHub! Your username is: {username} and your generated password is: {password}";
            bool smsSent = false;
            bool emailSent = false;

            // 1. Try SMS via Fast2SMS
            if (!string.IsNullOrWhiteSpace(_fast2SmsApiKey) && _fast2SmsApiKey != "PASTE_FAST2SMS_API_KEY_HERE" && !string.IsNullOrWhiteSpace(mobile))
            {
                try
                {
                    var sendRequest = new HttpRequestMessage(HttpMethod.Post, "https://www.fast2sms.com/dev/bulkV2");
                    sendRequest.Headers.Add("authorization", _fast2SmsApiKey);
                    sendRequest.Content = new FormUrlEncodedContent(new Dictionary<string, string>
                    {
                        { "route", _fast2SmsRoute },
                        { "message", messageText },
                        { "language", "english" },
                        { "flash", "0" },
                        { "numbers", mobile }
                    });

                    var response = await _httpClient.SendAsync(sendRequest);
                    string apiOutput = await response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode && !apiOutput.Contains("\"return\":false", StringComparison.OrdinalIgnoreCase))
                    {
                        smsSent = true;
                    }
                }
                catch { }
            }

            // 2. Try Email via SMTP
            if (!string.IsNullOrWhiteSpace(email))
            {
                try
                {
                    string scheme = HttpContext.Request.Scheme;
                    string host = HttpContext.Request.Host.ToUriComponent();
                    string baseUrl = $"{scheme}://{host}";

                    string senderEmail = "tpass2829@gmail.com"; 
                    string senderPassword = "uozwlvrkykjzgjmj"; 
                    using (MailMessage mail = new MailMessage()) { 
                        mail.From = new MailAddress(senderEmail, "ProductHub Support"); 
                        mail.To.Add(email.Trim()); 
                        mail.Subject = "🎉 Welcome to ProductHub - Account Created Successfully"; 
                        mail.IsBodyHtml = true;
                        mail.Body = $@"
                            <div style='font-family: &quot;Segoe UI&quot;, Tahoma, Geneva, Verdana, sans-serif; max-width: 600px; margin: auto; padding: 30px; border: 1px solid #e2e8f0; border-radius: 16px; background-color: #ffffff; box-shadow: 0 4px 12px rgba(0,0,0,0.02);'>
                                <div style='text-align: center; margin-bottom: 24px;'>
                                    <div style='background-color: #E8F5E9; color: #16A34A; display: inline-block; padding: 12px; border-radius: 50%; margin-bottom: 12px;'>
                                        <svg width='32' height='32' fill='currentColor' viewBox='0 0 24 24' style='display: block;'>
                                            <path d='M12 2C6.48 2 2 6.48 2 12s4.48 10 10 10 10-4.48 10-10S17.52 2 12 2zm1 15h-2v-6h2v6zm0-8h-2V7h2v2z'/>
                                        </svg>
                                    </div>
                                    <h2 style='color: #1E293B; margin: 0 0 8px 0; font-weight: 700; font-size: 22px;'>Welcome to ProductHub!</h2>
                                    <p style='color: #64748B; font-size: 14px; margin: 0;'>Your login profile has been successfully created.</p>
                                </div>
                                
                                <div style='margin-bottom: 24px; line-height: 1.6; color: #334155; font-size: 15px;'>
                                    <p>Hello <strong>{username}</strong>,</p>
                                    <p>Your account is ready! Below are your secure login credentials:</p>
                                    
                                    <div style='background-color: #F8FAFC; border: 1px solid #E2E8F0; border-radius: 8px; padding: 18px; margin: 20px 0;'>
                                        <table style='width: 100%; border-collapse: collapse; font-size: 14.5px;'>
                                            <tr>
                                                <td style='padding: 6px 0; color: #64748B; width: 140px; font-weight: 500;'>Username:</td>
                                                <td style='padding: 6px 0; color: #1E293B; font-weight: 700;'>{username}</td>
                                            </tr>
                                            <tr>
                                                <td style='padding: 6px 0; color: #64748B; font-weight: 500;'>Temporary Pass:</td>
                                                <td style='padding: 6px 0; color: #EF4444; font-weight: 700; font-family: monospace; font-size: 15.5px;'>{password}</td>
                                            </tr>
                                            <tr>
                                                <td style='padding: 6px 0; color: #64748B; font-weight: 500;'>Mobile Number:</td>
                                                <td style='padding: 6px 0; color: #1E293B; font-weight: 600;'>{mobile}</td>
                                            </tr>
                                        </table>
                                    </div>
                                    
                                    <div style='background-color: #FFFBEB; padding: 20px; border-left: 4px solid #D97706; margin: 24px 0; border-radius: 0 8px 8px 0;'>
                                        <p style='margin: 0; font-weight: 700; color: #B45309; font-size: 14.2px;'>⚠️ Action Required:</p>
                                        <p style='margin: 6px 0 0 0; color: #78350F; font-size: 13.5px;'>Please sign in and ensure that your profile information is up to date, including your <strong>mobile number</strong>, to secure your account and configure 2-Step OTP options.</p>
                                    </div>
                                </div>
                                
                                <div style='text-align: center; margin-top: 30px;'>
                                    <a href='{baseUrl}' style='background-color: #16A34A; color: #ffffff; padding: 12px 28px; text-decoration: none; border-radius: 8px; font-weight: 600; display: inline-block; box-shadow: 0 2px 4px rgba(22,163,74,0.15); transition: background-color 0.15s ease;'>Log into ProductHub</a>
                                </div>
                                
                                <hr style='border: 0; border-top: 1px solid #e2e8f0; margin: 30px 0;' />
                                <div style='text-align: center; font-size: 12px; color: #94A3B8;'>
                                    This is a secure security notification from ProductHub.<br/>
                                    If you did not request this account activation, please contact system support.
                                </div>
                            </div>"; 
                        
                        using (SmtpClient smtp = new SmtpClient("smtp.gmail.com", 587)) { 
                            smtp.EnableSsl = true; 
                            smtp.UseDefaultCredentials = false; 
                            smtp.Credentials = new NetworkCredential(senderEmail, senderPassword); 
                            await smtp.SendMailAsync(mail); 
                        } 
                    }
                    emailSent = true;
                }
                catch { }
            }

            return smsSent || emailSent;
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

                // Verify OTP from local OTP table (sent via SMS / Email / UI alert)
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