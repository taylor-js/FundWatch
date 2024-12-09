// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
#nullable disable

using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace FundWatch.Areas.Identity.Pages.Account
{
    public class LogoutModel : PageModel
    {
        private readonly SignInManager<IdentityUser> _signInManager;
        private readonly ILogger<LogoutModel> _logger;

        public LogoutModel(SignInManager<IdentityUser> signInManager, ILogger<LogoutModel> logger)
        {
            _signInManager = signInManager;
            _logger = logger;
        }

        public async Task<IActionResult> OnPost(string returnUrl = null)
        {
            _logger.LogInformation("Logout initiated.");
            try
            {
                await _signInManager.SignOutAsync();
                _logger.LogInformation("User successfully logged out.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during logout.");
                // You can also return an error view or handle the exception in another way
                return RedirectToPage("/Identity/Account/Logout");
            }
            if (returnUrl != null)
            {
                return LocalRedirect(returnUrl);
            }
            return RedirectToPage("/Index");
        }
    }
}
