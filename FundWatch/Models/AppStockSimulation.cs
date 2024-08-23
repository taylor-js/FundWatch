using Microsoft.AspNetCore.Identity;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FundWatch.Models
{
    public class AppStockSimulation
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
        public DateTime SimulatedPurchaseDate { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal SimulatedPurchasePrice { get; set; }

        [Required]
        public int SimulatedNumberOfShares { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal SimulatedCurrentPrice { get; set; }

        [NotMapped]
        public decimal SimulatedValueChange => (SimulatedCurrentPrice - SimulatedPurchasePrice) * SimulatedNumberOfShares;
    }
}
