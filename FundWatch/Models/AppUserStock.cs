using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FundWatch.Models
{
    public class AppUserStock
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; }

        //[ForeignKey("UserId")]
        //[BindNever]
        //public IdentityUser User { get; set; }

        [Required]
        [MaxLength(10)]
        public string StockSymbol { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal PurchasePrice { get; set; }

        [Required]
        public DateTime DatePurchased { get; set; } = DateTime.Today;

        [Required]
        public int NumberOfSharesPurchased { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal CurrentPrice { get; set; }

        public DateTime? DateSold { get; set; } = DateTime.Today.AddDays(1);

        public int? NumberOfSharesSold { get; set; }

        [NotMapped]
        public decimal ValueChange => (CurrentPrice - PurchasePrice) * NumberOfSharesPurchased;

        [NotMapped]
        public decimal PerformancePercentage => ((CurrentPrice - PurchasePrice) / PurchasePrice) * 100;

        [NotMapped]
        public decimal TotalValue => (NumberOfSharesPurchased - (NumberOfSharesSold ?? 0)) * CurrentPrice;

    }
}
