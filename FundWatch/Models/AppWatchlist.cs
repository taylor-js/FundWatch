using Microsoft.AspNetCore.Identity;
using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FundWatch.Models
{
    public class AppWatchlist
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
        public DateTime AddedDate { get; set; }
    }
}
