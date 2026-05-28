using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using ProductHub_MVC.Data;
using ProductHub_MVC.Models;
using OfficeOpenXml; 

namespace ProductHub_MVC.Controllers
{
    public class ProductController : Controller
    {
        private readonly SqlDbContext _context;

        public ProductController(SqlDbContext context)
        {
            _context = context;
        }

        private void LogActivity(string actionType, string description)
        {
            try {
                string user = HttpContext.Session.GetString("UserSession") ?? "SYSTEM";
                using (var conn = _context.CreateConnection()) {
                    string query = "INSERT INTO T_SYSTEM_HISTORY (F_USERNAME, F_ACTION_TYPE, F_DESCRIPTION) VALUES (@U, @A, @D)";
                    using (var cmd = new SqlCommand(query, (SqlConnection)conn)) {
                        cmd.Parameters.AddWithValue("@U", user);
                        cmd.Parameters.AddWithValue("@A", actionType);
                        cmd.Parameters.AddWithValue("@D", description);
                        conn.Open(); cmd.ExecuteNonQuery();
                    }
                }
            } catch { /* Fail silent */ }
        }

        // =========================================================
        // 1. DATA GRID: MAIN VIEW (WITH DYNAMIC BRAND SECURITY ISOLATION)
        // =========================================================
        public IActionResult Index(string sortBy, string brandFilter, double? minPrice, double? maxPrice, double? minRating)
        {
            if (HttpContext.Session.GetString("UserSession") == null) return RedirectToAction("Login", "Account");

            ViewBag.LoggedUser = HttpContext.Session.GetString("UserSession");
            ViewBag.IsAdmin = HttpContext.Session.GetInt32("IsAdmin") ?? 0;
            ViewBag.CanAddRow = HttpContext.Session.GetInt32("CanAddRow") ?? 0;
            ViewBag.CanDownload = HttpContext.Session.GetInt32("CanDownload") ?? 0;
            ViewBag.CanImport = HttpContext.Session.GetInt32("CanImport") ?? 0;
            ViewBag.CanExport = HttpContext.Session.GetInt32("CanExport") ?? 0;
            ViewBag.CanCompare = HttpContext.Session.GetInt32("CanCompare") ?? 0;
            ViewBag.CanEmail = HttpContext.Session.GetInt32("CanEmail") ?? 0;
            
            ViewBag.CanSeeBrand = HttpContext.Session.GetInt32("CanSeeBrand") ?? 0;
            ViewBag.CanSeeQty = HttpContext.Session.GetInt32("CanSeeQty") ?? 0;
            ViewBag.CanSeePrice = HttpContext.Session.GetInt32("CanSeePrice") ?? 0;
            ViewBag.CanSeeRating = HttpContext.Session.GetInt32("CanSeeRating") ?? 0;
            ViewBag.CanUseEdit = HttpContext.Session.GetInt32("CanUseEdit") ?? 0;
            ViewBag.CanUseDelete = HttpContext.Session.GetInt32("CanUseDelete") ?? 0;

            // ✅ READ LIVE BRAND ISOLATION ASSIGNED TO THIS ACTIVE LOGGED IN USER ROW
            string restrictedBrand = "ALL";
            using (var connection = _context.CreateConnection()) {
                string checkQuery = "SELECT F_RESTRICTED_BRAND FROM T_USERS WHERE F_USERNAME = @User";
                using (var cmd = new SqlCommand(checkQuery, (SqlConnection)connection)) {
                    cmd.Parameters.AddWithValue("@User", ViewBag.LoggedUser);
                    connection.Open();
                    var res = cmd.ExecuteScalar();
                    if (res != null) restrictedBrand = res.ToString();
                }
            }

            // Overwrite incoming search filter if administrator forced an isolated target brand row constraint
            if (restrictedBrand != "ALL") {
                brandFilter = restrictedBrand;
                ViewBag.ForcedIsolationNotice = $"🔒 Restricted Profile View: Data query locked exclusively onto brand data catalogs matching context logs: '{restrictedBrand}'.";
            }

            List<Product> products = FetchFilteredProducts(brandFilter, minPrice, maxPrice, minRating, sortBy);
            
            ViewBag.CurrentSort = sortBy;
            ViewBag.CurrentBrand = brandFilter;
            ViewBag.MinPrice = minPrice;
            ViewBag.MaxPrice = maxPrice;
            ViewBag.MinRating = minRating;

            return View(products);
        }

