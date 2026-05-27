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

        // =========================================================
        // 1. DATA GRID: FETCH, FILTER & SORT ENGINE (LOCKED DOWN)
        // =========================================================
        public IActionResult Index(string sortBy, string brandFilter, double? minPrice, double? maxPrice, double? minRating)
        {
            // ✅ STEP 5 SECURE CHECK: Redirect to security gatekeeper if user session footprint doesn't exist
            if (HttpContext.Session.GetString("UserSession") == null)
            {
                return RedirectToAction("Login", "Account");
            }

            List<Product> products = FetchFilteredProducts(brandFilter, minPrice, maxPrice, minRating, sortBy);

            ViewBag.CurrentSort = sortBy;
            ViewBag.CurrentBrand = brandFilter;
            ViewBag.MinPrice = minPrice;
            ViewBag.MaxPrice = maxPrice;
            ViewBag.MinRating = minRating;

            return View(products);
        }

        // =========================================================
        // 2. COMPARISON ENGINE (DYNAMIC MATRIX VIEW - LOCKED DOWN)
        // =========================================================
        public IActionResult Compare(List<int> ids)
        {
            // ✅ STEP 5 SECURE CHECK: Protect comparison profiles from direct URL manipulation entry
            if (HttpContext.Session.GetString("UserSession") == null)
            {
                return RedirectToAction("Login", "Account");
            }

            if (ids == null || ids.Count < 2 || ids.Count > 4)
            {
                TempData["ErrorMessage"] = "Please select between 2 and 4 products to compare.";
                return RedirectToAction(nameof(Index));
            }

            List<Product> comparisonCollection = new List<Product>();
            
            using (var connection = _context.CreateConnection())
            {
                var parameterNames = string.Join(",", ids.Select((id, index) => $"@Id{index}"));
                string query = $"SELECT F_PRODUCT_ID, F_PROD_NAME, F_BRAND, F_QTY, F_PRICE, F_PROD_DESC, F_PROD_RATING FROM T_PRODUCTS WHERE F_PRODUCT_ID IN ({parameterNames})";

                using (var cmd = new SqlCommand(query, (SqlConnection)connection))
                {
                    for (int i = 0; i < ids.Count; i++)
                    {
                        cmd.Parameters.AddWithValue($"@Id{i}", ids[i]);
                    }

                    connection.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            comparisonCollection.Add(new Product
                            {
                                ProductId = Convert.ToInt32(reader["F_PRODUCT_ID"]),
                                ProductName = reader["F_PROD_NAME"].ToString() ?? string.Empty,
                                Brand = reader["F_BRAND"].ToString() ?? string.Empty,
                                Quantity = reader["F_QTY"].ToString() ?? string.Empty,
                                Price = Convert.ToDouble(reader["F_PRICE"]),
                                ProductDescription = reader["F_PROD_DESC"]?.ToString() ?? "No specifications provided.",
                                ProductRating = Convert.ToDouble(reader["F_PROD_RATING"])
                            });
                        }
                    }
                }
            }

            return View(comparisonCollection);
        }

        // =========================================================
        // 3. CRUD OPERATIONS (ADD & DELETE - AUTHENTICATED)
        // =========================================================
        [HttpPost]
        public IActionResult AddProduct(Product model)
        {
            if (HttpContext.Session.GetString("UserSession") == null) 
            {
                return RedirectToAction("Login", "Account");
            }

            if (ModelState.IsValid)
            {
                using (var connection = _context.CreateConnection())
                {
                    string query = @"INSERT INTO T_PRODUCTS (F_PROD_NAME, F_BRAND, F_QTY, F_PRICE, F_PROD_DESC, F_PROD_RATING) 
                                     VALUES (@Name, @Brand, @Qty, @Price, @Desc, @Rating)";

                    using (var command = new SqlCommand(query, (SqlConnection)connection))
                    {
                        command.Parameters.AddWithValue("@Name", model.ProductName);
                        command.Parameters.AddWithValue("@Brand", model.Brand);
                        command.Parameters.AddWithValue("@Qty", model.Quantity);
                        command.Parameters.AddWithValue("@Price", model.Price);
                        command.Parameters.AddWithValue("@Desc", model.ProductDescription ?? (object)DBNull.Value);
                        command.Parameters.AddWithValue("@Rating", model.ProductRating);

                        connection.Open();
                        command.ExecuteNonQuery();
                    }
                }
                TempData["SuccessMessage"] = "✅ Product added successfully!";
                return RedirectToAction(nameof(Index));
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        public IActionResult DeleteProduct(int id)
        {
            if (HttpContext.Session.GetString("UserSession") == null) 
            {
                return RedirectToAction("Login", "Account");
            }

            using (var connection = _context.CreateConnection())
            {
                string query = "DELETE FROM T_PRODUCTS WHERE F_PRODUCT_ID = @Id";
                using (var command = new SqlCommand(query, (SqlConnection)connection))
                {
                    command.Parameters.AddWithValue("@Id", id);
                    connection.Open();
                    command.ExecuteNonQuery();
                }
            }
            TempData["SuccessMessage"] = "✅ Product deleted!";
            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // 4. EXCEL TEMPLATE & DATA EXPORT HANDLING
        // =========================================================
        public IActionResult DownloadTemplate()
        {
            if (HttpContext.Session.GetString("UserSession") == null) 
            {
                return RedirectToAction("Login", "Account");
            }

            OfficeOpenXml.ExcelPackage.License.SetNonCommercialPersonal("ProductHub");
            
            using (var package = new ExcelPackage())
            {
                var worksheet = package.Workbook.Worksheets.Add("Template");
                BuildExcelHeaderSchema(worksheet);

                worksheet.Cells[2, 1].Value = "Sample Product";
                worksheet.Cells[2, 2].Value = "Sample Brand";
                worksheet.Cells[2, 3].Value = "10 Units";
                worksheet.Cells[2, 4].Value = 1500.00;
                worksheet.Cells[2, 5].Value = "Sample Description.";
                worksheet.Cells[2, 6].Value = 4.5;

                var fileBytes = package.GetAsByteArray();
                return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "ProductTemplate.xlsx");
            }
        }

        public IActionResult ExportData(string brandFilter, double? minPrice, double? maxPrice, double? minRating)
        {
            if (HttpContext.Session.GetString("UserSession") == null) 
            {
                return RedirectToAction("Login", "Account");
            }

            OfficeOpenXml.ExcelPackage.License.SetNonCommercialPersonal("ProductHub");
            List<Product> targetedProducts = FetchFilteredProducts(brandFilter, minPrice, maxPrice, minRating, "");

            using (var package = new ExcelPackage())
            {
                var worksheet = package.Workbook.Worksheets.Add("Product Records");
                BuildExcelHeaderSchema(worksheet);

                int currentRow = 2;
                foreach (var prod in targetedProducts)
                {
                    worksheet.Cells[currentRow, 1].Value = prod.ProductName;
                    worksheet.Cells[currentRow, 2].Value = prod.Brand;
                    worksheet.Cells[currentRow, 3].Value = prod.Quantity;
                    worksheet.Cells[currentRow, 4].Value = prod.Price;
                    worksheet.Cells[currentRow, 5].Value = prod.ProductDescription;
                    worksheet.Cells[currentRow, 6].Value = prod.ProductRating;
                    currentRow++;
                }

                if (worksheet.Dimension != null) 
                    worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();

                var fileBytes = package.GetAsByteArray();
                return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "ProductHub_Export.xlsx");
            }
        }

        [HttpPost]
        public IActionResult ImportData(IFormFile alexaExcelFile)
        {
            if (HttpContext.Session.GetString("UserSession") == null) 
            {
                return RedirectToAction("Login", "Account");
            }
            if (alexaExcelFile == null || alexaExcelFile.Length == 0) 
                return BadRequest("No file selected.");

            OfficeOpenXml.ExcelPackage.License.SetNonCommercialPersonal("ProductHub");

            try
            {
                using (var stream = new MemoryStream())
                {
                    alexaExcelFile.CopyTo(stream);
                    using (var package = new ExcelPackage(stream))
                    {
                        var worksheet = package.Workbook.Worksheets.FirstOrDefault();
                        if (worksheet != null && worksheet.Dimension != null)
                        {
                            int totalRows = worksheet.Dimension.End.Row;
                            using (var connection = _context.CreateConnection())
                            {
                                connection.Open();
                                for (int row = 2; row <= totalRows; row++)
                                {
                                    string name = worksheet.Cells[row, 1].Value?.ToString() ?? string.Empty;
                                    string brand = worksheet.Cells[row, 2].Value?.ToString() ?? string.Empty;
                                    string qty = worksheet.Cells[row, 3].Value?.ToString() ?? string.Empty;
                                    double price = Convert.ToDouble(worksheet.Cells[row, 4].Value ?? 0);
                                    string desc = worksheet.Cells[row, 5].Value?.ToString() ?? string.Empty;
                                    double rating = Convert.ToDouble(worksheet.Cells[row, 6].Value ?? 0);

                                    if (string.IsNullOrEmpty(name)) continue;

                                    string query = @"INSERT INTO T_PRODUCTS (F_PROD_NAME, F_BRAND, F_QTY, F_PRICE, F_PROD_DESC, F_PROD_RATING) 
                                                     VALUES (@Name, @Brand, @Qty, @Price, @Desc, @Rating)";

                                    using (var command = new SqlCommand(query, (SqlConnection)connection))
                                    {
                                        command.Parameters.AddWithValue("@Name", name);
                                        command.Parameters.AddWithValue("@Brand", brand);
                                        command.Parameters.AddWithValue("@Qty", qty);
                                        command.Parameters.AddWithValue("@Price", price);
                                        command.Parameters.AddWithValue("@Desc", string.IsNullOrEmpty(desc) ? DBNull.Value : (object)desc);
                                        command.Parameters.AddWithValue("@Rating", rating);
                                        command.ExecuteNonQuery();
                                    }
                                }
                            }
                        }
                    }
                }
                TempData["SuccessMessage"] = "✅ Products imported successfully!";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"❌ Import error: {ex.Message}";
            }
            return RedirectToAction(nameof(Index));
        }

        // =====================================================================
        // 6. ✅ EXPORT BY CHECKBOX: EMAIL PIPELINE WITH TIMESTAMPED UNIQUE NAMES
        // =====================================================================
        [HttpPost]
        public async Task<IActionResult> EmailZipData(List<int> ids, string recipientEmail)
        {
            if (HttpContext.Session.GetString("UserSession") == null) 
            {
                return RedirectToAction("Login", "Account");
            }

            if (string.IsNullOrEmpty(recipientEmail))
            {
                TempData["ErrorMessage"] = "❌ Process Fault: Recipient email address configuration entry is required.";
                return RedirectToAction(nameof(Index));
            }

            if (ids == null || ids.Count == 0)
            {
                TempData["ErrorMessage"] = "❌ Boundary Error: No products selected for dispatch formatting.";
                return RedirectToAction(nameof(Index));
            }

            // Secure hidden backend account credentials
            string senderEmail = "tpass2829@gmail.com";
            string senderPassword = "uozwlvrkykjzgjmj";

            try
            {
                OfficeOpenXml.ExcelPackage.License.SetNonCommercialPersonal("ProductHub");
                byte[] excelFileBytes;

                // FETCHES ONLY EXPLICITLY CHECKED DATA ROWS OUT OF SQL SERVER
                List<Product> selectedProducts = new List<Product>();
                using (var connection = _context.CreateConnection())
                {
                    var parameterNames = string.Join(",", ids.Select((id, index) => $"@Id{index}"));
                    string query = $"SELECT F_PRODUCT_ID, F_PROD_NAME, F_BRAND, F_QTY, F_PRICE, F_PROD_DESC, F_PROD_RATING FROM T_PRODUCTS WHERE F_PRODUCT_ID IN ({parameterNames})";

                    using (var cmd = new SqlCommand(query, (SqlConnection)connection))
                    {
                        for (int i = 0; i < ids.Count; i++)
                        {
                            cmd.Parameters.AddWithValue($"@Id{i}", ids[i]);
                        }

                        connection.Open();
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                selectedProducts.Add(new Product
                                {
                                    ProductId = Convert.ToInt32(reader["F_PRODUCT_ID"]),
                                    ProductName = reader["F_PROD_NAME"].ToString() ?? string.Empty,
                                    Brand = reader["F_BRAND"].ToString() ?? string.Empty,
                                    Quantity = reader["F_QTY"].ToString() ?? string.Empty,
                                    Price = Convert.ToDouble(reader["F_PRICE"]),
                                    ProductDescription = reader["F_PROD_DESC"]?.ToString(),
                                    ProductRating = Convert.ToDouble(reader["F_PROD_RATING"])
                                });
                            }
                        }
                    }
                }

                using (var package = new ExcelPackage())
                {
                    var worksheet = package.Workbook.Worksheets.Add("Selected Items Report");
                    BuildExcelHeaderSchema(worksheet);
                    int r = 2;
                    foreach (var p in selectedProducts)
                    {
                        worksheet.Cells[r, 1].Value = p.ProductName;
                        worksheet.Cells[r, 2].Value = p.Brand;
                        worksheet.Cells[r, 3].Value = p.Quantity;
                        worksheet.Cells[r, 4].Value = p.Price;
                        worksheet.Cells[r, 5].Value = p.ProductDescription;
                        worksheet.Cells[r, 6].Value = p.ProductRating;
                        r++;
                    }
                    
                    if (worksheet.Dimension != null)
                        worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
                        
                    excelFileBytes = package.GetAsByteArray();
                }

                // ✅ GENERATES DYNAMIC FILE LABELS USING DATES & TIMES TO ELIMINATE CACHE CLASHES
                string uniqueTimestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string uniqueExcelName = $"InventoryReport_{uniqueTimestamp}.xlsx";
                string uniqueZipName = $"ProductPackage_{uniqueTimestamp}.zip";

                // Create compressed ZIP package cleanly in memory
                byte[] finalZipBytes;
                using (var zipStream = new MemoryStream())
                {
                    using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, true))
                    {
                        var zipEntry = archive.CreateEntry(uniqueExcelName, System.IO.Compression.CompressionLevel.Optimal);
                        using (var entryStream = zipEntry.Open())
                        {
                            entryStream.Write(excelFileBytes, 0, excelFileBytes.Length);
                        }
                    }
                    finalZipBytes = zipStream.ToArray();
                }

                using (MailMessage mail = new MailMessage())
                {
                    mail.From = new MailAddress(senderEmail, "ProductHub Admin Console");
                    mail.To.Add(recipientEmail.Trim()); 
                    mail.Subject = $"📦 Inventory Report Update (ID: {uniqueTimestamp})";
                    mail.Body = $"Dear User,\n\nPlease find attached your custom product data spreadsheet containing your selected dataset items compressed inside a secure ZIP log archive wrapper.\n\n" +
                                $"Total Items Transmitted: {selectedProducts.Count}\n" +
                                $"File Generated: {uniqueExcelName}\n" +
                                $"Sent Dynamically Via Login Account Profile: {senderEmail}\n\n" +
                                $"Best regards,\nProductHub Notification System Engine Layer";
                    mail.IsBodyHtml = false;

                    Attachment attachment = new Attachment(new MemoryStream(finalZipBytes), uniqueZipName);
                    mail.Attachments.Add(attachment);

                    using (SmtpClient smtp = new SmtpClient("smtp.gmail.com", 587))
                    {
                        smtp.EnableSsl = true;
                        smtp.UseDefaultCredentials = false;
                        smtp.Credentials = new NetworkCredential(senderEmail, senderPassword);
                        
                        await smtp.SendMailAsync(mail);
                    }
                }

                TempData["SuccessMessage"] = $"Base Query Complete: Data report packet containing {selectedProducts.Count} selected items has been securely dispatched to mailbox: {recipientEmail}!";
            }
            catch (Exception ex)
            {
                TempData["ErrorMessage"] = $"❌ Dynamic Transit Exception: {ex.Message}";
            }

            return RedirectToAction(nameof(Index));
        }

        // =========================================================
        // PRIVATE HELPERS
        // =========================================================
        private void BuildExcelHeaderSchema(ExcelWorksheet sheet)
        {
            sheet.Cells[1, 1].Value = "Product Name";
            sheet.Cells[1, 2].Value = "Brand";
            sheet.Cells[1, 3].Value = "Quantity";
            sheet.Cells[1, 4].Value = "Price";
            sheet.Cells[1, 5].Value = "Description";
            sheet.Cells[1, 6].Value = "Rating";
        }

        private List<Product> FetchFilteredProducts(string brand, double? minP, double? maxP, double? minR, string sortField)
        {
            List<Product> productCollection = new List<Product>();
            using (var connection = _context.CreateConnection())
            {
                string query = "SELECT F_PRODUCT_ID, F_PROD_NAME, F_BRAND, F_QTY, F_PRICE, F_PROD_DESC, F_PROD_RATING FROM T_PRODUCTS WHERE 1=1";
                
                if (!string.IsNullOrEmpty(brand)) query += " AND F_BRAND LIKE @B";
                if (minP.HasValue) query += " AND F_PRICE >= @MinP";
                if (maxP.HasValue) query += " AND F_PRICE <= @MaxP";
                if (minR.HasValue) query += " AND F_PROD_RATING >= @MinR";

                switch (sortField)
                {
                    case "name_desc": query += " ORDER BY F_PROD_NAME DESC"; break;
                    case "name_asc": query += " ORDER BY F_PROD_NAME ASC"; break;
                    case "price_desc": query += " ORDER BY F_PRICE DESC"; break;
                    case "price_asc": query += " ORDER BY F_PRICE ASC"; break;
                    default: query += " ORDER BY F_PRODUCT_ID ASC"; break;
                }

                using (var cmd = new SqlCommand(query, (SqlConnection)connection))
                {
                    cmd.Parameters.AddWithValue("@B", !string.IsNullOrEmpty(brand) ? (object)("%" + brand + "%") : DBNull.Value);
                    cmd.Parameters.AddWithValue("@MinP", minP.HasValue ? (object)minP.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@MaxP", maxP.HasValue ? (object)maxP.Value : DBNull.Value);
                    cmd.Parameters.AddWithValue("@MinR", minR.HasValue ? (object)minR.Value : DBNull.Value);

                    connection.Open();
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            productCollection.Add(new Product
                            {
                                ProductId = Convert.ToInt32(reader["F_PRODUCT_ID"]),
                                ProductName = reader["F_PROD_NAME"].ToString() ?? string.Empty,
                                Brand = reader["F_BRAND"].ToString() ?? string.Empty,
                                Quantity = reader["F_QTY"].ToString() ?? string.Empty,
                                Price = Convert.ToDouble(reader["F_PRICE"]),
                                ProductDescription = reader["F_PROD_DESC"]?.ToString(),
                                ProductRating = Convert.ToDouble(reader["F_PROD_RATING"])
                            });
                        }
                    }
                }
            }
            return productCollection;
        }
    }
}