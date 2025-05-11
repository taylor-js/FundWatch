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
        public DateTime DatePurchased { get; set; }

        [Required]
        public int NumberOfSharesPurchased { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal CurrentPrice { get; set; }

        public DateTime? DateSold { get; set; }

        public int? NumberOfSharesSold { get; set; }

        [NotMapped]
        public decimal ValueChange
        {
            get
            {
                var sharesOwned = NumberOfSharesPurchased - (NumberOfSharesSold ?? 0);
                return (CurrentPrice - PurchasePrice) * sharesOwned;
            }
        }

        [NotMapped]
        public decimal TotalValue
        {
            get
            {
                var sharesOwned = NumberOfSharesPurchased - (NumberOfSharesSold ?? 0);
                return CurrentPrice * sharesOwned;
            }
        }

        [NotMapped]
        public decimal PerformancePercentage
        {
            get
            {
                var sharesOwned = NumberOfSharesPurchased - (NumberOfSharesSold ?? 0);
                var costBasis = PurchasePrice * sharesOwned;
                var totalValue = CurrentPrice * sharesOwned;
                return costBasis != 0 ? ((totalValue - costBasis) / costBasis) : 0;
            }
        }

        // Additional properties for charting and analytics
        [NotMapped]
        public string Sector { get; set; }

        [NotMapped]
        public string Industry { get; set; }
    }
}