        // =========================================================================
        // 2. DYNAMIC ACCESS MATRIX: GENERATES DROP-DOWN SEEDS LIVE FROM REAL RECORDS
        // =========================================================================
        public IActionResult Users()
        {
            if (HttpContext.Session.GetInt32("IsAdmin") != 1) return RedirectToAction(nameof(Index));

            // ✅ CORE GENERATOR: Fetch unique brands dynamically directly from data tables
            List<string> dynamicDistinctBrands = new List<string> { "ALL" };
            using (var connection = _context.CreateConnection()) {
                string brandListQuery = "SELECT DISTINCT F_BRAND FROM T_PRODUCTS WHERE F_BRAND IS NOT NULL AND F_BRAND <> '' ORDER BY F_BRAND ASC";
                using (var cmd = new SqlCommand(brandListQuery, (SqlConnection)connection)) {
                    connection.Open();
                    using (var reader = cmd.ExecuteReader()) {
                        while (reader.Read()) dynamicDistinctBrands.Add(reader["F_BRAND"].ToString());
                    }
                }
            }
            ViewBag.AvailableBrandsList = dynamicDistinctBrands;

            List<Dictionary<string, object>> userProfiles = new List<Dictionary<string, object>>();
            using (var connection = _context.CreateConnection())
            {
                string query = "SELECT F_USER_ID, F_USERNAME, F_PASSWORD, F_MOBILE_NUMBER, F_BACKUP_CODE, F_RESTRICTED_BRAND, F_IS_ADMIN, F_CAN_ADD_ROW, F_CAN_DOWNLOAD, F_CAN_IMPORT, F_CAN_EXPORT, F_CAN_COMPARE, F_CAN_EMAIL, F_CAN_SEE_BRAND, F_CAN_SEE_QTY, F_CAN_SEE_PRICE, F_CAN_SEE_RATING, F_CAN_USE_EDIT, F_CAN_USE_DELETE FROM T_USERS";
                using (var cmd = new SqlCommand(query, (SqlConnection)connection))
                {
                    connection.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read()) userProfiles.Add(new Dictionary<string, object> {
                            { "Id", reader["F_USER_ID"] }, { "Name", reader["F_USERNAME"].ToString() ?? "" },
                            { "Password", reader["F_PASSWORD"].ToString() ?? "" }, { "Mobile", reader["F_MOBILE_NUMBER"].ToString() ?? "" },
                            { "BackupCode", reader["F_BACKUP_CODE"].ToString() ?? "" }, 
                            { "RestrictedBrand", reader["F_RESTRICTED_BRAND"].ToString() ?? "ALL" }, // Pack current assignment string
                            { "IsAdmin", Convert.ToInt32(reader["F_IS_ADMIN"]) },
                            { "AddRow", Convert.ToInt32(reader["F_CAN_ADD_ROW"]) }, { "Download", Convert.ToInt32(reader["F_CAN_DOWNLOAD"]) },
                            { "Import", Convert.ToInt32(reader["F_CAN_IMPORT"]) }, { "Export", Convert.ToInt32(reader["F_CAN_EXPORT"]) },
                            { "Compare", Convert.ToInt32(reader["F_CAN_COMPARE"]) }, { "Email", Convert.ToInt32(reader["F_CAN_EMAIL"]) },
                            { "SeeBrand", Convert.ToInt32(reader["F_CAN_SEE_BRAND"]) }, { "SeeQty", Convert.ToInt32(reader["F_CAN_SEE_QTY"]) },
                            { "SeePrice", Convert.ToInt32(reader["F_CAN_SEE_PRICE"]) }, { "SeeRating", Convert.ToInt32(reader["F_CAN_SEE_RATING"]) },
                            { "UseEdit", Convert.ToInt32(reader["F_CAN_USE_EDIT"]) }, { "UseDelete", Convert.ToInt32(reader["F_CAN_USE_DELETE"]) }
                        });
                    }
                }
            }
            return View(userProfiles);
        }

        [HttpPost]
        public IActionResult SaveUserConfig(int userId, string userNameParam, string restrictedBrand, int addRow, int download, int import, int export, int compare, int email,
                                            int seeBrand, int seeQty, int seePrice, int seeRating, int useEdit, int useDelete)
        {
            if (HttpContext.Session.GetInt32("IsAdmin") != 1) return Forbid();
            using (var connection = _context.CreateConnection()) {
                string query = @"UPDATE T_USERS SET 
                                F_CAN_ADD_ROW=@Add, F_CAN_DOWNLOAD=@Dl, F_CAN_IMPORT=@Imp, F_CAN_EXPORT=@Exp, F_CAN_COMPARE=@Comp, F_CAN_EMAIL=@Em,
                                F_CAN_SEE_BRAND=@B, F_CAN_SEE_QTY=@Q, F_CAN_SEE_PRICE=@P, F_CAN_SEE_RATING=@R, F_CAN_USE_EDIT=@E, F_CAN_USE_DELETE=@D,
                                F_RESTRICTED_BRAND=@Restrict
                                WHERE F_USER_ID=@Id";
                using (var cmd = new SqlCommand(query, (SqlConnection)connection)) {
                    cmd.Parameters.AddWithValue("@Id", userId); cmd.Parameters.AddWithValue("@Add", addRow);
                    cmd.Parameters.AddWithValue("@Dl", download); cmd.Parameters.AddWithValue("@Imp", import);
                    cmd.Parameters.AddWithValue("@Exp", export); cmd.Parameters.AddWithValue("@Comp", compare);
                    cmd.Parameters.AddWithValue("@Em", email);
                    cmd.Parameters.AddWithValue("@B", seeBrand); cmd.Parameters.AddWithValue("@Q", seeQty);
                    cmd.Parameters.AddWithValue("@P", seePrice); cmd.Parameters.AddWithValue("@R", seeRating);
                    cmd.Parameters.AddWithValue("@E", useEdit); cmd.Parameters.AddWithValue("@D", useDelete);
                    cmd.Parameters.AddWithValue("@Restrict", restrictedBrand.Trim()); // Bind text drop down selection value
                    connection.Open(); cmd.ExecuteNonQuery();
                }
            }
            LogActivity("PERMISSIONS", $"Modified authorization switches and structural visibility brand constraint to '{restrictedBrand}' for account user: '{userNameParam}'.");
            TempData["SuccessMessage"] = "🛡️ Permission configuration synchronized live in real-time!";
            return RedirectToAction(nameof(Users));
        }

        [HttpGet]
        public IActionResult History(string targetUser)
        {
            if (HttpContext.Session.GetString("UserSession") == null) return RedirectToAction("Login", "Account");
            if (HttpContext.Session.GetInt32("IsAdmin") != 1) return Forbid();

            ViewBag.LoggedUser = HttpContext.Session.GetString("UserSession");
            ViewBag.IsAdmin = HttpContext.Session.GetInt32("IsAdmin") ?? 0;
            ViewBag.SelectedFilterUser = targetUser;

            List<Dictionary<string, object>> auditLogsCollection = new List<Dictionary<string, object>>();
            using (var connection = _context.CreateConnection())
            {
                string query = "SELECT F_LOG_ID, F_USERNAME, F_ACTION_TYPE, F_DESCRIPTION, F_TIMESTAMP FROM T_SYSTEM_HISTORY ";
                if (!string.IsNullOrEmpty(targetUser)) query += " WHERE F_USERNAME = @TgtUser ";
                query += " ORDER BY F_TIMESTAMP DESC";

                using (var cmd = new SqlCommand(query, (SqlConnection)connection))
                {
                    if (!string.IsNullOrEmpty(targetUser)) cmd.Parameters.AddWithValue("@TgtUser", targetUser.Trim());
                    connection.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read()) {
                            auditLogsCollection.Add(new Dictionary<string, object>{
                                { "Id", reader["F_LOG_ID"] }, { "User", reader["F_USERNAME"].ToString() ?? "" },
                                { "Action", reader["F_ACTION_TYPE"].ToString() ?? "" }, { "Desc", reader["F_DESCRIPTION"].ToString() ?? "" },
                                { "Time", Convert.ToDateTime(reader["F_TIMESTAMP"]).ToString("dd MMM yyyy, hh:mm tt") }
                            });
                        }
                    }
                }
            }
            return View(auditLogsCollection);
        }

        [HttpPost]
        public IActionResult AdministrativeAddUser(string username, string password, string mobile)
        {
            if (HttpContext.Session.GetInt32("IsAdmin") != 1) return Forbid();
            using (var conn = _context.CreateConnection()) {
                string query = "INSERT INTO T_USERS (F_USERNAME, F_PASSWORD, F_MOBILE_NUMBER) VALUES (@U, @P, @M)";
                using (var cmd = new SqlCommand(query, (SqlConnection)conn)) {
                    cmd.Parameters.AddWithValue("@U", username.Trim()); cmd.Parameters.AddWithValue("@P", password.Trim());
                    cmd.Parameters.AddWithValue("@M", mobile.Trim());
                    conn.Open(); cmd.ExecuteNonQuery();
                }
            }
            LogActivity("USER_MANAGEMENT", $"Created brand-new login profile mapping entry row: '{username.Trim()}' linked with mobile: {mobile}.");
            return RedirectToAction(nameof(Users));
        }

        [HttpPost]
        public IActionResult AdministrativeDeleteUser(int userId, string targetName)
        {
            if (HttpContext.Session.GetInt32("IsAdmin") != 1) return Forbid();
            using (var conn = _context.CreateConnection()) {
                string query = "DELETE FROM T_USERS WHERE F_USER_ID = @Id AND F_IS_ADMIN = 0";
                using (var cmd = new SqlCommand(query, (SqlConnection)conn)) {
                    cmd.Parameters.AddWithValue("@Id", userId);
                    conn.Open(); cmd.ExecuteNonQuery();
                }
            }
            LogActivity("USER_MANAGEMENT", $"Pruned user account profile registry rows completely: '{targetName}'.");
            return RedirectToAction(nameof(Users));
        }

        [HttpPost]
        public IActionResult AdministrativeChangeUserPassword(int userId, string targetName, string nextPassword)
        {
            if (HttpContext.Session.GetInt32("IsAdmin") != 1) return Forbid();
            using (var conn = _context.CreateConnection()) {
                string query = "UPDATE T_USERS SET F_PASSWORD = @P WHERE F_USER_ID = @Id";
                using (var cmd = new SqlCommand(query, (SqlConnection)conn)) {
                    cmd.Parameters.AddWithValue("@P", nextPassword.Trim()); cmd.Parameters.AddWithValue("@Id", userId);
                    conn.Open(); cmd.ExecuteNonQuery();
                }
            }
            LogActivity("PASSWORD_CHANGE", $"Forced password credential modification overwrite for account user: '{targetName}'.");
            return RedirectToAction(nameof(Users));
        }

        [HttpPost] public IActionResult AddProduct(Product m) { using (var c = _context.CreateConnection()) { string q = "INSERT INTO T_PRODUCTS (F_PROD_NAME,F_BRAND,F_QTY,F_PRICE,F_PROD_RATING) VALUES (@N,@B,@Q,@P,@R)"; using (var cmd = new SqlCommand(q,(SqlConnection)c)) { cmd.Parameters.AddWithValue("@N",m.ProductName); cmd.Parameters.AddWithValue("@B",m.Brand); cmd.Parameters.AddWithValue("@Q",m.Quantity); cmd.Parameters.AddWithValue("@P",m.Price); cmd.Parameters.AddWithValue("@R",m.ProductRating); c.Open(); cmd.ExecuteNonQuery(); } } LogActivity("ADD_ROW", $"Inserted brand-new inventory data row item: '{m.ProductName}' priced at Rs. {m.Price}."); return RedirectToAction(nameof(Index)); }
        [HttpPost] public IActionResult EditProduct(Product m) { using (var c = _context.CreateConnection()) { string q = "UPDATE T_PRODUCTS SET F_PROD_NAME=@N,F_BRAND=@B,F_QTY=@Q,F_PRICE=@P,F_PROD_RATING=@R WHERE F_PRODUCT_ID=@I"; using (var cmd = new SqlCommand(q,(SqlConnection)c)) { cmd.Parameters.AddWithValue("@I",m.ProductId); cmd.Parameters.AddWithValue("@N",m.ProductName); cmd.Parameters.AddWithValue("@B",m.Brand); cmd.Parameters.AddWithValue("@Q",m.Quantity); cmd.Parameters.AddWithValue("@P",m.Price); cmd.Parameters.AddWithValue("@R",m.ProductRating); c.Open(); cmd.ExecuteNonQuery(); } } LogActivity("EDIT", $"Updated inventory data specification parameters for component: '{m.ProductName}'."); return RedirectToAction(nameof(Index)); }
        [HttpPost] public IActionResult DeleteProduct(int id) { string namePlaceholder = $"ID {id}"; using (var c = _context.CreateConnection()) { c.Open(); using (var getNameCmd = new SqlCommand("SELECT F_PROD_NAME FROM T_PRODUCTS WHERE F_PRODUCT_ID=@Id", (SqlConnection)c)) { getNameCmd.Parameters.AddWithValue("@Id", id); namePlaceholder = getNameCmd.ExecuteScalar()?.ToString() ?? namePlaceholder; } using (var cmd = new SqlCommand("DELETE FROM T_PRODUCTS WHERE F_PRODUCT_ID=@I",(SqlConnection)c)) { cmd.Parameters.AddWithValue("@I",id); cmd.ExecuteNonQuery(); } } LogActivity("DELETE", $"Removed item row permanently from product catalog: '{namePlaceholder}'."); return RedirectToAction(nameof(Index)); }

        private void BuildExcelHeaderSchema(ExcelWorksheet sheet) { sheet.Cells[1, 1].Value = "Product Name"; sheet.Cells[1, 2].Value = "Brand"; sheet.Cells[1, 3].Value = "Quantity"; sheet.Cells[1, 4].Value = "Price"; sheet.Cells[1, 5].Value = "Description"; sheet.Cells[1, 6].Value = "Rating"; }
        private List<Product> FetchFilteredProducts(string brand, double? minP, double? maxP, double? minR, string sort) { List<Product> list = new List<Product>(); using (var connection = _context.CreateConnection()) { string query = "SELECT F_PRODUCT_ID, F_PROD_NAME, F_BRAND, F_QTY, F_PRICE, F_PROD_DESC, F_PROD_RATING FROM T_PRODUCTS WHERE 1=1"; if (!string.IsNullOrEmpty(brand)) query += " AND F_BRAND LIKE @B"; if (minP.HasValue) query += " AND F_PRICE >= @MinP"; if (maxP.HasValue) query += " AND F_PRICE <= @MaxP"; if (minR.HasValue) query += " AND F_PROD_RATING >= @MinR"; using (var cmd = new SqlCommand(query, (SqlConnection)connection)) { cmd.Parameters.AddWithValue("@B", !string.IsNullOrEmpty(brand) ? "%" + brand + "%" : DBNull.Value); cmd.Parameters.AddWithValue("@MinP", minP ?? (object)DBNull.Value); cmd.Parameters.AddWithValue("@MaxP", maxP ?? (object)DBNull.Value); cmd.Parameters.AddWithValue("@MinR", minR ?? (object)DBNull.Value); connection.Open(); using (var reader = cmd.ExecuteReader()) { while (reader.Read()) list.Add(new Product { ProductId = Convert.ToInt32(reader["F_PRODUCT_ID"]), ProductName = reader["F_PROD_NAME"].ToString() ?? "", Brand = reader["F_BRAND"].ToString() ?? "", Quantity = reader["F_QTY"].ToString() ?? "", Price = Convert.ToDouble(reader["F_PRICE"]), ProductDescription = reader["F_PROD_DESC"]?.ToString(), ProductRating = Convert.ToDouble(reader["F_PROD_RATING"]) }); } } } return list; }
    }
}