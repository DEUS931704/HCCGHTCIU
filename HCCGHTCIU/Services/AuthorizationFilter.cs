using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using HCCGHTCIU.Models;

namespace HCCGHTCIU.Services
{
    public class AdminAuthorizationFilter : IAuthorizationFilter
    {
        public void OnAuthorization(AuthorizationFilterContext context)
        {
            var session = context.HttpContext.Session;
            var userRole = session.GetString("UserRole");

            if (string.IsNullOrEmpty(userRole) ||
                Enum.Parse<UserRole>(userRole) != UserRole.Admin)
            {
                context.Result = new RedirectToActionResult("Login", "Home", null);
            }
        }
    }
}