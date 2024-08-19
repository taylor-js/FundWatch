using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace FundWatch.Areas.Identity.Data
{
    public class ApplicationUser : IdentityUser
    {
        [Required]
        [PersonalData]
        [Column(TypeName = "varchar(100)")]
        public string FirstName { get; set; }

        [Required]
        [PersonalData]
        [Column(TypeName = "varchar(100)")]
        public string LastName { get; set; }
    }
}
