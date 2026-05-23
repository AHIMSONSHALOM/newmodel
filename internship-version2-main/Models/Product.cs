using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProductHub_MVC.Models
{
    [Table("T_PRODUCTS")]
    public class Product
    {
        [Key]
        [Column("F_PRODUCT_ID")]
        public int ProductId { get; set; } 

        [Required(ErrorMessage = "Product name is required.")]
        [Column("F_PROD_NAME")]
        public string ProductName { get; set; } = string.Empty; 

        [Required(ErrorMessage = "Brand name is required.")]
        [Column("F_BRAND")]
        public string Brand { get; set; } = string.Empty; 

        [Required(ErrorMessage = "Quantity detail is required.")]
        [Column("F_QTY")]
        public string Quantity { get; set; } = string.Empty; 

        [Required(ErrorMessage = "Price is required.")]
        [Column("F_PRICE")]
        public double Price { get; set; } 

        [Column("F_PROD_DESC")]
        public string? ProductDescription { get; set; } 

        [Column("F_PROD_RATING")]
        public double ProductRating { get; set; } 
    }
}