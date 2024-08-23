using Microsoft.AspNetCore.Identity;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FundWatch.Models
{
    public class AppStockTransaction
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; }

        //[ForeignKey("UserId")]
        //public IdentityUser User { get; set; }

        [Required]
        [MaxLength(10)]
        public string StockSymbol { get; set; }

        [Required]
        [MaxLength(4)]
        public string TransactionType { get; set; } // "Buy" or "Sell"

        [Required]
        public DateTime TransactionDate { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal PricePerShare { get; set; }

        [Required]
        public int NumberOfShares { get; set; }
    }
}
