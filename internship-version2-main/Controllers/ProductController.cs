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

            List<Product> products = FetchFilteredProducts(brandFilter, minPrice, maxPrice, minRating, sortBy);
            return View(products);
        }

        // =========================================================================
        // 🖥️ SEPARATE USERS MANAGEMENT PANEL PAGE (ADMIN CRUD + PASSWORD MANAGE)
        // =========================================================================
        public IActionResult Users()
        {
            if (HttpContext.Session.GetInt32("IsAdmin") != 1) return RedirectToAction(nameof(Index));

            List<Dictionary<string, object>> userProfiles = new List<Dictionary<string, object>>();
            using (var connection = _context.CreateConnection())
            {
                string query = "SELECT F_USER_ID, F_USERNAME, F_PASSWORD, F_MOBILE_NUMBER, F_IS_ADMIN, F_CAN_ADD_ROW, F_CAN_DOWNLOAD, F_CAN_IMPORT, F_CAN_EXPORT, F_CAN_COMPARE, F_CAN_EMAIL FROM T_USERS";
                using (var cmd = new SqlCommand(query, (SqlConnection)connection))
                {
                    connection.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            userProfiles.Add(new Dictionary<string, object> {
                                { "Id", reader["F_USER_ID"] },
                                { "Name", reader["F_USERNAME"].ToString() ?? "" },
                                { "Password", reader["F_PASSWORD"].ToString() ?? "" },
                                { "Mobile", reader["F_MOBILE_NUMBER"].ToString() ?? "" },
                                { "IsAdmin", Convert.ToInt32(reader["F_IS_ADMIN"]) },
                                { "AddRow", Convert.ToInt32(reader["F_CAN_ADD_ROW"]) },
                                { "Download", Convert.ToInt32(reader["F_CAN_DOWNLOAD"]) },
                                { "Import", Convert.ToInt32(reader["F_CAN_IMPORT"]) },
                                { "Export", Convert.ToInt32(reader["F_CAN_EXPORT"]) },
                                { "Compare", Convert.ToInt32(reader["F_CAN_COMPARE"]) },
                                { "Email", Convert.ToInt32(reader["F_CAN_EMAIL"]) }
                            });
                        }
                    }
                }
            }
            return View(userProfiles);
        }

        [HttpPost]
        public IActionResult AdministrativeAddUser(string username, string password, string mobile)
        {
            if (HttpContext.Session.GetInt32("IsAdmin") != 1) return Forbid();

            using (var conn = _context.CreateConnection()) {
                string query = "INSERT INTO T_USERS (F_USERNAME, F_PASSWORD, F_MOBILE_NUMBER) VALUES (@U, @P, @M)";
                using (var cmd = new SqlCommand(query, (SqlConnection)conn)) {
                    cmd.Parameters.AddWithValue("@U", username.Trim());
                    cmd.Parameters.AddWithValue("@P", password.Trim());
                    cmd.Parameters.AddWithValue("@M", mobile.Trim());
                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            TempData["SuccessMessage"] = "👤 New user account profile successfully registered!";
            return RedirectToAction(nameof(Users));
        }

        [HttpPost]
        public IActionResult AdministrativeDeleteUser(int userId)
        {
            if (HttpContext.Session.GetInt32("IsAdmin") != 1) return Forbid();

            using (var conn = _context.CreateConnection()) {
                string query = "DELETE FROM T_USERS WHERE F_USER_ID = @Id AND F_IS_ADMIN = 0";
                using (var cmd = new SqlCommand(query, (SqlConnection)conn)) {
                    cmd.Parameters.AddWithValue("@Id", userId);
                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            TempData["SuccessMessage"] = "🗑️ Account mapping pruned successfully from active directories.";
            return RedirectToAction(nameof(Users));
        }

        [HttpPost]
        public IActionResult AdministrativeChangeUserPassword(int userId, string nextPassword)
        {
            if (HttpContext.Session.GetInt32("IsAdmin") != 1) return Forbid();

            using (var conn = _context.CreateConnection()) {
                string query = "UPDATE T_USERS SET F_PASSWORD = @P WHERE F_USER_ID = @Id";
                using (var cmd = new SqlCommand(query, (SqlConnection)conn)) {
                    cmd.Parameters.AddWithValue("@P", nextPassword.Trim());
                    cmd.Parameters.AddWithValue("@Id", userId);
                    conn.Open();
                    cmd.ExecuteNonQuery();
                }
            }
            TempData["SuccessMessage"] = "🔑 User password security metrics overwritten successfully!";
            return RedirectToAction(nameof(Users));
        }

        [HttpPost]
        public IActionResult SaveUserConfig(int userId, int addRow, int download, int import, int export, int compare, int email)
        {
            if (HttpContext.Session.GetInt32("IsAdmin") != 1) return Forbid();
            using (var connection = _context.CreateConnection()) {
                string query = @"UPDATE T_USERS SET F_CAN_ADD_ROW=@Add, F_CAN_DOWNLOAD=@Dl, F_CAN_IMPORT=@Imp, F_CAN_EXPORT=@Exp, F_CAN_COMPARE=@Comp, F_CAN_EMAIL=@Em WHERE F_USER_ID=@Id";
                using (var cmd = new SqlCommand(query, (SqlConnection)connection)) {
                    cmd.Parameters.AddWithValue("@Id", userId); cmd.Parameters.AddWithValue("@Add", addRow);
                    cmd.Parameters.AddWithValue("@Dl", download); cmd.Parameters.AddWithValue("@Imp", import);
                    cmd.Parameters.AddWithValue("@Exp", export); cmd.Parameters.AddWithValue("@Comp", compare);
                    cmd.Parameters.AddWithValue("@Em", email);
                    connection.Open(); cmd.ExecuteNonQuery();
                }
            }
            TempData["SuccessMessage"] = "🛡️ Permission controls deployed live successfully!";
            return RedirectToAction(nameof(Users));
        }

        // =========================================================
        // CORE INVENTORY METHODS RE-PIPELINED
        // =========================================================
        [HttpPost]
        public IActionResult AddProduct(Product model) {
            using (var connection = _context.CreateConnection()) {
                string query = "INSERT INTO T_PRODUCTS (F_PROD_NAME, F_BRAND, F_QTY, F_PRICE, F_PROD_RATING) VALUES (@Name, @Brand, @Qty, @Price, @Rating)";
                using (var command = new SqlCommand(query, (SqlConnection)connection)) {
                    command.Parameters.AddWithValue("@Name", model.ProductName); command.Parameters.AddWithValue("@Brand", model.Brand);
                    command.Parameters.AddWithValue("@Qty", model.Quantity); command.Parameters.AddWithValue("@Price", model.Price);
                    command.Parameters.AddWithValue("@Rating", model.ProductRating); connection.Open(); command.ExecuteNonQuery();
                }
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public IActionResult EditProduct(Product model) {
            using (var connection = _context.CreateConnection()) {
                string query = "UPDATE T_PRODUCTS SET F_PROD_NAME=@Name, F_BRAND=@Brand, F_QTY=@Qty, F_PRICE=@Price, F_PROD_RATING=@Rating WHERE F_PRODUCT_ID=@Id";
                using (var command = new SqlCommand(query, (SqlConnection)connection)) {
                    command.Parameters.AddWithValue("@Id", model.ProductId); command.Parameters.AddWithValue("@Name", model.ProductName);
                    command.Parameters.AddWithValue("@Brand", model.Brand); command.Parameters.AddWithValue("@Qty", model.Quantity);
                    command.Parameters.AddWithValue("@Price", model.Price); command.Parameters.AddWithValue("@Rating", model.ProductRating);
                    connection.Open(); command.ExecuteNonQuery();
                }
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public IActionResult DeleteProduct(int id) {
            using (var connection = _context.CreateConnection()) {
                string query = "DELETE FROM T_PRODUCTS WHERE F_PRODUCT_ID = @Id";
                using (var command = new SqlCommand(query, (SqlConnection)connection)) {
                    command.Parameters.AddWithValue("@Id", id); connection.Open(); command.ExecuteNonQuery();
                }
            }
            return RedirectToAction(nameof(Index));
        }

        public IActionResult DownloadTemplate() {
            OfficeOpenXml.ExcelPackage.License.SetNonCommercialPersonal("ProductHub");
            using (var package = new ExcelPackage()) {
                var worksheet = package.Workbook.Worksheets.Add("Template"); BuildExcelHeaderSchema(worksheet);
                return File(package.GetAsByteArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "ProductTemplate.xlsx");
            }
        }

        public IActionResult ExportData(string brandFilter, double? minPrice, double? maxPrice, double? minRating) {
            OfficeOpenXml.ExcelPackage.License.SetNonCommercialPersonal("ProductHub");
            List<Product> products = FetchFilteredProducts(brandFilter, minPrice, maxPrice, minRating, "");
            using (var package = new ExcelPackage()) {
                var worksheet = package.Workbook.Worksheets.Add("Records"); BuildExcelHeaderSchema(worksheet);
                int r = 2;
                foreach (var p in products) {
                    worksheet.Cells[r, 1].Value = p.ProductName; worksheet.Cells[r, 2].Value = p.Brand;
                    worksheet.Cells[r, 3].Value = p.Quantity; worksheet.Cells[r, 4].Value = p.Price;
                    worksheet.Cells[r, 6].Value = p.ProductRating; r++;
                }
                return File(package.GetAsByteArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "ProductHub_Export.xlsx");
            }
        }

        [HttpPost]
        public IActionResult ImportData(IFormFile alexaExcelFile) {
            OfficeOpenXml.ExcelPackage.License.SetNonCommercialPersonal("ProductHub");
            using (var stream = new MemoryStream()) {
                alexaExcelFile.CopyTo(stream);
                using (var package = new ExcelPackage(stream)) {
                    var ws = package.Workbook.Worksheets.FirstOrDefault();
                    if (ws != null && ws.Dimension != null) {
                        using (var conn = _context.CreateConnection()) {
                            conn.Open();
                            for (int row = 2; row <= ws.Dimension.End.Row; row++) {
                                string name = ws.Cells[row, 1].Value?.ToString() ?? ""; if (string.IsNullOrEmpty(name)) continue;
                                string q = "INSERT INTO T_PRODUCTS (F_PROD_NAME, F_BRAND, F_QTY, F_PRICE, F_PROD_RATING) VALUES (@N, @B, @Q, @P, @R)";
                                using (var cmd = new SqlCommand(q, (SqlConnection)conn)) {
                                    cmd.Parameters.AddWithValue("@N", name); cmd.Parameters.AddWithValue("@B", ws.Cells[row, 2].Value?.ToString() ?? "");
                                    cmd.Parameters.AddWithValue("@Q", ws.Cells[row, 3].Value?.ToString() ?? ""); cmd.Parameters.AddWithValue("@P", Convert.ToDouble(ws.Cells[row, 4].Value ?? 0));
                                    cmd.Parameters.AddWithValue("@R", Convert.ToDouble(ws.Cells[row, 6].Value ?? 0)); cmd.ExecuteNonQuery();
                                }
                            }
                        }
                    }
                }
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public async Task<IActionResult> EmailZipData(List<int> ids, string recipientEmail) {
            string senderEmail = "tpass2829@gmail.com"; string senderPassword = "uozwlvrkykjzgjmj";
            byte[] excelBytes; List<Product> prods = new List<Product>();
            using (var connection = _context.CreateConnection()) {
                var pNames = string.Join(",", ids.Select((id, idx) => $"@Id{idx}"));
                string query = $"SELECT F_PROD_NAME, F_BRAND, F_QTY, F_PRICE, F_PROD_RATING FROM T_PRODUCTS WHERE F_PRODUCT_ID IN ({pNames})";
                using (var cmd = new SqlCommand(query, (SqlConnection)connection)) {
                    for (int i = 0; i < ids.Count; i++) cmd.Parameters.AddWithValue($"@Id{i}", ids[i]);
                    connection.Open(); using (var reader = cmd.ExecuteReader()) {
                        while (reader.Read()) prods.Add(new Product {
                            ProductName = reader["F_PROD_NAME"].ToString() ?? "", Brand = reader["F_BRAND"].ToString() ?? "",
                            Quantity = reader["F_QTY"].ToString() ?? "", Price = Convert.ToDouble(reader["F_PRICE"]),
                            ProductRating = Convert.ToDouble(reader["F_PROD_RATING"])
                        });
                    }
                }
            }
            using (var package = new ExcelPackage()) {
                var ws = package.Workbook.Worksheets.Add("Report"); BuildExcelHeaderSchema(ws);
                int r = 2; foreach (var p in prods) {
                    ws.Cells[r,1].Value = p.ProductName; ws.Cells[r,2].Value = p.Brand;
                    ws.Cells[r,3].Value = p.Quantity; ws.Cells[r,4].Value = p.Price; ws.Cells[r,6].Value = p.ProductRating; r++;
                }
                excelBytes = package.GetAsByteArray();
            }
            byte[] zipBytes; using (var ms = new MemoryStream()) {
                using (var arc = new ZipArchive(ms, ZipArchiveMode.Create, true)) {
                    var entry = arc.CreateEntry("Report.xlsx", System.IO.Compression.CompressionLevel.Optimal);
                    using (var es = entry.Open()) es.Write(excelBytes, 0, excelBytes.Length);
                }
                zipBytes = ms.ToArray();
            }
            using (MailMessage mail = new MailMessage()) {
                mail.From = new MailAddress(senderEmail); mail.To.Add(recipientEmail.Trim());
                mail.Subject = "📦 Inventory Report Portfolio Package"; mail.Body = "Attached Zip Sheet Archive.";
                mail.Attachments.Add(new Attachment(new MemoryStream(zipBytes), "Report.zip"));
                using (SmtpClient smtp = new SmtpClient("smtp.gmail.com", 587)) {
                    smtp.EnableSsl = true; smtp.UseDefaultCredentials = false;
                    smtp.Credentials = new NetworkCredential(senderEmail, senderPassword); await smtp.SendMailAsync(mail);
                }
            }
            return RedirectToAction(nameof(Index));
        }

        private void BuildExcelHeaderSchema(ExcelWorksheet sheet) {
            sheet.Cells[1, 1].Value = "Product Name"; sheet.Cells[1, 2].Value = "Brand";
            sheet.Cells[1, 3].Value = "Quantity"; sheet.Cells[1, 4].Value = "Price";
            sheet.Cells[1, 5].Value = "Description"; sheet.Cells[1, 6].Value = "Rating";
        }

        private List<Product> FetchFilteredProducts(string brand, double? minP, double? maxP, double? minR, string sort) {
            List<Product> list = new List<Product>();
            using (var connection = _context.CreateConnection()) {
                string query = "SELECT F_PRODUCT_ID, F_PROD_NAME, F_BRAND, F_QTY, F_PRICE, F_PROD_DESC, F_PROD_RATING FROM T_PRODUCTS WHERE 1=1";
                if (!string.IsNullOrEmpty(brand)) query += " AND F_BRAND LIKE @B";
                if (minP.HasValue) query += " AND F_PRICE >= @MinP";
                if (maxP.HasValue) query += " AND F_PRICE <= @MaxP";
                if (minR.HasValue) query += " AND F_PROD_RATING >= @MinR";
                using (var cmd = new SqlCommand(query, (SqlConnection)connection)) {
                    cmd.Parameters.AddWithValue("@B", !string.IsNullOrEmpty(brand) ? "%" + brand + "%" : DBNull.Value);
                    cmd.Parameters.AddWithValue("@MinP", minP ?? (object)DBNull.Value); cmd.Parameters.AddWithValue("@MaxP", maxP ?? (object)DBNull.Value);
                    cmd.Parameters.AddWithValue("@MinR", minR ?? (object)DBNull.Value); connection.Open();
                    using (var reader = cmd.ExecuteReader()) {
                        while (reader.Read()) list.Add(new Product {
                            ProductId = Convert.ToInt32(reader["F_PRODUCT_ID"]), ProductName = reader["F_PROD_NAME"].ToString() ?? "",
                            Brand = reader["F_BRAND"].ToString() ?? "", Quantity = reader["F_QTY"].ToString() ?? "",
                            Price = Convert.ToDouble(reader["F_PRICE"]), ProductDescription = reader["F_PROD_DESC"]?.ToString(),
                            ProductRating = Convert.ToDouble(reader["F_PROD_RATING"])
                        });
                    }
                }
            }
            return list;
        }
    }
}